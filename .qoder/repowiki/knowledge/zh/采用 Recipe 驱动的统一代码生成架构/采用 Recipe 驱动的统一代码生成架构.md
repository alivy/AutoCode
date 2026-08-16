---
kind: adr
name: 采用 Recipe 驱动的统一代码生成架构
slug: adr
category: adr
---

# 采用 Recipe 驱动的统一代码生成架构

_来源：ae35db1 → 3d78fee 提交周期内记录的编码计划——内容为规划时意图，实现可能滞后或有出入。_

**状态：** accepted

## 背景
现有每个生成器是独立的 IIncrementalGenerator，硬编码触发 Attribute 名；AutoCodeRefactoringProvider 硬编码了 11 个生成器的所有推荐逻辑。新增一个自定义生成器需要改 3 处：Generator + Attribute + RefactoringProvider，用户无法定义自己的代码生成规则。

## 决策驱动
- 声明式配置优先于硬编码
- 一份配置两端共享（Source Generator + IDE）
- 用户可自定义生成规则无需修改框架代码

## 备选方案
- **Recipe 驱动架构（JSON 配置 + DotLiquid 模板）** — 优点：声明式、可扩展、Source Generator 与 RefactoringProvider 共享同一份 autocode.json 配置、支持隐式匹配和显式标记两种用法
- **保持现有独立 Generator + 硬编码模式** _（已否决）_ — 优点：实现简单直接；缺点：每新增一个生成器需改三处代码、无法让用户自定义规则、扩展性差

## 决策
在 AutoCode.Model 中定义 CodeGenRecipe/RecipeTrigger/RecipeOutput 模型，通过 autocode.json 的 customGenerators 节声明配方；使用 CustomGenerateAttribute 作为快捷标记；AutoCode.Generators/CustomRecipeGenerator 作为统一 Source Generator 处理所有配方；AutoCode.Analyzers 中的 ICodeGenRecipe 接口统一内置 11 个生成器和自定义配方的推荐逻辑。

## 影响
新增生成器只需添加 JSON 配置和 .liquid 模板文件，无需修改框架代码。但需要维护两份相同的 Recipe 模型（Model 层用于编译时，Analyzers 层用于 IDE），且 DotLiquid 模板语法对用户有一定学习成本。