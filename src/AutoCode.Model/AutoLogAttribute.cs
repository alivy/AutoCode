using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 自动日志装饰器生成特性
    /// 标记在 Service 类上，自动生成带日志的装饰器类
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoLogAttribute : Attribute
    {
        /// <summary>
        /// 是否记录方法参数（默认 true）
        /// </summary>
        public bool LogParameters { get; set; } = true;

        /// <summary>
        /// 是否记录执行耗时（默认 true）
        /// </summary>
        public bool LogElapsed { get; set; } = true;
    }
}
