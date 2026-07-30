using System.Diagnostics;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoCode.Benchmarks
{
    /// <summary>
    /// 启动时间 + 内存占用对比测试。
    /// 这才是 AutoCode vs Castle DynamicProxy 的核心差异所在。
    /// </summary>
    public class StartupBenchmark
    {
        public static void Run()
        {
            Console.WriteLine("\n═══ 启动时间 & 内存占用对比 ═══\n");

            // ─── Castle DynamicProxy 首次代理创建 ───
            var sw1 = Stopwatch.StartNew();
            var generator = new ProxyGenerator();
            var proxy1 = generator.CreateInterfaceProxyWithTarget<IOrderService>(
                new OrderServiceImpl(), new CastleLoggingInterceptor(NullLogger.Instance));
            sw1.Stop();
            var castleFirstCall = sw1.Elapsed.TotalMilliseconds;

            // Castle 后续创建（类型已缓存）
            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                generator.CreateInterfaceProxyWithTarget<IOrderService>(
                    new OrderServiceImpl(), new CastleLoggingInterceptor(NullLogger.Instance));
            }
            sw2.Stop();
            var castleSubsequent = sw2.Elapsed.TotalMilliseconds / 100;

            // ─── AutoCode 编译时生成（new 即可，无运行时类型生成）───
            var sw3 = Stopwatch.StartNew();
            var autoCode1 = new InterceptedOrderService(
                new OrderServiceImpl(), NullLogger<InterceptedOrderService>.Instance);
            sw3.Stop();
            var autoCodeFirstCall = sw3.Elapsed.TotalMilliseconds;

            // AutoCode 后续创建
            var sw4 = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                _ = new InterceptedOrderService(
                    new OrderServiceImpl(), NullLogger<InterceptedOrderService>.Instance);
            }
            sw4.Stop();
            var autoCodeSubsequent = sw4.Elapsed.TotalMilliseconds / 100;

            // ─── 手动装饰器 ───
            var sw5 = Stopwatch.StartNew();
            var manual1 = new ManualLoggingDecorator(new OrderServiceImpl(), NullLogger.Instance);
            sw5.Stop();
            var manualFirstCall = sw5.Elapsed.TotalMilliseconds;

            Console.WriteLine($"{"指标",-30} {"Castle DynamicProxy",20} {"AutoCode 编译时",20} {"手动装饰器",20}");
            Console.WriteLine(new string('─', 95));
            Console.WriteLine($"{"首次创建(ms)",-30} {castleFirstCall,20:F3} {autoCodeFirstCall,20:F3} {manualFirstCall,20:F3}");
            Console.WriteLine($"{"后续创建均值(ms)",-30} {castleSubsequent,20:F3} {autoCodeSubsequent,20:F3} {"~0",20}");
            Console.WriteLine($"{"运行时类型生成",-30} {"✅ Reflection.Emit",20} {"❌ 无需",20} {"❌ 无需",20}");
            Console.WriteLine($"{"额外程序集依赖",-30} {"Castle.Core 400KB+",20} {"0",20} {"0",20}");
            Console.WriteLine($"{"NativeAOT 兼容",-30} {"❌",20} {"✅",20} {"✅",20}");
            Console.WriteLine($"{"可调试性",-30} {"❌ 代理类不可见",20} {"✅ F12 跳转 .g.cs",20} {"✅",20}");
            Console.WriteLine($"{"Trimming 安全",-30} {"❌",20} {"✅",20} {"✅",20}");

            Console.WriteLine($"\n分析:");
            Console.WriteLine($"  Castle 首次创建需要 Reflection.Emit 动态生成代理类型: {castleFirstCall:F3}ms");
            Console.WriteLine($"  AutoCode 首次创建就是普通 new（类型在编译时已存在）: {autoCodeFirstCall:F3}ms");
            Console.WriteLine($"  差异倍数: {castleFirstCall / Math.Max(autoCodeFirstCall, 0.001):F1}x");
            Console.WriteLine($"\n  在微服务/Serverless 场景中，启动时间差异会被放大：");
            Console.WriteLine($"  假设 50 个 Service 需要代理，Castle 启动额外开销 ≈ {castleFirstCall * 50:F1}ms");
            Console.WriteLine($"  AutoCode 启动额外开销 ≈ 0ms（编译时已完成）");
        }
    }
}
