using AutoCode.Model;
using AutoCode.Model.InterfaceAttribute;

namespace APP.WebAPI.Services
{
    /// <summary>
    /// 订单服务 - [AutoIntercept] 编译时 AOP 示例。
    /// 
    /// 对比传统动态代理（Castle DynamicProxy）：
    ///   - 无需运行时 Reflection.Emit
    ///   - 生成的代码可 F12 跳转、逐行调试
    ///   - NativeAOT / Trimming 完全兼容
    ///   - 零额外 NuGet 依赖
    /// 
    /// 编译后自动生成 InterceptedOrderService.g.cs，包含：
    ///   ① Log    → Before/After/Exception 结构化日志 + 耗时
    ///   ② Cache  → Before 命中短路，After 写入缓存
    ///   ③ Retry  → 异常时指数退避重试
    ///   ④ Metrics → 上报耗时/成功率到 System.Diagnostics.Metrics
    /// </summary>
    [AutoIntercept(
        InterceptType.Log | InterceptType.Cache | InterceptType.Retry | InterceptType.Metrics | InterceptType.Validate,
        LogParameters = true,
        CacheDurationSeconds = 60,
        MaxRetryCount = 3,
        RetryBaseDelayMs = 200)]
    [CustomIntercept(typeof(DataCollectorHandler), Order = 1)]
    [CustomIntercept(typeof(ConcurrencyMonitorHandler), Order = 2)]
    [AutoInterface]
    public class OrderService : IOrderService, IScoped
    {
        // 模拟数据
        private static readonly List<OrderInfo> _orders = new()
        {
            new OrderInfo { Id = 1001, Product = "机械键盘", Amount = 399.00m, Status = "已完成" },
            new OrderInfo { Id = 1002, Product = "显示器", Amount = 2499.00m, Status = "配送中" },
            new OrderInfo { Id = 1003, Product = "鼠标垫", Amount = 49.90m, Status = "待发货" },
        };

        /// <summary>
        /// 获取所有订单 → 拦截管线: Log + Metrics
        /// </summary>
        public List<OrderInfo> GetAll()
        {
            return _orders.ToList();
        }

        /// <summary>
        /// 根据 ID 获取订单 → 拦截管线: Log + Cache + Retry + Metrics
        /// （缓存命中时直接返回，不执行方法体）
        /// </summary>
        public OrderInfo? GetById(int id)
        {
            // 模拟数据库查询延迟
            Thread.Sleep(50);
            return _orders.FirstOrDefault(o => o.Id == id);
        }

        /// <summary>
        /// 创建订单 → 拦截管线: Log + Retry + Metrics
        /// </summary>
        public OrderInfo Create(string product, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(product))
                throw new ArgumentException("商品名称不能为空");
            if (amount <= 0)
                throw new ArgumentException("金额必须大于0");

            var order = new OrderInfo
            {
                Id = _orders.Count > 0 ? _orders.Max(o => o.Id) + 1 : 1001,
                Product = product,
                Amount = amount,
                Status = "待支付"
            };
            _orders.Add(order);
            return order;
        }

        /// <summary>
        /// 模拟支付（可能失败）→ 展示 Retry 重试能力
        /// </summary>
        public async Task<bool> PayAsync(int orderId)
        {
            var order = _orders.FirstOrDefault(o => o.Id == orderId)
                ?? throw new InvalidOperationException($"订单 {orderId} 不存在");

            // 模拟 30% 支付失败率（重试可恢复）
            await Task.Delay(100);
            if (new Random().NextDouble() < 0.3)
                throw new TimeoutException("支付网关超时");

            order.Status = "已支付";
            return true;
        }

        /// <summary>
        /// 取消订单 → 无返回值方法拦截示例
        /// </summary>
        public void Cancel(int orderId)
        {
            var order = _orders.FirstOrDefault(o => o.Id == orderId)
                ?? throw new InvalidOperationException($"订单 {orderId} 不存在");
            order.Status = "已取消";
        }
    }

    /// <summary>订单信息模型</summary>
    public class OrderInfo
    {
        public int Id { get; set; }
        public string Product { get; set; } = "";
        public decimal Amount { get; set; }
        public string Status { get; set; } = "";
    }
}
