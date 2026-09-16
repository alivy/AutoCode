using System.Collections.Immutable;
using System.Linq;
using AutoCode.Plugins.Interface;
using AutoCode.Tests.V2.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AutoCode.Tests.V2.Snapshots
{
    /// <summary>
    /// InterfaceGenerator 增量缓存哨兵测试：无关变更（新增无标记语法树）不应触发任何重新生成。
    /// 若管道模型丧失值相等性（record 被改回可变 class / 集合未用 ImmutableEquatableArray），此测试变红。
    /// </summary>
    public class InterfaceGeneratorCachingTests : GeneratorTestBase
    {
        [Fact]
        public void UnrelatedChange_AllOutputsCached()
        {
            var source = """
                using AutoCode.Model.InterfaceAttribute;
                namespace TestApp
                {
                    [AutoInterface]
                    public class UserService
                    {
                        public int GetId() => 1;
                    }
                }
                """;

            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "CachingTestAssembly",
                new[] { tree },
                DefaultReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // trackIncrementalGeneratorSteps: 启用步骤跟踪，否则 TrackedOutputSteps 恒为空
            var driver = CSharpGeneratorDriver.Create(
                new[] { new InterfaceGenerator().AsSourceGenerator() },
                optionsProvider: new EnableV2OptionsProvider(),
                driverOptions: new GeneratorDriverOptions(
                    IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

            // 无关变更：新增一个不含任何 AutoCode 标记的语法树
            var unrelatedTree = CSharpSyntaxTree.ParseText("namespace Other { public class Unrelated { } }");
            var compilation2 = compilation.AddSyntaxTrees(unrelatedTree);
            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation2);

            var result = driver.GetRunResult().Results.Single();
            Assert.NotEmpty(result.TrackedOutputSteps);

            // TrackedOutputSteps: 步骤名 → 该步骤的多次运行记录（每次运行各自有 Outputs）
            foreach (var (stepName, steps) in result.TrackedOutputSteps)
            {
                foreach (var step in steps)
                {
                    Assert.All(step.Outputs, output =>
                        Assert.True(
                            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                            $"步骤 '{stepName}' 未命中增量缓存（实际: {output.Reason}）——检查管道模型是否丧失值相等性"));
                }
            }
        }
    }
}
