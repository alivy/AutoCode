---
kind: error_handling
name: 基于 Roslyn 诊断与 CodeFix 的错误处理体系
category: error_handling
scope:
    - '**'
source_files:
    - src/AutoCode.Analyzers/Diagnostics/AutoCodeDiagnosticDescriptors.cs
    - src/AutoCode.Analyzers/Analyzers/MissingAutoInterfaceAnalyzer.cs
    - src/AutoCode.Analyzers/Analyzers/InterfaceDivergenceAnalyzer.cs
    - src/AutoCode.Analyzers/Analyzers/UnusedAutoIgnoreAnalyzer.cs
    - src/AutoCode.Analyzers/CodeFixes/AddAutoInterfaceCodeFix.cs
    - src/AutoCode.Analyzers/CodeFixes/RemoveAutoIgnoreCodeFix.cs
    - src/AutoCode.Map/Diagnostics/DiagnosticDescriptors.cs
---

该仓库是一个基于 Roslyn IIncrementalGenerator 的 C# 代码生成工具集，其错误处理主要围绕 **Roslyn 静态分析（Analyzer）与诊断（Diagnostic）** 机制构建，而非传统的运行时异常或中间件模式。具体体现在以下几个方面：

### 1. 系统/框架
- 使用 `Microsoft.CodeAnalysis.Diagnostics` 实现自定义 Analyzer，通过 `DiagnosticAnalyzer` 基类在编译期检测代码问题并上报 `Diagnostic`。
- 使用 `CodeFixProvider` 为诊断提供一键修复能力，支持批量修复（`WellKnownFixAllProviders.BatchFixer`）。
- 所有诊断通过 `DiagnosticDescriptor` 定义，包含 ID、标题、消息模板、分类、严重级别等元数据。

### 2. 核心文件与组织
- **AutoCode.Analyzers/Diagnostics/AutoCodeDiagnosticDescriptors.cs**：集中定义 AC001~AC003 三个诊断描述符，按 Usage/Design 分类。
- **AutoCode.Analyzers/Analyzers/**：三个 Analyzer 分别负责检测缺失 `[AutoInterface]`、接口与实现不一致、非公共成员上无意义的 `[AutoIgnore]`。
- **AutoCode.Analyzers/CodeFixes/**：对应两个 CodeFix，分别为诊断提供自动修复逻辑。
- **AutoCode.Map/Diagnostics/DiagnosticDescriptors.cs**：映射器相关的诊断（RMG046、RMG002、RMG008），复用 Mapperly 的诊断风格。

### 3. 架构与约定
- 每个 Analyzer 继承 `DiagnosticAnalyzer`，在 `Initialize` 中注册符号/语法节点回调，使用 `context.ReportDiagnostic` 上报问题。
- 诊断严重级别分为 Warning（AC001、AC003）和 Info（AC002），映射器相关为 Error。
- 诊断 ID 采用前缀约定：AutoCode 分析器用 `ACxxx`，映射器用 `RMGxxx`。
- CodeFix 通过 `[ExportCodeFixProvider]` 暴露，实现 `FixableDiagnosticIds` 绑定到对应诊断。

### 4. 约束与规范
- 所有诊断必须通过 `DiagnosticDescriptor` 定义，禁止硬编码字符串。
- Analyzer 需调用 `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` 跳过生成代码。
- 诊断消息使用占位符 `{0}`、`{1}` 等注入上下文信息（类型名、成员名、接口名等）。
- CodeFix 需提供 `GetFixAllProvider()` 以支持批量修复。
- 特性匹配同时检查完整命名空间和短名称，兼容不同引用方式。