using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace AutoCode.DotTemplate.SourceGenerator
{
    /// <summary>
    /// 模版语法树
    /// </summary>
    public class DotTemplateSyntaxReceiver : ISyntaxReceiver
    {

        /// <summary>
        /// 模版特性名称
        /// </summary>
        public string TemplateAttributeName = "DotTemplate";

        /// <summary>
        /// 收集cs数据
        /// </summary>
        public CSData csData = new CSData();



        /// <summary>
        /// 生成语法树节点时构建
        /// </summary>
        /// <param name="syntaxNode"></param>
        /// <exception cref="NotImplementedException"></exception>
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            //Debugger.Launch();
            if (syntaxNode is NamespaceDeclarationSyntax namespaceDeclaration)
            {
                csData.NameSpace = namespaceDeclaration.Name.GetText().ToString();
                var classDeclarations = namespaceDeclaration.DescendantNodes().OfType<ClassDeclarationSyntax>();
                foreach (ClassDeclarationSyntax classDeclaration in classDeclarations)
                {
                    // 获取类声明的特性 
                    var attributes = classDeclaration.AttributeLists.SelectMany(attributeList => attributeList.Attributes.ToList());
                    foreach (var attribute in attributes)
                    {
                        var attributeName = attribute.Name is IdentifierNameSyntax identifierNameSyntax
                               ? identifierNameSyntax.Identifier.Text
                                : attribute.Name.ToString();
                        if (TemplateAttributeName.Contains(attributeName))
                        {
                            csData.ClassSyntaxeList.Add(classDeclaration);
                        }
                    }
                }
            }
        }
    }
}
