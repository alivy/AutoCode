using System.Collections.Generic;

namespace AutoCode.SourceGenerator.InterfaceAutoBuilder
{
    /// <summary>
    /// 接口生成规格数据模型
    /// </summary>
    internal sealed class InterfaceSpec
    {
        public string InterfaceName { get; }
        public string NamespaceName { get; }
        public IReadOnlyList<string> Usings { get; }
        public IReadOnlyList<MethodSpec> Methods { get; }
        public IReadOnlyList<PropertySpec> Properties { get; }

        public InterfaceSpec(
            string interfaceName,
            string namespaceName,
            IReadOnlyList<string> usings,
            IReadOnlyList<MethodSpec> methods,
            IReadOnlyList<PropertySpec> properties)
        {
            InterfaceName = interfaceName;
            NamespaceName = namespaceName;
            Usings = usings;
            Methods = methods;
            Properties = properties;
        }

        public bool Equals(InterfaceSpec? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (InterfaceName != other.InterfaceName) return false;
            if (NamespaceName != other.NamespaceName) return false;
            if (Usings.Count != other.Usings.Count) return false;
            if (Methods.Count != other.Methods.Count) return false;
            if (Properties.Count != other.Properties.Count) return false;
            for (int i = 0; i < Usings.Count; i++)
                if (Usings[i] != other.Usings[i]) return false;
            for (int i = 0; i < Methods.Count; i++)
                if (!Methods[i].Equals(other.Methods[i])) return false;
            for (int i = 0; i < Properties.Count; i++)
                if (!Properties[i].Equals(other.Properties[i])) return false;
            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (InterfaceName?.GetHashCode() ?? 0);
                hash = hash * 31 + (NamespaceName?.GetHashCode() ?? 0);
                foreach (var u in Usings) hash = hash * 31 + (u?.GetHashCode() ?? 0);
                foreach (var m in Methods) hash = hash * 31 + m.GetHashCode();
                foreach (var p in Properties) hash = hash * 31 + p.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 方法规格
    /// </summary>
    internal sealed class MethodSpec
    {
        public string Name { get; }
        public string ReturnType { get; }
        public IReadOnlyList<ParameterSpec> Parameters { get; }
        public string? TypeParameters { get; }
        /// <summary>XML 文档注释（原始格式）</summary>
        public string? XmlDoc { get; }
        /// <summary>是否为异步方法（返回 Task/ValueTask）</summary>
        public bool IsAsync { get; }

        public MethodSpec(string name, string returnType, IReadOnlyList<ParameterSpec> parameters,
            string? typeParameters = null, string? xmlDoc = null, bool isAsync = false)
        {
            Name = name;
            ReturnType = returnType;
            Parameters = parameters;
            TypeParameters = typeParameters;
            XmlDoc = xmlDoc;
            IsAsync = isAsync;
        }

        public bool Equals(MethodSpec? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (Name != other.Name) return false;
            if (ReturnType != other.ReturnType) return false;
            if (TypeParameters != other.TypeParameters) return false;
            if (XmlDoc != other.XmlDoc) return false;
            if (IsAsync != other.IsAsync) return false;
            if (Parameters.Count != other.Parameters.Count) return false;
            for (int i = 0; i < Parameters.Count; i++)
                if (!Parameters[i].Equals(other.Parameters[i])) return false;
            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Name?.GetHashCode() ?? 0);
                hash = hash * 31 + (ReturnType?.GetHashCode() ?? 0);
                hash = hash * 31 + (TypeParameters?.GetHashCode() ?? 0);
                hash = hash * 31 + (XmlDoc?.GetHashCode() ?? 0);
                hash = hash * 31 + IsAsync.GetHashCode();
                foreach (var p in Parameters) hash = hash * 31 + p.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 参数规格
    /// </summary>
    internal sealed class ParameterSpec
    {
        public string Type { get; }
        public string Name { get; }

        public ParameterSpec(string type, string name)
        {
            Type = type;
            Name = name;
        }

        public bool Equals(ParameterSpec? other)
        {
            if (other is null) return false;
            return Type == other.Type && Name == other.Name;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Type?.GetHashCode() ?? 0) * 31 + (Name?.GetHashCode() ?? 0);
            }
        }
    }

    /// <summary>
    /// 属性规格
    /// </summary>
    internal sealed class PropertySpec
    {
        public string Name { get; }
        public string Type { get; }
        public bool HasGetter { get; }
        public bool HasSetter { get; }

        public PropertySpec(string name, string type, bool hasGetter, bool hasSetter)
        {
            Name = name;
            Type = type;
            HasGetter = hasGetter;
            HasSetter = hasSetter;
        }

        public bool Equals(PropertySpec? other)
        {
            if (other is null) return false;
            return Name == other.Name && Type == other.Type
                && HasGetter == other.HasGetter && HasSetter == other.HasSetter;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Name?.GetHashCode() ?? 0);
                hash = hash * 31 + (Type?.GetHashCode() ?? 0);
                hash = hash * 31 + HasGetter.GetHashCode();
                hash = hash * 31 + HasSetter.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// InterfaceSpec 等值比较器，用于增量缓存
    /// </summary>
    internal sealed class InterfaceSpecComparer : IEqualityComparer<InterfaceSpec>
    {
        public static readonly InterfaceSpecComparer Instance = new InterfaceSpecComparer();

        public bool Equals(InterfaceSpec? x, InterfaceSpec? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return x.Equals(y);
        }

        public int GetHashCode(InterfaceSpec obj) => obj.GetHashCode();
    }
}
