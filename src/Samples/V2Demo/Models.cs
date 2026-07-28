// ============================================================
// AutoCode v2 使用案例 - 实体定义
// 演示：[AutoInterface] + [AutoLog] + DI 生命周期 + [AutoValidator]
// ============================================================

using AutoCode.Model;
using AutoCode.Model.InterfaceAttribute;
using System.ComponentModel.DataAnnotations;
using V2Demo.Entities;

namespace V2Demo.Entities
{
    /// <summary>
    /// 用户实体 - 演示多种特性组合使用
    /// </summary>
    public class UserEntity
    {
        public int Id { get; set; }
        public string UserName { get; set; } = "";
        public string Email { get; set; } = "";
        public int Age { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 产品实体
    /// </summary>
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public string Category { get; set; } = "";
        public int Stock { get; set; }
    }
}

namespace V2Demo.Models
{
    // ========================================================
    // 案例 1: [AutoInterface] - 自动接口提取
    // 编译时自动生成 IUserService 接口
    // ========================================================

    /// <summary>
    /// 用户服务 - 编译时自动提取接口 IUserService
    /// 生成的接口包含所有公共方法和属性
    /// </summary>
    [AutoInterface]
    public class UserService
    {
        public string ServiceName { get; set; } = "UserService";

        public UserEntity? GetById(int id)
        {
            // 模拟实现
            return new UserEntity { Id = id, UserName = "Demo", Email = "demo@test.com" };
        }

        public List<UserEntity> GetAll()
        {
            return new List<UserEntity>();
        }

        public UserEntity Create(string name, string email)
        {
            return new UserEntity { Id = 1, UserName = name, Email = email };
        }

        public bool Delete(int id) => true;

        // [AutoIgnore] 标记的方法不会出现在接口中
        [AutoIgnore]
        public void InternalCleanup() { }
    }

    // ========================================================
    // 案例 2: [MapFrom] - 跨类型智能映射
    // 编译时自动生成 UserDtoMapper 扩展方法类
    // ========================================================

    /// <summary>
    /// 用户 DTO - 从 UserEntity 自动映射
    /// 生成：MapTo() 扩展方法 + ToUserDto() 静态工厂
    /// </summary>
    [MapFrom(typeof(Entities.UserEntity))]
    public class UserDto
    {
        public int Id { get; set; }

        // 自定义属性映射：源类型中叫 UserName，目标叫 Name
        [MapProperty("UserName")]
        public string Name { get; set; } = "";

        public string Email { get; set; } = "";

        // 此属性在源类型中不存在，不会参与映射
        public string DisplayName { get; set; } = "";
    }

    // ========================================================
    // 案例 3: [AutoDTO] - 从实体自动生成 DTO
    // 编译时自动生成 ProductDto partial class + FromEntity/ToEntity
    // ========================================================

    /// <summary>
    /// 产品 DTO - 自动从 Product 实体生成
    /// 排除内部字段 Stock
    /// </summary>
    [AutoDTO(typeof(Entities.Product), Exclude = new[] { "Stock" })]
    public partial class ProductDto
    {
        // 属性和 FromEntity/ToEntity 方法由生成器自动填充
    }

    // ========================================================
    // 案例 4: [AutoValidator] - 编译时验证代码生成
    // 编译时自动生成 CreateUserRequestValidator 类
    // ========================================================

    /// <summary>
    /// 创建用户请求 - 自动生成验证器
    /// 支持：Required / MaxLength / EmailAddress / Range
    /// </summary>
    [AutoValidator]
    public class CreateUserRequest
    {
        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Range(0, 150)]
        public int Age { get; set; }

        [MinLength(8)]
        public string Password { get; set; } = "";

        [Compare("Password")]
        public string ConfirmPassword { get; set; } = "";
    }

    // ========================================================
    // 案例 5: DI 生命周期接口 - 编译时依赖注入注册
    // 实现 IScoped/ISingleton/ITransient 自动注册
    // ========================================================

    public interface IScoped { }
    public interface ISingleton { }
    public interface ITransient { }

    public interface ICacheService
    {
        string? Get(string key);
        void Set(string key, string value);
    }

    /// <summary>
    /// 缓存服务 - 实现 ISingleton 自动注册为单例
    /// 编译时生成：services.TryAddSingleton&lt;ICacheService, CacheService&gt;()
    /// </summary>
    public class CacheService : ICacheService, ISingleton
    {
        private readonly Dictionary<string, string> _cache = new();

        public string? Get(string key) => _cache.GetValueOrDefault(key);
        public void Set(string key, string value) => _cache[key] = value;
    }

    // ========================================================
    // 案例 6: [AutoLog] - 日志装饰器自动生成
    // 编译时自动生成 LoggingOrderService 装饰器类
    // ========================================================

    public interface IOrderService
    {
        string CreateOrder(string product, int quantity);
        Task<List<string>> GetOrdersAsync();
    }

    /// <summary>
    /// 订单服务 - 自动生成日志装饰器
    /// 装饰器功能：方法入参记录 + 耗时统计 + 异常捕获
    /// </summary>
    [AutoLog]
    public class OrderService : IOrderService
    {
        public string CreateOrder(string product, int quantity)
        {
            return $"ORD-{Guid.NewGuid():N}";
        }

        public async Task<List<string>> GetOrdersAsync()
        {
            await Task.Delay(10); // 模拟异步
            return new List<string> { "ORD-001", "ORD-002" };
        }
    }
}
