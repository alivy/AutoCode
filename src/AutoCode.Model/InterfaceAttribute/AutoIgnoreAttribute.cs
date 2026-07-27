using System;

namespace AutoCode.Model.InterfaceAttribute
{
    /// <summary>
    /// 生成忽略标记
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class AutoIgnoreAttribute : Attribute
    {
        /// <summary>
        /// 忽略json文件生成
        /// </summary>
        public bool IgnoreJson { get; set; }

        public AutoIgnoreAttribute()
        {

        }


        public AutoIgnoreAttribute(bool ignoreJson)
        {
            IgnoreJson = ignoreJson;
        }
    }
}
