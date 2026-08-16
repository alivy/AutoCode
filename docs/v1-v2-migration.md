# V1 / V2 生成器迁移指南

AutoCode 2.2.0 将原本分散在 40 个项目中的生成器合并为统一的 `AutoCode.Generators` 项目，同时保留了两套实现：

- **V1 生成器**（`AutoCode.Generators/V1/`）：经典实现，功能稳定，**默认启用**
- **V2 生成器**（`AutoCode.Generators/V2/`）：基于新引擎（Pipeline + CodeBuilder）的插件化实现，**默认关闭**

本文档说明两者的差异、选择依据与迁移方法。

---

## 为什么默认仅运行 V1？

V1 与 V2 生成器**监听相同的特性**（如 `[AutoInterface]`、`[AutoDTO]`、`[AutoValidator]`）。若两套同时运行，同一类型会被生成两次，导致：

```
error CS0101: 命名空间"MyApp.Models"已经包含"UserDto"的定义
```

> **历史背景**：2.2.0 合并后，V2 生成器因依赖程序集（`AutoCode.Engine`）未正确传递给编译器而**静默失效**（CS8785），恰好掩盖了重复生成问题。2.3.1 修复依赖传递后，V2 真正生效，重复生成问题随之暴露，因此引入 `AutoCode_EnableV2` 开关将 V2 默认关闭。

## V1 与 V2 功能对照

| 特性 | V1 | V2 | 说明 |
|------|----|----|------|
| `[AutoInterface]` | ✅ 默认运行 | 需启用 | 接口生成 |
| `[Mapper]` | ✅ 默认运行 | — | 同类型 CopyTo 映射 |
| `[MapFrom]` | — | 需启用 | **V2 独有**：跨类型映射 + `[MapProperty]` 自定义映射 |
| `[AutoDTO]` | ✅ 默认运行 | 需启用 | DTO 生成 |
| `[AutoValidator]` | ✅ 默认运行 | 需启用 | 编译时验证 |
| `[AutoController]` | ✅ 默认运行 | 需启用 | RESTful Controller 生成 |
| `IScoped/ISingleton/ITransient` | ✅ 默认运行 | 需启用 | DI 自动注册 |
| `[AutoTest]` | ✅ 默认运行 | 需启用 | 测试桩生成 |
| `[AutoLog]` | ✅ 默认运行 | 需启用 | 日志装饰器 |
| `[AutoCrud]` | ✅ 默认运行 | 需启用 | CRUD 一键生成 |
| `[AutoEntity]` | — | 需启用 | **V2 独有**：级联全链路生成 |
| `[AutoIntercept]` | ✅ 默认运行 | — | **V1 独有**：编译时 AOP 拦截器 |
| `[DotTemplate]` | ✅ 默认运行 | — | **V1 独有**：DotLiquid 模板生成（独立项目） |

**关键结论：**

- **V1 独有**：`[Mapper]`、`[AutoIntercept]`（AOP）、`[DotTemplate]`（模板）
- **V2 独有**：`[MapFrom]`（跨类型映射）、`[AutoEntity]`（级联生成）
- **两者重叠**：其余 8 个特性，默认由 V1 处理

## 如何选择

### 场景 1：只使用 V1 功能（推荐，默认配置）

如果你使用的特性都在 V1 覆盖范围内（接口/DTO/验证/Controller/DI/日志/测试/CRUD/AOP/模板），**无需任何配置**，保持默认即可。

### 场景 2：需要 V2 独有功能（`[MapFrom]` / `[AutoEntity]`）

启用 V2：

```xml
<PropertyGroup>
  <AutoCode_EnableV2>true</AutoCode_EnableV2>
</PropertyGroup>
<ItemGroup>
  <CompilerVisibleProperty Include="AutoCode_EnableV2" />
</ItemGroup>
```

**注意事项**：启用 V2 后，对于 V1/V2 重叠的特性（`[AutoInterface]`、`[AutoDTO]` 等），两套生成器会同时运行并产生 CS0101 重复定义错误。当前版本建议：

- 若项目同时使用 `[AutoInterface]`（V1 处理）和 `[AutoEntity]`（V2 处理），启用 V2 后 `[AutoEntity]` 标记的类会由 V2 全链路生成，而 `[AutoInterface]` 标记的类会被 V1+V2 同时处理导致冲突
- **规避方法**：启用 V2 的项目中，重叠特性尽量统一使用一套标记风格，或通过 `autocode.json` 的 `plugins.*.enabled` 精细关闭不需要的 V2 插件

```json
{
  "plugins": {
    "interface": { "enabled": false },  // 关闭 V2 接口生成，避免与 V1 冲突
    "dto": { "enabled": false },
    "cascade": { "enabled": true }      // 仅保留 V2 级联生成
  }
}
```

> **后续版本规划**：V1 重叠功能的生成器将逐步增加独立开关，最终实现 V1/V2 完全可配置互斥。

## 配置优先级总结

V2 生成器是否实际运行，由两层开关共同决定（**AND 关系**）：

1. **全局开关**：MSBuild 属性 `AutoCode_EnableV2`（默认 `false`）
2. **插件开关**：`autocode.json` 中 `plugins.<名称>.enabled`（默认 `true`）

即：只有全局启用 V2 **且**对应插件未在 JSON 中禁用时，V2 插件才会生成代码。

## 迁移检查清单

从 2.2.0 及更早版本升级到 2.3.1+ 时：

- [ ] 确认项目没有显式设置 `AutoCode_EnableV2=true`（除非确实需要 V2 功能）
- [ ] 若出现 CS8785 警告 → 升级到 2.3.1（依赖传递已修复）
- [ ] 若出现 CS0101 重复定义 → 检查是否误启用了 V2
- [ ] 清理构建缓存：`dotnet build-server shutdown` 后 `dotnet build -t:Rebuild`
- [ ] 若通过 ProjectReference 源码方式引用旧拆分项目（`AutoCode.Map`、`AutoCode.Plugins.*` 等），迁移到统一的 `AutoCode.Generators`：

```xml
<!-- 旧方式（2.2.0 前，已废弃） -->
<ProjectReference Include="..\AutoCode.Map\AutoCode.Map.csproj" />

<!-- 新方式（统一引用） -->
<ProjectReference Include="..\AutoCode.Generators\AutoCode.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## 相关文档

- [配置参考](configuration.md) — 全部配置项说明
- [故障排除](troubleshooting.md) — 常见问题解决方案
- [CHANGELOG](../CHANGELOG.md) — 各版本变更详情
