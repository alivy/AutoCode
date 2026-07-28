using AutoCode.DependencyInjection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AutoCode.Tests
{
    /// <summary>
    /// 编译时依赖注入生成器测试
    /// </summary>
    public class DependencyInjectionGeneratorTests
    {
        /// <summary>
        /// 测试 IScoped 服务生成 TryAddScoped 注册
        /// </summary>
        [Fact]
        public void DI_GeneratesScopedRegistration()
        {
            var source = """
                using Microsoft.Extensions.DependencyInjection;

                namespace TestApp
                {
                    public interface IMyService { }

                    public class MyService : IMyService, IScoped { }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0, "应该生成 DI 注册代码");

            var generatedSource = generatedTrees[0].ToString();
            Assert.Contains("AddAutoDI", generatedSource);
            Assert.Contains("TryAddScoped", generatedSource);
            Assert.Contains("IMyService", generatedSource);
            Assert.Contains("MyService", generatedSource);
        }

        /// <summary>
        /// 测试 ISingleton 服务生成 TryAddSingleton 注册
        /// </summary>
        [Fact]
        public void DI_GeneratesSingletonRegistration()
        {
            var source = """
                using Microsoft.Extensions.DependencyInjection;

                namespace TestApp
                {
                    public interface ICacheService { }

                    public class CacheService : ICacheService, ISingleton { }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0);

            var generatedSource = generatedTrees[0].ToString();
            Assert.Contains("TryAddSingleton", generatedSource);
        }

        /// <summary>
        /// 测试 ITransient 服务生成 TryAddTransient 注册
        /// </summary>
        [Fact]
        public void DI_GeneratesTransientRegistration()
        {
            var source = """
                using Microsoft.Extensions.DependencyInjection;

                namespace TestApp
                {
                    public interface ILoggerService { }

                    public class LoggerService : ILoggerService, ITransient { }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0);

            var generatedSource = generatedTrees[0].ToString();
            Assert.Contains("TryAddTransient", generatedSource);
        }

        /// <summary>
        /// 测试无生命周期接口的类不生成注册
        /// </summary>
        [Fact]
        public void DI_NoLifetimeInterface_NoRegistration()
        {
            var source = """
                namespace TestApp
                {
                    public interface IMyService { }

                    public class MyService : IMyService { }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.Empty(generatedTrees);
        }

        /// <summary>
        /// 测试多个服务接口注册
        /// </summary>
        [Fact]
        public void DI_MultipleServiceInterfaces()
        {
            var source = """
                using Microsoft.Extensions.DependencyInjection;

                namespace TestApp
                {
                    public interface IReadService { }
                    public interface IWriteService { }

                    public class DataService : IReadService, IWriteService, IScoped { }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0);

            var generatedSource = generatedTrees[0].ToString();
            Assert.Contains("IReadService", generatedSource);
            Assert.Contains("IWriteService", generatedSource);
        }

        private static (System.Collections.Immutable.ImmutableArray<Diagnostic>, System.Collections.Generic.List<SyntaxTree>) RunGenerator(string source)
        {
            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { CSharpSyntaxTree.ParseText(source) },
                GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new DependencyInjectionGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);

            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

            var runResult = driver.GetRunResult();
            return (runResult.Diagnostics, runResult.GeneratedTrees.ToList());
        }

        private static IEnumerable<MetadataReference> GetReferences()
        {
            var dotnetRoot = System.Environment.GetEnvironmentVariable("DOTNET_ROOT")
                ?? @"C:\Program Files\dotnet";
            var sdkPacks = System.IO.Path.Combine(dotnetRoot, "packs");
            var netRefDir = System.IO.Directory.GetDirectories(sdkPacks, "Microsoft.NETCore.App.Ref*")
                .OrderByDescending(d => d)
                .FirstOrDefault();

            string refPath;
            if (netRefDir != null)
            {
                var version = System.IO.Directory.GetDirectories(netRefDir)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
                refPath = System.IO.Path.Combine(version!, "ref", "net8.0");
            }
            else
            {
                refPath = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            }

            foreach (var dll in System.IO.Directory.GetFiles(refPath, "*.dll"))
            {
                yield return MetadataReference.CreateFromFile(dll);
            }

            // Microsoft.Extensions.DependencyInjection.Abstractions
            var diAssembly = typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly;
            yield return MetadataReference.CreateFromFile(diAssembly.Location);
        }
    }
}
