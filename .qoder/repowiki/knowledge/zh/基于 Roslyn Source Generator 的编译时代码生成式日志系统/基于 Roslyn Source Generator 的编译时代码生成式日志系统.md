---
kind: logging_system
name: 基于 Roslyn Source Generator 的编译时代码生成式日志系统
category: logging_system
scope:
    - '**'
source_files:
    - src/AutoCode.Model/AutoLogAttribute.cs
    - src/AutoCode.Generators/V2/LogDecoratorGenerator.cs
    - src/AutoCode.Generators/V1/LogDecoratorGenerator.cs
    - src/AutoCode.Engine/Config/ConfigRecommender.cs
    - autocode.json
    - src/AutoCode.Model/AutoEntityAttribute.cs
    - src/AutoCode.Model/AutoInterceptAttribute.cs
---

## 1. 系统概览

AutoCode 的日志系统并非运行时日志框架，而是一个**编译时代码生成式 AOP 日志方案**。它通过 Roslyn `IIncrementalGenerator` 扫描标记了 `[AutoLog]`（或 `[AutoLogAttribute]`）的服务类，自动生成一个实现相同接口的装饰器类（命名约定为 `Logging{ClassName}`），在方法调用前后注入结构化日志、耗时统计与异常捕获。底层依赖 .NET 标准抽象 `Microsoft.Extensions.Logging.ILogger<T>`，因此可对接任意 ILogger 提供者（Console、Serilog、NLog、Application Insights 等）。

## 2. 核心文件与职责

- **声明入口**：`src/AutoCode.Model/AutoLogAttribute.cs` — 定义 `[AutoLog]` 特性，暴露 `LogParameters`（默认 true，记录入参）、`LogElapsed`（默认 true，记录耗时）两个开关。
- **V2 生成器**：`src/AutoCode.Generators/V2/LogDecoratorGenerator.cs` — 当前主分支使用的增量生成器，使用 `CodeBuilder`/`CodeWriter` 构建 AST 风格的代码输出；支持敏感参数脱敏（检测参数上的 `SensitiveAttribute`）。
- **V1 生成器**：`src/AutoCode.Generators/V1/LogDecoratorGenerator.cs` — 旧版字符串拼接实现，功能基本一致但不支持敏感参数脱敏。
- **配置推荐**：`src/AutoCode.Engine/Config/ConfigRecommender.cs` — 当检测到项目中使用 `ILogger` / `LogInformation` 时，自动推荐启用 `plugins.intercept` 并设置 `defaultInterceptors = Log,Metrics`。
- **全局配置**：根目录 `autocode.json` 中 `logging` 段提供 `structuredLogging`、`includeOpenTelemetry`、`maskSensitive` 等开关；`cascade.logging` 控制是否级联生成；`intercept.defaultInterceptors` 指定默认拦截器链包含 `Log`。
- **示例用法**：`src/APP.WebAPI/Services/OrderService.cs` 等 Service 类上标注 `[AutoLog]`，由生成器产出对应 `LoggingOrderService.g.cs`。

## 3. 架构与设计决策

### 3.1 装饰器模式 + 源码生成
每个带 `[AutoLog]` 的服务类会生成一个同名前缀为 `Logging` 的装饰器类，实现同一接口，持有 `_inner`（被包装服务）和 `_logger`（`ILogger<LoggingXxx>`）两个字段，构造时通过 DI 注入。该设计完全零侵入——业务类无需引用任何日志库。

### 3.2 结构化日志字段
V2 生成器将非敏感参数以键值对形式写入日志消息模板，例如：
```
_logger.LogInformation("MethodName 开始, param1 = {param1}, param2 = {param2}", param1, param2);
```
若存在 `SensitiveAttribute` 标记的参数，则替换为占位符 `[SensitiveParams={count}]` 并仅记录数量，避免泄露密码、Token 等敏感信息。

### 3.3 耗时统计与异常捕获
每个方法体包裹 `Stopwatch.StartNew()`，成功路径记录 `"完成, 耗时 {Elapsed}ms"`，异常路径通过 `LogError(ex, ...)` 记录堆栈与耗时后重新抛出。异步方法（`Task`/`ValueTask`/`void`）均有专门分支处理。

### 3.4 双版本并存
V1 使用 `StringBuilder` 拼接字符串生成代码，V2 改用 `AutoCode.Engine.CodeBuilder`（`ClassBuilder`/`MethodBuilder`/`PropertyBuilder`/`CodeWriter`）进行类型安全的代码构建，并通过 `V2Gate` 统一接入增量生成管线。V1 仍保留用于兼容旧项目。

### 3.5 与拦截器体系集成
除独立的 `[AutoLog]` 外，`AutoInterceptAttribute` 也支持 `LogParameters` 开关，且 `intercept.defaultInterceptors = "Log,Metrics"` 使日志成为默认拦截链的一部分。`ConfigRecommender` 在发现 `ILogger` 使用时会自动建议开启此拦截器。

## 4. 约定与约束

| 约定 | 说明 | 来源 |
|---|---|---|
| 装饰器命名 | `Logging{原类名}.g.cs` | V2 生成器 `decoratorName = $"Logging{info.ClassName}"` |
| 目标接口 | 装饰器实现服务类实现的第一个非 DI 生命周期接口（排除 `IScoped`/`ISingleton`/`ITransient`/`IDependencyBase`） | 生成器 `serviceInterface` 选择逻辑 |
| 日志级别 | 正常流程用 `LogInformation`，异常用 `LogError(ex, ...)` | 生成器方法体模板 |
| 敏感参数 | 参数加 `[SensitiveAttribute]` 即被脱敏，仅记录个数 | V2 生成器 `IsSensitive` 判断 |
| 开关项 | `LogParameters`、`LogElapsed` 默认均为 true，可在特性上覆盖 | `AutoLogAttribute` 属性定义 |
| 配置入口 | `autocode.json` 的 `logging.*`、`cascade.logging`、`intercept.defaultInterceptors`、`plugins.logging.enabled` | 根配置文件 |
| 运行时依赖 | 仅依赖 `Microsoft.Extensions.Logging.Abstractions`，不绑定具体日志后端 | 生成代码 `using Microsoft.Extensions.Logging;` |

## 5. 关键约束与规则

- **必须实现接口**：只有实现了某个业务接口（非 DI 生命周期接口）的类才会被识别为目标，纯类不会被生成装饰器。
- **仅普通方法**：生成器过滤 `MethodKind == Ordinary`，构造函数、静态方法、重写方法等不在范围内。
- **异常不吞掉**：catch 块中记录错误后 `throw;` 重新抛出，保证调用方感知异常。
- **异步安全**：对 `Task`、`ValueTask`、`void` 三种返回类型分别生成不同分支，避免 await 语义错误。
- **敏感参数脱敏是强制行为**：一旦参数标记 `SensitiveAttribute`，其值不会出现在日志中，只记录出现次数。

## 6. 适用性说明

本仓库的“日志系统”不是运行时日志框架，而是**编译时代码生成式 AOP 日志装饰器**。它不引入运行时日志中间件，也不管理日志级别、输出目标或采样策略——这些由宿主应用的 `ILogger` 绑定决定。AutoCode 的职责仅限于在编译期生成调用 `ILogger<T>` 的装饰器代码。