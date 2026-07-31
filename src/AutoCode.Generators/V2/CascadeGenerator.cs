using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using AutoCode.Engine.CodeBuilder;

namespace AutoCode.Plugins.Cascade
{
    /// <summary>
    /// 级联生成器 - [AutoEntity] 一个标记触发全链路代码生成。
    /// 根据配置自动生成：DTO + Mapper + Validator + Repository + Service + Controller。
    /// 用户可通过属性参数或 autocode.json 禁用任何子生成。
    /// </summary>
    [Generator]
    public class CascadeGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var cascadeSources = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cds &&
                        cds.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
                        {
                            var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                            return name == "AutoEntity" || name == "AutoEntityAttribute";
                        }),
                    transform: static (ctx, ct) => ExtractEntityConfig(ctx, ct))
                .Where(static s => s != null)
                .SelectMany(static (config, _) => GenerateCascade(config!));

            context.RegisterSourceOutput(cascadeSources, static (spc, file) =>
            {
                spc.AddSource(file.FileName, SourceText.From(file.Content, Encoding.UTF8));
            });
        }

        private static CascadeConfig? ExtractEntityConfig(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var entitySymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
            if (entitySymbol == null)
                return null;

            var attr = entitySymbol.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name == "AutoEntityAttribute" || a.AttributeClass?.Name == "AutoEntity");
            if (attr == null)
                return null;

            // 读取配置
            var config = new CascadeConfig
            {
                EntityName = entitySymbol.Name,
                EntityFull = entitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Namespace = entitySymbol.ContainingNamespace?.ToDisplayString() ?? "",
                KeyType = "int",
                KeyProperty = "Id"
            };

            // 从属性参数读取
            foreach (var arg in attr.NamedArguments)
            {
                switch (arg.Key)
                {
                    case "GenerateDto": config.GenerateDto = arg.Value.Value is true; break;
                    case "GenerateMapper": config.GenerateMapper = arg.Value.Value is true; break;
                    case "GenerateValidation": config.GenerateValidation = arg.Value.Value is true; break;
                    case "GenerateRepository": config.GenerateRepository = arg.Value.Value is true; break;
                    case "GenerateService": config.GenerateService = arg.Value.Value is true; break;
                    case "GenerateController": config.GenerateController = arg.Value.Value is true; break;
                    case "GenerateTests": config.GenerateTests = arg.Value.Value is true; break;
                    case "GenerateLogging": config.GenerateLogging = arg.Value.Value is true; break;
                    case "KeyProperty": config.KeyProperty = arg.Value.Value as string ?? "Id"; break;
                    case "RoutePrefix": config.RoutePrefix = arg.Value.Value as string; break;
                }
            }

            // 获取主键类型
            var keyProp = entitySymbol.GetMembers().OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.Name == config.KeyProperty);
            if (keyProp != null)
                config.KeyType = keyProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            // 获取属性列表
            config.Properties = entitySymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public)
                .Where(p => !p.IsStatic && !p.IsIndexer)
                .Select(p => new EntityProp
                {
                    Name = p.Name,
                    Type = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    FullType = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated,
                    HasSetter = p.SetMethod != null,
                    HasValidation = p.GetAttributes().Any(a =>
                        a.AttributeClass?.ContainingNamespace?.ToDisplayString()
                            == "System.ComponentModel.DataAnnotations")
                })
                .ToList();

            // 路由
            if (string.IsNullOrEmpty(config.RoutePrefix))
                config.RoutePrefix = $"api/{entitySymbol.Name.ToLowerInvariant()}s";

            return config;
        }

        private static List<CascadeFile> GenerateCascade(CascadeConfig config)
        {
            var files = new List<CascadeFile>();
            var e = config.EntityName;
            var ns = config.Namespace;
            var key = config.KeyType;

            // 1. DTO
            if (config.GenerateDto)
            {
                files.Add(GenerateDto(config));
            }

            // 2. Mapper (DTO ↔ Entity)
            if (config.GenerateMapper && config.GenerateDto)
            {
                files.Add(GenerateMapper(config));
            }

            // 3. Validator
            if (config.GenerateValidation && config.Properties.Any(p => p.HasValidation))
            {
                files.Add(GenerateValidator(config));
            }

            // 4. Repository Interface + Implementation
            if (config.GenerateRepository)
            {
                files.Add(GenerateRepositoryInterface(config));
                files.Add(GenerateRepositoryImpl(config));
            }

            // 5. Service Interface + Implementation
            if (config.GenerateService)
            {
                files.Add(GenerateServiceInterface(config));
                files.Add(GenerateServiceImpl(config));
            }

            // 6. Controller
            if (config.GenerateController)
            {
                files.Add(GenerateController(config));
            }

            return files;
        }

        private static CascadeFile GenerateDto(CascadeConfig c)
        {
            var dtoName = $"{c.EntityName}Dto";
            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System", "System.Collections.Generic");
            w.FileScopedNamespace(c.Namespace);

            w.Class(dtoName, cls =>
            {
                cls.Public().Partial();
                cls.Doc($"{c.EntityName} 的数据传输对象（由 [AutoEntity] 级联生成）");

                var dtoProps = c.Properties.Where(p =>
                    p.Name != "RowVersion" && p.Name != "ConcurrencyStamp"
                    && p.Name != "IsDeleted" && p.Name != "DeletedAt");

                foreach (var prop in dtoProps)
                {
                    cls.Property(prop.Name, p =>
                    {
                        var type = prop.Type;
                        if (prop.IsNullable && !type.EndsWith("?")) type += "?";
                        p.Type(type);
                    });
                }

                // FromEntity
                cls.Method("FromEntity", m =>
                {
                    m.Public().Static();
                    m.Parameter(c.EntityFull, "entity");
                    m.Returns(dtoName);
                    m.Body(b =>
                    {
                        b.Return($"new {dtoName}");
                        b.Line("{");
                        foreach (var prop in dtoProps)
                            b.Line($"    {prop.Name} = entity.{prop.Name},");
                        b.Line("}");
                    });
                });

                // ToEntity
                cls.Method("ToEntity", m =>
                {
                    m.Public();
                    m.Parameter(c.EntityFull, "entity");
                    m.Body(b =>
                    {
                        foreach (var prop in dtoProps.Where(p => p.HasSetter))
                            b.Line($"entity.{prop.Name} = {prop.Name};");
                    });
                });
            });

            return new CascadeFile { FileName = $"{dtoName}.g.cs", Content = w.Build() };
        }

        private static CascadeFile GenerateMapper(CascadeConfig c)
        {
            var dtoName = $"{c.EntityName}Dto";
            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System", "System.Collections.Generic", "System.Linq");
            w.FileScopedNamespace(c.Namespace);

            w.Class($"{c.EntityName}Mapper", cls =>
            {
                cls.Public().Static();
                cls.Doc($"{c.EntityName} ↔ {dtoName} 映射扩展（由 [AutoEntity] 级联生成）");

                cls.Method("ToDto", m =>
                {
                    m.Public().Static();
                    m.ThisParameter(c.EntityName, "entity");
                    m.Returns(dtoName);
                    m.Body(b => b.Return($"{dtoName}.FromEntity(entity)"));
                });

                cls.Method("ToDtoList", m =>
                {
                    m.Public().Static();
                    m.ThisParameter($"IEnumerable<{c.EntityName}>", "entities");
                    m.Returns($"List<{dtoName}>");
                    m.Body(b => b.Return("entities.Select(e => e.ToDto()).ToList()"));
                });
            });

            return new CascadeFile { FileName = $"{c.EntityName}Mapper.g.cs", Content = w.Build() };
        }

        private static CascadeFile GenerateValidator(CascadeConfig c)
        {
            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System", "System.Collections.Generic", "System.Text.RegularExpressions");
            w.FileScopedNamespace(c.Namespace);

            w.Class($"{c.EntityName}Validator", cls =>
            {
                cls.Public();
                cls.Doc($"{c.EntityName} 验证器（由 [AutoEntity] 级联生成）");

                cls.Method("Validate", m =>
                {
                    m.Public();
                    m.Parameter(c.EntityName, "input");
                    m.Returns("List<string>");
                    m.Body(b =>
                    {
                        b.Var("errors", "new List<string>()");
                        b.If("input == null").Line("errors.Add(\"Input cannot be null.\");").Return("errors").End();

                        foreach (var prop in c.Properties.Where(p => p.HasValidation && p.Type == "string"))
                        {
                            b.Line($"if (string.IsNullOrWhiteSpace(input.{prop.Name})) errors.Add(\"{prop.Name} is required.\");");
                        }

                        b.Return("errors");
                    });
                });
            });

            return new CascadeFile { FileName = $"{c.EntityName}Validator.g.cs", Content = w.Build() };
        }

        private static CascadeFile GenerateRepositoryInterface(CascadeConfig c)
        {
            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System.Collections.Generic", "System.Threading.Tasks");
            w.FileScopedNamespace(c.Namespace);

            w.Interface($"I{c.EntityName}Repository", i =>
            {
                i.Public();
                i.Doc($"{c.EntityName} 仓储接口（由 [AutoEntity] 级联生成）");
                i.Method("GetAllAsync", m => m.Returns($"Task<List<{c.EntityName}>>"));
                i.Method("GetByIdAsync", m => m.Parameter(c.KeyType, "id").Returns($"Task<{c.EntityName}?>"));
                i.Method("AddAsync", m => m.Parameter(c.EntityName, "entity").Returns($"Task<{c.EntityName}>"));
                i.Method("UpdateAsync", m => m.Parameter(c.EntityName, "entity").Returns($"Task<{c.EntityName}?>"));
                i.Method("DeleteAsync", m => m.Parameter(c.KeyType, "id").Returns("Task<bool>"));
            });

            return new CascadeFile { FileName = $"I{c.EntityName}Repository.g.cs", Content = w.Build() };
        }

        private static CascadeFile GenerateRepositoryImpl(CascadeConfig c)
        {
            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System.Collections.Generic", "System.Linq", "System.Threading.Tasks", "Microsoft.EntityFrameworkCore");
            w.FileScopedNamespace(c.Namespace);

            w.Class($"{c.EntityName}Repository", cls =>
            {
                cls.Public();
                cls.Implements($"I{c.EntityName}Repository");
                cls.Doc($"{c.EntityName} EF Core 仓储（由 [AutoEntity] 级联生成）");

                cls.Field("_context", f => f.Private().ReadOnly().Type("DbContext"));
                cls.Field("_dbSet", f => f.Private().ReadOnly().Type($"DbSet<{c.EntityName}>"));
                cls.Constructor(ctor =>
                {
                    ctor.AssignField("DbContext", "context", "_context");
                    ctor.Body($"_dbSet = _context.Set<{c.EntityName}>();");
                });

                cls.Method("GetAllAsync", m => m.Public().Async().Returns($"Task<List<{c.EntityName}>>")
                    .Body(b => b.Return("await _dbSet.ToListAsync()")));
                cls.Method("GetByIdAsync", m => m.Public().Async().Parameter(c.KeyType, "id").Returns($"Task<{c.EntityName}?>")
                    .Body(b => b.Return("await _dbSet.FindAsync(id)")));
                cls.Method("AddAsync", m => m.Public().Async().Parameter(c.EntityName, "entity").Returns($"Task<{c.EntityName}>")
                    .Body(b => { b.Line("await _dbSet.AddAsync(entity);"); b.Line("await _context.SaveChangesAsync();"); b.Return("entity"); }));
                cls.Method("UpdateAsync", m => m.Public().Async().Parameter(c.EntityName, "entity").Returns($"Task<{c.EntityName}?>")
                    .Body(b => { b.Line("_dbSet.Update(entity);"); b.Line("await _context.SaveChangesAsync();"); b.Return("entity"); }));
                cls.Method("DeleteAsync", m => m.Public().Async().Parameter(c.KeyType, "id").Returns("Task<bool>")
                    .Body(b => { b.Var("e", "await _dbSet.FindAsync(id)"); b.If("e == null").Return("false").End(); b.Line("_dbSet.Remove(e);"); b.Line("await _context.SaveChangesAsync();"); b.Return("true"); }));
            });

            return new CascadeFile { FileName = $"{c.EntityName}Repository.g.cs", Content = w.Build() };
        }

        private static CascadeFile GenerateServiceInterface(CascadeConfig c)
        {
            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System.Collections.Generic", "System.Threading.Tasks");
            w.FileScopedNamespace(c.Namespace);

            w.Interface($"I{c.EntityName}Service", i =>
            {
                i.Public();
                i.Method("GetAllAsync", m => m.Returns($"Task<List<{c.EntityName}Dto>>"));
                i.Method("GetByIdAsync", m => m.Parameter(c.KeyType, "id").Returns($"Task<{c.EntityName}Dto?>"));
                i.Method("CreateAsync", m => m.Parameter($"{c.EntityName}Dto", "dto").Returns($"Task<{c.EntityName}Dto>"));
                i.Method("UpdateAsync", m => m.Parameter(c.KeyType, "id").Parameter($"{c.EntityName}Dto", "dto").Returns($"Task<{c.EntityName}Dto?>"));
                i.Method("DeleteAsync", m => m.Parameter(c.KeyType, "id").Returns("Task<bool>"));
            });

            return new CascadeFile { FileName = $"I{c.EntityName}Service.g.cs", Content = w.Build() };
        }

        private static CascadeFile GenerateServiceImpl(CascadeConfig c)
        {
            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System.Collections.Generic", "System.Linq", "System.Threading.Tasks");
            w.FileScopedNamespace(c.Namespace);

            w.Class($"{c.EntityName}Service", cls =>
            {
                cls.Public();
                cls.Implements($"I{c.EntityName}Service");
                cls.Doc($"{c.EntityName} 服务（由 [AutoEntity] 级联生成）");

                cls.Field("_repo", f => f.Private().ReadOnly().Type($"I{c.EntityName}Repository"));
                cls.Constructor(ctor => ctor.AssignField($"I{c.EntityName}Repository", "repo", "_repo"));

                cls.Method("GetAllAsync", m => m.Public().Async().Returns($"Task<List<{c.EntityName}Dto>>")
                    .Body(b => { b.Var("entities", "await _repo.GetAllAsync()"); b.Return("entities.Select(e => e.ToDto()).ToList()"); }));
                cls.Method("GetByIdAsync", m => m.Public().Async().Parameter(c.KeyType, "id").Returns($"Task<{c.EntityName}Dto?>")
                    .Body(b => { b.Var("e", "await _repo.GetByIdAsync(id)"); b.Return("e?.ToDto()"); }));
                cls.Method("CreateAsync", m => m.Public().Async().Parameter($"{c.EntityName}Dto", "dto").Returns($"Task<{c.EntityName}Dto>")
                    .Body(b => { b.Var("entity", $"new {c.EntityName}()"); b.Line("dto.ToEntity(entity);"); b.Var("created", "await _repo.AddAsync(entity)"); b.Return("created.ToDto()"); }));
                cls.Method("UpdateAsync", m => m.Public().Async().Parameter(c.KeyType, "id").Parameter($"{c.EntityName}Dto", "dto").Returns($"Task<{c.EntityName}Dto?>")
                    .Body(b => { b.Var("e", "await _repo.GetByIdAsync(id)"); b.If("e == null").Return("null").End(); b.Line("dto.ToEntity(e);"); b.Line("await _repo.UpdateAsync(e);"); b.Return("e.ToDto()"); }));
                cls.Method("DeleteAsync", m => m.Public().Async().Parameter(c.KeyType, "id").Returns("Task<bool>")
                    .Body(b => b.Return("await _repo.DeleteAsync(id)")));
            });

            return new CascadeFile { FileName = $"{c.EntityName}Service.g.cs", Content = w.Build() };
        }

        private static CascadeFile GenerateController(CascadeConfig c)
        {
            var w = new CodeWriter();
            w.AutoGeneratedHeader();
            w.Using("System.Collections.Generic", "System.Threading.Tasks", "Microsoft.AspNetCore.Mvc");
            w.FileScopedNamespace(c.Namespace);

            w.Class($"{c.EntityName}sController", cls =>
            {
                cls.Public();
                cls.Attribute("ApiController");
                cls.Attribute($"Route(\"{c.RoutePrefix}\")");
                cls.Attribute("Produces(\"application/json\")");
                cls.Doc($"{c.EntityName} API（由 [AutoEntity] 级联生成）");

                cls.Field("_service", f => f.Private().ReadOnly().Type($"I{c.EntityName}Service"));
                cls.Constructor(ctor => ctor.AssignField($"I{c.EntityName}Service", "service", "_service"));

                cls.Method("GetAll", m => m.Public().Async().Attribute("HttpGet")
                    .Returns($"ActionResult<List<{c.EntityName}Dto>>")
                    .Body(b => b.Return("Ok(await _service.GetAllAsync())")));

                cls.Method("GetById", m => m.Public().Async().Attribute("HttpGet(\"{id}\")")
                    .Parameter(c.KeyType, "id").Returns($"ActionResult<{c.EntityName}Dto>")
                    .Body(b => { b.Var("r", "await _service.GetByIdAsync(id)"); b.Return("r == null ? NotFound() : Ok(r)"); }));

                cls.Method("Create", m => m.Public().Async().Attribute("HttpPost")
                    .Parameter($"{c.EntityName}Dto", "dto", "FromBody").Returns($"ActionResult<{c.EntityName}Dto>")
                    .Body(b => { b.Var("r", "await _service.CreateAsync(dto)"); b.Return("CreatedAtAction(nameof(GetById), new { id = r.Id }, r)"); }));

                cls.Method("Update", m => m.Public().Async().Attribute("HttpPut(\"{id}\")")
                    .Parameter(c.KeyType, "id").Parameter($"{c.EntityName}Dto", "dto", "FromBody")
                    .Returns($"ActionResult<{c.EntityName}Dto>")
                    .Body(b => { b.Var("r", "await _service.UpdateAsync(id, dto)"); b.Return("r == null ? NotFound() : Ok(r)"); }));

                cls.Method("Delete", m => m.Public().Async().Attribute("HttpDelete(\"{id}\")")
                    .Parameter(c.KeyType, "id").Returns("IActionResult")
                    .Body(b => b.Return("await _service.DeleteAsync(id) ? NoContent() : NotFound()")));
            });

            return new CascadeFile { FileName = $"{c.EntityName}sController.g.cs", Content = w.Build() };
        }
    }

    #region Models

    internal sealed class CascadeConfig
    {
        public string EntityName { get; set; } = "";
        public string EntityFull { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string KeyType { get; set; } = "int";
        public string KeyProperty { get; set; } = "Id";
        public string RoutePrefix { get; set; } = "";
        public bool GenerateDto { get; set; } = true;
        public bool GenerateMapper { get; set; } = true;
        public bool GenerateValidation { get; set; } = true;
        public bool GenerateRepository { get; set; } = true;
        public bool GenerateService { get; set; } = true;
        public bool GenerateController { get; set; } = true;
        public bool GenerateTests { get; set; }
        public bool GenerateLogging { get; set; }
        public List<EntityProp> Properties { get; set; } = new List<EntityProp>();
    }

    internal sealed class EntityProp
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string FullType { get; set; } = "";
        public bool IsNullable { get; set; }
        public bool HasSetter { get; set; }
        public bool HasValidation { get; set; }
    }

    internal sealed class CascadeFile
    {
        public string FileName { get; set; } = "";
        public string Content { get; set; } = "";
    }

    #endregion
}
