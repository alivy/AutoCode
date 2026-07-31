---
kind: design
name: 引入三大特性驱动的新增生成器：AutoTest / AutoLog / AutoCrud
source: session
category: adr
---

# 引入三大特性驱动的新增生成器：AutoTest / AutoLog / AutoCrud

_来源：c54abef → 9053202 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
项目需要快速生成测试桩、日志装饰器和 CRUD 样板代码，减少重复编码工作，提升开发效率。

## 决策驱动
- 代码生成效率
- 模式一致性
- 模块化扩展能力

## 备选方案
- **AutoTest：基于 [AutoTest] 特性生成 xUnit 测试桩** — 优点：每个方法自动生成基础测试骨架，包含 Arrange/Act/Assert 结构
- **AutoLog：基于 [AutoLog] 特性生成 Decorator Pattern 日志包装类** — 优点：统一日志格式，自动记录开始/结束/异常/耗时，符合 AOP 思想
- **AutoCrud：基于 [AutoCrud] 特性一键生成 Service + Interface + Controller** — 优点：标准 CRUD 操作模板化，内存实现可替换为 EF Core

## 决策
创建三个独立项目：AutoCode.Testing（TestGenerator.cs）、AutoCode.Logging（LogDecoratorGenerator.cs）、AutoCode.Crud（CrudGenerator.cs），每个对应一个 Attribute 模型（AutoTestAttribute/AutoLogAttribute/AutoCrudAttribute），遵循统一的特性驱动生成模式。

## 影响
新增三个 NuGet 包，每个生成器独立可交付；测试覆盖 xUnit 框架约定，日志装饰器遵循 ILogger 抽象，CRUD 生成内存实现便于快速原型验证。