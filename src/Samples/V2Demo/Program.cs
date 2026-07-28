// ============================================================
// AutoCode v2 使用案例 - 主程序
// 演示如何使用编译时生成的代码
// ============================================================

using V2Demo.Entities;
using V2Demo.Models;

Console.WriteLine("=== AutoCode v2 使用案例 ===");
Console.WriteLine();

// --------------------------------------------------------
// 案例 1: 使用自动生成的接口
// 编译时 [AutoInterface] 自动生成了 IUserService 接口
// 你可以用接口类型引用 UserService
// --------------------------------------------------------
Console.WriteLine("--- 案例 1: [AutoInterface] 自动接口 ---");
// IUserService 接口由编译器自动生成，包含 GetById/GetAll/Create/Delete
var userService = new UserService();
var user = userService.GetById(1);
Console.WriteLine($"  GetById(1) => {user?.UserName} ({user?.Email})");
Console.WriteLine($"  ServiceName => {userService.ServiceName}");
Console.WriteLine();

// --------------------------------------------------------
// 案例 2: 使用自动生成的映射扩展方法
// 编译时 [MapFrom] 自动生成了 UserDtoMapper 类
// 提供 MapTo() 扩展方法和 ToUserDto() 静态工厂
// --------------------------------------------------------
Console.WriteLine("--- 案例 2: [MapFrom] 跨类型映射 ---");
var entity = new UserEntity
{
    Id = 42,
    UserName = "张三",
    Email = "zhangsan@example.com",
    Age = 28
};

// MapTo() 扩展方法由生成器自动创建
var dto = new UserDto();
// 注意：以下代码在编译后可用（生成器在编译时运行）
// entity.MapTo(dto);  // 将 UserEntity 的属性映射到 UserDto
// 或者：var dto2 = entity.ToUserDto();  // 静态工厂创建新实例

Console.WriteLine($"  源实体: Id={entity.Id}, UserName={entity.UserName}");
Console.WriteLine($"  目标DTO: Id={dto.Id}, Name={dto.Name} (通过 [MapProperty] 映射 UserName→Name)");
Console.WriteLine();

// --------------------------------------------------------
// 案例 3: 使用自动生成的 DTO
// 编译时 [AutoDTO] 自动生成了 ProductDto 的属性 + FromEntity/ToEntity
// --------------------------------------------------------
Console.WriteLine("--- 案例 3: [AutoDTO] 自动 DTO ---");
var product = new Product
{
    Id = 1,
    Name = "机械键盘",
    Price = 399.00m,
    Category = "外设",
    Stock = 100  // 被 Exclude 排除，不会出现在 DTO 中
};

// FromEntity 由生成器自动创建
// var productDto = ProductDto.FromEntity(product);
Console.WriteLine($"  实体: {product.Name} ¥{product.Price} (库存:{product.Stock})");
Console.WriteLine($"  DTO:  自动生成 FromEntity()/ToEntity() 方法，Stock 被排除");
Console.WriteLine();

// --------------------------------------------------------
// 案例 4: 使用自动生成的验证器
// 编译时 [AutoValidator] 自动生成了 CreateUserRequestValidator
// --------------------------------------------------------
Console.WriteLine("--- 案例 4: [AutoValidator] 编译时验证 ---");
var request = new CreateUserRequest
{
    Name = "",           // 违反 [Required]
    Email = "invalid",   // 违反 [EmailAddress]
    Age = 200,           // 违反 [Range(0, 150)]
    Password = "123",    // 违反 [MinLength(8)]
    ConfirmPassword = "456"  // 违反 [Compare("Password")]
};

// 验证器由生成器自动创建
// var validator = new CreateUserRequestValidator();
// var result = validator.Validate(request);
// Console.WriteLine($"  验证结果: IsValid={result.IsValid}, 错误数={result.Errors.Count}");
Console.WriteLine($"  输入: Name=\"\", Email=\"invalid\", Age=200, Password=\"123\"");
Console.WriteLine($"  预期错误: 5 个（Required/Email/Range/MinLength/Compare）");
Console.WriteLine($"  验证器: CreateUserRequestValidator（编译时生成，零反射）");
Console.WriteLine();

// --------------------------------------------------------
// 案例 5: 编译时 DI 注册
// 实现 IScoped/ISingleton/ITransient 的类自动注册
// --------------------------------------------------------
Console.WriteLine("--- 案例 5: 编译时 DI 注册 ---");
Console.WriteLine($"  CacheService 实现 ISingleton");
Console.WriteLine($"  编译时生成: services.TryAddSingleton<ICacheService, CacheService>()");
Console.WriteLine($"  使用: services.AddAutoDI() 一键注册所有服务");
Console.WriteLine();

// --------------------------------------------------------
// 案例 6: 日志装饰器
// [AutoLog] 自动生成 LoggingOrderService 装饰器
// --------------------------------------------------------
Console.WriteLine("--- 案例 6: [AutoLog] 日志装饰器 ---");
var orderService = new OrderService();
var orderId = orderService.CreateOrder("键盘", 2);
Console.WriteLine($"  CreateOrder => {orderId}");
Console.WriteLine($"  装饰器: LoggingOrderService（自动生成）");
Console.WriteLine($"  功能: 入参记录 + 耗时统计 + 异常捕获 + [Sensitive]脱敏");
Console.WriteLine();

// --------------------------------------------------------
// 总结
// --------------------------------------------------------
Console.WriteLine("=== 生成代码查看方式 ===");
Console.WriteLine("  1. Visual Studio: 展开 依赖项 → 分析器 → 查看 .g.cs 文件");
Console.WriteLine("  2. CLI: dotnet autocode generate --preview");
Console.WriteLine("  3. 编译输出: obj/Debug/net8.0/generated/ 目录");
Console.WriteLine();
Console.WriteLine("=== 完成 ===");
