using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AutoCode.Engine.CodeBuilder
{
    /// <summary>
    /// 类型种类
    /// </summary>
    public enum ClassKind
    {
        Class,
        Interface,
        Struct,
        Record,
        RecordStruct
    }

    /// <summary>
    /// 成员访问修饰符
    /// </summary>
    public enum MemberAccess
    {
        Public,
        Internal,
        Private,
        Protected,
        ProtectedInternal,
        PrivateProtected
    }

    /// <summary>
    /// Fluent 类型构建器 - 构建类/接口/结构体/record 的完整定义。
    /// 支持继承、接口实现、泛型约束、成员（方法/属性/字段/嵌套类型）。
    /// </summary>
    public sealed class ClassBuilder
    {
        private readonly string _name;
        private readonly ClassKind _kind;
        private MemberAccess _accessibility = MemberAccess.Public;
        private bool _isStatic;
        private bool _isAbstract;
        private bool _isSealed;
        private bool _isPartial;
        private string? _baseType;
        private readonly List<string> _interfaces = new List<string>();
        private readonly List<string> _typeParameters = new List<string>();
        private readonly List<string> _constraints = new List<string>();
        private readonly List<string> _attributes = new List<string>();
        private readonly List<MethodBuilder> _methods = new List<MethodBuilder>();
        private readonly List<PropertyBuilder> _properties = new List<PropertyBuilder>();
        private readonly List<FieldBuilder> _fields = new List<FieldBuilder>();
        private readonly List<ClassBuilder> _nestedTypes = new List<ClassBuilder>();
        private readonly List<string> _rawMembers = new List<string>();
        private string? _xmlDocSummary;
        private readonly List<string> _xmlDocRemarks = new List<string>();
        private readonly List<string> _constructorBodies = new List<string>();
        private readonly List<string> _ctorParameters = new List<string>();
        private bool _generateConstructor;

        internal HashSet<string> Usings { get; } = new HashSet<string>(StringComparer.Ordinal);

        public ClassBuilder(string name, ClassKind kind)
        {
            _name = name;
            _kind = kind;
        }

        /// <summary>设置访问修饰符</summary>
        public ClassBuilder Access(MemberAccess access)
        {
            _accessibility = access;
            return this;
        }

        /// <summary>设为 public</summary>
        public ClassBuilder Public() => Access(MemberAccess.Public);

        /// <summary>设为 internal</summary>
        public ClassBuilder Internal() => Access(MemberAccess.Internal);

        /// <summary>设为 static</summary>
        public ClassBuilder Static()
        {
            _isStatic = true;
            return this;
        }

        /// <summary>设为 abstract</summary>
        public ClassBuilder Abstract()
        {
            _isAbstract = true;
            return this;
        }

        /// <summary>设为 sealed</summary>
        public ClassBuilder Sealed()
        {
            _isSealed = true;
            return this;
        }

        /// <summary>设为 partial</summary>
        public ClassBuilder Partial()
        {
            _isPartial = true;
            return this;
        }

        /// <summary>设置基类</summary>
        public ClassBuilder Inherits(string baseType)
        {
            _baseType = baseType;
            return this;
        }

        /// <summary>添加接口实现</summary>
        public ClassBuilder Implements(params string[] interfaces)
        {
            _interfaces.AddRange(interfaces);
            return this;
        }

        /// <summary>添加泛型参数</summary>
        public ClassBuilder TypeParameter(params string[] typeParams)
        {
            _typeParameters.AddRange(typeParams);
            return this;
        }

        /// <summary>添加泛型约束</summary>
        public ClassBuilder Constraint(string constraint)
        {
            _constraints.Add(constraint);
            return this;
        }

        /// <summary>添加特性标注</summary>
        public ClassBuilder Attribute(string attributeCode)
        {
            _attributes.Add(attributeCode);
            return this;
        }

        /// <summary>设置 XML 文档注释</summary>
        public ClassBuilder Doc(string summary, params string[] remarks)
        {
            _xmlDocSummary = summary;
            _xmlDocRemarks.AddRange(remarks);
            return this;
        }

        /// <summary>添加 using</summary>
        public ClassBuilder Using(params string[] namespaces)
        {
            foreach (var ns in namespaces)
                Usings.Add(ns);
            return this;
        }

        /// <summary>添加方法</summary>
        public ClassBuilder Method(string name, Action<MethodBuilder> configure)
        {
            var builder = new MethodBuilder(name);
            configure(builder);
            _methods.Add(builder);
            return this;
        }

        /// <summary>添加属性</summary>
        public ClassBuilder Property(string name, Action<PropertyBuilder> configure)
        {
            var builder = new PropertyBuilder(name);
            configure(builder);
            _properties.Add(builder);
            return this;
        }

        /// <summary>添加字段</summary>
        public ClassBuilder Field(string name, Action<FieldBuilder> configure)
        {
            var builder = new FieldBuilder(name);
            configure(builder);
            _fields.Add(builder);
            return this;
        }

        /// <summary>添加嵌套类型</summary>
        public ClassBuilder NestedClass(string name, Action<ClassBuilder> configure)
        {
            var builder = new ClassBuilder(name, ClassKind.Class);
            configure(builder);
            _nestedTypes.Add(builder);
            return this;
        }

        /// <summary>生成构造函数</summary>
        public ClassBuilder Constructor(Action<ConstructorBuilder> configure)
        {
            var builder = new ConstructorBuilder(_name);
            configure(builder);
            _generateConstructor = true;
            _ctorParameters.AddRange(builder.Parameters);
            _constructorBodies.AddRange(builder.BodyLines);
            return this;
        }

        /// <summary>添加原始代码成员</summary>
        public ClassBuilder RawMember(string code)
        {
            _rawMembers.Add(code);
            return this;
        }

        /// <summary>
        /// 构建类型定义代码
        /// </summary>
        internal string Build(CodeWriter parentWriter)
        {
            var sb = new StringBuilder();
            var indent = parentWriter.GetIndent();

            // XML Doc
            if (_xmlDocSummary != null)
            {
                sb.AppendLine($"{indent}/// <summary>");
                foreach (var line in _xmlDocSummary.Split('\n'))
                    sb.AppendLine($"{indent}/// {line.TrimEnd('\r')}");
                sb.AppendLine($"{indent}/// </summary>");
                foreach (var remark in _xmlDocRemarks)
                    sb.AppendLine($"{indent}/// <remarks>{remark}</remarks>");
            }

            // Attributes
            foreach (var attr in _attributes)
                sb.AppendLine($"{indent}[{attr}]");

            // Type declaration
            var declaration = BuildDeclaration();
            sb.AppendLine($"{indent}{declaration}");
            sb.AppendLine($"{indent}{{");

            var memberIndent = indent + "    ";

            // Fields
            foreach (var field in _fields)
                sb.Append(field.Build(memberIndent));

            if (_fields.Count > 0 && (_properties.Count > 0 || _methods.Count > 0))
                sb.AppendLine();

            // Constructor
            if (_generateConstructor)
            {
                sb.Append(BuildConstructor(memberIndent));
                sb.AppendLine();
            }

            // Properties
            foreach (var prop in _properties)
                sb.Append(prop.Build(memberIndent));

            if (_properties.Count > 0 && _methods.Count > 0)
                sb.AppendLine();

            // Methods
            for (int i = 0; i < _methods.Count; i++)
            {
                sb.Append(_methods[i].Build(memberIndent, _kind == ClassKind.Interface));
                if (i < _methods.Count - 1)
                    sb.AppendLine();
            }

            // Raw members
            foreach (var raw in _rawMembers)
            {
                sb.AppendLine();
                sb.AppendLine($"{memberIndent}{raw}");
            }

            // Nested types
            foreach (var nested in _nestedTypes)
            {
                sb.AppendLine();
                sb.Append(nested.Build(parentWriter));
            }

            sb.AppendLine($"{indent}}}");

            return sb.ToString();
        }

        private string BuildDeclaration()
        {
            var parts = new List<string>();

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

            // Modifiers
            if (_isStatic) parts.Add("static");
            if (_isAbstract && _kind == ClassKind.Class) parts.Add("abstract");
            if (_isSealed && _kind == ClassKind.Class) parts.Add("sealed");
            if (_isPartial) parts.Add("partial");

            // Kind
            parts.Add(_kind switch
            {
                ClassKind.Class => "class",
                ClassKind.Interface => "interface",
                ClassKind.Struct => "struct",
                ClassKind.Record => "record",
                ClassKind.RecordStruct => "record struct",
                _ => "class"
            });

            // Name + type parameters
            var name = _name;
            if (_typeParameters.Count > 0)
                name += $"<{string.Join(", ", _typeParameters)}>";
            parts.Add(name);

            var result = string.Join(" ", parts);

            // Base type + interfaces
            var inheritance = new List<string>();
            if (_baseType != null) inheritance.Add(_baseType);
            inheritance.AddRange(_interfaces);
            if (inheritance.Count > 0)
                result += " : " + string.Join(", ", inheritance);

            // Constraints
            foreach (var constraint in _constraints)
                result += $" {constraint}";

            return result;
        }

        private string BuildConstructor(string indent)
        {
            var sb = new StringBuilder();
            var paramList = string.Join(", ", _ctorParameters);
            sb.AppendLine($"{indent}public {_name}({paramList})");
            sb.AppendLine($"{indent}{{");
            foreach (var line in _constructorBodies)
                sb.AppendLine($"{indent}    {line}");
            sb.AppendLine($"{indent}}}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// 构造函数构建器
    /// </summary>
    public sealed class ConstructorBuilder
    {
        private readonly string _className;
        internal List<string> Parameters { get; } = new List<string>();
        internal List<string> BodyLines { get; } = new List<string>();

        public ConstructorBuilder(string className)
        {
            _className = className;
        }

        /// <summary>添加参数并赋值到字段</summary>
        public ConstructorBuilder AssignField(string type, string paramName, string fieldName)
        {
            Parameters.Add($"{type} {paramName}");
            BodyLines.Add($"{fieldName} = {paramName};");
            return this;
        }

        /// <summary>添加原始参数</summary>
        public ConstructorBuilder Parameter(string type, string name)
        {
            Parameters.Add($"{type} {name}");
            return this;
        }

        /// <summary>添加构造函数体代码行</summary>
        public ConstructorBuilder Body(string line)
        {
            BodyLines.Add(line);
            return this;
        }
    }
}
