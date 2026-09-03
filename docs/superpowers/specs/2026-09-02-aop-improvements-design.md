# AOP 拦截器四阶段改进设计（AutoCode.Intercept v3）

- **日期**：2026-09-02
- **状态**：已获用户批准（方案 B：四阶段渐进交付）
- **范围**：`AutoCode.Generators/V1/InterceptGenerator.cs`、`AutoCode.Model`（AutoInterceptAttribute / IInterceptHandler / 新运行时抽象）、`APP.WebAPI` 示例、快照测试、README/CHANGELOG/samples 文档

## 1. 背景与问题清单

现状（v2.1）：`[AutoIntercept]` 声明 12 种拦截器，实际仅 8 种有生成逻辑；文档承诺的部分能力未生效。基于代码审计确认的问题：

| # | 问题 | 严重度 |
|---|---|---|
| 1 | Authorize/Transaction/Audit/Profiling 四种拦截器声明了但无生成代码，标记后静默无效 | 高 |
| 2 | `ctx.Handled = true` 降级不生效：生成的 catch 无条件 `throw;`，示例与文档承诺失效 | 高 |
| 3 | `IAsyncMethodHandler` 不被生成器识别，CodeFix 生成的异步 Handler 触发 AC9003 编译错误（工具自相矛盾） | 高 |
| 4 | `LogResult`、`ExcludeMethods` 属性被解析但从未用于生成 | 中 |
| 5 | 方法自身泛型参数 `<T>` 未捕获 → 装饰器丢泛型签名 | 高 |
| 6 | `ref/out/in/params` 参数 RefKind 未捕获 | 中 |
| 7 | 方法重载 → 同名 Args record 冲突（CS0101） | 中 |
| 8 | Retry 延迟不可取消（无 CancellationToken 透传） | 低 |
| 9 | Throttle 名实不符：`MaxRequestsPerSecond=100` 实际是 `SemaphoreSlim(100)` 并发许可 | 中 |
| 10 | Retry 对所有异常重试，无异常类型过滤 | 中 |
| 11 | CircuitBreaker 无半开状态，static 字段语义粗糙 | 中 |
| 12 | Cache 缓存 null 结果语义未定义；命中短路跳过 Log/Metrics 的 Before | 低 |
| 13 | 拦截管线顺序硬编码，不可配置 | 中 |
| 14 | 管道模型为可变 class → 增量缓存失效（已知技术债） | 中 |
| 15 | 快照测试仅 3 个场景，覆盖率不足 | 中 |
| 16 | 1165 行巨型类，字符串缩进技巧（`b += "    "`）脆弱 | 低 |
| 17 | 文档宣称 12 种拦截器与实现不符 | 中 |

## 2. 架构原则（贯穿四阶段）

1. **不迁移双轨**：InterceptGenerator 保持独立 `IIncrementalGenerator` 的现状（V1 目录、不受 V2Gate 门控、测试传 `enableV2: false`），不做 V2 迁移。
2. **契约层零依赖**：新运行时抽象（`IClaimsPrincipalProvider`、`IAuditStore`、`AuditEntry`）全部进 AutoCode.Model，仅用 netstandard2.0 BCL 类型。
3. **顺序表驱动发射**：阶段 4 将每种拦截器的代码生成逻辑抽为独立 emit 函数，按 PipelineOrder 顺序表发射。该重构支撑阶段 2 新拦截器插入，避免继续堆叠条件分支。
4. **快照先行**（AGENTS.md 强制）：每阶段先 `Verifier.Verify` 再 `AssertCompilesCleanly`；received→verified 人工审查入库；生成物必须含 `#nullable enable` 与 auto-generated 头。
5. **不允许静默失效**：凡声明必生效；无法生效的场景必须有诊断（Error/Warning/Info）显式提示。

## 3. 阶段 1：声明与实现对齐（修假功能）

