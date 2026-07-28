using AutoCode.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCode.Analyzers.CodeFixes
{
    /// <summary>
    /// AC003 快速修复: 移除非公共成员上无意义的 [AutoIgnore] 特性
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveAutoIgnoreCodeFix)), Shared]
    public class RemoveAutoIgnoreCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(AutoCodeDiagnosticDescriptors.UnusedAutoIgnore.Id);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            // 查找标记了 [AutoIgnore] 的成员声明
            var node = root.FindNode(diagnosticSpan);
            var memberDecl = node.AncestorsAndSelf()
                .OfType<MemberDeclarationSyntax>()
                .FirstOrDefault();

            if (memberDecl == null) return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "移除无意义的 [AutoIgnore]",
                    createChangedDocument: c => RemoveAutoIgnoreAttributeAsync(context.Document, memberDecl, c),
                    equivalenceKey: "RemoveAutoIgnore"),
                diagnostic);
        }

        private static async Task<Document> RemoveAutoIgnoreAttributeAsync(
            Document document,
            MemberDeclarationSyntax memberDecl,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return document;

            // 查找并移除 [AutoIgnore] 特性
            var attributeLists = memberDecl.AttributeLists;
            foreach (var attrList in attributeLists)
            {
                var autoIgnoreAttr = attrList.Attributes
                    .FirstOrDefault(a =>
                    {
                        var name = a.Name is IdentifierNameSyntax id
                            ? id.Identifier.Text
                            : a.Name.ToString();
                        return name == "AutoIgnore" || name == "AutoIgnoreAttribute";
                    });

                if (autoIgnoreAttr != null)
                {
                    AttributeListSyntax newAttrList;
                    if (attrList.Attributes.Count == 1)
                    {
                        // 整个 AttributeList 只有这一个特性，移除整个列表
                        var newMember = memberDecl.RemoveNode(attrList, SyntaxRemoveOptions.KeepNoTrivia);
                        if (newMember != null)
                        {
                            var newRoot = root.ReplaceNode(memberDecl, newMember);
                            return document.WithSyntaxRoot(newRoot);
                        }
                    }
                    else
                    {
                        // 移除单个特性
                        newAttrList = attrList.RemoveNode(autoIgnoreAttr, SyntaxRemoveOptions.KeepNoTrivia);
                        if (newAttrList != null)
                        {
                            var newMember = memberDecl.ReplaceNode(attrList, newAttrList);
                            var newRoot = root.ReplaceNode(memberDecl, newMember);
                            return document.WithSyntaxRoot(newRoot);
                        }
                    }
                }
            }

            return document;
        }
    }
}
