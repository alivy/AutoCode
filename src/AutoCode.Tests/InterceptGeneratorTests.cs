using AutoCode.Intercept;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using Xunit;

namespace AutoCode.Tests
{
    /// <summary>
    /// AutoIntercept 编译时 AOP 拦截器生成器测试
    /// </summary>
    public class InterceptGeneratorTests
    {
        [Fact]
        public void LogInterceptor_GeneratesBeforeAfterException()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { int GetValue(); }

                    [AutoIntercept(InterceptType.Log)]
                    public class MyService : IMyService
                    {
                        public int GetValue() => 42;
                    }
                }
                """;

            var (diagnostics, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("InterceptedMyService", generated);
            Assert.Contains("_logger.LogInformation", generated);
            Assert.Contains("_logger.LogError", generated);
            Assert.Contains("Stopwatch", generated);
            Assert.Contains("开始", generated);
            Assert.Contains("完成", generated);
            Assert.Contains("异常", generated);
        }

        [Fact]
        public void CacheInterceptor_GeneratesTryGetAndSet()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { string GetData(int id); }

                    [AutoIntercept(InterceptType.Cache, CacheDurationSeconds = 120)]
                    public class MyService : IMyService
                    {
                        public string GetData(int id) => "data";
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("_cache.TryGetValue", generated);
            Assert.Contains("_cache.Set", generated);
            Assert.Contains("TimeSpan.FromSeconds(120)", generated);
            Assert.Contains("__cacheKey", generated);
        }

        [Fact]
        public void RetryInterceptor_GeneratesExponentialBackoff()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { void DoWork(); }

                    [AutoIntercept(InterceptType.Retry, MaxRetryCount = 5, RetryBaseDelayMs = 200)]
                    public class MyService : IMyService
                    {
                        public void DoWork() { }
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("__attempt", generated);
            Assert.Contains("__attempt < 5", generated);
            Assert.Contains("200", generated);
        }

        [Fact]
        public void CircuitBreaker_GeneratesThresholdCheck()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { void Call(); }

                    [AutoIntercept(InterceptType.CircuitBreaker, CircuitFailureThreshold = 3)]
                    public class MyService : IMyService
                    {
                        public void Call() { }
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("_consecutiveFailures", generated);
            Assert.Contains("_circuitOpenUntil", generated);
            Assert.Contains(">= 3", generated);
            Assert.Contains("熔断器已打开", generated);
        }

        [Fact]
        public void MetricsInterceptor_GeneratesHistogramAndCounter()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { int Get(); }

                    [AutoIntercept(InterceptType.Metrics)]
                    public class MyService : IMyService
                    {
                        public int Get() => 1;
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("Meter", generated);
            Assert.Contains("Histogram<double>", generated);
            Assert.Contains("Counter<long>", generated);
            Assert.Contains("_duration.Record", generated);
            Assert.Contains("_successCount.Add", generated);
        }

        [Fact]
        public void ValidateInterceptor_GeneratesNullChecks()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { void Save(string name, int age); }

                    [AutoIntercept(InterceptType.Validate)]
                    public class MyService : IMyService
                    {
                        public void Save(string name, int age) { }
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("IsNullOrWhiteSpace", generated);
            Assert.Contains("ArgumentException", generated);
        }

        [Fact]
        public void TracingInterceptor_GeneratesActivitySource()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { void Run(); }

                    [AutoIntercept(InterceptType.Tracing)]
                    public class MyService : IMyService
                    {
                        public void Run() { }
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("ActivitySource", generated);
            Assert.Contains("StartActivity", generated);
            Assert.Contains("SetTag", generated);
        }

        [Fact]
        public void ThrottleInterceptor_GeneratesSemaphore()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { void Process(); }

                    [AutoIntercept(InterceptType.Throttle, MaxRequestsPerSecond = 50)]
                    public class MyService : IMyService
                    {
                        public void Process() { }
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("SemaphoreSlim", generated);
            Assert.Contains("50, 50", generated);
            Assert.Contains("_throttle.Release()", generated);
        }

        [Fact]
        public void SkipIntercept_ExcludesMethod()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { void Included(); void Excluded(); }

                    [AutoIntercept(InterceptType.Log)]
                    public class MyService : IMyService
                    {
                        public void Included() { }

                        [SkipIntercept]
                        public void Excluded() { }
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("Included", generated);
            Assert.DoesNotContain("Excluded", generated);
        }

        [Fact]
        public void InterceptOverride_ChangesMethodFlags()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { void Basic(); void Enhanced(); }

                    [AutoIntercept(InterceptType.Log)]
                    public class MyService : IMyService
                    {
                        public void Basic() { }

                        [InterceptOverride(InterceptType.Log | InterceptType.Retry)]
                        public void Enhanced() { }
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            // Enhanced 方法应该有 Retry 逻辑
            Assert.Contains("__attempt", generated);
        }

        [Fact]
        public void AsyncMethod_GeneratesAwaitPattern()
        {
            var source = """
                using AutoCode.Model;
                using System.Threading.Tasks;
                namespace TestApp
                {
                    public interface IMyService { Task<string> GetAsync(int id); }

