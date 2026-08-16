using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoCode.Generators
{
    /// <summary>
    /// V2 生成器启用开关。
    /// V1 与 V2 生成器监听相同特性（[AutoInterface]/[AutoDTO] 等），同时激活会重复生成同一类型（CS0101）。
    /// 默认仅运行 V1；在消费项目的 .csproj 中设置
    /// <code>&lt;AutoCode_EnableV2&gt;true&lt;/AutoCode_EnableV2&gt;</code>
    /// 后 V2 生成器才会输出代码（此时应确保 V1 不处理相同特性，避免冲突）。
    /// </summary>
    internal static class V2Gate
    {
        /// <summary>
        /// 获取 V2 启用状态的增量提供器（读取 build_property.AutoCode_EnableV2）。
        /// </summary>
        public static IncrementalValueProvider<bool> Enabled(IncrementalGeneratorInitializationContext context)
        {
            return context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
                IsEnabled(provider.GlobalOptions));
        }

        /// <summary>
        /// 与既有 pipeline 组合并过滤：未启用时丢弃所有元素。
        /// 用法：context.RegisterSourceOutput(V2Gate.Apply(context, sources), callback)
        /// </summary>
        public static IncrementalValuesProvider<T> Apply<T>(
            IncrementalGeneratorInitializationContext context,
            IncrementalValuesProvider<T> source)
        {
            return source
                .Combine(Enabled(context))
                .Where(static pair => pair.Right)
                .Select(static (pair, _) => pair.Left);
        }

        private static bool IsEnabled(AnalyzerConfigOptions options)
        {
            return options.TryGetValue("build_property.AutoCode_EnableV2", out var value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
