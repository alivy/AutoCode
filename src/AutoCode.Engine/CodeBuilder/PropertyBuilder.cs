using System;
using System.Collections.Generic;
using System.Text;

namespace AutoCode.Engine.CodeBuilder
{
    /// <summary>
    /// Fluent 属性构建器 - 构建属性定义。
    /// 支持 getter/setter 控制、访问修饰符、特性标注、XML 文档、初始值。
    /// </summary>
    public sealed class PropertyBuilder
    {
        private readonly string _name;
        private string _type = "object";
        private MemberAccess _accessibility = MemberAccess.Public;
        private MemberAccess? _getterAccess;
        private MemberAccess? _setterAccess;
        private bool _isStatic;
        private bool _isAbstract;
        private bool _isVirtual;
        private bool _isOverride;
        private bool _hasGetter = true;
        private bool _hasSetter = true;
        private bool _isReadOnly;
        private bool _isRequired;
        private string? _initializer;
        private string? _getterBody;
        private string? _setterBody;
        private string? _expressionBody;
        private readonly List<string> _attributes = new List<string>();
        private string? _xmlDocSummary;

        public PropertyBuilder(string name)
        {
            _name = name;
        }

        /// <summary>设置属性类型</summary>
        public PropertyBuilder Type(string type)
        {
            _type = type;
            return this;
        }

        /// <summary>设置访问修饰符</summary>
        public PropertyBuilder Access(MemberAccess access)
        {
            _accessibility = access;
            return this;
        }

        /// <summary>设为 public</summary>
        public PropertyBuilder Public() => Access(MemberAccess.Public);

        /// <summary>设为 private</summary>
        public PropertyBuilder Private() => Access(MemberAccess.Private);

        /// <summary>设为 internal</summary>
        public PropertyBuilder Internal() => Access(MemberAccess.Internal);

        /// <summary>设为 static</summary>
        public PropertyBuilder Static()
        {
            _isStatic = true;
            return this;
        }

        /// <summary>设为 abstract</summary>
        public PropertyBuilder Abstract()
        {
            _isAbstract = true;
            return this;
        }

        /// <summary>设为 virtual</summary>
        public PropertyBuilder Virtual()
        {
            _isVirtual = true;
            return this;
        }

        /// <summary>设为 override</summary>
        public PropertyBuilder Override()
        {
            _isOverride = true;
            return this;
        }

        /// <summary>设为 required（C# 11）</summary>
        public PropertyBuilder Required()
        {
            _isRequired = true;
            return this;
        }

        /// <summary>只读属性（无 setter）</summary>
        public PropertyBuilder ReadOnly()
        {
            _hasSetter = false;
            _isReadOnly = true;
            return this;
        }

        /// <summary>只写属性（无 getter）</summary>
        public PropertyBuilder WriteOnly()
        {
            _hasGetter = false;
            return this;
        }

        /// <summary>设置 getter 访问级别</summary>
        public PropertyBuilder GetterAccess(MemberAccess access)
        {
            _getterAccess = access;
            return this;
        }

        /// <summary>设置 setter 访问级别（如 private set）</summary>
        public PropertyBuilder SetterAccess(MemberAccess access)
        {
            _setterAccess = access;
            return this;
        }

        /// <summary>private set</summary>
        public PropertyBuilder PrivateSet()
        {
            _setterAccess = MemberAccess.Private;
            return this;
        }

        /// <summary>设置初始值</summary>
        public PropertyBuilder Initializer(string value)
        {
            _initializer = value;
            return this;
        }

        /// <summary>设置 getter 方法体</summary>
        public PropertyBuilder Getter(string body)
        {
            _getterBody = body;
            return this;
        }

        /// <summary>设置 setter 方法体</summary>
        public PropertyBuilder Setter(string body)
        {
            _setterBody = body;
            return this;
        }

        /// <summary>设置表达式体 (=> expression)</summary>
        public PropertyBuilder ExpressionBody(string expression)
        {
            _expressionBody = expression;
            return this;
        }

