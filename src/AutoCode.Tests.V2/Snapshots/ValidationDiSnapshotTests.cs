using AutoCode.Plugins.DependencyInjection;
using AutoCode.Plugins.Validation;
using AutoCode.Tests.V2.Infrastructure;
using VerifyXunit;

namespace AutoCode.Tests.V2.Snapshots
{
    /// <summary>ValidationGenerator (V2) 快照测试。</summary>
    public class ValidationGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task AutoValidator_DataAnnotationsRules()
        {
            var source = """
                using AutoCode.Model;
                using System.ComponentModel.DataAnnotations;
                namespace TestApp
                {
                    [AutoValidator]
                    public class CreateUserRequest
                    {
                        [Required]
                        [MaxLength(50)]
                        public string Name { get; set; } = "";

                        [Range(0, 150)]
                        public int Age { get; set; }

                        [EmailAddress]
                        public string? Email { get; set; }
                    }
                }
                """;

            var run = RunGenerator(new ValidationGenerator(), source);

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }

    /// <summary>DependencyInjectionGenerator (V2) 快照测试（DI 接口按名字匹配，存根即可）。</summary>
    public class DependencyInjectionGeneratorSnapshotTests : GeneratorTestBase
    {
        [Fact]
        public async Task LifetimeInterfaces_GenerateRegistration()
        {
            var source = """
                namespace TestApp
                {
                    public interface IScoped { }
                    public interface ISingleton { }
                    public interface IUserService { }

                    public class UserService : IUserService, IScoped { }
                    public class CacheService : ISingleton { }
                }
                """;

            var run = RunGenerator(new DependencyInjectionGenerator(), source,
                extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
    }
}
