using AutoCode.Plugins.Dto;
using AutoCode.Plugins.Mapper;
using AutoCode.Tests.V2.Infrastructure;
using VerifyXunit;

namespace AutoCode.Tests.V2.Snapshots
{
    /// <summary>MapperGenerator (V2) 快照测试。</summary>
    public class MapperGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task MapFrom_WithCustomPropertyMapping()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public class UserEntity
                    {
                        public int Id { get; set; }
                        public string UserName { get; set; } = "";
                        public string Email { get; set; } = "";
                    }

                    [MapFrom(typeof(UserEntity))]
                    public class UserDto
                    {
                        public int Id { get; set; }
                        [MapProperty("UserName")]
                        public string Name { get; set; } = "";
                        public string Email { get; set; } = "";
                    }
                }
                """;

            var run = RunGenerator(new MapperGenerator(), source);

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }

    /// <summary>DtoGenerator (V2) 快照测试。</summary>
    public class DtoGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task AutoDto_WithExclude_GeneratesFromEntity()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public class ProductEntity
                    {
                        public int Id { get; set; }
                        public string Name { get; set; } = "";
                        public decimal Price { get; set; }
                        public string PasswordHash { get; set; } = "";
                    }

                    [AutoDTO(typeof(ProductEntity), Exclude = new[] { "PasswordHash" })]
                    public partial class ProductDto { }
                }
                """;

            var run = RunGenerator(new DtoGenerator(), source);

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }
}
