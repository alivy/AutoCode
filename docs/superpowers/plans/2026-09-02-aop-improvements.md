# AOP 拦截器四阶段改进 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 AutoCode.Intercept 从"8/12 拦截器、部分承诺失效"升级为"12/12 可用、声明必生效、顺序可配、增量缓存健全"的完整编译时 AOP 框架。

**Architecture:** 保持 InterceptGenerator 独立 `IIncrementalGenerator`（V1 目录、不经 V2Gate、测试传 `enableV2: false`）不变；新运行时抽象进 AutoCode.Model 零依赖契约层；阶段 4 将管线生成重构为顺序表驱动的 emit 函数。全程 TDD：快照测试先行（`Verifier.Verify` 先于 `AssertCompilesCleanly`），received→人工审查→改名 verified。

**Tech Stack:** C# / Roslyn IIncrementalGenerator / netstandard2.0（生成器 + Model）/ net8.0（测试）/ Verify.Xunit 快照 / TRUSTED_PLATFORM_ASSEMBLIES 引用。

**Spec:** [2026-09-02-aop-improvements-design.md](../specs/2026-09-02-aop-improvements-design.md)

---

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `src/AutoCode.Model/InterceptRuntimeAbstractions.cs` | 新建 | `IClaimsPrincipalProvider`、`AuditEntry`、`IAuditStore` 运行时抽象（零依赖） |
| `src/AutoCode.Model/AutoInterceptAttribute.cs` | 修改 | 新增 9 个配置属性（AuthorizeRoles/AuthorizePolicy/Transaction*/Audit*/Profiling*/PipelineOrder/RetryOnExceptionTypes） |
| `src/AutoCode.Generators/V1/InterceptGenerator.cs` | 修改（核心） | 全部生成逻辑：Handled 降级、异步 Handler、4 种新拦截器、语言特性、语义修复、emit 重构、record 化 |
| `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs` | 修改 | 新增 ~15 个快照测试 |
| `src/AutoCode.Tests.V2/Snapshots/*.verified.txt` | 新建/更新 | 快照锁定期望输出 |
| `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorCachingTests.cs` | 新建 | 增量缓存哨兵测试（阶段 4） |
| `src/APP.WebAPI/Services/*` | 修改 | 四种新拦截器演示 |
| `README.md` / `CHANGELOG.md` / `samples/02-InterceptAOP/README.md` / `samples/03-TypedMethodHandler/README.md` | 修改 | 文档同步（阶段 4 收尾） |

**快照工作流约定**（AGENTS.md）：新测试首次运行产生 `*.received.txt` → 人工审查内容语义正确 → 改名 `*.verified.txt` 入库 → 再次运行必须通过。CI 上 `DiffEngine_Disabled=true`。

---

## 阶段 1：声明与实现对齐

