---
kind: logging_system
name: 日志系统 — 无结构化日志框架，仅使用 Console.WriteLine 简单输出
category: logging_system
scope:
    - '**'
source_files:
    - src/AutoCode.Cli/Program.cs
    - src/APP/Program.cs
    - src/APP.Map/Program.cs
    - src/APP.WebAPI/Program.cs
---

该仓库未集成任何结构化日志框架（如 Serilog、NLog、Microsoft.Extensions.Logging），也没有统一的日志记录基础设施。代码中的“日志”行为完全由 `Console.WriteLine` 直接输出到控制台，属于最基础的调试/诊断输出方式。

具体表现：
- CLI 工具 `AutoCode.Cli/Program.cs` 使用 `Console.WriteLine` 打印生成器列表、初始化结果、模板验证状态等信息，并配合 `[ERROR]`、`[OK]` 等前缀进行简单的级别区分。
- 示例应用 `APP/Program.cs`、`APP.Map/Program.cs` 同样直接使用 `Console.WriteLine` 输出测试信息。
- Web API 项目 `APP.WebAPI/Program.cs` 和核心配置 `APP.WebAPI.Core/Application/AppCore.cs` 中未发现任何 ILogger、Serilog、NLog 或 Microsoft.Extensions.Logging 的引用与配置。
- 测试项目中出现 `ILoggerService` 仅为自定义接口名称，并非 Microsoft.Extensions.Logging 的 `ILogger`，且仅用于依赖注入测试，与日志框架无关。

约束与约定：
- 当前仓库不存在日志级别管理、结构化字段、日志路由或持久化机制。
- 所有输出均为同步控制台文本，无法按环境（Development/Production）切换输出目标。
- 若未来需要引入日志系统，建议采用 `Microsoft.Extensions.Logging` 作为统一抽象，并通过 `appsettings.json` 配置不同环境的日志级别与输出目标。