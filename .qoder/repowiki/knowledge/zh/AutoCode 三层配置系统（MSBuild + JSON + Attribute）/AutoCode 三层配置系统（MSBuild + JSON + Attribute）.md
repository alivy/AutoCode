---
kind: configuration_system
name: AutoCode 三层配置系统（MSBuild + JSON + Attribute）
category: configuration_system
scope:
    - '**'
source_files:
    - src/AutoCode.Engine/Config/AutoCodeConfig.cs
    - src/AutoCode.Engine/Config/ConfigRecommender.cs
    - src/autocode.json
    - src/AutoCode.Cli/Program.cs
    - scripts/install-autocode.ps1
---

## 系统概述

AutoCode 实现了一套**三层合并配置系统**，优先级从高到低为：**Attribute > autocode.json > MSBuild 全局属性**。所有生成器通过统一的 `IAutoCodeConfig` 接口访问配置，支持字符串、布尔、整数、枚举、数组及分段配置读取。

## 核心架构

### 配置层与合并策略
- **MSBuild 层**：通过 `build_property.AutoCode_*` 前缀的 MSBuild 属性注入，由 `AutoCodeConfig.FromMSBuild()` 加载
- **JSON 文件层**：项目根目录 `autocode.json`，使用内置轻量 JSON 解析器（netstandard2.0 兼容），扁平化为 `dot.separated` 键名
- **Attribute 层**：编译时通过特性参数覆盖，调用 `Set(key, value)` 设置最高优先级值
- 合并通过 `AutoCodeConfig.Merge()` 按顺序叠加，后者覆盖前者

### 配置接口设计
`IAutoCodeConfig` 提供类型安全的访问方法：
- `GetString/GetBoolean/GetInt/GetEnum<T>` — 基础类型读取，带默认值
- `GetStringArray` — 逗号或分号分隔的字符串数组
- `GetSection(sectionName)` — 获取子配置节（如 `mapper.GetSection("nullHandling")`）
- `HasKey/GetKeys` — 配置项探测与遍历

### 配置文件结构
`autocode.json` 包含以下配置节：
- `conventions` — 命名约定（Service/Repository/Dto 后缀、自动检测开关）
- `mapper` — 映射器行为（方法名、空值处理、集合映射策略）
- `dto` — DTO 生成选项（record 模式、审计字段排除）
- `webapi` — Web API 行为（响应包装、分页、版本控制）
- `validation` — 验证器生成（FluentValidation 风格、异步验证）
- `dependencyInjection` — DI 注册（命名空间、方法名、模块隔离）
- `cascade` — 级联生成开关（每个插件可独立启用/禁用）
- `logging` — 结构化日志、OpenTelemetry、敏感信息脱敏
- `intercept` — AOP 拦截器（默认拦截器列表、缓存/重试/熔断参数）
- `plugins` — 各插件总开关（interface/mapper/dto/validation/webapi/crud/di/testing/logging/cascade/intercept）

### 智能推荐引擎
`ConfigRecommender` 分析项目结构和依赖，自动生成配置建议：
- EF Core → 建议开启 CRUD + Repository
- ASP.NET Core → 建议 Controller + 统一响应
- 测试框架 → 建议测试生成
- 多项目结构 → 建议 Mapper + DTO
- ILogger/缓存使用 → 建议 AutoIntercept(Log/Metrics/Cache)
- AutoMapper → 建议迁移到编译时映射
- 手动 DI → 建议编译时 DI

### CLI 与初始化
- `dotnet autocode init` — 在项目根目录创建 `autocode.json` 和 `Templates/` 目录
- `install-autocode.ps1` — 一键安装脚本，自动添加 NuGet 引用、生成配置、可选创建示例实体
- 环境诊断命令检查 .NET SDK、配置文件、NuGet 引用、.editorconfig

### 约定与约束
- 配置键采用 `PascalCase` 属性名自动转换为 `dot.separated` 小写键（如 `InterfacePrefix` → `interface.prefix`）
- 布尔值支持 `true/1/yes` 三种形式
- 数组值支持逗号或分号分隔
- JSON 解析器为极简实现，仅支持基本类型、嵌套对象和数组，不依赖 System.Text.Json
- 所有配置键对大小写不敏感（`StringComparer.OrdinalIgnoreCase`）
- 未找到配置项时返回默认值而非抛出异常

## 关键文件
- `src/AutoCode.Engine/Config/AutoCodeConfig.cs` — 三层配置合并核心实现
- `src/AutoCode.Engine/Config/ConfigRecommender.cs` — 智能配置推荐引擎
- `src/autocode.json` — 项目级配置文件模板
- `src/AutoCode.Cli/Program.cs` — CLI 工具（init/check/templates 命令）
- `scripts/install-autocode.ps1` — 一键安装脚本
- `src/APP.WebAPI.Core/Application/AppCore.cs` — 运行时 IConfiguration 集成点