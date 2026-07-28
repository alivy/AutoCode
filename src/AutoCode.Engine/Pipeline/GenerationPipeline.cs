using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AutoCode.Engine.Plugin;

namespace AutoCode.Engine.Pipeline
{
    /// <summary>
    /// 管线 Hook 接口 - 允许在生成过程的各阶段插入自定义逻辑
    /// </summary>
    public interface IPipelineHook
    {
        /// <summary>Hook 优先级（越小越先执行）</summary>
        int Priority { get; }

        /// <summary>所有插件执行前</summary>
        void OnPipelineStart(GenerationContext context, IReadOnlyList<IGenerationPlugin> plugins);

        /// <summary>单个插件执行前</summary>
        void OnBeforePlugin(GenerationContext context, IGenerationPlugin plugin);

        /// <summary>单个插件执行后</summary>
        void OnAfterPlugin(GenerationContext context, IGenerationPlugin plugin, PluginResult result);

        /// <summary>插件执行出错时</summary>
        void OnPluginError(GenerationContext context, IGenerationPlugin plugin, Exception exception);

        /// <summary>转换/修改生成的输出（可修改文件内容）</summary>
        GeneratedFile? TransformOutput(GenerationContext context, GeneratedFile file);

        /// <summary>所有插件执行后</summary>
        void OnPipelineComplete(GenerationContext context, IReadOnlyList<GeneratedFile> allFiles);
    }

    /// <summary>
    /// Hook 基类 - 提供空实现，方便只覆盖需要的方法
    /// </summary>
    public abstract class PipelineHookBase : IPipelineHook
    {
        public virtual int Priority => 100;
        public virtual void OnPipelineStart(GenerationContext context, IReadOnlyList<IGenerationPlugin> plugins) { }
        public virtual void OnBeforePlugin(GenerationContext context, IGenerationPlugin plugin) { }
        public virtual void OnAfterPlugin(GenerationContext context, IGenerationPlugin plugin, PluginResult result) { }
        public virtual void OnPluginError(GenerationContext context, IGenerationPlugin plugin, Exception exception) { }
        public virtual GeneratedFile? TransformOutput(GenerationContext context, GeneratedFile file) => file;
        public virtual void OnPipelineComplete(GenerationContext context, IReadOnlyList<GeneratedFile> allFiles) { }
    }

    /// <summary>
    /// 生成管线接口
    /// </summary>
    public interface IGenerationPipeline
    {
        /// <summary>注册插件</summary>
        IGenerationPipeline AddPlugin(IGenerationPlugin plugin);

        /// <summary>注册 Hook</summary>
        IGenerationPipeline AddHook(IPipelineHook hook);

        /// <summary>执行管线，返回所有生成的文件</summary>
        PipelineExecutionResult Execute(GenerationContext context);
    }

    /// <summary>
    /// 管线执行总结果
    /// </summary>
    public sealed class PipelineExecutionResult
    {
        /// <summary>所有生成的文件</summary>
        public IReadOnlyList<GeneratedFile> Files { get; }

        /// <summary>各插件执行结果</summary>
        public IReadOnlyList<PluginResult> PluginResults { get; }

        /// <summary>总耗时（毫秒）</summary>
        public long TotalElapsedMilliseconds { get; }

        /// <summary>是否全部成功</summary>
        public bool Success { get; }

        public PipelineExecutionResult(IReadOnlyList<GeneratedFile> files,
            IReadOnlyList<PluginResult> pluginResults, long totalElapsed)
        {
            Files = files;
            PluginResults = pluginResults;
            TotalElapsedMilliseconds = totalElapsed;
            Success = pluginResults.All(r => r.Success);
        }
    }

    /// <summary>
    /// 生成管线实现 - 按优先级执行插件，支持 Hook、依赖排序、错误隔离
    /// </summary>
    public sealed class GenerationPipeline : IGenerationPipeline
    {
        private readonly List<IGenerationPlugin> _plugins = new List<IGenerationPlugin>();
        private readonly List<IPipelineHook> _hooks = new List<IPipelineHook>();

