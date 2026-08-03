using System;

namespace AutoCode.Model
{
    /// <summary>
    /// 通用自定义生成标记 - 配合 autocode.json 中的 customGenerators 使用。
    /// 在类上添加 [CustomGenerate("recipeName")] 即可触发对应名称的自定义配方。
    /// </summary>
    /// <example>
    /// <code>
    /// // autocode.json 中定义:
    /// // { "name": "auditService", "trigger": { "attributeName": "AutoAudit" }, ... }
    /// 
    /// [CustomGenerate("auditService")]
    /// public class OrderService { ... }
    /// // 编译时自动生成 OrderServiceAudit.g.cs
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
    public class CustomGenerateAttribute : Attribute
    {
        /// <summary>
        /// 配方名称 - 对应 autocode.json 中 customGenerators[].name
        /// </summary>
        public string RecipeName { get; }

        public CustomGenerateAttribute(string recipeName)
        {
            RecipeName = recipeName ?? throw new ArgumentNullException(nameof(recipeName));
        }
    }
}
