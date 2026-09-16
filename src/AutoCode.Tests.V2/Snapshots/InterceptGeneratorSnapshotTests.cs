using AutoCode.Intercept;
using AutoCode.Tests.V2.Infrastructure;
using VerifyXunit;

namespace AutoCode.Tests.V2.Snapshots
{
    /// <summary>
    /// InterceptGenerator (V1, 编译时 AOP) 快照测试：锁定拦截管线 + Args record + DI 注册的完整文本。
    /// 该生成器 1100+ 行、是全项目最复杂的生成器，快照覆盖是后续一切重构的安全网。
    /// 注意：V1 生成器不经 V2Gate，RunGenerator 需传 enableV2: false。
    /// </summary>
    public class InterceptGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task LogInterceptor_ClassLevel()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService { int GetValue(); }

                    [AutoIntercept(InterceptType.Log)]
                    public class OrderService : IOrderService
                    {
                        public int GetValue() => 42;
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }

        [Fact]
        public async Task CacheAndRetry_GeneratesTypedArgsRecord()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IPaymentService { bool Charge(int orderId, decimal amount); }

                    [AutoIntercept(InterceptType.Cache | InterceptType.Retry, CacheDurationSeconds = 120, MaxRetryCount = 3)]
                    public class PaymentService : IPaymentService
                    {
                        public bool Charge(int orderId, decimal amount) => true;
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }

        [Fact]
        public async Task MethodLevelIntercept_WithSkipIntercept()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IReportService
                    {
                        string Generate(int id);
                        void Cleanup();
                    }

                    public class ReportService : IReportService
                    {
                        [AutoIntercept(InterceptType.Log | InterceptType.Metrics)]
                        public string Generate(int id) => "report";

                        [SkipIntercept]
                        public void Cleanup() { }
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }
}
