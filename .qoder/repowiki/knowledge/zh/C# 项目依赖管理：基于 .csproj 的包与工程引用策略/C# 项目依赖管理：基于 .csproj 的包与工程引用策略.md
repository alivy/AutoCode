---
kind: dependency_management
name: C# 项目依赖管理：基于 .csproj 的包与工程引用策略
category: dependency_management
scope:
    - '**'
source_files:
    - src/AutoCode.sln
    - src/AutoCode.Extensions.SourceGenerator/AutoCode.SourceGenerator.Extensions.csproj
    - src/AutoCodeGenerator/AutoCode.SourceGenerator.csproj
    - src/AutoCode.Model/AutoCode.Model.csproj
    - src/APP.WebAPI/APP.WebAPI.csproj
    - src/AutoCode.Analyzers/AutoCode.Analyzers.csproj
    - src/AutoCode.Tests/AutoCode.Tests.csproj
---

本仓库采用标准的 .NET SDK 风格（SDK-style csproj）进行依赖管理，未使用 packages.config 或 NuGet.config 等旧式配置。依赖分为两类：通过 `<PackageReference>` 引入的外部 NuGet 包，以及通过 `<ProjectReference>` 引入的内部工程依赖。

**外部包管理**
- 所有第三方库均通过 `<PackageReference>` 在各自 `.csproj` 中声明版本，如 `Microsoft.CodeAnalysis.CSharp`、`Swashbuckle.AspNetCore`、`xunit` 等。
- 分析器/SourceGenerator 相关包统一使用 `PrivateAssets="all"` 标记，避免传递到运行时依赖。
- 未发现全局 `Directory.Packages.props` 或 `global.json` 进行集中版本管控，各包版本分散在各项目中定义。
- 未发现 `nuget.config`、私有源配置或 `packages.lock.json` 锁文件，依赖解析完全依赖默认 nuget.org。

**内部工程依赖**
- 解决方案由 `src/AutoCode.sln` 统一管理，包含约 20 个子项目，按功能划分为 SourceGenerator、Analyzers、Model、WebAPI、Tests 等模块。
- 生成器类项目（如 AutoCodeGenerator、AutoCode.Analyzers、AutoCode.DependencyInjection 等）通过 `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` 引用，使其作为 Roslyn 分析器/SourceGenerator 生效但不参与运行时编译。
- 核心模型库 `AutoCode.Model`（netstandard2.0）被多个生成器和应用层项目引用，作为公共约定基础。

**NuGet 打包策略**
- `AutoCode.Extensions.SourceGenerator` 是唯一的 NuGet 打包入口，通过自定义 MSBuild Target 在 Publish 配置下自动发布所有子生成器到 `NugetPackage/analyzers/dotnet/cs`，并打包为 `AM.AutoCode` 包。
- 打包前通过 `dotnet publish` 命令将各子项目输出到指定目录，再执行 `dotnet pack` 生成 nupkg 到 `.nuget/` 目录。
- 包元数据（Version、Company、Authors、RepositoryUrl 等）集中在该项目的 PropertyGroup 中定义。

**目标框架与兼容性**
- 生成器与分析器统一 targeting `netstandard2.0`，确保最大兼容性。
- 应用层项目（APP.WebAPI、DotTemplate.APP、AutoCode.Tests）使用 `net8.0`。
- 语言版本统一为 `LangVersion=12.0`，Nullable 启用。

**约束与约定**
- 所有 SourceGenerator/Analyzer 项目必须设置 `EnforceExtendedAnalyzerRules=true`。
- 测试项目通过 `IsTestProject=true` 和 `coverlet.collector` 集成覆盖率收集。
- 未使用 vendoring 或 Git Submodule 管理依赖，所有依赖均通过 NuGet 动态解析。