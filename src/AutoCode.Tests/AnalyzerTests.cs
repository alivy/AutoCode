using AutoCode.Analyzers.Analyzers;
using AutoCode.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AutoCode.Tests
{
    /// <summary>
    /// Analyzer 测试
    /// </summary>
    public class AnalyzerTests
    {
        /// <summary>
        /// 创建带有 AutoCode.Model 引用的分析器测试
        /// </summary>
        private static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateAnalyzerTest<TAnalyzer>(string testCode)
            where TAnalyzer : Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer, new()
        {
            var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
            {
                TestCode = testCode
            };
            // 添加 AutoCode.Model 程序集引用（包含 AutoInterface/AutoIgnore 特性）
            test.TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(AutoCode.Model.InterfaceAttribute.AutoInterfaceAttribute).Assembly.Location));
            return test;
        }
        // ==================== AC001: MissingAutoInterface ====================

        /// <summary>
        /// 有接口无 [AutoInterface] → 触发 AC001 警告
        /// </summary>
        [Fact]
        public async Task AC001_ClassWithInterface_NoAutoInterface_TriggersWarning()
        {
            var test = """
                namespace TestApp
                {
                    public interface IMyService
                    {
                        int GetValue();
                    }

                    public class {|#0:MyService|} : IMyService
                    {
                        public int GetValue() => 42;
                    }
                }
                """;

            var expected = new DiagnosticResult(AutoCodeDiagnosticDescriptors.MissingAutoInterface)
                .WithLocation(0)
                .WithArguments("MyService", "IMyService");

            var analyzerTest = CreateAnalyzerTest<MissingAutoInterfaceAnalyzer>(test);
            analyzerTest.ExpectedDiagnostics.Add(expected);
            await analyzerTest.RunAsync();
        }

        /// <summary>
        /// 有接口有 [AutoInterface] → 不触发
        /// </summary>
        [Fact]
        public async Task AC001_ClassWithInterface_HasAutoInterface_NoWarning()
        {
            var test = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    public interface IMyService
                    {
                        int GetValue();
                    }

                    [AutoInterface]
                    public class MyService : IMyService
                    {
                        public int GetValue() => 42;
                    }
                }
                """;

            await CreateAnalyzerTest<MissingAutoInterfaceAnalyzer>(test).RunAsync();
        }

        /// <summary>
        /// 抽象类不触发 AC001
        /// </summary>
        [Fact]
        public async Task AC001_AbstractClass_NoWarning()
        {
            var test = """
                namespace TestApp
                {
                    public interface IMyService
                    {
                        int GetValue();
                    }

                    public abstract class MyService : IMyService
                    {
                        public int GetValue() => 42;
                    }
                }
                """;

            await CreateAnalyzerTest<MissingAutoInterfaceAnalyzer>(test).RunAsync();
        }

        // ==================== AC002: InterfaceDivergence ====================

        /// <summary>
        /// [AutoInterface] 类有额外公共成员不在接口中 → 触发 AC002
        /// </summary>
        [Fact]
        public async Task AC002_ExtraPublicMember_TriggersInfo()
        {
            var test = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    public interface IMyService
                    {
                        int GetValue();
                    }

                    [AutoInterface]
                    public class MyService : IMyService
                    {
                        public int GetValue() => 42;
                        public void {|#0:ExtraMethod|}() { }
                    }
                }
                """;

            var expected = new DiagnosticResult(AutoCodeDiagnosticDescriptors.InterfaceDivergence)
                .WithLocation(0)
                .WithArguments("MyService", "ExtraMethod", "IMyService");

            var analyzerTest = CreateAnalyzerTest<InterfaceDivergenceAnalyzer>(test);
            analyzerTest.ExpectedDiagnostics.Add(expected);
            await analyzerTest.RunAsync();
        }

        /// <summary>
        /// [AutoInterface] 类成员与接口一致 → 不触发
        /// </summary>
        [Fact]
        public async Task AC002_MembersMatch_NoInfo()
        {
            var test = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    public interface IMyService
                    {
                        int GetValue();
                    }

                    [AutoInterface]
                    public class MyService : IMyService
                    {
                        public int GetValue() => 42;
                    }
                }
                """;

            await CreateAnalyzerTest<InterfaceDivergenceAnalyzer>(test).RunAsync();
        }

        // ==================== AC003: UnusedAutoIgnore ====================

        /// <summary>
        /// [AutoIgnore] 在 private 方法上 → 触发 AC003 警告
        /// </summary>
        [Fact]
        public async Task AC003_AutoIgnoreOnPrivateMethod_TriggersWarning()
        {
            var test = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    [AutoInterface]
                    public class MyService
                    {
                        public int GetValue() => 42;

                        [AutoIgnore]
                        private void {|#0:Helper|}() { }
                    }
                }
                """;

            var expected = new DiagnosticResult(AutoCodeDiagnosticDescriptors.UnusedAutoIgnore)
                .WithLocation(0)
                .WithArguments("Helper");

            var analyzerTest = CreateAnalyzerTest<UnusedAutoIgnoreAnalyzer>(test);
            analyzerTest.ExpectedDiagnostics.Add(expected);
            await analyzerTest.RunAsync();
        }

        /// <summary>
        /// [AutoIgnore] 在 public 方法上 → 不触发
        /// </summary>
        [Fact]
        public async Task AC003_AutoIgnoreOnPublicMethod_NoWarning()
        {
            var test = """
                using AutoCode.Model.InterfaceAttribute;

                namespace TestApp
                {
                    [AutoInterface]
                    public class MyService
                    {
                        public int GetValue() => 42;

                        [AutoIgnore]
                        public void Helper() { }
                    }
                }
                """;

            await CreateAnalyzerTest<UnusedAutoIgnoreAnalyzer>(test).RunAsync();
        }
    }
}
