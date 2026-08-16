# 变更日志

本文档记录 AutoCode 的所有重要变更。格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [2.3.1] - 2026-08-16

### 修复

- **生成器依赖传递（CS8785）**：修复 `AutoCode.Generators` / `AutoCode.Analyzers` 作为 Analyzer 被 ProjectReference 引用时，其依赖程序集（`AutoCode.Model`、`AutoCode.Engine`、`System.Text.Json` 等）未传递给编译器，导致 6 个生成器（DI/Dto/Interface/Validation/Controller/CustomRecipe）运行时抛 `FileNotFoundException` **静默失效**的问题。通过 `GetDependencyTargetPaths` MSBuild 目标将依赖 DLL 随 Analyzer 引用一并传递。
- **V1/V2 重复生成（CS0101/CS0111）**：依赖修复后暴露出 V1 与 V2 生成器监听相同特性（`[AutoInterface]`、`[AutoDTO]` 等）导致同一类型被生成两次的架构缺陷。新增 `AutoCode_EnableV2` MSBuild 开关（默认 `false` 仅运行 V1），接入全部 10 个 V2 生成器。
- **NuGet 打包脚本失效**：`AutoCode.SourceGenerator.Extensions` 的 PreBuild 目标仍在 publish 已被 2.2.0 合并删除的 12 个旧项目，改为 publish 合并后的 `AutoCode.Generators`；包版本 1.2.0 → 2.3.0。
- **测试断言过时**：`InterceptGeneratorTests.DIRegistration_GeneratesExtensionMethod` 未跟上生成器重构（`Decorate` → `AddScoped` 工厂注册模式）。
- **程序集版本冲突（MSB3277）**：测试项目显式引用 `Microsoft.CodeAnalysis.CSharp.Workspaces` 4.11.0，解决 Testing SDK 1.1.2 传递旧版 1.0.1 的冲突。

### 修复（生成代码质量）

- **CS8669**：`InterfaceGenerator` 生成的接口包含 `string?` 等可空注解但未输出 `#nullable enable` 指令。
- **CS8618**：`DtoGenerator` 生成的非空引用类型属性未初始化，现自动追加 `= default!;`。
- **CS0472**：`ValidationGenerator` 对值类型（int 等）的 `[Required]` 生成 `== null` 检查（恒为 false），现自动跳过。
- **CS8603**：`InterceptGenerator` 使用 `FullyQualifiedFormat` 丢失返回类型的可空注解（`OrderInfo?` → `OrderInfo`），改用 nullable 感知的 `SymbolDisplayFormat`。
- **CS8116**：`InterceptGenerator` 缓存模式匹配使用可空注解类型（`is OrderInfo?`），改用基础类型。

### 变更

- **仓库清理**：删除 40→11 合并后遗留的 30 个孤立项目文件夹（旧 V1 拆分项目、旧 V2 插件项目、旧示例 `APP`/`APP.Map`/`DotTemplate.APP`/`Samples/V2Demo` 等）。
- **测试迁移**：`AutoCode.Tests` / `AutoCode.Benchmarks` / `AutoCode.Tests.V2` 引用从旧拆分项目迁移到合并后的 `AutoCode.Generators`；`AutoCode.Tests.V2` 加入解决方案（11 → 12 个项目，测试总数 58 个）。

### 文档

- README：修正 Plugin.Sdk 矛盾描述、CLI 命令补全（7 个命令）、NuGet 版本号更新、项目结构同步、新增 V1/V2 切换说明。
- 新增 `docs/` 文档目录：配置参考、CLI 参考、故障排除、V1/V2 迁移指南。

## [2.3.0]

### 新增

- 自定义代码生成框架：`autocode.json` 的 `customGenerators` 配置 + Liquid 模板定义生成规则
- `CustomRecipeGenerator`：统一 Source Generator 处理所有自定义配方
- `[CustomGenerate]`：通用标记属性，配合配方名精准触发自定义生成
- 轻量级模板引擎：支持 `{{ variable }}`、`{% for %}`、`{% if %}` 等 Liquid 语法子集
- Ctrl+. 动态识别：RefactoringProvider 自动加载自定义配方，与内置生成器统一展示
- 示例模板：AuditService、Repository

## [2.2.0]

### 变更

- **架构精简**：40 → 11 个程序集合并（11 个 V1 SG + 11 个 V2 Plugins → `AutoCode.Generators` 统一项目）
- 统一 Ctrl+. 右键重构覆盖全部 11 个生成器
- 类特征智能推荐（实体→AutoEntity/DTO/Crud，Service→Interface/Controller/Log/Test，Request→Validator）
- 一键 Handler 生成
- 项目职责明确分层

## [2.1.0]

### 新增

- **编译时 AOP**：`[AutoIntercept]` 拦截器（替代 Castle DynamicProxy 等动态代理）
- 拦截管线：Log/Cache/Retry/CircuitBreaker/Metrics/Throttle/Validate/Tracing/Transaction 自由组合
- 强类型 Args 自动生成：`{MethodName}Args` record，Handler 直接引用
- 两层 Handler 设计：`IInterceptHandler`（通用横切）+ `IMethodHandler<TArgs,TResult>`（强类型）
- 异步 Handler：`IAsyncMethodHandler<TArgs,TResult>`
- 方法级精准拦截：`[AutoIntercept]` 可打在方法上
- AC9100 开发者感知提示
- 性能基准测试（vs Castle DynamicProxy）
- 智能 CodeFix + 配置推荐引擎
- 示例画廊（samples/ 4 个示例）
- 一键安装脚本 + dotnet new 模板 + VS Code 扩展

## [2.0.0]

### 新增

- 插件化引擎（`AutoCode.Engine`）+ Pipeline 管线
- Fluent CodeBuilder（CodeWriter/ClassBuilder/MethodBuilder/PropertyBuilder）
- 约定引擎（ConventionEngine）
- JSON 配置（autocode.json）
- 插件 SDK（已归档）
- CascadePlugin 级联生成
- 新特性 `[MapFrom]` / `[AutoEntity]`

## [1.3.0]

- +3 生成器（AutoTest/AutoLog/AutoCrud）
- +2 分析器（AC004/AC006）
- Async/XML Doc/Nullable/Swagger 智能化增强

## [1.2.0]

- +4 生成器（DTO/验证/Controller/DI）
- +3 分析器、+2 CodeFix
- CLI 工具、CI/CD、MSBuild 配置、23 个测试

## [1.1.x]

- IIncrementalGenerator 迁移、增量缓存、泛型/属性支持

## [1.0.x]

- ISourceGenerator 初始版本