### 3.1 Handled 降级生效
生成 catch 块在调用完全部 Handler 的 `OnException` 后：
- 有返回值方法：`if (__mctx.Handled) return (TResult)__mctx.Result!; throw;`（legacy 检查 `__ctx.Handled`）
- void / 无结果 Task 方法：`if (Handled) return; throw;`
- Retry 耗尽后的最终异常路径走同一逻辑
- 多 Handler 时任一设置 Handled 即生效（依次调用全部后统一检查）
- 边界约定：Handled=true 但 Result 未设置时，引用类型返回 null，值类型抛 InvalidCastException——文档明示"降级必须同时设置 Result"
- 重试场景中的降级：单次重试内 Handler 设置 Handled 则停止重试并降级返回（不再等待重试耗尽）

### 3.2 IAsyncMethodHandler 识别与 await 调用
- `ParseCustomHandler` 增加 `IAsyncMethodHandler` 检测 → `IsAsyncHandler`
- 生成三处 `await handler.OnBeforeAsync/OnAfterAsync/OnExceptionAsync(__args, ...)`
- 新诊断 **AC9004**（Error）：异步 Handler 挂载在同步方法上（无法 await）
- CodeFix 的"生成异步 Handler"动作自此真正可用

### 3.3 LogResult / ExcludeMethods 生效
- `LogResult=true`：After 日志追加 `结果: {Result}`（仅对有返回值方法）
- 类级 `ExcludeMethods`（逗号分隔）解析后与 `[SkipIntercept]` 同等透传语义

**交付物**：快照 ×4（降级三形态、异步 Handler、LogResult、ExcludeMethods）；`APP.WebAPI/Services/OrderServiceV2.cs` 降级示例真实生效。

## 4. 阶段 2：四种拦截器落地（12/12 可用）

### 4.1 Authorize
- AutoCode.Model 新增：`public interface IClaimsPrincipalProvider { System.Security.Claims.ClaimsPrincipal? CurrentPrincipal { get; } }`
- Attribute 新属性：`AuthorizeRoles`（string?，逗号分隔）、`AuthorizePolicy`（string?，**预留**：v3 设置时仅发 AC9010 Info 提示"Policy 检查暂未实现，请使用 AuthorizeRoles"——不静默忽略）
- 生成 Before：`CurrentPrincipal` 为 null 或未认证 → 抛 `UnauthorizedAccessException`；角色不符 → 抛 `UnauthorizedAccessException`（消息注明缺失角色）
- 装饰器构造注入 Provider；DI 用 `GetRequiredService` 强制注册；配套 **AC9006**（Info）：启用 Authorize 需注册 `IClaimsPrincipalProvider` 实现
- 用户自行提供适配实现（如基于 HttpContext）；框架不绑定 ASP.NET Core

### 4.2 Transaction
- 用 BCL `System.Transactions.TransactionScope`，零新增依赖
- 异步方法：`TransactionScopeAsyncFlowOption.Enabled`；成功路径 `__tx.Complete()`；异常靠 scope dispose 自动回滚
- Attribute 新属性：`TransactionTimeoutSeconds`（默认 60）、`TransactionIsolationLevel`（枚举，默认 ReadCommitted）
- 文档注明：Linux 环境分布式事务升级限制，适用于单连接（单 DbContext）场景

### 4.3 Audit
- AutoCode.Model 新增：`AuditEntry`（ClassName/MethodName/Parameters/Result/Elapsed/Succeeded/Error）+ `IAuditStore { void Write(AuditEntry); Task WriteAsync(AuditEntry); }`
- 成功路径（After）与异常路径（catch）都写审计：异常路径 `Succeeded=false`、`Error=ex.Message`
- `[Sensitive]` 参数在审计中脱敏为 `"[REDACTED]"`
- Attribute 新属性：`AuditIncludeParameters`（默认 true）
- 异步方法 `await WriteAsync`；同步方法调 `Write`

