# AutoCode

基于 Roslyn IIncrementalGenerator 的 C# 编译时代码生成工具集，提供 **10 个生成器 + 5 个分析器 + 2 个代码修复 + CLI 工具**，覆盖接口生成、模板生成、对象映射、DTO、验证、Controller、DI 注册、测试桩、日志装饰器、CRUD 一键生成等场景。

## 核心特性

### 代码生成器

| 生成器 | 特性标记 | 功能 |
|--------|----------|------|
| **InterfaceGenerator** | `[AutoInterface]` | 自动从类提取公共方法、属性、泛型方法生成接口（Async 感知 + XML 文档继承 + Nullable 感知） |
| **DotTemplateGenerator** | `[DotTemplate]` | 基于 DotLiquid 模板引擎二次生成代码 |
| **MapperGenerator** | `[Mapper]` | 自动生成对象属性复制的 CopyTo 扩展方法 |
| **DtoGenerator** | `[AutoDTO]` | 从实体类自动生成 DTO + FromEntity/ToEntity 方法 |
| **ValidationGenerator** | `[AutoValidator]` | 根据 DataAnnotations 生成编译时验证代码 |
| **ControllerGenerator** | `[AutoController]` | 从 Service 类自动生成 RESTful API Controller（Swagger 注解） |
| **DependencyInjectionGenerator** | `IScoped/ISingleton/ITransient` | 编译时 DI 注册，替代运行时反射扫描 |
| **TestGenerator** | `[AutoTest]` | 自动生成 xUnit 测试桩（Arrange-Act-Assert 模式） |
| **LogDecoratorGenerator** | `[AutoLog]` | 自动生成日志装饰器（参数记录 + 耗时统计 + 异常捕获） |
| **CrudGenerator** | `[AutoCrud]` | 一键生成 CRUD Service 接口 + 内存实现 + RESTful Controller |

### 代码分析器

| 诊断 ID | 严重性 | 触发条件 | 代码修复 |
|---------|--------|----------|----------|
| **AC001** | Warning | 类实现了接口但缺少 `[AutoInterface]` | 自动添加 `[AutoInterface]` + using |
| **AC002** | Info | `[AutoInterface]` 类的公共成员与接口不一致 | 提示同步 |
| **AC003** | Warning | `[AutoIgnore]` 标记在非公共成员上 | 自动移除 `[AutoIgnore]` |
| **AC004** | Warning | Controller 直接引用 DbContext/Repository 等数据层类型 | 提示通过 Service 层访问 |
| **AC006** | Info | Service/Controller 类命名不符合约定 | 提示重命名 |

### MSBuild 配置

通过 `.csproj` PropertyGroup 控制生成器行为：

```xml
<PropertyGroup>
  <AutoCode_InterfacePrefix>I</AutoCode_InterfacePrefix>
  <AutoCode_MapMethodName>CopyTo</AutoCode_MapMethodName>
  <AutoCode_GenerateNullable>true</AutoCode_GenerateNullable>
</PropertyGroup>
```

## 技术架构

- **运行时**: .NET 8.0 / netstandard2.0 (生成器)
- **代码分析**: Microsoft.CodeAnalysis.CSharp 4.11.0
- **模板引擎**: DotLiquid 2.2.692
- **生成器模式**: IIncrementalGenerator (增量生成 + 增量缓存)
- **测试框架**: xUnit + Microsoft.CodeAnalysis.Testing (23 个测试)
- **CI/CD**: GitHub Actions (PR 自动测试, Tag 自动发布 NuGet)

## 安装

```bash
dotnet add package AM.AutoCode
```

CLI 工具：

```bash
dotnet tool install -g AM.AutoCode.Cli
```

## 使用指南

### 1. 自动接口生成

```csharp
[AutoInterface]
public class UserService : IUserService
{
    public int GetId() => 1;
    public string Name { get; set; }       // 属性也会生成
    public T Get<T>(int id) => default;    // 泛型方法支持

    [AutoIgnore]
    public string Secret() => "hidden";    // 不会出现在接口中
}
```

生成结果：

```csharp
public interface IUserService
{
    int GetId();
    string Name { get; set; }
    T Get<T>(int id);
}
```

