# AutoCode

基于 Roslyn IIncrementalGenerator 的 C# 编译时代码生成工具集，提供接口自动生成、模板代码生成和对象映射三大核心能力。

## 核心特性

| 生成器 | 特性标记 | 功能 |
|--------|----------|------|
| **InterfaceGenerator** | `[AutoInterface]` | 自动从类提取公共方法和属性生成接口 |
| **DotTemplateGenerator** | `[DotTemplate]` | 基于 DotLiquid 模板引擎二次生成代码 |
| **MapperGenerator** | `[Mapper]` | 自动生成对象属性复制的 CopyTo 扩展方法 |

## 技术架构

- **运行时**: .NET 8.0 / netstandard2.0 (生成器)
- **代码分析**: Microsoft.CodeAnalysis.CSharp 4.11.0
- **模板引擎**: DotLiquid 2.2.692
- **生成器模式**: IIncrementalGenerator (增量生成，支持增量缓存)
- **测试框架**: xUnit + Microsoft.CodeAnalysis.Testing

## 安装

```bash
dotnet add package AM.AutoCode
```

## 使用指南

### 1. 自动接口生成

为标记 `[AutoInterface]` 的类自动生成接口，支持方法签名、属性签名、泛型方法。

#### 基础用法

```csharp
using AutoCode.Model.InterfaceAttribute;

namespace MyApp.Services
{
    [AutoInterface]
    public class UserService : IUserService
    {
        public int GetId() => 1;
        public string GetName() => "test";
    }
}
```

生成的接口（内存中，无需手动创建文件）：

```csharp
namespace MyApp.Services
{
    public interface IUserService
    {
        int GetId();
        string GetName();
    }
}
```

#### 自定义接口名称

```csharp
[AutoInterface("ICustomService")]
public class CustomService : ICustomService
{
    public void Execute() { }
}
```

#### 忽略特定方法/属性

使用 `[AutoIgnore]` 标记不需要生成到接口中的成员：

```csharp
[AutoInterface]
public class OrderService : IOrderService
{
    public string CreateOrder() => "OK";

    [AutoIgnore]
    public string InternalMethod() => "secret";  // 不会出现在接口中
}
```

#### 属性生成

公共属性会自动生成到接口中，保留 get/set 访问器：

```csharp
[AutoInterface]
public class ConfigService : IConfigService
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int ReadOnlyValue { get; }  // 只生成 get
}
```

生成结果：

```csharp
public interface IConfigService
{
    int Id { get; set; }
    string Name { get; set; }
    int ReadOnlyValue { get; }
}
```

#### 泛型方法支持

```csharp
[AutoInterface]
public class Repository : IRepository
{
    public T Get<T>(int id) => default;
    public List<T> GetAll<T>() => new();
}
```

生成结果：

```csharp
public interface IRepository
{
    T Get<T>(int id);
    List<T> GetAll<T>();
}
```

#### 多接口生成

同一个类可以标记多个 `[AutoInterface]`，生成多个接口：

```csharp
[AutoInterface("IReadableService")]
[AutoInterface("IWritableService")]
public class DataService : IReadableService, IWritableService
{
    public string Read() => "data";
    public void Write(string data) { }
}
```

### 2. 模板代码生成

