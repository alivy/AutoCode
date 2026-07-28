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
    /// AC001 快速修复: 为类添加 [AutoInterface] 特性
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddAutoInterfaceCodeFix)), Shared]
    public class AddAutoInterfaceCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(AutoCodeDiagnosticDescriptors.MissingAutoInterface.Id);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var classDecl = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                .OfType<ClassDeclarationSyntax>().First();

            if (classDecl == null) return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "添加 [AutoInterface] 特性",
                    createChangedDocument: c => AddAutoInterfaceAttributeAsync(context.Document, classDecl, c),
                    equivalenceKey: "AddAutoInterface"),
                diagnostic);
        }

        private static async Task<Document> AddAutoInterfaceAttributeAsync(
            Document document,
            ClassDeclarationSyntax classDecl,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return document;

            // 创建 [AutoInterface] 特性
            var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("AutoInterface"));
            var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(attribute))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            // 添加到类的特性列表
            var newClassDecl = classDecl.AddAttributeLists(attributeList);

            // 检查是否需要添加 using
            var compilationUnit = root as CompilationUnitSyntax;
            if (compilationUnit != null)
            {
                var hasUsing = compilationUnit.Usings.Any(u =>
                    u.Name?.ToString() == "AutoCode.Model.InterfaceAttribute");

                if (!hasUsing)
                {
                    var usingDirective = SyntaxFactory.UsingDirective(
                        SyntaxFactory.ParseName("AutoCode.Model.InterfaceAttribute"))
                        .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                    var newRoot = compilationUnit.AddUsings(usingDirective);
                    newRoot = newRoot.ReplaceNode(classDecl, newClassDecl);
                    return document.WithSyntaxRoot(newRoot);
                }
            }

            var updatedRoot = root.ReplaceNode(classDecl, newClassDecl);
            return document.WithSyntaxRoot(updatedRoot);
        }
    }
}
