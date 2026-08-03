using System;
using System.Collections.Generic;

namespace AutoCode.Analyzers.Recipes
{
    /// <summary>
    /// 统一的代码生成配方接口 - 内置生成器和自定义配方都实现此接口。
    /// 被 AutoCodeRefactoringProvider 消费，驱动 Ctrl+. 右键推荐。
    /// </summary>
    public interface ICodeGenRecipe
    {
        /// <summary>配方唯一标识（如 "autoEntity"、"auditService"）</summary>
        string Name { get; }

        /// <summary>Ctrl+. 显示文本</summary>
        string Title { get; }

        /// <summary>图标前缀（如 "🚀"、"📋"）</summary>
        string Icon { get; }

        /// <summary>分组：Entity / Service / Dto / Custom</summary>
        string Category { get; }

        /// <summary>要添加的 Attribute 名称（不含 Attribute 后缀）</summary>
        string AttributeName { get; }

        /// <summary>Attribute 构造参数表达式（如 "InterceptType.Log | InterceptType.Metrics"）</summary>
        string? AttributeArgument { get; }

        /// <summary>是否推荐此配方（根据类特征判断）</summary>
        bool IsApplicable(ClassAnalysisInfo classInfo);

        /// <summary>此配方是否已应用（类上已有对应 Attribute）</summary>
        bool IsAlreadyApplied(ClassAnalysisInfo classInfo);
    }

    /// <summary>
    /// 类分析信息 - RefactoringProvider 从语法树提取的类元数据。
    /// </summary>
    public class ClassAnalysisInfo
    {
        public string ClassName { get; set; } = "";
        public HashSet<string> ExistingAttributes { get; set; } = new HashSet<string>();
        public HashSet<string> Interfaces { get; set; } = new HashSet<string>();
        public List<string> PropertyNames { get; set; } = new List<string>();
        public List<string> MethodNames { get; set; } = new List<string>();
        public bool HasIdProperty { get; set; }
        public bool HasDataAnnotations { get; set; }
        public bool HasPublicMethods { get; set; }

        // 推断标志
        public bool IsEntity => HasIdProperty && !IsService && !IsRequest && !IsDto && !IsRepository;
        public bool IsService => ClassName.EndsWith("Service");
        public bool IsRequest => ClassName.EndsWith("Request") || ClassName.EndsWith("Command");
        public bool IsDto => ClassName.EndsWith("Dto") || ClassName.EndsWith("Response");
        public bool IsRepository => ClassName.EndsWith("Repository");
    }
}
