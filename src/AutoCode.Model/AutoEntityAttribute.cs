using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 级联生成标记 - 一个特性触发全链路代码生成。
    /// 自动生成：DTO + Mapper + Validator + Repository + Service + Controller + Tests
    /// </summary>
    /// <example>
    /// <code>
    /// [AutoEntity]
    /// public class Product
    /// {
    ///     public int Id { get; set; }
    ///     public string Name { get; set; }
    ///     public decimal Price { get; set; }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AutoEntityAttribute : Attribute
    {
        /// <summary>是否生成 DTO（默认 true）</summary>
        public bool GenerateDto { get; set; } = true;

        /// <summary>是否生成映射（默认 true）</summary>
        public bool GenerateMapper { get; set; } = true;

        /// <summary>是否生成验证（默认 true）</summary>
        public bool GenerateValidation { get; set; } = true;

        /// <summary>是否生成 Repository（默认 true）</summary>
        public bool GenerateRepository { get; set; } = true;

        /// <summary>是否生成 Service（默认 true）</summary>
        public bool GenerateService { get; set; } = true;

        /// <summary>是否生成 Controller（默认 true）</summary>
        public bool GenerateController { get; set; } = true;

        /// <summary>是否生成测试（默认 false）</summary>
        public bool GenerateTests { get; set; }

        /// <summary>是否生成日志装饰器（默认 false）</summary>
        public bool GenerateLogging { get; set; }

        /// <summary>主键属性名（默认 "Id"）</summary>
        public string KeyProperty { get; set; } = "Id";

        /// <summary>API 路由前缀（默认使用实体名复数小写）</summary>
        public string? RoutePrefix { get; set; }
    }

    /// <summary>
    /// 标记参数/属性为敏感数据 - 日志中自动脱敏
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class SensitiveAttribute : Attribute
    {
        /// <summary>脱敏方式</summary>
        public MaskMode Mode { get; set; } = MaskMode.Partial;
    }

    /// <summary>
    /// 脱敏模式
    /// </summary>
    public enum MaskMode
    {
        /// <summary>完全遮蔽 (****)</summary>
        Full,

        /// <summary>部分遮蔽 (保留前3后2: abc****xy)</summary>
        Partial,

        /// <summary>仅显示长度 ([Length=11])</summary>
        LengthOnly
    }

    /// <summary>
    /// 标记实体支持软删除
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class SoftDeleteAttribute : Attribute
    {
        /// <summary>软删除标记属性名（默认 "IsDeleted"）</summary>
        public string PropertyName { get; set; } = "IsDeleted";

        /// <summary>删除时间属性名（默认 "DeletedAt"）</summary>
        public string DeletedAtProperty { get; set; } = "DeletedAt";
    }

    /// <summary>
    /// 标记实体支持审计日志
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class AuditableAttribute : Attribute
    {
        /// <summary>创建者属性名</summary>
        public string CreatedByProperty { get; set; } = "CreatedBy";

        /// <summary>创建时间属性名</summary>
        public string CreatedAtProperty { get; set; } = "CreatedAt";

        /// <summary>修改者属性名</summary>
        public string ModifiedByProperty { get; set; } = "ModifiedBy";

        /// <summary>修改时间属性名</summary>
        public string ModifiedAtProperty { get; set; } = "ModifiedAt";
    }

    /// <summary>
    /// 自动 Builder 模式生成
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class AutoBuilderAttribute : Attribute
    {
        /// <summary>生成的 Builder 类名（默认 {ClassName}Builder）</summary>
        public string? BuilderName { get; set; }

        /// <summary>是否生成 Build() 方法中的验证（默认 true）</summary>
        public bool ValidateOnBuild { get; set; } = true;
    }

    /// <summary>
    /// 自动枚举扩展方法生成
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false)]
    public sealed class AutoEnumAttribute : Attribute
    {
        /// <summary>生成 DisplayName 扩展（从 DescriptionAttribute 读取）</summary>
        public bool GenerateDisplayName { get; set; } = true;

        /// <summary>生成 GetAll 方法</summary>
        public bool GenerateGetAll { get; set; } = true;

        /// <summary>生成安全 Parse 方法</summary>
        public bool GenerateTryParse { get; set; } = true;
    }
}
