# 配置参考

AutoCode 采用**三层配置系统**，按优先级从高到低：

1. **MSBuild 属性**（`.csproj` 中的 `<PropertyGroup>`）— 最高优先级
2. **autocode.json**（项目根目录 JSON 配置文件）— 中等优先级
3. **Attribute 参数**（代码中标记的特性参数）— 默认值

本文档列出 `autocode.json` 的全部配置节点。配置通过 `CompilerVisibleProperty` 传递给 Source Generator 在编译时读取。

---

## conventions — 约定推断

控制约定引擎如何按命名模式自动发现类型。

| 键 | 类型 | 默认值 | 说明 |
|----|------|--------|------|
| `servicePattern` | string | `"*Service"` | Service 类名匹配模式（支持 `*` 通配符） |
| `repositoryPattern` | string | `"*Repository"` | Repository 类名匹配模式 |
| `dtoSuffix` | string | `"Dto"` | DTO 类名后缀 |
| `autoDetectServices` | bool | `true` | 自动识别 Service 类 |
| `autoDetectDtos` | bool | `true` | 自动识别 DTO 类 |
| `autoDetectRepositories` | bool | `true` | 自动识别 Repository 类 |
| `autoDetectControllers` | bool | `false` | 自动识别 Controller 类（默认关闭，避免误判） |

```json
"conventions": {
  "servicePattern": "*Service",
  "repositoryPattern": "*Repository",
  "dtoSuffix": "Dto",
  "autoDetectServices": true,
  "autoDetectDtos": true,
  "autoDetectRepositories": true,
  "autoDetectControllers": false
}
```

## mapper — 对象映射（`[MapFrom]` / `[Mapper]`）

| 键 | 类型 | 默认值 | 说明 |
|----|------|--------|------|
| `methodName` | string | `"MapTo"` | 生成的映射扩展方法名 |
| `nullHandling` | string | `"Skip"` | null 值处理策略：`Skip`（跳过）/ `Copy`（复制）/ `Throw`（抛异常） |
| `collectionMapping` | string | `"DeepCopy"` | 集合映射方式：`DeepCopy`（深拷贝）/ `Reference`（引用） |
| `generateProjection` | bool | `false` | 是否生成 IQueryable 投影表达式 |

## dto — DTO 生成（`[AutoDTO]`）

| 键 | 类型 | 默认值 | 说明 |
|----|------|--------|------|
| `useRecord` | bool | `false` | 生成 record 类型而非 class |
| `excludeAuditFields` | bool | `true` | 自动排除审计字段（CreatedAt/CreatedBy/ModifiedAt/IsDeleted 等） |

## webapi — Controller 生成（`[AutoController]`）

| 键 | 类型 | 默认值 | 说明 |
|----|------|--------|------|
| `responseWrapper` | bool | `true` | 生成统一响应包装（`ApiResponse<T>`） |
| `pagination` | bool | `true` | 列表接口自动支持分页参数 |
| `versioning` | string | `"UrlSegment"` | API 版本控制方式：`UrlSegment` / `Header` / `None` |
| `version` | string | `""` | 默认 API 版本号（为空则不添加） |

## validation — 验证生成（`[AutoValidator]`）

| 键 | 类型 | 默认值 | 说明 |
|----|------|--------|------|
| `generateFluentStyle` | bool | `false` | 生成 Fluent 链式验证 API |
| `enableAsyncValidation` | bool | `false` | 生成异步验证方法 |

## dependencyInjection — DI 注册（`IScoped`/`ISingleton`/`ITransient`）

| 键 | 类型 | 默认值 | 说明 |
|----|------|--------|------|
| `namespace` | string | `"AutoCode.DependencyInjection"` | 生成的 DI 扩展类命名空间 |
| `methodName` | string | `"AddAutoDI"` | 生成的注册扩展方法名 |
| `modulePerAssembly` | bool | `false` | 每个程序集生成独立注册模块 |

## cascade — 级联生成（`[AutoEntity]`）

控制 `[AutoEntity]` 触发全链路生成时各产物的开关。

| 键 | 类型 | 默认值 | 说明 |
|----|------|--------|------|
| `dto` | bool | `true` | 生成 DTO |
| `mapper` | bool | `true` | 生成实体 ↔ DTO 映射 |
| `validation` | bool | `true` | 生成验证器 |
| `repository` | bool | `true` | 生成 Repository |
| `service` | bool | `true` | 生成 Service 接口与实现 |
| `controller` | bool | `true` | 生成 RESTful Controller |
| `tests` | bool | `false` | 生成测试桩 |
| `logging` | bool | `false` | 生成日志装饰器 |

## logging — 日志装饰器（`[AutoLog]`）

