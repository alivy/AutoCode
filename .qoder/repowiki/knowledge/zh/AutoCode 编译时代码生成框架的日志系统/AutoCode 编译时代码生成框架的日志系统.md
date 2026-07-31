---
kind: logging_system
name: AutoCode 编译时代码生成框架的日志系统
category: logging_system
scope:
    - '**'
source_files:
    - src/AutoCode.Model/AutoLogAttribute.cs
    - src/AutoCode.Logging/LogDecoratorGenerator.cs
    - src/AutoCode.Plugins.Logging/LogDecoratorGenerator.cs
    - src/AutoCode.Intercept/InterceptGenerator.cs
    - src/AutoCode.Engine/Config/ConfigRecommender.cs
    - src/AutoCode.Plugins.Testing/TestGenerator.cs
---

## 系统概述

AutoCode 框架提供基于 Roslyn IIncrementalGenerator 的**编译时代码生成式日志系统**。通过在 Service 类上标记 `[AutoLog]` 特性，自动生成实现装饰器模式（Decorator Pattern）的日志包装类，无需手写样板代码即可为所有方法注入结构化日志、耗时统计、异常捕获和敏感参数脱敏能力。

## 核心组件与架构

### 1. 特性定义层（AutoCode.Model）
- `AutoLogAttribute.cs`：定义日志装饰器的元数据，支持 `LogParameters`（是否记录参数，默认 true）和 `LogElapsed`（是否记录耗时，默认 true）两个配置项
- `SensitiveAttribute.cs`：用于标记需要脱敏的参数属性，配合日志生成器自动隐藏敏感信息

### 2. 日志生成器实现（双版本并存）

**v1 版本 - AutoCode.Logging 项目**
- `LogDecoratorGenerator.cs`：基础版 Roslyn 源生成器，直接字符串拼接生成 C# 代码
- 使用 `Microsoft.Extensions.Logging.ILogger<T>` 作为日志输出
- 自动生成 `Logging{ClassName}` 装饰器类，实现目标接口并包装所有方法调用

**v2 版本 - AutoCode.Plugins.Logging 项目**
- `LogDecoratorGenerator.cs`：增强版生成器，使用 CodeBuilder 构建器模式生成代码
- 支持结构化日志参数传递，自动过滤敏感参数
- 通过 `SensitiveAttribute` 标记的参数会被替换为 `[SensitiveParams=N]` 占位符

### 3. 集成到拦截管线
- `AutoCode.Intercept/InterceptGenerator.cs`：在 AOP 拦截管线中集成日志功能
- 当启用 `InterceptFlags.Log` 时，自动注入 `ILogger<T>` 依赖
- 支持与其他拦截器（缓存、指标、追踪等）组合使用

## 生成的日志行为

### 标准日志流程
每个被 `[AutoLog]` 标记的方法都会生成如下结构的装饰器代码：
1. **方法开始**：记录方法名和参数（非敏感参数）
2. **耗时统计**：使用 `Stopwatch` 测量执行时间
3. **异常捕获**：try-catch 包裹，异常时记录错误日志并重抛
4. **方法完成**：记录成功完成和耗时（毫秒）

### 异步方法支持
- 对 `Task`、`ValueTask` 返回类型进行特殊处理
- 正确 await 异步调用，确保异常传播和耗时统计准确

### 结构化日志字段
生成的日志消息包含以下结构化字段：
- 方法名称（MethodName）
- 非敏感参数键值对（ParameterName=Value）
- 敏感参数数量（[SensitiveParams=N]）
- 执行耗时（Elapsed=xxx ms）

## 配置推荐机制

`ConfigRecommender.cs` 中的智能推荐引擎会检测项目中的 `ILogger` 使用情况，自动建议开启日志相关功能：
- 检测到 `ILogger` 或 `LogInformation` → 推荐启用 `AutoLog` 或 `AutoIntercept(Log)`
- 推荐优先级为 Medium，可通过配置文件覆盖

## 测试集成

`AutoCode.Plugins.Testing/TestGenerator.cs` 中针对 `ILogger` 类型的依赖注入提供 Mock 对象生成：
```csharp
if (type.StartsWith("ILogger"))
    return "new Mock<Microsoft.Extensions.Logging.ILogger>().Object";
```

## 技术栈与约束

- **日志框架**：Microsoft.Extensions.Logging（.NET 标准依赖注入日志抽象）
- **生成方式**：Roslyn IIncrementalGenerator（编译时代码生成）
- **设计模式**：装饰器模式（Decorator Pattern），通过构造函数注入内部服务和 ILogger
- **命名约定**：生成的装饰器类名为 `Logging{原类名}.g.cs`
- **性能考虑**：使用 Stopwatch 而非 DateTime.Now 进行耗时统计

## 与传统日志方案的区别

| 特性 | 传统手写日志 | AutoCode 生成式日志 |
|------|-------------|-------------------|
| 代码量 | 每个方法需手写日志语句 | 零手写，自动生成 |
| 一致性 | 容易遗漏或格式不统一 | 全项目统一格式 |
| 维护成本 | 修改日志逻辑需逐个文件更新 | 修改生成器一次生效 |
| 性能 | 运行时反射或字符串拼接 | 编译时代码优化 |
| 安全性 | 可能误记敏感信息 | 自动敏感参数脱敏 |
