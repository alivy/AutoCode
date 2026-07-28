using AutoCode.Map.Diagnostics;
using AutoCode.Map.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AutoCode.Map
{
    /// <summary>
    /// 对象映射代码生成器 - 基于 IIncrementalGenerator
    /// 为标记 [Mapper] 的类自动生成 CopyTo 扩展方法
    /// </summary>
    [Generator]
    public class MapperGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// Mapper 特性的完全限定元数据名称
        /// </summary>
        private const string MapperAttributeFullName =
            "AutoCode.Model.AutoMapperModel.MapperAttribute";

        /// <summary>
        /// 初始化增量生成器
        /// </summary>
        /// <param name="context">增量生成器初始化上下文</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 报告语言版本诊断
            var compilationDiagnostics = context.CompilationProvider.SelectMany(
                (compilation, _) => BuildCompilationDiagnostics(compilation));
            context.ReportDiagnostics(compilationDiagnostics);

            // 读取 MSBuild 配置: AutoCode_MapMethodName
            var mapMethodName = context.AnalyzerConfigOptionsProvider
                .Select((provider, _) =>
                {
                    provider.GlobalOptions.TryGetValue("build_property.AutoCode_MapMethodName", out var name);
                    return name ?? "CopyTo";
                });

            // 使用 CreateSyntaxProvider 查找标记了 [Mapper] 的类
            var mapperSources = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => IsClassWithMapperAttribute(node),
                    transform: (ctx, _) => GenerateMapperSource(ctx))
                .Combine(mapMethodName)
                .Select((pair, _) => ApplyMethodName(pair.Left, pair.Right))
                .WhereNotNull();

            context.EmitMapperSource(mapperSources);
        }

        /// <summary>
        /// 将配置的方法名应用到生成的源代码
        /// </summary>
        private static MapperSource? ApplyMethodName(MapperSource? source, string methodName)
        {
            if (source == null) return null;
            if (methodName == "CopyTo") return source;
            return new MapperSource
            {
                FileName = source.FileName,
                SourceText = source.SourceText.Replace("CopyTo", methodName)
            };
        }

        /// <summary>
        /// 判断语法节点是否为标记了 [Mapper] 的类声明
        /// </summary>
        /// <param name="node">语法节点</param>
        /// <returns>是否匹配</returns>
        private static bool IsClassWithMapperAttribute(SyntaxNode node)
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
                    return name == "Mapper" || name == "MapperAttribute";
                });
        }

        /// <summary>
        /// 生成映射器源代码
        /// </summary>
        /// <param name="ctx">语法上下文</param>
        /// <returns>映射器源代码信息</returns>
        private static MapperSource? GenerateMapperSource(GeneratorSyntaxContext ctx)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol == null)
                return null;

            var namespaceName = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var className = classSymbol.Name;
            var mapperClassName = $"{className}Mapper";

            var sb = new StringBuilder();

            // 生成 using 指令
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();

            // 生成命名空间
            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            var indent = string.IsNullOrEmpty(namespaceName) ? "" : "    ";

            // 生成映射器类
            sb.AppendLine($"{indent}public static class {mapperClassName}");
            sb.AppendLine($"{indent}{{");

            // 生成 CopyTo 方法
            sb.AppendLine($"{indent}    /// <summary>");
            sb.AppendLine($"{indent}    /// 将源对象的属性复制到目标对象");
            sb.AppendLine($"{indent}    /// </summary>");
            sb.AppendLine($"{indent}    /// <param name=\"source\">源对象</param>");
            sb.AppendLine($"{indent}    /// <param name=\"target\">目标对象</param>");
            sb.AppendLine($"{indent}    public static void CopyTo(this {className} source, {className} target)");
            sb.AppendLine($"{indent}    {{");

            // 为每个属性生成复制代码
            foreach (var property in classSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer)
                    continue;

                if (property.SetMethod == null)
                    continue;

                GeneratePropertyMapping(sb, $"{indent}        ", property);
            }

            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return new MapperSource
            {
                FileName = $"{mapperClassName}.g.cs",
                SourceText = sb.ToString()
            };
        }

        /// <summary>
        /// 为单个属性生成映射代码
        /// </summary>
        /// <param name="sb">字符串构建器</param>
        /// <param name="indent">缩进</param>
        /// <param name="property">属性符号</param>
        private static void GeneratePropertyMapping(StringBuilder sb, string indent, IPropertySymbol property)
        {
            var propName = property.Name;
            var propType = property.Type;

            if (IsSimpleType(propType))
            {
                sb.AppendLine($"{indent}target.{propName} = source.{propName};");
            }
            else if (IsListType(propType, out var listItemType))
            {
                var listItemName = listItemType?.Name ?? "object";
                // 简单类型列表元素直接赋值，复杂类型使用列表构造器浅拷贝
                if (listItemType != null && IsSimpleType(listItemType))
                {
                    sb.AppendLine($"{indent}if (source.{propName} != null)");
                    sb.AppendLine($"{indent}{{");
                    sb.AppendLine($"{indent}    target.{propName} = new List<{listItemName}>();");
                    sb.AppendLine($"{indent}    foreach (var item in source.{propName})");
                    sb.AppendLine($"{indent}    {{");
                    sb.AppendLine($"{indent}        target.{propName}.Add(item);");
                    sb.AppendLine($"{indent}    }}");
                    sb.AppendLine($"{indent}}}");
                }
                else
                {
                    // 复杂类型使用列表构造器避免跨程序集 CopyTo 缺失
                    sb.AppendLine($"{indent}if (source.{propName} != null)");
                    sb.AppendLine($"{indent}{{");
                    sb.AppendLine($"{indent}    target.{propName} = new List<{listItemName}>(source.{propName});");
                    sb.AppendLine($"{indent}}}");
                }
            }
            else if (propType is INamedTypeSymbol namedType && !namedType.IsValueType)
            {
                // 复杂引用类型直接赋值，避免跨程序集 CopyTo 缺失
                sb.AppendLine($"{indent}target.{propName} = source.{propName};");
            }
        }

        /// <summary>
        /// 判断是否为简单类型
        /// </summary>
        /// <param name="typeSymbol">类型符号</param>
        /// <returns>是否为简单类型</returns>
        private static bool IsSimpleType(ITypeSymbol typeSymbol)
        {
            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_Char:
                case SpecialType.System_DateTime:
                case SpecialType.System_Decimal:
                case SpecialType.System_Double:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_SByte:
                case SpecialType.System_Single:
                case SpecialType.System_String:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 判断是否为 List 类型并获取元素类型
        /// </summary>
        /// <param name="typeSymbol">类型符号</param>
        /// <param name="listItemType">列表元素类型</param>
        /// <returns>是否为 List 类型</returns>
        private static bool IsListType(ITypeSymbol typeSymbol, out INamedTypeSymbol? listItemType)
        {
            listItemType = null;

            if (typeSymbol is not INamedTypeSymbol namedType)
                return false;

            if (namedType.IsGenericType && namedType.Name == "List" && namedType.TypeArguments.Length == 1)
            {
                listItemType = namedType.TypeArguments[0] as INamedTypeSymbol;
                return listItemType != null;
            }

            return false;
        }

        /// <summary>
        /// 构建编译诊断信息
        /// </summary>
        /// <param name="compilation">编译对象</param>
        /// <returns>诊断信息列表</returns>
        private static IEnumerable<Diagnostic> BuildCompilationDiagnostics(Compilation compilation)
        {
            if (compilation is CSharpCompilation
                {
                    LanguageVersion: < LanguageVersion.CSharp9
                } cSharpCompilation)
            {
                yield return Diagnostic.Create(DiagnosticDescriptors.LanguageVersionNotSupported,
                    null,
                    cSharpCompilation.LanguageVersion.ToString(),
                    LanguageVersion.CSharp9.ToString()
                );
            }
        }
    }
}
