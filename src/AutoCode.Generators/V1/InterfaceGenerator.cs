using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AutoCode.SourceGenerator.InterfaceAutoBuilder
{
    /// <summary>
    /// 接口生成器 - 基于 IIncrementalGenerator 的高性能增量生成器
    /// 自动为标记 [AutoInterface] 的类生成接口
    /// </summary>
    [Generator]
    public class InterfaceGenerator : IIncrementalGenerator
    {
        private const string AutoInterfaceAttributeFullName =
            "AutoCode.Model.InterfaceAttribute.AutoInterfaceAttribute";
        private const string AutoIgnoreAttributeFullName =
            "AutoCode.Model.InterfaceAttribute.AutoIgnoreAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 读取 MSBuild 配置: AutoCode_InterfacePrefix
            var interfacePrefix = context.AnalyzerConfigOptionsProvider
                .Select((provider, _) =>
                {
                    provider.GlobalOptions.TryGetValue("build_property.AutoCode_InterfacePrefix", out var prefix);
                    return prefix ?? "I";
                });

            var interfaceSpecs = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => IsClassWithAutoInterface(node),
                    transform: (ctx, _) => ExtractInterfaceSpecs(ctx))
                .Combine(interfacePrefix)
                .SelectMany((pair, _) => ApplyPrefix(pair.Left, pair.Right))
                .Where(spec => spec != null)!
                .WithComparer(InterfaceSpecComparer.Instance);

            context.RegisterSourceOutput(interfaceSpecs, (spc, spec) =>
            {
                var source = InterfaceBuilder.BuildInterface(spec);
                spc.AddSource($"{spec.InterfaceName}.g.cs", SourceText.From(source, Encoding.UTF8));
            });
        }

        /// <summary>
        /// 将配置的接口前缀应用到默认接口名
        /// </summary>
        private static IEnumerable<InterfaceSpec> ApplyPrefix(IEnumerable<InterfaceSpec> specs, string prefix)
        {
            foreach (var spec in specs)
            {
                // 如果接口名以默认 "I" 开头且配置了不同前缀，替换前缀
                if (prefix != "I" && spec.InterfaceName.StartsWith("I") && spec.InterfaceName.Length > 1
                    && char.IsUpper(spec.InterfaceName[1]))
                {
                    yield return new InterfaceSpec(
                        prefix + spec.InterfaceName.Substring(1),
                        spec.NamespaceName,
                        spec.Usings,
                        spec.Methods,
                        spec.Properties);
                }
                else
                {
                    yield return spec;
                }
            }
        }

        private static bool IsClassWithAutoInterface(SyntaxNode node)
        {
            if (node is not ClassDeclarationSyntax classDecl)
                return false;

            return classDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr =>
                {
                    var name = attr.Name is IdentifierNameSyntax id
                        ? id.Identifier.Text
                        : attr.Name.ToString();
                    return name == "AutoInterface" || name == "AutoInterfaceAttribute";
                });
        }

        private static IEnumerable<InterfaceSpec> ExtractInterfaceSpecs(GeneratorSyntaxContext ctx)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                yield break;

            var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (classSymbol == null)
                yield break;

            var namespaceName = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var usings = GetUsingDirectives(classDecl);
            var methods = GetPublicMethods(classSymbol);
            var properties = GetPublicProperties(classSymbol);

            // 查找 AutoInterface 特性
            var autoInterfaceAttributes = classSymbol.GetAttributes()
                .Where(a => a.AttributeClass?.ToDisplayString() == AutoInterfaceAttributeFullName
                         || a.AttributeClass?.Name == "AutoInterfaceAttribute");

            if (!autoInterfaceAttributes.Any())
            {
                var attrSyntax = classDecl.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .FirstOrDefault(a =>
                    {
                        var name = a.Name is IdentifierNameSyntax id
                            ? id.Identifier.Text
                            : a.Name.ToString();
                        return name == "AutoInterface" || name == "AutoInterfaceAttribute";
                    });

                var interfaceName = $"I{classSymbol.Name}";
                if (attrSyntax?.ArgumentList?.Arguments.Count > 0)
                {
                    var nameArg = attrSyntax.ArgumentList.Arguments[0].Expression.ToString().Trim('"');
                    if (!string.IsNullOrEmpty(nameArg))
                        interfaceName = nameArg;
                }

                yield return new InterfaceSpec(interfaceName, namespaceName, usings, methods, properties);
                yield break;
            }

            foreach (var attribute in autoInterfaceAttributes)
            {
                var interfaceName = $"I{classSymbol.Name}";

                if (attribute.ConstructorArguments.Length > 0)
                {
                    var nameArg = attribute.ConstructorArguments[0].Value as string;
                    if (!string.IsNullOrEmpty(nameArg))
                        interfaceName = nameArg!;
                }

                yield return new InterfaceSpec(interfaceName, namespaceName, usings, methods, properties);
            }
        }

        /// <summary>
        /// 获取类的公共方法（排除 [AutoIgnore]），支持泛型、异步、XML 文档、Nullable
        /// </summary>
        private static IReadOnlyList<MethodSpec> GetPublicMethods(INamedTypeSymbol classSymbol)
        {
            var methods = new List<MethodSpec>();

            // Nullable 感知的类型显示格式
            var nullableFormat = new SymbolDisplayFormat(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                    | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

            foreach (var member in classSymbol.GetMembers())
            {
                if (member is not IMethodSymbol method)
                    continue;
                if (method.DeclaredAccessibility != Accessibility.Public)
                    continue;
                if (method.MethodKind != MethodKind.Ordinary)
                    continue;
                if (HasAutoIgnoreAttribute(method))
                    continue;

                // 泛型类型参数
                string? typeParameters = null;
                if (method.IsGenericMethod && method.TypeParameters.Length > 0)
                {
                    typeParameters = "<" + string.Join(", ", method.TypeParameters.Select(t => t.Name)) + ">";
                }

                // 异步检测: Task / Task<T> / ValueTask / ValueTask<T>
                var isAsync = IsAsyncReturnType(method.ReturnType);

                // XML 文档注释提取
                var xmlDoc = ExtractXmlDoc(method);

                var parameters = new List<ParameterSpec>();
                foreach (var param in method.Parameters)
                {
                    parameters.Add(new ParameterSpec(
                        param.Type.ToDisplayString(nullableFormat),
                        param.Name));
                }

                methods.Add(new MethodSpec(
                    method.Name,
                    method.ReturnType.ToDisplayString(nullableFormat),
                    parameters,
                    typeParameters,
                    xmlDoc,
                    isAsync));
            }

            return methods;
        }

        /// <summary>
        /// 检测返回类型是否为异步类型 (Task/ValueTask)
        /// </summary>
        private static bool IsAsyncReturnType(ITypeSymbol returnType)
        {
            if (returnType is not INamedTypeSymbol namedType)
                return false;

            var fullName = namedType.OriginalDefinition.ToDisplayString();
            return fullName == "System.Threading.Tasks.Task"
                || fullName == "System.Threading.Tasks.Task<T>"
                || fullName == "System.Threading.Tasks.ValueTask"
                || fullName == "System.Threading.Tasks.ValueTask<T>";
        }

        /// <summary>
        /// 从方法的语法节点提取 XML 文档注释
        /// </summary>
        private static string? ExtractXmlDoc(IMethodSymbol method)
        {
            if (method.DeclaringSyntaxReferences.Length == 0)
                return null;

            var syntax = method.DeclaringSyntaxReferences[0].GetSyntax();
            var docTrivia = syntax.GetLeadingTrivia()
                .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                    || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

            if (docTrivia.IsKind(SyntaxKind.None))
                return null;

            // 清理文档注释：移除 /// 前缀，保留内容
            var lines = docTrivia.ToString()
                .Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None)
                .Select(line => line.TrimStart())
                .Where(line => line.Length > 0)
                .Select(line => line.StartsWith("///") ? line.Substring(3).TrimStart() : line)
                .ToList();

            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }

        /// <summary>
        /// 获取类的公共属性（排除标记了 [AutoIgnore] 的属性）
        /// </summary>
        private static IReadOnlyList<PropertySpec> GetPublicProperties(INamedTypeSymbol classSymbol)
        {
            var properties = new List<PropertySpec>();

            var nullableFormat = new SymbolDisplayFormat(
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                    | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

            foreach (var member in classSymbol.GetMembers())
            {
                if (member is not IPropertySymbol property)
                    continue;
                if (property.DeclaredAccessibility != Accessibility.Public)
                    continue;
                if (property.IsStatic || property.IsIndexer)
                    continue;
                if (HasAutoIgnoreAttribute(property))
                    continue;

                properties.Add(new PropertySpec(
                    property.Name,
                    property.Type.ToDisplayString(nullableFormat),
                    property.GetMethod != null,
                    property.SetMethod != null));
            }

            return properties;
        }

        private static bool HasAutoIgnoreAttribute(ISymbol symbol)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == AutoIgnoreAttributeFullName)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 从类声明节点向上遍历获取所有 using 指令，支持 FileScopedNamespace (C# 10+)
        /// </summary>
        private static IReadOnlyList<string> GetUsingDirectives(ClassDeclarationSyntax classDecl)
        {
            var usings = new List<string>();
            var seen = new HashSet<string>();

            var parent = classDecl.Parent;
            while (parent != null)
            {
                // 传统块范围命名空间
                if (parent is NamespaceDeclarationSyntax namespaceDecl)
                {
                    AddUsings(namespaceDecl.Usings, usings, seen);
                }
                // C# 10+ 文件范围命名空间
                else if (parent is FileScopedNamespaceDeclarationSyntax fileScopedNamespace)
                {
                    AddUsings(fileScopedNamespace.Usings, usings, seen);
                }
                // 编译单元（全局 using）
                else if (parent is CompilationUnitSyntax compilationUnit)
                {
                    AddUsings(compilationUnit.Usings, usings, seen);
                }
                parent = parent.Parent;
            }

            return usings;
        }

        private static void AddUsings(
            SyntaxList<UsingDirectiveSyntax> usingDirectives,
            List<string> usings,
            HashSet<string> seen)
        {
            foreach (var usingDirective in usingDirectives)
            {
                var name = usingDirective.Name?.ToString();
                if (!string.IsNullOrEmpty(name) && seen.Add(name!))
                    usings.Add(name!);
            }
        }
    }
}
