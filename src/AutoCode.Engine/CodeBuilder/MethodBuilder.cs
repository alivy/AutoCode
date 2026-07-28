using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AutoCode.Engine.CodeBuilder
{
    /// <summary>
    /// Fluent 方法构建器 - 构建方法签名 + 方法体。
    /// 支持 async、泛型、参数注解、XML 文档、特性标注。
    /// </summary>
    public sealed class MethodBuilder
    {
        private readonly string _name;
        private MemberAccess _accessibility = MemberAccess.Public;
        private bool _isStatic;
        private bool _isAbstract;
        private bool _isVirtual;
        private bool _isOverride;
        private bool _isAsync;
        private bool _isSealed;
        private string _returnType = "void";
        private readonly List<string> _typeParameters = new List<string>();
        private readonly List<string> _constraints = new List<string>();
        private readonly List<ParameterInfo> _parameters = new List<ParameterInfo>();
        private readonly List<string> _attributes = new List<string>();
        private readonly List<string> _bodyLines = new List<string>();
        private string? _expressionBody;
        private string? _xmlDocSummary;
        private readonly List<(string Name, string Desc)> _xmlDocParams = new List<(string, string)>();
        private string? _xmlDocReturns;

        public MethodBuilder(string name)
        {
            _name = name;
        }

        /// <summary>设置访问修饰符</summary>
        public MethodBuilder Access(MemberAccess access)
        {
            _accessibility = access;
            return this;
        }

        /// <summary>设为 public</summary>
        public MethodBuilder Public() => Access(MemberAccess.Public);

        /// <summary>设为 private</summary>
        public MethodBuilder Private() => Access(MemberAccess.Private);

        /// <summary>设为 internal</summary>
        public MethodBuilder Internal() => Access(MemberAccess.Internal);

        /// <summary>设为 protected</summary>
        public MethodBuilder Protected() => Access(MemberAccess.Protected);

        /// <summary>设为 static</summary>
        public MethodBuilder Static()
        {
            _isStatic = true;
            return this;
        }

        /// <summary>设为 abstract</summary>
        public MethodBuilder Abstract()
        {
            _isAbstract = true;
            return this;
        }

        /// <summary>设为 virtual</summary>
        public MethodBuilder Virtual()
        {
            _isVirtual = true;
            return this;
        }

        /// <summary>设为 override</summary>
        public MethodBuilder Override()
        {
            _isOverride = true;
            return this;
        }

        /// <summary>设为 sealed override</summary>
        public MethodBuilder SealedOverride()
        {
            _isSealed = true;
            _isOverride = true;
            return this;
        }

        /// <summary>设为 async</summary>
        public MethodBuilder Async()
        {
            _isAsync = true;
            return this;
        }

        /// <summary>设置返回类型</summary>
        public MethodBuilder Returns(string returnType)
        {
            _returnType = returnType;
            return this;
        }

        /// <summary>添加泛型参数</summary>
        public MethodBuilder TypeParameter(params string[] typeParams)
        {
            _typeParameters.AddRange(typeParams);
            return this;
        }

        /// <summary>添加泛型约束</summary>
        public MethodBuilder Constraint(string constraint)
        {
            _constraints.Add(constraint);
            return this;
        }

        /// <summary>添加参数</summary>
        public MethodBuilder Parameter(string type, string name, string? defaultValue = null)
        {
            _parameters.Add(new ParameterInfo(type, name, null, defaultValue));
            return this;
        }

        /// <summary>添加带注解的参数（如 [FromBody]）</summary>
        public MethodBuilder Parameter(string type, string name, string attribute, string? defaultValue = null)
        {
            _parameters.Add(new ParameterInfo(type, name, attribute, defaultValue));
            return this;
        }

        /// <summary>添加 this 扩展方法参数</summary>
        public MethodBuilder ThisParameter(string type, string name)
        {
            _parameters.Insert(0, new ParameterInfo(type, name, "this", null));
            return this;
        }

        /// <summary>添加 params 参数</summary>
        public MethodBuilder ParamsParameter(string type, string name)
        {
            _parameters.Add(new ParameterInfo(type, name, "params", null));
            return this;
        }

        /// <summary>添加特性标注</summary>
        public MethodBuilder Attribute(string attributeCode)
        {
            _attributes.Add(attributeCode);
            return this;
        }

        /// <summary>设置 XML 文档</summary>
        public MethodBuilder Doc(string summary)
        {
            _xmlDocSummary = summary;
            return this;
        }

        /// <summary>添加参数文档</summary>
        public MethodBuilder DocParam(string name, string description)
        {
            _xmlDocParams.Add((name, description));
            return this;
        }

        /// <summary>添加返回值文档</summary>
        public MethodBuilder DocReturns(string description)
        {
            _xmlDocReturns = description;
            return this;
        }

        /// <summary>设置方法体（多行）</summary>
        public MethodBuilder Body(Action<BodyBuilder> configure)
        {
            var builder = new BodyBuilder();
            configure(builder);
            _bodyLines.AddRange(builder.Lines);
            return this;
        }

        /// <summary>直接添加方法体行</summary>
        public MethodBuilder BodyLine(string line)
        {
            _bodyLines.Add(line);
            return this;
        }

        /// <summary>设置表达式体 (=> expression)</summary>
        public MethodBuilder ExpressionBody(string expression)
        {
            _expressionBody = expression;
            return this;
        }

        /// <summary>
        /// 构建方法代码
        /// </summary>
        internal string Build(string indent, bool isInterface = false)
        {
            var sb = new StringBuilder();

            // XML Doc
            if (_xmlDocSummary != null)
            {
                sb.AppendLine($"{indent}/// <summary>");
                sb.AppendLine($"{indent}/// {_xmlDocSummary}");
                sb.AppendLine($"{indent}/// </summary>");
                foreach (var (name, desc) in _xmlDocParams)
                    sb.AppendLine($"{indent}/// <param name=\"{name}\">{desc}</param>");
                if (_xmlDocReturns != null)
                    sb.AppendLine($"{indent}/// <returns>{_xmlDocReturns}</returns>");
            }

            // Attributes
            foreach (var attr in _attributes)
                sb.AppendLine($"{indent}[{attr}]");

            // Signature
            var signature = BuildSignature(isInterface);
            sb.Append($"{indent}{signature}");

            // Constraints
            foreach (var constraint in _constraints)
                sb.Append($" {constraint}");

            // Body
            if (isInterface && _bodyLines.Count == 0 && _expressionBody == null)
            {
                sb.AppendLine(";");
            }
            else if (_expressionBody != null)
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}    => {_expressionBody};");
            }
            else if (_isAbstract)
            {
                sb.AppendLine(";");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}{{");
                foreach (var line in _bodyLines)
                    sb.AppendLine($"{indent}    {line}");
                sb.AppendLine($"{indent}}}");
            }

            return sb.ToString();
        }

        private string BuildSignature(bool isInterface)
        {
            var parts = new List<string>();

            if (!isInterface)
            {
                // Accessibility
                parts.Add(_accessibility switch
                {
                    MemberAccess.Public => "public",
                    MemberAccess.Internal => "internal",
                    MemberAccess.Private => "private",
                    MemberAccess.Protected => "protected",
                    MemberAccess.ProtectedInternal => "protected internal",
                    MemberAccess.PrivateProtected => "private protected",
                    _ => "public"
                });

                if (_isStatic) parts.Add("static");
                if (_isAbstract) parts.Add("abstract");
                if (_isVirtual) parts.Add("virtual");
                if (_isSealed) parts.Add("sealed");
                if (_isOverride) parts.Add("override");
                if (_isAsync) parts.Add("async");
            }
            else
            {
                if (_isAsync) parts.Add("async");
                if (_isStatic) parts.Add("static");
            }

            // Return type
            parts.Add(_returnType);

            // Method name + type params
            var methodName = _name;
            if (_typeParameters.Count > 0)
                methodName += $"<{string.Join(", ", _typeParameters)}>";
            parts.Add(methodName);

            var result = string.Join(" ", parts);

            // Parameters
            var paramStrings = _parameters.Select(p =>
            {
                var prefix = p.Annotation != null ? $"{p.Annotation} " : "";
                var suffix = p.DefaultValue != null ? $" = {p.DefaultValue}" : "";
                return $"{prefix}{p.Type} {p.Name}{suffix}";
            });
            result += $"({string.Join(", ", paramStrings)})";

            return result;
        }

        private sealed class ParameterInfo
        {
            public string Type { get; }
            public string Name { get; }
            public string? Annotation { get; }
            public string? DefaultValue { get; }

            public ParameterInfo(string type, string name, string? annotation, string? defaultValue)
            {
                Type = type;
                Name = name;
                Annotation = annotation;
                DefaultValue = defaultValue;
            }
        }
    }

    /// <summary>
    /// 方法体构建器 - 提供常见语句的 Fluent API
    /// </summary>
    public sealed class BodyBuilder
    {
        internal List<string> Lines { get; } = new List<string>();

        /// <summary>声明变量</summary>
        public BodyBuilder Var(string name, string expression)
        {
            Lines.Add($"var {name} = {expression};");
            return this;
        }

        /// <summary>声明指定类型变量</summary>
        public BodyBuilder Var(string type, string name, string expression)
        {
            Lines.Add($"{type} {name} = {expression};");
            return this;
        }

        /// <summary>赋值语句</summary>
        public BodyBuilder Assign(string target, string expression)
        {
            Lines.Add($"{target} = {expression};");
            return this;
        }

        /// <summary>return 语句</summary>
        public BodyBuilder Return(string expression)
        {
            Lines.Add($"return {expression};");
            return this;
        }

        /// <summary>return（无值）</summary>
        public BodyBuilder Return()
        {
            Lines.Add("return;");
            return this;
        }

        /// <summary>throw 语句</summary>
        public BodyBuilder Throw(string expression)
        {
            Lines.Add($"throw {expression};");
            return this;
        }

        /// <summary>方法调用语句</summary>
        public BodyBuilder Call(string expression)
        {
            Lines.Add($"{expression};");
            return this;
        }

        /// <summary>await 语句</summary>
        public BodyBuilder Await(string varName, string expression)
        {
            Lines.Add($"var {varName} = await {expression};");
            return this;
        }

        /// <summary>await 无返回值</summary>
        public BodyBuilder AwaitCall(string expression)
        {
            Lines.Add($"await {expression};");
            return this;
        }

        /// <summary>if 语句块开始</summary>
        public BodyBuilder If(string condition)
        {
            Lines.Add($"if ({condition})");
            Lines.Add("{");
            return this;
        }

        /// <summary>else if</summary>
        public BodyBuilder ElseIf(string condition)
        {
            Lines.Add("}");
            Lines.Add($"else if ({condition})");
            Lines.Add("{");
            return this;
        }

        /// <summary>else</summary>
        public BodyBuilder Else()
        {
            Lines.Add("}");
            Lines.Add("else");
            Lines.Add("{");
            return this;
        }

        /// <summary>结束代码块</summary>
        public BodyBuilder End()
        {
            Lines.Add("}");
            return this;
        }

        /// <summary>foreach 语句</summary>
        public BodyBuilder ForEach(string varName, string collection)
        {
            Lines.Add($"foreach (var {varName} in {collection})");
            Lines.Add("{");
            return this;
        }

        /// <summary>try 块开始</summary>
        public BodyBuilder Try()
        {
            Lines.Add("try");
            Lines.Add("{");
            return this;
        }

        /// <summary>catch 块</summary>
        public BodyBuilder Catch(string exceptionType, string varName)
        {
            Lines.Add("}");
            Lines.Add($"catch ({exceptionType} {varName})");
            Lines.Add("{");
            return this;
        }

        /// <summary>finally 块</summary>
        public BodyBuilder Finally()
        {
            Lines.Add("}");
            Lines.Add("finally");
            Lines.Add("{");
            return this;
        }

        /// <summary>原始代码行</summary>
        public BodyBuilder Line(string code)
        {
            Lines.Add(code);
            return this;
        }

        /// <summary>空行</summary>
        public BodyBuilder Blank()
        {
            Lines.Add("");
            return this;
        }

        /// <summary>注释行</summary>
        public BodyBuilder Comment(string text)
        {
            Lines.Add($"// {text}");
            return this;
        }
    }
}
