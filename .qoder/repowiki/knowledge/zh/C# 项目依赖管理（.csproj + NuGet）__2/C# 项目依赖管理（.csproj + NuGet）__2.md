---
kind: dependency_management
name: C# 项目依赖管理（.csproj + NuGet）
category: dependency_management
scope:
    - '**'
source_files:
    - src/AutoCode.sln
    - src/AutoCode.Extensions.SourceGenerator/AutoCode.SourceGenerator.Extensions.csproj
    - src/AutoCode.SourceGenerator/AutoCode.SourceGenerator.csproj
    - src/APP.WebAPI/APP.WebAPI.csproj
    - src/AutoCode.Analyzers/AutoCode.Analyzers.csproj
    - src/AutoCode.Cli/AutoCode.Cli.csproj
    - src/AutoCode.Model/AutoCode.Model.csproj
---

该仓库是一个 C# 代码生成框架，采用 .NET SDK 风格的 .csproj 文件进行依赖声明，通过 NuGet 管理第三方包，并通过 ProjectReference 引用内部模块。具体模式如下：

1. **依赖声明方式**
   - 第三方库通过 `<PackageReference>` 在 csproj 中声明版本，例如 `Microsoft.CodeAnalysis.CSharp`、`Swashbuckle.AspNetCore`、`System.CommandLine` 等。
   - 内部模块之间通过 `<ProjectReference>` 引用，并配合 `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` 将生成器作为 Roslyn Analyzer 注入到消费项目中。

2. **目标框架与语言版本**
   - 核心生成器与模型库使用 `netstandard2.0` / `netstandard2.1`，保证最大兼容性。
   - CLI 与应用示例使用 `net8.0`，启用 `ImplicitUsings` 和 `Nullable` 现代特性。

3. **NuGet 打包策略**
   - `AutoCode.Extensions.SourceGenerator.csproj` 是统一的 NuGet 包入口，包含 `PackageId=AM.AutoCode`、`Version=1.2.0`、`DevelopmentDependency=true` 等元数据。
   - 通过 MSBuild Target 在 Publish 配置下自动发布各子生成器到 `NugetPackage/analyzers/dotnet/cs` 目录，再执行 `dotnet pack` 输出到 `.nuget/` 目录。
   - 同时打包 `lib/netstandard2.1` 下的运行时模型库。

4. **工具化分发**
   - `AutoCode.Cli` 通过 `<PackAsTool>true</PackAsTool>` 和 `<ToolCommandName>autocode</ToolCommandName>` 打包为 .NET Global Tool，命令名为 `autocode`。

5. **无集中式锁定文件**
   - 仓库中未发现 `Directory.Packages.props`、`global.json` 或 `nuget.config` 等集中化版本管理文件，版本号直接写在各项目 csproj 的 PackageReference 中。

6. **关键约束**
   - 所有 SourceGenerator/Analyzer 项目均设置 `EnforceExtendedAnalyzerRules=true` 以遵循 Roslyn 分析器规范。
   - 生成器项目通过 `<ProjectCapability Include="SourceGeneration" />` 标记自身为源码生成器。
   - 应用项目通过 `EmitCompilerGeneratedFiles=true` 暴露生成的代码以便调试。