支持：自定义接口名 `[AutoInterface("ICustom")]`、多接口 `[AutoInterface("IA")][AutoInterface("IB")]`。

### 2. 模板代码生成

```csharp
[DotTemplate("Templates/ServiceCaller.dot", "$Source.cs", "{{ DefName }}Caller.cs")]
public class OrderService
{
    public int CreateOrder(string name) => 1;
}
```

模板使用 [DotLiquid](https://dotliquidmarkup.org/) 语法，可访问 `Usings`、`Methods`、`Propertys`、`Inherits` 等类结构数据。

### 3. 对象映射生成

```csharp
[Mapper]
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<AddressDto> Addresses { get; set; }
}

// 使用
source.CopyTo(target);
```

### 4. AutoDTO 生成

```csharp
[AutoDTO(typeof(UserEntity), Exclude = new[] { "PasswordHash", "IsDeleted" })]
public partial class UserDto { }  // 一行代码，自动生成全部
```

生成结果：

```csharp
public partial class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }

    public static UserDto FromEntity(UserEntity entity) => new() { ... };
    public void ToEntity(UserEntity entity) { ... }
}
```

支持 `Include`/`Exclude` 属性过滤。

### 5. 编译时验证

```csharp
[AutoValidator]
public class CreateUserRequest
{
    [Required] [MaxLength(50)]  public string Name { get; set; }
    [Required] [EmailAddress]   public string Email { get; set; }
    [Range(0, 150)]             public int Age { get; set; }
    [MinLength(8)]              public string Password { get; set; }
    [Url]                       public string? AvatarUrl { get; set; }
}
```

生成结果（零反射，纯编译时）：

```csharp
public class CreateUserRequestValidator
{
    public List<string> Validate(CreateUserRequest input)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(input.Name)) errors.Add("Name is required.");
        if (input.Name?.Length > 50) errors.Add("Name must not exceed 50 characters.");
        if (input.Age < 0 || input.Age > 150) errors.Add("Age must be between 0 and 150.");
        return errors;
    }
}
```

支持：`[Required]`、`[Range]`、`[MaxLength]`、`[MinLength]`、`[EmailAddress]`、`[Url]`、`[RegularExpression]`。

### 6. AutoController 生成

```csharp
[AutoInterface]
[AutoController(RoutePrefix = "api/users")]
public class UserService : IUserService, IScoped
{
    public List<UserDto> GetAll() { ... }
    public UserDto? GetById(int id) { ... }
    public UserDto Create(CreateUserRequest req) { ... }
    public UserDto? Update(int id, UpdateUserRequest req) { ... }
    public void Delete(int id) { ... }
}
```

生成 RESTful Controller（HTTP 方法自动推断）：

```csharp
[ApiController]
[Route("api/users")]
public class UserServiceController : ControllerBase
{
    [HttpGet]            GetAll()
    [HttpGet("{id}")]    GetById(int id)
    [HttpPost]           Create([FromBody] CreateUserRequest req)
    [HttpPut("{id}")]    Update(int id, [FromBody] UpdateUserRequest req)
    [HttpDelete("{id}")] Delete(int id)
}
```

HTTP 推断规则：Get/Find→GET, Create/Add→POST, Update/Modify→PUT, Delete/Remove→DELETE。

### 7. 编译时依赖注入

实现 `IScoped`/`ISingleton`/`ITransient` 接口的类自动注册：

```csharp
public class UserService : IUserService, IScoped { }
public class CacheService : ICacheService, ISingleton { }
```

生成结果：

```csharp
public static partial class AutoDependencyInjection
{
    public static IServiceCollection AddAutoDI(this IServiceCollection services)
    {
        services.TryAddScoped<IUserService, UserService>();
        services.TryAddSingleton<ICacheService, CacheService>();
        return services;
    }
}
```

替代运行时反射扫描，兼容 NativeAOT。

## CLI 工具

```bash
dotnet autocode list                  # 列出所有生成器和分析器
dotnet autocode init [path]           # 初始化模板目录 + 示例模板
dotnet autocode validate-templates    # 验证 .dot 模板语法
```

## 项目结构

```
AutoCode/
├── src/
│   ├── AutoCodeGenerator/              # 接口生成器
│   ├── AutoCode.XmlTemplate.SourceGenerator/  # 模板生成器
│   ├── AutoCode.Map/                   # 对象映射生成器
│   ├── AutoCode.Dto/                   # DTO 生成器
│   ├── AutoCode.Validation/            # 验证代码生成器
│   ├── AutoCode.WebApi/                # API Controller 生成器
│   ├── AutoCode.DependencyInjection/   # 编译时 DI 注册生成器
│   ├── AutoCode.Analyzers/             # 分析器 + CodeFix (AC001/AC002/AC003)
│   ├── AutoCode.Model/                 # 特性模型库 (7 个特性)
│   ├── AutoCode.Cli/                   # CLI 工具
│   ├── AutoCode.Extensions.SourceGenerator/  # NuGet 打包
│   │
│   ├── APP/                            # 接口生成示例
│   ├── APP.WebAPI/                     # 综合示例 (DTO+验证+Controller+DI)
│   ├── APP.WebAPI.Core/                # WebAPI 核心框架
│   ├── APP.Map/                        # 对象映射示例
│   ├── DotTemplate.APP/                # 模板生成示例
│   │
│   ├── AutoCode.Tests/                 # 单元测试 (23 个)
│   ├── AutoCode.sln                    # 解决方案
│   └── .editorconfig                   # 代码规范
│
├── .github/workflows/ci.yml           # CI/CD 流水线
└── README.md
```

## 构建与测试

```bash
cd src
dotnet build AutoCode.sln       # 构建
dotnet test AutoCode.sln        # 测试 (23 个)
```

### NuGet 打包与发布

```bash
# 打包
dotnet build src/AutoCode.Extensions.SourceGenerator/AutoCode.SourceGenerator.Extensions.csproj -c Publish

# 发布
nuget push src/.nuget/AM.AutoCode.1.2.0.nupkg YOUR_API_KEY -Source https://api.nuget.org/v3/index.json
```

### CI/CD

- **PR / push main**: 自动 build + test
- **Tag push (v\*)**: 自动 pack + publish NuGet
- 需在 GitHub Settings 配置 `NUGET_API_KEY` secret

## 技术亮点

- **IIncrementalGenerator**: 10 个生成器均采用 Roslyn 增量生成器，编译性能最优
- **增量缓存**: InterfaceSpecComparer 防止无关变更触发重复生成
- **Async 感知**: 接口生成器自动识别 Task<T>/ValueTask<T> 返回类型
- **XML 文档继承**: 接口生成器自动复制方法上的 /// 注释到生成的接口
- **Nullable 感知**: 生成的接口正确区分 string 和 string?
- **Swagger 注解**: Controller 生成器自动添加 [ProducesResponseType]/[Produces]
- **CreateSyntaxProvider**: 统一语法树级别特性匹配
- **FileScopedNamespace**: 支持 C# 10+ 文件范围命名空间
- **零文件写入**: 所有生成器仅使用 `context.AddSource`，不写磁盘
- **NativeAOT 兼容**: 编译时 DI 注册，零运行时反射
- **Analyzer + CodeFix**: 5 个诊断规则 + 2 个一键修复
- **架构守护**: 分层违规检测 (AC004) + 命名规范强制 (AC006)
- **MSBuild 配置**: 通过 .csproj 控制生成器行为

## 版本历史

| 版本 | 说明 |
|------|------|
| 1.3.0 | 深度提效：+3 生成器 (AutoTest/AutoLog/AutoCrud)、+2 分析器 (AC004/AC006)、Async/XML Doc/Nullable/Swagger 智能化增强 |
| 1.2.0 | 全面扩展：+4 生成器 (DTO/验证/Controller/DI)、+3 分析器、+2 CodeFix、CLI 工具、CI/CD、MSBuild 配置、23 个测试 |
| 1.1.x | 架构重构：IIncrementalGenerator 迁移、增量缓存、泛型/属性支持 |
| 1.0.x | ISourceGenerator 初始版本 |

## License

MIT
