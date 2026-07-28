using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AutoCode.Engine.Diagnostics
{
    /// <summary>
    /// 诊断收集器接口 - 统一收集生成过程中的错误/警告/信息
    /// </summary>
    public interface IDiagnosticCollector
    {
        /// <summary>报告错误</summary>
        void ReportError(string diagnosticId, string message, Location? location = null);

        /// <summary>报告警告</summary>
        void ReportWarning(string diagnosticId, string message, Location? location = null);

        /// <summary>报告信息提示</summary>
        void ReportInfo(string diagnosticId, string message, Location? location = null);

        /// <summary>报告建议（约定推断结果）</summary>
        void ReportSuggestion(string diagnosticId, string message, Location? location = null, string? fixAction = null);

        /// <summary>获取所有诊断</summary>
        IReadOnlyList<DiagnosticEntry> GetAll();

        /// <summary>是否有错误</summary>
        bool HasErrors { get; }

        /// <summary>清空</summary>
        void Clear();
    }

    /// <summary>
    /// 诊断严重级别
    /// </summary>
    public enum DiagnosticSeverityLevel
    {
        Info,
        Suggestion,
        Warning,
        Error
    }

    /// <summary>
    /// 诊断条目
    /// </summary>
    public sealed class DiagnosticEntry
    {
        /// <summary>诊断 ID（如 AC1001, AC2001）</summary>
        public string Id { get; }

        /// <summary>消息</summary>
        public string Message { get; }

        /// <summary>严重级别</summary>
        public DiagnosticSeverityLevel Severity { get; }

        /// <summary>代码位置</summary>
        public Location? Location { get; }

        /// <summary>修复建议描述</summary>
        public string? FixSuggestion { get; }

        /// <summary>文档链接</summary>
        public string? HelpLink { get; set; }

        /// <summary>时间戳</summary>
        public DateTime Timestamp { get; }

        public DiagnosticEntry(string id, string message, DiagnosticSeverityLevel severity,
            Location? location = null, string? fixSuggestion = null)
        {
            Id = id;
            Message = message;
            Severity = severity;
            Location = location;
            FixSuggestion = fixSuggestion;
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// 转换为 Roslyn Diagnostic
        /// </summary>
        public Diagnostic ToRoslynDiagnostic()
        {
            var descriptor = new DiagnosticDescriptor(
                Id,
                GetTitle(),
                Message,
                "AutoCode",
                Severity switch
                {
                    DiagnosticSeverityLevel.Error => DiagnosticSeverity.Error,
                    DiagnosticSeverityLevel.Warning => DiagnosticSeverity.Warning,
                    DiagnosticSeverityLevel.Suggestion => DiagnosticSeverity.Info,
                    _ => DiagnosticSeverity.Info
                },
                isEnabledByDefault: true,
                helpLinkUri: HelpLink ?? $"https://github.com/autocode/docs/{Id}");

            return Diagnostic.Create(descriptor, Location ?? Location.None);
        }

        private string GetTitle()
        {
            // 从消息中提取简短标题
            var idx = Message.IndexOf(':');
            return idx > 0 ? Message.Substring(0, idx) : Message;
        }
    }

    /// <summary>
    /// 诊断收集器实现
    /// </summary>
    public sealed class DiagnosticCollector : IDiagnosticCollector
    {
        private readonly List<DiagnosticEntry> _entries = new List<DiagnosticEntry>();
        private readonly object _lock = new object();

        public bool HasErrors
        {
            get
            {
                lock (_lock)
                {
                    foreach (var e in _entries)
                        if (e.Severity == DiagnosticSeverityLevel.Error)
                            return true;
                    return false;
                }
            }
        }

        public void ReportError(string diagnosticId, string message, Location? location = null)
        {
            Add(new DiagnosticEntry(diagnosticId, message, DiagnosticSeverityLevel.Error, location));
        }

        public void ReportWarning(string diagnosticId, string message, Location? location = null)
        {
            Add(new DiagnosticEntry(diagnosticId, message, DiagnosticSeverityLevel.Warning, location));
        }

        public void ReportInfo(string diagnosticId, string message, Location? location = null)
        {
            Add(new DiagnosticEntry(diagnosticId, message, DiagnosticSeverityLevel.Info, location));
        }

        public void ReportSuggestion(string diagnosticId, string message, Location? location = null, string? fixAction = null)
        {
            Add(new DiagnosticEntry(diagnosticId, message, DiagnosticSeverityLevel.Suggestion, location, fixAction));
        }

        public IReadOnlyList<DiagnosticEntry> GetAll()
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        private void Add(DiagnosticEntry entry)
        {
            lock (_lock)
            {
                _entries.Add(entry);
            }
        }
    }

    /// <summary>
    /// 诊断 ID 常量 - 统一 ID 分配体系
    /// AC1xxx: 引擎/管线
    /// AC2xxx: Mapper 插件
    /// AC3xxx: WebApi 插件
    /// AC4xxx: DTO 插件
    /// AC5xxx: Validation 插件
    /// AC6xxx: DI 插件
    /// AC7xxx: CRUD 插件
    /// AC8xxx: Convention 推断
    /// </summary>
    public static class DiagnosticIds
    {
        // Engine (AC1xxx)
        public const string PluginExecutionFailed = "AC1001";
        public const string ConfigParseError = "AC1002";
        public const string PluginDependencyMissing = "AC1003";
        public const string PipelineTimeout = "AC1004";

        // Mapper (AC2xxx)
        public const string MapperNoMatchingProperties = "AC2001";
        public const string MapperTypeMismatch = "AC2002";
        public const string MapperCircularReference = "AC2003";
        public const string MapperMissingSetter = "AC2004";

        // WebApi (AC3xxx)
        public const string WebApiNoServiceInterface = "AC3001";
        public const string WebApiAmbiguousRoute = "AC3002";

        // DTO (AC4xxx)
        public const string DtoSourceNotFound = "AC4001";
        public const string DtoNoProperties = "AC4002";

        // Validation (AC5xxx)
        public const string ValidationNoRules = "AC5001";
        public const string ValidationUnsupportedAttribute = "AC5002";

        // DI (AC6xxx)
        public const string DiNoLifetimeInterface = "AC6001";
        public const string DiDuplicateRegistration = "AC6002";

        // CRUD (AC7xxx)
        public const string CrudNoKeyProperty = "AC7001";

        // Convention (AC8xxx)
        public const string ConventionServiceDetected = "AC8001";
        public const string ConventionDtoDetected = "AC8002";
        public const string ConventionMappingSuggested = "AC8003";
    }
}
