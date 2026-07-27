using AutoCode.DotTemplate.SourceGenerator.Extend;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static AutoCode.DotTemplate.SourceGenerator.CSData;

namespace AutoCode.XmlTemplate.SourceGenerator
{
    /// <summary>
    /// 基于 DotLiquid 模板的代码生成器 - 使用 IIncrementalGenerator
    /// 支持 AdditionalFiles 和文件读取两种模板来源
    /// </summary>
    [Generator]
    public class DotTemplateGenerator : IIncrementalGenerator
    {
        private const string DotTemplateAttributeFullName = "AutoCode.Model.DotTemplateAttribute";
        private const string SourceFileMarker = "$Source.cs";
        private static readonly HashSet<string> TemplateSuffixes = new HashSet<string> { ".dot" };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 使用 CreateSyntaxProvider 查找标记了 [DotTemplate] 的类
            var templateResults = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => IsClassWithDotTemplate(node),
                    transform: (ctx, _) => ProcessTemplate(ctx))
                .Where(result => result != null)!;

            // 合并 AdditionalFiles 和语法提供者
            context.RegisterSourceOutput(templateResults, (spc, result) =>
            {
                foreach (var diag in result.Diagnostics)
                    spc.ReportDiagnostic(diag);

                if (!string.IsNullOrEmpty(result.SourceText))
                    spc.AddSource(result.FileName, SourceText.From(result.SourceText, Encoding.UTF8));
            });
        }

        /// <summary>
        /// 判断语法节点是否为标记了 [DotTemplate] 的类声明
        /// </summary>
        private static bool IsClassWithDotTemplate(SyntaxNode node)
        {
            if (node is not ClassDeclarationSyntax classDecl)
                return false;

            return classDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr =>
                {
                    var name = attr.Name is IdentifierNameSyntax id
                        ? id.Identifier.Text
                        : attr.Name.ToString();
                    return name == "DotTemplate" || name == "DotTemplateAttribute";
                });
        }

