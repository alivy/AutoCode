using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 自动验证器生成特性
    /// 标记在类上，根据 DataAnnotations 自动生成编译时验证代码
    /// </summary>
    /// <example>
    /// <code>
    /// [AutoValidator]
    /// public class CreateUserRequest
    /// {
    ///     [Required] [MaxLength(50)]
    ///     public string Name { get; set; }
    ///
    ///     [Range(0, 150)]
    ///     public int Age { get; set; }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoValidatorAttribute : Attribute
    {
    }
}
