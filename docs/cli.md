# CLI 参考

AutoCode 提供命令行工具 `dotnet autocode`，用于项目初始化、代码生成、模板管理与环境诊断。

## 安装

```bash
dotnet tool install -g AM.AutoCode.Cli
```

## 命令总览

| 命令 | 功能 |
|------|------|
| `list` | 列出全部生成器、分析器与插件及其状态 |
| `new` | 交互式创建实体/服务/控制器等代码骨架 |
| `generate` | 执行代码生成（支持预览模式） |
| `analyze` | 分析项目结构并给出优化建议 |
| `doctor` | 诊断 AutoCode 配置与环境问题 |
| `templates` | 管理代码模板（list / install） |
| `init` | 初始化 AutoCode 配置（autocode.json + 模板目录） |

---

## list

列出所有可用的生成器、分析器与插件，显示启用状态与版本。

```bash
dotnet autocode list
```

输出内容包括：11 个 IIncrementalGenerator、5 个分析器（AC001~AC9100 诊断规则）、V2 插件启用状态。

## new

交互式创建代码骨架。当前支持 `entity` 子命令。

```bash
dotnet autocode new entity <实体名> [选项]
```

**参数与选项：**

| 参数/选项 | 类型 | 默认值 | 说明 |
|-----------|------|--------|------|
| `<实体名>` | string（必填） | — | 实体类名称 |
| `--with-crud` | bool | `true` | 同时生成 CRUD 全链路（Service + Controller + Repository） |
| `--with-tests` | bool | `false` | 生成 xUnit 测试桩 |
| `--with-validation` | bool | `true` | 生成编译时验证器 |
| `--output` | string | `.` | 输出目录 |

**示例：**

```bash
# 创建 Product 实体 + CRUD 全链路 + 验证
dotnet autocode new entity Product --with-crud --with-validation

# 仅创建实体与测试
dotnet autocode new entity Order --with-crud false --with-tests --output ./Models
```

## generate

对项目执行代码生成分析。默认仅预览，不写磁盘。

```bash
dotnet autocode generate [选项]
```

**选项：**

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `--preview` | bool | `false` | 预览模式：列出将生成的文件但不实际执行 |
| `--project` | string | `.` | 目标项目路径 |

**示例：**

```bash
# 预览当前项目将生成哪些代码
dotnet autocode generate --preview

# 分析指定项目
dotnet autocode generate --project ./src/MyApp
```

> 实际的代码生成在编译时由 Source Generator 自动完成；此命令用于预览与调试生成结果。

## analyze

分析项目源码结构，识别可使用 AutoCode 优化的位置（可提取接口的类、可生成 DTO 的实体、可自动注册的服务等）。

```bash
dotnet autocode analyze [路径]
```

**参数：**

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `路径` | string | `.` | 要分析的项目或目录路径 |

**示例：**

```bash
dotnet autocode analyze ./src
```

## doctor

诊断 AutoCode 的配置与环境健康状况，检查常见问题：

- NuGet 包引用是否正确（AM.AutoCode 版本）
- `autocode.json` 是否存在且格式有效
- `CompilerVisibleProperty` 是否正确声明
- V1/V2 生成器冲突检测
- 生成器是否成功加载（CS8785 静默失效检测）

```bash
dotnet autocode doctor
```

**使用建议：** 遇到生成器不生效、生成重复代码等问题时，首先运行此命令。

## templates

管理 DotLiquid 代码模板。

### templates list

列出所有可用模板（内置 + 项目自定义）：

```bash
dotnet autocode templates list
```

### templates install

将指定模板安装到当前项目：

```bash
dotnet autocode templates install <模板名>
```

**参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| `<模板名>` | string（必填） | 要安装的模板名称 |

## init

在指定路径初始化 AutoCode 配置，生成 `autocode.json` 与模板目录骨架：

```bash
dotnet autocode init [路径]
```

**参数：**

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `路径` | string | `.` | 初始化目标目录 |

**生成内容：**

- `autocode.json`：包含全部插件的默认启用配置
- `templates/`：示例模板目录（含 AuditService.liquid、Repository.liquid 示例）

## 典型工作流

```bash
# 1. 新项目初始化
dotnet autocode init ./MyApp

# 2. 检查环境
dotnet autocode doctor

# 3. 创建实体与全链路代码
dotnet autocode new entity Product --with-crud

# 4. 编译（Source Generator 自动生成其余代码）
dotnet build

# 5. 查看生成结果
dotnet autocode generate --preview
```

## 相关文档

- [配置参考](configuration.md) — autocode.json 全节点说明
- [故障排除](troubleshooting.md) — 常见问题与解决方案
