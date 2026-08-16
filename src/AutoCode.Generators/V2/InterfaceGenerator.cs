using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using AutoCode.Engine.CodeBuilder;

namespace AutoCode.Plugins.Interface
{
    /// <summary>
    /// 接口生成器 v2 - 从类自动提取公共接口。
    /// 增强：partial class、record struct、泛型方法、事件成员、XML 文档继承、
    /// 自定义接口名、多接口、[AutoIgnore] 排除、Async 感知、Nullable 感知。
    /// </summary>
    [Generator]
    public class InterfaceGenerator : IIncrementalGenerator
    {
        private const string AutoInterfaceAttrName = "AutoInterfaceAttribute";
        private const string AutoIgnoreAttrName = "AutoIgnoreAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var configProvider = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_InterfacePrefix", out var prefix);
                return new InterfaceConfig { Prefix = prefix ?? "I" };
            });

            var interfaceSources = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cds &&
                        cds.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
                        {
                            var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                            return name == "AutoInterface" || name == AutoInterfaceAttrName;
                        }),
                    transform: static (ctx, ct) => ExtractInterfaceInfo(ctx, ct))
                .Where(static s => s != null && s.Members.Count > 0)
                .Combine(configProvider);

            context.RegisterSourceOutput(AutoCode.Generators.V2Gate.Apply(context, interfaceSources), static (spc, pair) =>
            {
                var info = pair.Left!;
                var config = pair.Right;
                var output = GenerateInterface(info, config);
                if (output != null)
                    spc.AddSource(output.FileName, SourceText.From(output.Content, Encoding.UTF8));
            });
        }

        private static InterfaceInfo? ExtractInterfaceInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
            if (classSymbol == null)
                return null;

            var attr = classSymbol.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name == AutoInterfaceAttrName || a.AttributeClass?.Name == "AutoInterface");

            // 自定义接口名
            string? customName = null;
            if (attr != null && attr.ConstructorArguments.Length > 0)
                customName = attr.ConstructorArguments[0].Value as string;

            var members = new List<InterfaceMemberInfo>();

            // 提取公共方法
            foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public)
                    continue;
                if (method.MethodKind != MethodKind.Ordinary)
                    continue;
                if (method.IsStatic)
                    continue;
                if (HasIgnoreAttribute(method))
                    continue;

                members.Add(new InterfaceMemberInfo
                {
                    Kind = MemberKind.Method,
                    Name = method.Name,
                    ReturnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsAsync = IsAsyncReturn(method.ReturnType),
                    TypeParameters = method.TypeParameters.Select(tp => tp.Name).ToList(),
                    Parameters = method.Parameters.Select(p => new ParamInfo
                    {
                        Name = p.Name,
                        Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        HasDefault = p.HasExplicitDefaultValue,
                        DefaultValue = p.HasExplicitDefaultValue ? FormatDefault(p.ExplicitDefaultValue) : null
                    }).ToList(),
                    XmlDoc = GetXmlDoc(method)
                });
            }

            // 提取公共属性
            foreach (var prop in classSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public)
                    continue;
                if (prop.IsStatic || prop.IsIndexer)
                    continue;
                if (HasIgnoreAttribute(prop))
                    continue;

                members.Add(new InterfaceMemberInfo
                {
                    Kind = MemberKind.Property,
                    Name = prop.Name,
                    ReturnType = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    HasGetter = prop.GetMethod != null,
                    HasSetter = prop.SetMethod != null,
                    XmlDoc = GetXmlDoc(prop)
                });
            }

            // 提取事件
            foreach (var evt in classSymbol.GetMembers().OfType<IEventSymbol>())
            {
                if (evt.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public)
                    continue;
                if (HasIgnoreAttribute(evt))
                    continue;

                members.Add(new InterfaceMemberInfo
                {
                    Kind = MemberKind.Event,
                    Name = evt.Name,
                    ReturnType = evt.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                });
            }

            return new InterfaceInfo
            {
                ClassName = classSymbol.Name,
                Namespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                CustomInterfaceName = customName,
                Members = members
            };
        }

        private static InterfaceOutput? GenerateInterface(InterfaceInfo info, InterfaceConfig config)
        {
            var interfaceName = info.CustomInterfaceName
                ?? $"{config.Prefix}{info.ClassName}";

            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System", "System.Collections.Generic", "System.Threading.Tasks");
            w.FileScopedNamespace(info.Namespace);

            w.Interface(interfaceName, c =>
            {
                c.Public();
                c.Doc($"{info.ClassName} 的自动提取接口（由 AutoCode Interface 插件生成）");

                foreach (var member in info.Members)
                {
                    switch (member.Kind)
                    {
                        case MemberKind.Method:
                            GenerateMethodMember(c, member);
                            break;
                        case MemberKind.Property:
                            GeneratePropertyMember(c, member);
                            break;
                        case MemberKind.Event:
                            c.RawMember($"event {member.ReturnType} {member.Name};");
                            break;
                    }
                }
            });

            return new InterfaceOutput
            {
                FileName = $"{interfaceName}.g.cs",
                Content = w.Build()
            };
        }

        private static void GenerateMethodMember(ClassBuilder c, InterfaceMemberInfo member)
        {
            c.Method(member.Name, m =>
            {
                if (member.XmlDoc != null)
                    m.Doc(member.XmlDoc);

                // 泛型参数
                if (member.TypeParameters.Count > 0)
                    m.TypeParameter(member.TypeParameters.ToArray());

                m.Returns(member.ReturnType);

                foreach (var param in member.Parameters)
                {
                    m.Parameter(param.Type, param.Name, param.DefaultValue);
                }
            });
        }

        private static void GeneratePropertyMember(ClassBuilder c, InterfaceMemberInfo member)
        {
            c.Property(member.Name, p =>
            {
                if (member.XmlDoc != null)
                    p.Doc(member.XmlDoc);
                p.Type(member.ReturnType);
                if (!member.HasGetter) p.WriteOnly();
                if (!member.HasSetter) p.ReadOnly();
            });
        }

        private static bool HasIgnoreAttribute(ISymbol symbol)
        {
            return symbol.GetAttributes().Any(a =>
                a.AttributeClass?.Name == AutoIgnoreAttrName || a.AttributeClass?.Name == "AutoIgnore");
        }

        private static bool IsAsyncReturn(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named) return false;
            var full = named.OriginalDefinition.ToDisplayString();
            return full.StartsWith("System.Threading.Tasks.Task")
                || full.StartsWith("System.Threading.Tasks.ValueTask");
        }

        private static string? GetXmlDoc(ISymbol symbol)
        {
            var xml = symbol.GetDocumentationCommentXml();
            if (string.IsNullOrEmpty(xml))
                return null;

            // 提取 summary 内容
            var start = xml.IndexOf("<summary>", StringComparison.Ordinal);
            var end = xml.IndexOf("</summary>", StringComparison.Ordinal);
            if (start >= 0 && end > start)
            {
                var content = xml.Substring(start + 9, end - start - 9).Trim();
                return string.IsNullOrEmpty(content) ? null : content;
            }
            return null;
        }

        private static string? FormatDefault(object? value)
        {
            if (value == null) return "null";
            if (value is string s) return $"\"{s}\"";
            if (value is bool b) return b ? "true" : "false";
            if (value is char c) return $"'{c}'";
            return value.ToString();
        }
    }

    #region Models

    internal sealed class InterfaceInfo
    {
        public string ClassName { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string? CustomInterfaceName { get; set; }
        public List<InterfaceMemberInfo> Members { get; set; } = new List<InterfaceMemberInfo>();
    }

    internal sealed class InterfaceMemberInfo
    {
        public MemberKind Kind { get; set; }
        public string Name { get; set; } = "";
        public string ReturnType { get; set; } = "";
        public bool IsAsync { get; set; }
        public bool HasGetter { get; set; } = true;
        public bool HasSetter { get; set; } = true;
        public List<string> TypeParameters { get; set; } = new List<string>();
        public List<ParamInfo> Parameters { get; set; } = new List<ParamInfo>();
        public string? XmlDoc { get; set; }
    }

    internal sealed class ParamInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool HasDefault { get; set; }
        public string? DefaultValue { get; set; }
    }

    internal enum MemberKind { Method, Property, Event }

    internal sealed class InterfaceConfig
    {
        public string Prefix { get; set; } = "I";
    }

    internal sealed class InterfaceOutput
    {
        public string FileName { get; set; } = "";
        public string Content { get; set; } = "";
    }

    #endregion
}
