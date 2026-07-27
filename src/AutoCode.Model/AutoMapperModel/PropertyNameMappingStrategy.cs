using System;
using System.Collections.Generic;
using System.Text;

namespace AutoCode.Model.AutoMapperModel
{
    /// <summary>
    /// 定义将属性映射到另一个属性时使用的策略。
    /// </summary>
    public enum PropertyNameMappingStrategy
    {
        /// <summary>
        /// 以区分大小写的方式按属性名称进行匹配。
        /// </summary>
        CaseSensitive,

        /// <summary>
        /// 以不区分大小写的方式按属性名称进行匹配。
        /// </summary>
        CaseInsensitive
    }
}