### 4.4 Profiling
- 零新注入：Before 取 `GC.GetTotalMemory(false)`，After 计算 GC 增量 + 耗时
- 输出走 ILogger（`Log` 或 `Profiling` 任一启用即注入 `ILogger<InterceptedXxx>`）
- Attribute 新属性：
  - `ProfilingThresholdMs`（默认 0：恒输出；>0 时仅超阈值输出）
  - `ProfilingIncludeProcess`（默认 false：仅 GC 增量+耗时；true：额外采集 `Process.GetCurrentProcess()` 的 WorkingSet64、线程数——用户决策：可控制加载，按需启停）
- 诊断 **AC9005**（Info）：`ProfilingIncludeProcess=true` 时的开销提示

**交付物**：快照 ×4；APP.WebAPI 演示（如 `PaymentService` 加 Authorize/Transaction、新增 Audit/Profiling 示例）；README 特性表 12/12。

## 5. 阶段 3：语言特性与语义修复

### 5.1 泛型方法
- 捕获 `TypeParameters`（名称 + where 约束），沿用 InterfaceGenerator 已验证模式：nullable 感知格式 + `FormatTypeParameterConstraints`
- 方法签名与 Args record 同步泛型化（`record GetXxxArgs<T>(...) where T : ...`）
- 透传方法与拦截方法都要保留泛型签名

### 5.2 ref/out/in/params
- `ParamInfo` 增加 `RefKind`；方法签名与 `_inner` 调用保留修饰符
- **约束**：Args record 只含普通值参数（ref/out/in 不可进 record 位置参数），AC9100 提示注明"含 ref/out 参数的方法，强类型 Handler 中不可用这些参数"

### 5.3 方法重载消歧
- Args record 命名：`{MethodName}Args_{参数类型ShortName拼接}`（如 `GetOrderArgs_Int32`），拼接结果仍冲突时追加序号兜底
- AC9100 提示同步新命名

### 5.4 Retry + CancellationToken
- 检测末位 `CancellationToken` 参数：`Task.Delay(..., ct)` 透传；每次重试前 `ct.ThrowIfCancellationRequested()`

### 5.5 Throttle 真·每秒限流
- 移除 `SemaphoreSlim(N)` 并发许可实现
- 改固定窗口：静态 `_windowStartTicks` + `_windowCount` + lock；窗口内计数达 `MaxRequestsPerSecond` 则等待至下一窗口（async：`Task.Delay`；sync：`Thread.Sleep`），循环重试入窗
- `MaxRequestsPerSecond` 名实相符

### 5.6 Retry 异常类型过滤
- Attribute 新属性：`RetryOnExceptionTypes`（`Type[]?`，null=全部异常）
- 生成 `catch (Exception __ex) when (__attempt < N && (类型 is 检查))`，未配置类型时行为与现状一致

### 5.7 CircuitBreaker 半开状态机
- 状态：Closed →（连续失败达 `CircuitFailureThreshold`）→ Open（快速失败，抛 `InvalidOperationException`）→（冷却 `CircuitBreakDurationSeconds` 结束）→ HalfOpen（单探测，`Interlocked.CompareExchange` 保证并发下仅一个探测）→ 成功回 Closed / 失败回 Open
- 保持 static per-class 共享语义（装饰器实例 Scoped，熔断状态全局共享）

### 5.8 Cache 语义
- null 结果不写入缓存（`if (__result is not null) _cache.Set(...)`）
- 缓存命中分支补日志 `"Xxx 缓存命中"`（Log 启用时），解决命中短路跳过 Log Before 的观测盲区

**交付物**：快照 ×8，全部经 `AssertCompilesCleanly` 二次编译验证。

## 6. 阶段 4：顺序可配 + 架构加固

