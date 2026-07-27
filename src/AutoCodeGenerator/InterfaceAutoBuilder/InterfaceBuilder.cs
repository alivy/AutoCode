using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace AutoCode.SourceGenerator.InterfaceAutoBuilder
{
    public class InterfaceBuilder
    {

        /// <summary>
        /// 构建接口字符串
        /// </summary>
        /// <param name="baseClassInfo"></param>
        /// <param name="publicMethods"></param>
        /// <returns></returns>
        public static string BuilderInterfaceStr(BaseCSInfo baseClassInfo,
              IEnumerable<MethodDeclarationSyntax> publicMethods)
        {
            StringBuilder strBuilder = new StringBuilder();
            IEnumerable<UsingDirectiveSyntax> usingDirectives = baseClassInfo.UsingDirectives;
            NamespaceDeclarationSyntax nameSpace = baseClassInfo.NamespaceDeclaration;
            ClassDeclarationSyntax classTo = baseClassInfo.ClassDeclarations;
            BaseCSInfo.InterfaceAttributeInfo InterfaceInfo = baseClassInfo.InterfaceInfo;
            var interfaceName = (InterfaceInfo != null && !string.IsNullOrEmpty(InterfaceInfo.InterfaceName)) ?
                InterfaceInfo.InterfaceName : $"I{classTo.Identifier}";

            var usingStrs = new HashSet<string>();
            foreach (UsingDirectiveSyntax usingDirective in usingDirectives)
            {
                var usingName = usingDirective.Name.ToString();
                if (!usingName.Contains("global::") && !usingStrs.Contains(usingName))
                {
                    usingStrs.Add(usingName);
                }
            }
            foreach (var usingStr in usingStrs)
            {
                strBuilder.AppendLine($"using {usingStr};");
            }
            strBuilder.AppendLine($"namespace {nameSpace.Name.GetText()}");
            strBuilder.AppendLine("{");
            strBuilder.AppendLine($"\tpublic  interface {interfaceName}");
            strBuilder.AppendLine("\t{");
            if (publicMethods != null)
            {
                foreach (MethodDeclarationSyntax method in publicMethods)
                {
                    // 输出方法参数
                    string parameterStr = string.Empty;
                    var parameters = method.ParameterList?.Parameters
                         .Where(parameter => parameter.Kind() == SyntaxKind.Parameter)
                         .Select(parameter => parameter);
                    if (parameters != null)
                    {
                        parameterStr = string.Join(", ", parameters.Select(parameter => $"{parameter.Type} {parameter.Identifier}"));
                    }

                    strBuilder.AppendLine($"\t\t public  {method.ReturnType} {method.Identifier}({(string.IsNullOrWhiteSpace(parameterStr) ? "" : parameterStr)}); ");
                }
            }
            strBuilder.AppendLine("\t}");
            strBuilder.AppendLine("}");
            return strBuilder.ToString();
        }
    }
}
