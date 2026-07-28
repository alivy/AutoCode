using AutoCode.Model;
using System.ComponentModel.DataAnnotations;

namespace APP.WebAPI.Models
{
    /// <summary>
    /// 创建用户请求 - 使用 [AutoValidator] 自动生成验证代码
    ///
    /// 生成器会自动生成 CreateUserRequestValidator 类:
    /// - public List&lt;string&gt; Validate(CreateUserRequest input)
    /// - 根据 DataAnnotations 生成对应的验证逻辑
    /// </summary>
    [AutoValidator]
    public class CreateUserRequest
    {
        [Required]
        [MaxLength(60)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Range(0, 150)]
        public int Age { get; set; }

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Url]
        public string? AvatarUrl { get; set; }
    }

    /// <summary>
    /// 更新用户请求
    /// </summary>
    [AutoValidator]
    public class UpdateUserRequest
    {
        [Required]
        public int Id { get; set; }

        [MaxLength(50)]
        public string? Name { get; set; }

        [Range(0, 150)]
        public int? Age { get; set; }
    }
}
