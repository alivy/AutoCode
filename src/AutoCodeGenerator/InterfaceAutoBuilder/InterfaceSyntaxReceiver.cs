using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AutoCode.SourceGenerator.InterfaceAutoBuilder
{
    /* 需求： 在构建代码时，只需要定义类，就自动生成接口，接口内容为构建public方法的接口
     * 实现：
     * 首先确定标识，添加[AutoInterface]标识的类自动生成接口，找到所有public的方法，
     * 如果有方法不生成接口对外，则标记[AutoIgnore]
     */
    /// <summary>
    /// 继承接口 代码生成器
    /// </summary>
    public class InterfaceSyntaxReceiver : ISyntaxReceiver
    {
        /// <summary>
        /// 忽略特性名称
        /// </summary>
        public string IgnoreAttributeName = "AutoIgnore";

        /// <summary>
        /// 生成接口特性名称
        /// </summary>
        public string InterfaceAttributeName = "AutoInterface";

        /// <summary>
        /// using集合
        /// </summary>
        public List<UsingDirectiveSyntax> UsingDirectives { get; } = new List<UsingDirectiveSyntax>();

        /// <summary>
        /// 获得CS文件基础信息
        /// </summary>
        public List<BaseCSInfo> BaseClassInfos = new List<BaseCSInfo>();
        /// <summary>
        /// 
        /// </summary>
        /// <param name="syntaxNode"></param>
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            //Debugger.Launch();
            // 获取所有 using 指令
            //if (syntaxNode is UsingDirectiveSyntax usingDirectiveSyntax)
            //{
            //    UsingDirectives.Add(usingDirectiveSyntax);
            //}
            //var usingDirectives = syntaxNode.DescendantNodes().OfType<UsingDirectiveSyntax>().ToList();
            // 找到所有的 NamespaceDeclarationSyntax 节点
            //var namespaceDeclarations = syntaxNode.DescendantNodesAndSelf().OfType<NamespaceDeclarationSyntax>();
            if (syntaxNode is NamespaceDeclarationSyntax namespaceDeclaration)
            {
                // 找到该 NamespaceDeclarationSyntax 下的所有 ClassDeclarationSyntax 节点
                var classDeclarations = namespaceDeclaration.DescendantNodes().OfType<ClassDeclarationSyntax>();
                foreach (ClassDeclarationSyntax classDeclaration in classDeclarations)
                {
                    // 获取类声明的特性
                    var attributes = classDeclaration.AttributeLists.SelectMany(attributeList =>
                        attributeList.Attributes.ToList());
                    foreach (var attribute in attributes)
                    {
                        var attributeName = attribute.Name is IdentifierNameSyntax identifierNameSyntax
                               ? identifierNameSyntax.Identifier.Text
                                : attribute.Name.ToString();

                        if (attributeName == InterfaceAttributeName)
                        {
                            var collector = new ClassUsingCollector();
                            collector.Visit(classDeclaration);
                            var baseClassInfo = new BaseCSInfo
                            {
                                ClassDeclarations = classDeclaration,
                                NamespaceDeclaration = namespaceDeclaration,
                                UsingDirectives = collector.Usings.ToList()
                            };
                            var attributeArgs = attribute.ArgumentList?.Arguments
                                  .Select(arg => arg.Expression.ToString().Trim('\"'))
                                  .ToList();
                            if (attributeArgs != null)
                            {
                                if (attributeArgs.Count > 0)
                                    baseClassInfo.InterfaceInfo.InterfaceName = attributeArgs.FirstOrDefault();

                                if (attributeArgs.Count > 1)
                                    baseClassInfo.InterfaceInfo.InterfacePath = attributeArgs.LastOrDefault();
                            }
                            BaseClassInfos.Add(baseClassInfo);
                        }
                    }
                }
            }
        }


        /// <summary>
        /// 获取实体下的方法
        /// </summary>
        /// <param name="syntaxReceiver"></param>
        /// <param name="classDeclaration"></param>
        /// <returns></returns>
        public IEnumerable<MethodDeclarationSyntax> GetClassMethods(InterfaceSyntaxReceiver syntaxReceiver, ClassDeclarationSyntax classDeclaration)
        {
            // 获取类的方法
            var methods = classDeclaration.Members.OfType<MethodDeclarationSyntax>();
            // 获取类的所有公共方法
            IEnumerable<MethodDeclarationSyntax> publicMethods = methods.Where(method => method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)));
            publicMethods = publicMethods.Where(method =>
            {
                if (!method.AttributeLists.Any())
                    return true;
                var attributeNames = method.AttributeLists.SelectMany(attributeList =>
                                           attributeList.Attributes.Select(attribute =>
                                               attribute.Name is IdentifierNameSyntax identifierNameSyntax
                                                   ? identifierNameSyntax.Identifier.Text
                                                   : attribute.Name.ToString()));
                return !attributeNames.Contains(syntaxReceiver.IgnoreAttributeName);
            }).Select(x => x).ToList();
            return publicMethods;
        }


        public class ClassUsingCollector : CSharpSyntaxWalker
        {
            public ClassUsingCollector() : base(SyntaxWalkerDepth.StructuredTrivia) { }

            public List<UsingDirectiveSyntax> Usings { get; } = new List<UsingDirectiveSyntax>();

            public override void VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                var parent = node.Parent;
                while (parent != null)
                {
                    if (parent is NamespaceDeclarationSyntax namespaceDeclaration)
                    {
                        Usings.AddRange(namespaceDeclaration.Usings);
                    }
                    else if (parent is CompilationUnitSyntax compilationUnit)
                    {
                        Usings.AddRange(compilationUnit.Usings);
                    }

                    parent = parent.Parent;
                }

                base.VisitClassDeclaration(node);
            }
        }

    }
}
