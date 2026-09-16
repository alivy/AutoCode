# AGENTS.md — AutoCode 仓库 AI 代理指南

## 项目概述

AutoCode 是基于 Roslyn IIncrementalGenerator 的 C# 编译时代码生成框架。用户用 Attribute 标记类，编译时自动生成接口/DTO/映射/验证/Controller/DI 注册/测试桩/日志装饰器/CRUD 全链路/AOP 拦截管线。零运行时反射，NativeAOT 兼容。

## 环境要求

- .NET SDK 8.0+
- PowerShell（Windows）：不支持 `&&`，用 `;` 分隔命令
- 生成器项目目标框架：netstandard2.0（Roslyn 宿主约束，不可升级）
- 测试/示例项目：net8.0

## 构建与测试

```powershell
cd src
dotnet build AutoCode.sln        # 构建全部 12 个项目
dotnet test AutoCode.sln         # 全部测试（V1: AutoCode.Tests + V2: AutoCode.Tests.V2）
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj   # 仅 V2
```

NuGet 打包：`dotnet build src/AutoCode.Extensions.SourceGenerator/AutoCode.SourceGenerator.Extensions.csproj -c Publish`

## 架构分层（依赖单向）

```
消费方（APP.WebAPI 等）
  → AutoCode.Model（契约层：全部 Attribute + IInterceptHandler，零依赖，netstandard2.0）
  → AutoCode.Generators（生成层：V1/ + V2/ + Helpers/，Analyzer 方式被引用，netstandard2.0）
       → AutoCode.Engine（CodeBuilder/Template/Config 基建，netstandard2.0）
AutoCode.Analyzers（诊断 AC001~AC9100 + CodeFix + Ctrl+. 重构）
AutoCode.Cli（dotnet 工具，net8.0）
```

关键事实：**V2 的 11 个生成器是独立的 IIncrementalGenerator**（Engine 的 Pipeline/Plugin 体系未被使用，ADR 0001 已决策 v3.0 退役，见 docs/adr/）。扩展机制的真实形态是 Recipe 配方（Analyzers 的 ICodeGenRecipe + autocode.json customGenerators）。

## 修改生成器时的强制要求

1. **快照测试先行**：AutoCode.Tests.V2/Snapshots/ 已有快照基建（GeneratorTestBase + Verify.Xunit）。修改任何生成器输出前，确保有快照覆盖；输出变化必须体现在 verified.txt 的 diff 中。
2. **二次编译断言**：新测试必须调用 `AssertCompilesCleanly(run)`——生成代码随输入源码一起二次编译，零 CS 错误才通过。
3. **快照工作流**：首次/变更后运行生成 `*.received.txt`，人工审查确认后改名 `*.verified.txt` 入库；CI 上 `DiffEngine_Disabled=true`。
4. **Verify 断言顺序**：先 `await Verifier.Verify(...)` 后 `AssertCompilesCleanly(...)`（编译失败时仍能拿到 received 排查）。
5. 测试引用程序集用 TRUSTED_PLATFORM_ASSEMBLIES（勿目录扫描运行时目录，会混入原生 dll 报 CS0009）。

## 代码约定

- 特性标记集中 AutoCode.Model，命名 `AutoXxxAttribute`；**注意命名空间不统一**：`AutoInterfaceAttribute`/`AutoIgnoreAttribute` 在 `AutoCode.Model.InterfaceAttribute`，其余多在 `AutoCode.Model`。
- 诊断 ID 格式 `AC` + 数字；生成器通过 `context.AddSource` 输出，**禁止写磁盘**。
- 生成器输出必须含 `#nullable enable` 与 auto-generated 头。
- 类型显示用 nullable 感知的 SymbolDisplayFormat（勿用裸 FullyQualifiedFormat——会丢 `?` 注解与泛型约束，已有缺陷记录待修）。
- V1/V2 双轨：V2 生成器由 V2Gate 门控（MSBuild `AutoCode_EnableV2`），测试基建的 EnableV2OptionsProvider 已处理；V1 生成器（如 InterceptGenerator）测试时传 `enableV2: false`。

## 测试基建要点（Tests.V2）

- `RunGenerator(generator, source, enableV2, extraReferences)` 驱动单生成器
- Intercept 类生成代码依赖 Microsoft.Extensions.*，用 `extraReferences: ExtensionsReferences()`
- 测试源 `using AutoCode.Model;`（真实程序集已引用），Interface 相关特性用 `using AutoCode.Model.InterfaceAttribute;`

## 文档同步义务

修改生成行为/配置/命令时同步：README.md（特性表 + 版本历史）、CHANGELOG.md、docs/ 对应文档；配置键变更需同步 autocode.schema.json 与 llms.txt。

## 当前技术债（已记录，勿重复报告）

- V2 生成器增量模型为可变 class（无值相等）→ 增量缓存失效，阶段 2 计划 record 化
- V2 InterfaceGenerator 丢 nullable 注解与泛型约束（待修）
- Engine 的 Pipeline/Plugin/Convention/ConfigRecommender 为死代码（ADR 0001，v3.0 删除）
- CLI generate/doctor 为浅实现（文本扫描/硬编码通过）
