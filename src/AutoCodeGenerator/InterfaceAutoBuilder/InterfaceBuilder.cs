using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AutoCode.SourceGenerator.InterfaceAutoBuilder
{
    /// <summary>
    /// 接口代码构建器
    /// </summary>
    internal static class InterfaceBuilder
    {
        /// <summary>
        /// 构建接口源代码字符串
        /// </summary>
        public static string BuildInterface(InterfaceSpec spec)
        {
            var sb = new StringBuilder();

            // 生成 using 指令
            foreach (var ns in spec.Usings)
            {
                if (!string.IsNullOrEmpty(ns))
                    sb.AppendLine($"using {ns};");
            }

            if (sb.Length > 0)
                sb.AppendLine();

            // 生成命名空间
            if (!string.IsNullOrEmpty(spec.NamespaceName))
            {
                sb.AppendLine($"namespace {spec.NamespaceName}");
                sb.AppendLine("{");
            }

            var indent = string.IsNullOrEmpty(spec.NamespaceName) ? "" : "    ";
            sb.AppendLine($"{indent}public interface {spec.InterfaceName}");
            sb.AppendLine($"{indent}{{");

            // 生成属性签名
            foreach (var property in spec.Properties)
            {
                var accessors = new List<string>();
                if (property.HasGetter) accessors.Add("get");
                if (property.HasSetter) accessors.Add("set");
                var accessorList = string.Join("; ", accessors);
                sb.AppendLine($"{indent}    {property.Type} {property.Name} {{ {accessorList}; }}");
            }

            if (spec.Properties.Count > 0 && spec.Methods.Count > 0)
                sb.AppendLine();

            // 生成方法签名
            foreach (var method in spec.Methods)
            {
                // 输出 XML 文档注释
                if (!string.IsNullOrEmpty(method.XmlDoc))
                {
                    var docLines = method.XmlDoc.Split('\n');
                    sb.AppendLine($"{indent}    /// <summary>");
                    foreach (var line in docLines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("<summary>") || trimmed.StartsWith("</summary>"))
                            continue;
                        if (trimmed.StartsWith("<param") || trimmed.StartsWith("<returns"))
                        {
                            sb.AppendLine($"{indent}    /// {trimmed}");
                        }
                        else if (trimmed.Length > 0)
                        {
                            sb.AppendLine($"{indent}    /// {trimmed}");
                        }
                    }
                    sb.AppendLine($"{indent}    /// </summary>");
                }

                var parameters = method.Parameters.Count > 0
                    ? string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"))
                    : string.Empty;

                var typeParams = method.TypeParameters ?? string.Empty;
                sb.AppendLine($"{indent}    {method.ReturnType} {method.Name}{typeParams}({parameters});");
            }

            sb.AppendLine($"{indent}}}");

            if (!string.IsNullOrEmpty(spec.NamespaceName))
                sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
