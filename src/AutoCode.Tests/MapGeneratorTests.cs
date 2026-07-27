using AutoCode.Map;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AutoCode.Tests
{
    /// <summary>
    /// 映射生成器测试
    /// </summary>
    public class MapGeneratorTests
    {
        /// <summary>
        /// 测试 [Mapper] 标记的类生成 CopyTo 扩展方法
        /// </summary>
        [Fact]
        public void Map_GeneratesCopyToExtension()
        {
            var source = """
                using AutoCode.Model.AutoMapperModel;

                namespace TestApp
                {
                    [Mapper]
                    public class SourceModel
                    {
                        public int Id { get; set; }
                        public string Name { get; set; }
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0, "应该生成映射器源文件");

            var mapperSource = generatedTrees[0].ToString();
            Assert.Contains("SourceModelMapper", mapperSource);
            Assert.Contains("CopyTo", mapperSource);
        }

        /// <summary>
        /// 测试简单类型属性的映射
        /// </summary>
        [Fact]
        public void Map_HandlesSimpleTypes()
        {
            var source = """
                using AutoCode.Model.AutoMapperModel;

                namespace TestApp
                {
                    [Mapper]
                    public class SimpleModel
                    {
                        public int IntProp { get; set; }
                        public string StringProp { get; set; }
                        public bool BoolProp { get; set; }
                        public double DoubleProp { get; set; }
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0);

            var mapperSource = generatedTrees[0].ToString();
            Assert.Contains("target.IntProp = source.IntProp", mapperSource);
            Assert.Contains("target.StringProp = source.StringProp", mapperSource);
            Assert.Contains("target.BoolProp = source.BoolProp", mapperSource);
            Assert.Contains("target.DoubleProp = source.DoubleProp", mapperSource);
        }

        /// <summary>
        /// 测试无 [Mapper] 标记的类不生成映射代码
        /// </summary>
        [Fact]
        public void Map_DoesNotGenerateWithoutAttribute()
        {
            var source = """
                namespace TestApp
                {
                    public class NoMapperModel
                    {
                        public int Id { get; set; }
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.Empty(generatedTrees);
        }

        /// <summary>
        /// 运行映射生成器并返回结果
        /// </summary>
        /// <param name="source">源代码</param>
        /// <returns>诊断信息和生成的语法树列表</returns>
        private static (System.Collections.Immutable.ImmutableArray<Diagnostic>, System.Collections.Generic.List<SyntaxTree>) RunGenerator(string source)
        {
            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { CSharpSyntaxTree.ParseText(source) },
                GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new MapperGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);

            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

            var runResult = driver.GetRunResult();
            return (runResult.Diagnostics, runResult.GeneratedTrees.ToList());
        }

        /// <summary>
        /// 获取编译引用
        /// </summary>
        private static IEnumerable<MetadataReference> GetReferences()
        {
            // 使用 .NET 8 参考程序集
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

            // AutoCode.Model 引用（包含 MapperAttribute）
            var modelAssembly = typeof(AutoCode.Model.AutoMapperModel.MapperAttribute).Assembly;
            yield return MetadataReference.CreateFromFile(modelAssembly.Location);
        }
    }
}