### Task 1: Handled 降级真正生效（AC 语义修复）

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`（`GenerateMethod` 的 catch 段 + retry 段）
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**——在 `InterceptGeneratorSnapshotTests` 类内追加（放在类尾 `}` 前）：

```csharp
        [Fact]
        public async Task HandledFallback_ReturnsDegradedValue()
        {
            var source = """
                using System;
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService { string GetName(int id); }

                    public class FallbackHandler : MethodHandlerBase<GetNameArgs, string>
                    {
                        public override void OnException(GetNameArgs args, Exception ex, MethodContext ctx)
                        {
                            ctx.Handled = true;
                            ctx.Result = "degraded";
                        }
                    }

                    [AutoIntercept(InterceptType.Log)]
                    [CustomIntercept(typeof(FallbackHandler))]
                    public class OrderService : IOrderService
                    {
                        public string GetName(int id) => throw new InvalidOperationException("boom");
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**（received 中 catch 块应为无条件 `throw;`）

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~HandledFallback"`
Expected: FAIL（无 verified 文件，产出 received）

- [ ] **Step 3: 实现——catch 段加 Handled 检查**

在 `GenerateMethod` 中，`needTryCatch` 分支的 `GenerateExceptionHandling(...)` 调用之后、`throw;` 之前插入 `GenerateHandledFallback` 调用：

```csharp
                GenerateExceptionHandling(sb, $"{b}    ", method, flags, info);
                GenerateHandledFallback(sb, $"{b}    ", method, info);
                sb.AppendLine($"{b}    throw;");
                sb.AppendLine($"{b}}}");
```

新增方法（放在 `GenerateExceptionHandling` 之后）：

```csharp
        /// <summary>
        /// 生成 Handled 降级检查：任一 Handler 设置 Handled=true 则不再抛出。
        /// 返回值方法要求 Handler 已设置 ctx.Result（引用类型可为 null，值类型未设置会 InvalidCastException）。
        /// </summary>
        private static void GenerateHandledFallback(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info)
        {
            var effectiveHandlers = method.CustomHandlers ?? info.CustomHandlers;
            bool hasMethodHandlers = effectiveHandlers.Any(h => h.IsMethodHandler);
            bool hasLegacyHandlers = effectiveHandlers.Any(h => !h.IsMethodHandler);
            var hasResult = !method.IsVoid && !method.IsTaskNoResult;
            var innerType = method.IsAsync && hasResult ? ExtractAsyncInnerType(method.ReturnType) : method.ReturnType;

            if (hasMethodHandlers)
            {
                if (hasResult)
                    sb.AppendLine($"{b}if (__mctx.Handled) return ({innerType})__mctx.Result!;");
                else
                    sb.AppendLine($"{b}if (__mctx.Handled) return;");
            }
            if (hasLegacyHandlers)
            {
                if (hasResult)
                    sb.AppendLine($"{b}if (__ctx.Handled) return ({innerType})__ctx.Result!;");
                else
                    sb.AppendLine($"{b}if (__ctx.Handled) return;");
            }
        }
```

- [ ] **Step 4: 实现——重试路径的降级**

`hasRetry` 分支：在 retry catch 内 `OnException` 调用之后、`Task.Delay/Thread.Sleep` 之前插入 `GenerateHandledFallback(sb, $"{ind}                ", method, info);`。

随后修复"重试耗尽后 Handler 收不到最终异常"缺陷：`for` 循环前插入外层 `try` 包裹（缩进随 `b` 变量递进），并在 for 循环关闭花括号之后追加外层 catch：

```csharp
                sb.AppendLine($"{ind}    }}");
                sb.AppendLine($"{ind}    catch (Exception __ex)");
                sb.AppendLine($"{ind}    {{");
                GenerateExceptionHandling(sb, $"{ind}        ", method, flags, info);
                GenerateHandledFallback(sb, $"{ind}        ", method, info);
                sb.AppendLine($"{ind}        throw;");
                sb.AppendLine($"{ind}    }}");
```

- [ ] **Step 5: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~HandledFallback"`
Expected: 测试 FAIL（received 产出）；审查 `InterceptGeneratorSnapshotTests.HandledFallback_ReturnsDegradedValue.received.txt`：
1. 生成代码中含 `if (__mctx.Handled) return (global::System.String)__mctx.Result!;`
2. 测试输出中 `AssertCompilesCleanly` 未报 CS 错误（Verify 失败在前，编译断言在 Verify 之后）

- [ ] **Step 6: 入库快照**

Run: `Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.HandledFallback_ReturnsDegradedValue.received.txt InterceptGeneratorSnapshotTests.HandledFallback_ReturnsDegradedValue.verified.txt`

- [ ] **Step 7: 复跑确认通过**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~HandledFallback"`
Expected: PASS

- [ ] **Step 8: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "fix(intercept): Handled 降级真正生效 + 重试耗尽后异常也走 Handler 管线"
```

### Task 2: IAsyncMethodHandler 识别与 await 调用（AC9004）

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
        [Fact]
        public async Task AsyncMethodHandler_AwaitAllThreePhases()
        {
            var source = """
                using System;
                using System.Threading.Tasks;
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService { Task<string> GetNameAsync(int id); }

                    public class AsyncAuditHandler : AsyncMethodHandlerBase<GetNameAsyncArgs, string>
                    {
                        public override async Task OnBeforeAsync(GetNameAsyncArgs args, MethodContext ctx)
                            => await Task.CompletedTask;
                    }

                    public class OrderService : IOrderService
                    {
                        [CustomIntercept(typeof(AsyncAuditHandler))]
                        public Task<string> GetNameAsync(int id) => Task.FromResult("ok");
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }

        [Fact]
        public async Task AsyncHandlerOnSyncMethod_ReportsAC9004()
        {
            var source = """
                using System.Threading.Tasks;
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService { string GetName(int id); }

                    public class AsyncAuditHandler : AsyncMethodHandlerBase<GetNameArgs, string> { }

                    public class OrderService : IOrderService
                    {
                        [CustomIntercept(typeof(AsyncAuditHandler))]
                        public string GetName(int id) => "ok";
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            Assert.Contains(run.Diagnostics, d => d.Id == "AC9004");
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~AsyncMethodHandler|FullyQualifiedName~AsyncHandlerOnSyncMethod"`
Expected: FAIL（AsyncAuditHandler 触发既有 AC9003）

- [ ] **Step 3: 实现——模型与解析**

`CustomHandlerInfo` 增加属性：`public bool IsAsyncHandler { get; set; }`

`ParseCustomHandler` 中判定改为：

```csharp
                var interfaces = handlerType.AllInterfaces.Select(i => i.Name).ToList();
                var implementsHandler = interfaces.Any(i =>
                    i == "IInterceptHandler" || i == "IMethodHandler" || i == "IAsyncMethodHandler");
                if (!implementsHandler)
                {
                    diagnostics.Add(Diagnostic.Create(CustomHandlerNotImpl, classDecl.Identifier.GetLocation(), handlerType.Name));
                    return;
                }

                // 检测是强类型 IMethodHandler<,>/IAsyncMethodHandler<,> 还是通用 IInterceptHandler
                bool isMethodHandler = interfaces.Any(i => i == "IMethodHandler" || i == "IAsyncMethodHandler");
                bool isAsyncHandler = interfaces.Any(i => i == "IAsyncMethodHandler");
```

`CustomHandlerInfo` 构造处追加 `IsAsyncHandler = isAsyncHandler`。

- [ ] **Step 4: 实现——AC9004 诊断描述符**（放在 AC9003 描述符之后）：

```csharp
        private static readonly DiagnosticDescriptor AsyncHandlerOnSyncMethod = new(
            "AC9004", "异步 Handler 不能用于同步方法",
            "异步 Handler '{0}' 挂在同步方法 '{1}' 上，无法 await；请改用 IMethodHandler/IInterceptHandler 或改为异步方法",
            "AutoCode.Intercept", DiagnosticSeverity.Error, true);
```

- [ ] **Step 5: 实现——提取阶段诊断**（`ExtractInterceptInfo` 方法循环中 `methodHandlers` 计算之后）：

```csharp
                var isAsyncReturn = m.ReturnType.ToDisplayString().StartsWith("System.Threading.Tasks.Task")
                    || m.ReturnType.ToDisplayString().StartsWith("System.Threading.Tasks.ValueTask");
                if (methodHandlers != null && methodHandlers.Any(h => h.IsAsyncHandler) && !isAsyncReturn)
                {
                    diagnostics.Add(Diagnostic.Create(AsyncHandlerOnSyncMethod,
                        m.Locations.FirstOrDefault() ?? classDecl.Identifier.GetLocation(),
                        methodHandlers.First(h => h.IsAsyncHandler).ShortName, m.Name));
                }
```

- [ ] **Step 6: 实现——生成 await 调用（OnBefore/OnAfter/OnException 三处）**

`GenerateMethod` 的 Custom OnBefore 段改为：

```csharp
                    if (handler.IsAsyncHandler)
                        sb.AppendLine($"{b}await {fieldName}.OnBeforeAsync(__args, __mctx);");
                    else if (handler.IsMethodHandler)
                        sb.AppendLine($"{b}{fieldName}.OnBefore(__args, __mctx);");
                    else
                        sb.AppendLine($"{b}{fieldName}.OnBefore(__ctx);");
```

`GenerateAfterIntercepts` 与 `GenerateExceptionHandling`、retry catch 段同理：`if (handler.IsMethodHandler)` 处先判 `IsAsyncHandler`，生成 `await {fieldName}.OnAfterAsync(__args, {(resultVar ?? "default!")}, __mctx);` 与 `await {fieldName}.OnExceptionAsync(__args, __ex, __mctx);`。

- [ ] **Step 7: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~AsyncMethodHandler|FullyQualifiedName~AsyncHandlerOnSyncMethod"`
Expected: AC9004 测试 PASS；快照测试 FAIL 产出 received。审查 received：含 `await _asyncAuditHandler.OnBeforeAsync(__args, __mctx);`，无 CS 错误。

- [ ] **Step 8: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.AsyncMethodHandler_AwaitAllThreePhases.received.txt InterceptGeneratorSnapshotTests.AsyncMethodHandler_AwaitAllThreePhases.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~AsyncMethodHandler"
```

Expected: PASS

- [ ] **Step 9: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): IAsyncMethodHandler 识别与 await 调用 + AC9004 诊断"
```

### Task 3: LogResult / ExcludeMethods 生效

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
        [Fact]
        public async Task LogResult_And_ExcludeMethods_AreHonored()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IReportService
                    {
                        string Generate(int id);
                        void Cleanup();
                    }

                    [AutoIntercept(InterceptType.Log, LogResult = true, ExcludeMethods = "Cleanup")]
                    public class ReportService : IReportService
                    {
                        public string Generate(int id) => "report";
                        public void Cleanup() { }
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~LogResult"`
Expected: FAIL 产出 received（当前 Cleanup 被拦截而非透传、After 日志无结果）

- [ ] **Step 3: 实现——解析 ExcludeMethods**

`InterceptInfo` 增加 `public string? ExcludeMethods { get; set; }`。在 `ExtractInterceptInfo` 的 named 参数 switch 中追加：

```csharp
                        case "ExcludeMethods": info.ExcludeMethods = named.Value.Value as string; break;
```

`excludeMethods` 集合初始化处（`var excludeMethods = new HashSet<string>();` 之后）追加：

```csharp
            if (!string.IsNullOrEmpty(info.ExcludeMethods))
                foreach (var name in info.ExcludeMethods.Split(','))
                    excludeMethods.Add(name.Trim());
```

- [ ] **Step 4: 实现——LogResult After 日志**

`GenerateAfterIntercepts` 的 Log 段替换为：

```csharp
            // Log - After
            if (flags.HasFlag(InterceptFlags.Log))
            {
                if (info.LogResult && resultVar != null)
                {
                    sb.AppendLine($"{b}_logger.LogInformation(\"{method.Name} 完成, 结果: {{__result}}, 耗时 {{Elapsed}}ms\", {resultVar}, __sw.ElapsedMilliseconds);");
                }
                else
                {
                    sb.AppendLine($"{b}_logger.LogInformation(\"{method.Name} 完成, 耗时 {{Elapsed}}ms\", __sw.ElapsedMilliseconds);");
                }
            }
```

- [ ] **Step 5: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~LogResult"`
Expected: FAIL 产出 received。审查：Cleanup 为透传（`=> _inner.Cleanup();`）；Generate 的 After 日志含 `结果: {__result}`。

- [ ] **Step 6: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.LogResult_And_ExcludeMethods_AreHonored.received.txt InterceptGeneratorSnapshotTests.LogResult_And_ExcludeMethods_AreHonored.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~LogResult"
```

Expected: PASS

- [ ] **Step 7: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): LogResult 结果日志与 ExcludeMethods 透传排除生效"
```

### Task 4: 阶段 1 回归——示例验证与全量构建

**Files:**
- Verify: `src/APP.WebAPI/Services/OrderServiceV2.cs`（降级示例，代码无需改）

- [ ] **Step 1: 全量测试**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj`
Expected: 全部 PASS（既有 3 个快照 + 新增快照）

- [ ] **Step 2: 全解决方案构建**

Run: `dotnet build src/AutoCode.sln`
Expected: 0 errors（APP.WebAPI 中 OrderServiceV2 的降级 Handler 现在生成 `if (__mctx.Handled) return ...` 分支）

- [ ] **Step 3: 检查既有快照未漂移**

Run: `git status --short src/AutoCode.Tests.V2/Snapshots/`
Expected: 无 `.received.txt` 残留（若既有 3 个快照受缩进影响，审查 diff 语义等价后更新 verified——只允许缩进/顺序变化，不允许语义变化）

- [ ] **Step 4: Commit（如有快照更新）**

```powershell
git add -A
git commit -m "test(intercept): 阶段 1 回归——既有快照语义不变"
```

---

## 阶段 2：四种拦截器落地（12/12 可用）

### Task 5: Model 新运行时抽象 + Attribute 新属性

**Files:**
- Create: `src/AutoCode.Model/InterceptRuntimeAbstractions.cs`
- Modify: `src/AutoCode.Model/AutoInterceptAttribute.cs`

- [ ] **Step 1: 创建运行时抽象文件**

```csharp
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AutoCode.Model
{
    /// <summary>
    /// 当前用户主体提供器 - Authorize 拦截器的运行时依赖。
    /// 由使用方适配实现（如基于 HttpContext），框架不绑定 ASP.NET Core。
    /// </summary>
    public interface IClaimsPrincipalProvider
    {
        ClaimsPrincipal? CurrentPrincipal { get; }
    }

    /// <summary>
    /// 审计条目 - Audit 拦截器写入 IAuditStore 的数据载体。
    /// </summary>
    public sealed class AuditEntry
    {
        /// <summary>被审计的类名</summary>
        public string ClassName { get; set; } = "";

        /// <summary>被审计的方法名</summary>
        public string MethodName { get; set; } = "";

        /// <summary>方法参数（[Sensitive] 参数已脱敏为 "[REDACTED]"）</summary>
        public IReadOnlyDictionary<string, object?> Parameters { get; set; } = new Dictionary<string, object?>();

        /// <summary>返回值（异常时为 null）</summary>
        public object? Result { get; set; }

        /// <summary>执行耗时</summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>是否成功</summary>
        public bool Succeeded { get; set; }

        /// <summary>异常消息（成功时为 null）</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// 审计存储 - Audit 拦截器的运行时依赖。
    /// 同步方法走 Write，异步方法走 WriteAsync。
    /// </summary>
    public interface IAuditStore
    {
        void Write(AuditEntry entry);
        Task WriteAsync(AuditEntry entry);
    }
}
```

- [ ] **Step 2: Attribute 新属性**（`AutoInterceptAttribute` 类内，`ExcludeMethods` 属性之后追加）：

```csharp
        // ─── 权限选项（Authorize） ───

        /// <summary>要求的角色（逗号分隔），全部满足才放行；null 表示仅要求已认证</summary>
        public string? AuthorizeRoles { get; set; }

        /// <summary>策略名（预留：v3 仅发 AC9010 提示，请使用 AuthorizeRoles）</summary>
        public string? AuthorizePolicy { get; set; }

        // ─── 事务选项（Transaction） ───

        /// <summary>事务超时（秒，默认 60）</summary>
        public int TransactionTimeoutSeconds { get; set; } = 60;

        /// <summary>事务隔离级别（默认 ReadCommitted）</summary>
        public System.Transactions.IsolationLevel TransactionIsolationLevel { get; set; }
            = System.Transactions.IsolationLevel.ReadCommitted;

        // ─── 审计选项（Audit） ───

        /// <summary>是否在审计条目中携带参数（默认 true）</summary>
        public bool AuditIncludeParameters { get; set; } = true;

        // ─── 性能分析选项（Profiling） ───

        /// <summary>性能输出阈值（毫秒，默认 0=恒输出；>0 仅超阈值输出）</summary>
        public int ProfilingThresholdMs { get; set; }

        /// <summary>是否额外采集 Process 级指标（WorkingSet/线程数，默认 false）</summary>
        public bool ProfilingIncludeProcess { get; set; }

        // ─── 重试选项（Retry） ───

        /// <summary>仅这些异常类型触发重试（null = 全部异常）</summary>
        public Type[]? RetryOnExceptionTypes { get; set; }
```

- [ ] **Step 3: 构建验证 Model 项目**

Run: `dotnet build src/AutoCode.Model/AutoCode.Model.csproj`
Expected: 0 errors（netstandard2.0 下 `System.Security.Claims`、`System.Transactions` 均为 BCL 可用）

- [ ] **Step 4: Commit**

```powershell
git add src/AutoCode.Model/
git commit -m "feat(model): IClaimsPrincipalProvider/IAuditStore/AuditEntry 运行时抽象 + AOP 新配置属性"
```

### Task 6: Authorize 拦截器（AC9006/AC9010）

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
        [Fact]
        public async Task Authorize_GeneratesPrincipalCheck()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService { string GetSecret(int id); }

                    [AutoIntercept(InterceptType.Authorize, AuthorizeRoles = "Admin,Manager")]
                    public class OrderService : IOrderService
                    {
                        public string GetSecret(int id) => "secret";
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Authorize_GeneratesPrincipalCheck"`
Expected: FAIL 产出 received（当前 Authorize 标记无任何生成逻辑）

- [ ] **Step 3: 实现——解析属性与诊断**

`InterceptInfo` 增加字段：`public string? AuthorizeRoles { get; set; }`、`public string? AuthorizePolicy { get; set; }`。named 参数 switch 追加：

```csharp
                        case "AuthorizeRoles": info.AuthorizeRoles = named.Value.Value as string; break;
                        case "AuthorizePolicy": info.AuthorizePolicy = named.Value.Value as string; break;
```

新诊断描述符（AC9003 附近）：

```csharp
        private static readonly DiagnosticDescriptor AuthorizeProviderHint = new(
            "AC9006", "Authorize 需要注册 IClaimsPrincipalProvider",
            "类 '{0}' 启用了 Authorize 拦截，请注册 IClaimsPrincipalProvider 实现（如基于 HttpContext 的适配）",
            "AutoCode.Intercept", DiagnosticSeverity.Info, true);

        private static readonly DiagnosticDescriptor AuthorizePolicyReserved = new(
            "AC9010", "AuthorizePolicy 暂未实现",
            "AuthorizePolicy 策略检查在 v3 暂未实现，当前仅使用 AuthorizeRoles 检查角色",
            "AutoCode.Intercept", DiagnosticSeverity.Info, true);
```

`ExtractInterceptInfo` 中方法提取完成后、`return info;` 之前：

```csharp
            var anyAuthorize = info.Methods.Any(m => m.Flags.HasFlag(InterceptFlags.Authorize)) || interceptors.HasFlag(InterceptFlags.Authorize);
            if (anyAuthorize)
                diagnostics.Add(Diagnostic.Create(AuthorizeProviderHint, classDecl.Identifier.GetLocation(), classSymbol.Name));
            if (!string.IsNullOrEmpty(info.AuthorizePolicy))
                diagnostics.Add(Diagnostic.Create(AuthorizePolicyReserved, classDecl.Identifier.GetLocation()));
```

- [ ] **Step 4: 实现——生成 Authorize Before 段**

`GenerateMethod` 中，Validate 段之后插入：

```csharp
            // ─── Authorize - Before ───
            if (flags.HasFlag(InterceptFlags.Authorize))
            {
                sb.AppendLine($"{b}// ─── 权限校验 ───");
                sb.AppendLine($"{b}var __principal = _principalProvider.CurrentPrincipal;");
                sb.AppendLine($"{b}if (__principal == null || __principal.Identity == null || !__principal.Identity.IsAuthenticated)");
                sb.AppendLine($"{b}    throw new UnauthorizedAccessException(\"[Authorize] 当前用户未认证\");");
                if (!string.IsNullOrEmpty(info.AuthorizeRoles))
                {
                    var roles = string.Join(", ", info.AuthorizeRoles.Split(',').Select(r => $"\"{r.Trim()}\""));
                    sb.AppendLine($"{b}var __requiredRoles = new[] {{ {roles} }};");
                    sb.AppendLine($"{b}if (!__requiredRoles.Any(r => __principal.IsInRole(r)))");
                    sb.AppendLine($"{b}    throw new UnauthorizedAccessException($\"[Authorize] 缺少角色: {{string.Join(\", \", __requiredRoles)}}\");");
                }
                sb.AppendLine();
            }
```

- [ ] **Step 5: 实现——字段/构造/DI/USING**

`GenerateInterceptedClass`：
1. using 条件：`if (hasCustom || classFlags.HasFlag(InterceptFlags.Authorize) || classFlags.HasFlag(InterceptFlags.Audit)) sb.AppendLine("using AutoCode.Model;");`
2. 字段区追加：`if (classFlags.HasFlag(InterceptFlags.Authorize)) sb.AppendLine($"{ind}    private readonly IClaimsPrincipalProvider _principalProvider;");`
3. 构造函数参数与赋值追加（Log 段之后）：`{ ctorParams.Add("IClaimsPrincipalProvider principalProvider"); ctorAssigns.Add("_principalProvider = principalProvider;"); }`

`GenerateDIRegistration`：ctorArgs 追加 `sp.GetRequiredService<IClaimsPrincipalProvider>()`（allFlags 含 Authorize 时）。

- [ ] **Step 6: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Authorize_GeneratesPrincipalCheck"`
Expected: FAIL 产出 received。审查：含 `_principalProvider.CurrentPrincipal`、`__requiredRoles`、`UnauthorizedAccessException`；`AssertCompilesCleanly` 无 CS 错误。

- [ ] **Step 7: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.Authorize_GeneratesPrincipalCheck.received.txt InterceptGeneratorSnapshotTests.Authorize_GeneratesPrincipalCheck.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Authorize_GeneratesPrincipalCheck"
```

Expected: PASS

- [ ] **Step 8: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): Authorize 拦截器（角色检查 + AC9006/AC9010 提示）"
```

### Task 7: Transaction 拦截器

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
        [Fact]
        public async Task Transaction_GeneratesScopeWithAsyncFlow()
        {
            var source = """
                using System.Threading.Tasks;
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService { Task<bool> PayAsync(int orderId); }

                    [AutoIntercept(InterceptType.Transaction, TransactionTimeoutSeconds = 30)]
                    public class OrderService : IOrderService
                    {
                        public Task<bool> PayAsync(int orderId) => Task.FromResult(true);
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Transaction_GeneratesScopeWithAsyncFlow"`
Expected: FAIL 产出 received（当前 Transaction 无生成逻辑）

- [ ] **Step 3: 实现——解析属性**

`InterceptInfo` 增加：`public int TransactionTimeoutSeconds { get; set; } = 60;`、`public System.Transactions.IsolationLevel TransactionIsolationLevel { get; set; } = System.Transactions.IsolationLevel.ReadCommitted;`

named 参数 switch 追加：

```csharp
                        case "TransactionTimeoutSeconds": info.TransactionTimeoutSeconds = named.Value.Value is int i7 ? i7 : 60; break;
                        case "TransactionIsolationLevel": info.TransactionIsolationLevel = named.Value.Value is int il ? (System.Transactions.IsolationLevel)il : System.Transactions.IsolationLevel.ReadCommitted; break;
```

- [ ] **Step 4: 实现——生成 Transaction 段**

`GenerateMethod` 中，Retry 段之后（`hasRetry` 块关闭后）、`needTryCatch` 判定之前插入：

```csharp
            // ─── Transaction - Before ───
            if (flags.HasFlag(InterceptFlags.Transaction))
            {
                sb.AppendLine($"{b}// ─── 事务 ───");
                var txOptions = $"new TransactionOptions {{ IsolationLevel = System.Transactions.IsolationLevel.{info.TransactionIsolationLevel}, Timeout = TimeSpan.FromSeconds({info.TransactionTimeoutSeconds}) }}";
                if (method.IsAsync)
                {
                    sb.AppendLine($"{b}using var __tx = new TransactionScope(TransactionScopeOption.Required, {txOptions}, TransactionScopeAsyncFlowOption.Enabled);");
                }
                else
                {
                    sb.AppendLine($"{b}using var __tx = new TransactionScope(TransactionScopeOption.Required, {txOptions});");
                }
                sb.AppendLine();
            }
```

`GenerateAfterIntercepts` 开头追加（成功提交，未 Complete 时 dispose 自动回滚）：

```csharp
            // Transaction - 提交
            if (flags.HasFlag(InterceptFlags.Transaction))
                sb.AppendLine($"{b}__tx.Complete();");
```

- [ ] **Step 5: 实现——USING**

`GenerateInterceptedClass` 中，`classFlags.HasFlag(InterceptFlags.Transaction)` 时输出 `using System.Transactions;`。

- [ ] **Step 6: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Transaction_GeneratesScopeWithAsyncFlow"`
Expected: FAIL 产出 received。审查：含 `TransactionScopeAsyncFlowOption.Enabled`、`__tx.Complete()`、`Timeout = TimeSpan.FromSeconds(30)`；无 CS 错误。

- [ ] **Step 7: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.Transaction_GeneratesScopeWithAsyncFlow.received.txt InterceptGeneratorSnapshotTests.Transaction_GeneratesScopeWithAsyncFlow.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Transaction_GeneratesScopeWithAsyncFlow"
```

Expected: PASS

- [ ] **Step 8: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): Transaction 拦截器（TransactionScope + AsyncFlow）"
```

### Task 8: Audit 拦截器（含 Sensitive 脱敏）

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
        [Fact]
        public async Task Audit_WritesEntryOnSuccessAndFailure()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService { int GetTotal(string token); }

                    public class InMemoryAuditStore : IAuditStore
                    {
                        public void Write(AuditEntry entry) { }
                        public System.Threading.Tasks.Task WriteAsync(AuditEntry entry) => System.Threading.Tasks.Task.CompletedTask;
                    }

                    [AutoIntercept(InterceptType.Audit)]
                    public class OrderService : IOrderService
                    {
                        [Sensitive]
                        public int GetTotal(string token) => 1;
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

注意：`SensitiveAttribute` 在 `AutoCode.Model` 命名空间下（V2 LogDecoratorGenerator 已使用），测试源已 `using AutoCode.Model;`。

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Audit_WritesEntryOnSuccessAndFailure"`
Expected: FAIL 产出 received

- [ ] **Step 3: 实现——参数敏感检测**

`ParamInfo` 增加 `public bool IsSensitive { get; set; }`。`ExtractInterceptInfo` 的 `Parameters` 构造处：

```csharp
                    Parameters = m.Parameters.Select(p => new ParamInfo
                    {
                        Name = p.Name,
                        Type = p.Type.ToDisplayString(nullableFormat),
                        IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated,
                        IsSensitive = p.GetAttributes().Any(a =>
                            a.AttributeClass?.Name == "SensitiveAttribute" || a.AttributeClass?.Name == "Sensitive")
                    }).ToList()
```

- [ ] **Step 4: 实现——解析 AuditIncludeParameters**

`InterceptInfo` 增加 `public bool AuditIncludeParameters { get; set; } = true;`，named 参数 switch 追加 `case "AuditIncludeParameters": info.AuditIncludeParameters = named.Value.Value is bool b3 && b3; break;`

- [ ] **Step 5: 实现——Audit After 段**

`GenerateAfterIntercepts` 末尾（自定义 OnAfter 之后）追加：

```csharp
            // Audit - After（成功审计）
            if (flags.HasFlag(InterceptFlags.Audit))
            {
                sb.AppendLine($"{b}var __auditEntry = new AuditEntry");
                sb.AppendLine($"{b}{{");
                sb.AppendLine($"{b}    ClassName = \"{info.ClassName}\",");
                sb.AppendLine($"{b}    MethodName = \"{method.Name}\",");
                GenerateAuditParameters(sb, $"{b}    ", method, info);
                sb.AppendLine($"{b}    Result = {resultVar ?? "null"},");
                sb.AppendLine($"{b}    Elapsed = __sw.Elapsed,");
                sb.AppendLine($"{b}    Succeeded = true");
                sb.AppendLine($"{b}}});");
                if (method.IsAsync)
                    sb.AppendLine($"{b}await _auditStore.WriteAsync(__auditEntry);");
                else
                    sb.AppendLine($"{b}_auditStore.Write(__auditEntry);");
            }
```

新增辅助方法：

```csharp
        /// <summary>生成审计参数字典（[Sensitive] 参数脱敏为 "[REDACTED]"）。</summary>
        private static void GenerateAuditParameters(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info)
        {
            if (!info.AuditIncludeParameters || method.Parameters.Count == 0)
            {
                sb.AppendLine($"{b}Parameters = new Dictionary<string, object?>(),");
                return;
            }
            sb.AppendLine($"{b}Parameters = new Dictionary<string, object?>");
            sb.AppendLine($"{b}{{");
            foreach (var p in method.Parameters)
            {
                var value = p.IsSensitive ? "\"[REDACTED]\"" : p.Name;
                sb.AppendLine($"{b}    [\"{p.Name}\"] = {value},");
            }
            sb.AppendLine($"{b}}},");
        }
```

- [ ] **Step 6: 实现——Audit 异常段**

`GenerateExceptionHandling` 末尾追加：

```csharp
            // Audit - 异常审计
            if (flags.HasFlag(InterceptFlags.Audit))
            {
                sb.AppendLine($"{b}var __auditEntry = new AuditEntry");
                sb.AppendLine($"{b}{{");
                sb.AppendLine($"{b}    ClassName = \"{info.ClassName}\",");
                sb.AppendLine($"{b}    MethodName = \"{method.Name}\",");
                GenerateAuditParameters(sb, $"{b}    ", method, info);
                sb.AppendLine($"{b}    Elapsed = __sw.Elapsed,");
                sb.AppendLine($"{b}    Succeeded = false,");
                sb.AppendLine($"{b}    Error = __ex.Message");
                sb.AppendLine($"{b}}});");
                if (method.IsAsync)
                    sb.AppendLine($"{b}await _auditStore.WriteAsync(__auditEntry);");
                else
                    sb.AppendLine($"{b}_auditStore.Write(__auditEntry);");
            }
```

- [ ] **Step 7: 实现——字段/构造/DI**

`GenerateInterceptedClass`：字段 `private readonly IAuditStore _auditStore;`；ctor 参数 `IAuditStore auditStore` + 赋值（classFlags 含 Audit 时）。`GenerateDIRegistration`：ctorArgs 追加 `sp.GetRequiredService<IAuditStore>()`。

- [ ] **Step 8: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Audit_WritesEntryOnSuccessAndFailure"`
Expected: FAIL 产出 received。审查：成功路径 `Succeeded = true`、`["token"] = "[REDACTED]"`；异常路径 `Succeeded = false`、`Error = __ex.Message`；无 CS 错误。

- [ ] **Step 9: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.Audit_WritesEntryOnSuccessAndFailure.received.txt InterceptGeneratorSnapshotTests.Audit_WritesEntryOnSuccessAndFailure.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Audit_WritesEntryOnSuccessAndFailure"
```

Expected: PASS

- [ ] **Step 10: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): Audit 拦截器（成功/异常双路径审计 + Sensitive 脱敏）"
```

### Task 9: Profiling 拦截器（可控制加载，AC9005）

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
        [Fact]
        public async Task Profiling_WithProcessMetrics_IsSwitchable()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IReportService { string Generate(int id); }

                    [AutoIntercept(InterceptType.Profiling, ProfilingThresholdMs = 50, ProfilingIncludeProcess = true)]
                    public class ReportService : IReportService
                    {
                        public string Generate(int id) => "report";
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Profiling_WithProcessMetrics"`
Expected: FAIL 产出 received

- [ ] **Step 3: 实现——解析属性**

`InterceptInfo` 增加：`public int ProfilingThresholdMs { get; set; }`、`public bool ProfilingIncludeProcess { get; set; }`。named 参数 switch 追加：

```csharp
                        case "ProfilingThresholdMs": info.ProfilingThresholdMs = named.Value.Value is int i8 ? i8 : 0; break;
                        case "ProfilingIncludeProcess": info.ProfilingIncludeProcess = named.Value.Value is bool b4 && b4; break;
```

新诊断描述符：

```csharp
        private static readonly DiagnosticDescriptor ProfilingProcessHint = new(
            "AC9005", "ProfilingIncludeProcess 有额外开销",
            "类 '{0}' 启用了 ProfilingIncludeProcess，每次调用都会采集 Process 级指标，建议仅在排查期开启",
            "AutoCode.Intercept", DiagnosticSeverity.Info, true);
```

`ExtractInterceptInfo` 中，与 AC9006 同位置：

```csharp
            if (info.ProfilingIncludeProcess)
                diagnostics.Add(Diagnostic.Create(ProfilingProcessHint, classDecl.Identifier.GetLocation(), classSymbol.Name));
```

- [ ] **Step 4: 实现——Logger 注入条件扩展（Log 或 Profiling）**

`GenerateInterceptedClass` 中所有 `classFlags.HasFlag(InterceptFlags.Log)` 的 logger 相关判断（`using Microsoft.Extensions.Logging;`、字段、ctor 参数、ctor 赋值）统一改为 `(classFlags.HasFlag(InterceptFlags.Log) || classFlags.HasFlag(InterceptFlags.Profiling))`。`GenerateDIRegistration` 同理。

- [ ] **Step 5: 实现——Profiling Before（GC 基线）**

`GenerateMethod` 中，`__sw` 计时生成行之后追加：

```csharp
            if (flags.HasFlag(InterceptFlags.Profiling))
                sb.AppendLine($"{b}var __gcBefore = GC.GetTotalMemory(false);");
```

- [ ] **Step 6: 实现——Profiling After**

`GenerateAfterIntercepts` 末尾（Audit 段之后）追加：

```csharp
            // Profiling - After（性能输出，阈值可配）
            if (flags.HasFlag(InterceptFlags.Profiling))
            {
                var ind2 = b;
                if (info.ProfilingThresholdMs > 0)
                {
                    sb.AppendLine($"{b}if (__sw.ElapsedMilliseconds >= {info.ProfilingThresholdMs})");
                    sb.AppendLine($"{b}{{");
                    ind2 = b + "    ";
                }
                sb.AppendLine($"{ind2}var __gcDelta = GC.GetTotalMemory(false) - __gcBefore;");
                if (info.ProfilingIncludeProcess)
                {
                    sb.AppendLine($"{ind2}var __proc = System.Diagnostics.Process.GetCurrentProcess();");
                    sb.AppendLine($"{ind2}_logger.LogInformation(\"{method.Name} 性能: 耗时 {{Elapsed}}ms, GC增量 {{GcDelta}}B, 工作集 {{Ws}}B, 线程数 {{Threads}}\", __sw.ElapsedMilliseconds, __gcDelta, __proc.WorkingSet64, __proc.Threads.Count);");
                }
                else
                {
                    sb.AppendLine($"{ind2}_logger.LogInformation(\"{method.Name} 性能: 耗时 {{Elapsed}}ms, GC增量 {{GcDelta}}B\", __sw.ElapsedMilliseconds, __gcDelta);");
                }
                if (info.ProfilingThresholdMs > 0)
                    sb.AppendLine($"{b}}}");
            }
```

- [ ] **Step 7: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Profiling_WithProcessMetrics"`
Expected: FAIL 产出 received。审查：含 `__gcBefore`、阈值 `if (__sw.ElapsedMilliseconds >= 50)`、`WorkingSet64`；无 CS 错误。

- [ ] **Step 8: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.Profiling_WithProcessMetrics_IsSwitchable.received.txt InterceptGeneratorSnapshotTests.Profiling_WithProcessMetrics_IsSwitchable.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Profiling_WithProcessMetrics"
```

Expected: PASS

- [ ] **Step 9: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): Profiling 拦截器（GC/耗时 + 可开关 Process 级指标 + AC9005）"
```

### Task 10: 阶段 2 示例与回归

**Files:**
- Create: `src/APP.WebAPI/Services/PrincipalProvider.cs`
- Create: `src/APP.WebAPI/Services/AuditLogStore.cs`
- Modify: `src/APP.WebAPI/Services/PaymentService.cs`

- [ ] **Step 1: 创建 ClaimsPrincipalProvider 适配示例**

```csharp
using System.Security.Claims;
using AutoCode.Model;

namespace APP.WebAPI.Services
{
    /// <summary>示例：基于 Thread.CurrentPrincipal 的 ClaimsPrincipal 提供器（生产环境请改用 HttpContext 适配）。</summary>
    public class PrincipalProvider : IClaimsPrincipalProvider
    {
        public ClaimsPrincipal? CurrentPrincipal => ClaimsPrincipal.Current;
    }
}
```

- [ ] **Step 2: 创建内存审计存储示例**

```csharp
using System.Collections.Concurrent;
using System.Threading.Tasks;
using AutoCode.Model;

namespace APP.WebAPI.Services
{
    /// <summary>示例：内存审计存储。</summary>
    public class AuditLogStore : IAuditStore
    {
        public static readonly ConcurrentQueue<AuditEntry> Recent = new();

        public void Write(AuditEntry entry) => Recent.Enqueue(entry);
        public Task WriteAsync(AuditEntry entry) { Recent.Enqueue(entry); return Task.CompletedTask; }
    }
}
```

- [ ] **Step 3: 演示新拦截器**（`PaymentService` 类特性替换为组合，演示 Authorize+Transaction+Audit+Profiling）：

```csharp
    [AutoIntercept(InterceptType.Authorize | InterceptType.Transaction | InterceptType.Audit | InterceptType.Profiling,
        AuthorizeRoles = "Admin",
        TransactionTimeoutSeconds = 30,
        ProfilingThresholdMs = 100)]
    public class PaymentService : IPaymentService
```

- [ ] **Step 4: DI 注册**（Program.cs 或既有注册扩展中追加）：

```csharp
services.AddScoped<IClaimsPrincipalProvider, PrincipalProvider>();
services.AddScoped<IAuditStore, AuditLogStore>();
```

- [ ] **Step 5: 全量回归**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj; dotnet build src/AutoCode.sln`
Expected: 全部 PASS / 0 errors

- [ ] **Step 6: Commit**

```powershell
git add src/APP.WebAPI/
git commit -m "feat(samples): Authorize/Transaction/Audit/Profiling 演示 + Provider/AuditStore 适配示例"
```

---

## 阶段 3：语言特性与语义修复

### Task 11: 泛型方法支持（TypeParameters + where 约束）

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**（`InterceptGeneratorSnapshotTests` 类尾追加）：

```csharp
        [Fact]
        public async Task GenericMethod_PreservesTypeParametersAndConstraints()
        {
            var source = """
                using System;
                using System.Collections.Generic;
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IQueryService
                    {
                        List<T> Filter<T>(T source, Func<T, bool> predicate) where T : class;
                    }

                    [AutoIntercept(InterceptType.Log)]
                    public class QueryService : IQueryService
                    {
                        public List<T> Filter<T>(T source, Func<T, bool> predicate) where T : class => new();
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }

        [Fact]
        public async Task GenericMethod_WithHandler_GeneratesGenericArgsRecord()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IStoreService { int Get<TId>(TId id) where TId : struct; }

                    public class GetIntHandler : MethodHandlerBase<GetArgs<int>, int> { }

                    [AutoIntercept(InterceptType.Log)]
                    public class StoreService : IStoreService
                    {
                        [CustomIntercept(typeof(GetIntHandler))]
                        public int Get<TId>(TId id) where TId : struct => 0;
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~GenericMethod"`
Expected: FAIL（当前生成器丢方法泛型签名与 where 约束，装饰器无法实现接口成员）

- [ ] **Step 3: 模型属性**——`InterceptMethodInfo` 追加：

```csharp
        /// <summary>方法自身泛型参数名列表（如 "T, TId"；无泛型为空字符串）</summary>
        public string TypeParameters { get; set; } = "";
        /// <summary>泛型 where 约束（如 "where TId : struct"；无约束为空字符串）</summary>
        public string TypeConstraints { get; set; } = "";
```

- [ ] **Step 4: 约束格式化帮助方法**（`ExtractAsyncInnerType` 之后追加）：

```csharp
        /// <summary>格式化单个泛型参数的 where 约束（无约束返回 null）</summary>
        private static string? FormatTypeParameterConstraints(ITypeParameterSymbol tp, SymbolDisplayFormat format)
        {
            var parts = new List<string>();
            if (tp.HasReferenceTypeConstraint)
                parts.Add(tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            if (tp.HasUnmanagedTypeConstraint)
                parts.Add("unmanaged");
            else if (tp.HasValueTypeConstraint)
                parts.Add("struct");
            if (tp.HasNotNullConstraint)
                parts.Add("notnull");
            foreach (var constraintType in tp.ConstraintTypes)
                parts.Add(constraintType.ToDisplayString(format));
            if (tp.HasConstructorConstraint)
                parts.Add("new()");
            return parts.Count == 0 ? null : $"where {tp.Name} : {string.Join(", ", parts)}";
        }
```

- [ ] **Step 5: 提取处填充**——`ExtractInterceptInfo` 的 `info.Methods.Add(new InterceptMethodInfo {...})` 中（`Parameters` 之后）追加：

```csharp
                    TypeParameters = string.Join(", ", m.TypeParameters.Select(tp => tp.Name)),
                    TypeConstraints = string.Join(" ", m.TypeParameters
                        .Select(tp => FormatTypeParameterConstraints(tp, nullableFormat))
                        .Where(c => c != null)),
```

- [ ] **Step 6: 生成修改（5 处）**

(a) `GenerateMethod` 顶部（`var returnType = method.ReturnType;` 之后）追加：

```csharp
            var typeParams = string.IsNullOrEmpty(method.TypeParameters) ? "" : $"<{method.TypeParameters}>";
            var constraints = string.IsNullOrEmpty(method.TypeConstraints) ? "" : $" {method.TypeConstraints}";
```

(b) 签名行——透传段 `public {(method.IsAsync ? "async " : "")}{returnType} {method.Name}({parameters})` 与拦截段同名行，两处均改为 `{method.Name}{typeParams}({parameters}){constraints}`；透传段调用行 `=> _inner.{method.Name}({args});` 改为 `=> _inner.{method.Name}{typeParams}({args});`

(c) `GenerateMethodInvocation` 中 4 处 `_inner.{method.Name}({args})` 全部改为 `_inner.{method.Name}{typeParams}({args})`（函数内先算 `var typeParams = string.IsNullOrEmpty(method.TypeParameters) ? "" : $"<{method.TypeParameters}>";`）

(d) `GenerateMethod` 的 `__args` 构造处：

```csharp
                    var argsName = $"{method.Name}Args";
                    var argsTypeArgs = string.IsNullOrEmpty(method.TypeParameters) ? "" : $"<{method.TypeParameters}>";
                    var argsCtor = string.Join(", ", method.Parameters.Select(p => p.Name));
                    sb.AppendLine($"{b}var __args = new {argsName}{argsTypeArgs}({argsCtor});");
```

(e) `GenerateArgsRecords` 的 record 声明（有参/无参两分支）改为：

```csharp
                var recordTypeParams = string.IsNullOrEmpty(method.TypeParameters) ? "" : $"<{method.TypeParameters}>";
                var recordConstraints = string.IsNullOrEmpty(method.TypeConstraints) ? "" : $" {method.TypeConstraints}";
                if (method.Parameters.Count == 0)
                {
                    // 无参方法生成空 record
                    sb.AppendLine($"{ind}public record {argsName}{recordTypeParams}{recordConstraints};");
                }
                else
                {
                    sb.AppendLine($"{ind}public record {argsName}{recordTypeParams}({paramList}){recordConstraints};");
                }
```

(f) AC9100 提示的 argsName（`ExtractInterceptInfo` 中）：

```csharp
                    var argsName = m.TypeParameters.Length > 0
                        ? $"{m.Name}Args<{string.Join(", ", m.TypeParameters.Select(tp => tp.Name))}>"
                        : $"{m.Name}Args";
```

- [ ] **Step 7: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~GenericMethod"`
Expected: FAIL 产出 received。审查两个 received：
1. 含 `Filter<T>(... where T : class)` 签名与 `_inner.Filter<T>(source, predicate)`
2. 含 `public record GetArgs<TId>(TId Id) where TId : struct;` 与 `var __args = new GetArgs<TId>(id);`
3. `AssertCompilesCleanly` 无 CS 错误

- [ ] **Step 8: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.GenericMethod_PreservesTypeParametersAndConstraints.received.txt InterceptGeneratorSnapshotTests.GenericMethod_PreservesTypeParametersAndConstraints.verified.txt
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.GenericMethod_WithHandler_GeneratesGenericArgsRecord.received.txt InterceptGeneratorSnapshotTests.GenericMethod_WithHandler_GeneratesGenericArgsRecord.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~GenericMethod"
```

Expected: PASS

- [ ] **Step 9: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): 泛型方法支持（TypeParameters + where 约束 + 泛型 Args record）"
```

### Task 12: ref/out/in/params 参数修饰符保留

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**：

```csharp
        [Fact]
        public async Task RefOutInParams_PreservedInDecorator()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface ICounterService
                    {
                        bool TryIncrement(ref int value, out string message, in decimal delta, params string[] tags);
                    }

                    [AutoIntercept(InterceptType.Log)]
                    public class CounterService : ICounterService
                    {
                        public bool TryIncrement(ref int value, out string message, in decimal delta, params string[] tags)
                        {
                            value++;
                            message = "ok";
                            return true;
                        }
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~RefOutInParams"`
Expected: FAIL（当前生成器丢 ref/out/in/params 修饰符，二次编译报 CS1620/CS1615 等）

- [ ] **Step 3: 模型属性**——`ParamInfo` 追加：

```csharp
        /// <summary>参数修饰符：""/ref/out/in/params</summary>
        public string RefKind { get; set; } = "";
```

- [ ] **Step 4: 提取处填充**——`Parameters = m.Parameters.Select(p => new ParamInfo {...})` 中追加：

```csharp
                        RefKind = p.IsParams ? "params"
                            : p.RefKind == Microsoft.CodeAnalysis.RefKind.None ? ""
                            : p.RefKind.ToString().ToLowerInvariant(),
```

- [ ] **Step 5: 生成修改**

(a) `GenerateMethod` 顶部 `parameters`/`args` 构造改为：

```csharp
            var parameters = string.Join(", ", method.Parameters.Select(p =>
                string.IsNullOrEmpty(p.RefKind) ? $"{p.Type} {p.Name}" : $"{p.RefKind} {p.Type} {p.Name}"));
            var args = string.Join(", ", method.Parameters.Select(p =>
                string.IsNullOrEmpty(p.RefKind) ? p.Name : $"{p.RefKind} {p.Name}"));
```

（透传段复用这两个变量，自动获得修饰符保留。）

(b) Args record 过滤 by-ref 参数——`GenerateMethod` 的 `argsCtor` 与 `GenerateArgsRecords` 的 `paramList` 改为只含普通/params 参数：

```csharp
// GenerateMethod 的 __args 构造处
                    var argsCtor = string.Join(", ", method.Parameters
                        .Where(p => p.RefKind is "" or "params")
                        .Select(p => p.Name));

// GenerateArgsRecords 中
                var recordParams = method.Parameters.Where(p => p.RefKind is "" or "params").ToList();
                var paramList = string.Join(", ", recordParams.Select(p =>
                {
                    // 参数名转 PascalCase（record 属性规范）
                    var propName = char.ToUpper(p.Name[0]) + p.Name.Substring(1);
                    return $"{p.Type} {propName}";
                }));
```

并同步将 record 声明的无参判断从 `method.Parameters.Count == 0` 改为 `recordParams.Count == 0`。

(c) AC9100 提示注明（`ExtractInterceptInfo` 的 `paramDesc` 计算处追加）：

```csharp
                    var hasByRef = m.Parameters.Any(p =>
                        p.RefKind == Microsoft.CodeAnalysis.RefKind.Ref ||
                        p.RefKind == Microsoft.CodeAnalysis.RefKind.Out ||
                        p.RefKind == Microsoft.CodeAnalysis.RefKind.In);
                    if (hasByRef)
                        paramDesc += "（ref/out/in 参数不进入 Args record）";
```

- [ ] **Step 6: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~RefOutInParams"`
Expected: FAIL 产出 received。审查：
1. 签名 `public bool TryIncrement(ref int value, out string message, in decimal delta, params string[] tags)`
2. 调用 `_inner.TryIncrement(ref value, out message, in delta, tags)`
3. record `public record TryIncrementArgs(string[] Tags);`（ref/out/in 已过滤）
4. 无 CS 错误

- [ ] **Step 7: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.RefOutInParams_PreservedInDecorator.received.txt InterceptGeneratorSnapshotTests.RefOutInParams_PreservedInDecorator.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~RefOutInParams"
```

Expected: PASS

- [ ] **Step 8: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): ref/out/in/params 修饰符保留（by-ref 参数不进 Args record）"
```

### Task 13: 方法重载消歧（Args record 参数类型后缀命名）

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Modify: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`（既有测试源码的 Handler 基类引用同步）
- Update: `src/AutoCode.Tests.V2/Snapshots/*.verified.txt`（全部含 Args record 的快照重生成）

- [ ] **Step 1: 写失败测试**：

```csharp
        [Fact]
        public async Task OverloadedMethods_GenerateDisambiguatedArgsRecords()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface ICalcService
                    {
                        int Add(int a, int b);
                        int Add(string a, string b);
                    }

                    public class IntAddHandler : MethodHandlerBase<AddArgs_Int32_Int32, int> { }

                    public class CalcService : ICalcService
                    {
                        [CustomIntercept(typeof(IntAddHandler))]
                        public int Add(int a, int b) => a + b;

                        public int Add(string a, string b) => a.Length + b.Length;
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~OverloadedMethods"`
Expected: FAIL（当前两个重载生成同名 `AddArgs` record → CS0101）

- [ ] **Step 3: 实现命名帮助方法**（`ExtractAsyncInnerType` 之后追加）：

```csharp
        /// <summary>Args record 命名：{MethodName}Args_{参数类型短名拼接}，实现重载消歧。</summary>
        private static string GetArgsRecordName(InterceptMethodInfo method)
        {
            var recordParams = method.Parameters.Where(p => p.RefKind is "" or "params").ToList();
            if (recordParams.Count == 0) return $"{method.Name}Args";
            return $"{method.Name}Args_" + string.Join("_", recordParams.Select(p => GetShortTypeName(p.Type)));
        }

        /// <summary>IMethodSymbol 版（AC9100 提示在 InterceptMethodInfo 构造前计算）。</summary>
        private static string GetArgsRecordName(IMethodSymbol m)
        {
            var byVal = m.Parameters.Where(p => p.RefKind == Microsoft.CodeAnalysis.RefKind.None).ToList();
            if (byVal.Count == 0) return $"{m.Name}Args";
            var minimal = SymbolDisplayFormat.MinimallyQualifiedFormat;
            return $"{m.Name}Args_" + string.Join("_", byVal.Select(p => GetShortTypeName(p.Type.ToDisplayString(minimal))));
        }

        /// <summary>提取类型短名（去命名空间与可空注解；特殊类型映射为 CLR 名，泛型只取短名）。</summary>
        private static string GetShortTypeName(string type)
        {
            var t = type.TrimEnd('?');
            if (t.Contains('<')) t = t.Substring(0, t.IndexOf('<'));
            var dot = t.LastIndexOf('.');
            if (dot >= 0) t = t.Substring(dot + 1);
            switch (t)
            {
                case "int": return "Int32";
                case "long": return "Int64";
                case "short": return "Int16";
                case "byte": return "Byte";
                case "bool": return "Boolean";
                case "decimal": return "Decimal";
                case "double": return "Double";
                case "float": return "Single";
                case "char": return "Char";
                case "string": return "String";
                case "object": return "Object";
                default: return t;
            }
        }
```

- [ ] **Step 4: 替换三处 argsName 计算**

(a) `GenerateMethod` 的 `__args` 构造处：`var argsName = $"{method.Name}Args";` → `var argsName = GetArgsRecordName(method);`
(b) `GenerateArgsRecords` 循环内：`var argsName = $"{method.Name}Args";` → `var argsName = GetArgsRecordName(method);`
(c) AC9100 提示处（`ExtractInterceptInfo` 中 Task 11 Step 6(f) 的泛型版本）：

```csharp
                    var argsName = GetArgsRecordName(m);
                    if (m.TypeParameters.Length > 0)
                        argsName += $"<{string.Join(", ", m.TypeParameters.Select(tp => tp.Name))}>";
```

- [ ] **Step 5: 更新既有测试源码的 Handler 基类引用**（argsName 变了，引用旧名的测试输入会编译失败）：

| 文件/测试 | 旧引用 | 新引用 |
|---|---|---|
| `InterceptGeneratorSnapshotTests.HandledFallback_ReturnsDegradedValue` | `MethodHandlerBase<GetNameArgs, string>` | `MethodHandlerBase<GetNameArgs_Int32, string>` |
| `InterceptGeneratorSnapshotTests.AsyncMethodHandler_AwaitAllThreePhases` | `AsyncMethodHandlerBase<GetNameAsyncArgs, string>` | `AsyncMethodHandlerBase<GetNameAsyncArgs_Int32, string>` |
| `InterceptGeneratorSnapshotTests.AsyncHandlerOnSyncMethod_ReportsAC9004` | `AsyncMethodHandlerBase<GetNameArgs, string>` | `AsyncMethodHandlerBase<GetNameArgs_Int32, string>` |
| `InterceptGeneratorSnapshotTests.GenericMethod_WithHandler_GeneratesGenericArgsRecord` | `MethodHandlerBase<GetArgs<int>, int>` | `MethodHandlerBase<GetArgs_TId<int>, int>` |

- [ ] **Step 6: 全量重生成受影响快照**

```powershell
Remove-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.*.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~InterceptGeneratorSnapshotTests"
```

Expected: 全部 FAIL 产出 received（Args record 命名变化）

- [ ] **Step 7: 审查 received 后批量入库**

审查要点（用 `git diff --no-index` 对比新旧文件验证仅命名变化、无语义变化）：
1. 所有被拦截方法（非透传）的 Args record 均带参数类型后缀（如 `ChargeArgs_Int32_Decimal`、`GenerateArgs_Int32`）
2. 无参方法的 record 保持无后缀（`GetValueArgs`）
3. `LogInterceptor_ClassLevel` 的 `GetValueArgs` 无变化、`MethodLevelIntercept` 的 Cleanup 透传仍无 record

```powershell
Get-ChildItem src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.*.received.txt | ForEach-Object { Rename-Item $_.FullName ($_.Name -replace '\.received\.txt$', '.verified.txt') }
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~InterceptGeneratorSnapshotTests"
```

Expected: 全部 PASS

- [ ] **Step 8: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "fix(intercept): Args record 参数类型后缀命名，方法重载不再 CS0101"
```

### Task 14: Retry CancellationToken 透传 + Throttle 固定窗口 + 异常类型过滤

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**（三个 Fact）：

```csharp
        [Fact]
        public async Task Retry_PassesCancellationTokenToDelay()
        {
            var source = """
                using System.Threading;
                using System.Threading.Tasks;
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IDataService { Task<string> FetchAsync(string url, CancellationToken ct); }

                    [AutoIntercept(InterceptType.Retry, MaxRetryCount = 3)]
                    public class DataService : IDataService
                    {
                        public Task<string> FetchAsync(string url, CancellationToken ct) => Task.FromResult("data");
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }

        [Fact]
        public async Task Throttle_UsesFixedWindowRateLimit()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IReportService { void Generate(int id); }

                    [AutoIntercept(InterceptType.Throttle, MaxRequestsPerSecond = 5)]
                    public class ReportService : IReportService
                    {
                        public void Generate(int id) { }
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }

        [Fact]
        public async Task RetryOnExceptionTypes_FiltersRetryableExceptions()
        {
            var source = """
                using System;
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IApiService { string Call(); }

                    [AutoIntercept(InterceptType.Retry, MaxRetryCount = 3, RetryOnExceptionTypes = new[] { typeof(TimeoutException) })]
                    public class ApiService : IApiService
                    {
                        public string Call() => throw new TimeoutException();
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Retry_PassesCancellationToken|FullyQualifiedName~Throttle_UsesFixedWindow|FullyQualifiedName~RetryOnExceptionTypes"`
Expected: 三个均 FAIL 产出 received（当前 Task.Delay 无 CT、SemaphoreSlim 并发许可、when 无类型过滤）

- [ ] **Step 3: 模型与解析**

`InterceptInfo` 追加：

```csharp
        /// <summary>仅这些异常类型触发重试（空 = 全部异常），存 FullyQualified 类型名</summary>
        public List<string> RetryExceptionTypeNames { get; set; } = new();
```

`InterceptMethodInfo` 追加：

```csharp
        /// <summary>末位参数是否为 CancellationToken（Retry 延迟透传取消令牌）</summary>
        public bool HasCancellationToken { get; set; }
```

named 参数 switch 追加（`case "MaxRequestsPerSecond"` 附近）：

```csharp
                        case "RetryOnExceptionTypes":
                            if (named.Value.Kind == TypedConstantKind.Array)
                            {
                                foreach (var v in named.Value.Values)
                                    if (v.Value is ITypeSymbol ts)
                                        info.RetryExceptionTypeNames.Add(ts.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                            }
                            break;
```

`case "MaxRequestsPerSecond"` 改为（固定窗口下 0 会死循环，防御 clamp）：

```csharp
                        case "MaxRequestsPerSecond": info.MaxRequestsPerSecond = named.Value.Value is int i6 && i6 > 0 ? i6 : 1; break;
```

`info.Methods.Add` 中追加：

```csharp
                    HasCancellationToken = m.Parameters.LastOrDefault()?.Type.ToDisplayString() == "System.Threading.CancellationToken",
```

- [ ] **Step 4: 实现——Retry 段整体替换**

`GenerateMethod` 中 `hasRetry` 的包裹打开段（含 Task 1 的外层 try）整体替换为：

```csharp
            // ─── Retry 包裹 ───
            bool hasRetry = flags.HasFlag(InterceptFlags.Retry);
            if (hasRetry)
            {
                var ctName = method.HasCancellationToken ? method.Parameters[method.Parameters.Count - 1].Name : null;
                var typeFilter = info.RetryExceptionTypeNames.Count > 0
                    ? $" && ({string.Join(" || ", info.RetryExceptionTypeNames.Select(t => $"__ex is {t}"))})"
                    : "";
                sb.AppendLine();
                sb.AppendLine($"{b}// ─── 重试（指数退避）───");
                sb.AppendLine($"{b}try");
                sb.AppendLine($"{b}{{");
                sb.AppendLine($"{b}    for (int __attempt = 1; ; __attempt++)");
                sb.AppendLine($"{b}    {{");
                if (ctName != null)
                    sb.AppendLine($"{b}        {ctName}.ThrowIfCancellationRequested();");
                sb.AppendLine($"{b}        try");
                sb.AppendLine($"{b}        {{");
                b = $"{ind}                ";
            }
```

对应 retry catch 段（原 `catch (Exception __ex) when (...)` 起的关闭部分）替换为：

```csharp
            if (hasRetry)
            {
                sb.AppendLine($"{ind}            }}");
                sb.AppendLine($"{ind}            catch (Exception __ex) when (__attempt < {info.MaxRetryCount}{typeFilter})");
                sb.AppendLine($"{ind}            {{");
                if (flags.HasFlag(InterceptFlags.Log))
                    sb.AppendLine($"{ind}                _logger.LogWarning(__ex, \"{method.Name} 第{{Attempt}}次失败，准备重试\", __attempt);");
                if (effectiveHandlers.Count > 0)
                {
                    foreach (var handler in effectiveHandlers)
                    {
                        var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                        if (handler.IsMethodHandler)
                        {
                            sb.AppendLine($"{ind}                __mctx.Elapsed = __sw.Elapsed;");
                            sb.AppendLine($"{ind}                __mctx.AttemptNumber = __attempt;");
                            if (handler.IsAsyncHandler)
                                sb.AppendLine($"{ind}                await {fieldName}.OnExceptionAsync(__args, __ex, __mctx);");
                            else
                                sb.AppendLine($"{ind}                {fieldName}.OnException(__args, __ex, __mctx);");
                        }
                        else
                        {
                            sb.AppendLine($"{ind}                __ctx.Elapsed = __sw.Elapsed;");
                            sb.AppendLine($"{ind}                __ctx.AttemptNumber = __attempt;");
                            sb.AppendLine($"{ind}                {fieldName}.OnException(__ctx, __ex);");
                        }
                    }
                }
                GenerateHandledFallback(sb, $"{ind}                ", method, info);
                sb.AppendLine($"{ind}                {(method.IsAsync ? $"await Task.Delay(__attempt * {info.RetryBaseDelayMs}{(ctName != null ? $", {ctName}" : "")});" : $"Thread.Sleep(__attempt * {info.RetryBaseDelayMs});")}");
                sb.AppendLine($"{ind}            }}");
                sb.AppendLine($"{ind}        }}");
                sb.AppendLine($"{ind}    }}");
                sb.AppendLine($"{ind}    catch (Exception __ex)");
                sb.AppendLine($"{ind}    {{");
                GenerateExceptionHandling(sb, $"{ind}        ", method, flags, info);
                GenerateHandledFallback(sb, $"{ind}        ", method, info);
                sb.AppendLine($"{ind}        throw;");
                sb.AppendLine($"{ind}    }}");
            }
```

说明：若 Task 1 实现的外层 try 缩进与此处不完全一致，按本段缩进模式对齐（`{ind}` 级 4 空格递进），语义不变；`typeFilter` 变量在 `hasRetry` 的 `if` 块内声明，catch 段与打开段同处 `GenerateMethod` 作用域可直接引用。

- [ ] **Step 5: 实现——Throttle 固定窗口**

(a) 字段段（`GenerateInterceptedClass` 中 `classFlags.HasFlag(InterceptFlags.Throttle)` 分支）替换：

```csharp
            if (classFlags.HasFlag(InterceptFlags.Throttle))
            {
                sb.AppendLine($"{ind}    private static long _throttleWindowStartTicks;");
                sb.AppendLine($"{ind}    private static int _throttleWindowCount;");
                sb.AppendLine($"{ind}    private static readonly object _throttleLock = new object();");
            }
```

(b) Before 段（`GenerateMethod` 中 Throttle Before，含原 `try` 开括号）替换：

```csharp
            // ─── Throttle - Before（固定窗口限流：每秒最多 {info.MaxRequestsPerSecond} 次）───
            if (flags.HasFlag(InterceptFlags.Throttle))
            {
                sb.AppendLine($"{b}// ─── 限流（固定窗口）───");
                sb.AppendLine($"{b}while (true)");
                sb.AppendLine($"{b}{{");
                sb.AppendLine($"{b}    var __tickNow = DateTime.UtcNow.Ticks;");
                sb.AppendLine($"{b}    lock (_throttleLock)");
                sb.AppendLine($"{b}    {{");
                sb.AppendLine($"{b}        if (__tickNow - _throttleWindowStartTicks >= TimeSpan.TicksPerSecond)");
                sb.AppendLine($"{b}        {{");
                sb.AppendLine($"{b}            _throttleWindowStartTicks = __tickNow;");
                sb.AppendLine($"{b}            _throttleWindowCount = 0;");
                sb.AppendLine($"{b}        }}");
                sb.AppendLine($"{b}        if (_throttleWindowCount < {info.MaxRequestsPerSecond})");
                sb.AppendLine($"{b}        {{");
                sb.AppendLine($"{b}            _throttleWindowCount++;");
                sb.AppendLine($"{b}            break;");
                sb.AppendLine($"{b}        }}");
                sb.AppendLine($"{b}    }}");
                sb.AppendLine($"{b}    var __tickWaitMs = (int)((TimeSpan.TicksPerSecond - (DateTime.UtcNow.Ticks - _throttleWindowStartTicks)) / TimeSpan.TicksPerMillisecond);");
                sb.AppendLine($"{b}    {(method.IsAsync ? "await Task.Delay(Math.Max(1, __tickWaitMs));" : "Thread.Sleep(Math.Max(1, __tickWaitMs));")}");
                sb.AppendLine($"{b}}}");
                sb.AppendLine();
            }
```

(c) 删除 Throttle finally 段（`// ─── Throttle finally ───` 整段，含 `_throttle.Release()`）——固定窗口无资源释放。

- [ ] **Step 6: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Retry_PassesCancellationToken|FullyQualifiedName~Throttle_UsesFixedWindow|FullyQualifiedName~RetryOnExceptionTypes"`
Expected: FAIL 产出 received。审查：
1. `ct.ThrowIfCancellationRequested();` 与 `await Task.Delay(__attempt * 1000, ct);`
2. 无 `SemaphoreSlim`，有 `_throttleWindowStartTicks`/`_throttleWindowCount`/`_throttleLock` 与 while 窗口循环
3. `catch (Exception __ex) when (__attempt < 3 && (__ex is global::System.TimeoutException))`
4. 三个测试均无 CS 错误

- [ ] **Step 7: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.Retry_PassesCancellationTokenToDelay.received.txt InterceptGeneratorSnapshotTests.Retry_PassesCancellationTokenToDelay.verified.txt
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.Throttle_UsesFixedWindowRateLimit.received.txt InterceptGeneratorSnapshotTests.Throttle_UsesFixedWindowRateLimit.verified.txt
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.RetryOnExceptionTypes_FiltersRetryableExceptions.received.txt InterceptGeneratorSnapshotTests.RetryOnExceptionTypes_FiltersRetryableExceptions.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Retry_PassesCancellationToken|FullyQualifiedName~Throttle_UsesFixedWindow|FullyQualifiedName~RetryOnExceptionTypes"
```

Expected: PASS

- [ ] **Step 8: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): Retry 透传 CancellationToken + Throttle 固定窗口限流 + RetryOnExceptionTypes 异常过滤"
```

### Task 15: CircuitBreaker 半开状态机

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**：

```csharp
        [Fact]
        public async Task CircuitBreaker_GeneratesHalfOpenStateMachine()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IStockService { decimal GetPrice(string symbol); }

                    [AutoIntercept(InterceptType.CircuitBreaker, CircuitFailureThreshold = 3, CircuitBreakDurationSeconds = 30)]
                    public class StockService : IStockService
                    {
                        public decimal GetPrice(string symbol) => 100m;
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~CircuitBreaker_GeneratesHalfOpen"`
Expected: FAIL 产出 received（当前无半开状态：冷却结束直接放行全部请求）

- [ ] **Step 3: 实现——字段段**（`GenerateInterceptedClass` 中 CircuitBreaker 字段分支）替换：

```csharp
            if (classFlags.HasFlag(InterceptFlags.CircuitBreaker))
            {
                sb.AppendLine($"{ind}    private static int _consecutiveFailures;");
                sb.AppendLine($"{ind}    private static DateTime _circuitOpenUntil = DateTime.MinValue;");
                sb.AppendLine($"{ind}    private static int _circuitHalfOpenProbe;");
                sb.AppendLine($"{ind}    private static readonly object _circuitLock = new object();");
            }
```

- [ ] **Step 4: 实现——Before 段**（`GenerateMethod` 中熔断检查段）替换：

```csharp
            // ─── CircuitBreaker - Before（Closed → Open → HalfOpen 状态机）───
            if (flags.HasFlag(InterceptFlags.CircuitBreaker))
            {
                sb.AppendLine($"{b}// ─── 熔断检查 ───");
                sb.AppendLine($"{b}if (DateTime.UtcNow < _circuitOpenUntil)");
                sb.AppendLine($"{b}    throw new InvalidOperationException(\"[CircuitBreaker] 熔断器已打开，请稍后重试\");");
                sb.AppendLine($"{b}if (Volatile.Read(ref _consecutiveFailures) >= {info.CircuitFailureThreshold})");
                sb.AppendLine($"{b}{{");
                sb.AppendLine($"{b}    // 冷却期已结束但尚未恢复 → 半开：仅允许一个探测请求");
                sb.AppendLine($"{b}    if (Interlocked.Increment(ref _circuitHalfOpenProbe) > 1)");
                sb.AppendLine($"{b}    {{");
                sb.AppendLine($"{b}        Interlocked.Decrement(ref _circuitHalfOpenProbe);");
                sb.AppendLine($"{b}        throw new InvalidOperationException(\"[CircuitBreaker] 半开探测进行中，请稍后重试\");");
                sb.AppendLine($"{b}    }}");
                sb.AppendLine($"{b}}}");
                sb.AppendLine();
            }
```

- [ ] **Step 5: 实现——After 成功重置段**（`GenerateAfterIntercepts` 中 CircuitBreaker 成功重置）替换：

```csharp
            // CircuitBreaker - 成功重置（探测成功 → 回 Closed）
            if (flags.HasFlag(InterceptFlags.CircuitBreaker))
            {
                sb.AppendLine($"{b}if (_consecutiveFailures > 0) Interlocked.Exchange(ref _consecutiveFailures, 0);");
                sb.AppendLine($"{b}if (_circuitHalfOpenProbe > 0) Interlocked.Exchange(ref _circuitHalfOpenProbe, 0);");
            }
```

- [ ] **Step 6: 实现——Exception 段**（`GenerateExceptionHandling` 中 CircuitBreaker 失败计数）替换：

```csharp
            if (flags.HasFlag(InterceptFlags.CircuitBreaker))
            {
                sb.AppendLine($"{b}lock (_circuitLock)");
                sb.AppendLine($"{b}{{");
                sb.AppendLine($"{b}    if (++_consecutiveFailures >= {info.CircuitFailureThreshold})");
                sb.AppendLine($"{b}        _circuitOpenUntil = DateTime.UtcNow.AddSeconds({info.CircuitBreakDurationSeconds});");
                sb.AppendLine($"{b}}}");
                sb.AppendLine($"{b}if (_circuitHalfOpenProbe > 0) Interlocked.Exchange(ref _circuitHalfOpenProbe, 0);");
            }
```

说明：`Volatile.Read`/`Interlocked` 来自 `System.Threading`（生成文件已 using，`SemaphoreSlim` 移除后仍被 `Volatile`/`Interlocked` 使用）。

- [ ] **Step 7: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~CircuitBreaker_GeneratesHalfOpen"`
Expected: FAIL 产出 received。审查：
1. 字段含 `_circuitHalfOpenProbe`
2. Before 含 `Volatile.Read(ref _consecutiveFailures) >= 3` 与 `Interlocked.Increment(ref _circuitHalfOpenProbe) > 1` 探测互斥
3. After/Exception 均含 `_circuitHalfOpenProbe` 重置
4. 无 CS 错误

- [ ] **Step 8: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.CircuitBreaker_GeneratesHalfOpenStateMachine.received.txt InterceptGeneratorSnapshotTests.CircuitBreaker_GeneratesHalfOpenStateMachine.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~CircuitBreaker_GeneratesHalfOpen"
```

Expected: PASS

- [ ] **Step 9: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): CircuitBreaker 半开状态机（单探测 + 快速失败）"
```

### Task 16: Cache 语义（null 不缓存 + 命中日志）

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**：

```csharp
        [Fact]
        public async Task Cache_NullResultNotCached_HitLogs()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface ILookupService { object? Resolve(string key); }

                    [AutoIntercept(InterceptType.Cache | InterceptType.Log, CacheDurationSeconds = 60)]
                    public class LookupService : ILookupService
                    {
                        public object? Resolve(string key) => null;
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Cache_NullResult"`
Expected: FAIL 产出 received（当前 null 直接写入缓存、命中无日志）

- [ ] **Step 3: 实现——命中日志**（`GenerateMethod` 中 Cache Before 命中分支）替换：

```csharp
                sb.AppendLine($"{b}if (_cache.TryGetValue(__cacheKey, out object? __cachedObj) && __cachedObj is {patternType} __cached)");
                sb.AppendLine($"{b}{{");
                if (flags.HasFlag(InterceptFlags.Log))
                    sb.AppendLine($"{b}    _logger.LogInformation(\"{method.Name} 缓存命中\");");
                sb.AppendLine($"{b}    return __cached;");
                sb.AppendLine($"{b}}}");
```

- [ ] **Step 4: 实现——null 不缓存**（`GenerateAfterIntercepts` 中 Cache After 段）替换：

```csharp
            // Cache - After（null 结果不缓存，避免缓存穿透语义）
            if (flags.HasFlag(InterceptFlags.Cache) && resultVar != null)
            {
                var cacheInnerType = method.IsAsync ? ExtractAsyncInnerType(method.ReturnType) : method.ReturnType;
                if (IsReferenceType(cacheInnerType) || cacheInnerType.EndsWith("?"))
                {
                    sb.AppendLine($"{b}if ({resultVar} is not null)");
                    sb.AppendLine($"{b}    _cache.Set(__cacheKey, {resultVar}, TimeSpan.FromSeconds({info.CacheDurationSeconds}));");
                }
                else
                {
                    sb.AppendLine($"{b}_cache.Set(__cacheKey, {resultVar}, TimeSpan.FromSeconds({info.CacheDurationSeconds}));");
                }
            }
```

- [ ] **Step 5: 运行测试并审查快照**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Cache_NullResult"`
Expected: FAIL 产出 received。审查：
1. 命中分支含 `_logger.LogInformation("Resolve 缓存命中");`
2. Set 前有 `if (__result is not null)` 守卫（object? 引用类型）
3. 无 CS 错误

- [ ] **Step 6: 入库快照并复跑**

```powershell
Rename-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.Cache_NullResultNotCached_HitLogs.received.txt InterceptGeneratorSnapshotTests.Cache_NullResultNotCached_HitLogs.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~Cache_NullResult"
```

Expected: PASS

- [ ] **Step 7: 阶段 3 回归——全量测试**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj; dotnet build src/AutoCode.sln`
Expected: 全部 PASS / 0 errors。`CacheAndRetry_GeneratesTypedArgsRecord` 既有快照若因 Cache After 变化漂移，审查 diff 语义等价（`bool` 值类型无 null 守卫，Set 行不变）后更新 verified。

- [ ] **Step 8: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "fix(intercept): Cache null 不缓存 + 命中日志补观测盲区"
```

---

## 阶段 4：顺序可配 + 架构加固

### Task 17: emit 函数拆分（纯搬迁重构，行为零变化）

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`

本任务不做任何行为变化——唯一验收标准：全部既有快照零 diff。为 Task 18 的顺序表驱动铺路。

- [ ] **Step 1: 基线确认**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~InterceptGeneratorSnapshotTests"`
Expected: 全部 PASS 且无 `.received.txt` 残留（git status 干净）

- [ ] **Step 2: 抽取 After emit 函数**（放在 `GenerateAfterIntercepts` 之前，函数体 = 各段现有代码原样移入）：

```csharp
        private static void EmitTransactionAfter(StringBuilder sb, string b)
            => sb.AppendLine($"{b}__tx.Complete();");

        private static void EmitLogAfter(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info, string? resultVar)
        {
            if (info.LogResult && resultVar != null)
                sb.AppendLine($"{b}_logger.LogInformation(\"{method.Name} 完成, 结果: {{__result}}, 耗时 {{Elapsed}}ms\", {resultVar}, __sw.ElapsedMilliseconds);");
            else
                sb.AppendLine($"{b}_logger.LogInformation(\"{method.Name} 完成, 耗时 {{Elapsed}}ms\", __sw.ElapsedMilliseconds);");
        }

        private static void EmitMetricsAfter(StringBuilder sb, string b, InterceptMethodInfo method)
        {
            sb.AppendLine($"{b}_duration.Record(__sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>(\"method\", \"{method.Name}\"));");
            sb.AppendLine($"{b}_successCount.Add(1, new KeyValuePair<string, object?>(\"method\", \"{method.Name}\"));");
        }

        private static void EmitTracingAfter(StringBuilder sb, string b)
            => sb.AppendLine($"{b}__activity?.SetTag(\"elapsed_ms\", __sw.ElapsedMilliseconds);");

        private static void EmitCircuitBreakerAfter(StringBuilder sb, string b)
        {
            sb.AppendLine($"{b}if (_consecutiveFailures > 0) Interlocked.Exchange(ref _consecutiveFailures, 0);");
            sb.AppendLine($"{b}if (_circuitHalfOpenProbe > 0) Interlocked.Exchange(ref _circuitHalfOpenProbe, 0);");
        }

        private static void EmitCacheAfter(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info, string? resultVar)
        {
            var cacheInnerType = method.IsAsync ? ExtractAsyncInnerType(method.ReturnType) : method.ReturnType;
            if (IsReferenceType(cacheInnerType) || cacheInnerType.EndsWith("?"))
            {
                sb.AppendLine($"{b}if ({resultVar} is not null)");
                sb.AppendLine($"{b}    _cache.Set(__cacheKey, {resultVar}, TimeSpan.FromSeconds({info.CacheDurationSeconds}));");
            }
            else
            {
                sb.AppendLine($"{b}_cache.Set(__cacheKey, {resultVar}, TimeSpan.FromSeconds({info.CacheDurationSeconds}));");
            }
        }

        private static void EmitCustomAfter(StringBuilder sb, string b, List<CustomHandlerInfo> handlers, string? resultVar)
        {
            foreach (var handler in handlers)
            {
                var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                if (handler.IsMethodHandler)
                {
                    sb.AppendLine($"{b}__mctx.Elapsed = __sw.Elapsed;");
                    if (handler.IsAsyncHandler)
                        sb.AppendLine($"{b}await {fieldName}.OnAfterAsync(__args, {(resultVar ?? "default!")}, __mctx);");
                    else
                        sb.AppendLine($"{b}{fieldName}.OnAfter(__args, {(resultVar ?? "default!")}, __mctx);");
                }
                else
                {
                    sb.AppendLine($"{b}__ctx.Elapsed = __sw.Elapsed;");
                    sb.AppendLine($"{b}{fieldName}.OnAfter(__ctx, {(resultVar ?? "null")});");
                }
            }
        }

        private static void EmitAuditAfter(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info, string? resultVar)
        {
            sb.AppendLine($"{b}var __auditEntry = new AuditEntry");
            sb.AppendLine($"{b}{{");
            sb.AppendLine($"{b}    ClassName = \"{info.ClassName}\",");
            sb.AppendLine($"{b}    MethodName = \"{method.Name}\",");
            GenerateAuditParameters(sb, $"{b}    ", method, info);
            sb.AppendLine($"{b}    Result = {resultVar ?? "null"},");
            sb.AppendLine($"{b}    Elapsed = __sw.Elapsed,");
            sb.AppendLine($"{b}    Succeeded = true");
            sb.AppendLine($"{b}}});");
            if (method.IsAsync)
                sb.AppendLine($"{b}await _auditStore.WriteAsync(__auditEntry);");
            else
                sb.AppendLine($"{b}_auditStore.Write(__auditEntry);");
        }

        private static void EmitProfilingAfter(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info)
        {
            var ind2 = b;
            if (info.ProfilingThresholdMs > 0)
            {
                sb.AppendLine($"{b}if (__sw.ElapsedMilliseconds >= {info.ProfilingThresholdMs})");
                sb.AppendLine($"{b}{{");
                ind2 = b + "    ";
            }
            sb.AppendLine($"{ind2}var __gcDelta = GC.GetTotalMemory(false) - __gcBefore;");
            if (info.ProfilingIncludeProcess)
            {
                sb.AppendLine($"{ind2}var __proc = System.Diagnostics.Process.GetCurrentProcess();");
                sb.AppendLine($"{ind2}_logger.LogInformation(\"{method.Name} 性能: 耗时 {{Elapsed}}ms, GC增量 {{GcDelta}}B, 工作集 {{Ws}}B, 线程数 {{Threads}}\", __sw.ElapsedMilliseconds, __gcDelta, __proc.WorkingSet64, __proc.Threads.Count);");
            }
            else
            {
                sb.AppendLine($"{ind2}_logger.LogInformation(\"{method.Name} 性能: 耗时 {{Elapsed}}ms, GC增量 {{GcDelta}}B\", __sw.ElapsedMilliseconds, __gcDelta);");
            }
            if (info.ProfilingThresholdMs > 0)
                sb.AppendLine($"{b}}}");
        }
```

- [ ] **Step 3: 抽取 Exception emit 函数**（`GenerateExceptionHandling` 之前）：

```csharp
        private static void EmitLogException(StringBuilder sb, string b, InterceptMethodInfo method)
            => sb.AppendLine($"{b}_logger.LogError(__ex, \"{method.Name} 异常, 耗时 {{Elapsed}}ms\", __sw.ElapsedMilliseconds);");

        private static void EmitMetricsException(StringBuilder sb, string b, InterceptMethodInfo method)
            => sb.AppendLine($"{b}_errorCount.Add(1, new KeyValuePair<string, object?>(\"method\", \"{method.Name}\"));");

        private static void EmitTracingException(StringBuilder sb, string b)
        {
            sb.AppendLine($"{b}__activity?.SetStatus(ActivityStatusCode.Error, __ex.Message);");
            sb.AppendLine($"{b}__activity?.RecordException(__ex);");
        }

        private static void EmitCircuitBreakerException(StringBuilder sb, string b, InterceptInfo info)
        {
            sb.AppendLine($"{b}lock (_circuitLock)");
            sb.AppendLine($"{b}{{");
            sb.AppendLine($"{b}    if (++_consecutiveFailures >= {info.CircuitFailureThreshold})");
            sb.AppendLine($"{b}        _circuitOpenUntil = DateTime.UtcNow.AddSeconds({info.CircuitBreakDurationSeconds});");
            sb.AppendLine($"{b}}}");
            sb.AppendLine($"{b}if (_circuitHalfOpenProbe > 0) Interlocked.Exchange(ref _circuitHalfOpenProbe, 0);");
        }

        private static void EmitCustomException(StringBuilder sb, string b, List<CustomHandlerInfo> effectiveHandlers)
        {
            foreach (var handler in effectiveHandlers)
            {
                var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                if (handler.IsMethodHandler)
                {
                    sb.AppendLine($"{b}__mctx.Elapsed = __sw.Elapsed;");
                    if (handler.IsAsyncHandler)
                        sb.AppendLine($"{b}await {fieldName}.OnExceptionAsync(__args, __ex, __mctx);");
                    else
                        sb.AppendLine($"{b}{fieldName}.OnException(__args, __ex, __mctx);");
                }
                else
                {
                    sb.AppendLine($"{b}__ctx.Elapsed = __sw.Elapsed;");
                    sb.AppendLine($"{b}{fieldName}.OnException(__ctx, __ex);");
                }
            }
        }

        private static void EmitAuditException(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info)
        {
            sb.AppendLine($"{b}var __auditEntry = new AuditEntry");
            sb.AppendLine($"{b}{{");
            sb.AppendLine($"{b}    ClassName = \"{info.ClassName}\",");
            sb.AppendLine($"{b}    MethodName = \"{method.Name}\",");
            GenerateAuditParameters(sb, $"{b}    ", method, info);
            sb.AppendLine($"{b}    Elapsed = __sw.Elapsed,");
            sb.AppendLine($"{b}    Succeeded = false,");
            sb.AppendLine($"{b}    Error = __ex.Message");
            sb.AppendLine($"{b}}});");
            if (method.IsAsync)
                sb.AppendLine($"{b}await _auditStore.WriteAsync(__auditEntry);");
            else
                sb.AppendLine($"{b}_auditStore.Write(__auditEntry);");
        }
```

- [ ] **Step 4: 抽取 Before emit 函数**（放在 `GenerateMethod` 之前）：

```csharp
        private static void EmitValidateBefore(StringBuilder sb, string b, InterceptMethodInfo method)
        {
            sb.AppendLine($"{b}// ─── 参数校验 ───");
            foreach (var p in method.Parameters)
            {
                if (p.Type == "string" || p.Type == "global::System.String")
                    sb.AppendLine($"{b}if (string.IsNullOrWhiteSpace({p.Name})) throw new ArgumentException(\"参数 {p.Name} 不能为空\", nameof({p.Name}));");
                else if (IsReferenceType(p.Type) && !p.IsNullable)
                    sb.AppendLine($"{b}if ({p.Name} is null) throw new ArgumentNullException(nameof({p.Name}));");
            }
            sb.AppendLine();
        }

        private static void EmitAuthorizeBefore(StringBuilder sb, string b, InterceptInfo info)
        {
            sb.AppendLine($"{b}// ─── 权限校验 ───");
            sb.AppendLine($"{b}var __principal = _principalProvider.CurrentPrincipal;");
            sb.AppendLine($"{b}if (__principal == null || __principal.Identity == null || !__principal.Identity.IsAuthenticated)");
            sb.AppendLine($"{b}    throw new UnauthorizedAccessException(\"[Authorize] 当前用户未认证\");");
            if (!string.IsNullOrEmpty(info.AuthorizeRoles))
            {
                var roles = string.Join(", ", info.AuthorizeRoles.Split(',').Select(r => $"\"{r.Trim()}\""));
                sb.AppendLine($"{b}var __requiredRoles = new[] {{ {roles} }};");
                sb.AppendLine($"{b}if (!__requiredRoles.Any(r => __principal.IsInRole(r)))");
                sb.AppendLine($"{b}    throw new UnauthorizedAccessException($\"[Authorize] 缺少角色: {{string.Join(\", \", __requiredRoles)}}\");");
            }
            sb.AppendLine();
        }

        private static void EmitCustomBefore(StringBuilder sb, string b, InterceptMethodInfo method, List<CustomHandlerInfo> effectiveHandlers,
            bool hasMethodHandlers, bool hasLegacyHandlers, string returnType)
        {
            sb.AppendLine($"{b}// ─── 自定义拦截器 OnBefore ───");
            foreach (var handler in effectiveHandlers)
            {
                var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                if (handler.IsMethodHandler)
                {
                    if (handler.IsAsyncHandler)
                        sb.AppendLine($"{b}await {fieldName}.OnBeforeAsync(__args, __mctx);");
                    else
                        sb.AppendLine($"{b}{fieldName}.OnBefore(__args, __mctx);");
                }
                else
                {
                    sb.AppendLine($"{b}{fieldName}.OnBefore(__ctx);");
                }
            }
            if (hasMethodHandlers)
            {
                sb.AppendLine($"{b}if (__mctx.ShortCircuit)");
                if (!method.IsVoid && !method.IsTaskNoResult)
                {
                    if (method.IsAsync)
                        sb.AppendLine($"{b}    return ({ExtractAsyncInnerType(returnType)})__mctx.Result!;");
                    else
                        sb.AppendLine($"{b}    return ({returnType})__mctx.Result!;");
                }
                else
                {
                    sb.AppendLine($"{b}    return;");
                }
            }
            if (hasLegacyHandlers)
            {
                sb.AppendLine($"{b}if (__ctx.ShortCircuit)");
                if (!method.IsVoid && !method.IsTaskNoResult)
                {
                    if (method.IsAsync)
                        sb.AppendLine($"{b}    return ({ExtractAsyncInnerType(returnType)})__ctx.Result!;");
                    else
                        sb.AppendLine($"{b}    return ({returnType})__ctx.Result!;");
                }
                else
                {
                    sb.AppendLine($"{b}    return;");
                }
            }
            sb.AppendLine();
        }

        private static void EmitThrottleBefore(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info)
        {
            sb.AppendLine($"{b}// ─── 限流（固定窗口）───");
            sb.AppendLine($"{b}while (true)");
            sb.AppendLine($"{b}{{");
            sb.AppendLine($"{b}    var __tickNow = DateTime.UtcNow.Ticks;");
            sb.AppendLine($"{b}    lock (_throttleLock)");
            sb.AppendLine($"{b}    {{");
            sb.AppendLine($"{b}        if (__tickNow - _throttleWindowStartTicks >= TimeSpan.TicksPerSecond)");
            sb.AppendLine($"{b}        {{");
            sb.AppendLine($"{b}            _throttleWindowStartTicks = __tickNow;");
            sb.AppendLine($"{b}            _throttleWindowCount = 0;");
            sb.AppendLine($"{b}        }}");
            sb.AppendLine($"{b}        if (_throttleWindowCount < {info.MaxRequestsPerSecond})");
            sb.AppendLine($"{b}        {{");
            sb.AppendLine($"{b}            _throttleWindowCount++;");
            sb.AppendLine($"{b}            break;");
            sb.AppendLine($"{b}        }}");
            sb.AppendLine($"{b}    }}");
            sb.AppendLine($"{b}    var __tickWaitMs = (int)((TimeSpan.TicksPerSecond - (DateTime.UtcNow.Ticks - _throttleWindowStartTicks)) / TimeSpan.TicksPerMillisecond);");
            sb.AppendLine($"{b}    {(method.IsAsync ? "await Task.Delay(Math.Max(1, __tickWaitMs));" : "Thread.Sleep(Math.Max(1, __tickWaitMs));")}");
            sb.AppendLine($"{b}}}");
            sb.AppendLine();
        }

        private static void EmitCacheBefore(StringBuilder sb, string b, InterceptMethodInfo method, InterceptFlags flags, InterceptInfo info)
        {
            var cacheKey = info.CacheKeyPrefix ?? $"{info.ClassName}.{method.Name}";
            var innerType = method.IsAsync ? ExtractAsyncInnerType(method.ReturnType) : method.ReturnType;
            var patternType = innerType.TrimEnd('?');
            sb.AppendLine($"{b}// ─── 缓存（Before: 命中短路）───");
            sb.AppendLine($"{b}var __cacheKey = $\"{cacheKey}:{string.Join(":", method.Parameters.Select(p => $"{{{p.Name}}}"))}\";");
            sb.AppendLine($"{b}if (_cache.TryGetValue(__cacheKey, out object? __cachedObj) && __cachedObj is {patternType} __cached)");
            sb.AppendLine($"{b}{{");
            if (flags.HasFlag(InterceptFlags.Log))
                sb.AppendLine($"{b}    _logger.LogInformation(\"{method.Name} 缓存命中\");");
            sb.AppendLine($"{b}    return __cached;");
            sb.AppendLine($"{b}}}");
            sb.AppendLine();
        }

        private static void EmitCircuitBreakerBefore(StringBuilder sb, string b, InterceptInfo info)
        {
            sb.AppendLine($"{b}// ─── 熔断检查 ───");
            sb.AppendLine($"{b}if (DateTime.UtcNow < _circuitOpenUntil)");
            sb.AppendLine($"{b}    throw new InvalidOperationException(\"[CircuitBreaker] 熔断器已打开，请稍后重试\");");
            sb.AppendLine($"{b}if (Volatile.Read(ref _consecutiveFailures) >= {info.CircuitFailureThreshold})");
            sb.AppendLine($"{b}{{");
            sb.AppendLine($"{b}    if (Interlocked.Increment(ref _circuitHalfOpenProbe) > 1)");
            sb.AppendLine($"{b}    {{");
            sb.AppendLine($"{b}        Interlocked.Decrement(ref _circuitHalfOpenProbe);");
            sb.AppendLine($"{b}        throw new InvalidOperationException(\"[CircuitBreaker] 半开探测进行中，请稍后重试\");");
            sb.AppendLine($"{b}    }}");
            sb.AppendLine($"{b}}}");
            sb.AppendLine();
        }

        private static void EmitTracingBefore(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info)
        {
            sb.AppendLine($"{b}// ─── 链路追踪 ───");
            sb.AppendLine($"{b}using var __activity = _activitySource.StartActivity(\"{info.ClassName}.{method.Name}\");");
            foreach (var p in method.Parameters)
                sb.AppendLine($"{b}__activity?.SetTag(\"{p.Name}\", {p.Name}?.ToString());");
            sb.AppendLine();
        }

        private static void EmitLogBefore(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info)
        {
            var paramLog = info.LogParameters && method.Parameters.Count > 0
                ? ", " + string.Join(", ", method.Parameters.Select(p => $"{p.Name}={{{p.Name}}}"))
                : "";
            var paramArgs = info.LogParameters && method.Parameters.Count > 0
                ? ", " + string.Join(", ", method.Parameters.Select(p => p.Name))
                : "";
            sb.AppendLine($"{b}_logger.LogInformation(\"{method.Name} 开始{paramLog}\"{paramArgs});");
        }

        private static void EmitTransactionBefore(StringBuilder sb, string b, InterceptMethodInfo method, InterceptInfo info)
        {
            sb.AppendLine($"{b}// ─── 事务 ───");
            var txOptions = $"new TransactionOptions {{ IsolationLevel = System.Transactions.IsolationLevel.{info.TransactionIsolationLevel}, Timeout = TimeSpan.FromSeconds({info.TransactionTimeoutSeconds}) }}";
            if (method.IsAsync)
                sb.AppendLine($"{b}using var __tx = new TransactionScope(TransactionScopeOption.Required, {txOptions}, TransactionScopeAsyncFlowOption.Enabled);");
            else
                sb.AppendLine($"{b}using var __tx = new TransactionScope(TransactionScopeOption.Required, {txOptions});");
            sb.AppendLine();
        }
```

- [ ] **Step 5: 改写三个调度函数**（段体替换为函数调用，顺序与现有发射顺序一致）：

`GenerateMethod` 中线性段替换（Validate/Authorize/Custom/Throttle/Cache/CircuitBreaker/Tracing/Log/Transaction 各自替换为一行调用，包裹段 Retry/needTryCatch 保持不动）：

```csharp
            if (flags.HasFlag(InterceptFlags.Validate)) EmitValidateBefore(sb, b, method);
            if (flags.HasFlag(InterceptFlags.Authorize)) EmitAuthorizeBefore(sb, b, info);
            if (hasCustom) EmitCustomBefore(sb, b, method, effectiveHandlers, hasMethodHandlers, hasLegacyHandlers, returnType);
            if (flags.HasFlag(InterceptFlags.Throttle)) EmitThrottleBefore(sb, b, method, info);
            if (flags.HasFlag(InterceptFlags.Cache) && !method.IsVoid && !method.IsTaskNoResult) EmitCacheBefore(sb, b, method, flags, info);
            if (flags.HasFlag(InterceptFlags.CircuitBreaker)) EmitCircuitBreakerBefore(sb, b, info);
            if (flags.HasFlag(InterceptFlags.Tracing)) EmitTracingBefore(sb, b, method, info);
            if (flags.HasFlag(InterceptFlags.Log)) EmitLogBefore(sb, b, method, info);
            if (flags.HasFlag(InterceptFlags.Transaction)) EmitTransactionBefore(sb, b, method, info);
```

注意：Transaction 调用保持 Task 7 的插入位置（Retry 包裹之后、`needTryCatch` 判定之前）。Cache Before 的条件含 `!method.IsVoid && !method.IsTaskNoResult`（原段同条件）。

`GenerateAfterIntercepts` 整体替换为：

```csharp
        private static void GenerateAfterIntercepts(StringBuilder sb, string b, InterceptMethodInfo method, InterceptFlags flags, InterceptInfo info, List<CustomHandlerInfo> handlers, string? resultVar)
        {
            if (flags.HasFlag(InterceptFlags.Transaction)) EmitTransactionAfter(sb, b);
            if (flags.HasFlag(InterceptFlags.Log)) EmitLogAfter(sb, b, method, info, resultVar);
            if (flags.HasFlag(InterceptFlags.Metrics)) EmitMetricsAfter(sb, b, method);
            if (flags.HasFlag(InterceptFlags.Tracing)) EmitTracingAfter(sb, b);
            if (flags.HasFlag(InterceptFlags.CircuitBreaker)) EmitCircuitBreakerAfter(sb, b);
            if (flags.HasFlag(InterceptFlags.Cache) && resultVar != null) EmitCacheAfter(sb, b, method, info, resultVar);
            if (handlers.Count > 0) EmitCustomAfter(sb, b, handlers, resultVar);
            if (flags.HasFlag(InterceptFlags.Audit)) EmitAuditAfter(sb, b, method, info, resultVar);
            if (flags.HasFlag(InterceptFlags.Profiling)) EmitProfilingAfter(sb, b, method, info);
        }
```

`GenerateExceptionHandling` 整体替换为：

```csharp
        private static void GenerateExceptionHandling(StringBuilder sb, string b, InterceptMethodInfo method, InterceptFlags flags, InterceptInfo info)
        {
            var effectiveHandlers = method.CustomHandlers ?? info.CustomHandlers;

            if (flags.HasFlag(InterceptFlags.Log)) EmitLogException(sb, b, method);
            if (flags.HasFlag(InterceptFlags.Metrics)) EmitMetricsException(sb, b, method);
            if (flags.HasFlag(InterceptFlags.Tracing)) EmitTracingException(sb, b);
            if (flags.HasFlag(InterceptFlags.CircuitBreaker)) EmitCircuitBreakerException(sb, b, info);
            if (effectiveHandlers.Count > 0) EmitCustomException(sb, b, effectiveHandlers);
            if (flags.HasFlag(InterceptFlags.Audit)) EmitAuditException(sb, b, method, info);
        }
```

- [ ] **Step 6: 回归验证零 diff**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~InterceptGeneratorSnapshotTests"; dotnet build src/AutoCode.sln`
Expected: 全部 PASS、0 errors、`git status` 无 `.received.txt`（任何顺序/内容偏差都会打破快照，立即定位修复）

- [ ] **Step 7: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs
git commit -m "refactor(intercept): 拆分 Before/After/Exception emit 函数（纯搬迁，行为不变）"
```

### Task 18: PipelineOrder 顺序表驱动（AC9007）

**Files:**
- Modify: `src/AutoCode.Model/AutoInterceptAttribute.cs`
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`
- Update: `src/AutoCode.Tests.V2/Snapshots/*.verified.txt`（After/Exception 段顺序变化，全量重生成）

- [ ] **Step 1: 写失败测试**（两个 Fact）：

```csharp
        [Fact]
        public async Task PipelineOrder_CustomOrder_TakesEffect()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IReportService { string Generate(int id); }

                    [AutoIntercept(InterceptType.Log | InterceptType.Cache, PipelineOrder = "Cache,Log")]
                    public class ReportService : IReportService
                    {
                        public string Generate(int id) => "report";
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            await Verifier.Verify(ToSnapshotText(run));
            AssertCompilesCleanly(run);
        }

        [Fact]
        public async Task PipelineOrder_UnknownStage_ReportsAC9007()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IReportService { string Generate(int id); }

                    [AutoIntercept(InterceptType.Log, PipelineOrder = "Log,NotAThing")]
                    public class ReportService : IReportService
                    {
                        public string Generate(int id) => "report";
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            Assert.Contains(run.Diagnostics, d => d.Id == "AC9007");
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~PipelineOrder"`
Expected: FAIL（PipelineOrder 属性不存在 → CS0246，测试编译失败）

- [ ] **Step 3: Attribute 属性**（`AutoInterceptAttribute` 中 `RetryOnExceptionTypes` 之后追加）：

```csharp
        /// <summary>拦截管线顺序（逗号分隔拦截器名，如 "Cache,Log"）；未列出的按默认序排后</summary>
        public string? PipelineOrder { get; set; }
```

- [ ] **Step 4: 模型、解析与诊断**

`InterceptInfo` 追加 `public string? PipelineOrder { get; set; }`。named 参数 switch 追加：

```csharp
                        case "PipelineOrder": info.PipelineOrder = named.Value.Value as string; break;
```

新诊断描述符（AC9007）：

```csharp
        private static readonly DiagnosticDescriptor UnknownPipelineStage = new(
            "AC9007", "PipelineOrder 包含未知拦截器名",
            "PipelineOrder 中 '{0}' 不是已知拦截器名（有效: Validate,Authorize,Custom,Throttle,Cache,CircuitBreaker,Tracing,Log,Retry,Transaction,Audit,Profiling,Metrics），已忽略",
            "AutoCode.Intercept", DiagnosticSeverity.Warning, true);
```

`ExtractInterceptInfo` 中（`return info;` 之前）追加校验：

```csharp
            if (!string.IsNullOrEmpty(info.PipelineOrder))
            {
                foreach (var name in info.PipelineOrder.Split(','))
                {
                    var trimmed = name.Trim();
                    if (trimmed.Length > 0 && !s_knownStages.Contains(trimmed))
                        diagnostics.Add(Diagnostic.Create(UnknownPipelineStage, classDecl.Identifier.GetLocation(), trimmed));
                }
            }
```

- [ ] **Step 5: 顺序表实现**（`ExtractAsyncInnerType` 附近追加）：

```csharp
        /// <summary>已知拦截阶段（PipelineOrder 校验与默认序）。</summary>
        private static readonly string[] s_knownStages =
        {
            "Validate", "Authorize", "Custom", "Throttle", "Cache", "CircuitBreaker",
            "Tracing", "Log", "Retry", "Transaction", "Audit", "Profiling", "Metrics"
        };

        /// <summary>
        /// 计算发射顺序：PipelineOrder 指定的阶段在前，未列出的按默认序排后。
        /// 语义边界：Retry 为包裹性拦截器（for/catch 结构化代码），包裹位置固定在 GenerateMethod
        /// 的线性段之后、needTryCatch 之前；顺序表仅驱动线性段与 After/Exception 段。
        /// Transaction 的 using var 作用域到方法尾，随线性段顺序移动（默认序下位于 for 外，
        /// 即整个重试循环共享一个事务）。
        /// </summary>
        private static List<string> GetPipelineStages(InterceptInfo info)
        {
            var result = new List<string>();
            if (!string.IsNullOrEmpty(info.PipelineOrder))
            {
                foreach (var name in info.PipelineOrder.Split(','))
                {
                    var trimmed = name.Trim();
                    if (trimmed.Length > 0 && s_knownStages.Contains(trimmed) && !result.Contains(trimmed))
                        result.Add(trimmed);
                }
            }
            foreach (var name in s_knownStages)
                if (!result.Contains(name))
                    result.Add(name);
            return result;
        }
```

- [ ] **Step 6: 三个调度点改为顺序表驱动**

`GenerateMethod` 中 Task 17 Step 5 的线性调用块替换为：

```csharp
            var stages = GetPipelineStages(info);
            foreach (var stage in stages)
            {
                switch (stage)
                {
                    case "Validate": EmitValidateBefore(sb, b, method); break;
                    case "Authorize": EmitAuthorizeBefore(sb, b, info); break;
                    case "Custom": if (hasCustom) EmitCustomBefore(sb, b, method, effectiveHandlers, hasMethodHandlers, hasLegacyHandlers, returnType); break;
                    case "Throttle": EmitThrottleBefore(sb, b, method, info); break;
                    case "Cache": if (!method.IsVoid && !method.IsTaskNoResult) EmitCacheBefore(sb, b, method, flags, info); break;
                    case "CircuitBreaker": EmitCircuitBreakerBefore(sb, b, info); break;
                    case "Tracing": EmitTracingBefore(sb, b, method, info); break;
                    case "Log": EmitLogBefore(sb, b, method, info); break;
                    case "Transaction": EmitTransactionBefore(sb, b, method, info); break;
                    // Retry：包裹段，保持 GenerateMethod 固定位置，不在此发射
                }
            }
```

（Transaction 的固定调用行删除——已入顺序表；Retry 包裹段保持原位不动。）

`GenerateAfterIntercepts` 主体替换为：

```csharp
            var stages = GetPipelineStages(info);
            foreach (var stage in stages)
            {
                switch (stage)
                {
                    case "Transaction": EmitTransactionAfter(sb, b); break;
                    case "Log": EmitLogAfter(sb, b, method, info, resultVar); break;
                    case "Metrics": EmitMetricsAfter(sb, b, method); break;
                    case "Tracing": EmitTracingAfter(sb, b); break;
                    case "CircuitBreaker": EmitCircuitBreakerAfter(sb, b); break;
                    case "Cache": if (resultVar != null) EmitCacheAfter(sb, b, method, info, resultVar); break;
                    case "Custom": if (handlers.Count > 0) EmitCustomAfter(sb, b, handlers, resultVar); break;
                    case "Audit": EmitAuditAfter(sb, b, method, info, resultVar); break;
                    case "Profiling": EmitProfilingAfter(sb, b, method, info); break;
                }
            }
```

`GenerateExceptionHandling` 主体替换为：

```csharp
            var effectiveHandlers = method.CustomHandlers ?? info.CustomHandlers;
            var stages = GetPipelineStages(info);
            foreach (var stage in stages)
            {
                switch (stage)
                {
                    case "Log": EmitLogException(sb, b, method); break;
                    case "Metrics": EmitMetricsException(sb, b, method); break;
                    case "Tracing": EmitTracingException(sb, b); break;
                    case "CircuitBreaker": EmitCircuitBreakerException(sb, b, info); break;
                    case "Custom": if (effectiveHandlers.Count > 0) EmitCustomException(sb, b, effectiveHandlers); break;
                    case "Audit": EmitAuditException(sb, b, method, info); break;
                }
            }
```

- [ ] **Step 7: 运行测试并全量重生成快照**

```powershell
Remove-Item src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.*.verified.txt
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~InterceptGeneratorSnapshotTests"
```

Expected: 全部 FAIL 产出 received（After/Exception 段改为默认表顺序：After = Custom→Cache→CircuitBreaker→Tracing→Log→Transaction→Audit→Profiling→Metrics；Exception = Custom→CircuitBreaker→Tracing→Log→Audit→Metrics）

- [ ] **Step 8: 审查后批量入库**

审查要点：
1. `PipelineOrder_CustomOrder` 的 received：Cache Before（`__cacheKey`/`TryGetValue`）在 Log Before（`开始`）之前；AC9007 测试 PASS
2. 其余快照的 After/Exception 顺序变化与默认表一致，无其他语义变化

```powershell
Get-ChildItem src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.*.received.txt | ForEach-Object { Rename-Item $_.FullName ($_.Name -replace '\.received\.txt$', '.verified.txt') }
dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~InterceptGeneratorSnapshotTests"
```

Expected: 全部 PASS

- [ ] **Step 9: Commit**

```powershell
git add src/AutoCode.Model/AutoInterceptAttribute.cs src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): PipelineOrder 顺序表驱动 + AC9007 未知阶段警告"
```

### Task 19: 管道模型 record 化 + 增量缓存哨兵测试

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Create: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorCachingTests.cs`

- [ ] **Step 1: 替换四个模型类为 record**（类定义区整体替换）：

```csharp
    internal sealed record CustomHandlerInfo(string TypeName, string ShortName, int Order, bool IsMethodHandler, bool IsAsyncHandler);

    internal sealed record ParamInfo(string Name, string Type, bool IsNullable, bool IsSensitive, string RefKind);

    internal sealed record InterceptMethodInfo(
        string Name, string ReturnType, bool IsAsync, bool IsVoid, bool IsTaskNoResult,
        InterceptFlags Flags, bool IsPassthrough, string TypeParameters, string TypeConstraints,
        bool HasCancellationToken, AutoCode.Map.Helpers.ImmutableEquatableArray<ParamInfo> Parameters,
        AutoCode.Map.Helpers.ImmutableEquatableArray<CustomHandlerInfo>? CustomHandlers);

    internal sealed record InterceptInfo(
        string Namespace, string ClassName, string InterfaceName, string InterfaceShortName,
        InterceptFlags Interceptors, bool IsMethodLevelMode,
        AutoCode.Map.Helpers.ImmutableEquatableArray<InterceptMethodInfo> Methods,
        AutoCode.Map.Helpers.ImmutableEquatableArray<CustomHandlerInfo> CustomHandlers,
        System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics,
        bool LogParameters, bool LogResult, int CacheDurationSeconds, string? CacheKeyPrefix,
        int MaxRetryCount, int RetryBaseDelayMs, int CircuitFailureThreshold, int CircuitBreakDurationSeconds,
        int MaxRequestsPerSecond, string? ExcludeMethods, string? AuthorizeRoles, string? AuthorizePolicy,
        int TransactionTimeoutSeconds, System.Transactions.IsolationLevel TransactionIsolationLevel,
        bool AuditIncludeParameters, int ProfilingThresholdMs, bool ProfilingIncludeProcess,
        List<string> RetryExceptionTypeNames, string? PipelineOrder);
```

权衡说明：`Diagnostics` 用 `ImmutableArray<Diagnostic>`（元素引用相等，缓存哨兵场景 transform 不重跑、不参与比较）；`RetryExceptionTypeNames` 保持 `List<string>`（只读消费，不参与结构判断的关键路径）。

- [ ] **Step 2: 修改 ExtractInterceptInfo 为局部收集模式**

(a) 删除 `var info = new InterceptInfo { ... }`（原行 169-178），替换为局部变量声明：

```csharp
            var ns = classSymbol.ContainingNamespace?.ToDisplayString() ?? "";
            var logParameters = true;
            var logResult = false;
            var cacheDurationSeconds = 300;
            string? cacheKeyPrefix = null;
            var maxRetryCount = 3;
            var retryBaseDelayMs = 100;
            var circuitFailureThreshold = 5;
            var circuitBreakDurationSeconds = 30;
            var maxRequestsPerSecond = 100;
            string? excludeMethods = null;
            string? authorizeRoles = null;
            string? authorizePolicy = null;
            var transactionTimeoutSeconds = 60;
            var transactionIsolationLevel = System.Transactions.IsolationLevel.ReadCommitted;
            var auditIncludeParameters = true;
            var profilingThresholdMs = 0;
            var profilingIncludeProcess = false;
            var retryExceptionTypeNames = new List<string>();
            string? pipelineOrder = null;
            var methods = new List<InterceptMethodInfo>();
            var handlers = new List<CustomHandlerInfo>();
```

(b) `info.Interceptors = interceptors;` 行删除；named 参数 switch 中所有 `info.Xxx =` 改为对应局部变量赋值（`info.LogParameters` → `logParameters`，`info.LogResult` → `logResult`，`info.CacheDurationSeconds` → `cacheDurationSeconds`，`info.CacheKeyPrefix` → `cacheKeyPrefix`，`info.MaxRetryCount` → `maxRetryCount`，`info.RetryBaseDelayMs` → `retryBaseDelayMs`，`info.CircuitFailureThreshold` → `circuitFailureThreshold`，`info.CircuitBreakDurationSeconds` → `circuitBreakDurationSeconds`，`info.MaxRequestsPerSecond` → `maxRequestsPerSecond`，`info.ExcludeMethods` → `excludeMethods`，`info.AuthorizeRoles` → `authorizeRoles`，`info.AuthorizePolicy` → `authorizePolicy`，`info.TransactionTimeoutSeconds` → `transactionTimeoutSeconds`，`info.TransactionIsolationLevel` → `transactionIsolationLevel`，`info.AuditIncludeParameters` → `auditIncludeParameters`，`info.ProfilingThresholdMs` → `profilingThresholdMs`，`info.ProfilingIncludeProcess` → `profilingIncludeProcess`，`info.RetryExceptionTypeNames` → `retryExceptionTypeNames`，`info.PipelineOrder` → `pipelineOrder`）

(c) 类级 CustomIntercept 收集改为：

```csharp
            foreach (var ca in classCustomAttrs)
            {
                var h = ParseCustomHandler(ca, diagnostics, classDecl);
                if (h != null) handlers.Add(h);
            }
            handlers = handlers.OrderBy(h => h.Order).ToList();
```

(d) 方法级 CustomIntercept（原 `tempInfo` 逻辑）改为：

```csharp
                if (methodCustomAttrs.Count > 0)
                {
                    var mHandlers = new List<CustomHandlerInfo>();
                    foreach (var mca in methodCustomAttrs)
                    {
                        var h = ParseCustomHandler(mca, diagnostics, classDecl);
                        if (h != null) mHandlers.Add(h);
                    }
                    methodCustomHandlers[member.Name] = mHandlers.OrderBy(h => h.Order).ToList();
                }
```

(e) `info.Methods.Add(new InterceptMethodInfo {...})` 改为：

```csharp
                methods.Add(new InterceptMethodInfo(
                    m.Name,
                    m.ReturnType.ToDisplayString(nullableFormat),
                    IsAsyncReturn(m.ReturnType),
                    m.ReturnType.SpecialType == SpecialType.System_Void,
                    m.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task",
                    methodFlags,
                    isPassthrough,
                    string.Join(", ", m.TypeParameters.Select(tp => tp.Name)),
                    string.Join(" ", m.TypeParameters
                        .Select(tp => FormatTypeParameterConstraints(tp, nullableFormat))
                        .Where(c => c != null)),
                    m.Parameters.LastOrDefault()?.Type.ToDisplayString() == "System.Threading.CancellationToken",
                    m.Parameters.Select(p => new ParamInfo(
                        p.Name,
                        p.Type.ToDisplayString(nullableFormat),
                        p.NullableAnnotation == NullableAnnotation.Annotated,
                        p.GetAttributes().Any(a =>
                            a.AttributeClass?.Name == "SensitiveAttribute" || a.AttributeClass?.Name == "Sensitive"),
                        p.IsParams ? "params"
                            : p.RefKind == Microsoft.CodeAnalysis.RefKind.None ? ""
                            : p.RefKind.ToString().ToLowerInvariant())).ToImmutableEquatableArray(),
                    methodHandlers?.ToImmutableEquatableArray()));
```

(f) AC9100 提示中的 `info.ExcludeMethods` 判断（excludeMethods 集合初始化处）改为 `excludeMethods` 局部变量：`if (!string.IsNullOrEmpty(excludeMethods)) foreach (var name in excludeMethods.Split(',')) excludeMethodsSet.Add(name.Trim());`（注意 `excludeMethods` 局部变量名与既有 `var excludeMethods = new HashSet<string>();` 冲突——将原 HashSet 改名为 `excludeMethodsSet` 或字符串局部改名为 `excludeMethodsCsv`）

(g) 最终返回（原 `return info;` / `return null;` 处）：

```csharp
            if (methods.Count == 0 && !isMethodLevelMode)
                return null;

            return new InterceptInfo(
                ns, classSymbol.Name, interfaceName, interfaceShortName,
                interceptors, isMethodLevelMode,
                methods.ToImmutableEquatableArray(), handlers.ToImmutableEquatableArray(),
                diagnostics.ToImmutableArray(),
                logParameters, logResult, cacheDurationSeconds, cacheKeyPrefix,
                maxRetryCount, retryBaseDelayMs, circuitFailureThreshold, circuitBreakDurationSeconds,
                maxRequestsPerSecond, excludeMethods, authorizeRoles, authorizePolicy,
                transactionTimeoutSeconds, transactionIsolationLevel,
                auditIncludeParameters, profilingThresholdMs, profilingIncludeProcess,
                retryExceptionTypeNames, pipelineOrder);
```

（早期 `return new InterceptInfo { Diagnostics = diagnostics }` 无接口诊断分支同步改为构造调用或继续返回 null 前先报告诊断。）

- [ ] **Step 3: ParseCustomHandler 改签名**：

```csharp
        /// <summary>解析 [CustomIntercept] 特性数据，返回 Handler 信息（无效时返回 null 并报诊断）。</summary>
        private static CustomHandlerInfo? ParseCustomHandler(AttributeData ca, List<Diagnostic> diagnostics, ClassDeclarationSyntax classDecl)
        {
            if (ca.ConstructorArguments.Length > 0 && ca.ConstructorArguments[0].Value is INamedTypeSymbol handlerType)
            {
                var interfaces = handlerType.AllInterfaces.Select(i => i.Name).ToList();
                var implementsHandler = interfaces.Any(i =>
                    i == "IInterceptHandler" || i == "IMethodHandler" || i == "IAsyncMethodHandler");
                if (!implementsHandler)
                {
                    diagnostics.Add(Diagnostic.Create(CustomHandlerNotImpl, classDecl.Identifier.GetLocation(), handlerType.Name));
                    return null;
                }

                // 检测是强类型 IMethodHandler<,>/IAsyncMethodHandler<,> 还是通用 IInterceptHandler
                bool isMethodHandler = interfaces.Any(i => i == "IMethodHandler" || i == "IAsyncMethodHandler");
                bool isAsyncHandler = interfaces.Any(i => i == "IAsyncMethodHandler");

                int order = 100;
                foreach (var na in ca.NamedArguments)
                {
                    if (na.Key == "Order" && na.Value.Value is int ov) order = ov;
                }

                return new CustomHandlerInfo(
                    handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    handlerType.Name,
                    order,
                    isMethodHandler,
                    isAsyncHandler);
            }
            return null;
        }
```

- [ ] **Step 4: 构建生成器项目并修复编译错误**

Run: `dotnet build src/AutoCode.Generators/AutoCode.Generators.csproj`
Expected: 0 errors（若消费侧 `info.Methods` 等以 List 语义使用，如 `.Count`/foreach/索引均兼容；`GenerateDIRegistration` 中 `info.Diagnostics` 的 foreach 对 ImmutableArray 同样可用）

- [ ] **Step 5: 写缓存哨兵测试**（Create: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorCachingTests.cs`）：

```csharp
using System.Linq;
using AutoCode.Tests.V2.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AutoCode.Tests.V2.Snapshots
{
    /// <summary>
    /// InterceptGenerator 增量缓存哨兵测试：无关变更（新增无标记语法树）不应触发任何重新生成。
    /// 若管道模型丧失值相等性（record 被改回可变 class / 集合未用 ImmutableEquatableArray），此测试变红。
    /// </summary>
    public class InterceptGeneratorCachingTests : GeneratorTestBase
    {
        [Fact]
        public void UnrelatedChange_AllOutputsCached()
        {
            var source = """
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService { int GetValue(); }

                    [AutoIntercept(InterceptType.Log)]
                    public class OrderService : IOrderService
                    {
                        public int GetValue() => 42;
                    }
                }
                """;

            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "CachingTestAssembly",
                new[] { tree },
                DefaultReferences().Concat(ExtensionsReferences()),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var driver = CSharpGeneratorDriver.Create(
                new[] { new InterceptGenerator().AsSourceGenerator() },
                optionsProvider: null, // V1 不经 V2Gate
                driverOptions: new GeneratorDriverOptions(
                    IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

            // 无关变更：新增一个不含任何 AutoCode 标记的语法树
            var unrelatedTree = CSharpSyntaxTree.ParseText("namespace Other { public class Unrelated { } }");
            var compilation2 = compilation.AddSyntaxTrees(unrelatedTree);
            driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation2);

            var result = driver.GetRunResult().Results.Single();
            Assert.NotEmpty(result.TrackedOutputSteps);

            foreach (var (stepName, steps) in result.TrackedOutputSteps)
            {
                foreach (var step in steps)
                {
                    Assert.All(step.Outputs, output =>
                        Assert.True(
                            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                            $"步骤 '{stepName}' 未命中增量缓存（实际: {output.Reason}）——检查管道模型是否丧失值相等性"));
                }
            }
        }
    }
}
```

- [ ] **Step 6: 运行哨兵测试与全量回归**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~InterceptGeneratorCachingTests"; dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj`
Expected: 哨兵 PASS；全量 PASS（快照零漂移——record 化不改变生成输出）

- [ ] **Step 7: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorCachingTests.cs
git commit -m "refactor(intercept): 管道模型 record 化修复增量缓存 + 缓存哨兵测试"
```

### Task 20: AC9008 Handler 双接口编译期禁止

**Files:**
- Modify: `src/AutoCode.Generators/V1/InterceptGenerator.cs`
- Test: `src/AutoCode.Tests.V2/Snapshots/InterceptGeneratorSnapshotTests.cs`

- [ ] **Step 1: 写失败测试**：

```csharp
        [Fact]
        public async Task HandlerImplementingBothInterfaces_ReportsAC9008()
        {
            var source = """
                using System;
                using AutoCode.Model;
                namespace TestApp
                {
                    public interface IOrderService { string GetName(int id); }

                    public class DualHandler : MethodHandlerBase<GetNameArgs_Int32, string>, IInterceptHandler
                    {
                        public void OnBefore(InterceptContext ctx) { }
                        public void OnAfter(InterceptContext ctx, object? result) { }
                        public void OnException(InterceptContext ctx, Exception ex) { }
                    }

                    public class OrderService : IOrderService
                    {
                        [CustomIntercept(typeof(DualHandler))]
                        public string GetName(int id) => "ok";
                    }
                }
                """;

            var run = RunGenerator(new InterceptGenerator(), source,
                enableV2: false, extraReferences: ExtensionsReferences());

            Assert.Contains(run.Diagnostics, d => d.Id == "AC9008");
        }
```

- [ ] **Step 2: 运行验证失败**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~HandlerImplementingBothInterfaces"`
Expected: FAIL（当前双接口 Handler 被识别为 IMethodHandler 且无诊断）

- [ ] **Step 3: 实现——诊断描述符**（AC9007 描述符之后追加）：

```csharp
        private static readonly DiagnosticDescriptor DualHandlerInterface = new(
            "AC9008", "Handler 不能同时实现两种接口",
            "类型 '{0}' 同时实现 IInterceptHandler 与 IMethodHandler/IAsyncMethodHandler，管线调用语义无法确定；请只实现其中一种",
            "AutoCode.Intercept", DiagnosticSeverity.Error, true);
```

- [ ] **Step 4: 实现——ParseCustomHandler 检查**（Task 19 改后形态，`interfaces` 计算之后插入）：

```csharp
                bool isLegacy = interfaces.Contains("IInterceptHandler");
                bool isTyped = interfaces.Any(i => i == "IMethodHandler" || i == "IAsyncMethodHandler");
                if (isLegacy && isTyped)
                {
                    diagnostics.Add(Diagnostic.Create(DualHandlerInterface, classDecl.Identifier.GetLocation(), handlerType.Name));
                    return null;
                }
                if (!isLegacy && !isTyped)
                {
                    diagnostics.Add(Diagnostic.Create(CustomHandlerNotImpl, classDecl.Identifier.GetLocation(), handlerType.Name));
                    return null;
                }

                // 检测是强类型 IMethodHandler<,>/IAsyncMethodHandler<,> 还是通用 IInterceptHandler
                bool isMethodHandler = isTyped;
                bool isAsyncHandler = interfaces.Any(i => i == "IAsyncMethodHandler");
```

（原 `var implementsHandler = interfaces.Any(...)` 判断与 `bool isMethodHandler = interfaces.Any(...)` 两行替换为上述逻辑。）

- [ ] **Step 5: 运行验证通过 + 回归**

Run: `dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj --filter "FullyQualifiedName~HandlerImplementingBothInterfaces"; dotnet test src/AutoCode.Tests.V2/AutoCode.Tests.V2.csproj`
Expected: AC9008 测试 PASS；全量 PASS（其余 Handler 路径行为不变）

- [ ] **Step 6: Commit**

```powershell
git add src/AutoCode.Generators/V1/InterceptGenerator.cs src/AutoCode.Tests.V2/Snapshots/
git commit -m "feat(intercept): AC9008 禁止 Handler 同时实现通用与强类型接口"
```

### Task 21: 快照全量确认 + 文档同步 + 最终回归

**Files:**
- Verify: `src/AutoCode.Tests.V2/Snapshots/`（全量确认无漂移）
- Modify: `README.md`（特性表行 + 诊断表 + AOP 章节）
- Modify: `CHANGELOG.md`
- Modify: `samples/02-InterceptAOP/README.md`、`samples/03-TypedMethodHandler/README.md`

- [ ] **Step 1: README 特性表行更新**（`[AutoIntercept]` 行）：

```markdown
| **InterceptPlugin** | `[AutoIntercept]` | 编译时 AOP 拦截器：Validate/Authorize/Throttle/Cache/CircuitBreaker/Tracing/Log/Retry/Transaction/Audit/Profiling/Metrics 全 12 种管线，`PipelineOrder` 可配顺序，替代动态代理 |
```

- [ ] **Step 2: README 诊断表追加**（AC9100 行之后）：

```markdown
| **AC9004** | Error | 异步 Handler（IAsyncMethodHandler）挂在同步方法上 | 改同步 Handler 或改异步方法 |
| **AC9005** | Info | ProfilingIncludeProcess=true 有额外开销 | 仅在排查期开启 |
| **AC9006** | Info | 启用 Authorize 拦截 | 注册 IClaimsPrincipalProvider 实现 |
| **AC9007** | Warning | PipelineOrder 含未知拦截器名 | 检查拼写 |
| **AC9008** | Error | Handler 同时实现 IInterceptHandler 与 IMethodHandler/IAsyncMethodHandler | 只实现一种 |
| **AC9010** | Info | AuthorizePolicy 已设置 | 暂未实现策略检查，使用 AuthorizeRoles |
```

- [ ] **Step 3: README AOP 章节追加 PipelineOrder 与属性用法**（AOP 拦截器章节代码示例后）：

```markdown
全部 12 种拦截器：`Log`/`Cache`/`Retry`/`CircuitBreaker`/`Metrics`/`Throttle`/`Validate`/`Tracing`/`Authorize`/`Transaction`/`Audit`/`Profiling`。

**拦截管线顺序**：默认 `Validate → Authorize → Custom(OnBefore) → Throttle → Cache → CircuitBreaker → Tracing → Log → Retry → Transaction → 调用 → Custom(OnAfter/OnException) → Audit → Profiling → Metrics`。用 `PipelineOrder` 覆盖（未列出的按默认序排后，未知名发 AC9007）：

```csharp
[AutoIntercept(
    InterceptType.Authorize | InterceptType.Transaction | InterceptType.Audit | InterceptType.Profiling,
    AuthorizeRoles = "Admin",
    TransactionTimeoutSeconds = 30,
    ProfilingThresholdMs = 100,
    PipelineOrder = "Authorize,Transaction,Audit,Profiling")]
public class PaymentService : IPaymentService
```

**关键配置属性**：`AuthorizeRoles`/`AuthorizePolicy`（预留）、`TransactionTimeoutSeconds`/`TransactionIsolationLevel`、`AuditIncludeParameters`、`ProfilingThresholdMs`/`ProfilingIncludeProcess`、`RetryOnExceptionTypes`、`PipelineOrder`、`LogResult`、`ExcludeMethods`。

**注意**：Authorize 需自行注册 `IClaimsPrincipalProvider` 适配实现；Audit 需注册 `IAuditStore`；Transaction 在 Linux 上适用单连接（单 DbContext）场景；强类型 Args record 命名为 `{MethodName}Args_{参数类型短名拼接}`（重载消歧）。
```

- [ ] **Step 4: CHANGELOG.md 顶部追加**：

```markdown
## [Unreleased]

### Added
- Intercept 补齐 Authorize/Transaction/Audit/Profiling 四种拦截器（12/12 全部可用）
- `PipelineOrder` 管线顺序可配（Attribute 级）+ AC9007 未知阶段警告
- `IAsyncMethodHandler` 识别与 await 调用 + AC9004
- 泛型方法（TypeParameters + where 约束）、ref/out/in/params 修饰符保留、方法重载 Args record 后缀消歧
- Retry CancellationToken 透传、`RetryOnExceptionTypes` 异常过滤、Throttle 固定窗口限流、CircuitBreaker 半开状态机
- 新增诊断 AC9004/AC9005/AC9006/AC9007/AC9008/AC9010

### Fixed
- `ctx.Handled = true` 降级真正生效（含重试耗尽后的最终异常路径）
- `LogResult`/`ExcludeMethods` 属性生效
- Cache null 结果不缓存 + 缓存命中日志
- 管道模型 record 化，修复增量缓存失效

### Changed
- Args record 命名规则：`{MethodName}Args_{参数类型短名拼接}`（方法重载不再 CS0101）
```

- [ ] **Step 5: samples 文档更新**

`samples/02-InterceptAOP/README.md`：
1. 拦截器清单更新为 12 种并标注默认管线顺序
2. 追加 `Handled` 降级示例（`ctx.Handled = true; ctx.Result = ...;` 不再抛异常，示例已真实生效）
3. 追加 Authorize + Transaction + Audit + Profiling 组合示例（含 `IClaimsPrincipalProvider`/`IAuditStore` 注册说明）与 `PipelineOrder` 用法

`samples/03-TypedMethodHandler/README.md`：
1. 追加异步 Handler 示例（`AsyncMethodHandlerBase<GetNameAsyncArgs_Int32, string>` 三方法 await 调用）
2. 注明 Args 命名规则：`{MethodName}Args_{参数类型短名拼接}`，ref/out/in 参数不进入 Args record

- [ ] **Step 6: 最终回归**

```powershell
dotnet test src/AutoCode.sln
dotnet build src/AutoCode.sln
git status --short src/AutoCode.Tests.V2/Snapshots/
```

Expected: 全部测试 PASS（V1 AutoCode.Tests + V2 AutoCode.Tests.V2）；0 errors；无 `.received.txt` 残留

- [ ] **Step 7: Commit**

```powershell
git add README.md CHANGELOG.md samples/02-InterceptAOP/README.md samples/03-TypedMethodHandler/README.md
git commit -m "docs: AOP 12/12 拦截器、PipelineOrder、新诊断与属性文档同步"
```
