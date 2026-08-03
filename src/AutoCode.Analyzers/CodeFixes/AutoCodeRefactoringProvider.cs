using AutoCode.Analyzers.Recipes;
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
    /// AutoCode 统一右键重构提供器 - 覆盖内置 11 个生成器 + 用户自定义配方。
    /// 通过 ICodeGenRecipe 接口统一管理，根据类特征智能推荐可用配方。
    /// 开发者无需记忆任何 Attribute 名称，Ctrl+. 即可一键添加。
    /// 
    /// 推断规则（由 BuiltInRecipes 定义）：
    ///   含 Id 属性 / 实体类 → AutoEntity、AutoDTO、MapFrom、AutoCrud、AutoValidator
    ///   名称匹配 *Service   → AutoInterface、AutoController、AutoLog、AutoTest、AutoIntercept、IScoped
    ///   名称匹配 *Request   → AutoValidator、AutoDTO
    ///   名称匹配 *Dto       → MapFrom
    ///   名称匹配 *Repository→ AutoInterface、IScoped
    ///   任意 public 方法     → AutoIntercept + 生成 Handler
    ///   自定义配方（autocode.json customGenerators）→ 动态注册
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

            // 构建类分析信息
            var classInfo = BuildClassAnalysisInfo(classDecl, classSymbol);

            // ── 1. 内置配方推荐 ──
            foreach (var recipe in BuiltInRecipes.All)
            {
                if (!recipe.IsApplicable(classInfo)) continue;
                if (recipe.IsAlreadyApplied(classInfo)) continue;

                if (recipe.Name == "scoped")
                {
                    // IScoped 是接口而非 Attribute
                    RegisterAddInterface(context, root, classDecl, "IScoped", $"💉 {recipe.Title}");
                }
                else if (recipe.Name == "autoController" && recipe is AutoControllerRecipe ctrlRecipe)
                {
                    // AutoController 需要动态参数
                    var arg = ctrlRecipe.GetArgument(classInfo);
                    RegisterAddAttributeWithParam(context, root, classDecl, recipe.AttributeName, arg, $"{recipe.Icon} {recipe.Title}");
                }
                else if (!string.IsNullOrEmpty(recipe.AttributeArgument))
                {
                    RegisterAddAttributeWithParam(context, root, classDecl, recipe.AttributeName,
                        recipe.AttributeArgument, $"{recipe.Icon} {recipe.Title}");
                }
                else
                {
                    RegisterAddAttribute(context, root, classDecl, recipe.AttributeName, $"{recipe.Icon} {recipe.Title}");
                }
            }

            // ── 2. 自定义配方推荐（从 autocode.json 加载）──
            var customRecipes = await LoadCustomRecipesAsync(context.Document);
            foreach (var recipe in customRecipes)
            {
                if (!recipe.IsApplicable(classInfo)) continue;
                if (recipe.IsAlreadyApplied(classInfo)) continue;

                if (!string.IsNullOrEmpty(recipe.AttributeArgument))
                {
                    RegisterAddAttributeWithParam(context, root, classDecl, recipe.AttributeName,
                        recipe.AttributeArgument, recipe.Title);
                }
                else
                {
                    RegisterAddAttribute(context, root, classDecl, recipe.AttributeName, recipe.Title);
                }
            }
        }

        private static ClassAnalysisInfo BuildClassAnalysisInfo(
            ClassDeclarationSyntax classDecl, INamedTypeSymbol classSymbol)
        {
            var info = new ClassAnalysisInfo
            {
                ClassName = classSymbol.Name,
                HasPublicMethods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Modifiers.Any(SyntaxKind.PublicKeyword)),
                HasIdProperty = classDecl.Members.OfType<PropertyDeclarationSyntax>()
                    .Any(p => p.Identifier.Text == "Id" || p.Identifier.Text.EndsWith("Id")),
                HasDataAnnotations = classDecl.Members.OfType<PropertyDeclarationSyntax>()
                    .Any(p => p.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
                    {
                        var n = a.Name.ToString();
                        return n is "Required" or "MaxLength" or "Range" or "MinLength" or "EmailAddress" or "Url";
                    }))
            };

            foreach (var attr in GetExistingAttributes(classDecl))
                info.ExistingAttributes.Add(attr);

            foreach (var prop in classDecl.Members.OfType<PropertyDeclarationSyntax>())
                info.PropertyNames.Add(prop.Identifier.Text);

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
                info.MethodNames.Add(method.Identifier.Text);

            foreach (var iface in classSymbol.Interfaces)
                info.Interfaces.Add(iface.Name);
            foreach (var iface in classSymbol.AllInterfaces)
                info.Interfaces.Add(iface.Name);

            return info;
        }

        private static async Task<List<ICodeGenRecipe>> LoadCustomRecipesAsync(Document document)
        {
            try
            {
                var projectDir = System.IO.Path.GetDirectoryName(document.Project.FilePath);
                if (string.IsNullOrEmpty(projectDir)) return new List<ICodeGenRecipe>();

                // 向上遍历目录树查找 autocode.json（可能位于仓库根目录而非项目目录）
                var searchDir = projectDir;
                while (!string.IsNullOrEmpty(searchDir))
                {
                    var configPath = System.IO.Path.Combine(searchDir, "autocode.json");
                    if (System.IO.File.Exists(configPath))
                    {
                        var json = System.IO.File.ReadAllText(configPath);
                        return CustomRecipeAdapter.LoadFromConfigJson(json);
                    }
                    var parent = System.IO.Directory.GetParent(searchDir)?.FullName;
                    if (string.IsNullOrEmpty(parent) || parent == searchDir) break;
                    searchDir = parent;
                }

                return new List<ICodeGenRecipe>();
            }
            catch
            {
                return new List<ICodeGenRecipe>();
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
