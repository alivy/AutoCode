using System;
using System.Collections.Generic;

namespace AutoCode.Model
{
    /// <summary>
    /// 自定义代码生成配方 - 声明式定义代码生成规则。
    /// 同时被 Source Generator（编译时）和 RefactoringProvider（IDE）消费。
    /// 通过 autocode.json 的 customGenerators 节配置。
    /// </summary>
    public class CodeGenRecipe
    {
        /// <summary>配方唯一标识（如 "auditService"）</summary>
        public string Name { get; set; } = "";

        /// <summary>Ctrl+. 显示文本（如 "添加审计日志服务"）</summary>
        public string Title { get; set; } = "";

        /// <summary>Ctrl+. 图标前缀（如 "📋"）</summary>
        public string Icon { get; set; } = "⚙️";

        /// <summary>分组：Custom / Entity / Service / Dto</summary>
        public string Category { get; set; } = "Custom";

        /// <summary>触发条件</summary>
        public RecipeTrigger Trigger { get; set; } = new RecipeTrigger();

        /// <summary>输出配置</summary>
        public RecipeOutput Output { get; set; } = new RecipeOutput();
    }

    /// <summary>
    /// 配方触发条件 - 定义何种类/方法应该被此配方处理。
    /// 多个条件之间为 AND 关系（全部满足才触发）。
    /// </summary>
    public class RecipeTrigger
    {
        /// <summary>匹配的 Attribute 名称（如 "AutoAudit"，不含 Attribute 后缀）</summary>
        public string? AttributeName { get; set; }

        /// <summary>类名匹配模式（支持 * 通配符，如 "*Service"）</summary>
        public string? ClassPattern { get; set; }

        /// <summary>要求类实现的接口列表</summary>
        public string[]? RequiredInterfaces { get; set; }

        /// <summary>要求类包含的属性名列表</summary>
        public string[]? RequiredProperties { get; set; }

        /// <summary>要求类包含的方法名列表</summary>
        public string[]? RequiredMethods { get; set; }
    }

    /// <summary>
    /// 配方输出配置 - 定义生成文件的命名和模板。
    /// 支持占位符：{ClassName}、{RecipeName}、{SourceNamespace}
    /// </summary>
    public class RecipeOutput
    {
        /// <summary>DotLiquid 模板文件路径（如 "Templates/AuditService.liquid"）</summary>
        public string Template { get; set; } = "";

        /// <summary>
        /// 生成文件名模式（默认 "{ClassName}{RecipeName}.g.cs"）。
        /// 支持占位符：{ClassName}、{RecipeName}
        /// </summary>
        public string FileName { get; set; } = "{ClassName}{RecipeName}.g.cs";

        /// <summary>
        /// 生成代码的命名空间（默认 "{SourceNamespace}.Generated"）。
        /// 支持占位符：{SourceNamespace}
        /// </summary>
        public string Namespace { get; set; } = "{SourceNamespace}.Generated";
    }
}
