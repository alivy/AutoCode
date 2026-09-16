---
kind: error_handling
name: AutoCode 错误处理体系：诊断、管线 Hook 与编译时 AOP 异常拦截
category: error_handling
scope:
    - '**'
source_files:
    - src/AutoCode.Engine/Diagnostics/DiagnosticCollector.cs
    - src/AutoCode.Analyzers/Diagnostics/AutoCodeDiagnosticDescriptors.cs
    - src/AutoCode.Engine/Pipeline/GenerationPipeline.cs
    - src/AutoCode.Model/IInterceptHandler.cs
    - src/AutoCode.Generators/V1/InterceptGenerator.cs
    - src/AutoCode.Analyzers/CodeFixes/GenerateHandlerRefactoring.cs
    - src/AutoCode.Engine/CodeBuilder/MethodBuilder.cs
    - src/APP.WebAPI/Services/CustomInterceptHandlers.cs
    - src/APP.WebAPI/Services/OrderServiceV2.cs
---

## 1. 整体方法

AutoCode 的错误处理分为三个层次，分别覆盖**源码生成阶段**（Roslyn Analyzer/Generator）、**生成管线运行时**（插件执行）以及**用户业务代码的编译时代码注入**（AOP 拦截）。

- **源码级诊断**：通过 `Microsoft.CodeAnalysis.Diagnostic` 体系报告错误/警告/信息，统一使用 `ACxxxx` 编号。分析器位于 `AutoCode.Analyzers`，引擎侧诊断收集器位于 `AutoCode.Engine/Diagnostics/DiagnosticCollector.cs`。
- **管线错误隔离**：`GenerationPipeline.Execute` 对每个插件和 Hook 用 try/catch 包裹，单个失败不会中断整个生成流程；失败结果写入 `PluginResult.Fail` 并通过 `context.Diagnostics.ReportError("AC1001", ...)` 上报。
- **运行时 AOP 异常拦截**：框架为标注了 `[AutoIntercept]` 的方法在编译期生成 try/catch 包装，捕获异常后调用 `IInterceptHandler.OnException` / `IMethodHandler<TArgs,TResult>.OnException` / `IAsyncMethodHandler<TArgs,TResult>.OnExceptionAsync`，由用户实现决定是否降级（设置 `ctx.Handled = true`）或继续抛出。

## 2. 关键文件与包

| 文件 | 作用 |
|---|---|
| `src/AutoCode.Engine/Diagnostics/DiagnosticCollector.cs` | 定义 `IDiagnosticCollector`、`DiagnosticEntry`、`DiagnosticSeverityLevel`、集中式 `DiagnosticIds`（AC1xxx~AC8xxx），并提供 `ToRoslynDiagnostic()` 转换 |
| `src/AutoCode.Analyzers/Diagnostics/AutoCodeDiagnosticDescriptors.cs` | 定义 AC001~AC006 等 Roslyn 诊断描述符（Usage/Design 分类） |
| `src/AutoCode.Engine/Pipeline/GenerationPipeline.cs` | 插件执行编排：拓扑排序、Hook 生命周期、异常隔离、失败诊断上报 |
| `src/AutoCode.Model/IInterceptHandler.cs` | 定义通用拦截器 `IInterceptHandler`、强类型 `IMethodHandler<TArgs,TResult>`、异步 `IAsyncMethodHandler<TArgs,TResult>`、上下文 `InterceptContext`/`MethodContext` 及 `Handled`/`ShortCircuit`/`Result` 控制位 |
| `src/AutoCode.Generators/V1/InterceptGenerator.cs` | V1 生成器：为标注方法生成 try/catch 包装，调用各层 Handler 的 `OnException` |
| `src/AutoCode.Analyzers/CodeFixes/GenerateHandlerRefactoring.cs` | IDE 一键生成带默认 `OnBefore/OnAfter/OnException` 实现的 Handler 类 |
| `src/APP.WebAPI/Services/CustomInterceptHandlers.cs` | 示例：实现 `IInterceptHandler` 的 `OnException`，记录 metrics.LastError |
| `src/APP.WebAPI/Services/OrderServiceV2.cs` | 示例：强类型 `IMethodHandler` 的 `OnException`，演示重试/降级 |

