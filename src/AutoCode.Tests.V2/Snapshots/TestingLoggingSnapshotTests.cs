using AutoCode.Plugins.Logging;
using AutoCode.Plugins.Testing;
using AutoCode.Tests.V2.Infrastructure;
using VerifyXunit;

namespace AutoCode.Tests.V2.Snapshots
{
    /// <summary>TestGenerator (V2) 快照测试。</summary>
    public class TestGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task AutoTest_GeneratesAaaStubs()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    [AutoTest]
                    public class Calculator
                    {
                        public int Add(int a, int b) => a + b;
                        public string? Find(int id) => null;
                    }
                }
                """;

            var run = RunGenerator(new TestGenerator(), source,
                extraReferences: XunitReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }

    /// <summary>LogDecoratorGenerator (V2) 快照测试。</summary>
    public class LogDecoratorGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task AutoLog_GeneratesDecorator()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService
                    {
                        int GetValue();
                    }

                    [AutoLog]
                    public class OrderService : IOrderService
                    {
                        public int GetValue() => 42;
                    }
                }
                """;

            var run = RunGenerator(new LogDecoratorGenerator(), source,
                extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }
}