### 6.1 PipelineOrder（Attribute 级可配）
- Attribute 新属性：`string? PipelineOrder`（逗号分隔拦截器名，如 `"Log,Cache,Retry,Metrics"`）
- 生成器重构为顺序表驱动：每种拦截器抽为独立 emit 函数（`EmitValidateBefore`/`EmitThrottleBefore`/...），按表发射；未列出的按默认序排在已列出之后
- 未知名 → **AC9007**（Warning）
- 默认顺序（文档明示）：`Validate → Authorize → Custom(OnBefore) → Throttle → Cache → CircuitBreaker → Tracing → Log → Retry → Transaction → [调用 _inner] → Custom(OnAfter/OnException) → Audit → Profiling → Metrics`
- **Before/After 阶段语义**：每个拦截器的 Before 段按 PipelineOrder 正序发射，After/Exception 段同样按 PipelineOrder 正序发射（不反转），保证"Log 的 After 在 Audit 之前"这类直觉成立
- **与方法级 Override 的交互**：PipelineOrder 是类级统一顺序；`[InterceptOverride]` 只改变某方法的拦截器集合，不改变顺序

### 6.2 模型 record 化
- `InterceptInfo`/`InterceptMethodInfo`/`ParamInfo`/`CustomHandlerInfo` 改 record + `ImmutableEquatableArray`
- 修复增量缓存失效；新增缓存哨兵测试（`GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true)` + 断言 Cached/Unchanged）

### 6.3 文档同步（AGENTS.md 义务）
- README：12 拦截器全量说明、PipelineOrder 用法、新属性表、版本历史
- CHANGELOG；samples/02-InterceptAOP、03-TypedMethodHandler 更新（降级示例生效、异步 Handler 示例）
- APP.WebAPI 示例补全四种新拦截器演示

### 6.4 快照全量
- 更新既有 3 个快照（管线 emit 重构后 diff）+ 阶段 1~3 全部新快照（合计约 20+ verified.txt）

## 7. 诊断 ID 分配（AC9xxx 段新增）

| ID | 级别 | 内容 |
|---|---|---|
| AC9004 | Error | 异步 Handler（IAsyncMethodHandler）挂载在同步方法上 |
| AC9005 | Info | ProfilingIncludeProcess=true 的开销提示 |
| AC9006 | Info | 启用 Authorize 需注册 IClaimsPrincipalProvider |
| AC9007 | Warning | PipelineOrder 含未知拦截器名 |
| AC9008 | Error | Handler 类型同时实现 IInterceptHandler 与 IMethodHandler/IAsyncMethodHandler（用户决策：编译期禁止） |
| AC9010 | Info | AuthorizePolicy 已设置但 v3 暂未实现策略检查，请使用 AuthorizeRoles |

## 8. 测试策略

- 全程 AGENTS.md 纪律：快照先行、`AssertCompilesCleanly` 二次编译、received→verified 人工审查、Intercept 测试传 `enableV2: false` + `ExtensionsReferences()`
- 新运行时接口在 AutoCode.Model（测试项目已引用真实程序集）；`TransactionScope` 在 net8.0 BCL，无需新 TPA 引用
- 阶段 4 增加增量缓存哨兵测试（参照 InterfaceGeneratorCachingTests 模式）

## 9. 风险与约束

- **快照 diff 大**：阶段 4 的 emit 重构会改变既有快照全文——重构前后行为等价性由既有快照的语义断言保障（更新 verified 而非仅机械改名）
- **Transaction 的分布式限制**：Linux 下多连接升级 DTC 会失败，文档明示适用边界
- **Authorize 语义边界**：Provider 由用户适配（框架不绑定 ASP.NET Core），不提供默认实现——避免静默失效原则
- **ref/out 与强类型 Handler 的取舍**：record 无法持有 ref 参数，此为设计约束而非缺陷，AC9100 提示注明

## 10. 实施顺序

阶段 1 → 阶段 2 → 阶段 3 → 阶段 4，每阶段独立 review 与合并，不跨阶段混改。
