using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using AutoCode.Engine.CodeBuilder;

namespace AutoCode.Plugins.Dto
{
    /// <summary>
    /// DTO 生成器 v2 - 从实体类自动生成 DTO + FromEntity/ToEntity。
    /// 支持：record 类型、Include/Exclude 过滤、审计字段自动排除、分页 DTO、嵌套对象。
    /// </summary>
    [Generator]
    public class DtoGenerator : IIncrementalGenerator
    {
        private const string AutoDTOAttrName = "AutoDTOAttribute";

        // 默认排除的审计字段
        private static readonly HashSet<string> DefaultAuditFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CreatedAt", "CreatedBy", "ModifiedAt", "ModifiedBy",
            "DeletedAt", "IsDeleted", "RowVersion", "ConcurrencyStamp"
        };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var configProvider = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_Dto_UseRecord", out var useRecord);
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_Dto_ExcludeAudit", out var excludeAudit);
                return new DtoConfig
                {
                    UseRecord = useRecord == "true",
                    ExcludeAuditFields = excludeAudit != "false"
                };
            });

            var dtoSources = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cds &&
                        cds.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
                        {
                            var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                            return name == "AutoDTO" || name == AutoDTOAttrName;
                        }),
                    transform: static (ctx, ct) => ExtractDtoInfo(ctx, ct))
                .Where(static s => s != null)
                .Combine(configProvider);

            context.RegisterSourceOutput(AutoCode.Generators.V2Gate.Apply(context, dtoSources), static (spc, pair) =>
            {
                var info = pair.Left!;
                var config = pair.Right;
                var output = GenerateDto(info, config);
                if (output != null)
                    spc.AddSource(output.FileName, SourceText.From(output.Content, Encoding.UTF8));
            });
        }

        private static DtoInfo? ExtractDtoInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
            if (classSymbol == null)
                return null;

            var dtoAttr = classSymbol.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name == AutoDTOAttrName || a.AttributeClass?.Name == "AutoDTO");
            if (dtoAttr == null || dtoAttr.ConstructorArguments.Length == 0)
                return null;

            var sourceType = dtoAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (sourceType == null)
                return null;

            // Include/Exclude
            var include = GetNamedStringSet(dtoAttr, "Include");
            var exclude = GetNamedStringSet(dtoAttr, "Exclude");
            var useRecord = dtoAttr.NamedArguments
                .FirstOrDefault(a => a.Key == "UseRecord").Value.Value is true;

            // 提取属性
            var properties = sourceType.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public)
                .Where(p => !p.IsStatic && !p.IsIndexer)
                .Where(p => include == null || include.Contains(p.Name))
                .Where(p => exclude == null || !exclude.Contains(p.Name))
                .Select(p => new DtoPropInfo
                {
                    Name = p.Name,
                    Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ShortType = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated,
                    HasSetter = p.SetMethod != null
                })
                .ToList();

            return new DtoInfo
            {
                DtoClassName = classSymbol.Name,
                Namespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                SourceTypeName = sourceType.Name,
                SourceTypeFull = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Properties = properties,
                UseRecord = useRecord,
                ExcludeAuditFields = true
            };
        }

        private static DtoOutput? GenerateDto(DtoInfo info, DtoConfig config)
        {
            // 过滤审计字段
            var props = info.Properties.ToList();
            if (config.ExcludeAuditFields && info.ExcludeAuditFields)
            {
                props = props.Where(p => !DefaultAuditFields.Contains(p.Name)).ToList();
            }

            if (props.Count == 0)
                return null;

            var useRecord = info.UseRecord || config.UseRecord;
            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System", "System.Collections.Generic");
            w.FileScopedNamespace(info.Namespace);

            if (useRecord)
            {
                GenerateRecordDto(w, info, props);
            }
            else
            {
                GenerateClassDto(w, info, props);
            }

            // 分页 DTO
            GeneratePagedResult(w, info);

            return new DtoOutput
            {
                FileName = $"{info.DtoClassName}.g.cs",
                Content = w.Build()
            };
        }

        private static void GenerateClassDto(CodeWriter w, DtoInfo info, List<DtoPropInfo> props)
        {
            w.Class(info.DtoClassName, c =>
            {
                c.Public().Partial();
                c.Doc($"{info.SourceTypeName} 的 DTO（由 AutoCode DTO 插件自动生成）");

                // 属性
                foreach (var prop in props)
                {
                    c.Property(prop.Name, p =>
                    {
                        p.Type(prop.ShortType);
                        if (prop.IsNullable && !prop.ShortType.EndsWith("?"))
                            p.Type(prop.ShortType + "?");
                    });
                }

                // FromEntity
                c.Method("FromEntity", m =>
                {
                    m.Public().Static();
                    m.Doc($"从 {info.SourceTypeName} 实体创建 DTO");
                    m.Parameter(info.SourceTypeFull, "entity");
                    m.Returns(info.DtoClassName);
                    m.Body(b =>
                    {
                        b.If("entity == null").Line("throw new ArgumentNullException(nameof(entity));").End();
                        b.Return($"new {info.DtoClassName}");
                        b.Line("{");
                        foreach (var prop in props)
                        {
                            b.Line($"    {prop.Name} = entity.{prop.Name},");
                        }
                        b.Line("}");
                    });
                });

                // FromEntity 批量
                c.Method("FromEntities", m =>
                {
                    m.Public().Static();
                    m.Doc("批量转换");
                    m.Parameter($"IEnumerable<{info.SourceTypeFull}>", "entities");
                    m.Returns($"List<{info.DtoClassName}>");
                    m.Body(b =>
                    {
                        b.Return($"entities.Select(FromEntity).ToList()");
                    });
                });

                // ToEntity
                c.Method("ToEntity", m =>
                {
                    m.Public();
                    m.Doc("将 DTO 值复制到实体");
                    m.Parameter(info.SourceTypeFull, "entity");
                    m.Body(b =>
                    {
                        b.If("entity == null").Line("throw new ArgumentNullException(nameof(entity));").End();
                        foreach (var prop in props.Where(p => p.HasSetter))
                        {
                            b.Line($"entity.{prop.Name} = {prop.Name};");
                        }
                    });
                });
            });
        }

        private static void GenerateRecordDto(CodeWriter w, DtoInfo info, List<DtoPropInfo> props)
        {
            // record 使用 positional 参数
            var paramList = string.Join(", ",
                props.Select(p =>
                {
                    var type = p.ShortType;
                    if (p.IsNullable && !type.EndsWith("?"))
                        type += "?";
                    return $"{type} {p.Name}";
                }));

            w.Line($"/// <summary>");
            w.Line($"/// {info.SourceTypeName} 的 DTO record（由 AutoCode DTO 插件自动生成）");
            w.Line($"/// </summary>");
            w.Line($"public partial record {info.DtoClassName}({paramList})");
            w.Line("{");
            w.Line($"    /// <summary>从实体创建</summary>");
            w.Line($"    public static {info.DtoClassName} FromEntity({info.SourceTypeFull} entity)");
            w.Line($"        => new({string.Join(", ", props.Select(p => $"entity.{p.Name}"))});");
            w.Line();
            w.Line($"    /// <summary>批量转换</summary>");
            w.Line($"    public static List<{info.DtoClassName}> FromEntities(IEnumerable<{info.SourceTypeFull}> entities)");
            w.Line($"        => entities.Select(FromEntity).ToList();");
            w.Line("}");
        }

        private static void GeneratePagedResult(CodeWriter w, DtoInfo info)
        {
            w.Class($"Paged{info.DtoClassName}", c =>
            {
                c.Public();
                c.Doc($"{info.DtoClassName} 分页结果");
                c.Property("Items", p => p.Type($"List<{info.DtoClassName}>").Initializer($"new()"));
                c.Property("Total", p => p.Type("int"));
                c.Property("Page", p => p.Type("int"));
                c.Property("PageSize", p => p.Type("int"));
                c.Property("TotalPages", p => p.Type("int")
                    .ExpressionBody("(int)Math.Ceiling((double)Total / PageSize)"));
                c.Property("HasNext", p => p.Type("bool")
                    .ExpressionBody("Page < TotalPages"));
                c.Property("HasPrevious", p => p.Type("bool")
                    .ExpressionBody("Page > 1"));
            });
        }

        private static HashSet<string>? GetNamedStringSet(AttributeData attr, string name)
        {
            var namedArg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
            if (namedArg.Value.IsNull)
                return null;

            var values = new HashSet<string>(namedArg.Value.Values
                .Select(v => v.Value as string)
                .Where(s => s != null)
                .Cast<string>(), StringComparer.OrdinalIgnoreCase);

            return values.Count > 0 ? values : null;
        }
    }

    #region Models

    internal sealed class DtoInfo
    {
        public string DtoClassName { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string SourceTypeName { get; set; } = "";
        public string SourceTypeFull { get; set; } = "";
        public List<DtoPropInfo> Properties { get; set; } = new List<DtoPropInfo>();
        public bool UseRecord { get; set; }
        public bool ExcludeAuditFields { get; set; } = true;
    }

    internal sealed class DtoPropInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string ShortType { get; set; } = "";
        public bool IsNullable { get; set; }
        public bool HasSetter { get; set; }
    }

    internal sealed class DtoConfig
    {
        public bool UseRecord { get; set; }
        public bool ExcludeAuditFields { get; set; } = true;
    }

    internal sealed class DtoOutput
    {
        public string FileName { get; set; } = "";
        public string Content { get; set; } = "";
    }

    #endregion
}
