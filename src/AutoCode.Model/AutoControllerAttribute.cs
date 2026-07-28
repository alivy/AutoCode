using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 自动 API Controller 生成特性
    /// 标记在 Service 类上，自动生成对应的 API Controller
    /// </summary>
    /// <example>
    /// <code>
    /// [AutoController(RoutePrefix = "api/users")]
    /// public class UserService : IUserService
    /// {
    ///     public UserDto GetById(int id) { ... }
    ///     public void Create(CreateUserRequest req) { ... }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoControllerAttribute : Attribute
    {
        /// <summary>
        /// 路由前缀（如 "api/users"）
        /// </summary>
        public string RoutePrefix { get; set; } = string.Empty;

        /// <summary>
        /// API 版本号（如 "v1"）
        /// </summary>
        public string? ApiVersion { get; set; }
    }
}
