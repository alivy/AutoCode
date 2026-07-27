using AutoCode.DotTemplate.SourceGenerator;
using AutoCode.DotTemplate.SourceGenerator.Extend;
using DotLiquid;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AutoCode.XmlTemplate.SourceGenerator
{
    /// <summary>
    /// 根据模版生成代码
    /// </summary>
    [Generator]
    public class DotTemplateGenerator : ISourceGenerator
    {
        /// <summary>
        /// 生成内置源文件标记
        /// </summary>
        public const string SourceFile = "$Source.cs";

        /// <summary>
        /// 模板后缀
        /// </summary>
        public static HashSet<string> TemplateSuffix = new HashSet<string>() { ".dot" };

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new DotTemplateSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            //Debugger.Launch();
            try
            {
                Compilation compilation = context.Compilation;
                var conSyntaxReceiver = context.SyntaxReceiver;
                if (conSyntaxReceiver == null)
                    return;
                DotTemplateSyntaxReceiver syntaxReceiver = (DotTemplateSyntaxReceiver)conSyntaxReceiver;
                if (syntaxReceiver == null || !syntaxReceiver.csData.ClassSyntaxeList.Any())
                    return;
                foreach (var classDeclaration in syntaxReceiver.csData.ClassInfos(compilation))
                {
                    var csPath = classDeclaration.ClassPath;
                    var attributes = classDeclaration.Attributes.FirstOrDefault(x => x.DefName.Equals(syntaxReceiver.TemplateAttributeName));
                    if (attributes == null)
                        continue;
                    var dotStr = OneParameter(context, classDeclaration, csPath, attributes);
                    if (string.IsNullOrEmpty(dotStr))
                        continue;
                    string newFilePath = TwoParameter(context, classDeclaration, csPath, attributes, dotStr);
                    ThreeParameter(context, classDeclaration, attributes, dotStr, newFilePath);
                }
            }
            catch (Exception ex)
            {
                // 发生异常时，将错误信息传递给 GeneratorExecutionContext
                var diagnosticDescriptor = new DiagnosticDescriptor(
                                                          id: DiagnosticIds.SysError,
                                                          title: "Error in Source Generation",
                                                          messageFormat: ex.Message,
                                                          category: "AutoCode.DotTemplate.SourceGenerator",
                                                          defaultSeverity: DiagnosticSeverity.Error,
                                                          isEnabledByDefault: true);
                context.ReportDiagnostic(Diagnostic.Create(diagnosticDescriptor, Location.None));
            }
        }

        /// <summary>
        /// 当Attribute有三个参数时逻辑
        /// 此时指定了模板路径&生成文件路径参数&生成文件名参数
        /// </summary>
        /// <param name="context"></param>
        /// <param name="classDeclaration"></param>
        /// <param name="attributes"></param>
        /// <param name="dotStr"></param>
        /// <param name="newFilePath"></param>
        private static void ThreeParameter(GeneratorExecutionContext context, CSData.ClassInfo classDeclaration, CSData.ClassInfo.AttributeInfo attributes, string dotStr, string newFilePath)
        {
            if (attributes.Parameters.Count > 2 && attributes.Parameters[1] != null && attributes.Parameters[2] != null)
            {
                var newFileName = attributes.Parameters[1].DefName;
                var builderFileName = attributes.Parameters[2].DefName; //获取生成文件名
                                                                        //当确定为来源文件标记字段时，生成内存文件
                if (newFileName == SourceFile && !string.IsNullOrEmpty(builderFileName) && Path.GetExtension(builderFileName) == ".cs")
                {
                    var dotFileName = DotHelp.DotLiquidConvert(builderFileName, classDeclaration);
                    context.AddSource(dotFileName, dotStr);
                    return;
                }
                if (!string.IsNullOrEmpty(builderFileName))
                {
                    var customFileName = DotHelp.DotLiquidConvert(builderFileName, classDeclaration);
                    WriteFile(context, newFilePath, customFileName, dotStr, false);
                }
            }
        }

        /// <summary>
        /// 当Attribute有两个参数时逻辑
        /// 此时指定了模板路径和生成文件路径参数时
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="csPath"></param>
        /// <param name="attributes"></param>
        /// <param name="dotStr"></param>
        /// <returns></returns>
        private string TwoParameter(GeneratorExecutionContext context, CSData.ClassInfo classDeclaration, string csPath, CSData.ClassInfo.AttributeInfo attributes, string dotStr)
        {
            var newFilePath = string.Empty;
            if (attributes.Parameters.Count > 1 && attributes.Parameters[1] != null)
            {
                var newFileParameter = attributes.Parameters[1];//获取新cs存放文件夹路径
                if (newFileParameter != null)
                {
                    var newFileName = FilterSymbol(newFileParameter.DefName);
                    newFilePath = Path.IsPathRooted(newFileName) ? newFileName : PathCombine(csPath, newFileName);
                    if (attributes.Parameters.Count == 2)
                        WriteFile(context, newFilePath, classDeclaration.DefName, dotStr);
                }
            }
            return newFilePath;
        }

        /// <summary>'
        /// 当Attribute只有一个参数时逻辑
        /// 此时且指定模板文件路径处理
        /// 处理Json文件写入
        /// </summary>
        /// <param name="context"></param>
        /// <param name="classDeclaration"></param>
        /// <param name="csPath"></param>
        /// <param name="attributes"></param>
        /// <returns></returns>
        private string OneParameter(GeneratorExecutionContext context, CSData.ClassInfo classDeclaration, string csPath, CSData.ClassInfo.AttributeInfo attributes)
        {
            string dotStr = string.Empty;
            if (attributes.Parameters.Count > 0)
            {
                var templateParameter = attributes.Parameters.FirstOrDefault(); //获取模板文件路径
                if (attributes.Parameters.FirstOrDefault() == null)
                {
                    var diagnosticDescriptor = new DiagnosticDescriptor(
                                                   id: DiagnosticIds.FileError,
                                                   title: "Warning in Source Generation",
                                                   messageFormat: $"获取模板文件路径失败，请检查路径",
                                                   category: "AutoCode.DotTemplate.SourceGenerator",
                                                   defaultSeverity: DiagnosticSeverity.Warning,
                                                   isEnabledByDefault: true);
                    context.ReportDiagnostic(Diagnostic.Create(diagnosticDescriptor, Location.None));
                    return dotStr;

                }
                var templateName = FilterSymbol(templateParameter.DefName);
                var templatePath = Path.IsPathRooted(templateName) ? templateName : PathCombine(csPath, templateName);
                if (!File.Exists(templatePath) && !TemplateSuffix.Contains(Path.GetExtension(templatePath)))
                    return dotStr;
                var csJson = JsonHelp.SerializeObject(classDeclaration);
                WirteJsonFile(classDeclaration, csJson, templatePath, csPath); //添加Json文件，存放模板路径下

                string dotTemplate = File.ReadAllText(templatePath);
                dotStr = DotHelp.DotLiquidConvert(dotTemplate, classDeclaration);
                if (attributes.Parameters.Count == 1)
                    WriteFile(context, templatePath, classDeclaration.DefName, dotStr);
            }
            return dotStr;
        }

        /// <summary>
        /// 写入Josn文件
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="csjson"></param>
        /// <param name="dotTemplate"></param>
        private static void WirteJsonFile(CSData.ClassInfo classDeclaration, string csjson, string dotTemplate, string csPath)
        {
            dotTemplate = Path.GetDirectoryName(dotTemplate);
            var directoryPath = Path.IsPathRooted(dotTemplate) ? dotTemplate : PathCombine(csPath, dotTemplate);
            if (!string.IsNullOrEmpty(csjson))
                File.WriteAllText($"{directoryPath}\\{classDeclaration.DefName}.json", csjson);
        }

        /// <summary>
        /// 组装绝对路径和相对路径
        /// </summary>
        /// <param name="absolutePath"></param>
        /// <param name="relativePath"></param>
        /// <returns></returns>
        public static string PathCombine(string absolutePath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return absolutePath;
            string combinedPath = Path.Combine(absolutePath, relativePath);
            return Path.GetFullPath(combinedPath);
        }



        /// <summary>
        /// 写入文件
        /// </summary>
        /// <param name="path"></param>
        /// <param name="classDefName"></param>
        /// <param name="csjson"></param>
        /// <param name="dotStr"></param>
        /// <param name="isDefaultName">是否使用默认后缀名称</param>
        /// <returns></returns>
        private static void WriteFile(GeneratorExecutionContext context, string path, string classDefName, string dotStr, bool isDefaultSuffixName = true)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            string directoryPath = Path.GetDirectoryName(path);
            if (!Directory.Exists(directoryPath))
            {
                var diagnosticDescriptor = new DiagnosticDescriptor(
                                                     id: DiagnosticIds.FilePathWarn,
                                                     title: "Warning in Source Generation",
                                                     messageFormat: $"获取生成文件路径失败，请检查路径{directoryPath}",
                                                     category: "AutoCode.DotTemplate.SourceGenerator",
                                                     defaultSeverity: DiagnosticSeverity.Warning,
                                                     isEnabledByDefault: true);
                context.ReportDiagnostic(Diagnostic.Create(diagnosticDescriptor, Location.None));
                return;
            }
            if (isDefaultSuffixName)
            {
                File.WriteAllText($"{directoryPath}\\{classDefName}Copy.cs", dotStr);
            }
            else
            {
                if (!Path.HasExtension(classDefName))
                {
                    classDefName += ".cs";  // 如果文件名没有后缀，添加.cs后缀
                }
                File.WriteAllText($"{directoryPath}\\{classDefName}", dotStr);
            }
        }

        /// <summary>
        ///路径数据清洗
        /// </summary>
        /// <returns></returns>
        private string FilterSymbol(string path)
        {
            path = path.Replace("@\"", "");
            return path;
        }
    }
}
