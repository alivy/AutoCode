# AutoCode AOP 使用体验（DX）评审清单

- **日期**：2026-09-08
- **状态**：评审中（供用户圈选，未进入设计/实施）
- **范围**：`[AutoIntercept]` 编译时 AOP 的**开发者使用体验**，不含功能正确性
- **与既有 spec 的关系**：[2026-09-02-aop-improvements-design.md](./2026-09-02-aop-improvements-design.md) 已系统覆盖「功能正确性/完整性」（假功能补齐、Handled 降级、异步 Handler、泛型/ref-out/重载、retry/throttle/熔断/缓存语义、管线顺序、record 化）。本清单**只列它未覆盖或角度不同的 DX 缺口**，并标注重叠处，避免重复排期。

## 评审方法

结论均来自代码/配置证据，非主观印象。核心证据文件：
- 生成器：`src/AutoCode.Generators/V1/InterceptGenerator.cs`
- 契约层：`src/AutoCode.Model/AutoInterceptAttribute.cs`、`src/AutoCode.Model/IInterceptHandler.cs`
- 重构：`src/AutoCode.Analyzers/CodeFixes/GenerateHandlerRefactoring.cs`
- 配置：`autocode.json`、`autocode.schema.json`、`src/AutoCode.Engine/Config/ConfigRecommender.cs`
- 示例：`samples/02-InterceptAOP`、`samples/03-TypedMethodHandler`、`src/APP.WebAPI`

优先级：P0 = 采用即踩坑/静默失效；P1 = 排错成本高；P2 = 降门槛与可见性；P3 = 范围扩展/YAGNI 待评估。
成本：S = 半天内；M = 1~3 天；L = 一周以上（含快照/文档）。

---

## 一、静默失效类（最伤体验，P0）

### DX-01 · `autocode.json` 的 intercept 配置完全未被消费
- **证据**：`autocode.json` 声明 `intercept.defaultInterceptors / cacheDurationSeconds / maxRetryCount / retryBaseDelayMs / circuitFailureThreshold / circuitBreakDurationSeconds`；`autocode.schema.json` 对其做了文档化；`ConfigRecommender` 还会主动推荐 `{"intercept":{"defaultInterceptors":"Log,Metrics"}}`。但 V1 `InterceptGenerator` 全程只读 Attribute，**从不访问 `AnalyzerConfigOptionsProvider`**（全仓仅 V2 生成器读配置）。
- **DX 影响**：用户在 JSON 里调这些值**毫无效果**，且没有任何提示。比「没有此功能」更糟——用户以为生效了。直接违反既有 spec 原则⑤「不允许静默失效」。
- **建议方向（二选一）**：
  - (A) **打通**：让 InterceptGenerator 读 global options，把 JSON 值作为「未在 Attribute 显式指定时的默认值」来源（定义清楚 Attribute > JSON > 内置默认 的优先级）。
  - (B) **收敛**：若短期不做约定式配置，则从 `autocode.json` / `schema` / `ConfigRecommender` 移除这些键并文档说明，消除误导。
- **取舍**：(A) 有 V2 现成配置消费模式可参照，但需定义优先级语义并加快照；(B) 成本极低但放弃「集中默认/约定式」能力（与 DX-06 强相关）。
- **优先级/成本**：P0 / M（A）或 S（B）

### DX-02 · 忘记调用 DI 注册 → 拦截静默不生效（且官方示例就中招）
- **证据**：生成器产出 `Intercepted{Class}.DI.g.cs` 里的 `services.AddIntercepted{Class}()`，但**全仓从无调用点**（`grep AddIntercepted` 仅命中生成器自身 + 一个测试断言）。`src/APP.WebAPI/Program.cs` 走 `builder.InitAPI()`，`APP.WebAPI.Core` 内 0 处 `AddIntercepted`。→ **示例项目的 AOP 装饰器从未挂进容器，拦截实际未生效。**
- **DX 影响**：编译通过、运行无错、就是没拦截——最难排查的一类失败。「最后一公里」是手动的、happy path 未文档化、且官方示例本身漏接，示范效应负面。
- **建议方向**：
  - (A) **聚合注册**：生成一个 `services.AddAutoCodeIntercepts()` 一次性挂载全部装饰器，用户只需在启动处调一行；README/示例改用它。
  - (B) **漏接诊断**：Analyzer 检测「存在被装饰类但启动路径未注册」→ Warning（跨编译单元分析注册调用，成本较高）。
  - (C) **与 DependencyInjectionGenerator 联动**：由 DI 生成器自动挂载装饰器（耦合两个生成器）。