        /// <summary>添加特性标注</summary>
        public PropertyBuilder Attribute(string attributeCode)
        {
            _attributes.Add(attributeCode);
            return this;
        }

        /// <summary>设置 XML 文档</summary>
        public PropertyBuilder Doc(string summary)
        {
            _xmlDocSummary = summary;
            return this;
        }

        /// <summary>
        /// 构建属性代码
        /// </summary>
        internal string Build(string indent)
        {
            var sb = new StringBuilder();

            // XML Doc
            if (_xmlDocSummary != null)
            {
                sb.AppendLine($"{indent}/// <summary>");
                sb.AppendLine($"{indent}/// {_xmlDocSummary}");
                sb.AppendLine($"{indent}/// </summary>");
            }

            // Attributes
            foreach (var attr in _attributes)
                sb.AppendLine($"{indent}[{attr}]");

            // Declaration
            var declaration = BuildDeclaration();

            // Expression body
            if (_expressionBody != null)
            {
                sb.AppendLine($"{indent}{declaration} => {_expressionBody};");
                return sb.ToString();
            }

            // Auto-property or full property
            if (_getterBody == null && _setterBody == null)
            {
                // Auto-property
                var accessors = BuildAutoAccessors();
                var line = $"{indent}{declaration} {{ {accessors} }}";
                if (_initializer != null)
                    line += $" = {_initializer};";
                sb.AppendLine(line);
            }
            else
            {
                // Full property
                sb.AppendLine($"{indent}{declaration}");
                sb.AppendLine($"{indent}{{");
                if (_hasGetter && _getterBody != null)
                {
                    sb.AppendLine($"{indent}    get");
                    sb.AppendLine($"{indent}    {{");
                    foreach (var line in _getterBody.Split('\n'))
                        sb.AppendLine($"{indent}        {line.TrimEnd('\r')}");
                    sb.AppendLine($"{indent}    }}");
                }
                else if (_hasGetter)
                {
                    sb.AppendLine($"{indent}    get;");
                }

                if (_hasSetter && _setterBody != null)
                {
                    var setterPrefix = _setterAccess != null ? GetAccessKeyword(_setterAccess.Value) + " " : "";
                    sb.AppendLine($"{indent}    {setterPrefix}set");
                    sb.AppendLine($"{indent}    {{");
                    foreach (var line in _setterBody.Split('\n'))
                        sb.AppendLine($"{indent}        {line.TrimEnd('\r')}");
                    sb.AppendLine($"{indent}    }}");
                }
                else if (_hasSetter)
                {
                    var setterPrefix = _setterAccess != null ? GetAccessKeyword(_setterAccess.Value) + " " : "";
                    sb.AppendLine($"{indent}    {setterPrefix}set;");
                }
                sb.AppendLine($"{indent}}}");
            }

            return sb.ToString();
        }

        private string BuildDeclaration()
        {
            var parts = new List<string>();

            parts.Add(GetAccessKeyword(_accessibility));
            if (_isStatic) parts.Add("static");
            if (_isAbstract) parts.Add("abstract");
            if (_isVirtual) parts.Add("virtual");
            if (_isOverride) parts.Add("override");
            if (_isRequired) parts.Add("required");

            parts.Add(_type);
            parts.Add(_name);

            return string.Join(" ", parts);
        }

        private string BuildAutoAccessors()
        {
            var parts = new List<string>();

            if (_hasGetter)
            {
                var getterPrefix = _getterAccess != null ? GetAccessKeyword(_getterAccess.Value) + " " : "";
                parts.Add($"{getterPrefix}get;");
            }

            if (_hasSetter)
            {
                var setterPrefix = _setterAccess != null ? GetAccessKeyword(_setterAccess.Value) + " " : "";
                parts.Add($"{setterPrefix}set;");
            }

            if (_isReadOnly && !_hasSetter)
            {
                // init-only
                parts.Add("get; init;");
                parts.Clear();
                parts.Add("get;");
            }

            return string.Join(" ", parts);
        }

