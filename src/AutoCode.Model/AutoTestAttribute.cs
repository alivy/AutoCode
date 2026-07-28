using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 自动测试桩生成特性
    /// 标记在类上，自动生成 xUnit 测试类桩
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoTestAttribute : Attribute
    {
        /// <summary>
        /// 测试类名后缀（默认 "Tests"）
        /// </summary>
        public string Suffix { get; set; } = "Tests";
    }
}
