using System;
using System.Collections.Generic;

namespace AutoCode.Engine.Pipeline
{
    /// <summary>
    /// 生成的文件输出
    /// </summary>
    public sealed class GeneratedFile
    {
        /// <summary>文件名（如 "UserMapper.g.cs"）</summary>
        public string FileName { get; }

        /// <summary>生成的源代码内容</summary>
        public string Content { get; }

        /// <summary>生成此文件的插件名称</summary>
        public string GeneratedBy { get; }

        /// <summary>关联的源类型全名（用于诊断定位）</summary>
        public string? SourceTypeFullName { get; set; }

        /// <summary>附加元数据</summary>
        public Dictionary<string, object> Metadata { get; } = new Dictionary<string, object>();

        public GeneratedFile(string fileName, string content, string generatedBy)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            GeneratedBy = generatedBy ?? throw new ArgumentNullException(nameof(generatedBy));
        }

        public override string ToString() => $"{FileName} ({GeneratedBy})";
    }

    /// <summary>
    /// 插件触发方式
    /// </summary>
    public enum PluginTrigger
    {
        /// <summary>通过 Attribute 标记触发</summary>
        Attribute,

        /// <summary>通过约定推断触发</summary>
        Convention,

        /// <summary>手动/配置触发</summary>
        Manual,

        /// <summary>级联触发（由其他插件的输出触发）</summary>
        Cascade
    }

    /// <summary>
    /// 插件执行结果
    /// </summary>
    public sealed class PluginResult
    {
        /// <summary>插件名称</summary>
        public string PluginName { get; }

        /// <summary>是否成功</summary>
        public bool Success { get; }

        /// <summary>生成的文件列表</summary>
        public IReadOnlyList<GeneratedFile> Files { get; }

        /// <summary>执行耗时（毫秒）</summary>
        public long ElapsedMilliseconds { get; }

        /// <summary>错误信息（如果失败）</summary>
        public string? ErrorMessage { get; }

        /// <summary>警告信息</summary>
        public IReadOnlyList<string> Warnings { get; }

        private PluginResult(string pluginName, bool success, IReadOnlyList<GeneratedFile> files,
            long elapsed, string? error, IReadOnlyList<string> warnings)
        {
            PluginName = pluginName;
            Success = success;
            Files = files;
            ElapsedMilliseconds = elapsed;
            ErrorMessage = error;
            Warnings = warnings;
        }

        public static PluginResult Ok(string pluginName, IReadOnlyList<GeneratedFile> files, long elapsed,
            IReadOnlyList<string>? warnings = null)
            => new PluginResult(pluginName, true, files, elapsed, null, warnings ?? Array.Empty<string>());

        public static PluginResult Fail(string pluginName, string error, long elapsed)
            => new PluginResult(pluginName, false, Array.Empty<GeneratedFile>(), elapsed, error, Array.Empty<string>());
    }
}
