using System.Collections.Generic;
using System.Linq;

namespace AutoCode.Analyzers.Recipes
{
    /// <summary>
    /// 内置 11 个生成器的 ICodeGenRecipe 实现。
    /// 每个配方封装了触发条件判断逻辑，取代原 RefactoringProvider 中的硬编码 if-else。
    /// </summary>
    public static class BuiltInRecipes
    {
        public static readonly IReadOnlyList<ICodeGenRecipe> All = new ICodeGenRecipe[]
        {
            new AutoEntityRecipe(),
            new AutoDtoRecipe(),
            new AutoCrudRecipe(),
            new AutoValidatorRecipe(),
            new AutoInterfaceRecipe(),
            new AutoControllerRecipe(),
            new AutoLogRecipe(),
            new AutoTestRecipe(),
            new AutoInterceptRecipe(),
            new MapFromRecipe(),
            new ScopedRecipe(),
        };
    }

    #region Entity 类配方

    internal class AutoEntityRecipe : ICodeGenRecipe
    {
        public string Name => "autoEntity";
        public string Title => "[AutoEntity] 一键全链路（DTO+Mapper+Validator+API+DI）";
        public string Icon => "🚀";
        public string Category => "Entity";
        public string AttributeName => "AutoEntity";
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) => c.IsEntity;
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("AutoEntity") || c.ExistingAttributes.Contains("AutoEntityAttribute");
    }

    internal class AutoDtoRecipe : ICodeGenRecipe
    {
        public string Name => "autoDTO";
        public string Title => "[AutoDTO] 生成 DTO + FromEntity/ToEntity";
        public string Icon => "📋";
        public string Category => "Entity";
        public string AttributeName => "AutoDTO";
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) => c.IsEntity || c.IsRequest;
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("AutoDTO") || c.ExistingAttributes.Contains("AutoDTOAttribute");
    }

    internal class AutoCrudRecipe : ICodeGenRecipe
    {
        public string Name => "autoCrud";
        public string Title => "[AutoCrud] 生成 CRUD 全套（Service+Repository+Controller）";
        public string Icon => "🔄";
        public string Category => "Entity";
        public string AttributeName => "AutoCrud";
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) => c.IsEntity;
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("AutoCrud") || c.ExistingAttributes.Contains("AutoCrudAttribute");
    }

    internal class AutoValidatorRecipe : ICodeGenRecipe
    {
        public string Name => "autoValidator";
        public string Title => "[AutoValidator] 生成编译时验证代码";
        public string Icon => "✅";
        public string Category => "Entity";
        public string AttributeName => "AutoValidator";
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) =>
            (c.IsEntity && c.HasDataAnnotations) || c.IsRequest;
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("AutoValidator") || c.ExistingAttributes.Contains("AutoValidatorAttribute");
    }

    #endregion

    #region Service 类配方

    internal class AutoInterfaceRecipe : ICodeGenRecipe
    {
        public string Name => "autoInterface";
        public string Title => "[AutoInterface] 自动提取接口";
        public string Icon => "🔌";
        public string Category => "Service";
        public string AttributeName => "AutoInterface";
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) =>
            c.IsService || c.IsRepository || (c.HasPublicMethods && !c.IsEntity && !c.IsDto);
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("AutoInterface") || c.ExistingAttributes.Contains("AutoInterfaceAttribute");
    }

    internal class AutoControllerRecipe : ICodeGenRecipe
    {
        public string Name => "autoController";
        public string Title => "[AutoController] 生成 REST API Controller";
        public string Icon => "🌐";
        public string Category => "Service";
        public string AttributeName => "AutoController";
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) => c.IsService;
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("AutoController") || c.ExistingAttributes.Contains("AutoControllerAttribute");

        // 动态参数：RoutePrefix = "api/xxx"
        public string GetArgument(ClassAnalysisInfo c)
        {
            var entity = c.ClassName.Replace("Service", "");
            return $"RoutePrefix = \"api/{entity.ToLower()}s\"";
        }
    }

    internal class AutoLogRecipe : ICodeGenRecipe
    {
        public string Name => "autoLog";
        public string Title => "[AutoLog] 生成日志装饰器";
        public string Icon => "📝";
        public string Category => "Service";
        public string AttributeName => "AutoLog";
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) => c.IsService;
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("AutoLog") || c.ExistingAttributes.Contains("AutoLogAttribute");
    }

    internal class AutoTestRecipe : ICodeGenRecipe
    {
        public string Name => "autoTest";
        public string Title => "[AutoTest] 生成单元测试桩";
        public string Icon => "🧪";
        public string Category => "Service";
        public string AttributeName => "AutoTest";
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) => c.IsService;
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("AutoTest") || c.ExistingAttributes.Contains("AutoTestAttribute");
    }

    internal class AutoInterceptRecipe : ICodeGenRecipe
    {
        public string Name => "autoIntercept";
        public string Title => "[AutoIntercept] 添加 AOP 拦截管线";
        public string Icon => "⚡";
        public string Category => "Service";
        public string AttributeName => "AutoIntercept";
        public string? AttributeArgument => "InterceptType.Log | InterceptType.Metrics";
        public bool IsApplicable(ClassAnalysisInfo c) =>
            c.IsService || (c.HasPublicMethods && !c.IsEntity && !c.IsDto);
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("AutoIntercept") || c.ExistingAttributes.Contains("AutoInterceptAttribute");
    }

    #endregion

    #region Dto 类配方

    internal class MapFromRecipe : ICodeGenRecipe
    {
        public string Name => "mapFrom";
        public string Title => "[MapFrom] 生成编译时对象映射";
        public string Icon => "🗺️";
        public string Category => "Dto";
        public string AttributeName => "MapFrom";
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) => c.IsDto;
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.ExistingAttributes.Contains("MapFrom") || c.ExistingAttributes.Contains("MapFromAttribute");
    }

    #endregion

    #region DI 配方

    internal class ScopedRecipe : ICodeGenRecipe
    {
        public string Name => "scoped";
        public string Title => "实现 IScoped（编译时 DI 自动注册）";
        public string Icon => "💉";
        public string Category => "Service";
        public string AttributeName => "";  // 不加 Attribute，加接口
        public string? AttributeArgument => null;
        public bool IsApplicable(ClassAnalysisInfo c) =>
            (c.IsService || c.IsRepository) &&
            !c.Interfaces.Contains("IScoped") && !c.Interfaces.Contains("ISingleton");
        public bool IsAlreadyApplied(ClassAnalysisInfo c) =>
            c.Interfaces.Contains("IScoped") || c.Interfaces.Contains("ISingleton") ||
            c.Interfaces.Contains("ITransient");
    }

    #endregion
}
