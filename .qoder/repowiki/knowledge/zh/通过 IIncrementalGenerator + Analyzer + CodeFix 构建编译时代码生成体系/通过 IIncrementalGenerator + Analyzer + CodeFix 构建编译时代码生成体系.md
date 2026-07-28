---
kind: design
name: 通过 IIncrementalGenerator + Analyzer + CodeFix 构建编译时代码生成体系
source: session
category: adr
---

# 通过 IIncrementalGenerator + Analyzer + CodeFix 构建编译时代码生成体系

_来源：877c939 → 6e03419 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
AutoCode 需要从单纯的代码生成工具升级为开发体验工具，当前已有 3 个 IIncrementalGenerator（Interface/DotTemplate/Map），需要扩展为包含诊断、快速修复、配置管理、依赖注入注册、DTO/Validator/Controller 生成的完整生态。

## 决策驱动
- 编译时代码生成性能优于运行时反射
- 统一的分析器诊断提升开发体验
- MSBuild 配置支持用户自定义行为
- 增量生成器避免重复工作

## 备选方案
- **IIncrementalGenerator + Roslyn Analyzer + CodeFix** — 优点：编译时执行零运行时开销，IDE 原生集成，增量编译高效
- **T4 模板引擎** _（已否决）_ — 优点：简单直观；缺点：运行时执行，无 IDE 集成，无法提供实时诊断
- **运行时反射扫描** _（已否决）_ — 优点：无需编译期工具链；缺点：启动慢，类型安全差，无法在 IDE 中发现问题

## 决策
采用 IIncrementalGenerator 作为核心代码生成机制，配合 Microsoft.CodeAnalysis.CSharp.Workspaces 实现 Analyzer 和 CodeFix，所有生成器统一通过 AutoCodeOptions 读取 MSBuild 配置。

## 影响
需要维护多个独立项目（Analyzers/DependencyInjection/Dto/Validation/WebApi/Cli），每个都遵循相同的特性+生成器模式；NuGet 包结构复杂但功能模块化清晰。