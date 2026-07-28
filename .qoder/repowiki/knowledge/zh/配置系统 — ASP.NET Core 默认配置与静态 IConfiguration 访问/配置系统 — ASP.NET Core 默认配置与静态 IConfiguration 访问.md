---
kind: configuration_system
name: 配置系统 — ASP.NET Core 默认配置与静态 IConfiguration 访问
category: configuration_system
scope:
    - '**'
source_files:
    - src/APP.WebAPI/appsettings.json
    - src/APP.WebAPI/appsettings.Development.json
    - src/APP.WebAPI/Program.cs
    - src/APP.WebAPI.Core/Application/AppCore.cs
---

该仓库中的配置系统基于 ASP.NET Core 默认的 `Microsoft.Extensions.Configuration` 框架，采用标准的 `appsettings.json` + 环境变量/环境文件分层模式。核心特点如下：

1. **配置文件结构**：WebAPI 项目使用 `appsettings.json` 作为主配置，`appsettings.Development.json` 作为开发环境覆盖配置，遵循 ASP.NET Core 约定。

2. **配置加载流程**：在 `Program.cs` 中通过 `WebApplication.CreateBuilder(args)` 创建构建器，自动加载 `appsettings.json` 和对应环境的配置文件。

3. **静态配置访问**：`AppCore.cs` 中将 `IConfiguration` 实例保存为静态字段 `Configuration`，以便在整个应用中通过静态方式访问配置值。

4. **环境区分**：通过 `app.Environment.IsDevelopment()` 判断运行环境，控制 Swagger 等开发功能的启用。

5. **测试环境配置**：测试项目中通过 `Environment.GetEnvironmentVariable("DOTNET_ROOT")` 获取 .NET SDK 路径，体现环境变量在测试配置中的作用。

该配置系统相对简单，主要依赖 ASP.NET Core 内置的配置机制，没有自定义的配置提供程序或复杂的配置验证逻辑。