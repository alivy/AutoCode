using AutoCode.DotTemplate.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static AutoCode.DotTemplate.SourceGenerator.CSData;
using static AutoCode.DotTemplate.SourceGenerator.CSData.ClassInfo;

namespace AutoCode.XmlTemplate.SourceGenerator
{
    /// <summary>
    /// 语法树数据转换
    /// </summary>
    public static class SyntaxNodeConvert
    {
        /// <summary>
        /// 忽略特性名称
        /// </summary>
        public const string IgnoreAttributeName = "AutoIgnore";
        /// <summary>
        /// 将ClassDeclarationSyntax转化为ClassInfo对象
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="compilation"></param>
        /// <returns></returns>
        public static ClassInfo ClassConvert(this ClassDeclarationSyntax classDeclaration, Compilation compilation)
        {
            var @class = new ClassInfo()
            {
                DefName = classDeclaration.Identifier.ToString(),
                Modifier = string.Join(" ", classDeclaration.Modifiers),
                ClassPath = CetClassPath(compilation, classDeclaration),
                Remarks = GetRemarks(classDeclaration)
            };

            classDeclaration.GetUsing(@class)
                            .GetBaseClassNode(@class)
                            .GetAttributeNode(@class)
                            .GetMethodNode(@class)
                            .GetFileNode(@class)
                            .GetProperty(@class)
                            .GetClass(@class,compilation);
            return @class;
        }
        /// <summary>
        /// 获取Using
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="class"></param>
        /// <returns></returns>
        public static ClassDeclarationSyntax GetUsing(this ClassDeclarationSyntax classDeclaration, ClassInfo @class)
        {
            var collector = new ClassUsingCollector();
            collector.Visit(classDeclaration);
            @class.Usings = collector.Usings.Select(x => new UsingInfo { DefName = x.Name?.ToString() }).ToList();
            return classDeclaration;
        }




        /// <summary>
        /// 获取继承的接口和类
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="class"></param>
        public static ClassDeclarationSyntax GetBaseClassNode(this ClassDeclarationSyntax classDeclaration, ClassInfo @class)
        {
            if (classDeclaration.BaseList != null)
            {
                //var inherits = classDeclaration.BaseList?.Types.Where(t => t.IsKind(SyntaxKind.SimpleBaseType)).Select(t => ((SimpleBaseTypeSyntax)t).Type);
                var inherits = classDeclaration.BaseList.Types;
                @class.Inherits = inherits.Select(x => new InheritInfo { DefName = x.Type.ToString() }).ToList();
            }
            return classDeclaration;
        }

        /// <summary>
        /// 获取特性节点信息
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="class"></param>
        private static ClassDeclarationSyntax GetAttributeNode(this ClassDeclarationSyntax classDeclaration, ClassInfo @class)
        {
            var attributes = classDeclaration.AttributeLists.SelectMany(attributeList => attributeList.Attributes.ToList());
            foreach (var attribute in attributes)
            {
                //获取特性名称
                var attributeName = attribute.Name is IdentifierNameSyntax identifierNameSyntax ? identifierNameSyntax.Identifier.Text : attribute.Name.ToString();
                //获取特性参数
                var attributeArgs = attribute.ArgumentList?.Arguments.Select(arg => new AttributeInfo.Parameter()
                {
                    DefName = arg.Expression.ToString().Trim('\"'),
                    Type = arg.GetType().Name,
                    //Type = GetClassParameterType(compilation, classDeclaration, arg).ToString()
                }).ToList();
                var @attributeInfo = AttributeInfo.CrteateAttribute(attributeName, attributeArgs);
                @class.Attributes.Add(@attributeInfo);
            }
            return classDeclaration;
        }

        /// <summary>
        /// 获取属性信息
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="class"></param>
        private static ClassDeclarationSyntax GetProperty(this ClassDeclarationSyntax classDeclaration, ClassInfo @class)
        {
            // 获取类中的属性
            List<PropertyDeclarationSyntax> properties = classDeclaration.Members.OfType<PropertyDeclarationSyntax>().ToList();
            foreach (var property in properties)
            {
                var propertyInfo = new PropertyInfo()
                {
                    Modifier = string.Join(" ", property.Modifiers),
                    DefName = property.Identifier.Text,
                    Type = property.Type.ToString(),
                };
                @class.Propertys.Add(propertyInfo);
            }
            return classDeclaration;
        }

