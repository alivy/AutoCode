---
kind: configuration_system
name: 配置系统 — ASP.NET Core 默认配置与静态 IConfiguration 暴露
slug: configuration_system
category: configuration_system
scope:
    - '**'
---

本仓库的运行时配置系统基于 ASP.NET Core 默认的 `Microsoft.Extensions.Configuration` 框架，采用标准的 `appsettings.json` + 环境变量覆盖模式，并在核心层通过静态字段暴露 `IConfiguration` 供全局访问。

**使用的框架与工具**
- ASP.NET Core 内置配置系统（`IConfiguration`、`WebApplicationBuilder`）
- JSON 配置文件（`appsettings.json`、`appsettings.Development.json`）
- 环境变量覆盖（通过 `Environment.GetEnvironmentVariable` 在测试中读取 `DOTNET_ROOT`）

**关键文件与位置**
- `src/APP.WebAPI/appsettings.json`：应用基础配置（Logging、AllowedHosts）
- `src/APP.WebAPI/appsettings.Development.json`：开发环境覆盖配置
- `src/APP.WebAPI/Program.cs`：使用 `WebApplication.CreateBuilder(args)` 构建宿主并加载配置
- `src/APP.WebAPI.Core/Application/AppCore.cs`：静态持有 `IConfiguration Configuration`，在 `ConfigureApplication` 中从 `hostContext.Configuration` 赋值
- `src/APP.WebAPI.Core/DependencyInjection/DependencyInjectionServiceCollectionExtensions.cs`：依赖注入扫描扩展，不直接操作配置但依赖 DI 容器初始化顺序

**架构与约定**
1. **配置加载流程**：`Program.cs` 调用 `WebApplication.CreateBuilder(args)` 自动加载 `appsettings.json` 及环境特定文件；随后通过扩展方法 `builder.InitAPI()`（由 `AppCore.ConfigureApplication` 提供）将 `hostContext.Configuration` 赋值给静态字段 `AppCore.Configuration`。
2. **静态配置访问**：`AppCore.Configuration` 作为全局只读配置入口，任何组件可通过 `AppCore.Configuration["Key"]` 或 `GetSection()` 获取值，无需显式注入。
3. **环境分层**：遵循 ASP.NET Core 约定，`Development` 环境通过同名 `appsettings.Development.json` 覆盖生产配置。
4. **无自定义配置源**：未发现 `AddJsonFile`、`AddEnvironmentVariables`、`AddCommandLine` 等自定义配置源的调用，完全依赖框架默认行为。

**约束与规范**
- 配置键名使用 JSON 层级结构（如 `Logging:LogLevel:Default`），未使用 `.env`、`.yaml`、`.toml` 等其他格式。
- 敏感信息未通过配置系统管理，示例中的 API Key 以硬编码字符串形式出现在 `DotTemplate.APP/Program.cs` 注释中。
- 配置对象为静态全局单例，存在线程安全隐式假设（仅读取，无写入）。