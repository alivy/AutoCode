---
kind: error_handling
name: AutoCode 错误处理体系：诊断、拦截与异常传播
category: error_handling
scope:
    - '**'
source_files:
    - src/AutoCode.Engine/Diagnostics/DiagnosticCollector.cs
    - src/AutoCode.Analyzers/Diagnostics/AutoCodeDiagnosticDescriptors.cs
    - src/AutoCode.Model/IInterceptHandler.cs
    - src/APP.WebAPI/Services/CustomInterceptHandlers.cs
    - src/AutoCode.Intercept/InterceptGenerator.cs
    - src/AutoCode.Engine/CodeBuilder/MethodBuilder.cs
---

## 1. 系统概览

AutoCode 采用**分层错误处理架构**，将编译期诊断（Source Generator/Analyzer）与运行期 AOP 拦截解耦：
- **编译期**：通过 `DiagnosticCollector` + `DiagnosticIds` 统一收集错误/警告/建议，映射为 Roslyn Diagnostic 反馈给 IDE。
- **运行期**：通过 `IInterceptHandler` / `IMethodHandler<TArgs,TResult>` 拦截管线捕获异常，支持短路、降级、重试等策略。

## 2. 核心组件与文件

### 编译期诊断系统
- `src/AutoCode.Engine/Diagnostics/DiagnosticCollector.cs` — 统一诊断收集器，提供 `ReportError/Warning/Info/Suggestion` 四级别 API，线程安全实现，支持 `HasErrors` 快速判断。
- `src/AutoCode.Analyzers/Diagnostics/AutoCodeDiagnosticDescriptors.cs` — 定义 AC001~AC006 诊断描述符（MissingAutoInterface、InterfaceDivergence、UnusedAutoIgnore、LayerViolation、NamingConvention），分类为 Usage/Design。
- `src/AutoCode.Engine/Diagnostics/DiagnosticIds.cs` — 按插件域划分 ID 段（AC1xxx Engine、AC2xxx Mapper、AC3xxx WebApi、AC4xxx DTO、AC5xxx Validation、AC6xxx DI、AC7xxx CRUD、AC8xxx Convention）。

### 运行期拦截与异常处理
- `src/AutoCode.Model/IInterceptHandler.cs` — 定义两层拦截接口：
  - `IInterceptHandler` + `InterceptHandlerBase`：通用横切关注点（日志、指标、并发监控），通过 `InterceptContext` 传递上下文。
  - `IMethodHandler<TArgs,TResult>` + `MethodHandlerBase<TArgs,TResult>`：强类型方法拦截，参数/返回值类型化，通过 `MethodContext` 控制 `ShortCircuit`、`Handled`、`Result`。
  - 异步版本 `IAsyncMethodHandler<TArgs,TResult>` + `AsyncMethodHandlerBase<TArgs,TResult>`。
- `src/APP.WebAPI/Services/CustomInterceptHandlers.cs` — 示例实现：`ConcurrencyMonitorHandler`（并发计数）、`DataCollectorHandler`（指标采集），均在 `OnException` 中记录 `ErrorCount`、`LastError`、`LastErrorTime`。
- `src/APP.WebAPI/Services/OrderServiceV2.cs` — 展示业务层异常处理：在 `OnException` 中设置 `ctx.Result` 进行降级返回，使用 `BatchResult.Errors` 收集失败信息。

### 代码生成中的异常捕获
- `src/AutoCode.Intercept/InterceptGenerator.cs` — 生成带 `try/catch(Exception __ex)` 的包装代码，支持重试逻辑（`catch (Exception __ex) when (__attempt < MaxRetryCount)`），调用 `OnException(__args, __ex, __mctx)`。
- `src/AutoCode.Engine/CodeBuilder/MethodBuilder.cs` — 生成 `catch (exceptionType varName)` 分支。
- `src/AutoCode.Engine/Pipeline/GenerationPipeline.cs` — 插件执行时 `catch (Exception ex)` 包裹，防止单个插件失败导致整个生成中断。

## 3. 架构与约定

### 诊断 ID 分配规则
| 前缀 | 领域 | 示例 |
|------|------|------|
| AC0xxx | Analyzer 诊断 | AC001 MissingAutoInterface |
| AC1xxx | Engine/管线 | AC1001 PluginExecutionFailed |
| AC2xxx | Mapper 插件 | AC2001 MapperNoMatchingProperties |
| AC3xxx | WebApi 插件 | AC3001 WebApiNoServiceInterface |
| AC4xxx | DTO 插件 | AC4001 DtoSourceNotFound |
| AC5xxx | Validation 插件 | AC5001 ValidationNoRules |
| AC6xxx | DI 插件 | AC6001 DiNoLifetimeInterface |
| AC7xxx | CRUD 插件 | AC7001 CrudNoKeyProperty |
| AC8xxx | Convention 推断 | AC8001 ConventionServiceDetected |

### 拦截器异常处理流程
1. 目标方法抛出异常 → 生成代码捕获为 `__ex`
2. 按顺序调用各拦截器的 `OnException(args, __ex, ctx)`
3. 拦截器可设置 `ctx.Handled = true` 阻止异常继续传播
4. 可设置 `ctx.Result` 返回降级值（短路）
5. 未处理的异常继续向上抛出

### 重试机制
通过 `[AutoIntercept(InterceptType.Retry, MaxRetryCount = N)]` 配置，生成代码包含指数退避重试逻辑，每次重试递增 `AttemptNumber`。

## 4. 约束与最佳实践

- **编译期错误必须报告**：所有诊断通过 `IDiagnosticCollector.ReportError` 上报，禁止直接抛异常中断生成。
- **拦截器必须幂等**：`OnException` 中不应修改业务状态，仅用于日志、指标、告警。
- **异常分类**：业务校验异常用 `ArgumentException`，状态异常用 `InvalidOperationException`，外部依赖异常用 `TimeoutException`/自定义异常。
- **诊断消息格式**：标题: 详情（冒号分隔），便于自动提取标题。
- **无 panic/recover**：C# 生态下不使用 `throw new Exception()` 作为控制流，也不使用 `try/finally` 作为资源管理主模式。