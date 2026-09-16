---
kind: configuration_system
name: AutoCode 三层配置系统（autocode.json + MSBuild + Attribute）
category: configuration_system
scope:
    - '**'
source_files:
    - autocode.json
    - src/AutoCode.Engine/Config/AutoCodeConfig.cs
    - src/AutoCode.Engine/Config/ConfigRecommender.cs
    - docs/configuration.md
    - src/AutoCode.Cli/Program.cs
    - src/AutoCode.Model/RecipeConfigLoader.cs
    - src/AutoCode.Model/CodeGenRecipe.cs
    - src/AutoCode.Analyzers/Recipes/CustomRecipeAdapter.cs
    - src/AutoCode.Generators/CustomRecipeGenerator.cs
---

## 1. 系统概览

AutoCode 采用**三层配置合并系统**，统一入口为项目根目录的 `autocode.json`。配置在编译期由 Roslyn Source Generator / Analyzer 读取，运行时无需任何加载逻辑。

优先级（从高到低）：
1. **MSBuild 属性**（`.csproj` `<PropertyGroup>` 中声明的 `AutoCode_*` 属性）— 最高优先级
2. **`autocode.json` 文件** — 中等优先级
3. **Attribute 命名参数**（代码中标记的特性参数）— 默认值层

该优先级顺序在 `AutoCode.Engine/Config/AutoCodeConfig.cs` 的注释与 `Merge` 实现中明确定义并强制。

## 2. 核心文件与职责

- `autocode.json`（仓库根）：用户级配置示例，包含 `conventions`、`mapper`、`dto`、`webapi`、`validation`、`dependencyInjection`、`cascade`、`logging`、`intercept`、`plugins`、`customGenerators` 等全部节点。
- `src/AutoCode.Engine/Config/AutoCodeConfig.cs`：配置系统的核心实现，提供 `IAutoCodeConfig` 接口与 `AutoCodeConfig` 实现，负责从 JSON 字符串解析、从 MSBuild `AnalyzerConfigOptions` 读取、以及多源合并。
- `src/AutoCode.Engine/Config/ConfigRecommender.cs`：智能推荐引擎，扫描 `.csproj`、`.sln`、源码内容，自动推断 EF Core / ASP.NET Core / 测试框架 / 缓存 / DI 使用情况，生成 `ConfigRecommendation` 列表及可应用的 JSON Patch。
- `docs/configuration.md`：官方配置参考文档，逐节说明每个键的类型、默认值与含义。
- `src/AutoCode.Cli/Program.cs`：CLI 支持 `init` 命令生成初始 `autocode.json`，并提供诊断检查是否找到配置文件。
- `src/AutoCode.Model/RecipeConfigLoader.cs` 与 `CodeGenRecipe.cs`：从 `autocode.json` 的 `customGenerators` 节解析自定义配方。
- `src/AutoCode.Analyzers/Recipes/CustomRecipeAdapter.cs`：将 JSON 中的配方转换为 `ICodeGenRecipe` 供分析器使用。

## 3. 架构与设计决策

### 扁平化键空间
`AutoCodeConfig` 通过内部 `Dictionary<string, string>` 将所有配置源归一化为扁平的 `dot.separated` 键（如 `mapper.methodName`），并通过 `GetSection(name)` 返回带前缀的子配置视图，从而让各插件以相同方式访问配置。

### 轻量 JSON 解析
由于目标框架为 netstandard2.0，`AutoCodeConfig.FromJson` 内置了极简 JSON 扁平化解析器（`ParseJsonFlat`），仅支持对象、数组、字符串、数字、布尔，不依赖 `System.Text.Json`，确保 Source Generator 零外部依赖。

### 三层合并策略
`AutoCodeConfig.Merge(params IAutoCodeConfig[])` 按传入顺序覆盖：后传入的配置覆盖前者。调用方约定以 `[MSBuild, JsonFile, Attribute]` 顺序传入，从而实现“Attribute 覆盖 JSON 覆盖 MSBuild”的语义。

### 配置来源映射
| 来源 | 键格式 | 读取位置 |
|---|---|---|
| MSBuild | `build_property.AutoCode_InterfacePrefix` → `interface.prefix` | `AnalyzerConfigOptions`，需 `<CompilerVisibleProperty Include="..."/>` 暴露 |
| JSON | `autocode.json` 顶层键 | 项目根目录，CLI 会向上遍历查找 |
| Attribute | 特性构造参数 | 编译时由生成器注入到 `AutoCodeConfig.Set(...)` |

### 插件开关机制
`plugins.*.enabled` 控制 V2 各生成插件是否参与编译；`cascade.*` 控制 `[AutoEntity]` 触发的全链路产物开关；`conventions.*` 控制命名模式自动发现。

### 自定义配方
`customGenerators[]` 允许用户在 JSON 中声明 Liquid 模板驱动的自定义生成规则，配合 `templates/*.liquid` 与 `[CustomGenerate("name")]` 触发，无需编写 C# 生成器。

## 4. 约定与约束

- **配置文件位置**：`autocode.json` 位于项目根或仓库根，CLI 和 Analyzer 会向上遍历目录树查找。
- **MSBuild 属性必须显式暴露**：通过 `<CompilerVisibleProperty Include="AutoCode_..."/>` 才能传递给 Source Generator（见 `docs/configuration.md`）。
- **键名转换规则**：MSBuild 的 PascalCase 属性名（如 `AutoCode_MapMethodName`）会被转换为 dot-separated 小写键（如 `mapper.methodName`），由 `ToConfigKey` 实现。
- **布尔值解析宽松**：`true`、`True`、`1`、`yes` 均视为真。
- **数组值解析**：JSON 数组被扁平化为逗号分隔字符串，读取时按 `,` 或 `;` 拆分。
- **推荐优先于手动配置**：`ConfigRecommender` 基于项目结构扫描给出高/中/低优先级的建议，可直接生成 JSON Patch 应用到 `autocode.json`。
- **V2 总开关**：`AutoCode_EnableV2`（MSBuild 属性）控制是否启用 V2 生成管线，默认关闭（见迁移文档）。
- **无运行时配置加载**：所有配置在编译期解析，运行时代码不包含配置读取逻辑，符合 Source Generator 的设计约束。

## 5. 关键路径清单

- `autocode.json`
- `src/AutoCode.Engine/Config/AutoCodeConfig.cs`
- `src/AutoCode.Engine/Config/ConfigRecommender.cs`
- `docs/configuration.md`
- `src/AutoCode.Cli/Program.cs`
- `src/AutoCode.Model/RecipeConfigLoader.cs`
- `src/AutoCode.Model/CodeGenRecipe.cs`
- `src/AutoCode.Analyzers/Recipes/CustomRecipeAdapter.cs`
- `src/AutoCode.Generators/CustomRecipeGenerator.cs`
- `templates/AuditService.liquid`, `templates/Repository.liquid`