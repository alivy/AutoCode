using AutoCode.Model;

namespace APP.WebAPI.Models
{
    /// <summary>
    /// 用户 DTO - 使用 [AutoDTO] 自动生成
    /// 排除 PasswordHash 和 IsDeleted 等敏感字段
    ///
    /// 生成器会自动生成:
    /// - Id, Name, Email, Age, CreatedAt 属性
    /// - static UserDto FromEntity(UserEntity entity) 方法
    /// - void ToEntity(UserEntity entity) 方法
    /// </summary>
    [AutoDTO(typeof(UserEntity), Exclude = new[] { "PasswordHash", "IsDeleted" })]
    public partial class UserDto
    {
        // 属性和方法由生成器自动填充，无需手动编写
    }

    /// <summary>
    /// 用户列表项 DTO - 只包含 Id 和 Name
    /// </summary>
    [AutoDTO(typeof(UserEntity), Include = new[] { "Id", "Name" })]
    public partial class UserListItemDto
    {
    }
}
