---
kind: design
name: InterfaceGenerator 保留异步语义并继承 XML 文档与 Nullable 注解
source: session
category: adr
---

# InterfaceGenerator 保留异步语义并继承 XML 文档与 Nullable 注解

_来源：3d78fee → 03fd2a2 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
原 `InterfaceGenerator` 将 `public async Task<UserDto> GetByIdAsync(int id)` 等方法降级为同步签名，且丢失类方法上的 `/// <summary>` 注释，也不区分 `string` 与 `string?`，导致生成的接口契约失真。

## 决策驱动
- 接口契约准确性
- IDE 智能提示质量
- 向后兼容（MSBuild 开关控制）

## 备选方案
- **检测 Task/ValueTask 返回类型并在 MethodSpec 中保留 IsAsync 标记** — 优点：接口签名精确反映异步契约，调用方可正确 await
- **通过 DeclaringSyntaxReferences 提取 LeadingTrivia 中的 XML 文档并输出到接口** — 优点：XML 注释即契约文档，无需重复书写
- **使用 SymbolDisplayFormat.NullableFlowState 输出 string? 等可空注解，并由 AutoCode_GenerateNullable MSBuild 属性控制** — 优点：启用 nullable 的项目获得更精确的接口
- **强制所有接口方法改为同步包装** _（已否决）_ — 优点：实现简单；缺点：破坏异步契约，调用方仍需手动包装

## 决策
在 `InterfaceGenerator.GetPublicMethods` 中识别 `Task<T>`/`ValueTask<T>`/`Task`/`ValueTask` 并保留 `Task<T>` 返回类型；通过语法树提取 XML 文档；按 MSBuild 配置输出 Nullable 注解。

## 影响
生成的接口现在能准确表达异步与可空语义，减少下游调用方的适配成本；但需要确保引用项目的 nullable 设置与生成配置一致，否则可能产生警告。