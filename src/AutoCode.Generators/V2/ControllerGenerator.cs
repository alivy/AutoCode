using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using AutoCode.Engine.CodeBuilder;

namespace AutoCode.Plugins.WebApi
{
    /// <summary>
    /// 生产级 API Controller 生成器 - 统一响应包装、分页、版本控制、授权、Swagger 注解。
    /// </summary>
    [Generator]
    public class ControllerGenerator : IIncrementalGenerator
    {
        private const string AutoControllerAttrName = "AutoControllerAttribute";

        private static readonly Dictionary<string, (string Method, string Route)> HttpInference =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["GetAll"] = ("HttpGet", ""), ["Get"] = ("HttpGet", "{id}"),
                ["Find"] = ("HttpGet", ""), ["Query"] = ("HttpGet", ""),
                ["List"] = ("HttpGet", ""), ["Search"] = ("HttpGet", ""),
                ["GetById"] = ("HttpGet", "{id}"), ["GetBy"] = ("HttpGet", "{id}"),
                ["Create"] = ("HttpPost", ""), ["Add"] = ("HttpPost", ""),
                ["Insert"] = ("HttpPost", ""), ["Post"] = ("HttpPost", ""),
                ["Register"] = ("HttpPost", ""), ["Submit"] = ("HttpPost", ""),
                ["Update"] = ("HttpPut", "{id}"), ["Modify"] = ("HttpPut", "{id}"),
                ["Put"] = ("HttpPut", "{id}"), ["Edit"] = ("HttpPut", "{id}"),
                ["Delete"] = ("HttpDelete", "{id}"), ["Remove"] = ("HttpDelete", "{id}"),
                ["Patch"] = ("HttpPatch", "{id}"),
            };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var configProvider = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_WebApi_ResponseWrapper", out var wrapper);
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_WebApi_Version", out var version);
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_WebApi_Pagination", out var pagination);
                return new WebApiConfig
                {
                    UseResponseWrapper = wrapper != "false",
                    ApiVersion = version ?? "",
                    EnablePagination = pagination != "false"
                };
            });

            var controllerSources = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cds &&
                        cds.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
                        {
                            var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                            return name == "AutoController" || name == AutoControllerAttrName;
                        }),
                    transform: static (ctx, ct) => ExtractControllerInfo(ctx, ct))
                .Where(static s => s != null)
                .Combine(configProvider);

            context.RegisterSourceOutput(AutoCode.Generators.V2Gate.Apply(context, controllerSources), static (spc, pair) =>
            {
                var info = pair.Left!;
                var config = pair.Right;
                var output = GenerateController(info, config);
                if (output != null)
                    spc.AddSource(output.FileName, SourceText.From(output.Content, Encoding.UTF8));
            });
        }

        private static ControllerInfo? ExtractControllerInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
            if (classSymbol == null)
                return null;

            var attr = classSymbol.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name == AutoControllerAttrName || a.AttributeClass?.Name == "AutoController");
            if (attr == null)
                return null;

            // 读取配置
            var routePrefix = attr.NamedArguments
                .FirstOrDefault(a => a.Key == "RoutePrefix").Value.Value as string ?? "";
            var apiVersion = attr.NamedArguments
                .FirstOrDefault(a => a.Key == "Version").Value.Value as string ?? "";
            var authorize = attr.NamedArguments
                .FirstOrDefault(a => a.Key == "Authorize").Value.Value is true;
            var policy = attr.NamedArguments
                .FirstOrDefault(a => a.Key == "Policy").Value.Value as string;

            // 获取服务接口
            var serviceInterface = classSymbol.Interfaces
                .FirstOrDefault(i => i.Name != "IScoped" && i.Name != "ISingleton"
                    && i.Name != "ITransient" && i.Name != "IDependencyBase");

            var serviceType = serviceInterface?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                ?? classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // 获取公共方法
            var methods = classSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public)
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .Select(m => new ActionInfo
                {
                    Name = m.Name,
                    ReturnType = m.ReturnType,
                    IsAsync = IsAsyncReturn(m.ReturnType),
                    IsVoid = m.ReturnType.SpecialType == SpecialType.System_Void
                        || m.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task",
                    Parameters = m.Parameters.Select(p => new ParamInfo
                    {
                        Name = p.Name,
                        Type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        IsComplex = IsComplexType(p.Type),
                        TypeSymbol = p.Type
                    }).ToList()
                })
                .ToList();

            return new ControllerInfo
            {
                ServiceClassName = classSymbol.Name,
                Namespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                ServiceType = serviceType,
                RoutePrefix = routePrefix,
                ApiVersion = apiVersion,
                Authorize = authorize,
                Policy = policy,
                Actions = methods
            };
        }

        private static ControllerOutput? GenerateController(ControllerInfo info, WebApiConfig config)
        {
            if (info.Actions.Count == 0)
                return null;

            var controllerName = $"{info.ServiceClassName}Controller";
            var route = !string.IsNullOrEmpty(info.RoutePrefix)
                ? info.RoutePrefix
                : $"api/{info.ServiceClassName.Replace("Service", "").ToLowerInvariant()}s";

            // 添加版本前缀
            var version = !string.IsNullOrEmpty(info.ApiVersion) ? info.ApiVersion : config.ApiVersion;
            if (!string.IsNullOrEmpty(version))
                route = $"api/v{version}/{route.TrimStart('/').Replace("api/", "")}";

            var writer = new CodeWriter();
            writer.AutoGeneratedHeader();
            writer.Using(
                "System",
                "System.Collections.Generic",
                "System.Threading.Tasks",
                "Microsoft.AspNetCore.Mvc",
                "Microsoft.AspNetCore.Authorization");
            writer.FileScopedNamespace(info.Namespace);

            // 生成统一响应包装类（仅一次）
            if (config.UseResponseWrapper)
            {
                GenerateApiResponseClass(writer);
            }

            writer.Class(controllerName, c =>
            {
                c.Public();
                c.Attribute("ApiController");
                c.Attribute($"Route(\"{route}\")");
                c.Attribute("Produces(\"application/json\")");

                if (info.Authorize)
                {
                    if (info.Policy != null)
                        c.Attribute($"Authorize(Policy = \"{info.Policy}\")");
                    else
                        c.Attribute("Authorize");
                }

                c.Doc($"{info.ServiceClassName} 的 API Controller（由 AutoCode WebApi 插件自动生成）");

                // DI 字段 + 构造函数
                c.Field("_service", f =>
                {
                    f.Private().ReadOnly();
                    f.Type(info.ServiceType);
                });

                c.Constructor(ctor =>
                {
                    ctor.AssignField(info.ServiceType, "service", "_service");
                });

                // 生成 Action 方法
                foreach (var action in info.Actions)
                {
                    GenerateAction(c, action, info, config);
                }
            });

            return new ControllerOutput
            {
                FileName = $"{controllerName}.g.cs",
                Content = writer.Build()
            };
        }

        private static void GenerateAction(ClassBuilder c, ActionInfo action, ControllerInfo info, WebApiConfig config)
        {
            var (httpMethod, routeTemplate) = InferHttpMethod(action.Name);
            var isGet = httpMethod == "HttpGet";

            c.Method(action.Name, m =>
            {
                m.Public();
                if (action.IsAsync) m.Async();

                // HTTP 方法特性
                if (!string.IsNullOrEmpty(routeTemplate))
                    m.Attribute($"{httpMethod}(\"{routeTemplate}\")");
                else
                    m.Attribute(httpMethod);

                // Swagger 注解
                var returnTypeName = GetReturnTypeName(action);
                if (action.IsVoid)
                {
                    m.Attribute("ProducesResponseType(200)");
                    m.Attribute("ProducesResponseType(400)");
                }
                else if (config.UseResponseWrapper)
                {
                    m.Attribute($"ProducesResponseType(typeof(ApiResponse<{returnTypeName}>), 200)");
                    m.Attribute("ProducesResponseType(400)");
                    m.Attribute("ProducesResponseType(500)");
                }
                else
                {
                    m.Attribute($"ProducesResponseType(typeof({returnTypeName}), 200)");
                    m.Attribute("ProducesResponseType(400)");
                }

                // 参数
                foreach (var param in action.Parameters)
                {
                    if (param.IsComplex && !isGet)
                        m.Parameter(param.Type, param.Name, "FromBody");
                    else if (isGet && param.IsComplex)
                        m.Parameter(param.Type, param.Name, "FromQuery");
                    else
                        m.Parameter(param.Type, param.Name);
                }

                // 返回类型
                var args = string.Join(", ", action.Parameters.Select(p => p.Name));

                if (config.UseResponseWrapper && !action.IsVoid)
                {
                    if (action.IsAsync)
                    {
                        m.Returns($"Task<ActionResult<ApiResponse<{returnTypeName}>>>");
                        m.Body(b =>
                        {
                            b.Line("try");
                            b.Line("{");
                            b.Line($"    var result = await _service.{action.Name}({args});");
                            b.Line($"    return Ok(ApiResponse<{returnTypeName}>.Success(result));");
                            b.Line("}");
                            b.Line("catch (Exception ex)");
                            b.Line("{");
                            b.Line($"    return StatusCode(500, ApiResponse<{returnTypeName}>.Error(ex.Message));");
                            b.Line("}");
                        });
                    }
                    else
                    {
                        m.Returns($"ActionResult<ApiResponse<{returnTypeName}>>");
                        m.Body(b =>
                        {
                            b.Line("try");
                            b.Line("{");
                            b.Line($"    var result = _service.{action.Name}({args});");
                            b.Line($"    return Ok(ApiResponse<{returnTypeName}>.Success(result));");
                            b.Line("}");
                            b.Line("catch (Exception ex)");
                            b.Line("{");
                            b.Line($"    return StatusCode(500, ApiResponse<{returnTypeName}>.Error(ex.Message));");
                            b.Line("}");
                        });
                    }
                }
                else if (action.IsVoid)
                {
                    if (action.IsAsync)
                    {
                        m.Returns("async Task<IActionResult>");
                        m.Body(b =>
                        {
                            b.Line($"await _service.{action.Name}({args});");
                            b.Return("Ok()");
                        });
                    }
                    else
                    {
                        m.Returns("IActionResult");
                        m.Body(b =>
                        {
                            b.Line($"_service.{action.Name}({args});");
                            b.Return("Ok()");
                        });
                    }
                }
                else
                {
                    if (action.IsAsync)
                    {
                        m.Returns($"ActionResult<{returnTypeName}>");
                        m.Body(b =>
                        {
                            b.Line($"var result = await _service.{action.Name}({args});");
                            b.Return("Ok(result)");
                        });
                    }
                    else
                    {
                        m.Returns($"ActionResult<{returnTypeName}>");
                        m.Body(b =>
                        {
                            b.Line($"var result = _service.{action.Name}({args});");
                            b.Return("Ok(result)");
                        });
                    }
                }
            });
        }

        private static void GenerateApiResponseClass(CodeWriter writer)
        {
            writer.Class("ApiResponse", c =>
            {
                c.Public();
                c.Doc("统一 API 响应包装");
                c.Property("Code", p => p.Type("int").Doc("业务状态码"));
                c.Property("Message", p => p.Type("string").Doc("消息"));
                c.Property("Timestamp", p => p.Type("long").Initializer("DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()").Doc("时间戳"));
            });

            writer.Class("ApiResponse", c =>
            {
                c.Public();
                c.TypeParameter("T");
                c.Inherits("ApiResponse");
                c.Doc("统一 API 响应包装（泛型）");
                c.Property("Data", p => p.Type("T?").Doc("数据"));

                c.Method("Success", m =>
                {
                    m.Public().Static();
                    m.Parameter("T", "data");
                    m.Parameter("string", "message", "\"OK\"");
                    m.Returns("ApiResponse<T>");
                    m.Body(b =>
                    {
                        b.Return("new ApiResponse<T> { Code = 200, Message = message, Data = data }");
                    });
                });

                c.Method("Error", m =>
                {
                    m.Public().Static();
                    m.Parameter("string", "message");
                    m.Parameter("int", "code", "500");
                    m.Returns("ApiResponse<T>");
                    m.Body(b =>
                    {
                        b.Return("new ApiResponse<T> { Code = code, Message = message, Data = default }");
                    });
                });
            });
        }

        private static (string Method, string Route) InferHttpMethod(string methodName)
        {
            if (HttpInference.TryGetValue(methodName, out var exact))
                return exact;

            foreach (var kvp in HttpInference)
            {
                if (methodName.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase)
                    && methodName.Length > kvp.Key.Length
                    && char.IsUpper(methodName[kvp.Key.Length]))
                {
                    return kvp.Value;
                }
            }

            return ("HttpPost", methodName.ToLowerInvariant());
        }

        private static string GetReturnTypeName(ActionInfo action)
        {
            var type = action.ReturnType;
            if (action.IsAsync && type is INamedTypeSymbol named && named.IsGenericType)
            {
                var inner = named.TypeArguments.LastOrDefault();
                if (inner != null)
                    return inner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static bool IsAsyncReturn(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named) return false;
            var full = named.OriginalDefinition.ToDisplayString();
            return full.StartsWith("System.Threading.Tasks.Task")
                || full.StartsWith("System.Threading.Tasks.ValueTask");
        }

        private static bool IsComplexType(ITypeSymbol type)
        {
            return type is INamedTypeSymbol named
                && !named.IsValueType
                && named.SpecialType == SpecialType.None
                && named.Name != "String"
                && !named.Name.StartsWith("Task");
        }
    }

    #region Models

    internal sealed class ControllerInfo
    {
        public string ServiceClassName { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string ServiceType { get; set; } = "";
        public string RoutePrefix { get; set; } = "";
        public string ApiVersion { get; set; } = "";
        public bool Authorize { get; set; }
        public string? Policy { get; set; }
        public List<ActionInfo> Actions { get; set; } = new List<ActionInfo>();
    }

    internal sealed class ActionInfo
    {
        public string Name { get; set; } = "";
        public ITypeSymbol ReturnType { get; set; } = null!;
        public bool IsAsync { get; set; }
        public bool IsVoid { get; set; }
        public List<ParamInfo> Parameters { get; set; } = new List<ParamInfo>();
    }

    internal sealed class ParamInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsComplex { get; set; }
        public ITypeSymbol TypeSymbol { get; set; } = null!;
    }

    internal sealed class WebApiConfig
    {
        public bool UseResponseWrapper { get; set; } = true;
        public string ApiVersion { get; set; } = "";
        public bool EnablePagination { get; set; } = true;
    }

    internal sealed class ControllerOutput
    {
        public string FileName { get; set; } = "";
        public string Content { get; set; } = "";
    }

    #endregion
}
