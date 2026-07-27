using AutoCode.SourceGenerator.InterfaceAutoBuilder;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AutoCode.Tests
{
    /// <summary>
    /// 接口生成器测试
    /// </summary>
    public class InterfaceGeneratorTests
    {
        /// <summary>
        /// 测试 [AutoInterface] 生成默认接口名称 (I{ClassName})
        /// </summary>
        [Fact]
        public void AutoInterface_GeneratesDefaultInterface()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    [AutoInterface]
                    public class SampleService
                    {
                        public int GetValue() => 42;
                        public string GetName() => "test";
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0, "应该生成至少一个源文件");

            var interfaceSource = generatedTrees[0].ToString();
            Assert.Contains("interface ISampleService", interfaceSource);
            Assert.Contains("GetValue", interfaceSource);
            Assert.Contains("GetName", interfaceSource);
        }

        /// <summary>
        /// 测试 [AutoInterface("ICustomName")] 生成自定义接口名称
        /// </summary>
        [Fact]
        public void AutoInterface_GeneratesCustomName()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    [AutoInterface("ICustomService")]
                    public class CustomService
                    {
                        public void Execute() { }
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0);

            var interfaceSource = generatedTrees[0].ToString();
            Assert.Contains("interface ICustomService", interfaceSource);
            Assert.Contains("Execute", interfaceSource);
        }

        /// <summary>
        /// 测试 [AutoIgnore] 忽略方法
        /// </summary>
        [Fact]
        public void AutoInterface_RespectsAutoIgnore()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    [AutoInterface]
                    public class IgnoreService
                    {
                        public int IncludedMethod() => 1;

                        [AutoIgnore]
                        public int ExcludedMethod() => 2;
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0);

            var interfaceSource = generatedTrees[0].ToString();
            Assert.Contains("IncludedMethod", interfaceSource);
            Assert.DoesNotContain("ExcludedMethod", interfaceSource);
        }

        /// <summary>
        /// 测试带参数的方法生成
        /// </summary>
        [Fact]
        public void AutoInterface_GeneratesWithParameters()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    [AutoInterface]
                    public class ParameterService
                    {
                        public int Calculate(int x, int y) => x + y;
                        public void Process(string name, int age) { }
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0);

            var interfaceSource = generatedTrees[0].ToString();
            Assert.Contains("Calculate", interfaceSource);
            Assert.Contains("Process", interfaceSource);
        }

        /// <summary>
        /// 测试无 [AutoInterface] 标记的类不生成接口
        /// </summary>
        [Fact]
        public void AutoInterface_DoesNotGenerateWithoutAttribute()
        {
            var source = """
                namespace TestApp
                {
                    public class NoAttributeService
                    {
                        public int GetValue() => 1;
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.Empty(generatedTrees);
        }

        /// <summary>
        /// 测试属性生成支持
        /// </summary>
        [Fact]
        public void AutoInterface_GeneratesProperties()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    [AutoInterface]
                    public class PropertyService
                    {
                        public int Id { get; set; }
                        public string Name { get; set; }
                        public int ReadOnly { get; }
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0);

            var interfaceSource = generatedTrees[0].ToString();
            Assert.Contains("Id { get; set; }", interfaceSource);
            Assert.Contains("Name { get; set; }", interfaceSource);
            Assert.Contains("ReadOnly { get; }", interfaceSource);
        }

        /// <summary>
        /// 测试泛型方法生成支持
        /// </summary>
        [Fact]
        public void AutoInterface_GeneratesGenericMethods()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    [AutoInterface]
                    public class GenericService
                    {
                        public T Echo<T>(T value) => value;
                        public System.Collections.Generic.List<T> ToList<T>(T item) => new() { item };
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count > 0);

            var interfaceSource = generatedTrees[0].ToString();
            Assert.Contains("Echo<T>", interfaceSource);
            Assert.Contains("ToList<T>", interfaceSource);
        }

        /// <summary>
        /// 测试同一类上多个 [AutoInterface] 特性
        /// </summary>
        [Fact]
        public void AutoInterface_GeneratesMultipleInterfaces()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    [AutoInterface("IFirst")]
                    [AutoInterface("ISecond")]
                    public class MultiService
                    {
                        public void Execute() { }
                    }
                }
                """;

            var (diagnostics, generatedTrees) = RunGenerator(source);

            Assert.Empty(diagnostics);
            Assert.True(generatedTrees.Count >= 2, $"应该生成至少2个接口，实际生成了 {generatedTrees.Count} 个");

            var allSource = string.Join("\n", generatedTrees.Select(t => t.ToString()));
            Assert.Contains("interface IFirst", allSource);
            Assert.Contains("interface ISecond", allSource);
        }

        /// <summary>
        /// 运行接口生成器并返回结果
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

            // 检查编译诊断
            var compileErrors = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            Assert.True(compileErrors.Count == 0,
                $"编译错误: {string.Join(", ", compileErrors.Select(e => e.GetMessage()))}");

            var generator = new InterfaceGenerator();
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
            var refAsmPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "..");
            // 尝试查找 .NET SDK 参考程序集
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
                // 回退到运行时程序集
                refPath = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            }

            // 添加所有 .NET 参考程序集
            foreach (var dll in System.IO.Directory.GetFiles(refPath, "*.dll"))
            {
                yield return MetadataReference.CreateFromFile(dll);
            }

            // AutoCode.Model 引用
            var modelAssembly = typeof(AutoCode.Model.InterfaceAttribute.AutoInterfaceAttribute).Assembly;
            yield return MetadataReference.CreateFromFile(modelAssembly.Location);
        }
    }
}
