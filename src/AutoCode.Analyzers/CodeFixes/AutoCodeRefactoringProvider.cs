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
    /// AutoCode 统一右键重构提供器 - 覆盖全部 11 个生成器。
    /// 根据类的特征（名称模式、属性、已有接口）智能推荐可用的 AutoCode 特性，
    /// 开发者无需记忆任何 Attribute 名称，Ctrl+. 即可一键添加。
    /// 
    /// 推断规则：
    ///   含 Id 属性 / 实体类 → AutoEntity、AutoDTO、MapFrom、AutoCrud、AutoValidator
    ///   名称匹配 *Service   → AutoInterface、AutoController、AutoLog、AutoTest、AutoIntercept、IScoped
    ///   名称匹配 *Request   → AutoValidator、AutoDTO
    ///   名称匹配 *Dto       → MapFrom
    ///   名称匹配 *Repository→ AutoInterface、IScoped
    ///   任意 public 方法     → AutoIntercept + 生成 Handler
    /// </summary>
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(AutoCodeRefactoringProvider)), Shared]
    public class AutoCodeRefactoringProvider : CodeRefactoringProvider
    {
        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            var node = root.FindNode(context.Span);

            // ═══ 类级别推荐 ═══
            var classDecl = node.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDecl != null)
            {
                await RegisterClassRefactoringsAsync(context, root, classDecl);
            }

            // ═══ 方法级别推荐 ═══
            var methodDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDecl != null && methodDecl.Modifiers.Any(SyntaxKind.PublicKeyword))
            {
                RegisterMethodRefactorings(context, root, methodDecl);
            }
        }

        #region 类级别推荐

        private async Task RegisterClassRefactoringsAsync(
            CodeRefactoringContext context, SyntaxNode root, ClassDeclarationSyntax classDecl)
        {
            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel == null) return;

            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken);
            if (classSymbol == null) return;

            var className = classSymbol.Name;
            var existingAttrs = GetExistingAttributes(classDecl);
            var hasIdProperty = classDecl.Members.OfType<PropertyDeclarationSyntax>()
                .Any(p => p.Identifier.Text == "Id" || p.Identifier.Text.EndsWith("Id"));
            var hasDataAnnotations = classDecl.Members.OfType<PropertyDeclarationSyntax>()
                .Any(p => p.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
                {
                    var n = a.Name.ToString();
                    return n is "Required" or "MaxLength" or "Range" or "MinLength" or "EmailAddress" or "Url";
                }));

            var isService = className.EndsWith("Service");
            var isRequest = className.EndsWith("Request") || className.EndsWith("Command");
            var isDto = className.EndsWith("Dto") || className.EndsWith("Response");
            var isRepository = className.EndsWith("Repository");
            var isEntity = hasIdProperty && !isService && !isRequest && !isDto && !isRepository;

            // ─── 实体类推荐 ───
            if (isEntity)
            {
                if (!existingAttrs.Contains("AutoEntity"))
                    RegisterAddAttribute(context, root, classDecl, "AutoEntity",
                        $"🚀 [AutoEntity] 一键全链路（DTO+Mapper+Validator+API+DI）");

                if (!existingAttrs.Contains("AutoDTO"))
                    RegisterAddAttribute(context, root, classDecl, "AutoDTO",
                        $"📋 [AutoDTO] 生成 DTO + FromEntity/ToEntity");

                if (!existingAttrs.Contains("AutoCrud"))
                    RegisterAddAttribute(context, root, classDecl, "AutoCrud",
                        $"🔄 [AutoCrud] 生成 CRUD 全套（Service+Repository+Controller）");

                if (hasDataAnnotations && !existingAttrs.Contains("AutoValidator"))
                    RegisterAddAttribute(context, root, classDecl, "AutoValidator",
                        $"✅ [AutoValidator] 生成编译时验证代码");
            }

            // ─── Service 类推荐 ───
            if (isService)
            {
                if (!existingAttrs.Contains("AutoInterface"))
                    RegisterAddAttribute(context, root, classDecl, "AutoInterface",
                        $"🔌 [AutoInterface] 自动提取接口");

                if (!existingAttrs.Contains("AutoController"))
                    RegisterAddAttributeWithParam(context, root, classDecl, "AutoController",
                        $"RoutePrefix = \"api/{className.Replace("Service", "").ToLower()}s\"",
                        $"🌐 [AutoController] 生成 REST API Controller");

                if (!existingAttrs.Contains("AutoLog"))
                    RegisterAddAttribute(context, root, classDecl, "AutoLog",
                        $"📝 [AutoLog] 生成日志装饰器");

                if (!existingAttrs.Contains("AutoTest"))
                    RegisterAddAttribute(context, root, classDecl, "AutoTest",
                        $"🧪 [AutoTest] 生成单元测试桩");

                if (!existingAttrs.Contains("AutoIntercept"))
                    RegisterAddAttributeWithParam(context, root, classDecl, "AutoIntercept",
                        "InterceptType.Log | InterceptType.Metrics",
                        $"⚡ [AutoIntercept] 添加 AOP 拦截管线");

                // IScoped DI 标记
                if (!ImplementsInterface(classSymbol, "IScoped") && !ImplementsInterface(classSymbol, "ISingleton"))
                    RegisterAddInterface(context, root, classDecl, "IScoped",
                        $"💉 实现 IScoped（编译时 DI 自动注册）");
            }

            // ─── Request/Command 类推荐 ───
            if (isRequest)
            {
                if (!existingAttrs.Contains("AutoValidator"))
                    RegisterAddAttribute(context, root, classDecl, "AutoValidator",
                        $"✅ [AutoValidator] 生成编译时验证代码");

                if (!existingAttrs.Contains("AutoDTO"))
                    RegisterAddAttribute(context, root, classDecl, "AutoDTO",
                        $"📋 [AutoDTO] 生成 DTO");
            }

            // ─── Dto/Response 类推荐 ───
            if (isDto)
            {
                if (!existingAttrs.Contains("MapFrom"))
                    RegisterAddAttribute(context, root, classDecl, "MapFrom",
                        $"🗺️ [MapFrom] 生成编译时对象映射");
            }

            // ─── Repository 类推荐 ───
            if (isRepository)
            {
                if (!existingAttrs.Contains("AutoInterface"))
                    RegisterAddAttribute(context, root, classDecl, "AutoInterface",
                        $"🔌 [AutoInterface] 自动提取接口");

                if (!ImplementsInterface(classSymbol, "IScoped"))
                    RegisterAddInterface(context, root, classDecl, "IScoped",
                        $"💉 实现 IScoped（编译时 DI 自动注册）");
            }

            // ─── 通用推荐（任何类都可用）───
            if (!isEntity && !isService && !isRequest && !isDto && !isRepository)
            {
                if (!existingAttrs.Contains("AutoInterface") && classDecl.Members.OfType<MethodDeclarationSyntax>().Any())
                    RegisterAddAttribute(context, root, classDecl, "AutoInterface",
                        $"🔌 [AutoInterface] 自动提取接口");

                if (!existingAttrs.Contains("AutoIntercept"))
                    RegisterAddAttributeWithParam(context, root, classDecl, "AutoIntercept",
                        "InterceptType.Log | InterceptType.Metrics",
                        $"⚡ [AutoIntercept] 添加 AOP 拦截管线");
            }
        }

        #endregion

        #region 方法级别推荐

        private void RegisterMethodRefactorings(
            CodeRefactoringContext context, SyntaxNode root, MethodDeclarationSyntax methodDecl)
        {
            var existingAttrs = new HashSet<string>(methodDecl.AttributeLists
                .SelectMany(a => a.Attributes)
                .Select(a => a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString()));

            if (!existingAttrs.Contains("AutoIntercept") && !existingAttrs.Contains("AutoInterceptAttribute"))
            {
                var methodName = methodDecl.Identifier.Text;
                context.RegisterRefactoring(
                    CodeAction.Create(
                        title: $"⚡ [AutoIntercept] 拦截 {methodName}（Log+Metrics）",
                        createChangedDocument: ct => AddAttributeToMethodAsync(
                            context.Document, root, methodDecl,
                            "AutoIntercept", "InterceptType.Log | InterceptType.Metrics", ct),
                        equivalenceKey: $"AddAutoIntercept_{methodName}"));
            }

            if (!existingAttrs.Contains("SkipIntercept") && !existingAttrs.Contains("SkipInterceptAttribute"))
            {
                var methodName = methodDecl.Identifier.Text;
                context.RegisterRefactoring(
                    CodeAction.Create(
                        title: $"🚫 [SkipIntercept] 跳过 {methodName} 的拦截",
                        createChangedDocument: ct => AddSimpleAttributeToMethodAsync(
                            context.Document, root, methodDecl, "SkipIntercept", ct),
                        equivalenceKey: $"AddSkipIntercept_{methodName}"));
            }
        }

        #endregion

        #region 辅助方法

        private static HashSet<string> GetExistingAttributes(ClassDeclarationSyntax classDecl)
        {
            return new HashSet<string>(classDecl.AttributeLists
                .SelectMany(a => a.Attributes)
                .Select(a => a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString()));
        }

        private static bool ImplementsInterface(INamedTypeSymbol classSymbol, string interfaceName)
        {
            return classSymbol.Interfaces.Any(i => i.Name == interfaceName)
                || classSymbol.AllInterfaces.Any(i => i.Name == interfaceName);
        }

        /// <summary>添加简单 Attribute（无参数）到类</summary>
        private void RegisterAddAttribute(
            CodeRefactoringContext context, SyntaxNode root,
            ClassDeclarationSyntax classDecl, string attrName, string title)
        {
            context.RegisterRefactoring(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: ct => AddAttributeToClassAsync(
                        context.Document, root, classDecl, attrName, null, ct),
                    equivalenceKey: $"Add_{attrName}_{classDecl.Identifier.Text}"));
        }

        /// <summary>添加带参数的 Attribute 到类</summary>
        private void RegisterAddAttributeWithParam(
            CodeRefactoringContext context, SyntaxNode root,
            ClassDeclarationSyntax classDecl, string attrName, string paramExpr, string title)
        {
            context.RegisterRefactoring(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: ct => AddAttributeToClassAsync(
                        context.Document, root, classDecl, attrName, paramExpr, ct),
                    equivalenceKey: $"Add_{attrName}_{classDecl.Identifier.Text}"));
        }

        /// <summary>添加接口实现到类</summary>
        private void RegisterAddInterface(
            CodeRefactoringContext context, SyntaxNode root,
            ClassDeclarationSyntax classDecl, string interfaceName, string title)
        {
            context.RegisterRefactoring(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: ct => AddInterfaceToClassAsync(
                        context.Document, root, classDecl, interfaceName, ct),
                    equivalenceKey: $"AddIface_{interfaceName}_{classDecl.Identifier.Text}"));
        }

        private static async Task<Document> AddAttributeToClassAsync(
            Document document, SyntaxNode root, ClassDeclarationSyntax classDecl,
            string attrName, string? paramExpr, CancellationToken ct)
        {
            AttributeSyntax attr;
            if (paramExpr != null)
            {
                attr = SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName(attrName),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.ParseExpression(paramExpr)))));
            }
            else
            {
                attr = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attrName));
            }

            var attrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var newClassDecl = classDecl.AddAttributeLists(attrList);
            var newRoot = root.ReplaceNode(classDecl, newClassDecl);

            // 确保 using AutoCode.Model
            newRoot = EnsureUsing(newRoot, "AutoCode.Model");

            return document.WithSyntaxRoot(newRoot);
        }

        private static async Task<Document> AddAttributeToMethodAsync(
            Document document, SyntaxNode root, MethodDeclarationSyntax methodDecl,
            string attrName, string paramExpr, CancellationToken ct)
        {
            var attr = SyntaxFactory.Attribute(
                SyntaxFactory.IdentifierName(attrName),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.ParseExpression(paramExpr)))));

            var attrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var newMethodDecl = methodDecl.AddAttributeLists(attrList);
            var newRoot = root.ReplaceNode(methodDecl, newMethodDecl);
            newRoot = EnsureUsing(newRoot, "AutoCode.Model");

            return document.WithSyntaxRoot(newRoot);
        }

        private static async Task<Document> AddSimpleAttributeToMethodAsync(
            Document document, SyntaxNode root, MethodDeclarationSyntax methodDecl,
            string attrName, CancellationToken ct)
        {
            var attr = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attrName));
            var attrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var newMethodDecl = methodDecl.AddAttributeLists(attrList);
            var newRoot = root.ReplaceNode(methodDecl, newMethodDecl);
            newRoot = EnsureUsing(newRoot, "AutoCode.Model");

            return document.WithSyntaxRoot(newRoot);
        }

        private static async Task<Document> AddInterfaceToClassAsync(
            Document document, SyntaxNode root, ClassDeclarationSyntax classDecl,
            string interfaceName, CancellationToken ct)
        {
            var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName(interfaceName));
            var newClassDecl = classDecl.AddBaseListTypes(baseType);
            var newRoot = root.ReplaceNode(classDecl, newClassDecl);
            newRoot = EnsureUsing(newRoot, "AutoCode.Model");

            return document.WithSyntaxRoot(newRoot);
        }

        private static SyntaxNode EnsureUsing(SyntaxNode root, string ns)
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

        #endregion
    }
}
