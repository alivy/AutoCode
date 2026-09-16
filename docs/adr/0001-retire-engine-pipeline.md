# ADR 0001：退役 Engine 管线体系（Pipeline / Plugin / Hook）

- 状态：建议（Proposed）
- 日期：2026-08-19
- 决策范围：`AutoCode.Engine` 的 Pipeline / Plugin / Roslyn 基类 / Convention / ConfigRecommender
- 影响版本：计划随 v3.0 落地（允许 breaking change）

---

## 1. 背景

README 将"V2 插件化引擎 + Pipeline 管线执行"列为核心架构亮点，宣称 11 个生成插件经管线按优先级 + 依赖拓扑排序执行，支持错误隔离与 Hook 扩展。

本次代码审计（2026-08）对该叙事进行了事实核查，结论是：**管线体系从未接入真实编译路径，属于死代码**。

## 2. 证据（代码事实）

### 2.1 管线零消费方

全解决方案检索 `new GenerationPipeline`、`AddPlugin`、`AddHook`、`IPipelineHook`：

- 仅出现在 [GenerationPipeline.cs](../../src/AutoCode.Engine/Pipeline/GenerationPipeline.cs) 自身定义中；
- **没有任何生成器、测试、CLI 实例化或执行过管线**；
- [GeneratorTests.cs](../../src/AutoCode.Tests.V2/GeneratorTests.cs) 直接 `new` 具体生成器跑 `CSharpGeneratorDriver`，按"独立生成器"模型测试，从未测过管线。

### 2.2 唯一的"插件"是空壳

[InterceptPlugin.cs](../../src/AutoCode.Generators/V2/InterceptPlugin.cs)（管线体系在 Engine 之外的唯一实现）：

```csharp
public override IEnumerable<GeneratedFile> Generate(GenerationContext context)
{
    // 实际生成逻辑由 InterceptGenerator (IIncrementalGenerator) 独立完成。
    // 此插件主要用于管线注册、配置控制和 Hook 扩展。
    yield break;   // ← 空实现
}
```

它承认：真正的生成走的是独立 `IIncrementalGenerator`，插件存在仅为"注册进一条从未运行的管线"。

### 2.3 V2 的 11 个生成器全部是独立 IIncrementalGenerator

逐一确认 `src/AutoCode.Generators/V2/` 下 11 个生成器（Interface/Dto/Mapper/Validation/Controller/DI/Testing/Logging/Crud/Cascade/Intercept）均直接实现 `IIncrementalGenerator`，**无一继承** Engine 的 `AutoCodeIncrementalGenerator` 基类（该基类零继承者）。

### 2.4 Engine 组件消费清单

| 组件 | 真实消费方 | 判定 |
|---|---|---|
| CodeBuilder（CodeWriter/ClassBuilder/MethodBuilder/PropertyBuilder） | 6+ 个 V2 生成器 | ✅ 保留，核心价值 |
| Template（SimpleTemplateEngine/TemplateContext） | CustomRecipeGenerator | ✅ 保留 |
| Config（AutoCodeConfig） | 仅基类内部 + MapperGenerator | ⚠️ 随基类重新评估 |
| Diagnostics（DiagnosticCollector） | 仅基类内部 | ⚠️ 随基类重新评估 |
| Roslyn（AutoCodeIncrementalGenerator 基类，236 行） | **零继承者** | ❌ 删除 |
| Pipeline（GenerationPipeline/GenerationContext/GeneratedFile/PluginTrigger，约 550 行） | 仅空壳 InterceptPlugin | ❌ 删除 |
| Plugin（IGenerationPlugin/GenerationPluginBase/AutoCodePluginAttribute，119 行） | 仅空壳 InterceptPlugin | ❌ 删除 |
| Convention（ConventionEngine，328 行） | **零使用方** | ❌ 删除 |
| Config（ConfigRecommender，225 行） | **零使用方**（V2.1 交付后从未接入 CLI/安装脚本） | ❌ 删除或接入 CLI `doctor` |