## 3. 架构与设计决策

### 3.1 统一的诊断 ID 体系
`DiagnosticIds` 按子系统划分号段：
- AC1xxx：引擎/管线（如 `AC1001 PluginExecutionFailed`、`AC1002 ConfigParseError`）
- AC2xxx：Mapper 插件
- AC3xxx：WebApi 插件
- AC4xxx：DTO 插件
- AC5xxx：Validation 插件
- AC6xxx：DI 插件
- AC7xxx：CRUD 插件
- AC8xxx：Convention 推断
- AC0xx：Analyzer 静态检查（AC001~AC006）

每个 `DiagnosticEntry` 可附带 `HelpLink`，默认指向 `https://github.com/autocode/docs/{Id}`，将错误与文档绑定。

### 3.2 管线错误隔离模式
`GenerationPipeline.Execute` 中：
- 插件 `IsEnabled`、Hook 各阶段、插件 `Generate` 均被独立 try/catch 包裹；Hook 异常被吞掉以保护主管线。
- 插件失败时构造 `PluginResult.Fail(plugin.Name, ex.Message, elapsed)`，并调用所有 Hook 的 `OnPluginError(context, plugin, ex)`。
- 最终通过 `context.Diagnostics.ReportError("AC1001", $"插件 '{plugin.Name}' 执行失败: {ex.Message}", location)` 上报。
- `PipelineExecutionResult.Success` 由 `pluginResults.All(r => r.Success)` 计算，调用方可据此判断整体成败。

### 3.3 编译时代码注入的异常传播模型
`IInterceptHandler` / `IMethodHandler<TArgs,TResult>` / `IAsyncMethodHandler<TArgs,TResult>` 三套接口都提供 `OnException(...)` 回调，配合 `MethodContext`/`InterceptContext` 中的 `Handled` 标志：
- 若 Handler 设置 `ctx.Handled = true`，则异常不再向上抛出（可配合 `ctx.Result` 返回降级值）。
- 若不设置 `Handled`，异常继续向上传播。
- `ShortCircuit` + `Result` 用于 `OnBefore` 短路执行；`AttemptNumber` 支持重试场景。

生成的 try/catch 会依次调用所有注册的 Handler 的 `OnException`，顺序由 `Order` 属性控制（越小越先）。

### 3.4 代码生成器的 try/catch 能力
`MethodBuilder` 暴露 `Try()`、`Catch(exceptionType, varName)`、`Finally()` 等 DSL 方法，供模板/生成器构建包含异常处理的 C# 代码片段。

## 4. 约定与约束

- **诊断必须使用 `DiagnosticIds` 常量**：新增诊断应先在 `DiagnosticIds` 中分配 ACxxxx 编号，再在对应模块的 `ReportError/ReportWarning/ReportInfo/ReportSuggestion` 中引用，保证 ID 全局唯一且可检索。
- **Analyzer 诊断归类到 `AutoCode.Usage` 或 `AutoCode.Design` 两类**：新增静态检查需遵循此分类。
- **插件异常不得抛出到管线之外**：`GenerationPipeline` 强制吞掉 Hook 异常、将插件异常封装为 `PluginResult.Fail`，禁止直接 throw。
- **AOP 异常处理必须通过 Handler 的 `OnException`**：业务层不应自行 try/catch 被拦截的方法——框架已生成 try/catch 包装，用户只需实现 Handler。
- **Handler 可通过 `ctx.Handled = true` 显式声明“已处理”**：这是区分“降级”和“继续抛异常”的唯一约定标志。
- **示例服务中的业务异常**：`OrderService`、`PaymentService`、`UserService` 直接使用 `ArgumentException`、`InvalidOperationException`、`TimeoutException` 等标准 .NET 异常，体现“业务层抛标准异常，AOP 层统一捕获”的分层约定。

## 5. 适用性说明

本仓库是一个 .NET 源码级代码生成框架，其错误处理体系围绕 Roslyn 诊断、生成管线 Hook 与编译时代码注入的 AOP 异常拦截展开，而非传统 Web API 的中间件错误处理。因此该类别在本仓库中高度适用。