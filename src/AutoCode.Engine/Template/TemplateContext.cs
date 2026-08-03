using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoCode.Engine.Template
{
    /// <summary>
    /// 模板上下文 - 存储模板渲染时可用的变量和集合。
    /// Source Generator 从 Roslyn 语法树提取类信息后填充此上下文。
    /// </summary>
    public class TemplateContext
    {
        private readonly Dictionary<string, object?> _variables =
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<TemplateItem>> _collections =
            new Dictionary<string, List<TemplateItem>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>设置标量变量</summary>
        public void Set(string name, object? value) => _variables[name] = value;

        /// <summary>设置集合变量（用于 {% for item in collection %}）</summary>
        public void SetCollection(string name, List<TemplateItem> items) => _collections[name] = items;

        /// <summary>解析变量路径（支持 "ClassName"、"Namespace" 等）</summary>
        public object? Resolve(string path)
        {
            if (_variables.TryGetValue(path, out var val)) return val;
            return null;
        }

        /// <summary>获取集合变量</summary>
        public List<TemplateItem>? GetCollection(string name)
        {
            return _collections.TryGetValue(name, out var list) ? list : null;
        }

        /// <summary>
        /// 从类信息快速构建模板上下文。
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="namespaceName">命名空间</param>
        /// <param name="methods">公共方法列表</param>
        /// <param name="properties">公共属性列表</param>
        /// <param name="interfaces">已实现接口列表</param>
        /// <param name="recipeName">配方名称</param>
        /// <param name="extraVars">额外变量</param>
        public static TemplateContext FromClassInfo(
            string className,
            string namespaceName,
            IEnumerable<MethodInfo> methods,
            IEnumerable<PropertyInfo> properties,
            IEnumerable<string>? interfaces = null,
            string? recipeName = null,
            Dictionary<string, object?>? extraVars = null)
        {
            var ctx = new TemplateContext();

            // 基本变量
            ctx.Set("ClassName", className);
            ctx.Set("SourceNamespace", namespaceName);
            ctx.Set("Namespace", namespaceName + ".Generated");
            ctx.Set("RecipeName", recipeName ?? "");
            ctx.Set("GeneratedDate", DateTime.Now.ToString("yyyy-MM-dd"));

            // 方法集合
            var methodItems = methods.Select(m => new TemplateItem(new Dictionary<string, string>
            {
                ["Name"] = m.Name,
                ["ReturnType"] = m.ReturnType,
                ["Parameters"] = m.Parameters,
                ["ArgumentNames"] = m.ArgumentNames,
                ["IsVoid"] = (m.ReturnType == "void" || m.ReturnType == "Task").ToString().ToLower(),
                ["IsAsync"] = m.ReturnType.StartsWith("Task") ? "true" : "false",
                ["IsPublic"] = "true",
                ["XmlDoc"] = m.XmlDoc ?? ""
            })).ToList();
            ctx.SetCollection("Methods", methodItems);

            // 属性集合
            var propItems = properties.Select(p => new TemplateItem(new Dictionary<string, string>
            {
                ["Name"] = p.Name,
                ["Type"] = p.Type,
                ["IsNullable"] = p.IsNullable.ToString().ToLower(),
                ["HasGetter"] = p.HasGetter.ToString().ToLower(),
                ["HasSetter"] = p.HasSetter.ToString().ToLower()
            })).ToList();
            ctx.SetCollection("Properties", propItems);

            // 接口集合
            if (interfaces != null)
            {
                var ifaceItems = interfaces.Select(i => new TemplateItem(new Dictionary<string, string>
                {
                    ["Name"] = i
                })).ToList();
                ctx.SetCollection("Interfaces", ifaceItems);
            }

            // 方法/属性计数（用于条件判断）
            ctx.Set("HasMethods", methodItems.Count > 0);
            ctx.Set("HasProperties", propItems.Count > 0);
            ctx.Set("MethodCount", methodItems.Count);
            ctx.Set("PropertyCount", propItems.Count);

            // 额外变量
            if (extraVars != null)
            {
                foreach (var kv in extraVars)
                    ctx.Set(kv.Key, kv.Value);
            }

            return ctx;
        }
    }

    /// <summary>
    /// 模板集合项 - 表示循环中的一个元素（如一个方法、一个属性）。
    /// </summary>
    public class TemplateItem
    {
        private readonly Dictionary<string, string> _properties;

        public TemplateItem(Dictionary<string, string> properties)
        {
            _properties = properties;
        }

        public string? GetProperty(string name)
        {
            return _properties.TryGetValue(name, out var val) ? val : null;
        }

        public override string ToString()
        {
            return _properties.TryGetValue("Name", out var name) ? name : "";
        }
    }

    /// <summary>
    /// 方法信息 - Source Generator 从 Roslyn 语法树提取的方法元数据。
    /// </summary>
    public class MethodInfo
    {
        public string Name { get; set; } = "";
        public string ReturnType { get; set; } = "void";
        /// <summary>完整参数声明（如 "int id, string name"）</summary>
        public string Parameters { get; set; } = "";
        /// <summary>参数名列表（如 "id, name"，用于方法调用）</summary>
        public string ArgumentNames { get; set; } = "";
        public string? XmlDoc { get; set; }
    }

    /// <summary>
    /// 属性信息 - Source Generator 从 Roslyn 语法树提取的属性元数据。
    /// </summary>
    public class PropertyInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsNullable { get; set; }
        public bool HasGetter { get; set; } = true;
        public bool HasSetter { get; set; } = true;
    }
}
