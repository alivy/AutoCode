using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCode.Analyzers.CodeFixes
{
    /// <summary>
    /// 右键/Ctrl+. 代码重构：在标记了 [AutoIntercept] 或 [CustomIntercept] 的方法上，
    /// 提供"生成拦截 Handler"选项，一键生成带 OnBefore/OnAfter/OnException 默认实现的 Handler 类。
    /// 
    /// 开发者无需手动记忆 Args 类型名和返回值类型，重构工具自动推断。
    /// </summary>
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(GenerateHandlerRefactoring)), Shared]
    public class GenerateHandlerRefactoring : CodeRefactoringProvider
    {
        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            // 找到光标所在的方法
            var node = root.FindNode(context.Span);
            var methodDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();

            // ═══ 类级别：光标不在 public 方法内时，在类上提供“批量生成 Handler” ═══
            if (methodDecl == null || !methodDecl.Modifiers.Any(SyntaxKind.PublicKeyword))
            {
                var classDecl = node.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (classDecl != null)
                {
                    await RegisterClassLevelHandlerRefactoringAsync(context, root, classDecl);
                }
                return;
            }

            // 检查方法是否已有 [AutoIntercept] 或 [CustomIntercept] 特性
            var hasInterceptAttr = methodDecl.AttributeLists
                .SelectMany(a => a.Attributes)
                .Any(a =>
                {
                    var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                    return name is "AutoIntercept" or "AutoInterceptAttribute"
                        or "CustomIntercept" or "CustomInterceptAttribute";
                });

            // 获取方法信息
            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel == null) return;

            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl, context.CancellationToken);
            if (methodSymbol == null) return;

            var className = methodSymbol.ContainingType?.Name ?? "MyService";
            var methodName = methodSymbol.Name;
            var argsName = $"{methodName}Args";
            var returnType = GetReturnTypeDisplay(methodSymbol);
            var ns = methodSymbol.ContainingNamespace?.ToDisplayString() ?? "";

            // 构建参数描述（用于注释）
            var paramDesc = string.Join(", ", methodSymbol.Parameters.Select(p =>
                $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"));

            var handlerName = $"{methodName}Handler";

            if (hasInterceptAttr)
            {
                // ═══ 已有标记：直接生成 Handler ═══
                context.RegisterRefactoring(
                    CodeAction.Create(
                        title: $"🚀 生成拦截 Handler: {handlerName}",
                        createChangedDocument: ct => GenerateHandlerAsync(
                            context.Document, root, methodDecl, ns, className,
                            methodName, argsName, returnType, paramDesc, handlerName, ct),
                        equivalenceKey: $"GenerateHandler_{methodName}"));
            }
            else
            {
                // ═══ 无标记：一步到位（加 [AutoIntercept] + [CustomIntercept] + 生成 Handler）═══
                context.RegisterRefactoring(
                    CodeAction.Create(
                        title: $"🔌 添加拦截 + 生成 Handler: {handlerName}",
                        createChangedDocument: ct => GenerateHandlerAsync(
                            context.Document, root, methodDecl, ns, className,
                            methodName, argsName, returnType, paramDesc, handlerName, ct),
                        equivalenceKey: $"AddInterceptAndGenerate_{methodName}"));
            }

            // 额外选项：生成异步 Handler
            if (methodSymbol.IsAsync || returnType.StartsWith("Task"))
            {
                var asyncReturnType = ExtractAsyncInnerType(methodSymbol);
                context.RegisterRefactoring(
                    CodeAction.Create(
                        title: $"⚡ 生成异步 Handler: {handlerName}（IAsyncMethodHandler）",
                        createChangedDocument: ct => GenerateAsyncHandlerAsync(
                            context.Document, root, methodDecl, ns, className,
                            methodName, argsName, asyncReturnType, paramDesc, handlerName, ct),
                        equivalenceKey: $"GenerateAsyncHandler_{methodName}"));
            }
        }

        private static Task<Document> GenerateHandlerAsync(
            Document document, SyntaxNode root, MethodDeclarationSyntax methodDecl,
            string ns, string className, string methodName, string argsName,
            string returnType, string paramDesc, string handlerName, CancellationToken ct)
        {
            var code = $@"using AutoCode.Model;
using System;

namespace {ns}
{{
    /// <summary>
    /// {className}.{methodName} 方法的拦截处理器。
    /// 参数: ({paramDesc})
    /// 返回值: {returnType}
    /// 
    /// 由 AutoCode 自动生成骨架，Args 类型 '{argsName}' 由编译时生成器自动产出。
    /// </summary>
    public class {handlerName} : MethodHandlerBase<{argsName}, {returnType}>
    {{
        /// <summary>
        /// 方法执行前调用。
        /// 可访问强类型参数: args.XXX
        /// 设置 ctx.ShortCircuit = true + ctx.Result 可跳过方法执行。
        /// </summary>
        public override void OnBefore({argsName} args, MethodContext ctx)
        {{
            // TODO: 前置逻辑（参数校验、权限检查、并发计数、缓存查询）
            // 示例: Console.WriteLine($""[{{ctx.MethodName}}] 开始执行"");
        }}

        /// <summary>
        /// 方法成功执行后调用。
        /// result 是强类型返回值，可直接做数据处理。
        /// </summary>
        public override void OnAfter({argsName} args, {returnType} result, MethodContext ctx)
        {{
            // TODO: 后置逻辑（指标上报、审计日志、数据收集、缓存写入）
            // 示例: Console.WriteLine($""[{{ctx.MethodName}}] 完成, 耗时={{ctx.Elapsed.TotalMilliseconds:F1}}ms"");
        }}

        /// <summary>
        /// 方法抛出异常时调用。
        /// 设置 ctx.Handled = true 可吞掉异常（降级处理）。
        /// </summary>
        public override void OnException({argsName} args, Exception ex, MethodContext ctx)
        {{
            // TODO: 异常逻辑（告警通知、错误统计、熔断计数）
            // 示例: Console.WriteLine($""[{{ctx.MethodName}}] 异常: {{ex.Message}}"");
        }}
    }}
}}
";
            return AddNewFileAsync(document, root, methodDecl, handlerName, code, ct);
        }

        private static Task<Document> GenerateAsyncHandlerAsync(
            Document document, SyntaxNode root, MethodDeclarationSyntax methodDecl,
            string ns, string className, string methodName, string argsName,
            string returnType, string paramDesc, string handlerName, CancellationToken ct)
        {
            var code = $@"using AutoCode.Model;
using System;
using System.Threading.Tasks;

namespace {ns}
{{
    /// <summary>
    /// {className}.{methodName} 方法的异步拦截处理器。
    /// 参数: ({paramDesc})
    /// 返回值: {returnType}
    /// 
    /// 适用于需要异步操作的场景（写审计到数据库、调外部告警 API）。
    /// </summary>
    public class {handlerName} : AsyncMethodHandlerBase<{argsName}, {returnType}>
    {{
        public override async Task OnBeforeAsync({argsName} args, MethodContext ctx)
        {{
            // TODO: 异步前置逻辑
            // 示例: await _auditService.CheckPermissionAsync(args);
            await Task.CompletedTask;
        }}

        public override async Task OnAfterAsync({argsName} args, {returnType} result, MethodContext ctx)
        {{
            // TODO: 异步后置逻辑（写审计日志到数据库）
            // 示例: await _auditService.RecordAsync(ctx.MethodName, args, result, ctx.Elapsed);
            await Task.CompletedTask;
        }}

        public override async Task OnExceptionAsync({argsName} args, Exception ex, MethodContext ctx)
        {{
            // TODO: 异步异常逻辑（调外部告警 API）
            // 示例: await _alertService.NotifyAsync($""{{ctx.MethodName}} 异常: {{ex.Message}}"");
            await Task.CompletedTask;
        }}
    }}
}}
";
            return AddNewFileAsync(document, root, methodDecl, handlerName, code, ct);
        }

        /// <summary>
        /// 将生成的 Handler 代码添加到项目中，
        /// 并在原方法上自动添加 [AutoIntercept] + [CustomIntercept(typeof(HandlerName))]。
        /// </summary>
        private static async Task<Document> AddNewFileAsync(
            Document document, SyntaxNode root, MethodDeclarationSyntax methodDecl,
            string handlerName, string code, CancellationToken ct)
        {
            // 检查方法是否已有 [AutoIntercept]
            var hasAutoIntercept = methodDecl.AttributeLists
                .SelectMany(a => a.Attributes)
                .Any(a =>
                {
                    var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                    return name is "AutoIntercept" or "AutoInterceptAttribute";
                });

            var newMethodDecl = methodDecl;

            // 如果没有 [AutoIntercept]，先加上（默认 Log + Metrics）
            if (!hasAutoIntercept)
            {
                var interceptAttr = SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("AutoIntercept"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.ParseExpression("InterceptType.Log | InterceptType.Metrics")))));

                var interceptAttrList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(interceptAttr))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                newMethodDecl = newMethodDecl.AddAttributeLists(interceptAttrList);
            }

            // 添加 [CustomIntercept(typeof(HandlerName))]
            var customAttr = SyntaxFactory.Attribute(
                SyntaxFactory.IdentifierName("CustomIntercept"),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.TypeOfExpression(
                                SyntaxFactory.IdentifierName(handlerName))))));

            var customAttrList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(customAttr))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            newMethodDecl = newMethodDecl.AddAttributeLists(customAttrList);
            var newRoot = root.ReplaceNode(methodDecl, newMethodDecl);

            // 确保 using AutoCode.Model 存在
            var compilationUnit = newRoot as CompilationUnitSyntax;
            if (compilationUnit != null)
            {
                var hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == "AutoCode.Model");
                if (!hasUsing)
                {
                    var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("AutoCode.Model"))
                        .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                    newRoot = compilationUnit.AddUsings(usingDirective);
                }
            }

            // 将 Handler 代码添加到同一文档末尾（简化处理）
            // 实际生产中应该创建新文件，这里为了演示先追加到同文件
            var handlerTree = CSharpSyntaxTree.ParseText(code, cancellationToken: ct);
            var handlerRoot = handlerTree.GetRoot(ct) as CompilationUnitSyntax;
            if (handlerRoot != null && newRoot is CompilationUnitSyntax newCu)
            {
                // 提取 handler 的 namespace 内的类
                var handlerNs = handlerRoot.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
                if (handlerNs != null)
                {
                    var existingNs = newCu.Members.OfType<NamespaceDeclarationSyntax>()
                        .FirstOrDefault(n => n.Name.ToString() == handlerNs.Name.ToString());

                    if (existingNs != null)
                    {
                        // 将 handler 类追加到已有 namespace
                        var newNs = existingNs.AddMembers(handlerNs.Members.ToArray());
                        newRoot = newCu.ReplaceNode(existingNs, newNs);
                    }
                    else
                    {
                        // 追加整个 namespace
                        newRoot = newCu.AddMembers(handlerNs.WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed));
                    }
                }
            }

            return document.WithSyntaxRoot(newRoot);
        }

        private static string GetReturnTypeDisplay(IMethodSymbol method)
        {
            var returnType = method.ReturnType;
            var display = returnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            // Task<T> → T, Task → object, ValueTask<T> → T
            if (display.StartsWith("Task<") || display.StartsWith("ValueTask<"))
            {
                var start = display.IndexOf('<');
                var end = display.LastIndexOf('>');
                if (start >= 0 && end > start)
                    return display.Substring(start + 1, end - start - 1);
            }
            if (display == "Task" || display == "ValueTask")
                return "object";

            return display;
        }

        private static string ExtractAsyncInnerType(IMethodSymbol method)
        {
            var display = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (display.StartsWith("Task<") || display.StartsWith("ValueTask<"))
            {
                var start = display.IndexOf('<');
                var end = display.LastIndexOf('>');
                if (start >= 0 && end > start)
                    return display.Substring(start + 1, end - start - 1);
            }
            return "object";
        }

        // ═══════════════ 类级别：批量生成 Handler ═══════════════

        /// <summary>
        /// 类级别重构：光标在类名上时，为全部 public 方法一键生成拦截 Handler。
        /// </summary>
        private static async Task RegisterClassLevelHandlerRefactoringAsync(
            CodeRefactoringContext context, SyntaxNode root, ClassDeclarationSyntax classDecl)
        {
            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel == null) return;

            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken);
            if (classSymbol == null) return;

            // 收集 public 实例方法（排除构造/属性/静态工具方法外的普通方法）
            var publicMethods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(SyntaxKind.PublicKeyword))
                .ToList();

            if (publicMethods.Count == 0) return;

            var className = classSymbol.Name;
            var ns = classSymbol.ContainingNamespace?.ToDisplayString() ?? "";

            context.RegisterRefactoring(
                CodeAction.Create(
                    title: $"⚡ 为 {className} 的 {publicMethods.Count} 个 public 方法批量生成拦截 Handler",
                    createChangedDocument: ct => GenerateAllHandlersAsync(
                        context.Document, root, classDecl, classSymbol, ns, publicMethods, ct),
                    equivalenceKey: $"GenerateAllHandlers_{className}"));
        }

        /// <summary>
        /// 为类的所有 public 方法生成 Handler：
        /// 逐方法添加 [AutoIntercept]+[CustomIntercept]，并追加全部 Handler 类到文档。
        /// </summary>
        private static async Task<Document> GenerateAllHandlersAsync(
            Document document, SyntaxNode root, ClassDeclarationSyntax classDecl,
            INamedTypeSymbol classSymbol, string ns,
            List<MethodDeclarationSyntax> publicMethods, CancellationToken ct)
        {
            var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (semanticModel == null) return document;

            var className = classSymbol.Name;
            var currentClassDecl = classDecl;
            var handlerCodeBlocks = new List<string>();

            foreach (var methodDecl in publicMethods)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl, ct);
                if (methodSymbol == null) continue;

                var methodName = methodSymbol.Name;
                var argsName = $"{methodName}Args";
                var returnType = GetReturnTypeDisplay(methodSymbol);
                var handlerName = $"{methodName}Handler";
                var paramDesc = string.Join(", ", methodSymbol.Parameters.Select(p =>
                    $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"));

                // 生成单个 Handler 类代码
                handlerCodeBlocks.Add(BuildHandlerClassCode(ns, className, methodName,
                    argsName, returnType, paramDesc, handlerName));

                // 为方法添加 [AutoIntercept]（若无）+ [CustomIntercept(typeof(Handler))]
                currentClassDecl = AddInterceptAttributesToMethod(currentClassDecl, methodDecl, handlerName);
            }

            // 替换类声明（含新增特性）
            var newRoot = root.ReplaceNode(classDecl, currentClassDecl);

            // 确保 using AutoCode.Model
            newRoot = EnsureUsingForHandler(newRoot, "AutoCode.Model");

            // 追加全部 Handler 类到 namespace
            newRoot = AppendHandlerClasses(newRoot, ns, handlerCodeBlocks, ct);

            return document.WithSyntaxRoot(newRoot);
        }

        /// <summary>构建单个 Handler 类代码（不含 namespace）。</summary>
        private static string BuildHandlerClassCode(
            string ns, string className, string methodName,
            string argsName, string returnType, string paramDesc, string handlerName)
        {
            return $@"
    /// <summary>
    /// {className}.{methodName} 方法的拦截处理器。
    /// 参数: ({paramDesc})
    /// 返回值: {returnType}
    /// 由 AutoCode 自动生成骨架，Args 类型 '{argsName}' 由编译时生成器自动产出。
    /// </summary>
    public class {handlerName} : MethodHandlerBase<{argsName}, {returnType}>
    {{
        public override void OnBefore({argsName} args, MethodContext ctx)
        {{
            // TODO: 前置逻辑
        }}

        public override void OnAfter({argsName} args, {returnType} result, MethodContext ctx)
        {{
            // TODO: 后置逻辑
        }}

        public override void OnException({argsName} args, Exception ex, MethodContext ctx)
        {{
            // TODO: 异常逻辑
        }}
    }}";
        }

        /// <summary>为指定方法添加 [AutoIntercept]（若无）和 [CustomIntercept(typeof(Handler))]。</summary>
        private static ClassDeclarationSyntax AddInterceptAttributesToMethod(
            ClassDeclarationSyntax classDecl, MethodDeclarationSyntax methodDecl, string handlerName)
        {
            var targetMethod = classDecl.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == methodDecl.Identifier.Text
                    && m.ParameterList.Parameters.Count == methodDecl.ParameterList.Parameters.Count);
            if (targetMethod == null) return classDecl;

            var hasAutoIntercept = targetMethod.AttributeLists.SelectMany(a => a.Attributes)
                .Any(a =>
                {
                    var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                    return name is "AutoIntercept" or "AutoInterceptAttribute";
                });

            var newMethod = targetMethod;

            if (!hasAutoIntercept)
            {
                var interceptAttr = SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("AutoIntercept"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.ParseExpression("InterceptType.Log | InterceptType.Metrics")))));
                newMethod = newMethod.AddAttributeLists(
                    SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(interceptAttr))
                        .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));
            }

            var customAttr = SyntaxFactory.Attribute(
                SyntaxFactory.IdentifierName("CustomIntercept"),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.TypeOfExpression(SyntaxFactory.IdentifierName(handlerName))))));
            newMethod = newMethod.AddAttributeLists(
                SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(customAttr))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));

            return classDecl.ReplaceNode(targetMethod, newMethod);
        }

        /// <summary>将多个 Handler 类追加到目标 namespace。</summary>
        private static SyntaxNode AppendHandlerClasses(
            SyntaxNode root, string ns, List<string> handlerCodeBlocks, CancellationToken ct)
        {
            if (handlerCodeBlocks.Count == 0) return root;

            var joined = string.Join("\n", handlerCodeBlocks);
            var wrapped = $"namespace {ns}\n{{\n{joined}\n}}";
            var parsed = CSharpSyntaxTree.ParseText(wrapped, cancellationToken: ct).GetRoot(ct)
                as CompilationUnitSyntax;
            if (parsed == null) return root;

            var parsedNs = parsed.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (parsedNs == null) return root;

            if (root is CompilationUnitSyntax cu)
            {
                var existingNs = cu.Members.OfType<NamespaceDeclarationSyntax>()
                    .FirstOrDefault(n => n.Name.ToString() == parsedNs.Name.ToString());
                if (existingNs != null)
                {
                    var newNs = existingNs.AddMembers(parsedNs.Members.ToArray());
                    return cu.ReplaceNode(existingNs, newNs);
                }
                return cu.AddMembers(parsedNs.WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed));
            }
            return root;
        }

        /// <summary>确保 using 存在。</summary>
        private static SyntaxNode EnsureUsingForHandler(SyntaxNode root, string ns)
        {
            if (root is CompilationUnitSyntax cu)
            {
                var hasUsing = cu.Usings.Any(u => u.Name?.ToString() == ns);
                if (!hasUsing)
                {
                    var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns))
                        .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                    return cu.AddUsings(usingDirective);
                }
            }
            return root;
        }
    }
}
