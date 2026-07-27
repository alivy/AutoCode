using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AutoCode.SourceGenerator.InterfaceAutoBuilder
{
    /// <summary>
    /// 接口生成器
    /// </summary>
    [Generator]
    public class InterfaceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            // 自定义一个语法接收器的工厂，然后通过该工厂向已有的class类中添加新方法
            context.RegisterForSyntaxNotifications(() => new InterfaceSyntaxReceiver());
        }


        public void Execute(GeneratorExecutionContext context)
        {
            //Debugger.Launch();
            // 获得创建的语法接收器，然后通过context进行操作
            InterfaceSyntaxReceiver syntaxReceiver = (InterfaceSyntaxReceiver)context.SyntaxReceiver;
            //判断是否有这个类
            if (syntaxReceiver == null || !syntaxReceiver.BaseClassInfos.Any()) return;
            foreach (BaseCSInfo classDeclaration in syntaxReceiver.BaseClassInfos)
            {
                //var obj = JsonConvert.SerializeObject(classDeclaration.InterfaceInfo, Formatting.Indented);
                var publicMethods = syntaxReceiver.GetClassMethods(syntaxReceiver, classDeclaration.ClassDeclarations);
                var strBuilder = InterfaceBuilder.BuilderInterfaceStr(classDeclaration, publicMethods);
                SourceText sourceText = SourceText.From(strBuilder, Encoding.UTF8);
                var interfaceName = classDeclaration.InterfaceInfo.InterfaceName;
                var path = classDeclaration.InterfaceInfo.InterfacePath;
                if (!string.IsNullOrEmpty(interfaceName) && !string.IsNullOrEmpty(path))
                {
                    string filePath = string.Empty;
                    if (path == "/")
                    {
                        var classPath = CetClassPath(context, classDeclaration.ClassDeclarations);
                        if (!string.IsNullOrEmpty(classPath))
                            filePath = $"{classPath}\\{interfaceName}.cs";
                    }
                    else
                    {
                        filePath = $"{path}\\{interfaceName}.cs";
                    }
                    if (!string.IsNullOrEmpty(filePath) && !File.Exists(filePath))
                    {
                        File.WriteAllText(filePath, strBuilder);
                    }
                }
                if (!string.IsNullOrEmpty(interfaceName) && string.IsNullOrEmpty(path))
                {
                    context.AddSource($"{interfaceName}.cs", sourceText);
                    GeneratorDiagnostic(context, strBuilder);
                }
                if (string.IsNullOrEmpty(interfaceName))
                {
                    context.AddSource($"I{classDeclaration.ClassDeclarations.Identifier}.cs", sourceText);
                    GeneratorDiagnostic(context, sourceText.ToString());
                }

                //File.WriteAllText(@"F:\AI\obj.txt", obj);
            }
        }

        private static void GeneratorDiagnostic(GeneratorExecutionContext context, string msg)
        {
            var diagnosticDescriptor = new DiagnosticDescriptor(
              id: "IG001",
              title: "Information",
              messageFormat: $"Source has been added,msg:{msg}",
              category: "InterfaceGenerator",
              defaultSeverity: DiagnosticSeverity.Warning,
              isEnabledByDefault: true);

            var diagnostic = Diagnostic.Create(diagnosticDescriptor, Location.None);
            context.ReportDiagnostic(diagnostic);
        }

        /// <summary>
        /// 获取类文件的路劲
        /// </summary>
        /// <param name="context"></param>
        /// <param name="classDeclaration"></param>
        /// <returns></returns>
        private string CetClassPath(GeneratorExecutionContext context, ClassDeclarationSyntax classDeclaration)
        {
            // 获取类所在语法树
            var syntaxTree = context.Compilation.SyntaxTrees
                .FirstOrDefault(tree => tree.GetRoot().DescendantNodes().Contains(classDeclaration));
            // 获取类的目录
            return string.IsNullOrEmpty(syntaxTree.FilePath) ? string.Empty : Path.GetDirectoryName(syntaxTree.FilePath);
        }


        private string GetPath()
        {
            // 获取调用方程序集的信息
            Assembly assembly = Assembly.GetCallingAssembly();
            // 获取程序集的文件路径
            string assemblyPath = assembly.Location;
            // 在某些情况下，你可能需要获取程序集所在的目录而不是完整路径
            string assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            return assemblyDirectory;
        }
    }
}
