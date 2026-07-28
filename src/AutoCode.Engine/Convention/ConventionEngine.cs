using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace AutoCode.Engine.Convention
{
    /// <summary>
    /// 约定推断结果
    /// </summary>
    public sealed class ConventionMatch
    {
        /// <summary>匹配的规则名称</summary>
        public string RuleName { get; }

        /// <summary>匹配的类型符号</summary>
        public INamedTypeSymbol TypeSymbol { get; }

        /// <summary>建议执行的操作描述</summary>
        public string SuggestedAction { get; }

        /// <summary>建议激活的插件名称</summary>
        public string SuggestedPlugin { get; }

        /// <summary>置信度 (0.0 - 1.0)</summary>
        public double Confidence { get; }

        public ConventionMatch(string ruleName, INamedTypeSymbol typeSymbol,
            string suggestedAction, string suggestedPlugin, double confidence)
        {
            RuleName = ruleName;
            TypeSymbol = typeSymbol;
            SuggestedAction = suggestedAction;
            SuggestedPlugin = suggestedPlugin;
            Confidence = confidence;
        }
    }

    /// <summary>
    /// 约定规则接口 - 每个规则负责检测一种命名/结构模式
    /// </summary>
    public interface IConventionRule
    {
        /// <summary>规则名称</summary>
        string Name { get; }

        /// <summary>规则描述</summary>
        string Description { get; }

        /// <summary>是否启用</summary>
        bool IsEnabled(Config.IAutoCodeConfig config);

        /// <summary>检测类型是否匹配此约定</summary>
        ConventionMatch? Evaluate(INamedTypeSymbol typeSymbol, Pipeline.GenerationContext context);
    }

    /// <summary>
    /// 约定推断引擎 - 管理所有规则并执行推断
    /// </summary>
    public sealed class ConventionEngine
    {
        private readonly List<IConventionRule> _rules = new List<IConventionRule>();

        public ConventionEngine()
        {
            // 注册内置规则
            _rules.Add(new ServiceConventionRule());
            _rules.Add(new DtoConventionRule());
            _rules.Add(new RepositoryConventionRule());
            _rules.Add(new ControllerConventionRule());
        }

        /// <summary>添加自定义规则</summary>
        public ConventionEngine AddRule(IConventionRule rule)
        {
            _rules.Add(rule);
            return this;
        }

        /// <summary>
        /// 对指定类型执行所有约定推断
        /// </summary>
        public IEnumerable<ConventionMatch> Evaluate(INamedTypeSymbol typeSymbol, Pipeline.GenerationContext context)
        {
            foreach (var rule in _rules)
            {
                if (!rule.IsEnabled(context.Config))
                    continue;

                ConventionMatch? match = null;
                try
                {
                    match = rule.Evaluate(typeSymbol, context);
                }
                catch
                {
                    // 规则异常不影响其他规则
                }

                if (match != null)
                    yield return match;
            }
        }

        /// <summary>
        /// 对整个编译中的所有类型执行约定推断
        /// </summary>
        public IEnumerable<ConventionMatch> EvaluateAll(Pipeline.GenerationContext context)
        {
            foreach (var syntaxTree in context.Compilation.SyntaxTrees)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    yield break;

                var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot(context.CancellationToken);

                foreach (var node in root.DescendantNodes())
                {
                    if (node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax classDecl)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken)
                            as INamedTypeSymbol;
                        if (symbol == null || symbol.IsAbstract || symbol.IsStatic)
                            continue;

                        foreach (var match in Evaluate(symbol, context))
                            yield return match;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 内置规则：类名以 Service 结尾 → 建议提取接口 + DI 注册
    /// </summary>
    internal sealed class ServiceConventionRule : IConventionRule
    {
        public string Name => "ServiceDetection";
        public string Description => "检测以 Service 结尾的类，建议自动生成接口和 DI 注册";

        public bool IsEnabled(Config.IAutoCodeConfig config)
            => config.GetBoolean("conventions.auto.detect.services", true);

        public ConventionMatch? Evaluate(INamedTypeSymbol typeSymbol, Pipeline.GenerationContext context)
        {
            var name = typeSymbol.Name;
            var pattern = context.Config.GetString("conventions.service.pattern", "*Service");

            if (!SharedPatternLogic.MatchesPattern(name, pattern))
                return null;

            // 已经有接口了就不建议了
            var hasNonLifetimeInterface = typeSymbol.Interfaces
                .Any(i => i.Name != "IScoped" && i.Name != "ISingleton"
                    && i.Name != "ITransient" && i.Name != "IDependencyBase");

            if (hasNonLifetimeInterface)
                return null;

            // 检查是否有公共方法（有方法才值得提取接口）
            var hasPublicMethods = typeSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Any(m => m.DeclaredAccessibility == Accessibility.Public
                    && m.MethodKind == MethodKind.Ordinary);

            if (!hasPublicMethods)
                return null;

            return new ConventionMatch(
                Name,
                typeSymbol,
                $"类 '{name}' 符合 Service 命名约定，建议添加 [AutoInterface] 和 DI 生命周期接口",
                "Interface",
                0.8);
        }
    }

    /// <summary>
    /// 内置规则：类名以 Dto/Request/Response 结尾 → 建议生成验证
    /// </summary>
    internal sealed class DtoConventionRule : IConventionRule
    {
        public string Name => "DtoDetection";
        public string Description => "检测以 Dto/Request/Response 结尾的类，建议自动生成验证代码";

        private static readonly string[] Suffixes = { "Dto", "Request", "Response", "Command", "Query" };

        public bool IsEnabled(Config.IAutoCodeConfig config)
            => config.GetBoolean("conventions.auto.detect.dtos", true);

        public ConventionMatch? Evaluate(INamedTypeSymbol typeSymbol, Pipeline.GenerationContext context)
        {
            var name = typeSymbol.Name;
            var suffix = Suffixes.FirstOrDefault(s => name.EndsWith(s, StringComparison.Ordinal));
            if (suffix == null)
                return null;

            // 检查是否有带 DataAnnotation 的属性
            var hasValidationAttrs = typeSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Any(p => p.GetAttributes().Any(a =>
                    a.AttributeClass?.ContainingNamespace?.ToDisplayString()
                        == "System.ComponentModel.DataAnnotations"));

            if (!hasValidationAttrs)
                return null;

            return new ConventionMatch(
                Name,
                typeSymbol,
                $"类 '{name}' 符合 {suffix} 命名约定且包含验证特性，建议添加 [AutoValidator]",
                "Validation",
                0.7);
        }
    }

    /// <summary>
    /// 内置规则：类名以 Repository 结尾 → 建议 DI 注册
    /// </summary>
    internal sealed class RepositoryConventionRule : IConventionRule
    {
        public string Name => "RepositoryDetection";
        public string Description => "检测以 Repository 结尾的类，建议自动 DI 注册";

        public bool IsEnabled(Config.IAutoCodeConfig config)
            => config.GetBoolean("conventions.auto.detect.repositories", true);

        public ConventionMatch? Evaluate(INamedTypeSymbol typeSymbol, Pipeline.GenerationContext context)
        {
            var name = typeSymbol.Name;
            var pattern = context.Config.GetString("conventions.repository.pattern", "*Repository");

            if (!SharedPatternLogic.MatchesPattern(name, pattern))
                return null;

            // 已有生命周期接口就不建议了
            var hasLifetime = typeSymbol.AllInterfaces
                .Any(i => i.Name == "IScoped" || i.Name == "ISingleton" || i.Name == "ITransient");

            if (hasLifetime)
                return null;

            return new ConventionMatch(
                Name,
                typeSymbol,
                $"类 '{name}' 符合 Repository 命名约定，建议实现 IScoped 接口以自动注册 DI",
                "DependencyInjection",
                0.75);
        }
    }

    /// <summary>
    /// 内置规则：实现了 IXxxService 接口的类 → 建议生成 Controller
    /// </summary>
    internal sealed class ControllerConventionRule : IConventionRule
    {
        public string Name => "ControllerDetection";
        public string Description => "检测实现了 IXxxService 接口的类，建议自动生成 Controller";

        public bool IsEnabled(Config.IAutoCodeConfig config)
            => config.GetBoolean("conventions.auto.detect.controllers", false); // 默认关闭，避免过度生成

        public ConventionMatch? Evaluate(INamedTypeSymbol typeSymbol, Pipeline.GenerationContext context)
        {
            var serviceInterface = typeSymbol.Interfaces
                .FirstOrDefault(i => i.Name.StartsWith("I", StringComparison.Ordinal)
                    && i.Name.EndsWith("Service", StringComparison.Ordinal));

            if (serviceInterface == null)
                return null;

            // 已经有 [AutoController] 就不建议了
            var hasControllerAttr = typeSymbol.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "AutoControllerAttribute");

            if (hasControllerAttr)
                return null;

            return new ConventionMatch(
                Name,
                typeSymbol,
                $"类 '{typeSymbol.Name}' 实现了 '{serviceInterface.Name}'，建议添加 [AutoController] 生成 API",
                "WebApi",
                0.6);
        }
    }

    /// <summary>
    /// 共享模式匹配逻辑
    /// </summary>
    internal static class SharedPatternLogic
    {
        internal static bool MatchesPattern(string input, string pattern)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*")
                return true;

            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
                return input.IndexOf(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase) >= 0;

            if (pattern.StartsWith("*"))
                return input.EndsWith(pattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase);

            if (pattern.EndsWith("*"))
                return input.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase);

            return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}

// 在 IConventionRule 的实现类中提供 MatchesPattern 辅助方法
namespace AutoCode.Engine.Convention
{
    // 为规则类提供基类辅助
    internal abstract class ConventionRuleBase : IConventionRule
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract bool IsEnabled(Config.IAutoCodeConfig config);
        public abstract ConventionMatch? Evaluate(INamedTypeSymbol typeSymbol, Pipeline.GenerationContext context);

        protected static bool MatchesPattern(string input, string pattern)
            => SharedPatternLogic.MatchesPattern(input, pattern);
    }
}
