---
kind: configuration_system
name: 配置系统 — MSBuild 属性 + ASP.NET Core 配置文件双轨配置
category: configuration_system
scope:
    - '**'
source_files:
    - src/APP.WebAPI/appsettings.json
    - src/APP.WebAPI/appsettings.Development.json
    - src/APP.WebAPI/Program.cs
    - src/APP.WebAPI.Core/Application/AppCore.cs
    - src/APP.WebAPI.Core/Application/Extensions/ServiceCollectionExtensions.cs
    - src/AutoCode.Model/AutoCodeOptions.cs
    - src/AutoCode.Map/MapperGenerator.cs
---

本仓库的“配置”涉及两个层面：运行时应用配置（ASP.NET Core）与编译时代码生成器配置（MSBuild 属性）。两者职责分离、互不干扰。

一、运行时配置（ASP.NET Core）
- 配置文件位置：`src/APP.WebAPI/appsettings.json` 与 `src/APP.WebAPI/appsettings.Development.json`，采用标准 ASP.NET Core 分层覆盖机制（Development 环境覆盖默认配置）。
- 加载入口：`Program.cs` 通过 `WebApplication.CreateBuilder(args)` 创建构建器，随后调用扩展方法 `InitAPI()`；`AppCore.ConfigureApplication` 中将 `hostContext.Configuration` 赋值给静态字段 `AppCore.Configuration`，供后续组件以静态方式访问。
- 使用方式：当前示例仅启用 Logging 和 AllowedHosts，未自定义业务配置项；若需新增配置，可直接在 appsettings*.json 中添加键值，并通过 `IConfiguration` 或强类型绑定注入。
- 环境变量：测试代码中通过 `Environment.GetEnvironmentVariable("DOTNET_ROOT")` 读取环境变量，说明项目支持通过环境变量覆盖部分行为（如测试路径），符合 .NET 默认约定。

二、编译时代码生成器配置（MSBuild 属性）
- 配置集中定义：`AutoCode.Model/AutoCodeOptions.cs` 中的 `AutoCodeOptions` 类统一声明所有 MSBuild 属性名，前缀为 `build_property.AutoCode_`，包括：
  - `InterfacePrefix`：接口名前缀（默认 "I"）
  - `GenerateNullable`：是否生成可空注解（默认 true）
  - `MapMethodName`：映射方法名（默认 "CopyTo"）
  - `TemplateSuffix`：模板输出文件后缀（默认 ".generated.cs"）
  - `EnableDiagnostics`：是否启用分析器诊断（默认 true）
- 读取方式：各 SourceGenerator（如 `MapperGenerator`、`InterfaceGenerator`）通过 `context.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.AutoCode_<Key>", out var value)` 读取，并提供默认值回退。
- 配置来源：用户可在任意项目的 `.csproj` 文件中通过 `<PropertyGroup>` 设置这些属性，例如：<AutoCode_InterfacePrefix>I</AutoCode_InterfacePrefix>。该机制由 Roslyn IIncrementalGenerator 的 AnalyzerConfigOptionsProvider 提供，无需额外解析逻辑。

三、架构与约定
- 运行时配置与生成器配置严格分离：appsettings*.json 仅影响 Web API 运行期行为；MSBuild 属性仅影响编译期代码生成。
- 生成器配置采用“常量集中管理 + GlobalOptions 读取”模式，避免硬编码字符串散落各处。
- 应用启动流程中，`AppCore` 作为静态配置持有者，将 `IConfiguration` 暴露为静态字段，便于非依赖注入场景访问。

四、约束与规范
- 所有 AutoCode 生成器相关配置必须通过 `AutoCode_` 前缀的 MSBuild 属性传递，不得在 Generator 内部直接解析其他来源。
- 运行时配置遵循 ASP.NET Core 标准约定，环境特定配置通过 `appsettings.{Environment}.json` 覆盖。
- 环境变量仅用于测试等少数场景（如 DOTNET_ROOT），业务配置应优先放在 appsettings*.json 或通过 IConfiguration 注入。