        /// <summary>
        /// 获取字段信息
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="class"></param>
        private static ClassDeclarationSyntax GetFileNode(this ClassDeclarationSyntax classDeclaration, ClassInfo @class)
        {
            // 获取类中的字段
            List<FieldDeclarationSyntax> fields = classDeclaration.Members.OfType<FieldDeclarationSyntax>().ToList();
            foreach (var field in fields)
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    var filed = new FiledInfo()
                    {
                        Modifier = string.Join(" ", field.Modifiers),
                        DefName = variable.Identifier.Text,
                        Type = field.Declaration.Type.ToString(),
                    };
                    @class.Fileds.Add(filed);
                }
            }
            return classDeclaration;
        }

        /// <summary>
        /// 获取class方法元素节点
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="class"></param>
        private static ClassDeclarationSyntax GetMethodNode(this ClassDeclarationSyntax classDeclaration, ClassInfo @class)
        {
            // 获取类的方法
            var methods = classDeclaration.Members.OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var ignoreAttribute = false;
                if (method.AttributeLists.Any())
                {
                    var attributeNames = method.AttributeLists.SelectMany(attributeList =>
                                          attributeList.Attributes.Select(attribute =>
                                              attribute.Name is IdentifierNameSyntax identifierNameSyntax
                                                  ? identifierNameSyntax.Identifier.Text
                                                  : attribute.Name.ToString()));
                    ignoreAttribute = attributeNames.Contains(IgnoreAttributeName);
                }
                ///存在忽略标记，则不记录
                if (ignoreAttribute)
                    continue;
                var parameters = method.ParameterList?.Parameters
                                       .Where(parameter => parameter.Kind() == SyntaxKind.Parameter)
                                       .Select(parameter => new MethodInfo.Parameter()
                                       {
                                           DefName = parameter.Identifier.ToString(),
                                           Type = parameter?.Type?.ToString() ?? string.Empty,
                                           //Type = GetClassParameterType(compilation, classDeclaration, parameter)?.ToString() ?? string.Empty
                                       }).ToList();
                var meth = new MethodInfo()
                {
                    DefName = method.Identifier.ToString(),
                    Modifier = string.Join(" ", method.Modifiers),
                    Type = method.ReturnType.ToString(),
                    Parameters = parameters,
                    Remarks = GetRemarks(method)
                };
                @class.Methods.Add(meth);
            }
            return classDeclaration;
        }

        /// <summary>
        /// 获取参数类型
        /// </summary>
        /// <param name="compilation"></param>
        /// <param name="classDeclaration"></param>
        /// <param name="syntaxNode"></param>
        /// <returns></returns>
        public static ITypeSymbol? GetClassParameterType(Compilation compilation, ClassDeclarationSyntax classDeclaration, CSharpSyntaxNode syntaxNode)
        {

            // 获取类所在语法树
            SyntaxTree syntaxTree = compilation.SyntaxTrees.FirstOrDefault(tree =>
            tree.GetRoot().DescendantNodes().Contains(classDeclaration));
            TypeInfo? parameterSymbol = compilation.GetSemanticModel(syntaxTree).GetTypeInfo(syntaxNode);
            ITypeSymbol? parameterTypeSymbol = parameterSymbol?.Type;
            return parameterTypeSymbol;
        }

        /// <summary>
        /// 获取类文件的路劲
        /// </summary>
        /// <param name="context"></param>
        /// <param name="classDeclaration"></param>
        /// <returns></returns>
        private static string CetClassPath(Compilation compilation, ClassDeclarationSyntax classDeclaration)
        {
            // 获取类所在语法树
            var syntaxTree = compilation.SyntaxTrees
                .FirstOrDefault(tree => tree.GetRoot().DescendantNodes().Contains(classDeclaration));
            // 获取类的目录
            return string.IsNullOrEmpty(syntaxTree.FilePath) ? string.Empty : Path.GetDirectoryName(syntaxTree.FilePath);
        }


        /// <summary>
        /// 获取元素节点的备注
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <returns></returns>
        private static List<string> GetRemarks(CSharpSyntaxNode cSharpSyntax)
        {

            if (cSharpSyntax == null)
                return new List<string>();
            // 获取声明的所有注释
            return cSharpSyntax.GetLeadingTrivia()
                               .Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                                                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                                                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                               .Select(trivia =>
                               {
                                   var remark = trivia.ToString();
                                   return remark.StartsWith("///") || remark.StartsWith("//") ? remark : "///" + remark; ///处理特殊情况，去除掉了//或者///
                               }).ToList();
        }

        /// <summary>
        /// 获取Class
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="class"></param>
        /// <returns></returns>
        public static ClassDeclarationSyntax GetClass(this ClassDeclarationSyntax classDeclaration, ClassInfo @class, Compilation compilation)
        {
            var classDeclarations = classDeclaration.DescendantNodes().OfType<ClassDeclarationSyntax>();
            @class.ClassInfos = classDeclarations.Select(x => ClassConvert(x,compilation)).ToList();
            return classDeclaration;
        }

        /// <summary>
        /// 获取Class节点中的using
        /// </summary>
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
                    else if (parent is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
                    {
                        // 支持 C# 10+ 文件范围命名空间
                        Usings.AddRange(fileScopedNamespace.Usings);
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