基于 [DotLiquid](https://dotliquidmarkup.org/) 模板引擎，根据类的结构信息（方法、属性、字段、特性等）使用模板自动生成代码。

#### 创建模板文件

创建 `.dot` 模板文件，使用 DotLiquid 语法：

```
{% for us in Usings %}using {{ us.DefName }};
{% endfor %}
namespace {{ NameSpace }}
{
    public class {{ DefName }}Caller
    {
        {% for mth in Methods %}
        public {{ mth.Type }} {{ mth.DefName }}Copy({% for p in mth.Parameters %}{{ p.Type }} {{ p.DefName }}{% if forloop.last != true %}, {% endif %}{% endfor %})
        {
            return {{ DefName }}.{{ mth.DefName }}({% for p in mth.Parameters %}{{ p.DefName }}{% if forloop.last != true %}, {% endif %}{% endfor %});
        }
        {% endfor %}
    }
}
```

#### 使用模板生成代码

```csharp
using AutoCode.Model;

namespace MyApp
{
    // 参数1: 模板文件路径（相对路径基于当前 .cs 文件所在目录）
    // 参数2: "$Source.cs" 表示生成内存源文件
    // 参数3: 生成文件名（支持 DotLiquid 变量替换）
    [DotTemplate("Templates/ServiceCaller.dot", "$Source.cs", "{{ DefName }}Caller.cs")]
    public class OrderService
    {
        public int CreateOrder(string name) => 1;
        public void CancelOrder(int orderId) { }
    }
}
```

#### DotLiquid 可用变量

模板中可访问以下类结构数据：

| 变量 | 类型 | 说明 |
|------|------|------|
| `NameSpace` | string | 命名空间 |
| `DefName` | string | 类名 |
| `Modifier` | string | 修饰符 (public/internal 等) |
| `Usings` | List | using 指令集合，每项有 `DefName` |
| `Inherits` | List | 继承的接口/类，每项有 `DefName` |
| `Attributes` | List | 特性集合，每项有 `DefName` 和 `Parameters` |
| `Methods` | List | 方法集合，每项有 `Modifier`、`DefName`、`Type`、`Parameters`、`Remarks` |
| `Propertys` | List | 属性集合，每项有 `Modifier`、`DefName`、`Type` |
| `Fileds` | List | 字段集合，每项有 `Modifier`、`DefName`、`Type` |

### 3. 对象映射生成

为标记 `[Mapper]` 的类自动生成 `CopyTo` 扩展方法，支持简单类型、引用类型和集合类型。

#### 基础用法

```csharp
using AutoCode.Model.AutoMapperModel;

namespace MyApp.Models
{
    [Mapper]
    public class UserDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<AddressDto> Addresses { get; set; }
    }
}
```

生成的映射器：

```csharp
public static class UserDtoMapper
{
    /// <summary>
    /// 将源对象的属性复制到目标对象
    /// </summary>
    public static void CopyTo(this UserDto source, UserDto target)
    {
        target.Id = source.Id;
        target.Name = source.Name;
        target.CreatedAt = source.CreatedAt;
        if (source.Addresses != null)
        {
            target.Addresses = new List<AddressDto>(source.Addresses);
        }
    }
}
```

#### 使用映射器

```csharp
var source = new UserDto { Id = 1, Name = "test" };
var target = new UserDto();
source.CopyTo(target);  // 自动复制所有属性
```

## 项目结构

```
AutoCode/
├── src/
│   ├── AutoCodeGenerator/              # 接口生成器 (IIncrementalGenerator)
│   │   └── InterfaceAutoBuilder/
│   │       ├── InterfaceGenerator.cs   # 增量生成器主逻辑
│   │       ├── InterfaceBuilder.cs     # 接口代码构建器
│   │       └── InterfaceSpec.cs        # 数据模型 + 增量缓存比较器
│   │
│   ├── AutoCode.XmlTemplate.SourceGenerator/  # 模板生成器 (IIncrementalGenerator)
│   │   ├── DotTemplateGenerator.cs     # 增量生成器主逻辑
│   │   ├── CSData.cs                   # 类结构数据模型 (ILiquidizable)
│   │   ├── SyntaxNodeConvert.cs        # 语法树到数据模型转换
│   │   └── Extend/
│   │       ├── DotHelp.cs              # DotLiquid 渲染帮助类
│   │       └── DiagnosticIds.cs        # 诊断 ID 定义
│   │
│   ├── AutoCode.Map/                   # 对象映射生成器 (IIncrementalGenerator)
│   │   ├── MapperGenerator.cs          # 增量生成器主逻辑
│   │   ├── Helpers/
│   │   │   ├── IncrementalValuesProviderExtensions.cs
│   │   │   └── ImmutableEquatableArray.cs
│   │   └── Diagnostics/
│   │       └── DiagnosticDescriptors.cs
│   │
│   ├── AutoCode.Model/                 # 特性模型库 (netstandard2.0)
│   │   ├── InterfaceAttribute/         # [AutoInterface] [AutoIgnore]
│   │   ├── DotFileAttribute/           # [DotTemplate]
│   │   └── AutoMapperModel/            # [Mapper] 及映射配置特性
│   │
│   ├── AutoCode.MapDebug/              # Map 生成器调试版本
│   ├── AutoCode.Extensions.SourceGenerator/  # NuGet 打包项目
│   │
│   ├── APP/                            # 接口生成示例
│   ├── APP.WebAPI/                     # WebAPI 集成示例
│   ├── APP.WebAPI.Core/                # WebAPI 核心框架 (DI/AutoInit)
│   ├── APP.Map/                        # 对象映射示例
│   ├── DotTemplate.APP/                # 模板生成示例
│   ├── Models/                         # 测试模型
│   │
│   ├── AutoCode.Tests/                 # 单元测试项目
│   │   ├── InterfaceGeneratorTests.cs  # 接口生成器测试 (8 个)
│   │   └── MapGeneratorTests.cs        # 映射生成器测试 (3 个)
│   │
│   ├── AutoCode.sln                    # 解决方案文件
│   └── .editorconfig                   # 代码规范配置
│
└── README.md
```

## 构建与测试

### 环境要求

- .NET SDK 8.0 或更高版本
- Visual Studio 2022 / VS Code / Rider

### 构建解决方案

```bash
cd src
dotnet build AutoCode.sln
```

### 运行测试

```bash
cd src
dotnet test AutoCode.sln
```

### NuGet 打包

```bash
cd src
dotnet build AutoCode.Extensions.SourceGenerator/AutoCode.SourceGenerator.Extensions.csproj -c Publish
```

生成的 NuGet 包位于 `src/.nuget/AM.AutoCode.{version}.nupkg`。

### 发布到 NuGet

```bash
nuget push src/.nuget/AM.AutoCode.1.2.0.nupkg YOUR_API_KEY -Source https://api.nuget.org/v3/index.json
```

## 技术亮点

- **IIncrementalGenerator**: 三个生成器均采用 Roslyn 增量生成器，仅在实际变更时重新生成，编译性能最优
- **增量缓存**: InterfaceGenerator 使用自定义 `InterfaceSpecComparer`，防止无关代码变更触发重复生成
- **CreateSyntaxProvider**: 统一使用语法树级别特性匹配，兼容性更广
- **FileScopedNamespace**: 支持 C# 10+ 文件范围命名空间语法
- **零文件写入**: 所有生成器仅使用 `context.AddSource` 生成内存源，不写磁盘文件
- **泛型方法支持**: 接口生成器正确提取泛型类型参数 `<T>`
- **属性签名生成**: 接口生成器自动生成属性 get/set 访问器
- **跨程序集安全映射**: Map 生成器对复杂类型使用浅拷贝，避免跨程序集 CopyTo 缺失

## 版本历史

| 版本 | 说明 |
|------|------|
| 1.2.0 | 全面架构重构：IIncrementalGenerator 迁移、增量缓存、泛型/属性支持、FileScopedNamespace、NuGet 打包修复 |
| 1.1.x | ISourceGenerator 初始版本 |

## License

MIT