                    [AutoIntercept(InterceptType.Log | InterceptType.Cache)]
                    public class MyService : IMyService
                    {
                        public Task<string> GetAsync(int id) => Task.FromResult("ok");
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("async", generated);
            Assert.Contains("await _inner.GetAsync", generated);
        }

        [Fact]
        public void NoInterface_ReportsDiagnostic()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    [AutoIntercept(InterceptType.Log)]
                    public class MyService
                    {
                        public void DoWork() { }
                    }
                }
                """;

            var (diagnostics, _) = RunGenerator(source);
            Assert.Contains(diagnostics, d => d.Id == "AC9001");
        }

        [Fact]
        public void DIRegistration_GeneratesExtensionMethod()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { int Get(); }

                    [AutoIntercept(InterceptType.Log)]
                    public class MyService : IMyService
                    {
                        public int Get() => 1;
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            Assert.Contains("AddInterceptedMyService", generated);
            Assert.Contains("IServiceCollection", generated);
            Assert.Contains("AddScoped<global::TestApp.IMyService>", generated);
            Assert.Contains("new InterceptedMyService(", generated);
        }

        [Fact]
        public void CombinedInterceptors_GeneratesFullPipeline()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IMyService { string Get(int id); }

                    [AutoIntercept(InterceptType.Log | InterceptType.Cache | InterceptType.Retry | InterceptType.Metrics | InterceptType.Validate)]
                    public class MyService : IMyService
                    {
                        public string Get(int id) => "data";
                    }
                }
                """;

            var (_, trees) = RunGenerator(source);
            var generated = string.Join("\n", trees.Select(t => t.ToString()));

            // 验证所有拦截器都生成了
            Assert.Contains("_logger.LogInformation", generated);  // Log
            Assert.Contains("_cache.TryGetValue", generated);      // Cache
            Assert.Contains("__attempt", generated);               // Retry
            Assert.Contains("_duration.Record", generated);        // Metrics
            Assert.Contains("Stopwatch", generated);               // 计时
        }

        #region 测试基础设施

        private static (ImmutableArray<Diagnostic> Diagnostics, List<SyntaxTree> Trees) RunGenerator(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { syntaxTree },
                GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new InterceptGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);
            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

            var runResult = driver.GetRunResult();
            return (runResult.Diagnostics, runResult.GeneratedTrees.ToList());
        }

        private static IEnumerable<MetadataReference> GetReferences()
        {
            var refPath = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            foreach (var dll in System.IO.Directory.GetFiles(refPath, "*.dll"))
                yield return MetadataReference.CreateFromFile(dll);

            // AutoCode.Model
            var modelAssembly = typeof(AutoCode.Model.AutoInterceptAttribute).Assembly;
            yield return MetadataReference.CreateFromFile(modelAssembly.Location);
        }

        #endregion
    }
}