| 键 | 类型 | 默认值 | 说明 |
|----|------|--------|------|
| `structuredLogging` | bool | `true` | 使用结构化日志（`ILogger<T>` 消息模板） |
| `includeOpenTelemetry` | bool | `false` | 集成 OpenTelemetry Activity |
| `maskSensitive` | bool | `true` | 自动掩码 `[Sensitive]` 标记的参数（密码/令牌等） |

## intercept — AOP 拦截器（`[AutoIntercept]`）

| 键 | 类型 | 默认值 | 说明 |
|----|------|--------|------|
| `defaultInterceptors` | string | `"Log,Metrics"` | 类级别未指定时默认启用的拦截器（逗号分隔） |
| `cacheDurationSeconds` | int | `300` | Cache 拦截器默认缓存时长（秒） |
| `maxRetryCount` | int | `3` | Retry 拦截器默认最大重试次数 |
| `retryBaseDelayMs` | int | `100` | Retry 指数退避基础延迟（毫秒） |
| `circuitFailureThreshold` | int | `5` | CircuitBreaker 触发熔断的连续失败次数 |
| `circuitBreakDurationSeconds` | int | `30` | CircuitBreaker 熔断持续时间（秒） |

## plugins — 插件开关

按名称控制每个 V2 生成插件的启用状态。`enabled: false` 时该插件不生成任何代码。

```json
"plugins": {
  "interface": { "enabled": true },
  "mapper": { "enabled": true },
  "dto": { "enabled": true },
  "validation": { "enabled": true },
  "webapi": { "enabled": true },
  "crud": { "enabled": true },
  "dependencyInjection": { "enabled": true },
  "testing": { "enabled": true },
  "logging": { "enabled": true },
  "cascade": { "enabled": true },
  "intercept": { "enabled": true }
}
```

> **注意**：V2 生成器还受全局开关 `AutoCode_EnableV2`（MSBuild 属性）控制，默认关闭。详见 [V1/V2 迁移指南](v1-v2-migration.md)。

## customGenerators — 自定义生成配方（V2.2+）

定义自己的代码生成规则，无需编写 Source Generator。完整说明见 README「自定义代码生成配方」章节。

| 键 | 类型 | 说明 |
|----|------|------|
| `name` | string | 配方唯一标识（用于 `[CustomGenerate("name")]` 触发） |
| `title` | string | Ctrl+. 菜单中显示的标题 |
| `icon` | string | 菜单图标（emoji） |
| `category` | string | 分类（Service/Entity 等） |
| `trigger.classPattern` | string | 类名匹配模式（隐式触发） |
| `trigger.attributeName` | string | 特性名（显式触发） |
| `trigger.requiredProperties` | string[] | 要求类必须具有的属性 |
| `output.template` | string | Liquid 模板文件路径 |
| `output.fileName` | string | 生成文件名（支持 `{ClassName}` 等变量） |
| `output.namespace` | string | 生成代码的命名空间（支持变量） |

## 完整示例

仓库根目录的 [autocode.json](../autocode.json) 即为一份完整可用的配置示例。

## MSBuild 属性映射

部分配置也可通过 MSBuild 属性设置（优先级高于 JSON）：

| MSBuild 属性 | 对应 JSON 配置 | 说明 |
|--------------|----------------|------|
| `AutoCode_EnableV2` | — | V2 生成器总开关（默认 false） |
| `AutoCode_InterfacePrefix` | — | 接口名前缀（默认 `I`） |
| `AutoCode_MapMethodName` | `mapper.methodName` | 映射方法名 |
| `AutoCode_GenerateNullable` | — | 生成代码启用可空注解 |
| `AutoCode_Dto_UseRecord` | `dto.useRecord` | DTO 生成 record |
| `AutoCode_Dto_ExcludeAudit` | `dto.excludeAuditFields` | 排除审计字段 |
| `AutoCode_WebApi_ResponseWrapper` | `webapi.responseWrapper` | 统一响应包装 |
| `AutoCode_WebApi_Version` | `webapi.version` | API 版本 |
| `AutoCode_WebApi_Pagination` | `webapi.pagination` | 分页支持 |
| `AutoCode_DI_Namespace` | `dependencyInjection.namespace` | DI 命名空间 |
| `AutoCode_DI_MethodName` | `dependencyInjection.methodName` | DI 方法名 |
| `AutoCode_DI_ModulePerAssembly` | `dependencyInjection.modulePerAssembly` | 按程序集分模块 |

使用 MSBuild 属性时，需确保已声明 `CompilerVisibleProperty` 才能传递给生成器：

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="AutoCode_EnableV2" />
  <CompilerVisibleProperty Include="AutoCode_MapMethodName" />
</ItemGroup>
```
