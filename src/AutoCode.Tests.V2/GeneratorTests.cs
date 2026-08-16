using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using AutoCode.Plugins.Mapper;
using AutoCode.Plugins.DependencyInjection;
using AutoCode.Plugins.Validation;
using Xunit;

namespace AutoCode.Tests.V2
{
    /// <summary>
    /// 生成器集成测试 - 验证各插件在真实编译环境中的输出
    /// </summary>
    public class GeneratorTests
    {
        /// <summary>
        /// 测试用配置提供器：V2 生成器默认关闭（避免与 V1 重复生成），测试需显式启用 AutoCode_EnableV2。
        /// </summary>
        private sealed class EnableV2OptionsProvider : AnalyzerConfigOptionsProvider
        {
            private sealed class Options : AnalyzerConfigOptions
            {
                public override bool TryGetValue(string key, out string value)
                {
                    if (key == "build_property.AutoCode_EnableV2")
                    {
                        value = "true";
                        return true;
                    }
                    value = "";
                    return false;
                }
            }

            private static readonly AnalyzerConfigOptions s_options = new Options();

            public override AnalyzerConfigOptions GlobalOptions => s_options;
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_options;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_options;
        }

        private static (Compilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
            IIncrementalGenerator generator, string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            };

            // 添加运行时引用
            var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));

            var compilation = CSharpCompilation.Create("TestAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, optionsProvider: new EnableV2OptionsProvider());
            driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

            return (outputCompilation, diagnostics);
        }

        #region Mapper Generator Tests

        [Fact]
        public void MapperGenerator_WithMapFrom_GeneratesExtensionMethods()
        {
            var source = @"
namespace AutoCode.Model
{
    public class MapFromAttribute : System.Attribute
    {
        public System.Type SourceType { get; }
        public MapFromAttribute(System.Type t) { SourceType = t; }
    }
    public class MapPropertyAttribute : System.Attribute
    {
        public string SourceName { get; }
        public MapPropertyAttribute(string s) { SourceName = s; }
    }
    public class MapIgnoreAttribute : System.Attribute { }
}

namespace TestApp
{
    using AutoCode.Model;

    public class UserEntity
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
    }

    [MapFrom(typeof(UserEntity))]
    public class UserDto
    {
        public int Id { get; set; }
        [MapProperty(""UserName"")]
        public string Name { get; set; }
        public string Email { get; set; }
    }
}";
            var (output, _) = RunGenerator(new MapperGenerator(), source);
            var generatedTrees = output.SyntaxTrees.Skip(1).ToList();

            Assert.True(generatedTrees.Count > 0, "Should generate at least one file");

            var generatedCode = generatedTrees[0].ToString();
            Assert.Contains("MapTo", generatedCode);
            Assert.Contains("UserDto", generatedCode);
            Assert.Contains("source.UserName", generatedCode); // MapProperty 映射
        }

        [Fact]
        public void MapperGenerator_LegacyMapper_GeneratesCopyTo()
        {
            var source = @"
namespace AutoCode.Model.AutoMapperModel
{
    public class MapperAttribute : System.Attribute { }
}

namespace TestApp
{
    using AutoCode.Model.AutoMapperModel;

    [Mapper]
    public class Config
    {
        public string Name { get; set; }
        public int Value { get; set; }
    }
}";
            var (output, _) = RunGenerator(new MapperGenerator(), source);
            var generatedTrees = output.SyntaxTrees.Skip(1).ToList();

            Assert.True(generatedTrees.Count > 0, "Should generate mapping for legacy [Mapper]");
            var code = generatedTrees[0].ToString();
            Assert.Contains("target.Name = source.Name", code);
            Assert.Contains("target.Value = source.Value", code);
        }

        #endregion

        #region DI Generator Tests

        [Fact]
        public void DIGenerator_WithScopedInterface_GeneratesRegistration()
        {
            var source = @"
namespace TestApp
{
    public interface IScoped { }
    public interface IUserService { }

    public class UserService : IUserService, IScoped
    {
        public void DoWork() { }
    }
}";
            var (output, _) = RunGenerator(new DependencyInjectionGenerator(), source);
            var generatedTrees = output.SyntaxTrees.Skip(1).ToList();

            Assert.True(generatedTrees.Count > 0, "Should generate DI registration");
            var code = generatedTrees[0].ToString();
            Assert.Contains("TryAddScoped", code);
            Assert.Contains("IUserService", code);
            Assert.Contains("UserService", code);
        }

        [Fact]
        public void DIGenerator_MultipleLifetimes_GeneratesAll()
        {
            var source = @"
namespace TestApp
{
    public interface IScoped { }
    public interface ISingleton { }
    public interface ITransient { }
    public interface IServiceA { }
    public interface IServiceB { }
    public interface IServiceC { }

    public class ServiceA : IServiceA, IScoped { }
    public class ServiceB : IServiceB, ISingleton { }
    public class ServiceC : IServiceC, ITransient { }
}";
            var (output, _) = RunGenerator(new DependencyInjectionGenerator(), source);
            var generatedTrees = output.SyntaxTrees.Skip(1).ToList();

            Assert.True(generatedTrees.Count > 0);
            var code = generatedTrees[0].ToString();
            Assert.Contains("TryAddScoped", code);
            Assert.Contains("TryAddSingleton", code);
            Assert.Contains("TryAddTransient", code);
        }

        #endregion

        #region Validation Generator Tests

        [Fact]
        public void ValidationGenerator_WithRequired_GeneratesCheck()
        {
            var source = @"
namespace System.ComponentModel.DataAnnotations
{
    public class RequiredAttribute : System.Attribute { }
    public class MaxLengthAttribute : System.Attribute
    {
        public MaxLengthAttribute(int len) { }
    }
}

namespace AutoCode.Model
{
    public class AutoValidatorAttribute : System.Attribute { }
}

namespace TestApp
{
    using AutoCode.Model;
    using System.ComponentModel.DataAnnotations;

    [AutoValidator]
    public class CreateUserRequest
    {
        [Required]
        [MaxLength(50)]
        public string Name { get; set; }
    }
}";
            var (output, _) = RunGenerator(new ValidationGenerator(), source);
            var generatedTrees = output.SyntaxTrees.Skip(1).ToList();

            Assert.True(generatedTrees.Count > 0, "Should generate validator");
            var code = generatedTrees[0].ToString();
            Assert.Contains("CreateUserRequestValidator", code);
            Assert.Contains("IsNullOrWhiteSpace", code);
            Assert.Contains("Length > 50", code);
        }

        #endregion
    }
}
