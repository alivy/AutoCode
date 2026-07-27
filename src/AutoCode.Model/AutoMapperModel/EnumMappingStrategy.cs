using System;
using System.Collections.Generic;
using System.Text;

namespace AutoCode.Model.AutoMapperModel
{
    /// <summary>
    ///定义将枚举映射到另一个枚举时使用的策略。
    /// </summary>
    public enum EnumMappingStrategy
    {
        /// <summary>
        /// 根据枚举成员的值进行匹配。
        /// </summary>
        ByValue,

        /// <summary>
        /// 按名称匹配枚举成员。
        /// </summary>
        ByName,

        /// <summary>
        /// 根据枚举成员的值进行匹配。
        ///检查枚举中是否定义了该值。
        /// </summary>
        ByValueCheckDefined,
    }
}
