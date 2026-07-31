---
kind: design
name: InterfaceGenerator 增强：Async/Nullable/XML 文档感知
source: session
category: adr
---

# InterfaceGenerator 增强：Async/Nullable/XML 文档感知

_来源：c54abef → 9053202 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
现有 InterfaceGenerator 生成的接口签名丢失 async 语义（Task<T> 被降级）、不区分可空类型、且不会继承源类的 XML 文档注释，导致生成代码质量低于手写水平。

## 决策驱动
- 接口签名准确性
- C# 可空引用类型兼容性
- IDE 智能提示与文档体验

## 备选方案
- **在 InterfaceSpec.MethodSpec 中添加 IsAsync 标记并保留 Task<T>** — 优点：最小改动，保持接口简洁（接口不需要 async 关键字）
- **使用 SymbolDisplayFormat.NullableFlowState 输出可空注解** — 优点：与编译器行为一致，支持 MSBuild 配置开关
- **通过 method.DeclaringSyntaxReferences.GetLeadingTrivia() 提取 XML 注释** — 优点：无需额外元数据，直接从语法树获取

## 决策
改造 InterfaceGenerator.cs 的 GetPublicMethods：检测 Task/ValueTask 返回类型并设置 IsAsync；使用 SymbolDisplayFormat 输出可空注解；从 DeclaringSyntaxReferences 提取 XML 文档注入 MethodSpec.XmlDoc。

## 影响
生成的接口能正确反映异步方法和可空性，IDE 中显示完整的 XML 文档；新增 AutoCode_GenerateNullable MSBuild 属性控制是否输出可空注解。