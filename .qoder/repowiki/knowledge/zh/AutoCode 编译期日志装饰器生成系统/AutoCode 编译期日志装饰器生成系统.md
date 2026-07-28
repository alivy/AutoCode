---
kind: logging_system
name: AutoCode 编译期日志装饰器生成系统
category: logging_system
scope:
    - '**'
source_files:
    - src/AutoCode.Logging/LogDecoratorGenerator.cs
    - src/AutoCode.Model/AutoLogAttribute.cs
---

该仓库的日志系统基于 Roslyn IIncrementalGenerator 在编译期自动生成带结构化日志的装饰器类，采用 Decorator Pattern 对标记了 `AutoLogAttribute` 的服务类进行横切增强。

**使用的框架与工具**
- 基于 Microsoft.CodeAnalysis 的增量源生成器（IIncrementalGenerator）
- 使用 Microsoft.Extensions.Logging 作为运行时日志抽象（ILogger<T>）
- 通过 Stopwatch 记录方法执行耗时

**核心文件与位置**
- `src/AutoCode.Model/AutoLogAttribute.cs`：定义 `AutoLogAttribute`，提供 `LogParameters`（是否记录参数，默认 true）和 `LogElapsed`（是否记录耗时，默认 true）两个配置属性
- `src/AutoCode.Logging/LogDecoratorGenerator.cs`：实现 `LogDecoratorGenerator`，扫描带有 `[AutoLog]` 的类，为其生成 `Logging{ClassName}` 装饰器类

**架构与设计决策**
1. **装饰器模式**：生成的装饰器类实现被装饰服务所实现的接口，持有 `_inner`（原始服务实例）和 `_logger`（ILogger<T>）两个字段，通过构造函数注入
2. **增量生成**：使用 `SyntaxProvider` + `SemanticModel` 精准匹配标记了 `AutoLog` 或 `AutoLogAttribute` 的类声明，避免全量扫描
3. **异步支持**：自动识别 `Task`、`ValueTask` 及普通返回值，分别生成对应的同步/异步包装逻辑
4. **统一日志格式**：每个方法调用统一输出「开始」「完成」「异常」三类日志，包含方法名、参数值（可选）、耗时毫秒数
5. **异常透传**：catch 块中记录错误后重新抛出，不吞异常

**约定与约束**
- 仅对实现了业务接口的类生效（排除 `IScoped`、`ISingleton`、`ITransient`、`IDependencyBase` 等 DI 基接口）
- 生成的装饰器类名为 `Logging{原类名}`，位于相同命名空间下，文件名以 `.g.cs` 结尾
- 参数日志通过 C# 插值字符串 `{paramName={value}}` 形式输出，空参数时省略该部分
- 所有日志均使用 `InformationLevel` 记录成功路径，`ErrorLevel` 记录异常路径