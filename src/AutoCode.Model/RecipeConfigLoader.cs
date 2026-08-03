using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AutoCode.Model
{
    /// <summary>
    /// 自定义配方加载器 - 从 autocode.json 的 customGenerators 节解析 CodeGenRecipe 列表。
    /// 同时被 Source Generator（编译时）和 RefactoringProvider（IDE 分析器）消费。
    /// </summary>
    public static class RecipeConfigLoader
    {
        /// <summary>
        /// 从 autocode.json 的完整 JSON 文本中解析 customGenerators 列表。
        /// </summary>
        public static List<CodeGenRecipe> LoadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<CodeGenRecipe>();

            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (!doc.RootElement.TryGetProperty("customGenerators", out var arr) ||
                    arr.ValueKind != JsonValueKind.Array)
                    return new List<CodeGenRecipe>();

                var recipes = new List<CodeGenRecipe>();
                foreach (var elem in arr.EnumerateArray())
                {
                    var recipe = ParseRecipe(elem);
                    if (recipe != null && !string.IsNullOrEmpty(recipe.Name))
                        recipes.Add(recipe);
                }
                return recipes;
            }
            catch
            {
                return new List<CodeGenRecipe>();
            }
        }

        private static CodeGenRecipe? ParseRecipe(JsonElement elem)
        {
            var recipe = new CodeGenRecipe();

            if (elem.TryGetProperty("name", out var name))
                recipe.Name = name.GetString() ?? "";
            if (elem.TryGetProperty("title", out var title))
                recipe.Title = title.GetString() ?? "";
            if (elem.TryGetProperty("icon", out var icon))
                recipe.Icon = icon.GetString() ?? "⚙️";
            if (elem.TryGetProperty("category", out var cat))
                recipe.Category = cat.GetString() ?? "Custom";

            if (elem.TryGetProperty("trigger", out var trigger))
            {
                if (trigger.TryGetProperty("attributeName", out var attrName))
                    recipe.Trigger.AttributeName = attrName.GetString();
                if (trigger.TryGetProperty("classPattern", out var pattern))
                    recipe.Trigger.ClassPattern = pattern.GetString();
                if (trigger.TryGetProperty("requiredInterfaces", out var ifaces))
                    recipe.Trigger.RequiredInterfaces = ifaces.EnumerateArray()
                        .Select(e => e.GetString()).Where(s => s != null).ToArray()!;
                if (trigger.TryGetProperty("requiredProperties", out var props))
                    recipe.Trigger.RequiredProperties = props.EnumerateArray()
                        .Select(e => e.GetString()).Where(s => s != null).ToArray()!;
                if (trigger.TryGetProperty("requiredMethods", out var methods))
                    recipe.Trigger.RequiredMethods = methods.EnumerateArray()
                        .Select(e => e.GetString()).Where(s => s != null).ToArray()!;
            }

            if (elem.TryGetProperty("output", out var output))
            {
                if (output.TryGetProperty("template", out var tmpl))
                    recipe.Output.Template = tmpl.GetString() ?? "";
                if (output.TryGetProperty("fileName", out var fn))
                    recipe.Output.FileName = fn.GetString() ?? "{ClassName}{RecipeName}.g.cs";
                if (output.TryGetProperty("namespace", out var ns))
                    recipe.Output.Namespace = ns.GetString() ?? "{SourceNamespace}.Generated";
            }

            return recipe;
        }

        /// <summary>
        /// 将类名与通配符模式匹配（支持 * 前缀/后缀/中间通配）。
        /// 例：MatchPattern("OrderService", "*Service") → true
        /// </summary>
        public static bool MatchPattern(string className, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (pattern == "*") return true;

            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            {
                var middle = pattern.Substring(1, pattern.Length - 2);
                return className.IndexOf(middle, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (pattern.StartsWith("*"))
            {
                var suffix = pattern.Substring(1);
                return className.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }
            if (pattern.EndsWith("*"))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(className, pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
