---
kind: logging_system
name: AutoCode 日志系统 — 基于 Roslyn Source Generator 的自动日志装饰器
category: logging_system
scope:
    - '**'
source_files:
    - src/AutoCode.Model/AutoLogAttribute.cs
    - src/AutoCode.Logging/LogDecoratorGenerator.cs
    - src/AutoCode.Plugins.Logging/LogDecoratorGenerator.cs
    - src/AutoCode.Cli/Program.cs
---

## 1. 系统概述
AutoCode 框架通过 Roslyn IIncrementalGenerator 在编译期自动生成带结构化日志、耗时统计与异常捕获的装饰器类，无需手写日志代码。核心机制是：在 Service 类上标记 `[AutoLog]` 特性，生成器自动识别其实现的接口并生成 `Logging{ClassName}` 装饰器类，使用 Microsoft.Extensions.Logging 输出结构化日志。

## 2. 核心文件与包
- **AutoLog 特性定义**：`src/AutoCode.Model/AutoLogAttribute.cs` — 定义 `LogParameters`（是否记录参数）和 `LogElapsed`（是否记录耗时）两个配置项
- **v1 日志生成器**：`src/AutoCode.Logging/LogDecoratorGenerator.cs` — 基础版本，直接字符串拼接生成代码
- **v2 日志生成器（插件版）**：`src/AutoCode.Plugins.Logging/LogDecoratorGenerator.cs` — 增强版，支持敏感参数脱敏、结构化日志、使用 CodeBuilder 构建代码
- **CLI 集成**：`src/AutoCode.Cli/Program.cs` — 将 Logging 作为可选项暴露给命令行工具

## 3. 架构与设计模式
- **装饰器模式**：生成的 `Logging{ClassName}` 类实现目标接口，内部持有原服务实例 `_inner` 和 `ILogger<{decoratorName}>`，所有方法调用都经过装饰器包装
- **增量源码生成**：使用 `IIncrementalGenerator` + `SyntaxProvider` 监听带有 `[AutoLog]` 特性的类声明，避免全量扫描
- **异步友好**：自动识别 `Task`/`ValueTask`/`void` 返回类型，分别生成对应的 async/sync 方法实现
- **异常安全**：try-catch 包裹业务调用，异常时记录错误日志并重新抛出

## 4. 日志结构与约定
- **开始日志**：`_logger.LogInformation("{MethodName} 开始{paramLog}", params)` — 记录方法名和参数键值对
- **完成日志**：`_logger.LogInformation("{MethodName} 完成, 耗时 {Elapsed}ms", sw.ElapsedMilliseconds)` — 记录执行耗时
- **异常日志**：`_logger.LogError(ex, "{MethodName} 异常, 耗时 {Elapsed}ms", sw.ElapsedMilliseconds)` — 记录异常堆栈和耗时
- **结构化字段**：参数以 `{ParamName={Value}}` 形式记录，支持 `[SensitiveAttribute]` 标记的敏感参数仅记录数量不记录内容
- **性能监控**：使用 `Stopwatch` 精确测量方法执行时间

## 5. 约束与规则
- **目标限制**：仅处理实现了非生命周期接口（排除 `IScoped`/`ISingleton`/`ITransient`/`IDependencyBase`）的类
- **方法过滤**：仅处理普通方法（`MethodKind.Ordinary`），忽略属性、事件等成员
- **命名约定**：生成的装饰器类名为 `Logging{原始类名}.g.cs`，遵循 C# 源码生成器命名规范
- **依赖注入**：装饰器构造函数需要传入被装饰的服务实例和 `ILogger<T>` 实例，需配合 DI 容器使用
- **特性兼容性**：同时支持 `AutoLog` 和 `AutoLogAttribute` 两种写法