- **取舍**：(A) 最简单、低风险、立竿见影，推荐；(B) 最强但实现复杂；(C) 需协调生成顺序与职责边界。
- **优先级/成本**：P0 / S~M

---

## 二、错误「早而清晰」类（P1）

### DX-03 · `[CustomIntercept]` 的 Handler 未注册进 DI → 运行时才炸
- **证据**：DI 生成 `sp.GetRequiredService<{handlerType}>()`。Handler 若未注册，容器解析装饰器时抛异常，发生在**运行期**，信息不直接指向「你忘了注册 Handler」。
- **建议方向**：文档明确「Handler 需自行注册」；可选 Analyzer 提示；或生成 `GetService` + 缺失时抛带清晰指引的异常。
- **优先级/成本**：P1 / S~M

### DX-04 · Handler 泛型 `TArgs/TResult` 与方法签名不匹配 → 天书级编译错误
- **证据**：`ParseCustomHandler` 只校验 Handler 是否实现 `IInterceptHandler`/`IMethodHandler`，**不校验泛型实参**。若用户写 `MethodHandlerBase<GetNameArgs, string>` 而方法实际返回 `int`，生成的 `OnAfter(__args, __result, __mctx)` 类型不符才编译失败，报错点在**生成代码**里，离根因很远。
- **建议方向**：新增诊断（如 AC9xxx，Error）校验 Handler 的 `TArgs` 名 == `{Method}Args`、`TResult` == 方法返回内层类型，给「Handler X 的 TResult(string) 与方法 Y 返回(int) 不匹配」这类可操作信息 + CodeFix。
- **取舍**：需解析 Handler 泛型实参（`INamedTypeSymbol.TypeArguments` 可得），中等成本；能显著降低新手排错时间。
- **优先级/成本**：P1 / M

### DX-05 · `Handled=true` 但未设 `Result`，值类型 → 运行时 InvalidCastException
- **证据**：`GenerateHandledFallback` 直接硬转 `return (innerType)__mctx.Result!;`。Result 为 null 且目标为值类型时运行期崩。
- **与既有 spec 的关系**：spec 阶段1 的处理是「文档注明降级必须同时设 Result」——本条建议**在生成代码层面兜底**，比纯文档更友好。
- **建议方向**：生成安全降级，如 `if (__ctx.Result is {innerType} __r) return __r; throw new InvalidOperationException("清晰信息：Handler 设置 Handled 但未提供 Result")`。
- **优先级/成本**：P1 / S

---

## 三、降门槛 / 减少样板类（P2）

### DX-06 · 逐类打 Flags 啰嗦，缺 Profile/预设与约定式 AOP
- **证据**：`InterceptType.Log | InterceptType.Cache | InterceptType.Retry | InterceptType.Metrics` 需在每个类重复；无「预设组合」、无「命名空间/接口约定式启用」。
- **建议方向**：
  - (A) 内置预设常量（如 `InterceptPreset.Resilience = Log|Retry|CircuitBreaker|Metrics`、`Observability = Log|Metrics|Tracing`）。
  - (B) `autocode.json` 定义命名 profile，Attribute 引用 profile 名（依赖 DX-01 选 A）。
  - (C) 约定式：某命名空间下所有 `IXxxService` 自动启用指定拦截器（依赖 DX-01）。
- **取舍**：(A) 独立可做、成本最低；(B)(C) 能力强但依赖配置消费基建（DX-01 决策）。
- **优先级/成本**：P2 / S(A) 或 L(B/C)

