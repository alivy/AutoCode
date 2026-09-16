using AutoCode.Plugins.Interface;
using AutoCode.Tests.V2.Infrastructure;
using VerifyXunit;

namespace AutoCode.Tests.V2.Snapshots
{
    /// <summary>
    /// InterfaceGenerator (V2) 快照测试：锁定生成接口的完整文本。
    /// 快照文件首次运行生成 *.received.txt，确认无误后重命名为 *.verified.txt 入库。
    /// </summary>
    public class InterfaceGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task BasicClass_MethodsPropertiesAndAutoIgnore()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;
                namespace TestApp
                {
                    [AutoInterface]
                    public class UserService
                    {
                        public int GetId() => 1;
                        public string Name { get; set; } = "";

                        [AutoIgnore]
                        public string Secret() => "hidden";
                    }
                }
                """;

            var run = RunGenerator(new InterfaceGenerator(), source);

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }

        [Fact]
        public async Task GenericAsyncAndNullableMembers()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;
                using System.Threading.Tasks;
                namespace TestApp
                {
                    [AutoInterface]
                    public class OrderService
                    {
                        /// <summary>按 ID 获取订单</summary>
                        public Task<string?> GetAsync(int id) => Task.FromResult<string?>(null);
                        public T Get<T>(int id) where T : class => default!;
                        public event System.Action? OnChanged { add { } remove { } }
                    }
                }
                """;

            var run = RunGenerator(new InterfaceGenerator(), source);

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }

        [Fact]
        public async Task CustomInterfaceName_UsesSpecifiedName()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;
                namespace TestApp
                {
                    [AutoInterface("ICustomUserApi")]
                    public class UserService
                    {
                        public void Ping() { }
                    }
                }
                """;

            var run = RunGenerator(new InterfaceGenerator(), source);

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }
}
