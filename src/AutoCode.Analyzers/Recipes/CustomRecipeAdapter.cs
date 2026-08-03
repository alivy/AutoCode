using System;
using System.Collections.Generic;
using System.Linq;
using AutoCode.Model;

namespace AutoCode.Analyzers.Recipes
{
    /// <summary>
    /// 自定义配方适配器 - 将 autocode.json 中的 CodeGenRecipe 转换为 ICodeGenRecipe 接口。
    /// 让自定义配方与内置配方在 Ctrl+. 推荐中统一展示。
    /// </summary>
    internal class CustomRecipeAdapter : ICodeGenRecipe
    {
        private readonly CodeGenRecipe _recipe;

        public CustomRecipeAdapter(CodeGenRecipe recipe)
        {
            _recipe = recipe;
        }

        public string Name => _recipe.Name;
        public string Title => $"{_recipe.Icon} [自定义] {_recipe.Title}";
        public string Icon => _recipe.Icon;
        public string Category => _recipe.Category;

        public string AttributeName =>
            !string.IsNullOrEmpty(_recipe.Trigger.AttributeName)
                ? _recipe.Trigger.AttributeName!
                : "CustomGenerate";

        public string? AttributeArgument =>
            !string.IsNullOrEmpty(_recipe.Trigger.AttributeName)
                ? null
                : $"\"{_recipe.Name}\"";

        public bool IsApplicable(ClassAnalysisInfo classInfo)
        {
            var trigger = _recipe.Trigger;

            // classPattern 匹配
            if (!string.IsNullOrEmpty(trigger.ClassPattern))
            {
                if (!RecipeConfigLoader.MatchPattern(classInfo.ClassName, trigger.ClassPattern!))
                    return false;
            }

            // requiredProperties
            if (trigger.RequiredProperties != null && trigger.RequiredProperties.Length > 0)
            {
                if (!trigger.RequiredProperties.All(rp =>
                    classInfo.PropertyNames.Any(p => string.Equals(p, rp, StringComparison.OrdinalIgnoreCase))))
                    return false;
            }

            // requiredMethods
            if (trigger.RequiredMethods != null && trigger.RequiredMethods.Length > 0)
            {
                if (!trigger.RequiredMethods.All(rm =>
                    classInfo.MethodNames.Any(m => string.Equals(m, rm, StringComparison.OrdinalIgnoreCase))))
                    return false;
            }

            // requiredInterfaces
            if (trigger.RequiredInterfaces != null && trigger.RequiredInterfaces.Length > 0)
            {
                if (!trigger.RequiredInterfaces.All(ri =>
                    classInfo.Interfaces.Any(i => string.Equals(i, ri, StringComparison.OrdinalIgnoreCase))))
                    return false;
            }

            return true;
        }

        public bool IsAlreadyApplied(ClassAnalysisInfo classInfo)
        {
            // 检查类上是否已有对应的 Attribute 或 CustomGenerate("recipeName")
            if (!string.IsNullOrEmpty(_recipe.Trigger.AttributeName))
            {
                return classInfo.ExistingAttributes.Contains(_recipe.Trigger.AttributeName!) ||
                       classInfo.ExistingAttributes.Contains(_recipe.Trigger.AttributeName + "Attribute");
            }

            // 对于 [CustomGenerate("name")] 模式，暂无法精确检测，返回 false
            return false;
        }

        /// <summary>
        /// 从 autocode.json 内容加载自定义配方并转换为 ICodeGenRecipe 列表。
        /// </summary>
        public static List<ICodeGenRecipe> LoadFromConfigJson(string json)
        {
            var recipes = RecipeConfigLoader.LoadFromJson(json);
            return recipes.Select(r => (ICodeGenRecipe)new CustomRecipeAdapter(r)).ToList();
        }
    }
}
