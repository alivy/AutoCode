using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using AutoCode.Engine.Config;
using AutoCode.Engine.Diagnostics;
using AutoCode.Engine.Pipeline;
using AutoCode.Engine.Plugin;

namespace AutoCode.Engine.Roslyn
{
    /// <summary>
    /// 增量生成器基类 - 桥接 Roslyn IIncrementalGenerator 与 AutoCode Engine Pipeline。
    /// 继承此类即可快速创建基于引擎的生成器，自动获得：
    /// - 配置系统接入
    /// - 诊断收集
    /// - 插件管线执行
    /// - 约定推断
    /// </summary>
    public abstract class AutoCodeIncrementalGenerator : IIncrementalGenerator
    {
        /// <summary>此生成器关联的特性全名</summary>
        protected abstract string TargetAttributeFullName { get; }

        /// <summary>特性短名（用于语法级快速匹配）</summary>
        protected abstract string TargetAttributeShortName { get; }

        /// <summary>生成器名称（用于诊断和日志）</summary>
        protected abstract string GeneratorName { get; }

        /// <summary>
        /// 为单个类型符号生成代码。子类实现此方法。
        /// </summary>
        /// <param name="typeSymbol">标记了目标特性的类型</param>
        /// <param name="attributeData">特性数据</param>
        /// <param name="context">生成上下文</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>生成的文件列表</returns>
        protected abstract IEnumerable<GeneratedFile> GenerateForType(
            INamedTypeSymbol typeSymbol,
            AttributeData attributeData,
            GenerationContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// 可选：是否启用约定推断模式（无需特性标记也能触发）
        /// </summary>
        protected virtual bool EnableConventionMode => false;

        /// <summary>
        /// 可选：约定推断判断逻辑
        /// </summary>
        protected virtual bool MatchesConvention(INamedTypeSymbol typeSymbol, GenerationContext context)
            => false;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 配置提供器
            var configProvider = context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
            {
                return LoadConfig(provider);
            });

            // 查找标记了目标特性的类
            var typeDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => IsTargetClass(node),
                    transform: (ctx, ct) => ExtractTypeInfo(ctx, ct))
                .Where(t => t != null);

            // 组合
            var combined = typeDeclarations.Combine(configProvider);

            // 注册输出
            context.RegisterSourceOutput(combined, (spc, pair) =>
            {
                var typeInfo = pair.Left!;
                var config = pair.Right;

                ExecuteGeneration(spc, typeInfo, config);
            });
        }

        /// <summary>
        /// 语法级快速判断
        /// </summary>
        private bool IsTargetClass(SyntaxNode node)
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
                    return name == TargetAttributeShortName
                        || name == TargetAttributeShortName + "Attribute";
                });
        }

        /// <summary>
        /// 语义级提取类型信息
        /// </summary>
        private TypeInfo? ExtractTypeInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
            if (typeSymbol == null)
                return null;

            var attr = FindTargetAttribute(typeSymbol);
            if (attr == null)
                return null;

            return new TypeInfo
            {
                TypeSymbol = typeSymbol,
                AttributeData = attr,
                Location = classDecl.GetLocation()
            };
        }

        /// <summary>
        /// 执行生成逻辑
        /// </summary>
        private void ExecuteGeneration(SourceProductionContext spc, TypeInfo typeInfo, IAutoCodeConfig config)
        {
            var diagnostics = new DiagnosticCollector();

            // 使用轻量上下文（在 RegisterSourceOutput 中无法获取完整 Compilation）
            var context = new LightweightGenerationContext(config, diagnostics, typeInfo.TypeSymbol);

            try
            {
                var files = GenerateForType(
                    typeInfo.TypeSymbol,
                    typeInfo.AttributeData,
                    context,
                    spc.CancellationToken);

                foreach (var file in files)
                {
                    spc.AddSource(file.FileName, SourceText.From(file.Content, Encoding.UTF8));
                }
            }
            catch (Exception ex)
            {
                diagnostics.ReportError(
                    DiagnosticIds.PluginExecutionFailed,
                    $"{GeneratorName}: 生成失败 - {ex.Message}",
                    typeInfo.Location);
            }

            // 报告所有诊断
            foreach (var entry in diagnostics.GetAll())
            {
                spc.ReportDiagnostic(entry.ToRoslynDiagnostic());
            }
        }

        /// <summary>
        /// 查找目标特性
        /// </summary>
        protected AttributeData? FindTargetAttribute(INamedTypeSymbol typeSymbol)
        {
            foreach (var attr in typeSymbol.GetAttributes())
            {
                var fullName = attr.AttributeClass?.ToDisplayString();
                if (fullName == TargetAttributeFullName
                    || attr.AttributeClass?.Name == TargetAttributeShortName + "Attribute"
                    || attr.AttributeClass?.Name == TargetAttributeShortName)
                {
                    return attr;
                }
            }
            return null;
        }

        /// <summary>
        /// 从 MSBuild 选项加载配置
        /// </summary>
        private static IAutoCodeConfig LoadConfig(AnalyzerConfigOptionsProvider provider)
        {
            var config = new AutoCodeConfig();

            // 尝试读取通用配置
            if (provider.GlobalOptions.TryGetValue("build_property.AutoCode_InterfacePrefix", out var prefix))
                config.Set("interface.prefix", prefix);
            if (provider.GlobalOptions.TryGetValue("build_property.AutoCode_MapMethodName", out var mapMethod))
                config.Set("mapper.method.name", mapMethod);
            if (provider.GlobalOptions.TryGetValue("build_property.AutoCode_GenerateNullable", out var nullable))
                config.Set("generate.nullable", nullable);
            if (provider.GlobalOptions.TryGetValue("build_property.AutoCode_EnableDiagnostics", out var diag))
                config.Set("diagnostics.enabled", diag);

            return config;
        }

        private sealed class TypeInfo
        {
            public INamedTypeSymbol TypeSymbol { get; set; } = null!;
            public AttributeData AttributeData { get; set; } = null!;
            public Location? Location { get; set; }
        }
    }

    /// <summary>
    /// 轻量级生成上下文 - 在 RegisterSourceOutput 中使用（无法获取完整 Compilation）
    /// </summary>
    internal sealed class LightweightGenerationContext : GenerationContext
    {
        public LightweightGenerationContext(
            IAutoCodeConfig config,
            IDiagnosticCollector diagnostics,
            INamedTypeSymbol currentType)
            : base(
                compilation: null!,  // 在此模式下不可用
                config: config,
                diagnostics: diagnostics)
        {
            CurrentTypeSymbol = currentType;
        }
    }
}