死代码合计约 **1450 行**，约占 Engine 项目的 55%。

### 2.5 活着的扩展机制已存在于别处

`AutoCode.Analyzers` 中已有真实运行的配方体系：`ICodeGenRecipe` + `BuiltInRecipes`（11 个内置配方）+ `CustomRecipeAdapter`（autocode.json 自定义配方），被 `AutoCodeRefactoringProvider`（Ctrl+.）真实消费。加上 V2.3 的 `customGenerators`（声明式 Liquid 模板生成），**项目实际已有两套活着的扩展机制**——Engine 的"插件管线"是第三套、且是唯一没人用的。

## 3. 技术分析：为什么"统一管线"与 Roslyn 增量模型存在根本张力

即使投入成本真接线，管线提供的四项核心能力在 Roslyn 模型下均不成立或重复：

1. **依赖拓扑排序 / PreviousOutputs（跨插件数据流）——解决的是不存在的问题。**
   Roslyn 中，生成器 A 的产出在同一轮编译中对生成器 B **不可见**（生成物不参与 Compilation 输入）。所谓"Cascade 依赖 DTO 插件的产物"在编译期不可能发生——CascadeGenerator 是自己一次性生成全链路的。管线的依赖调度机制对应的是一个 Roslyn 模型里不存在的场景。

2. **统一调度——会压扁增量缓存图。**
   当前 11 个独立生成器各有独立 `SyntaxProvider`，Roslyn 可按类型粒度缓存。若合并为 Hub 生成器驱动管线（"全量 context 进、文件列表出"），任何标记类的变更都会使整个管线成为一个黑盒节点：要么全缓存、要么全失效。要保持增量性需在管线内重建一套按插件 × 类型分片的调度——复杂度远超收益，且这正是 Roslyn 已经免费提供了的能力。

3. **错误隔离——重复造轮子。**
   Roslyn 编译器宿主天然隔离各生成器异常（单个生成器崩溃不影响其余，IDE 显示黄色警告条）。管线的 try/catch 隔离是重复实现。

4. **Hook / TransformOutput——全局后处理与增量性冲突。**
   统一格式化等需求可在各生成器 `AddSource` 前以共享静态方法完成；全局 Transform 钩子只会让缓存键设计复杂化。

5. **第三方插件发现——自研机制劣于 Roslyn 原生机制。**
   `AutoCodePluginAttribute` 程序集级发现需要在编译器/IDE 进程中反射扫描程序集，面临 AnalyzerLoadContext、VS 进程缓存、程序集冲突等已知坑。而 Roslyn 原生路线下，"第三方插件"就是"再发布一个带 `[Generator]` 的 NuGet 包"——生态成熟、零框架成本。真正的代码级扩展者应复用 **CodeBuilder**（Engine 被验证最有价值的部分）编写自己的生成器。

## 4. 候选方案评估

### 方案 A：真接线（Hub 生成器 + 11 个生成器改造为 IGenerationPlugin）

- 工作量：5~7 天（生成器改造 + Hub 增量设计 + 插件发现 + 测试重写）
- 收益：**负**。增量缓存退化；错误隔离与 Roslyn 重复；跨插件数据流场景不存在；第三方发现机制自研成本高。
- 结论：**否决**。

### 方案 B：真删除（推荐）

- 删除：Pipeline、Plugin、Roslyn 基类、Convention、ConfigRecommender（或将其逻辑并入 CLI `doctor`）、InterceptPlugin 空壳；共约 1450 行。
- 保留并强化：CodeBuilder、Template、（按需精简后的）Config。
- 叙事修正：README 架构章节从"插件化管线"改为"**生成器矩阵 + Recipe 配方扩展 + CodeBuilder 共享基建**"。
- 扩展路线对齐：
  - 声明式扩展 → `customGenerators` 配方（已存在，低门槛）；
  - IDE 交互扩展 → `ICodeGenRecipe`（已存在，Analyzers 中真实运行）；
  - 代码级扩展 → 独立 SG 项目 + 复用 `AutoCode.Engine.CodeBuilder`（未来以独立 NuGet 发布 CodeBuilder，即兑现 README"插件 SDK"承诺的正确形态）。
