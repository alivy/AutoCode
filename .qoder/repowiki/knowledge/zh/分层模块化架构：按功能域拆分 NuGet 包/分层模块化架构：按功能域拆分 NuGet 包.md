---
kind: design
name: 分层模块化架构：按功能域拆分 NuGet 包
source: session
category: adr
---

# 分层模块化架构：按功能域拆分 NuGet 包

_来源：877c939 → 6e03419 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
AutoCode 需要支持接口生成、模板处理、映射、分析器、DI 注册、DTO、验证、Web API 控制器等多个功能域，每个领域都有独立的特性、生成器和测试需求。

## 决策驱动
- 单一职责原则
- 按需引用减少依赖
- 独立版本发布
- 并行开发能力

## 备选方案
- **单一大包包含所有功能** _（已否决）_ — 优点：部署简单，版本一致；缺点：包体积大，依赖耦合深，难以单独升级
- **按功能域拆分为独立 NuGet 包** — 优点：按需引用，独立演进，测试隔离；缺点：包管理复杂度增加，版本协调成本

## 决策
将 AutoCode 拆分为 AutoCode.Analyzers、AutoCode.DependencyInjection、AutoCode.Dto、AutoCode.Validation、AutoCode.WebApi、AutoCode.Cli 等独立项目，共享 AutoCode.Model 中的特性定义。

## 影响
解决方案包含 15+ 个项目，构建时间增加；但每个模块可独立测试、打包、发布，支持渐进式采用。