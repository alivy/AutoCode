using AutoCode.Model;
using AutoCode.Model.InterfaceAttribute;

namespace APP.WebAPI.Services
{
    // ═══════════════════════════════════════════════════════════
    // ℹ️ ChargeAsyncArgs 和 RefundAsyncArgs 由 AutoCode.Intercept 生成器自动产出。
    // 编译后在 obj/Debug/net8.0/generated/ 中可查看：
    //   PaymentService.Args.g.cs:
    //     public record ChargeAsyncArgs(int OrderId, decimal Amount);
    //     public record RefundAsyncArgs(int OrderId, string Reason);
    //     public record GetPaymentStatusArgs(int OrderId);
    //     public record HealthCheckArgs;
    // 用户无需手动定义，直接在 Handler 中引用即可。
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 扣款拦截器 - 强类型，知道方法名、参数、返回值。
    /// OnBefore: 拿到 OrderId + Amount
    /// OnAfter:  拿到 OrderId + Amount + bool result，可直接做数据处理
    /// </summary>
    public class ChargeAuditHandler : MethodHandlerBase<ChargeAsyncArgs, bool>
    {
        public override void OnBefore(ChargeAsyncArgs args, MethodContext ctx)
        {
            // args.OrderId, args.Amount — 直接可用，有 IntelliSense
            Console.WriteLine($"[审计] 发起扣款: 订单={args.OrderId}, 金额={args.Amount:C}");

            // 可以短路：比如金额超限时拒绝
            if (args.Amount > 100000)
            {
                ctx.ShortCircuit = true;
                ctx.Result = false;
            }
        }

        public override void OnAfter(ChargeAsyncArgs args, bool result, MethodContext ctx)
        {
            // result 是强类型 bool，直接处理
            if (result)
                Console.WriteLine($"[审计] 扣款成功: 订单={args.OrderId}, 金额={args.Amount:C}, 耗时={ctx.Elapsed.TotalMilliseconds}ms");
            else
                Console.WriteLine($"[审计] 扣款失败/拒绝: 订单={args.OrderId}");
        }

        public override void OnException(ChargeAsyncArgs args, Exception ex, MethodContext ctx)
        {
            Console.WriteLine($"[告警] 订单{args.OrderId}扣款异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 退款拦截器 - 强类型，拿到退款结果后做财务对账。
    /// </summary>
    public class RefundCollectorHandler : MethodHandlerBase<RefundAsyncArgs, object>
    {
        public override void OnAfter(RefundAsyncArgs args, object result, MethodContext ctx)
        {
            // 知道方法名、参数，直接采集数据
            Console.WriteLine($"[对账] 退款记录: 订单={args.OrderId}, 原因={args.Reason}, 耗时={ctx.Elapsed.TotalMilliseconds}ms");
        }
    }

    /// <summary>
    /// 支付服务 - 方法级拦截示例（模式 B）。
    /// 
    /// 与 OrderService（类级别拦截）不同，本类展示：
    ///   - 类上不打 [AutoIntercept]
    ///   - 只在需要的方法上精准标记
    ///   - 每个方法可以有完全不同的拦截配置
    ///   - 强类型 Handler 直接拿到参数和结果
    /// </summary>
    [AutoInterface]
    public class PaymentService : IPaymentService, IScoped
    {
        private static readonly Dictionary<int, string> _orderStatus = new()
        {
            [1001] = "待支付",
            [1002] = "已支付",
            [1003] = "已退款",
        };

        /// <summary>
        /// 发起扣款 - 方法级拦截：Log + Retry + 强类型审计 Handler。
        /// 生成器自动生成 ChargeAsyncArgs record，Handler 直接拿到 OrderId + Amount + bool result。
        /// </summary>
        [AutoIntercept(
            InterceptType.Log | InterceptType.Retry | InterceptType.Metrics,
            MaxRetryCount = 3,
            RetryBaseDelayMs = 500)]
        [CustomIntercept(typeof(ChargeAuditHandler), Order = 1)]
        public async Task<bool> ChargeAsync(int orderId, decimal amount)
        {
            if (!_orderStatus.ContainsKey(orderId))
                throw new InvalidOperationException($"订单 {orderId} 不存在");

            await Task.Delay(200);
            if (new Random().NextDouble() < 0.2)
                throw new TimeoutException("支付网关响应超时");

            _orderStatus[orderId] = "已支付";
            return true;
        }

        /// <summary>
        /// 退款 - 方法级拦截：强类型数据收集 Handler。
        /// 生成器自动生成 RefundAsyncArgs record，Handler 直接拿到 OrderId + Reason。
        /// </summary>
        [CustomIntercept(typeof(RefundCollectorHandler), Order = 1)]
        [AutoIntercept(InterceptType.Log)]
        public async Task RefundAsync(int orderId, string reason)
        {
            if (!_orderStatus.ContainsKey(orderId))
                throw new InvalidOperationException($"订单 {orderId} 不存在");

            await Task.Delay(100);
            _orderStatus[orderId] = "已退款";
        }

        /// <summary>
        /// 查询支付状态 - Cache + Log。
        /// </summary>
        [AutoIntercept(InterceptType.Log | InterceptType.Cache, CacheDurationSeconds = 30)]
        public string GetPaymentStatus(int orderId)
        {
            return _orderStatus.TryGetValue(orderId, out var status) ? status : "未知";
        }

        /// <summary>
        /// 健康检查 - 无标记，不拦截，直接透传。
        /// </summary>
        public bool HealthCheck() => true;
    }
}
