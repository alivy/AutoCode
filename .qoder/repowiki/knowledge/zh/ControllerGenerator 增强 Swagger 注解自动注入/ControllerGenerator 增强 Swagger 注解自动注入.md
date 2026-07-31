---
kind: design
name: ControllerGenerator 增强 Swagger 注解自动注入
source: session
category: adr
---

# ControllerGenerator 增强 Swagger 注解自动注入

_来源：c54abef → 9053202 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
生成的 Controller 缺少 [ProducesResponseType]、[Produces]、[ApiExplorerSettings] 等 Swagger/OpenAPI 注解，导致 API 文档不完整、Swagger UI 展示效果差。

## 决策驱动
- API 文档完整性
- Swagger UI 用户体验
- 零配置自动生成

## 备选方案
- **按 HTTP 方法自动推断响应码并添加 [ProducesResponseType]** — 优点：GET→200, POST/PUT/DELETE→200+400, ActionResult<T>→application/json
- **从 XML 注释提取 summary 作为 ApiExplorerSettings 描述** — 优点：复用已有注释，无需重复维护

## 决策
在 ControllerGenerator.cs 中为 GET 方法添加 [ProducesResponseType(typeof(T), 200)]，POST/PUT/DELETE 添加 [ProducesResponseType(200)] + [ProducesResponseType(400)]，ActionResult<T> 返回时添加 [Produces("application/json")]，并从 XML 注释提取 summary 作为 [ApiExplorerSettings] 描述。

## 影响
生成的 Controller 自带完整 OpenAPI 文档，Swagger UI 可直接展示参数和响应类型；无需手动编写任何 Swagger 注解。