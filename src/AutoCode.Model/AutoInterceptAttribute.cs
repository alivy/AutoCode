using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 拦截器类型枚举 - 定义所有可用的方法级横切关注点。
    /// 支持 Flags 组合，可在类/方法级别自由搭配。
    /// </summary>
    [Flags]
    public enum InterceptType
    {
        /// <summary>无拦截</summary>
        None = 0,

        /// <summary>日志拦截（Before/After/Exception 结构化日志 + 耗时统计）</summary>
        Log = 1 << 0,

        /// <summary>缓存拦截（Before 命中短路返回，After 写入缓存）</summary>
        Cache = 1 << 1,

        /// <summary>重试拦截（Exception 时按策略自动重试）</summary>
        Retry = 1 << 2,

        /// <summary>熔断拦截（连续失败后短路保护）</summary>
        CircuitBreaker = 1 << 3,

        /// <summary>参数校验拦截（Before 校验入参合法性）</summary>
        Validate = 1 << 4,

        /// <summary>权限校验拦截（Before 检查当前用户权限）</summary>
        Authorize = 1 << 5,

        /// <summary>指标采集拦截（After 上报耗时/成功率到 Metrics）</summary>
        Metrics = 1 << 6,

        /// <summary>链路追踪拦截（创建 OpenTelemetry Span）</summary>
        Tracing = 1 << 7,

        /// <summary>事务拦截（Before Begin / After Commit / Exception Rollback）</summary>
        Transaction = 1 << 8,

        /// <summary>审计拦截（After 记录操作日志）</summary>
        Audit = 1 << 9,

        /// <summary>限流拦截（Before 令牌桶/滑动窗口限流）</summary>
        Throttle = 1 << 10,

        /// <summary>性能分析拦截（记录 CPU/内存快照）</summary>
        Profiling = 1 << 11,

        /// <summary>全部拦截器</summary>
        All = Log | Cache | Retry | CircuitBreaker | Validate | Authorize
            | Metrics | Tracing | Transaction | Audit | Throttle | Profiling
    }

    /// <summary>
    /// 通用方法拦截特性 - 编译时 AOP，替代运行时动态代理。
    /// 标记在类或方法上，自动生成包含拦截管线的装饰器类。
    /// 零运行时反射、零额外依赖、NativeAOT 兼容。
    /// </summary>
    /// <example>
    /// <code>
    /// [AutoIntercept(InterceptType.Log | InterceptType.Retry | InterceptType.Cache)]
    /// public class OrderService : IOrderService
    /// {
    ///     public async Task&lt;Order&gt; GetOrderAsync(int id)
    ///     {
    ///         return await _db.Orders.FindAsync(id);
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AutoInterceptAttribute : Attribute
    {
        /// <summary>启用的拦截器类型（Flags 组合）</summary>
        public InterceptType Interceptors { get; }

        /// <summary>
        /// 指定拦截器组合
        /// </summary>
        public AutoInterceptAttribute(InterceptType interceptors = InterceptType.Log)
        {
            Interceptors = interceptors;
        }

        // ─── 日志选项 ───

        /// <summary>是否记录方法参数（默认 true）</summary>
        public bool LogParameters { get; set; } = true;

        /// <summary>是否记录返回值摘要（默认 false）</summary>
        public bool LogResult { get; set; }

        // ─── 缓存选项 ───

        /// <summary>缓存过期时间（秒，默认 300）</summary>
        public int CacheDurationSeconds { get; set; } = 300;

        /// <summary>缓存 Key 前缀（默认使用类名.方法名）</summary>
        public string? CacheKeyPrefix { get; set; }

        // ─── 重试选项 ───

        /// <summary>最大重试次数（默认 3）</summary>
        public int MaxRetryCount { get; set; } = 3;

        /// <summary>重试基础延迟（毫秒，默认 100，指数退避）</summary>
        public int RetryBaseDelayMs { get; set; } = 100;

        // ─── 熔断选项 ───

        /// <summary>熔断阈值：连续失败多少次后触发（默认 5）</summary>
        public int CircuitFailureThreshold { get; set; } = 5;

        /// <summary>熔断持续时间（秒，默认 30）</summary>
        public int CircuitBreakDurationSeconds { get; set; } = 30;

        // ─── 限流选项 ───

        /// <summary>每秒最大请求数（默认 100）</summary>
        public int MaxRequestsPerSecond { get; set; } = 100;

        // ─── 通用选项 ───

        /// <summary>是否仅拦截 public 方法（默认 true）</summary>
        public bool PublicOnly { get; set; } = true;

        /// <summary>排除的方法名（逗号分隔）</summary>
        public string? ExcludeMethods { get; set; }
    }

    /// <summary>
    /// 方法级拦截排除标记 - 标记在方法上表示跳过拦截
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SkipInterceptAttribute : Attribute
    {
    }

    /// <summary>
    /// 方法级拦截覆盖 - 覆盖类级别的拦截配置
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class InterceptOverrideAttribute : Attribute
    {
        /// <summary>此方法使用的拦截器组合</summary>
        public InterceptType Interceptors { get; }

        public InterceptOverrideAttribute(InterceptType interceptors)
        {
            Interceptors = interceptors;
        }
    }
}
