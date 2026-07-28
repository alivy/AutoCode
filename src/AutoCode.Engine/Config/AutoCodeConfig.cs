using System;
using System.Collections.Generic;

namespace AutoCode.Engine.Config
{
    /// <summary>
    /// AutoCode 统一配置接口 - 三层配置合并后的访问入口。
    /// 优先级：Attribute > autocode.json > MSBuild 全局
    /// </summary>
    public interface IAutoCodeConfig
    {
        /// <summary>获取字符串配置</summary>
        string GetString(string key, string defaultValue = "");

        /// <summary>获取布尔配置</summary>
        bool GetBoolean(string key, bool defaultValue = false);

        /// <summary>获取整数配置</summary>
        int GetInt(string key, int defaultValue = 0);

        /// <summary>获取枚举配置</summary>
        T GetEnum<T>(string key, T defaultValue) where T : struct, Enum;

        /// <summary>获取字符串数组配置</summary>
        IReadOnlyList<string> GetStringArray(string key);

        /// <summary>获取指定插件的配置节</summary>
        IAutoCodeConfig GetSection(string sectionName);

        /// <summary>是否包含指定键</summary>
        bool HasKey(string key);

        /// <summary>获取所有键</summary>
        IEnumerable<string> GetKeys();
    }

    /// <summary>
    /// 配置层来源
    /// </summary>
    public enum ConfigLayer
    {
        /// <summary>MSBuild 属性 (build_property.AutoCode_*)</summary>
        MSBuild,

        /// <summary>autocode.json 项目配置文件</summary>
        JsonFile,

        /// <summary>Attribute 命名参数</summary>
        Attribute
    }

    /// <summary>
    /// 三层配置系统实现 - 合并 MSBuild + JSON + Attribute 配置
    /// </summary>
    public sealed class AutoCodeConfig : IAutoCodeConfig
    {
        private readonly Dictionary<string, string> _values;
        private readonly string _prefix;

        public AutoCodeConfig() : this("", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
        }

        private AutoCodeConfig(string prefix, Dictionary<string, string> values)
        {
            _prefix = prefix;
            _values = values;
        }

        /// <summary>
        /// 从 MSBuild AnalyzerConfigOptions 加载配置
        /// </summary>
        public static AutoCodeConfig FromMSBuild(Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions options)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            const string prefix = "build_property.AutoCode_";

            // 通过已知的键名尝试读取
            var knownKeys = new[]
            {
                "InterfacePrefix", "GenerateNullable", "MapMethodName",
                "TemplateSuffix", "EnableDiagnostics", "ConventionMode",
                "OutputMode", "Namespace"
            };

            foreach (var key in knownKeys)
            {
                if (options.TryGetValue(prefix + key, out var value))
                {
                    values[ToConfigKey(key)] = value;
                }
            }

            return new AutoCodeConfig("", values);
        }

