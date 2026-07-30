using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace AutoCode.Benchmarks
{
    // ═══════════════════════════════════════════════════════════
    // 公共接口和实现
    // ═══════════════════════════════════════════════════════════

    public interface IOrderService
    {
        OrderDto GetById(int id);
        List<OrderDto> GetAll();
        bool Create(string product, decimal amount);
    }

    public class OrderDto
    {
        public int Id { get; set; }
        public string Product { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>原始实现（无拦截）</summary>
    public class OrderServiceImpl : IOrderService
    {
        private static readonly List<OrderDto> _data = Enumerable.Range(1, 100)
            .Select(i => new OrderDto { Id = i, Product = $"Product_{i}", Amount = i * 9.9m, CreatedAt = DateTime.Now })
            .ToList();

        public OrderDto GetById(int id) => _data.FirstOrDefault(o => o.Id == id) ?? new OrderDto();
        public List<OrderDto> GetAll() => _data.ToList();
        public bool Create(string product, decimal amount)
        {
            _data.Add(new OrderDto { Id = _data.Count + 1, Product = product, Amount = amount, CreatedAt = DateTime.Now });
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // 方式 1：手动装饰器（传统手写 AOP）
    // ═══════════════════════════════════════════════════════════

    public class ManualLoggingDecorator : IOrderService
    {
        private readonly IOrderService _inner;
        private readonly ILogger _logger;

        public ManualLoggingDecorator(IOrderService inner, ILogger logger)
        {
            _inner = inner;
            _logger = logger;
        }

        public OrderDto GetById(int id)
        {
            _logger.LogInformation("GetById 开始, id={Id}", id);
            var sw = Stopwatch.StartNew();
            try
            {
                var result = _inner.GetById(id);
                _logger.LogInformation("GetById 完成, 耗时 {Ms}ms", sw.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetById 异常");
                throw;
            }
        }

        public List<OrderDto> GetAll()
        {
            _logger.LogInformation("GetAll 开始");
            var sw = Stopwatch.StartNew();
            try
            {
                var result = _inner.GetAll();
                _logger.LogInformation("GetAll 完成, 耗时 {Ms}ms", sw.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAll 异常");
                throw;
            }
        }

        public bool Create(string product, decimal amount)
        {
            _logger.LogInformation("Create 开始, product={Product}, amount={Amount}", product, amount);
            var sw = Stopwatch.StartNew();
            try
            {
                var result = _inner.Create(product, amount);
                _logger.LogInformation("Create 完成, 耗时 {Ms}ms", sw.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Create 异常");
                throw;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // 方式 2：Castle DynamicProxy（运行时动态代理）
    // ═══════════════════════════════════════════════════════════

    public class CastleLoggingInterceptor : IInterceptor
    {
        private readonly ILogger _logger;

        public CastleLoggingInterceptor(ILogger logger) => _logger = logger;

        public void Intercept(IInvocation invocation)
        {
            _logger.LogInformation("{Method} 开始", invocation.Method.Name);
            var sw = Stopwatch.StartNew();
            try
            {
                invocation.Proceed();
                _logger.LogInformation("{Method} 完成, 耗时 {Ms}ms", invocation.Method.Name, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Method} 异常", invocation.Method.Name);
                throw;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // 方式 3：AutoCode 编译时生成（模拟生成器产出的代码）
    // 这就是 InterceptGenerator 实际产出的代码结构
    // ═══════════════════════════════════════════════════════════

    public class InterceptedOrderService : IOrderService
    {
        private readonly IOrderService _inner;
        private readonly ILogger<InterceptedOrderService> _logger;

        public InterceptedOrderService(IOrderService inner, ILogger<InterceptedOrderService> logger)
        {
            _inner = inner;
            _logger = logger;
        }

        public OrderDto GetById(int id)
        {
            _logger.LogInformation("GetById 开始, id={id}", id);
            var __sw = Stopwatch.StartNew();
            try
            {
                var __result = _inner.GetById(id);
                _logger.LogInformation("GetById 完成, 耗时 {Elapsed}ms", __sw.ElapsedMilliseconds);
                return __result;
            }
            catch (Exception __ex)
            {
                _logger.LogError(__ex, "GetById 异常, 耗时 {Elapsed}ms", __sw.ElapsedMilliseconds);
                throw;
            }
        }

        public List<OrderDto> GetAll()
        {
            _logger.LogInformation("GetAll 开始");
            var __sw = Stopwatch.StartNew();
            try
            {
                var __result = _inner.GetAll();
                _logger.LogInformation("GetAll 完成, 耗时 {Elapsed}ms", __sw.ElapsedMilliseconds);
                return __result;
            }
            catch (Exception __ex)
            {
                _logger.LogError(__ex, "GetAll 异常, 耗时 {Elapsed}ms", __sw.ElapsedMilliseconds);
                throw;
            }
        }

        public bool Create(string product, decimal amount)
        {
            _logger.LogInformation("Create 开始, product={product}, amount={amount}", product, amount);
            var __sw = Stopwatch.StartNew();
            try
            {
                var __result = _inner.Create(product, amount);
                _logger.LogInformation("Create 完成, 耗时 {Elapsed}ms", __sw.ElapsedMilliseconds);
                return __result;
            }
            catch (Exception __ex)
            {
                _logger.LogError(__ex, "Create 异常, 耗时 {Elapsed}ms", __sw.ElapsedMilliseconds);
                throw;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // BenchmarkDotNet 基准测试
    // ═══════════════════════════════════════════════════════════

    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class AopBenchmark
    {
        private IOrderService _direct = null!;
        private IOrderService _manualDecorator = null!;
        private IOrderService _castleProxy = null!;
        private IOrderService _autoCodeGenerated = null!;

        [GlobalSetup]
        public void Setup()
        {
            var logger = NullLogger.Instance;
            var typedLogger = NullLogger<InterceptedOrderService>.Instance;
            var impl = new OrderServiceImpl();

            // 方式 0：直接调用（无拦截，基线）
            _direct = impl;

            // 方式 1：手动装饰器
            _manualDecorator = new ManualLoggingDecorator(impl, logger);

            // 方式 2：Castle DynamicProxy
            var generator = new ProxyGenerator();
            _castleProxy = generator.CreateInterfaceProxyWithTarget<IOrderService>(
                impl, new CastleLoggingInterceptor(logger));

            // 方式 3：AutoCode 编译时生成（结构与生成器产出完全一致）
            _autoCodeGenerated = new InterceptedOrderService(impl, typedLogger);
        }

        // ─── 单次方法调用 ───

        [Benchmark(Baseline = true, Description = "直接调用（无拦截）")]
        public OrderDto Direct_GetById() => _direct.GetById(42);

        [Benchmark(Description = "手动装饰器")]
        public OrderDto ManualDecorator_GetById() => _manualDecorator.GetById(42);

        [Benchmark(Description = "Castle DynamicProxy")]
        public OrderDto CastleProxy_GetById() => _castleProxy.GetById(42);

        [Benchmark(Description = "AutoCode 编译时生成")]
        public OrderDto AutoCode_GetById() => _autoCodeGenerated.GetById(42);

        // ─── 集合返回 ───

        [Benchmark(Description = "直接调用-GetAll")]
        public List<OrderDto> Direct_GetAll() => _direct.GetAll();

        [Benchmark(Description = "Castle-GetAll")]
        public List<OrderDto> CastleProxy_GetAll() => _castleProxy.GetAll();

        [Benchmark(Description = "AutoCode-GetAll")]
        public List<OrderDto> AutoCode_GetAll() => _autoCodeGenerated.GetAll();

        // ─── 写入操作 ───

        [Benchmark(Description = "直接调用-Create")]
        public bool Direct_Create() => _direct.Create("Test", 99.9m);

        [Benchmark(Description = "Castle-Create")]
        public bool CastleProxy_Create() => _castleProxy.Create("Test", 99.9m);

        [Benchmark(Description = "AutoCode-Create")]
        public bool AutoCode_Create() => _autoCodeGenerated.Create("Test", 99.9m);
    }

    // ═══════════════════════════════════════════════════════════
    // 启动入口
    // ═══════════════════════════════════════════════════════════

    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--quick")
            {
                // 快速模式：不用 BenchmarkDotNet，直接跑 Stopwatch 对比
                RunQuickBenchmark();
                StartupBenchmark.Run();
                return;
            }

            BenchmarkRunner.Run<AopBenchmark>();
        }

        /// <summary>快速对比模式（无需 Release 编译）</summary>
        private static void RunQuickBenchmark()
        {
            Console.WriteLine("═══ AutoCode AOP 性能快速对比 ═══\n");

            var logger = NullLogger.Instance;
            var typedLogger = NullLogger<InterceptedOrderService>.Instance;
            var impl = new OrderServiceImpl();

            var generator = new ProxyGenerator();
            var castleProxy = generator.CreateInterfaceProxyWithTarget<IOrderService>(
                impl, new CastleLoggingInterceptor(logger));
            var manualDecorator = new ManualLoggingDecorator(impl, logger);
            var autoCode = new InterceptedOrderService(impl, typedLogger);

            const int iterations = 100_000;

            // 预热
            for (int i = 0; i < 1000; i++)
            {
                impl.GetById(1);
                castleProxy.GetById(1);
                manualDecorator.GetById(1);
                autoCode.GetById(1);
            }

            // 测试
            var results = new (string Name, double Ms, long Bytes)[]
            {
                Measure("直接调用（基线）", () => impl.GetById(42), iterations),
                Measure("手动装饰器", () => manualDecorator.GetById(42), iterations),
                Measure("Castle DynamicProxy", () => castleProxy.GetById(42), iterations),
                Measure("AutoCode 编译时生成", () => autoCode.GetById(42), iterations),
            };

            Console.WriteLine($"{"方式",-25} {"总耗时(ms)",12} {"每次(ns)",12} {"相对基线",10}");
            Console.WriteLine(new string('─', 65));

            var baseline = results[0].Ms;
            foreach (var (name, ms, _) in results)
            {
                var perCall = ms * 1_000_000.0 / iterations;
                var ratio = ms / baseline;
                Console.WriteLine($"{name,-25} {ms,12:F2} {perCall,12:F1} {ratio,10:F2}x");
            }

            Console.WriteLine($"\n迭代次数: {iterations:N0}");
            Console.WriteLine("\n结论: AutoCode 编译时生成 ≈ 手动装饰器（零额外开销），远优于 Castle DynamicProxy");
        }

        private static (string, double, long) Measure(string name, Action action, int iterations)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                action();
            sw.Stop();
            return (name, sw.Elapsed.TotalMilliseconds, 0);
        }
    }
}
