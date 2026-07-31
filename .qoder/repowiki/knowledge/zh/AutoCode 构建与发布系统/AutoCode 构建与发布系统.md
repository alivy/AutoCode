---
kind: build_system
name: AutoCode 构建与发布系统
category: build_system
scope:
    - '**'
source_files:
    - .github/workflows/ci.yml
    - src/AutoCode.sln
    - scripts/install-autocode.ps1
---

### 构建系统与工具链

该项目使用 **.NET SDK + MSBuild** 作为核心构建系统，通过 `src/AutoCode.sln` 解决方案文件统一管理 40+ 个 C# 项目（包含引擎、插件、测试、示例等）。所有构建命令基于 `dotnet CLI`，无需额外构建脚本。

### CI/CD 流水线

GitHub Actions 配置在 `.github/workflows/ci.yml`，定义了三阶段流水线：
- **build-and-test**：在 `ubuntu-latest` 上执行 `dotnet restore` → `dotnet build --configuration Release` → `dotnet test --verbosity normal`
- **pack**：仅在 `v*` 标签触发，构建 `AutoCode.SourceGenerator.Extensions.csproj` 并输出到 `src/.nuget/*.nupkg`
- **publish**：下载打包产物并通过 `dotnet nuget push` 发布到 NuGet.org，API Key 通过 `secrets.NUGET_API_KEY` 注入

### 版本与发布策略

- 版本控制通过 Git 标签 `v*` 驱动，CI 自动检测标签触发打包和发布
- NuGet 包输出路径固定为 `src/.nuget/`，由 pack 步骤收集
- 发布目标为公共 NuGet 源 `https://api.nuget.org/v3/index.json`

### 项目模板与安装脚本

- `scripts/install-autocode.ps1` 提供一键安装能力：自动添加 `AM.AutoCode` NuGet 包、生成 `autocode.json` 配置文件、可选创建示例实体代码
- 支持回退机制：NuGet 包未发布时自动切换为项目引用方式
- 环境诊断功能检查 .NET SDK、配置文件、`.editorconfig` 等依赖

### 解决方案结构

`solution` 文件定义了完整的构建图：
- 核心引擎：`AutoCode.Engine`、`AutoCode.Model`、`AutoCode.SourceGenerator`
- 插件体系：`AutoCode.Plugins.*`（Mapper、Dto、Validation、WebApi、Crud、Intercept、Logging、Testing、Interface、Cascade）
- 分析器：`AutoCode.Analyzers`
- 测试套件：`AutoCode.Tests`、`AutoCode.Tests.V2`、`AutoCode.Benchmarks`
- 示例应用：`APP`、`APP.WebAPI`、`DotTemplate.APP`、`V2Demo`

### 构建配置约定

- 平台：支持 `Any CPU`、`x86`、`x64` 三种架构
- 配置：`Debug`、`Release`、`Publish` 三种构建配置
- 目标框架：主要使用 .NET 8.0.x（CI 中指定）
- 源码生成：通过 Roslyn IIncrementalGenerator 在编译时生成代码，输出到 `obj/Debug/*/generated/` 目录

### 约束与规范

- 构建必须在 .NET 8.0.x 环境下执行（CI 强制）
- NuGet 包命名遵循 `AM.AutoCode` 前缀约定
- 配置文件统一使用 `autocode.json` 格式
- 生成的代码遵循 AutoCode 约定的命名模式和目录结构