---
kind: adr
name: 使用 Fluent CodeBuilder 替代 StringBuilder 代码生成
slug: adr
category: adr
---

# 使用 Fluent CodeBuilder 替代 StringBuilder 代码生成

_来源：c54abef → 86501d8 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
现有代码生成大量使用 StringBuilder 硬拼字符串，导致代码可读性差、易出错、难以维护。需要一种类型安全的代码构建方式。

## 决策驱动
- 代码可读性
- 类型安全
- 维护性
- IDE 支持

## 备选方案
- **Fluent API CodeBuilder（CodeWriter/ClassBuilder/MethodBuilder）** — 优点：强类型、IDE 智能提示、自动缩进管理、支持 using 收集、region 分组
- **模板引擎（T4/Razor/Scriban）** _（已否决）_ — 优点：适合复杂模板场景；缺点：学习成本高、调试困难、不适合细粒度代码片段生成
- **继续改进 StringBuilder 封装** _（已否决）_ — 优点：改动最小；缺点：无法解决根本问题、仍缺乏类型安全

## 决策
在 AutoCode.Engine/CodeBuilder 中实现完整的 Fluent API 代码构建器，包括 CodeWriter、ClassBuilder、MethodBuilder、PropertyBuilder、ExpressionBuilder 等组件，提供链式 API 构建 C# 代码。

## 影响
所有插件必须迁移到新的 CodeBuilder API；学习曲线较陡但长期收益显著；支持更复杂的代码结构生成；与 Roslyn 集成更好。