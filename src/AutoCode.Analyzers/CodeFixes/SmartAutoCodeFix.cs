using AutoCode.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCode.Analyzers.CodeFixes
{
    /// <summary>
    /// AC8xxx 智能建议 CodeFix - 根据类的命名模式和上下文，
    /// 推荐最合适的 AutoCode 特性（Ctrl+. 一键添加）。
    /// 
    /// 智能推荐逻辑：
    ///   - *Service 类 → 推荐 [AutoInterface] + [AutoIntercept(Log|Metrics)]
    ///   - *Repository 类 → 推荐 [AutoInterface]
    ///   - 含 Id 属性的实体类 → 推荐 [AutoEntity] 全链路
    ///   - 含异步方法的类 → 推荐 [AutoIntercept(Log|Retry)]
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SmartAutoCodeFix)), Shared]
    public class SmartAutoCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(
                AutoCodeDiagnosticDescriptors.MissingAutoInterface.Id,
                "AC8001", "AC8002", "AC8003");

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var classDecl = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                .OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDecl == null) return;

            var className = classDecl.Identifier.Text;

            // 智能推荐 1：[AutoEntity] 全链路（检测到 Id 属性）
            if (HasIdProperty(classDecl))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "🚀 添加 [AutoEntity] 一键生成全链路（DTO/Mapper/API/Test）",
                        createChangedDocument: c => AddAttributeAsync(context.Document, classDecl, "AutoEntity", "AutoCode.Model", c),
                        equivalenceKey: "AddAutoEntity"),
                    diagnostic);
            }

            // 智能推荐 2：[AutoIntercept] AOP 拦截
            if (HasAsyncMethods(classDecl) || className.EndsWith("Service"))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "⚡ 添加 [AutoIntercept(Log|Retry|Metrics)] 编译时 AOP",
                        createChangedDocument: c => AddInterceptAttributeAsync(context.Document, classDecl, c),
                        equivalenceKey: "AddAutoIntercept"),
                    diagnostic);
            }

            // 智能推荐 3：[AutoInterface] 接口提取
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "🔌 添加 [AutoInterface] 自动提取接口",
                    createChangedDocument: c => AddAttributeAsync(context.Document, classDecl, "AutoInterface", "AutoCode.Model", c),
                    equivalenceKey: "AddAutoInterface_Smart"),
                diagnostic);
        }

        /// <summary>检测类是否含 Id 属性（实体候选）</summary>
        private static bool HasIdProperty(ClassDeclarationSyntax classDecl)
        {
            return classDecl.Members
                .OfType<PropertyDeclarationSyntax>()
                .Any(p => p.Identifier.Text == "Id" || p.Identifier.Text.EndsWith("Id"));
        }

        /// <summary>检测类是否含异步方法</summary>
        private static bool HasAsyncMethods(ClassDeclarationSyntax classDecl)
        {
            return classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .Any(m => m.Modifiers.Any(SyntaxKind.AsyncKeyword) ||
                          m.ReturnType.ToString().Contains("Task"));
        }

        /// <summary>添加简单特性</summary>
        private static async Task<Document> AddAttributeAsync(
            Document document, ClassDeclarationSyntax classDecl,
            string attrName, string ns, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null) return document;

            var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(attrName));
            var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(attribute))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var newClassDecl = classDecl.AddAttributeLists(attributeList);
            var newRoot = EnsureUsing(root, classDecl, newClassDecl, ns);
            return document.WithSyntaxRoot(newRoot);
        }

        /// <summary>添加 [AutoIntercept(InterceptType.Log | InterceptType.Retry | InterceptType.Metrics)]</summary>
        private static async Task<Document> AddInterceptAttributeAsync(
            Document document, ClassDeclarationSyntax classDecl, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null) return document;

            // 构造: AutoIntercept(InterceptType.Log | InterceptType.Retry | InterceptType.Metrics)
            var attrName = SyntaxFactory.IdentifierName("AutoIntercept");
            var argExpr = SyntaxFactory.ParseExpression(
                "InterceptType.Log | InterceptType.Retry | InterceptType.Metrics");
            var argument = SyntaxFactory.AttributeArgument(argExpr);
            var argList = SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(argument));

            var attribute = SyntaxFactory.Attribute(attrName, argList);
            var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(attribute))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var newClassDecl = classDecl.AddAttributeLists(attributeList);
            var newRoot = EnsureUsing(root, classDecl, newClassDecl, "AutoCode.Model");
            return document.WithSyntaxRoot(newRoot);
        }

        /// <summary>确保 using 存在</summary>
        private static SyntaxNode EnsureUsing(
            SyntaxNode root, ClassDeclarationSyntax oldNode,
            ClassDeclarationSyntax newNode, string ns)
        {
            var compilationUnit = root as CompilationUnitSyntax;
            if (compilationUnit == null)
                return root.ReplaceNode(oldNode, newNode);

            var hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == ns);
            if (!hasUsing)
            {
                var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns))
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                var newRoot = compilationUnit.AddUsings(usingDirective);
                return newRoot.ReplaceNode(oldNode, newNode);
            }

            return root.ReplaceNode(oldNode, newNode);
        }
    }
}
