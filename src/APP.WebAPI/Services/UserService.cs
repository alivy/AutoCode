using AutoCode.Model;
using AutoCode.Model.InterfaceAttribute;
using APP.WebAPI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace APP.WebAPI.Services
{
    /// <summary>
    /// 用户服务 - 综合示例，展示多个生成器协同工作
    ///
    /// [AutoInterface]  → 自动生成 IUserService 接口
    /// [AutoController] → 自动生成 UserServiceController (API 控制器)
    /// IScoped          → 自动生成 DI 注册 (TryAddScoped)
    /// </summary>
    [AutoInterface]
    [AutoController(RoutePrefix = "api/users")]
    public class UserService : IUserService, IScoped
    {
        // 模拟数据存储
        private static readonly List<UserEntity> _users = new()
        {
            new UserEntity { Id = 1, Name = "Alice", Email = "alice@example.com", Age = 30, CreatedAt = DateTime.Now },
            new UserEntity { Id = 2, Name = "Bob", Email = "bob@example.com", Age = 25, CreatedAt = DateTime.Now },
        };

        /// <summary>
        /// 获取所有用户 → 生成 [HttpGet]
        /// </summary>
        public List<UserListItemDto> GetAll()
        {
            return _users.Select(u => UserListItemDto.FromEntity(u)).ToList();
        }

        /// <summary>
        /// 根据 ID 获取用户 → 生成 [HttpGet("{id}")]
        /// </summary>
        public UserDto? GetById(int id)
        {
            var entity = _users.FirstOrDefault(u => u.Id == id);
            return entity != null ? UserDto.FromEntity(entity) : null;
        }

        /// <summary>
        /// 创建用户 → 生成 [HttpPost]
        /// </summary>
        public UserDto Create(CreateUserRequest request)
        {
            // 使用自动生成的验证器
            var validator = new CreateUserRequestValidator();
            var errors = validator.Validate(request);
            if (errors.Count > 0)
                throw new ArgumentException(string.Join("; ", errors));

            var entity = new UserEntity
            {
                Id = _users.Max(u => u.Id) + 1,
                Name = request.Name,
                Email = request.Email,
                Age = request.Age,
                CreatedAt = DateTime.Now,
                PasswordHash = HashPassword(request.Password),
            };
            _users.Add(entity);
            return UserDto.FromEntity(entity);
        }

        /// <summary>
        /// 更新用户 → 生成 [HttpPut("{id}")]
        /// </summary>
        public UserDto? Update(int id, UpdateUserRequest request)
        {
            var entity = _users.FirstOrDefault(u => u.Id == id);
            if (entity == null) return null;

            if (request.Name != null) entity.Name = request.Name;
            if (request.Age.HasValue) entity.Age = request.Age.Value;

            return UserDto.FromEntity(entity);
        }

        /// <summary>
        /// 删除用户 → 生成 [HttpDelete("{id}")]
        /// </summary>
        public void Delete(int id)
        {
            var entity = _users.FirstOrDefault(u => u.Id == id);
            if (entity != null)
                _users.Remove(entity);
        }

        [AutoIgnore]  // 不会出现在 IUserService 接口中
        private static string HashPassword(string password) => $"hashed_{password}";
    }
}
