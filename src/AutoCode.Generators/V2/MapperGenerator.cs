using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using AutoCode.Engine.CodeBuilder;
using AutoCode.Engine.Config;
using AutoCode.Engine.Diagnostics;

namespace AutoCode.Plugins.Mapper
{
    /// <summary>
    /// 跨类型智能映射生成器 - 基于 IIncrementalGenerator + AutoCode Engine。
    /// 支持 [MapFrom] 跨类型映射、自动属性匹配、嵌套对象、集合映射、Nullable 安全。
    /// </summary>
    [Generator]
    public class MapperGenerator : IIncrementalGenerator
    {
        private const string MapFromAttributeFullName = "AutoCode.Model.MapFromAttribute";
        private const string MapPropertyAttributeFullName = "AutoCode.Model.MapPropertyAttribute";
        private const string MapIgnoreAttributeFullName = "AutoCode.Model.MapIgnoreAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 读取 MSBuild 配置
            var configProvider = context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_MapMethodName", out var methodName);
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_MapNullHandling", out var nullHandling);
                return new MapperConfig
                {
                    MethodName = methodName ?? "MapTo",
                    NullHandling = nullHandling ?? "Skip"
                };
            });

            // 查找标记了 [MapFrom] 的类
            var mapperDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsClassWithMapFromAttribute(node),
                    transform: static (ctx, ct) => ExtractMappingInfo(ctx, ct))
                .Where(static m => m != null);

            // 组合配置和映射信息
            var combined = mapperDeclarations.Combine(configProvider);

            // 注册输出
            context.RegisterSourceOutput(AutoCode.Generators.V2Gate.Apply(context, combined), static (spc, pair) =>
            {
                var mappingInfo = pair.Left!;
                var config = pair.Right;
                var source = GenerateMappingCode(mappingInfo, config);
                if (source != null)
                {
                    spc.AddSource(source.FileName, SourceText.From(source.Content, Encoding.UTF8));
                }
            });
        }

        /// <summary>
        /// 快速语法级判断：是否为带 [MapFrom] 的类声明
        /// </summary>
        private static bool IsClassWithMapFromAttribute(SyntaxNode node)
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
                    return name == "MapFrom" || name == "MapFromAttribute"
                        || name == "Mapper" || name == "MapperAttribute"; // 向后兼容旧 [Mapper]
                });
        }

        /// <summary>
        /// 语义级提取映射信息
        /// </summary>
        private static MappingInfo? ExtractMappingInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var targetSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
            if (targetSymbol == null)
                return null;

            // 查找 [MapFrom] 特性
            var mapFromAttr = targetSymbol.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == MapFromAttributeFullName
                || a.AttributeClass?.Name == "MapFromAttribute");

            if (mapFromAttr == null || mapFromAttr.ConstructorArguments.Length == 0)
            {
                // 向后兼容：旧的 [Mapper] 同类型映射
                var legacyMapper = targetSymbol.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.Name == "MapperAttribute");
                if (legacyMapper != null)
                {
                    return BuildLegacyMappingInfo(targetSymbol);
                }
                return null;
            }

            var sourceType = mapFromAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (sourceType == null)
                return null;

            // 读取特性配置
            var direction = MapDirection.Both;
            var nullHandling = NullHandling.Skip;
            var collectionMapping = CollectionMapping.DeepCopy;
            var generateProjection = false;

            foreach (var namedArg in mapFromAttr.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "Direction":
                        direction = (MapDirection)(namedArg.Value.Value ?? 0);
                        break;
                    case "NullHandling":
                        nullHandling = (NullHandling)(namedArg.Value.Value ?? 0);
                        break;
                    case "CollectionMapping":
                        collectionMapping = (CollectionMapping)(namedArg.Value.Value ?? 0);
                        break;
                    case "GenerateProjection":
                        generateProjection = namedArg.Value.Value is true;
                        break;
                }
            }

            // 提取属性映射关系
            var propertyMappings = ExtractPropertyMappings(sourceType, targetSymbol);

            return new MappingInfo
            {
                SourceType = sourceType,
                TargetType = targetSymbol,
                Direction = direction,
                NullHandling = nullHandling,
                CollectionMapping = collectionMapping,
                GenerateProjection = generateProjection,
                PropertyMappings = propertyMappings
            };
        }

        /// <summary>
        /// 提取属性映射关系（自动匹配 + 自定义映射）
        /// </summary>
        private static List<PropertyMapping> ExtractPropertyMappings(
            INamedTypeSymbol source, INamedTypeSymbol target)
        {
            var mappings = new List<PropertyMapping>();

            var sourceProps = source.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public)
                .Where(p => !p.IsStatic && !p.IsIndexer)
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            var targetProps = target.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public)
                .Where(p => !p.IsStatic && !p.IsIndexer)
                .Where(p => p.SetMethod != null); // 必须有 setter

            foreach (var targetProp in targetProps)
            {
                // 检查 [MapIgnore]
                var isIgnored = targetProp.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == MapIgnoreAttributeFullName
                    || a.AttributeClass?.Name == "MapIgnoreAttribute");
                if (isIgnored)
                    continue;

                // 检查 [MapProperty("SourceName")]
                var mapPropAttr = targetProp.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == MapPropertyAttributeFullName
                    || a.AttributeClass?.Name == "MapPropertyAttribute");

                string sourcePropName;
                string? converter = null;

                if (mapPropAttr != null && mapPropAttr.ConstructorArguments.Length > 0)
                {
                    sourcePropName = mapPropAttr.ConstructorArguments[0].Value as string ?? targetProp.Name;
                    converter = mapPropAttr.NamedArguments
                        .FirstOrDefault(a => a.Key == "Converter").Value.Value as string;
                }
                else
                {
                    sourcePropName = targetProp.Name;
                }

                // 查找源属性
                if (sourceProps.TryGetValue(sourcePropName, out var sourceProp))
                {
                    mappings.Add(new PropertyMapping
                    {
                        SourceProperty = sourceProp,
                        TargetProperty = targetProp,
                        Converter = converter,
                        MappingKind = ClassifyMapping(sourceProp.Type, targetProp.Type)
                    });
                }
            }

            return mappings;
        }

        /// <summary>
        /// 分类映射类型
        /// </summary>
        private static MappingKind ClassifyMapping(ITypeSymbol sourceType, ITypeSymbol targetType)
        {
            // 完全相同类型 → 直接赋值
            if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
                return MappingKind.Direct;

            // Nullable 兼容
            var sourceUnderlying = GetUnderlyingType(sourceType);
            var targetUnderlying = GetUnderlyingType(targetType);

            if (sourceUnderlying != null && SymbolEqualityComparer.Default.Equals(sourceUnderlying, targetType))
                return MappingKind.NullableToValue;

            if (targetUnderlying != null && SymbolEqualityComparer.Default.Equals(sourceType, targetUnderlying))
                return MappingKind.ValueToNullable;

            // 集合映射
            if (IsCollectionType(sourceType) && IsCollectionType(targetType))
                return MappingKind.Collection;

            // 枚举 ↔ 字符串
            if (sourceType.TypeKind == TypeKind.Enum && targetType.SpecialType == SpecialType.System_String)
                return MappingKind.EnumToString;
            if (sourceType.SpecialType == SpecialType.System_String && targetType.TypeKind == TypeKind.Enum)
                return MappingKind.StringToEnum;

            // 可转换类型（如 int → long, int → string）
            if (IsNumericConversion(sourceType, targetType))
                return MappingKind.Convert;

            // 复杂对象（需要递归映射）
            if (sourceType is INamedTypeSymbol && targetType is INamedTypeSymbol
                && !sourceType.IsValueType && !targetType.IsValueType)
                return MappingKind.NestedObject;

            // 默认直接赋值
            return MappingKind.Direct;
        }

        /// <summary>
        /// 生成映射代码
        /// </summary>
        private static GeneratedOutput? GenerateMappingCode(MappingInfo info, MapperConfig config)
        {
            if (info.PropertyMappings.Count == 0)
                return null;

            var sourceName = info.SourceType.Name;
            var targetName = info.TargetType.Name;
            var namespaceName = info.TargetType.ContainingNamespace?.ToDisplayString() ?? "";
            var extensionClassName = $"{targetName}MappingExtensions";
            var methodName = config.MethodName;

            var writer = new CodeWriter();
            writer.AutoGeneratedHeader();
            writer.Using("System", "System.Collections.Generic", "System.Linq");
            writer.FileScopedNamespace(namespaceName);

            // 生成扩展方法类
            writer.Class(extensionClassName, c =>
            {
                c.Public().Static();
                c.Doc($"{sourceName} ↔ {targetName} 映射扩展方法（由 AutoCode Mapper 自动生成）");

                // Source → Target 扩展方法
                if (info.Direction != MapDirection.Reverse)
                {
                    GenerateForwardMapping(c, info, methodName);
                }

                // Target → Source 反向映射
                if (info.Direction == MapDirection.Both)
                {
                    GenerateReverseMapping(c, info, methodName);
                }

                // 静态工厂方法
                if (info.Direction != MapDirection.Reverse)
                {
                    GenerateStaticFactory(c, info);
                }

                // IQueryable 投影
                if (info.GenerateProjection)
                {
                    GenerateProjection(c, info);
                }
            });

            return new GeneratedOutput
            {
                FileName = $"{extensionClassName}.g.cs",
                Content = writer.Build()
            };
        }

        private static void GenerateForwardMapping(ClassBuilder c, MappingInfo info, string methodName)
        {
            var sourceName = info.SourceType.Name;
            var targetName = info.TargetType.Name;

            c.Method(methodName, m =>
            {
                m.Public().Static();
                m.Doc($"将 {sourceName} 映射到 {targetName}");
                m.ThisParameter(sourceName, "source");
                m.Parameter(targetName, "target");
                m.Returns(targetName);
                m.Body(b =>
                {
                    b.If("source == null").Line("throw new ArgumentNullException(nameof(source));").End();
                    b.If("target == null").Line("throw new ArgumentNullException(nameof(target));").End();
                    b.Blank();

                    foreach (var mapping in info.PropertyMappings)
                    {
                        GeneratePropertyAssignment(b, mapping, "source", "target", info.NullHandling);
                    }

                    b.Blank();
                    b.Return("target");
                });
            });
        }

        private static void GenerateReverseMapping(ClassBuilder c, MappingInfo info, string methodName)
        {
            var sourceName = info.SourceType.Name;
            var targetName = info.TargetType.Name;

            c.Method($"MapTo{sourceName}", m =>
            {
                m.Public().Static();
                m.Doc($"将 {targetName} 反向映射到 {sourceName}");
                m.ThisParameter(targetName, "source");
                m.Parameter(sourceName, "target");
                m.Returns(sourceName);
                m.Body(b =>
                {
                    b.If("source == null").Line("throw new ArgumentNullException(nameof(source));").End();
                    b.If("target == null").Line("throw new ArgumentNullException(nameof(target));").End();
                    b.Blank();

                    foreach (var mapping in info.PropertyMappings)
                    {
                        // 反向：target(source) → source(target)
                        GeneratePropertyAssignment(b, mapping, "source", "target",
                            info.NullHandling, reverse: true);
                    }

                    b.Blank();
                    b.Return("target");
                });
            });
        }

        private static void GenerateStaticFactory(ClassBuilder c, MappingInfo info)
        {
            var sourceName = info.SourceType.Name;
            var targetName = info.TargetType.Name;

            c.Method($"To{targetName}", m =>
            {
                m.Public().Static();
                m.Doc($"从 {sourceName} 创建新的 {targetName} 实例");
                m.Parameter(sourceName, "source");
                m.Returns(targetName);
                m.Body(b =>
                {
                    b.If("source == null").Line("throw new ArgumentNullException(nameof(source));").End();
                    b.Blank();
                    b.Var("target", $"new {targetName}()");

                    foreach (var mapping in info.PropertyMappings)
                    {
                        GeneratePropertyAssignment(b, mapping, "source", "target", info.NullHandling);
                    }

                    b.Blank();
                    b.Return("target");
                });
            });
        }

        private static void GenerateProjection(ClassBuilder c, MappingInfo info)
        {
            var sourceName = info.SourceType.Name;
            var targetName = info.TargetType.Name;

            c.Field("Projection", f =>
            {
                f.Public().Static().ReadOnly();
                f.Type($"System.Linq.Expressions.Expression<Func<{sourceName}, {targetName}>>");
                f.Doc("IQueryable 投影表达式（用于 EF Core Select）");

                var assignments = info.PropertyMappings
                    .Select(m => $"{m.TargetProperty.Name} = src.{GetSourcePropertyName(m)}")
                    .ToList();

                f.Initializer($"src => new {targetName} {{ {string.Join(", ", assignments)} }}");
            });
        }

        /// <summary>
        /// 为单个属性生成赋值代码
        /// </summary>
        private static void GeneratePropertyAssignment(BodyBuilder b, PropertyMapping mapping,
            string sourceVar, string targetVar, NullHandling nullHandling, bool reverse = false)
        {
            var targetProp = reverse ? mapping.SourceProperty : mapping.TargetProperty;
            var sourcePropName = reverse ? mapping.TargetProperty.Name : GetSourcePropertyName(mapping);
            var targetPropName = reverse ? mapping.SourceProperty.Name : mapping.TargetProperty.Name;

            // 反向映射时检查源属性是否有 setter
            if (reverse && mapping.SourceProperty.SetMethod == null)
                return;

            if (mapping.Converter != null)
            {
                // 自定义转换器
                b.Line($"{targetVar}.{targetPropName} = {string.Format(mapping.Converter, $"{sourceVar}.{sourcePropName}")};");
                return;
            }

            switch (mapping.MappingKind)
            {
                case MappingKind.Direct:
                    b.Line($"{targetVar}.{targetPropName} = {sourceVar}.{sourcePropName};");
                    break;

                case MappingKind.NullableToValue:
                    b.Line($"{targetVar}.{targetPropName} = {sourceVar}.{sourcePropName} ?? default;");
                    break;

                case MappingKind.ValueToNullable:
                    b.Line($"{targetVar}.{targetPropName} = {sourceVar}.{sourcePropName};");
                    break;

                case MappingKind.Convert:
                    var targetType = reverse
                        ? mapping.SourceProperty.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                        : mapping.TargetProperty.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    b.Line($"{targetVar}.{targetPropName} = ({targetType}){sourceVar}.{sourcePropName};");
                    break;

                case MappingKind.EnumToString:
                    b.Line($"{targetVar}.{targetPropName} = {sourceVar}.{sourcePropName}.ToString();");
                    break;

                case MappingKind.StringToEnum:
                    var enumType = reverse
                        ? mapping.SourceProperty.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                        : mapping.TargetProperty.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    b.Line($"{targetVar}.{targetPropName} = Enum.TryParse<{enumType}>({sourceVar}.{sourcePropName}, true, out var _parsed) ? _parsed : default;");
                    break;

                case MappingKind.Collection:
                    GenerateCollectionMapping(b, mapping, sourceVar, targetVar, sourcePropName, targetPropName, nullHandling);
                    break;

                case MappingKind.NestedObject:
                    if (nullHandling == NullHandling.Skip)
                    {
                        b.If($"{sourceVar}.{sourcePropName} != null");
                        b.Line($"{targetVar}.{targetPropName} = {sourceVar}.{sourcePropName};");
                        b.End();
                    }
                    else
                    {
                        b.Line($"{targetVar}.{targetPropName} = {sourceVar}.{sourcePropName};");
                    }
                    break;

                default:
                    b.Line($"{targetVar}.{targetPropName} = {sourceVar}.{sourcePropName};");
                    break;
            }
        }

        private static void GenerateCollectionMapping(BodyBuilder b, PropertyMapping mapping,
            string sourceVar, string targetVar, string sourcePropName, string targetPropName,
            NullHandling nullHandling)
        {
            if (nullHandling == NullHandling.Skip)
            {
                b.If($"{sourceVar}.{sourcePropName} != null");
                b.Line($"{targetVar}.{targetPropName} = {sourceVar}.{sourcePropName}.ToList();");
                b.End();
            }
            else
            {
                b.Line($"{targetVar}.{targetPropName} = {sourceVar}.{sourcePropName}?.ToList();");
            }
        }

        private static string GetSourcePropertyName(PropertyMapping mapping)
        {
            // 如果有自定义映射，使用源属性名
            return mapping.SourceProperty.Name;
        }

        /// <summary>
        /// 向后兼容旧 [Mapper] 同类型 CopyTo
        /// </summary>
        private static MappingInfo BuildLegacyMappingInfo(INamedTypeSymbol typeSymbol)
        {
            var props = typeSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public)
                .Where(p => !p.IsStatic && !p.IsIndexer && p.SetMethod != null)
                .ToList();

            var mappings = props.Select(p => new PropertyMapping
            {
                SourceProperty = p,
                TargetProperty = p,
                MappingKind = MappingKind.Direct
            }).ToList();

            return new MappingInfo
            {
                SourceType = typeSymbol,
                TargetType = typeSymbol,
                Direction = MapDirection.OneWay,
                NullHandling = NullHandling.SetNull,
                CollectionMapping = CollectionMapping.ShallowCopy,
                PropertyMappings = mappings
            };
        }

        #region Type Helpers

        private static ITypeSymbol? GetUnderlyingType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && named.IsGenericType
                && named.OriginalDefinition.ToDisplayString() == "System.Nullable<T>")
            {
                return named.TypeArguments[0];
            }
            return null;
        }

        private static bool IsCollectionType(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named) return false;
            if (!named.IsGenericType) return false;

            var name = named.OriginalDefinition.ToDisplayString();
            return name.StartsWith("System.Collections.Generic.List")
                || name.StartsWith("System.Collections.Generic.IList")
                || name.StartsWith("System.Collections.Generic.ICollection")
                || name.StartsWith("System.Collections.Generic.IEnumerable")
                || name.StartsWith("System.Collections.Generic.IReadOnlyList");
        }

        private static bool IsNumericConversion(ITypeSymbol source, ITypeSymbol target)
        {
            var numericTypes = new HashSet<SpecialType>
            {
                SpecialType.System_Byte, SpecialType.System_SByte,
                SpecialType.System_Int16, SpecialType.System_UInt16,
                SpecialType.System_Int32, SpecialType.System_UInt32,
                SpecialType.System_Int64, SpecialType.System_UInt64,
                SpecialType.System_Single, SpecialType.System_Double,
                SpecialType.System_Decimal
            };

            return numericTypes.Contains(source.SpecialType) && numericTypes.Contains(target.SpecialType)
                && !SymbolEqualityComparer.Default.Equals(source, target);
        }

        #endregion
    }

    #region Models

    internal sealed class MapperConfig
    {
        public string MethodName { get; set; } = "MapTo";
        public string NullHandling { get; set; } = "Skip";
    }

    internal sealed class MappingInfo
    {
        public INamedTypeSymbol SourceType { get; set; } = null!;
        public INamedTypeSymbol TargetType { get; set; } = null!;
        public MapDirection Direction { get; set; }
        public NullHandling NullHandling { get; set; }
        public CollectionMapping CollectionMapping { get; set; }
        public bool GenerateProjection { get; set; }
        public List<PropertyMapping> PropertyMappings { get; set; } = new List<PropertyMapping>();
    }

    internal sealed class PropertyMapping
    {
        public IPropertySymbol SourceProperty { get; set; } = null!;
        public IPropertySymbol TargetProperty { get; set; } = null!;
        public string? Converter { get; set; }
        public MappingKind MappingKind { get; set; }
    }

    internal enum MappingKind
    {
        Direct,
        NullableToValue,
        ValueToNullable,
        Convert,
        EnumToString,
        StringToEnum,
        Collection,
        NestedObject
    }

    internal enum MapDirection
    {
        OneWay,
        Both,
        Reverse
    }

    internal enum NullHandling
    {
        Skip,
        SetNull,
        Default
    }

    internal enum CollectionMapping
    {
        DeepCopy,
        ShallowCopy,
        Reference
    }

    internal sealed class GeneratedOutput
    {
        public string FileName { get; set; } = "";
        public string Content { get; set; } = "";
    }

    #endregion
}