### DX-07 · `InterceptType.` 前缀重复
- **建议方向**：文档提示 `using static AutoCode.Model.InterceptType;` 后可直接写 `Log | Cache`；或 Attribute 提供字符串简写重载。
- **优先级/成本**：P2 / S（文档为主）

### DX-08 · 生效管线（拦截器集合+顺序）不可见，只能翻 `.g.cs`
- **证据**：装饰器 XML 注释里有 `DescribeInterceptors`，但要看到「某方法实际套了哪些、什么顺序」仍需展开生成文件。
- **建议方向**：Info 诊断（类/方法级）汇总有效拦截器与顺序，IDE 错误列表可见（参照现有 AC9100 Args 提示模式）。与 spec 阶段4 PipelineOrder 协同。
- **优先级/成本**：P2 / M

---

## 四、测试与范围类（P2 / P3）

### DX-09 · 缺 AOP 单测样例与助手
- **证据**：`samples/` 有 01~04，无「如何给带拦截的 Service 写单测」；开发者不清楚测装饰器还是测 inner、如何断言管线顺序/降级/短路。
- **建议方向**：新增 `samples/05-TestingAOP`（Before/After 风格）+ 轻量测试助手（构造装饰器、注入 fake Handler、断言顺序/降级/短路）。
- **优先级/成本**：P2 / M

### DX-10 · 仅方法级拦截，不支持属性 getter / 构造函数 / 事件
- **建议方向**：评估真实需求；无强需求则明确「by design 仅方法」并文档说明（YAGNI）。
- **优先级/成本**：P3 / 评估 S

### DX-11 · 无环境条件化拦截（如 Profiling 仅 Development）
- **建议方向**：Attribute/config 支持条件启用；或文档给出 `#if DEBUG` / 环境判断的推荐写法。
- **优先级/成本**：P3 / S~M

---

## 五、文档准确性（与既有 spec 重叠）

### DX-12 · 对外承诺与实现不符
- **证据**：README 拦截器表列出 9 种；spec 已确认实际仅 8 种有生成逻辑；且 DX-01 的配置项对外可见却无效。
- **处理**：归入既有 spec 阶段4「文档同步」统一收口，本清单不单独排期，仅登记以免遗漏。
- **优先级/成本**：P1 / S（随 spec 阶段4）

---

## 优先级汇总

| 波次 | 主题 | 条目 | 说明 |
|---|---|---|---|
| Wave 1（P0） | 消除静默失效 | DX-01、DX-02 | 采用即踩坑，最高优先；DX-01 需先决策「打通 vs 收敛」 |
| Wave 2（P1） | 错误早而清晰 | DX-03、DX-04、DX-05 | 降低排错成本，独立可做 |
| Wave 3（P2） | 降门槛/可见性/测试 | DX-06、DX-07、DX-08、DX-09 | DX-06(B/C) 依赖 DX-01 决策 |
| Wave 4（P3） | 范围扩展 | DX-10、DX-11 | 先评估必要性，可能 YAGNI |
| 随 spec | 文档同步 | DX-12 | 归入既有 spec 阶段4 |

## 依赖关系

- DX-06(B/C)、DX-08 的配置化能力 **依赖 DX-01 选 (A) 打通配置**。若 DX-01 选 (B) 收敛，则 DX-06 仅剩预设常量 (A) 可行。
- DX-02 与 DX-03 都涉及 DI 接入体验，可合并为「DI 接入一体化」一并设计。
- DX-05、DX-04 都改动 Handler/降级相关生成路径，可同批加快照。

## 待用户决策的开放问题

1. **DX-01 走 (A) 打通配置 还是 (B) 收敛删除？** 这决定 DX-06/DX-08 能否走配置化路线。
2. **DX-02 采用聚合注册 (A) 即可，还是要做到漏接诊断 (B)？**
3. 本轮想推进到哪一波（Wave 1 / 1+2 / 全部）？
4. 是否需要我把选定条目转成正式设计 spec + 实施计划（沿用既有 AOP spec 的快照先行纪律）？
