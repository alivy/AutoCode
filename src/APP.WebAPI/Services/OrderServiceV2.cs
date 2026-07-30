using AutoCode.Model;
using AutoCode.Model.InterfaceAttribute;

namespace APP.WebAPI.Services
{
    // ═══════════════════════════════════════════════════════════
    // 综合示例：覆盖各种参数类型的方法级拦截
    // 生成器自动产出所有 Args record，用户 Handler 直接引用
    // ═══════════════════════════════════════════════════════════

    #region 辅助类型定义

    /// <summary>订单查询条件（引用类型 / 复杂对象）</summary>
    public class OrderQuery
    {
        public string? Keyword { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public OrderStatus? Status { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    /// <summary>订单结果（引用类型返回值）</summary>
    public class OrderResult
    {
        public int Id { get; set; }
        public string Product { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public OrderStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>枚举类型参数</summary>
    public enum OrderStatus
    {
        Pending = 0,
        Paid = 1,
        Shipped = 2,
        Completed = 3,
        Cancelled = 4
    }

    /// <summary>批量操作请求（集合类型参数）</summary>
    public class BatchShipRequest
    {
        public List<int> OrderIds { get; set; } = new();
        public string Carrier { get; set; } = "";
        public string? TrackingNumberPrefix { get; set; }
    }

    /// <summary>批量操作结果（集合类型返回值）</summary>
    public class BatchResult
    {
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    #endregion

    #region 自定义 Handler 示例

    /// <summary>
    /// 场景 1：值类型参数（int + decimal + enum）
    /// 生成器自动产出: record CreateOrderArgs(int UserId, decimal Amount, OrderStatus Priority)
    /// </summary>
    public class CreateOrderAuditHandler : MethodHandlerBase<CreateOrderArgs, OrderResult>
    {
        public override void OnBefore(CreateOrderArgs args, MethodContext ctx)
        {
            // 值类型参数直接可用，无需强转
            Console.WriteLine($"[下单审计] 用户={args.UserId}, 金额={args.Amount:C}, 优先级={args.Priority}");

            // 业务规则：金额超限拦截
            if (args.Amount > 50000)
            {
                ctx.ShortCircuit = true;
                ctx.Result = new OrderResult { Status = OrderStatus.Cancelled };
            }
        }

        public override void OnAfter(CreateOrderArgs args, OrderResult result, MethodContext ctx)
        {
            // result 是强类型 OrderResult，直接访问属性
            Console.WriteLine($"[下单审计] 完成: 订单号={result.Id}, 状态={result.Status}, 耗时={ctx.Elapsed.TotalMilliseconds:F1}ms");
        }

        public override void OnException(CreateOrderArgs args, Exception ex, MethodContext ctx)
        {
            Console.WriteLine($"[下单告警] 用户{args.UserId}下单失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 场景 2：引用类型参数（复杂对象 OrderQuery）
    /// 生成器自动产出: record SearchOrdersArgs(OrderQuery Query)
    /// </summary>
    public class SearchOrdersMetricsHandler : MethodHandlerBase<SearchOrdersArgs, List<OrderResult>>
    {
        public override void OnBefore(SearchOrdersArgs args, MethodContext ctx)
        {
            // 引用类型参数：直接访问嵌套属性
            Console.WriteLine($"[搜索监控] 关键词={args.Query.Keyword}, 页码={args.Query.PageIndex}, 状态={args.Query.Status}");
            ctx.SetTag("search.keyword", args.Query.Keyword);
        }

        public override void OnAfter(SearchOrdersArgs args, List<OrderResult> result, MethodContext ctx)
        {
            // 集合返回值：直接拿到 List<OrderResult>
            Console.WriteLine($"[搜索监控] 返回 {result.Count} 条, 耗时={ctx.Elapsed.TotalMilliseconds:F1}ms");
        }
    }

    /// <summary>
    /// 场景 3：集合类型参数（List<int>）+ 可空类型
    /// 生成器自动产出: record BatchShipAsyncArgs(BatchShipRequest Request)
    /// </summary>
    public class BatchOperationHandler : MethodHandlerBase<BatchShipAsyncArgs, BatchResult>
    {
        public override void OnBefore(BatchShipAsyncArgs args, MethodContext ctx)
        {
            // 集合参数：直接访问 Count
            Console.WriteLine($"[批量操作] 承运商={args.Request.Carrier}, 订单数={args.Request.OrderIds.Count}");

            // 业务规则：单次批量不超过 100
            if (args.Request.OrderIds.Count > 100)
            {
                ctx.ShortCircuit = true;
                ctx.Result = new BatchResult { FailCount = args.Request.OrderIds.Count, Errors = { "单次批量不超过100" } };
            }
        }

        public override void OnAfter(BatchShipAsyncArgs args, BatchResult result, MethodContext ctx)
        {
            Console.WriteLine($"[批量操作] 成功={result.SuccessCount}, 失败={result.FailCount}");
        }
    }

    /// <summary>
    /// 场景 4：Nullable 值类型 + Guid + DateTime
    /// 生成器自动产出: record UpdateOrderStatusArgs(Guid OrderGuid, OrderStatus? NewStatus, DateTime? OperatedAt)
    /// </summary>
    public class StatusChangeHandler : MethodHandlerBase<UpdateOrderStatusArgs, bool>
    {
        public override void OnBefore(UpdateOrderStatusArgs args, MethodContext ctx)
        {
            // Nullable 值类型：安全访问
            Console.WriteLine($"[状态变更] Guid={args.OrderGuid}, 新状态={args.NewStatus?.ToString() ?? "未指定"}, 操作时间={args.OperatedAt:yyyy-MM-dd HH:mm}");
        }

        public override void OnAfter(UpdateOrderStatusArgs args, bool result, MethodContext ctx)
        {
            if (result)
                Console.WriteLine($"[状态变更] 成功: {args.OrderGuid} → {args.NewStatus}");
        }
    }

    /// <summary>
    /// 场景 5：多参数混合（string + int + bool + decimal?）
    /// 生成器自动产出: record AdjustPriceArgs(string Sku, int WarehouseId, bool ForceUpdate, decimal? NewPrice)
    /// </summary>
    public class PriceAdjustAuditHandler : MethodHandlerBase<AdjustPriceArgs, decimal>
    {
        public override void OnBefore(AdjustPriceArgs args, MethodContext ctx)
        {
            Console.WriteLine($"[价格审计] SKU={args.Sku}, 仓库={args.WarehouseId}, 强制={args.ForceUpdate}, 新价格={args.NewPrice?.ToString("C") ?? "未指定"}");
        }

        public override void OnAfter(AdjustPriceArgs args, decimal result, MethodContext ctx)
        {
            // 返回值是 decimal（值类型），直接用于计算
            Console.WriteLine($"[价格审计] 最终价格={result:C}, 耗时={ctx.Elapsed.TotalMilliseconds:F1}ms");
        }
    }

    #endregion

    /// <summary>
    /// 订单服务 - 方法级拦截综合示例。
    /// 覆盖：值类型、引用类型、Nullable、集合、枚举、Guid、多参数混合等各种场景。
    /// </summary>
    [AutoInterface]
    public class OrderServiceV2 : IOrderServiceV2, IScoped
    {
        private static readonly List<OrderResult> _orders = new()
        {
            new() { Id = 1, Product = "机械键盘", TotalAmount = 399, Status = OrderStatus.Paid, CreatedAt = DateTime.Now },
            new() { Id = 2, Product = "4K显示器", TotalAmount = 2999, Status = OrderStatus.Shipped, CreatedAt = DateTime.Now },
        };
        private static int _nextId = 3;

        // ─── 场景 1：值类型参数（int + decimal + enum）→ 返回引用类型 ───
        /// <summary>
        /// 创建订单。
        /// 自动生成: record CreateOrderArgs(int UserId, decimal Amount, OrderStatus Priority)
        /// Handler 拿到: args.UserId(int), args.Amount(decimal), args.Priority(enum), result(OrderResult)
        /// </summary>
        [AutoIntercept(InterceptType.Log | InterceptType.Metrics)]
        [CustomIntercept(typeof(CreateOrderAuditHandler))]
        public OrderResult CreateOrder(int userId, decimal amount, OrderStatus priority)
        {
            var order = new OrderResult
            {
                Id = _nextId++,
                Product = $"商品_{userId}",
                TotalAmount = amount,
                Status = priority,
                CreatedAt = DateTime.Now
            };
            _orders.Add(order);
            return order;
        }

        // ─── 场景 2：引用类型参数（复杂对象）→ 返回集合 ───
        /// <summary>
        /// 搜索订单。
        /// 自动生成: record SearchOrdersArgs(OrderQuery Query)
        /// Handler 拿到: args.Query.Keyword, args.Query.PageIndex 等嵌套属性, result(List&lt;OrderResult&gt;)
        /// </summary>
        [AutoIntercept(InterceptType.Log | InterceptType.Cache, CacheDurationSeconds = 60)]
        [CustomIntercept(typeof(SearchOrdersMetricsHandler))]
        public List<OrderResult> SearchOrders(OrderQuery query)
        {
            var result = _orders.AsEnumerable();
            if (!string.IsNullOrEmpty(query.Keyword))
                result = result.Where(o => o.Product.Contains(query.Keyword));
            if (query.Status.HasValue)
                result = result.Where(o => o.Status == query.Status.Value);
            return result.Skip((query.PageIndex - 1) * query.PageSize).Take(query.PageSize).ToList();
        }

        // ─── 场景 3：集合类型参数（嵌套在请求对象中）→ 返回复杂结果 ───
        /// <summary>
        /// 批量发货。
        /// 自动生成: record BatchShipArgs(BatchShipRequest Request)
        /// Handler 拿到: args.Request.OrderIds(List&lt;int&gt;), args.Request.Carrier, result(BatchResult)
        /// </summary>
        [AutoIntercept(InterceptType.Log | InterceptType.Retry, MaxRetryCount = 2)]
        [CustomIntercept(typeof(BatchOperationHandler))]
        public async Task<BatchResult> BatchShipAsync(BatchShipRequest request)
        {
            await Task.Delay(100);
            var result = new BatchResult();
            foreach (var id in request.OrderIds)
            {
                var order = _orders.FirstOrDefault(o => o.Id == id);
                if (order != null)
                {
                    order.Status = OrderStatus.Shipped;
                    result.SuccessCount++;
                }
                else
                {
                    result.FailCount++;
                    result.Errors.Add($"订单 {id} 不存在");
                }
            }
            return result;
        }

        // ─── 场景 4：Nullable 值类型 + Guid + DateTime? ───
        /// <summary>
        /// 更新订单状态。
        /// 自动生成: record UpdateOrderStatusArgs(Guid OrderGuid, OrderStatus? NewStatus, DateTime? OperatedAt)
        /// Handler 拿到: args.OrderGuid(Guid), args.NewStatus(OrderStatus?), args.OperatedAt(DateTime?)
        /// </summary>
        [AutoIntercept(InterceptType.Log)]
        [CustomIntercept(typeof(StatusChangeHandler))]
        public bool UpdateOrderStatus(Guid orderGuid, OrderStatus? newStatus, DateTime? operatedAt)
        {
            // 模拟通过 Guid 查找并更新
            if (_orders.Count == 0) return false;
            if (newStatus.HasValue)
                _orders[0].Status = newStatus.Value;
            return true;
        }

        // ─── 场景 5：多参数混合（string + int + bool + decimal?）→ 返回值类型 ───
        /// <summary>
        /// 调整价格。
        /// 自动生成: record AdjustPriceArgs(string Sku, int WarehouseId, bool ForceUpdate, decimal? NewPrice)
        /// Handler 拿到: args.Sku(string), args.WarehouseId(int), args.ForceUpdate(bool), args.NewPrice(decimal?), result(decimal)
        /// </summary>
        [AutoIntercept(InterceptType.Log | InterceptType.Validate)]
        [CustomIntercept(typeof(PriceAdjustAuditHandler))]
        public decimal AdjustPrice(string sku, int warehouseId, bool forceUpdate, decimal? newPrice)
        {
            var basePrice = 99.9m;
            return newPrice ?? basePrice * (forceUpdate ? 0.8m : 1.0m);
        }

        // ─── 场景 6：无参数方法 → 返回集合 ───
        /// <summary>
        /// 获取所有订单统计。
        /// 自动生成: record GetStatisticsArgs;  （空 record）
        /// </summary>
        [AutoIntercept(InterceptType.Log | InterceptType.Cache, CacheDurationSeconds = 120)]
        public Dictionary<OrderStatus, int> GetStatistics()
        {
            return _orders.GroupBy(o => o.Status).ToDictionary(g => g.Key, g => g.Count());
        }

        // ─── 场景 7：异步 + 值类型返回（Task&lt;int&gt;）───
        /// <summary>
        /// 获取订单数量。
        /// 自动生成: record CountByStatusArgs(OrderStatus Status)
        /// Handler 拿到: args.Status(enum), result(int)
        /// </summary>
        [AutoIntercept(InterceptType.Log | InterceptType.Metrics)]
        public async Task<int> CountByStatusAsync(OrderStatus status)
        {
            await Task.Delay(50);
            return _orders.Count(o => o.Status == status);
        }

        // ─── 场景 8：数组参数（int[]）───
        /// <summary>
        /// 批量取消。
        /// 自动生成: record BatchCancelArgs(int[] OrderIds, string Reason)
        /// Handler 拿到: args.OrderIds(int[]), args.Reason(string)
        /// </summary>
        [AutoIntercept(InterceptType.Log)]
        public int BatchCancel(int[] orderIds, string reason)
        {
            int count = 0;
            foreach (var id in orderIds)
            {
                var order = _orders.FirstOrDefault(o => o.Id == id);
                if (order != null) { order.Status = OrderStatus.Cancelled; count++; }
            }
            return count;
        }

        // ─── 无标记 → 透传，不拦截 ───
        public string GetServiceName() => "OrderServiceV2";
    }
    /// <summary>
    /// OrderServiceV2.CreateOrder 方法的拦截处理器。
    /// 参数: (int userId, decimal amount, OrderStatus priority)
    /// 返回值: OrderResult
    /// 
    /// 由 AutoCode 自动生成骨架，Args 类型 'CreateOrderArgs' 由编译时生成器自动产出。
    /// </summary>
    public class CreateOrderHandler : MethodHandlerBase<CreateOrderArgs, OrderResult>
    {
        /// <summary>
        /// 方法执行前调用。
        /// 可访问强类型参数: args.XXX
        /// 设置 ctx.ShortCircuit = true + ctx.Result 可跳过方法执行。
        /// </summary>
        public override void OnBefore(CreateOrderArgs args, MethodContext ctx)
        {
            // TODO: 前置逻辑（参数校验、权限检查、并发计数、缓存查询）
            // 示例: Console.WriteLine($"[{ctx.MethodName}] 开始执行");
        }

        /// <summary>
        /// 方法成功执行后调用。
        /// result 是强类型返回值，可直接做数据处理。
        /// </summary>
        public override void OnAfter(CreateOrderArgs args, OrderResult result, MethodContext ctx)
        {
            // TODO: 后置逻辑（指标上报、审计日志、数据收集、缓存写入）
            // 示例: Console.WriteLine($"[{ctx.MethodName}] 完成, 耗时={ctx.Elapsed.TotalMilliseconds:F1}ms");
        }

        /// <summary>
        /// 方法抛出异常时调用。
        /// 设置 ctx.Handled = true 可吞掉异常（降级处理）。
        /// </summary>
        public override void OnException(CreateOrderArgs args, Exception ex, MethodContext ctx)
        {
            // TODO: 异常逻辑（告警通知、错误统计、熔断计数）
            // 示例: Console.WriteLine($"[{ctx.MethodName}] 异常: {ex.Message}");
        }
    }
}
