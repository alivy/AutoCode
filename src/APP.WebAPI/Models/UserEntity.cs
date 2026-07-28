namespace APP.WebAPI.Models
{
    /// <summary>
    /// 用户实体（数据库模型）
    /// </summary>
    public class UserEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Age { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PasswordHash { get; set; } = string.Empty;  // 不应暴露到 DTO
        public bool IsDeleted { get; set; }                        // 不应暴露到 DTO
    }
}
