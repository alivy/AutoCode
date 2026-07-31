---
kind: dependency_management
name: C# 项目依赖管理（NuGet + MSBuild + Source Generator）
category: dependency_management
scope:
    - '**'
source_files:
    - src/AutoCode.Engine/AutoCode.Engine.csproj
    - src/AutoCode.Extensions.SourceGenerator/AutoCode.SourceGenerator.Extensions.csproj
    - src/AutoCode.Analyzers/AutoCode.Analyzers.csproj
    - src/autocode.json
    - .github/workflows/ci.yml
---

本仓库采用 .NET 生态标准的 NuGet 包管理机制，通过每个项目的 .csproj 文件中的 <PackageReference> 声明第三方依赖，未使用全局的 global.json、packages.config 或私有 NuGet 源配置文件。依赖管理呈现以下特点：

1. **按项目粒度声明依赖**：每个 .csproj 独立声明所需 NuGet 包，如 AutoCode.Engine 引用 Microsoft.CodeAnalysis.CSharp 4.11.0，AutoCode.Analyzers 引用 Microsoft.CodeAnalysis.Analyzers 3.11.0 等。

2. **Source Generator 专用依赖模式**：所有 Roslyn Source Generator 相关项目均将 Microsoft.CodeAnalysis.* 包标记为 PrivateAssets="all"，确保这些分析器/生成器仅在编译期生效，不传递到运行时依赖。

3. **自定义 NuGet 打包流程**：AutoCode.SourceGenerator.Extensions.csproj 定义了完整的 NuGet 包构建流程，包含 PreBuild 目标在 Publish 配置下自动发布各子项目到 NugetPackage 目录，PostBuild 目标执行 dotnet pack 生成 .nupkg 文件输出到 $(SolutionDir).nuget\ 目录。

4. **无版本集中管理**：未发现 global.json 或 Directory.Packages.props 等集中式版本管理文件，版本号直接硬编码在各个 .csproj 文件中。

5. **无 vendoring 策略**：未使用 dotnet package restore --locked-mode 或 vendor 目录，依赖通过 NuGet 包管理器正常恢复。

6. **CI 集成**：GitHub Actions 工作流位于 .github/workflows/ci.yml，用于自动化构建和测试。

7. **配置文件驱动**：autocode.json 作为框架运行时的配置中心，定义代码生成的约定、插件开关和行为选项，但这属于应用配置而非依赖管理配置。