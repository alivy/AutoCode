using AutoCode.Model;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace APP.WebAPI.Services
{
    /// <summary>
    /// 示例：并发监听拦截器 - 实时监控每个方法的活跃并发数。
    /// 
    /// 场景：你想知道某个 Service 方法同时有多少个请求在执行，
    ///       当并发超过阈值时记录警告日志。
    /// 
    /// 使用：[CustomIntercept(typeof(ConcurrencyMonitorHandler))]
    /// </summary>
    public class ConcurrencyMonitorHandler : InterceptHandlerBase
    {
        // 每个方法的当前并发数
        private static readonly ConcurrentDictionary<string, int> _activeCounts = new();
        private static int _totalActive;

        public override void OnBefore(InterceptContext context)
        {
            var key = $"{context.ClassName}.{context.MethodName}";
            var current = _activeCounts.AddOrUpdate(key, 1, (_, v) => v + 1);
            Interlocked.Increment(ref _totalActive);

            // 将并发数写入上下文，后续拦截器可读取
            context.SetTag("concurrency.current", current);
            context.SetTag("concurrency.total", Volatile.Read(ref _totalActive));

            // 超过阈值时标记（可配合 Log 拦截器输出）
            if (current > 10)
                context.SetTag("concurrency.warning", true);
        }

        public override void OnAfter(InterceptContext context, object? result)
        {
            var key = $"{context.ClassName}.{context.MethodName}";
            _activeCounts.AddOrUpdate(key, 0, (_, v) => v - 1);
            Interlocked.Decrement(ref _totalActive);
        }

        public override void OnException(InterceptContext context, Exception exception)
        {
            var key = $"{context.ClassName}.{context.MethodName}";
            _activeCounts.AddOrUpdate(key, 0, (_, v) => v - 1);
            Interlocked.Decrement(ref _totalActive);
        }

        /// <summary>获取指定方法的当前并发数（供外部查询）</summary>
        public static int GetActiveCount(string className, string methodName)
            => _activeCounts.TryGetValue($"{className}.{methodName}", out var v) ? v : 0;

        /// <summary>获取全局活跃并发数</summary>
        public static int GetTotalActive() => Volatile.Read(ref _totalActive);
    }

    /// <summary>
    /// 示例：数据收集拦截器 - 采集方法调用的业务指标数据。
    /// 
    /// 场景：你想收集每个方法的调用次数、平均耗时、成功率、参数分布等，
    ///       用于业务分析或自定义监控面板。
    /// 
    /// 使用：[CustomIntercept(typeof(DataCollectorHandler), Order = 1)]
    /// </summary>
    public class DataCollectorHandler : InterceptHandlerBase
    {
        // 简易内存指标存储（生产环境可替换为 Prometheus/InfluxDB）
        private static readonly ConcurrentDictionary<string, MethodMetrics> _metrics = new();

        public override void OnBefore(InterceptContext context)
        {
            var key = $"{context.ClassName}.{context.MethodName}";
            var metrics = _metrics.GetOrAdd(key, _ => new MethodMetrics());

            Interlocked.Increment(ref metrics.CallCount);
            context.SetTag("datacollector.startTicks", Stopwatch.GetTimestamp());
        }

        public override void OnAfter(InterceptContext context, object? result)
        {
            var key = $"{context.ClassName}.{context.MethodName}";
            if (_metrics.TryGetValue(key, out var metrics))
            {
                Interlocked.Increment(ref metrics.SuccessCount);
                var elapsed = context.Elapsed.TotalMilliseconds;
                metrics.TotalMs += elapsed;
                if (elapsed > metrics.MaxMs) metrics.MaxMs = elapsed;
                if (elapsed < metrics.MinMs) metrics.MinMs = elapsed;
            }
        }

        public override void OnException(InterceptContext context, Exception exception)
        {
            var key = $"{context.ClassName}.{context.MethodName}";
            if (_metrics.TryGetValue(key, out var metrics))
            {
                Interlocked.Increment(ref metrics.ErrorCount);
                metrics.LastError = exception.Message;
                metrics.LastErrorTime = DateTime.UtcNow;
            }
        }

        /// <summary>获取指定方法的指标（供 API 暴露）</summary>
        public static MethodMetrics? GetMetrics(string className, string methodName)
            => _metrics.TryGetValue($"{className}.{methodName}", out var m) ? m : null;

        /// <summary>获取所有指标快照</summary>
        public static IReadOnlyDictionary<string, MethodMetrics> GetAllMetrics() => _metrics;
    }

    /// <summary>方法级指标数据</summary>
    public class MethodMetrics
    {
        public long CallCount;
        public long SuccessCount;
        public long ErrorCount;
        public double TotalMs;
        public double MaxMs;
        public double MinMs = double.MaxValue;
        public string? LastError;
        public DateTime? LastErrorTime;

        public double AvgMs => CallCount > 0 ? TotalMs / CallCount : 0;
        public double SuccessRate => CallCount > 0 ? (double)SuccessCount / CallCount * 100 : 0;
    }
}
