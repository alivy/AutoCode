---
kind: dependency_management
name: AutoCode 依赖管理 — .NET 项目引用与 NuGet 包策略
category: dependency_management
scope:
    - '**'
source_files:
    - src/AutoCode.sln
    - src/AutoCode.Extensions.SourceGenerator/AutoCode.SourceGenerator.Extensions.csproj
    - src/APP.WebAPI/APP.WebAPI.csproj
    - src/AutoCode.Analyzers/AutoCode.Analyzers.csproj
    - scripts/install-autocode.ps1
---

本仓库是一个基于 Roslyn 的 C# 源码生成框架，其依赖管理采用 .NET SDK 风格的 csproj 文件 + NuGet 包组合方式，核心特点如下：

**1. 系统/工具**
- 使用 .NET SDK（Microsoft.NET.Sdk）进行项目构建与依赖解析。
- 通过 `<PackageReference>` 引用 NuGet 包，版本以硬编码形式声明在 csproj 中。
- 通过 `<ProjectReference ... OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` 将同解决方案内的 SourceGenerator / Analyzer 项目以“分析器”形式注入到消费项目中，实现编译时代码生成。
- 发布产物通过自定义 `AutoCode.SourceGenerator.Extensions.csproj` 中的 MSBuild Target 打包为单个 NuGet 包 `AM.AutoCode`，内部包含多个 analyzers 和 lib 组件。

**2. 关键文件与位置**
- `src/AutoCode.sln`：解决方案文件，集中列出所有子项目（约 40+ 个），是依赖关系的顶层清单。
- 各 `*.csproj` 文件：如 `src/APP.WebAPI/APP.WebAPI.csproj`、`src/AutoCode.Analyzers/AutoCode.Analyzers.csproj`，声明 `<PackageReference>` 与 `<ProjectReference>`。
- `src/AutoCode.Extensions.SourceGenerator/AutoCode.SourceGenerator.Extensions.csproj`：NuGet 包定义与打包脚本，控制 `AM.AutoCode` 包的输出结构（analyzers/dotnet/cs、lib/netstandard2.1、tools）。
- `scripts/install-autocode.ps1`：一键安装脚本，自动执行 `dotnet add package AM.AutoCode --prerelease`，并初始化 `autocode.json` 配置。
- `templates/autocode-webapi/`（若存在）：WebAPI 模板项目，供用户快速集成。

**3. 架构与约定**
- **内部分发模式**：开发阶段通过 Solution 内的 `<ProjectReference>` 直接引用各生成器项目，避免 NuGet 拉取；发布阶段由 `Extensions.csproj` 统一打包成 `AM.AutoCode` 单包。
- **SourceGenerator 注入约定**：所有作为分析器的项目均使用 `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` 引用，确保仅在编译期生效，不进入运行时依赖。
- **模型与运行时库分离**：`AutoCode.Model` 作为纯属性/特性定义库被所有生成器引用，而生成器本身以 analyzers 形式分发，遵循“零运行时反射、NativeAOT 兼容”的设计目标。
- **包版本管理**：当前仓库未使用 `global.json`、`Directory.Packages.props` 或 `packages.lock.json`，版本号分散在各 csproj 中，属于“按项目独立声明”的模式。

**4. 约定与约束**
- 所有 NuGet 包版本以固定数字声明（如 `Microsoft.CodeAnalysis.CSharp.Workspaces Version="4.11.0"`、`Swashbuckle.AspNetCore Version="6.6.2"`），未见通配符或范围版本。
- 分析器类包（如 `Microsoft.CodeAnalysis.Analyzers`）使用 `PrivateAssets="all"` 限制传播，防止污染下游依赖。
- 解决方案目录 `.nuget/` 用于存放打包产物，由 `PreBuild` Target 动态生成，非版本化提交。
- 安装脚本提供回退逻辑：当 NuGet 包不可用时，提示开发者改用项目引用方式集成。
- 未发现私有 NuGet 源配置（无 `NuGet.Config`）、无 `GOPRIVATE`（非 Go 项目）、无 vendoring 策略。