#pragma warning disable RS1035 // 模板生成需要读取文件
        /// <summary>
        /// 处理模板生成
        /// </summary>
        private static TemplateResult ProcessTemplate(GeneratorSyntaxContext ctx)
        {
            var result = new TemplateResult();

            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return result;

            var compilation = ctx.SemanticModel.Compilation;
            var classInfo = classDecl.ClassConvert(compilation);

            // 设置命名空间
            var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol != null)
            {
                classInfo.NameSpace = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            }

            // 获取源文件目录
            var sourceFilePath = classDecl.SyntaxTree.FilePath;
            var sourceDir = string.IsNullOrEmpty(sourceFilePath)
                ? string.Empty
                : Path.GetDirectoryName(sourceFilePath);

            // 从语法节点提取特性参数
            var dotTemplateAttrs = classDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Where(a =>
                {
                    var name = a.Name is IdentifierNameSyntax id
                        ? id.Identifier.Text
                        : a.Name.ToString();
                    return name == "DotTemplate" || name == "DotTemplateAttribute";
                });

            // 也从符号获取特性数据
            var symbolAttrs = classSymbol?.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString() == DotTemplateAttributeFullName
                         || a.AttributeClass?.Name == "DotTemplateAttribute")
                .ToList() ?? new List<AttributeData>();

            foreach (var attrSyntax in dotTemplateAttrs)
            {
                // 尝试从符号特性数据获取参数
                var matchingSymbolAttr = symbolAttrs.FirstOrDefault(a =>
                    a.ApplicationSyntaxReference?.Span.Start == attrSyntax.SpanStart);

                if (matchingSymbolAttr != null)
                {
                    ProcessAttribute(matchingSymbolAttr, classInfo, sourceDir, result);
                }
                else
                {
                    // 从语法节点直接提取参数
                    ProcessAttributeFromSyntax(attrSyntax, classInfo, sourceDir, result);
                }
            }

            return result;
        }

        private static void ProcessAttribute(
            AttributeData attribute,
            ClassInfo classInfo,
            string sourceDir,
            TemplateResult result)
        {
            var args = attribute.ConstructorArguments;
            if (args.Length == 0)
                return;

            // 处理 bool 构造函数
            if (args[0].Value is bool)
            {
                if (args.Length > 1 && args[1].Value is string fileName)
                {
                    result.FileName = EnsureCsExtension(DotHelp.DotLiquidConvert(fileName, classInfo));
                    result.SourceText = "// DotTemplate: system file generation not supported in incremental mode";
                }
                return;
            }

            // 处理 string 构造函数
            if (args[0].Value is not string templatePath)
                return;

            ProcessTemplatePath(templatePath, sourceDir, classInfo, args.ToArray(), result);
        }

        private static void ProcessAttributeFromSyntax(
            AttributeSyntax attrSyntax,
            ClassInfo classInfo,
            string sourceDir,
            TemplateResult result)
        {
            var args = attrSyntax.ArgumentList?.Arguments ?? new SeparatedSyntaxList<AttributeArgumentSyntax>();
            if (args.Count == 0)
                return;

            var firstArgExpr = args[0].Expression.ToString().Trim('"').Replace("@\"", "");
            ProcessTemplatePath(firstArgExpr, sourceDir, classInfo, null, result, args);
        }

        private static void ProcessTemplatePath(
            string templatePath,
            string sourceDir,
            ClassInfo classInfo,
            System.Collections.Generic.IReadOnlyList<Microsoft.CodeAnalysis.TypedConstant>? constructorArgs,
            TemplateResult result,
            SeparatedSyntaxList<AttributeArgumentSyntax>? syntaxArgs = null)
        {
            var fullPath = ResolvePath(templatePath, sourceDir);
            if (!File.Exists(fullPath))
            {
                result.Diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor("SG11002", "模板文件未找到",
                        $"模板文件未找到: {fullPath}",
                        "AutoCode.DotTemplate.SourceGenerator",
                        DiagnosticSeverity.Warning, true),
                    Location.None));
                return;
            }

            if (!TemplateSuffixes.Contains(Path.GetExtension(fullPath)))
                return;

            string templateContent;
            try
            {
                templateContent = File.ReadAllText(fullPath);
            }
            catch (System.Exception ex)
            {
                result.Diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor("SG11001", "模板文件读取失败",
                        $"读取模板文件失败: {ex.Message}",
                        "AutoCode.DotTemplate.SourceGenerator",
                        DiagnosticSeverity.Error, true),
                    Location.None));
                return;
            }

            string rendered;
            try
            {
                rendered = DotHelp.DotLiquidConvert(templateContent, classInfo);
            }
            catch (System.Exception ex)
            {
                result.Diagnostics.Add(Diagnostic.Create(
                    new DiagnosticDescriptor("SG11001", "模板渲染失败",
                        $"DotLiquid 模板渲染失败: {ex.Message}",
                        "AutoCode.DotTemplate.SourceGenerator",
                        DiagnosticSeverity.Error, true),
                    Location.None));
                return;
            }

            // 确定输出文件名
            string outputFileName = $"{classInfo.DefName}Copy.g.cs";

            if (constructorArgs != null)
            {
                if (constructorArgs.Count > 2
                    && constructorArgs[1].Value is string secondParam
                    && constructorArgs[2].Value is string thirdParam)
                {
                    outputFileName = EnsureCsExtension(DotHelp.DotLiquidConvert(thirdParam, classInfo));
                }
                else if (constructorArgs.Count > 1
                    && constructorArgs[1].Value is string outputPath
                    && outputPath == SourceFileMarker)
                {
                    outputFileName = $"{classInfo.DefName}Copy.g.cs";
                }
            }
            else if (syntaxArgs.HasValue && syntaxArgs.Value.Count > 2)
            {
                var thirdParam = syntaxArgs.Value[2].Expression.ToString().Trim('"');
                outputFileName = EnsureCsExtension(DotHelp.DotLiquidConvert(thirdParam, classInfo));
            }

            result.FileName = outputFileName;
            result.SourceText = rendered;
        }
#pragma warning restore RS1035

        private static string ResolvePath(string path, string baseDir)
        {
            if (Path.IsPathRooted(path))
                return path;
            if (string.IsNullOrEmpty(baseDir))
                return path;
            return Path.GetFullPath(Path.Combine(baseDir, path));
        }

        private static string EnsureCsExtension(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Generated.g.cs";
            return Path.HasExtension(fileName) ? fileName : fileName + ".cs";
        }
    }

    internal class TemplateResult
    {
        public string SourceText { get; set; } = string.Empty;
        public string FileName { get; set; } = "Generated.g.cs";
        public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();
    }
}
