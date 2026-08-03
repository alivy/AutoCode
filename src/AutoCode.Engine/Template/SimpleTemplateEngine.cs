using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoCode.Engine.Template
{
    /// <summary>
    /// 轻量级模板渲染引擎 - 兼容 Liquid 语法子集，零外部依赖。
    /// 支持：变量替换 {{ var }}、循环 {% for item in list %}、条件 {% if cond %}。
    /// </summary>
    public sealed class SimpleTemplateEngine
    {
        /// <summary>
        /// 渲染模板，将上下文变量替换为实际值。
        /// </summary>
        /// <param name="template">模板文本</param>
        /// <param name="context">变量上下文</param>
        /// <returns>渲染后的文本</returns>
        public string Render(string template, TemplateContext context)
        {
            if (string.IsNullOrEmpty(template)) return "";
            if (context == null) return template;

            var result = template;

            // 1. 处理 {% for item in collection %}...{% endfor %} 循环
            result = ProcessForLoops(result, context);

            // 2. 处理 {% if condition %}...{% endif %} 条件
            result = ProcessIfBlocks(result, context);

            // 3. 处理 {{ variable }} 变量替换
            result = ProcessVariables(result, context);

            return result;
        }

        #region 变量替换

        private string ProcessVariables(string template, TemplateContext context)
        {
            // 匹配 {{ variableName }} 或 {{ object.property }}
            return Regex.Replace(template, @"\{\{\s*([\w.]+)\s*\}\}", match =>
            {
                var path = match.Groups[1].Value;
                var value = context.Resolve(path);
                return value?.ToString() ?? "";
            });
        }

        #endregion

        #region For 循环

        private string ProcessForLoops(string template, TemplateContext context)
        {
            // {% for item in collection %}...{% endfor %}
            var pattern = @"\{%\s*for\s+(\w+)\s+in\s+(\w+)\s*%\}(.*?)\{%\s*endfor\s*%\}";
            return Regex.Replace(template, pattern, match =>
            {
                var itemName = match.Groups[1].Value;
                var collectionName = match.Groups[2].Value;
                var body = match.Groups[3].Value;

                var collection = context.GetCollection(collectionName);
                if (collection == null || !collection.Any()) return "";

                var sb = new StringBuilder();
                for (int i = 0; i < collection.Count; i++)
                {
                    var item = collection[i];
                    // 创建子上下文：item 的属性可以直接用 item.property 访问
                    var loopBody = body;

                    // 替换循环变量属性: {{ item.Property }}
                    loopBody = Regex.Replace(loopBody,
                        @"\{\{\s*" + Regex.Escape(itemName) + @"\.(\w+)\s*\}\}",
                        propMatch =>
                        {
                            var propName = propMatch.Groups[1].Value;
                            return item.GetProperty(propName) ?? "";
                        });

                    // 替换 {{ item }} 本身
                    loopBody = loopBody.Replace("{{ " + itemName + " }}", item.ToString() ?? "");

                    // 替换 forloop.index / forloop.first / forloop.last
                    loopBody = loopBody.Replace("{{ forloop.index }}", (i + 1).ToString());
                    loopBody = loopBody.Replace("{{ forloop.index0 }}", i.ToString());
                    loopBody = loopBody.Replace("{{ forloop.first }}", (i == 0).ToString().ToLower());
                    loopBody = loopBody.Replace("{{ forloop.last }}", (i == collection.Count - 1).ToString().ToLower());

                    sb.Append(loopBody);
                }
                return sb.ToString();
            }, RegexOptions.Singleline);
        }

        #endregion

        #region If 条件

        private string ProcessIfBlocks(string template, TemplateContext context)
        {
            // {% if condition %}...{% endif %} 和 {% if cond %}...{% else %}...{% endif %}
            var pattern = @"\{%\s*if\s+([\w.!]+)\s*%\}(.*?)(?:\{%\s*else\s*%\}(.*?))?\{%\s*endif\s*%\}";
            return Regex.Replace(template, pattern, match =>
            {
                var condition = match.Groups[1].Value;
                var trueBlock = match.Groups[2].Value;
                var falseBlock = match.Groups[3].Success ? match.Groups[3].Value : "";

                bool result = EvaluateCondition(condition, context);
                var selectedBlock = result ? trueBlock : falseBlock;

                // 递归处理嵌套 if
                return ProcessIfBlocks(selectedBlock, context);
            }, RegexOptions.Singleline);
        }

        private bool EvaluateCondition(string condition, TemplateContext context)
        {
            // 处理 ! 否定
            if (condition.StartsWith("!"))
            {
                return !EvaluateCondition(condition.Substring(1), context);
            }

            var value = context.Resolve(condition);
            if (value == null) return false;
            if (value is bool b) return b;
            if (value is string s) return !string.IsNullOrEmpty(s);
            if (value is int n) return n != 0;
            return true;
        }

        #endregion
    }
}
