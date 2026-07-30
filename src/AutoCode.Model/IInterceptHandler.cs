using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AutoCode.Model
{
    // ═══════════════════════════════════════════════════════════
    // 第一层：通用拦截器（不关心具体方法签名，用于横切关注点）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 通用拦截处理器 - 不关心具体方法签名，用于日志/指标/并发计数等横切关注点。
    /// </summary>
    public interface IInterceptHandler
    {
        void OnBefore(InterceptContext context);
        void OnAfter(InterceptContext context, object? result);
        void OnException(InterceptContext context, Exception exception);
    }

    /// <summary>
    /// 通用拦截器基类 - 提供默认空实现。
    /// </summary>
    public abstract class InterceptHandlerBase : IInterceptHandler
    {
        public virtual void OnBefore(InterceptContext context) { }
        public virtual void OnAfter(InterceptContext context, object? result) { }
        public virtual void OnException(InterceptContext context, Exception exception) { }
    }

    // ═══════════════════════════════════════════════════════════
    // 第二层：强类型方法拦截器（知道方法名、参数、返回值）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 强类型方法拦截处理器 - 编译时生成器为每个方法生成 TArgs，
    /// 用户直接拿到类型化的参数和结果，无需强转。
    /// 
    /// 使用方式：
    /// <code>
    /// // 生成器自动生成: public record ChargeAsyncArgs(int OrderId, decimal Amount);
    /// 
    /// public class ChargeHandler : IMethodHandler&lt;ChargeAsyncArgs, bool&gt;
    /// {
    ///     public void OnBefore(ChargeAsyncArgs args, MethodContext ctx)
    ///     {
    ///         // args.OrderId, args.Amount — 直接可用，有 IntelliSense
    ///         Console.WriteLine($"扣款: 订单{args.OrderId}, 金额{args.Amount}");
    ///     }
    /// 
    ///     public void OnAfter(ChargeAsyncArgs args, bool result, MethodContext ctx)
    ///     {
    ///         // result 是强类型 bool，直接处理
    ///         if (result) _auditLog.Record(args.OrderId, args.Amount, ctx.Elapsed);
    ///     }
    /// 
    ///     public void OnException(ChargeAsyncArgs args, Exception ex, MethodContext ctx)
    ///     {
    ///         _alert.Notify($"订单{args.OrderId}扣款失败: {ex.Message}");
    ///     }
    /// }
    /// </code>
    /// </summary>
    /// <typeparam name="TArgs">生成器自动生成的强类型参数对象</typeparam>
    /// <typeparam name="TResult">方法返回值类型（void 方法为 object）</typeparam>
    public interface IMethodHandler<TArgs, TResult>
    {
        /// <summary>
        /// 方法执行前。拿到全部入参（强类型）。
        /// 设置 ctx.ShortCircuit = true + ctx.Result 可短路返回。
        /// </summary>
        void OnBefore(TArgs args, MethodContext ctx);

        /// <summary>
        /// 方法成功执行后。拿到入参 + 强类型返回值，可直接做数据处理。
        /// </summary>
        void OnAfter(TArgs args, TResult result, MethodContext ctx);

        /// <summary>
        /// 方法抛异常时。拿到入参 + 异常对象。
        /// 设置 ctx.Handled = true 可吐异常降级。
        /// </summary>
        void OnException(TArgs args, Exception ex, MethodContext ctx);
    }

    /// <summary>
    /// 强类型方法拦截器基类 - 只覆盖关心的阶段。
    /// </summary>
    public abstract class MethodHandlerBase<TArgs, TResult> : IMethodHandler<TArgs, TResult>
    {
        public virtual void OnBefore(TArgs args, MethodContext ctx) { }
        public virtual void OnAfter(TArgs args, TResult result, MethodContext ctx) { }
        public virtual void OnException(TArgs args, Exception ex, MethodContext ctx) { }
    }

    // ═══════════════════════════════════════════════════════════
    // 上下文对象
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 通用拦截上下文（用于 IInterceptHandler）
    /// </summary>
    public class InterceptContext
    {
        public string ClassName { get; set; } = "";
        public string MethodName { get; set; } = "";
        public IReadOnlyDictionary<string, object?> Arguments { get; set; } = new Dictionary<string, object?>();
        public TimeSpan Elapsed { get; set; }
        public bool ShortCircuit { get; set; }
        public bool Handled { get; set; }
        public object? Result { get; set; }
        public int AttemptNumber { get; set; } = 1;
        public IServiceProvider? ServiceProvider { get; set; }

        private readonly Dictionary<string, object?> _tags = new();
        public void SetTag(string key, object? value) => _tags[key] = value;
        public T? GetTag<T>(string key) => _tags.TryGetValue(key, out var v) ? (T?)v : default;
    }

    /// <summary>
    /// 强类型方法上下文（用于 IMethodHandler&lt;TArgs, TResult&gt;）。
    /// 比 InterceptContext 更精简，因为参数已经通过 TArgs 传递。
    /// </summary>
    public class MethodContext
    {
        /// <summary>类名</summary>
        public string ClassName { get; set; } = "";

        /// <summary>方法名</summary>
        public string MethodName { get; set; } = "";

        /// <summary>执行耗时（OnAfter/OnException 中有效）</summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>当前重试次数</summary>
        public int AttemptNumber { get; set; } = 1;

        /// <summary>短路标记：设为 true 跳过方法执行，直接返回 Result</summary>
        public bool ShortCircuit { get; set; }

        /// <summary>异常已处理标记：设为 true 异常不再向上抛</summary>
        public bool Handled { get; set; }

        /// <summary>短路/降级时的返回值</summary>
        public object? Result { get; set; }

        /// <summary>服务提供器</summary>
        public IServiceProvider? ServiceProvider { get; set; }

        /// <summary>自定义标签（拦截器间共享）</summary>
        private readonly Dictionary<string, object?> _tags = new();
        public void SetTag(string key, object? value) => _tags[key] = value;
        public T? GetTag<T>(string key) => _tags.TryGetValue(key, out var v) ? (T?)v : default;
    }

    // ═══════════════════════════════════════════════════════════
    // 异步 Handler（真实场景：写审计日志到数据库、调外部告警 API）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 异步强类型方法拦截器 - 用于需要异步操作的场景。
    /// 例如：写入审计日志到数据库、调用外部告警 API、异步数据收集。
    /// </summary>
    /// <typeparam name="TArgs">生成器自动生成的强类型参数对象</typeparam>
    /// <typeparam name="TResult">方法返回值类型</typeparam>
    public interface IAsyncMethodHandler<TArgs, TResult>
    {
        /// <summary>方法执行前（异步）。可短路。</summary>
        System.Threading.Tasks.Task OnBeforeAsync(TArgs args, MethodContext ctx);

        /// <summary>方法成功执行后（异步）。拿到强类型 result。</summary>
        System.Threading.Tasks.Task OnAfterAsync(TArgs args, TResult result, MethodContext ctx);

        /// <summary>方法抛异常时（异步）。可降级。</summary>
        System.Threading.Tasks.Task OnExceptionAsync(TArgs args, Exception ex, MethodContext ctx);
    }

    /// <summary>
    /// 异步方法拦截器基类 - 只覆盖关心的阶段。
    /// </summary>
    public abstract class AsyncMethodHandlerBase<TArgs, TResult> : IAsyncMethodHandler<TArgs, TResult>
    {
        public virtual System.Threading.Tasks.Task OnBeforeAsync(TArgs args, MethodContext ctx)
            => System.Threading.Tasks.Task.CompletedTask;

        public virtual System.Threading.Tasks.Task OnAfterAsync(TArgs args, TResult result, MethodContext ctx)
            => System.Threading.Tasks.Task.CompletedTask;

        public virtual System.Threading.Tasks.Task OnExceptionAsync(TArgs args, Exception ex, MethodContext ctx)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════
    // 特性
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 自定义拦截器特性 - 支持 IInterceptHandler 和 IMethodHandler&lt;,&gt; 两种处理器。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class CustomInterceptAttribute : Attribute
    {
        /// <summary>拦截处理器类型</summary>
        public Type HandlerType { get; }

        /// <summary>执行顺序（越小越先）</summary>
        public int Order { get; set; } = 100;

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        public CustomInterceptAttribute(Type handlerType)
        {
            HandlerType = handlerType;
        }
    }
}
