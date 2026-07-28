---
kind: dependency_management
name: C# 项目依赖管理（.csproj + NuGet）
slug: dependency_management
category: dependency_management
scope:
    - '**'
---

本仓库采用 .NET SDK 风格的 .csproj 文件进行依赖声明，结合 NuGet 包管理器管理第三方库，并通过 ProjectReference 引用内部模块。具体模式如下：

1. **依赖声明方式**
   - 第三方库通过 `<PackageReference>` 在各自 `.csproj` 中声明版本，例如 `Microsoft.CodeAnalysis.CSharp 4.11.0`、`Swashbuckle.AspNetCore 6.6.2`、`xunit 2.5.3` 等。
   - 内部模块之间通过 `<ProjectReference>` 相互引用，如 `AutoCodeGenerator`、`AutoCode.Map`、`AutoCode.Model` 等。

2. **Source Generator 集成**
   - 代码生成器项目通过 `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` 的方式作为分析器注入到消费项目中，避免将生成器程序集打包进最终产物。

3. **NuGet 包发布**
   - `AutoCode.Extensions.SourceGenerator` 项目配置了完整的 NuGet 包元数据（PackageId、Version、Authors、RepositoryUrl 等），并设置了 `DevelopmentDependency=true` 标记为开发依赖。
   - 通过 MSBuild Target 在 Publish 配置下自动执行 `dotnet publish` 将各生成器输出到 `NugetPackage/analyzers/dotnet/cs` 目录，再使用 `dotnet pack` 打包。

4. **目标框架与语言版本**
   - 库项目统一使用 `netstandard2.0` 或 `netstandard2.1` 以确保兼容性。
   - 应用和测试项目使用 `net8.0`，所有项目启用 `<Nullable>enable</Nullable>` 和 `<LangVersion>12.0</LangVersion>`。

5. **构建配置**
   - 解决方案文件 `AutoCode.sln` 统一管理所有项目，定义了 Debug/Release/Publish 多种配置。
   - 部分项目启用了 `<EnablePackageValidation>true</EnablePackageValidation>` 进行包验证。

6. **无全局配置文件**
   - 未发现 `global.json`、`nuget.config` 或 `Directory.Packages.props` 等集中式依赖管理文件，版本信息分散在各个 `.csproj` 文件中。