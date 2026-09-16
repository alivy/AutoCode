using AutoCode.Plugins.Cascade;
using AutoCode.Plugins.Crud;
using AutoCode.Plugins.WebApi;
using AutoCode.Tests.V2.Infrastructure;
using VerifyXunit;

namespace AutoCode.Tests.V2.Snapshots
{
    /// <summary>ControllerGenerator (V2) 快照测试。</summary>
    public class ControllerGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task AutoController_InfersHttpVerbs()
        {
            var source = """
                using AutoCode.Model;
                using System.Collections.Generic;
                namespace TestApp
                {
                    [AutoController(RoutePrefix = "api/users")]
                    public class UserService
                    {
                        public List<string> GetAll() => new List<string>();
                        public string? GetById(int id) => null;
                        public string Create(string name) => name;
                        public void Delete(int id) { }
                    }
                }
                """;

            var run = RunGenerator(new ControllerGenerator(), source,
                extraReferences: AspNetCoreReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }

    /// <summary>CrudGenerator (V2) 快照测试。</summary>
    public class CrudGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task AutoCrud_GeneratesFullStack()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    [AutoCrud]
                    public class Product
                    {
                        public int Id { get; set; }
                        public string Name { get; set; } = "";
                        public decimal Price { get; set; }
                    }
                }
                """;

            var run = RunGenerator(new CrudGenerator(), source,
                extraReferences: AspNetCoreReferences().Concat(EfCoreReferences()));

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }

    /// <summary>CascadeGenerator (V2) 快照测试：一个 [AutoEntity] 触发全链路。</summary>
    public class CascadeGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task AutoEntity_CascadesAllLayers()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    [AutoEntity]
                    public class Order
                    {
                        public int Id { get; set; }
                        public string ProductName { get; set; } = "";
                        public decimal Amount { get; set; }
                    }
                }
                """;

            var run = RunGenerator(new CascadeGenerator(), source,
                extraReferences: AspNetCoreReferences().Concat(EfCoreReferences()));

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }
}
