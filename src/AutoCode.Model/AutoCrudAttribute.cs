using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 一键 CRUD 生成特性
    /// 标记在实体类上，自动生成 Service 接口 + 实现 + Controller
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoCrudAttribute : Attribute
    {
        /// <summary>
        /// API 路由前缀（默认 "api/[controller]"）
        /// </summary>
        public string RoutePrefix { get; set; } = string.Empty;

        /// <summary>
        /// 主键属性名（默认 "Id"）
        /// </summary>
        public string KeyName { get; set; } = "Id";
    }
}