        /// <summary>
        /// 从 JSON 字符串加载配置
        /// </summary>
        public static AutoCodeConfig FromJson(string json)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // 简单 JSON 解析（netstandard2.0 无 System.Text.Json，用轻量解析）
            ParseJsonFlat(json, "", values);
            return new AutoCodeConfig("", values);
        }

        /// <summary>
        /// 合并多个配置源（后者覆盖前者）
        /// </summary>
        public static AutoCodeConfig Merge(params IAutoCodeConfig[] configs)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var config in configs)
            {
                foreach (var key in config.GetKeys())
                {
                    values[key] = config.GetString(key);
                }
            }
            return new AutoCodeConfig("", values);
        }

        /// <summary>
        /// 设置配置值（用于 Attribute 层覆盖）
        /// </summary>
        public AutoCodeConfig Set(string key, string value)
        {
            _values[key] = value;
            return this;
        }

        public string GetString(string key, string defaultValue = "")
        {
            var fullKey = GetFullKey(key);
            return _values.TryGetValue(fullKey, out var value) ? value : defaultValue;
        }

        public bool GetBoolean(string key, bool defaultValue = false)
        {
            var str = GetString(key, "");
            if (string.IsNullOrEmpty(str)) return defaultValue;
            return str.Equals("true", StringComparison.OrdinalIgnoreCase)
                || str == "1"
                || str.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            var str = GetString(key, "");
            return int.TryParse(str, out var result) ? result : defaultValue;
        }

        public T GetEnum<T>(string key, T defaultValue) where T : struct, Enum
        {
            var str = GetString(key, "");
            if (string.IsNullOrEmpty(str)) return defaultValue;
            return Enum.TryParse<T>(str, true, out var result) ? result : defaultValue;
        }

        public IReadOnlyList<string> GetStringArray(string key)
        {
            var str = GetString(key, "");
            if (string.IsNullOrEmpty(str)) return Array.Empty<string>();
            return str.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public IAutoCodeConfig GetSection(string sectionName)
        {
            var fullKey = GetFullKey(sectionName);
            return new AutoCodeConfig(fullKey + ".", _values);
        }

        public bool HasKey(string key)
        {
            return _values.ContainsKey(GetFullKey(key));
        }

        public IEnumerable<string> GetKeys()
        {
            if (string.IsNullOrEmpty(_prefix))
                return _values.Keys;

            var result = new List<string>();
            foreach (var key in _values.Keys)
            {
                if (key.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
                    result.Add(key.Substring(_prefix.Length));
            }
            return result;
        }

        private string GetFullKey(string key)
        {
            return string.IsNullOrEmpty(_prefix) ? key : _prefix + key;
        }

        /// <summary>
        /// 将 PascalCase 键名转换为 dot.separated 配置键
        /// </summary>
        private static string ToConfigKey(string pascalKey)
        {
            // InterfacePrefix -> interface.prefix
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < pascalKey.Length; i++)
            {
                var c = pascalKey[i];
                if (char.IsUpper(c) && i > 0)
                {
                    result.Append('.');
                    result.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    result.Append(char.ToLowerInvariant(c));
                }
            }
            return result.ToString();
        }

        /// <summary>
        /// 轻量级 JSON 扁平化解析（支持嵌套对象 → dot notation）
        /// </summary>
        private static void ParseJsonFlat(string json, string prefix, Dictionary<string, string> values)
        {
            // 极简解析：处理 "key": "value" 和 "key": { ... }
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            int i = 0;
            while (i < json.Length)
            {
                // 跳过空白和逗号
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ','))
                    i++;

                if (i >= json.Length) break;

                // 读取键
                if (json[i] != '"') { i++; continue; }
                var key = ReadJsonString(json, ref i);
                if (key == null) break;

                // 跳过冒号
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ':'))
                    i++;

                if (i >= json.Length) break;

                var fullKey = string.IsNullOrEmpty(prefix) ? key : prefix + "." + key;

                if (json[i] == '{')
                {
                    // 嵌套对象
                    var nested = ExtractBraceBlock(json, ref i);
                    ParseJsonFlat(nested, fullKey, values);
                }
                else if (json[i] == '[')
                {
                    // 数组 → 逗号分隔字符串
                    var arr = ExtractBracketBlock(json, ref i);
                    values[fullKey] = arr;
                }
                else if (json[i] == '"')
                {
                    var value = ReadJsonString(json, ref i);
                    if (value != null)
                        values[fullKey] = value;
                }
                else
                {
                    // 数字/布尔
                    var start = i;
                    while (i < json.Length && json[i] != ',' && json[i] != '}' && !char.IsWhiteSpace(json[i]))
                        i++;
                    values[fullKey] = json.Substring(start, i - start).Trim();
                }
            }
        }

        private static string? ReadJsonString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"') return null;
            i++; // skip opening quote
            var start = i;
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\') i++; // skip escaped char
                i++;
            }
            var result = json.Substring(start, i - start);
            if (i < json.Length) i++; // skip closing quote
            return result;
        }

        private static string ExtractBraceBlock(string json, ref int i)
        {
            int depth = 0;
            int start = i;
            while (i < json.Length)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) { i++; break; } }
                i++;
            }
            return json.Substring(start, i - start);
        }

        private static string ExtractBracketBlock(string json, ref int i)
        {
            i++; // skip [
            var start = i;
            int depth = 1;
            while (i < json.Length && depth > 0)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') depth--;
                if (depth > 0) i++;
            }
            var content = json.Substring(start, i - start);
            if (i < json.Length) i++; // skip ]
            // 去除引号，保留逗号分隔
            return content.Replace("\"", "").Trim();
        }
    }
}
