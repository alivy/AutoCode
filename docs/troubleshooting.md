# 故障排除

本文档汇总使用 AutoCode 时的常见问题与解决方案。遇到问题时，建议先运行 `dotnet autocode doctor` 自动诊断。

---

## 生成器不生效 / 没有生成任何代码

### 症状

标记了 `[AutoInterface]`、`[AutoDTO]` 等特性，编译成功但找不到生成的接口/类。

### 排查步骤

**1. 确认 NuGet 包或 Analyzer 引用正确**

```xml
<!-- NuGet 方式 -->
<PackageReference Include="AM.AutoCode" Version="2.3.0" />

<!-- 或 ProjectReference 方式（本项目源码消费） -->
<ProjectReference Include="..\AutoCode.Generators\AutoCode.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

**2. 检查 CS8785 警告（生成器静默失效）**

编译输出中若出现：

```
warning CS8785: 生成器"XxxGenerator"未能生成源...FileNotFoundException: Could not load file or assembly 'AutoCode.Engine...'
```

说明生成器的依赖程序集未传递给编译器。**此问题已在 2.3.1 修复** — 升级到最新版本即可。若使用 ProjectReference 源码方式引用，确保 `AutoCode.Generators.csproj` 中包含 `GetDependencyTargetPaths` 目标（2.3.1+ 内置）。

**3. 查看生成输出目录**

在 `.csproj` 中添加以下属性后重新编译，生成的 `.g.cs` 文件会输出到 `obj/Debug/<tfm>/generated/` 目录：

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
```

**4. 确认特性命名空间**

```csharp
using AutoCode.Model;  // 所有 Auto* 特性所在命名空间
```

## CS0101 / CS0111：类型重复定义

### 症状

```
error CS0101: 命名空间"MyApp.Models"已经包含"UserDto"的定义
error CS0111: 类型"AutoDependencyInjection"已定义了一个名为"AddAutoDI"的具有相同参数类型的成员
```

### 原因

V1 与 V2 生成器监听相同特性（如 `[AutoInterface]`、`[AutoDTO]`），当 V2 被启用（`AutoCode_EnableV2=true`）而 V1 也在运行时，同一类型会被生成两次。

### 解决方案

**默认配置下不应出现此问题**（V2 默认关闭）。若你显式启用了 V2：

```xml
<AutoCode_EnableV2>true</AutoCode_EnableV2>
```

则需确保 V1 不处理相同特性。当前版本 V1 始终运行且不可独立关闭（V1 承担着 `[DotTemplate]`、`[AutoIntercept]` 等独有功能），因此：

- **推荐**：保持默认 `AutoCode_EnableV2` 为 `false`，仅使用 V1
- 如需 V2 特性（级联生成 `[AutoEntity]`、跨类型映射 `[MapFrom]`），注意这些特性 V1 不处理，启用 V2 不会冲突

详细说明见 [V1/V2 迁移指南](v1-v2-migration.md)。

## CS8669：生成代码中的可空注解警告

### 症状

```
warning CS8669: 对可 null 的引用类型的批注只应在"#nullable"批注上下文中的代码中使用
```

### 原因

生成的 `.g.cs` 文件包含 `string?` 等可空注解，但文件未声明 `#nullable enable`。

### 解决方案

**已在 2.3.1 修复**（生成器现在自动输出 `#nullable enable`）。升级即可。

## CS8618：生成的 DTO 属性未初始化

### 症状

```
warning CS8618: 在退出构造函数时，不可为 null 的属性"Name"必须包含非 null 值
```

### 解决方案

**已在 2.3.1 修复**：`DtoGenerator` 现在为非空引用类型属性自动生成 `= default!;` 初始化。

## 生成器修改后不生效（IDE 缓存）

### 症状

修改了特性或配置，但 IDE 中生成的代码没有更新。

### 解决方案

```bash
# 关闭 Roslyn 构建服务器清除缓存
dotnet build-server shutdown

# 强制全量重建
dotnet build -t:Rebuild
```

在 Visual Studio 中：关闭解决方案 → 删除 `.vs` 目录 → 重新打开。

## NuGet 包方式引用时分析器（AC001 等）不提示

### 原因

`AM.AutoCode` 包将生成器与分析器打包在 `analyzers/dotnet/cs` 目录，只有**编译时**才会加载。IDE 实时分析需要项目引用了 `AutoCode.Analyzers.dll`。

### 解决方案

确认使用 2.3.1+ 版本的包，其 analyzers 目录包含完整依赖（`AutoCode.Model.dll`、`AutoCode.Engine.dll`、`System.Text.Json.dll` 等）。

## PDB 文件损坏错误（CS0009）

### 症状

```
error CS0009: 无法打开元数据文件"...AutoCode.Model.pdb" — Unknown file format
```

### 解决方案

这是构建缓存损坏，清理后重建：

```bash
dotnet build-server shutdown
# 删除对应项目的 bin/obj 目录后重新构建
dotnet build
```

## 中国大陆网络环境：NuGet/GitHub 访问失败

### 症状

`dotnet restore` 或 `git pull` 超时：`Failed to connect to github.com:443` / `nuget.org 无法访问`。

### 解决方案

**Git 代理配置**（以本地代理端口 7890 为例）：

```bash
git config --global http.proxy http://127.0.0.1:7890
git config --global https.proxy http://127.0.0.1:7890
```

**NuGet 镜像**：使用国内镜像源（如华为云、腾讯云镜像），在 `NuGet.config` 中配置。

## IL2026：DataAnnotations 裁剪警告

### 症状

```
warning IL2026: Using member 'System.ComponentModel.DataAnnotations.MaxLengthAttribute...' can break functionality when trimming
```

### 说明

这是 .NET 8 对 `DataAnnotations` 部分特性的固有裁剪警告（`MaxLength`/`MinLength` 对非 ICollection 类型使用反射），与 AutoCode 无关。AutoCode 生成的验证代码本身零反射、AOT 兼容。

若项目启用了 `<IsAotCompatible>true</IsAotCompatible>`，可选择：

- 忽略（功能不受影响，仅在 trimming 时可能裁剪反射路径）
- 改用 AutoCode 的 `[AutoValidator]` 编译时验证（完全无反射）

## 获取帮助

- 运行 `dotnet autocode doctor` 自动诊断
- 查看 [配置参考](configuration.md) 确认配置项正确
- 查看 [V1/V2 迁移指南](v1-v2-migration.md) 了解生成器行为差异
- GitHub Issues：<https://github.com/alivy/AutoCode/issues>