        private static string GetAccessKeyword(MemberAccess access)
        {
            return access switch
            {
                MemberAccess.Public => "public",
                MemberAccess.Internal => "internal",
                MemberAccess.Private => "private",
                MemberAccess.Protected => "protected",
                MemberAccess.ProtectedInternal => "protected internal",
                MemberAccess.PrivateProtected => "private protected",
                _ => "public"
            };
        }
    }

    /// <summary>
    /// Fluent 字段构建器
    /// </summary>
    public sealed class FieldBuilder
    {
        private readonly string _name;
        private string _type = "object";
        private MemberAccess _accessibility = MemberAccess.Private;
        private bool _isStatic;
        private bool _isReadOnly;
        private bool _isConst;
        private string? _initializer;
        private readonly List<string> _attributes = new List<string>();
        private string? _xmlDocSummary;

        public FieldBuilder(string name)
        {
            _name = name;
        }

        /// <summary>设置字段类型</summary>
        public FieldBuilder Type(string type)
        {
            _type = type;
            return this;
        }

        /// <summary>设置访问修饰符</summary>
        public FieldBuilder Access(MemberAccess access)
        {
            _accessibility = access;
            return this;
        }

        /// <summary>设为 public</summary>
        public FieldBuilder Public() => Access(MemberAccess.Public);

        /// <summary>设为 private</summary>
        public FieldBuilder Private() => Access(MemberAccess.Private);

        /// <summary>设为 internal</summary>
        public FieldBuilder Internal() => Access(MemberAccess.Internal);

        /// <summary>设为 static</summary>
        public FieldBuilder Static()
        {
            _isStatic = true;
            return this;
        }

        /// <summary>设为 readonly</summary>
        public FieldBuilder ReadOnly()
        {
            _isReadOnly = true;
            return this;
        }

        /// <summary>设为 const</summary>
        public FieldBuilder Const()
        {
            _isConst = true;
            return this;
        }

        /// <summary>设置初始值</summary>
        public FieldBuilder Initializer(string value)
        {
            _initializer = value;
            return this;
        }

        /// <summary>添加特性标注</summary>
        public FieldBuilder Attribute(string attributeCode)
        {
            _attributes.Add(attributeCode);
            return this;
        }

        /// <summary>设置 XML 文档</summary>
        public FieldBuilder Doc(string summary)
        {
            _xmlDocSummary = summary;
            return this;
        }

        /// <summary>
        /// 构建字段代码
        /// </summary>
        internal string Build(string indent)
        {
            var sb = new StringBuilder();

            if (_xmlDocSummary != null)
            {
                sb.AppendLine($"{indent}/// <summary>");
                sb.AppendLine($"{indent}/// {_xmlDocSummary}");
                sb.AppendLine($"{indent}/// </summary>");
            }

            foreach (var attr in _attributes)
                sb.AppendLine($"{indent}[{attr}]");

            var parts = new List<string>();
            parts.Add(GetAccessKeyword(_accessibility));
            if (_isConst) parts.Add("const");
            else
            {
                if (_isStatic) parts.Add("static");
                if (_isReadOnly) parts.Add("readonly");
            }
            parts.Add(_type);
            parts.Add(_name);

            var line = $"{indent}{string.Join(" ", parts)}";
            if (_initializer != null)
                line += $" = {_initializer}";
            line += ";";

            sb.AppendLine(line);
            return sb.ToString();
        }

        private static string GetAccessKeyword(MemberAccess access)
        {
            return access switch
            {
                MemberAccess.Public => "public",
                MemberAccess.Internal => "internal",
                MemberAccess.Private => "private",
                MemberAccess.Protected => "protected",
                MemberAccess.ProtectedInternal => "protected internal",
                MemberAccess.PrivateProtected => "private protected",
                _ => "private"
            };
        }
    }
}
