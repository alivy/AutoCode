using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 自动 DTO 生成特性
    /// 标记在 partial 类上，根据源实体类型自动生成 DTO 属性和转换方法
    /// </summary>
    /// <example>
    /// <code>
    /// [AutoDTO(typeof(UserEntity), Include = new[] { "Id", "Name", "Email" })]
    /// public partial class UserDto { }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoDTOAttribute : Attribute
    {
        /// <summary>
        /// 源实体类型
        /// </summary>
        public Type SourceType { get; }

        /// <summary>
        /// 包含的属性名列表（为空则包含所有）
        /// </summary>
        public string[]? Include { get; set; }

        /// <summary>
        /// 排除的属性名列表
        /// </summary>
        public string[]? Exclude { get; set; }

        /// <summary>
        /// DTO 类名后缀（默认 "Dto"）
        /// </summary>
        public string Suffix { get; set; } = "Dto";

        public AutoDTOAttribute(Type sourceType)
        {
            SourceType = sourceType;
        }
    }
}
