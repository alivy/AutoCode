---
kind: design
name: ControllerGenerator 自动注入 Swagger 元数据
source: session
category: adr
---

# ControllerGenerator 自动注入 Swagger 元数据

_来源：3d78fee → 03fd2a2 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
原有 Controller 生成器只产出基础 REST 动作，缺少 `[ProducesResponseType]`、`[Produces]`、`[ApiExplorerSettings]` 等 Swagger 注解，导致 API 文档不完整、客户端 SDK 生成不准确。

## 决策驱动
- API 文档完整性
- OpenAPI/Swagger 兼容性
- 零额外配置

## 备选方案
- **根据 HTTP 动词与方法返回类型自动生成 `[ProducesResponseType]` 与 `[Produces]`，并从 XML summary 提取描述** — 优点：开箱即用，文档与代码同步更新
- **要求开发者手动添加 Swagger 注解** _（已否决）_ — 优点：完全可控；缺点：易遗漏、维护成本高
- **运行时扫描生成 OpenAPI 文档** _（已否决）_ — 优点：无需源码生成；缺点：无法覆盖未执行路径，生成质量不稳定

## 决策
在 `ControllerGenerator` 中按 GET/POST/PUT/DELETE 分别注入对应的 `[ProducesResponseType]`，对 `ActionResult<T>` 返回类型附加 `[Produces("application/json")]`，并将 XML 摘要映射为 `[ApiExplorerSettings]` 描述。

## 影响
Swagger UI 与客户端 SDK 生成不再需要手工补充注解；若业务方法返回复杂联合状态码，仍需手动覆盖默认行为。