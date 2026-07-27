using System;

namespace AutoCode.Model.AutoMapperModel
{
    /// <summary>
    ///<see cref=“映射转换类型”/>表示一种转换类型
    ///如何将一种类型转换为另一种类型。
    /// </summary>
    [Flags]
    public enum MappingConversionType
    {
        /// <summary>
        /// None.
        /// </summary>
        None = 0,

        /// <summary>
        ///使用目标类型的构造函数，
        ///其接受源类型作为单个参数。
        /// </summary>
        Constructor = 1 << 0,

        /// <summary>
        /// 从源类型到目标类型的隐式转换。
        /// </summary>
        ImplicitCast = 1 << 1,

        /// <summary>
        ///从源类型到目标类型的显式转换。
        /// </summary>
        ExplicitCast = 1 << 2,

        /// <summary>
        ///如果源类型是<see cref=“string”/>，
        ///在目标类型上使用名为“Parse”的静态可见方法
        ///返回类型等于目标类型，字符串作为单个参数。
        /// </summary>
        ParseMethod = 1 << 3,

        /// <summary>
        ///如果目标类型是<see cref=“string”/>，
        ///在源类型上使用“To String”方法。
        /// </summary>
        ToStringMethod = 1 << 4,

        /// <summary>
        ///如果目标是<see cref=“Enum”/>
        ///并且源是一个<see cref=“string”/>，
        ///解析字符串以匹配枚举成员的名称。
        /// </summary>
        StringToEnum = 1 << 5,

        /// <summary>
        ///如果源是<see cref=“Enum”/>
        ///目标是一个<see cref=“string”/>，
        ///使用枚举成员的名称将其转换为字符串。
        /// </summary>
        EnumToString = 1 << 6,

        /// <summary>
        ///如果源是<see cref=“Enum”/>
        ///目标是另一个<see cref=“Enum”/>，
        ///根据<see cref=“枚举映射策略”/>进行映射。
        /// </summary>
        EnumToEnum = 1 << 7,

        /// <summary>
        ///如果来源是<see cref=“日期时间”/>
        ///目标是仅限日期
        ///在目标类型上使用“From Date-Time”方法，将源作为单个参数。
        /// </summary>
        DateTimeToDateOnly = 1 << 8,

        /// <summary>
        /// 如果来源是<see cref=“日期时间”/>
        ///目标是“仅限时间”
        ///在目标类型上使用“From Date-Time”方法，将源作为单个参数。
        /// </summary>
        DateTimeToTimeOnly = 1 << 9,

        /// <summary>
        /// 如果源和目标是一个<see cref=“IQuerying{T}”/>。
        ///仅使用对象初始化器并内联映射代码。
        /// </summary>
        Queryable = 1 << 10,

        /// <summary>
        ///如果源和目标是<see cref=“IEnumerable{T}”/>
        ///分别映射每个元素。
        /// </summary>
        Enumerable = 1 << 11,

        /// <summary>
        ///如果源和目标<参见cref=“IDictionary{TKey，TValue}”/>
        ///或<见cref=“I只读词典{TKey，TValue}”/>。
        ///分别映射每个<see cref=“键值对{TKey，TValue}”/>。
        /// </summary>
        Dictionary = 1 << 12,

        /// <summary>
        ///如果源或目标是跨度&lt；T&gt；或只读范围&lt；T&gt；
        ///分别映射每个元素。
        /// </summary>
        Span = 1 << 13,

        /// <summary>
        ///如果源或目标是存储器&lt；T&gt；或只读存储器&lt；T&gt；
        ///分别映射每个元素。
        /// </summary>
        Memory = 1 << 14,

        /// <summary>
        ///如果目标是<see cref=“Value Tuple{T，U}”/>或元组表达式（a:10，B:12）。
        ///支持位置映射和命名映射。
        ///仅在<see cref=“IQuery{T}”/>中使用<see cred=“Value Tuple{T，U}”/>。
        /// </summary>
        Tuple = 1 << 15,

        /// <summary>
        /// 允许使用枚举的基础类型从枚举类型映射到枚举类型。
        /// </summary>
        EnumUnderlyingType = 1 << 16,

        /// <summary>
        /// 启用所有支持的转换。
        /// </summary>
        All = ~None,
    }
}