- 工作量：0.5~1 天（删除 + README/CHANGELOG 同步 + 编译验证）。
- 结论：**采纳**。

### 方案 C：保留为"实验性 API"

- 继续承担约 1450 行死代码的维护面、包体积与新成员认知负担；叙事不诚实问题依旧。
- 结论：**否决**。

## 5. 决策

**采纳方案 B：随 v3.0 退役 Engine 管线体系。**

核心原则：**AutoCode 的架构叙事收敛为"生成器矩阵 + Recipe 配方 + CodeBuilder 基建"三层；任何"插件化"承诺均以已验证活着的 Recipe 体系和 CodeBuilder 复用兑现，不再维持无人运行的第三套抽象。**

## 6. 后果

### 正面

- 删除约 1450 行死代码，Engine 从"55% 死代码"变为"全部真实被消费"；
- README 叙事与实现一致，消除新贡献者最大的认知陷阱；
- 包体积缩小，netstandard2.0 程序集加载更快（对编译器宿主可感知）；
- 扩展路线收敛到已验证的三条真实路径，未来投入不再分散。

### 负面与缓解

| 风险 | 评估 | 缓解 |
|---|---|---|
| `IGenerationPlugin`/`GenerationPipeline` 是 public API，理论上有外部实现者 | 可能性极低：管线从未有宿主执行，外部插件写了也无法运行 |  semver 3.0 升主版本；CHANGELOG 明确 breaking change；迁移指引：声明式 → customGenerators，代码级 → 独立 SG + CodeBuilder |
| README 中"插件 SDK 规划中"的承诺 | 承诺保留，形态修正 | 将 SDK 定义为"CodeBuilder 独立包 + Recipe 编写指南"，列入 v3.1 计划 |
| `ConfigRecommender`（V2.1 交付物）被删除 | 功能本身有价值，问题是从未接线 | 决策点：优先将其逻辑迁移至 CLI `doctor`（让承诺兑现），而非直接删除 |

## 7. 迁移步骤（v3.0 执行清单）

1. 删除 `src/AutoCode.Engine/Pipeline/`（4 个文件）、`src/AutoCode.Engine/Plugin/`、`src/AutoCode.Engine/Roslyn/`、`src/AutoCode.Engine/Convention/`；
2. 删除 `src/AutoCode.Generators/V2/InterceptPlugin.cs` 空壳；
3. `ConfigRecommender` 逻辑并入 `AutoCode.Cli` 的 `doctor` 命令（独立 PR，先接线的死代码复活优先于删除）；
4. 评估 `DiagnosticCollector`/`AutoCodeConfig`：若删除基类后无消费方，一并删除或内联到保留模块；
5. README 架构章节重写：架构图替换为"生成器矩阵 + Recipe + CodeBuilder"三层，删除管线 ASCII 图；
6. CHANGELOG 记录 breaking change + 迁移指引；
7. `docs/v2-v3-migration.md` 新增：自定义插件 → 三条替代路径对照表；
8. 全量构建 + 快照测试（阶段 0 产物）验证零行为变化。

## 8. 前置依赖

- **依赖阶段 0（快照测试安全网）先落地**：本决策的"删除"操作虽理论上不影响编译行为（死代码），但 README/文档/CLI 的联动修改需要测试网兜底；
- 建议执行顺序：阶段 0 快照 → 本 ADR 确认 → ConfigRecommender 并入 doctor（行为新增）→ 删除死代码（行为不变，快照应零变化）。
