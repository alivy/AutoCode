using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoCode.Tests.V2.Infrastructure
{
    /// <summary>
    /// 生成器测试基座：统一驱动 IIncrementalGenerator，提供三种断言能力——
    /// 1. 快照文本（ToSnapshotText）：配合 Verify 锁定生成物，任何生成行为变化都会打破快照；
    /// 2. 二次编译断言（AssertCompilesCleanly）：确保"输入源码 + 生成代码"整体编译零错误，
    ///    防止 CS8669/CS8618 类生成质量事故流入用户项目；
    /// 3. 诊断收集（GeneratorRun.Diagnostics）：断言 AC 诊断（AC9001 等）。
    /// </summary>
    public abstract class GeneratorTestBase
    {
        /// <summary>
        /// 测试用配置提供器：V2 生成器默认受 V2Gate 保护（避免与 V1 重复生成），测试需显式启用。
        /// </summary>
        protected sealed class EnableV2OptionsProvider : AnalyzerConfigOptionsProvider
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
                    // 必须返回 null 而非 ""：生成器侧惯用 `TryGetValue(...) ?? "默认值"`，
                    // 空字符串会吞掉默认值（曾导致接口名丢前缀）。与 Roslyn 真实行为对齐。
                    value = null!;
                    return false;
                }
            }

            private static readonly Options s_options = new();

            public override AnalyzerConfigOptions GlobalOptions => s_options;
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_options;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_options;
        }

        /// <summary>单个生成文件（HintName + 内容）。</summary>
        protected sealed record GeneratedSource(string HintName, string Content);

        /// <summary>一次生成器运行的完整结果。</summary>
        protected sealed record GeneratorRun(
            Compilation OutputCompilation,
            ImmutableArray<Diagnostic> Diagnostics,
            IReadOnlyList<GeneratedSource> GeneratedFiles);

        /// <summary>
        /// 运行单个生成器并返回完整结果。
        /// </summary>
        /// <param name="generator">被测生成器实例</param>
        /// <param name="source">测试输入源码</param>
        /// <param name="enableV2">是否启用 AutoCode_EnableV2（V1 生成器如 InterceptGenerator 传 false）</param>
        /// <param name="extraReferences">额外元数据引用（如 Microsoft.Extensions.*）</param>
        protected static GeneratorRun RunGenerator(
            IIncrementalGenerator generator,
            string source,
            bool enableV2 = true,
            IEnumerable<MetadataReference>? extraReferences = null)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "SnapshotTestAssembly",
                new[] { syntaxTree },
                DefaultReferences().Concat(extraReferences ?? Enumerable.Empty<MetadataReference>()),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var driver = CSharpGeneratorDriver.Create(
                new[] { generator.AsSourceGenerator() },
                optionsProvider: enableV2 ? new EnableV2OptionsProvider() : null);

            driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
                compilation, out var outputCompilation, out _);

            var runResult = driver.GetRunResult();
            var files = runResult.Results
                .Where(r => r.Exception == null)
                .SelectMany(r => r.GeneratedSources)
                .Select(s => new GeneratedSource(s.HintName, s.SourceText.ToString()))
                .ToList();

            return new GeneratorRun(outputCompilation, runResult.Diagnostics, files);
        }

        /// <summary>
        /// 将生成文件拼成确定性快照文本：按 HintName 字典序排列，换行统一为 \n，
        /// 保证 Windows 本地与 Linux CI 产出字节一致（生成器内部 AppendLine 跟随平台 NewLine）。
        /// </summary>
        protected static string ToSnapshotText(GeneratorRun run)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var file in run.GeneratedFiles.OrderBy(f => f.HintName, StringComparer.Ordinal))
            {
                sb.Append("===== ").Append(file.HintName).Append(" =====\n");
                sb.Append(file.Content.Replace("\r\n", "\n").TrimEnd()).Append('\n');
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n') + "\n";
        }

        /// <summary>
        /// 断言"输入源码 + 生成代码"整体二次编译零错误。
        /// 这是防止生成代码携带 CS 错误流入用户项目的核心防线。
        /// </summary>
        protected static void AssertCompilesCleanly(GeneratorRun run)
        {
            var errors = run.OutputCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();

            Assert.True(errors.Count == 0,
                "生成代码存在编译错误:\n" + string.Join("\n", errors));
        }

        /// <summary>
        /// 默认引用：TPA（可信平台程序集，仅托管 dll，排除运行时目录中的 coreclr 等原生 dll）
        /// + AutoCode.Model。
        /// </summary>
        protected static IEnumerable<MetadataReference> DefaultReferences()
        {
            var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    yield return MetadataReference.CreateFromFile(path);
            }

            yield return MetadataReference.CreateFromFile(
                typeof(AutoCode.Model.AutoInterceptAttribute).Assembly.Location);
        }

        /// <summary>
        /// Intercept 生成代码依赖的 Microsoft.Extensions.* 引用
        /// （IMemoryCache / ILogger / IServiceCollection / Meter）。
        /// </summary>
        protected static IEnumerable<MetadataReference> ExtensionsReferences()
        {
            yield return MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.Caching.Memory.IMemoryCache).Assembly.Location);
            yield return MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location);
            yield return MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location);
            yield return MetadataReference.CreateFromFile(
                typeof(Microsoft.Extensions.Primitives.IChangeToken).Assembly.Location);
            yield return MetadataReference.CreateFromFile(
                typeof(System.Diagnostics.Metrics.Meter).Assembly.Location);
        }

        /// <summary>
        /// ASP.NET Core 共享框架引用（Controller/Crud/Cascade 生成的 Controller 代码需要）。
        /// 扫描 Microsoft.AspNetCore.App 共享目录（全为托管 dll，无原生程序集）。
        /// </summary>
        protected static IEnumerable<MetadataReference> AspNetCoreReferences()
        {
            var sharedDir = Path.GetDirectoryName(
                typeof(Microsoft.AspNetCore.Mvc.ControllerBase).Assembly.Location)!;
            foreach (var dll in Directory.GetFiles(sharedDir, "Microsoft.AspNetCore.*.dll"))
                yield return MetadataReference.CreateFromFile(dll);
        }

        /// <summary>xunit 引用（TestGenerator 生成的测试桩需要 Fact/Assert）。</summary>
        protected static IEnumerable<MetadataReference> XunitReferences()
        {
            yield return MetadataReference.CreateFromFile(typeof(Xunit.FactAttribute).Assembly.Location);
            yield return MetadataReference.CreateFromFile(typeof(Xunit.Assert).Assembly.Location);
            // 生成的测试桩含 using Moq（即便未用，缺引用会 CS0246）
            yield return MetadataReference.CreateFromFile(typeof(Moq.Mock).Assembly.Location);
        }

        /// <summary>EF Core 引用（Crud/Cascade 生成的仓储代码需要 DbContext/DbSet）。</summary>
        protected static IEnumerable<MetadataReference> EfCoreReferences()
        {
            yield return MetadataReference.CreateFromFile(
                typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location);
        }
    }
}
