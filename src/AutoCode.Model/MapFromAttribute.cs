using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 标记目标类从指定源类型生成映射代码。
    /// 支持跨类型智能映射：自动匹配同名属性、嵌套对象、集合。
    /// </summary>
    /// <example>
    /// <code>
    /// [MapFrom(typeof(UserEntity))]
    /// public class UserDto
    /// {
    ///     public int Id { get; set; }
    ///     public string Name { get; set; }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
    public sealed class MapFromAttribute : Attribute
    {
        /// <summary>源类型（映射数据来源）</summary>
        public Type SourceType { get; }

        /// <summary>映射方向（默认双向）</summary>
        public MapDirection Direction { get; set; } = MapDirection.Both;

        /// <summary>Null 值处理策略</summary>
        public NullHandling NullHandling { get; set; } = NullHandling.Skip;

        /// <summary>集合映射策略</summary>
        public CollectionMapping CollectionMapping { get; set; } = CollectionMapping.DeepCopy;

        /// <summary>是否生成 IQueryable 投影表达式（用于 EF Core）</summary>
        public bool GenerateProjection { get; set; }

        public MapFromAttribute(Type sourceType)
        {
            SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
        }
    }

    /// <summary>
    /// 自定义属性映射 - 当源和目标属性名不同时使用
    /// </summary>
    /// <example>
    /// <code>
    /// [MapFrom(typeof(UserEntity))]
    /// public class UserDto
    /// {
    ///     [MapProperty("UserName")]  // 源类型中的属性名
    ///     public string Name { get; set; }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class MapPropertyAttribute : Attribute
    {
        /// <summary>源类型中对应的属性名</summary>
        public string SourceName { get; }

        /// <summary>自定义转换表达式（可选）</summary>
        public string? Converter { get; set; }

        public MapPropertyAttribute(string sourceName)
        {
            SourceName = sourceName ?? throw new ArgumentNullException(nameof(sourceName));
        }
    }

    /// <summary>
    /// 标记属性在映射时忽略
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class MapIgnoreAttribute : Attribute
    {
    }

    /// <summary>
    /// 映射方向
    /// </summary>
    public enum MapDirection
    {
        /// <summary>仅 Source → Target</summary>
        OneWay,

        /// <summary>双向映射</summary>
        Both,

        /// <summary>仅 Target → Source（反向）</summary>
        Reverse
    }

    /// <summary>
    /// Null 值处理策略
    /// </summary>
    public enum NullHandling
    {
        /// <summary>跳过 null 值（不覆盖目标）</summary>
        Skip,

        /// <summary>将 null 赋值到目标</summary>
        SetNull,

        /// <summary>使用默认值替代 null</summary>
        Default
    }

    /// <summary>
    /// 集合映射策略
    /// </summary>
    public enum CollectionMapping
    {
        /// <summary>深拷贝（创建新集合 + 逐元素映射）</summary>
        DeepCopy,

        /// <summary>浅拷贝（创建新集合，元素引用相同）</summary>
        ShallowCopy,

        /// <summary>直接引用赋值</summary>
        Reference
    }
}