        public IGenerationPipeline AddPlugin(IGenerationPlugin plugin)
        {
            _plugins.Add(plugin ?? throw new ArgumentNullException(nameof(plugin)));
            return this;
        }

        public IGenerationPipeline AddHook(IPipelineHook hook)
        {
            _hooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));
            return this;
        }

        public PipelineExecutionResult Execute(GenerationContext context)
        {
            var totalSw = Stopwatch.StartNew();
            var allFiles = new List<GeneratedFile>();
            var pluginResults = new List<PluginResult>();

            // 排序插件：先按依赖拓扑排序，再按优先级
            var orderedPlugins = TopologicalSort(_plugins);

            // 过滤启用的插件
            var enabledPlugins = orderedPlugins
                .Where(p =>
                {
                    try { return p.IsEnabled(context); }
                    catch { return false; }
                })
                .ToList();

            // Hook: Pipeline Start
            var orderedHooks = _hooks.OrderBy(h => h.Priority).ToList();
            foreach (var hook in orderedHooks)
            {
                try { hook.OnPipelineStart(context, enabledPlugins); }
                catch { /* Hook 异常不影响主管线 */ }
            }

            // 逐个执行插件
            foreach (var plugin in enabledPlugins)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    break;

                // 更新上下文中的 PreviousOutputs
                context.PreviousOutputs = allFiles.AsReadOnly();

                // Hook: Before Plugin
                foreach (var hook in orderedHooks)
                {
                    try { hook.OnBeforePlugin(context, plugin); }
                    catch { }
                }

                // 执行插件
                var sw = Stopwatch.StartNew();
                PluginResult result;

                try
                {
                    var files = plugin.Generate(context).ToList();
                    sw.Stop();

                    // Hook: Transform Output
                    var transformedFiles = new List<GeneratedFile>();
                    foreach (var file in files)
                    {
                        GeneratedFile? transformed = file;
                        foreach (var hook in orderedHooks)
                        {
                            if (transformed == null) break;
                            try
                            {
                                transformed = hook.TransformOutput(context, transformed);
                            }
                            catch { }
                        }
                        if (transformed != null)
                            transformedFiles.Add(transformed);
                    }

                    allFiles.AddRange(transformedFiles);
                    result = PluginResult.Ok(plugin.Name, transformedFiles, sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    result = PluginResult.Fail(plugin.Name, ex.Message, sw.ElapsedMilliseconds);

                    // Hook: On Error
                    foreach (var hook in orderedHooks)
                    {
                        try { hook.OnPluginError(context, plugin, ex); }
                        catch { }
                    }

                    // 报告诊断
                    context.Diagnostics.ReportError(
                        $"AC1001",
                        $"插件 '{plugin.Name}' 执行失败: {ex.Message}",
                        context.CurrentNode?.GetLocation());
                }

                pluginResults.Add(result);

                // Hook: After Plugin
                foreach (var hook in orderedHooks)
                {
                    try { hook.OnAfterPlugin(context, plugin, result); }
                    catch { }
                }
            }

            // Hook: Pipeline Complete
            foreach (var hook in orderedHooks)
            {
                try { hook.OnPipelineComplete(context, allFiles); }
                catch { }
            }

            totalSw.Stop();
            return new PipelineExecutionResult(allFiles, pluginResults, totalSw.ElapsedMilliseconds);
        }

        /// <summary>
        /// 拓扑排序：确保依赖的插件先执行
        /// </summary>
        private static List<IGenerationPlugin> TopologicalSort(List<IGenerationPlugin> plugins)
        {
            var sorted = new List<IGenerationPlugin>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pluginMap = plugins.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            void Visit(IGenerationPlugin plugin)
            {
                if (visited.Contains(plugin.Name))
                    return;

                visited.Add(plugin.Name);

                // 先访问依赖
                foreach (var dep in plugin.Dependencies)
                {
                    if (pluginMap.TryGetValue(dep, out var depPlugin))
                        Visit(depPlugin);
                }

                sorted.Add(plugin);
            }

            // 按优先级排序后逐个访问
            foreach (var plugin in plugins.OrderBy(p => p.Priority))
                Visit(plugin);

            return sorted;
        }
    }
}
