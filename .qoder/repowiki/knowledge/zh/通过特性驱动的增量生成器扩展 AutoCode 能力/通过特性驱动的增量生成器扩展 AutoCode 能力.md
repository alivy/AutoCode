---
kind: design
name: 通过特性驱动的增量生成器扩展 AutoCode 能力
source: session
category: adr
---

# 通过特性驱动的增量生成器扩展 AutoCode 能力

_来源：3d78fee → 03fd2a2 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
AutoCode 已有 7 个生成器和 3 个分析器，但面对测试、日志、CRUD 等重复性代码仍依赖手工编写或外部工具；需要在不侵入业务代码的前提下提供声明式增强。

## 决策驱动
- 零侵入（仅靠特性标记）
- 每个生成器独立可交付
- 与现有 Interface/Controller 生成管线一致

## 备选方案
- **基于特性的源码生成器（[AutoTest]/[AutoLog]/[AutoCrud]）** — 优点：声明式、编译期生成、无需运行时反射、与 IDE 集成良好
- **运行时 AOP 拦截（如 Castle DynamicProxy / AspectCore）** _（已否决）_ — 优点：无需修改源码；缺点：引入运行时开销、调试困难、对泛型/值类型支持有限
- **T4 / Razor 模板在构建时执行** _（已否决）_ — 优点：灵活；缺点：非标准 .NET 生态、IDE 体验差、难以单元测试

## 决策
为每个新增能力创建独立的 NuGet 包（AutoCode.Testing、AutoCode.Logging、AutoCode.Crud），通过自定义 Attribute + Roslyn Source Generator 在编译期生成代码，保持与现有 InterfaceGenerator/ControllerGenerator 相同的扩展点。

## 影响
项目结构从单一生成器演化为多包插件体系；每个新能力需配套 Model 特性、Generator 实现和单元测试；后续新增功能遵循同一模式即可复用基础设施。