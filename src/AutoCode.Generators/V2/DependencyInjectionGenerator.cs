using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using AutoCode.Engine.CodeBuilder;

namespace AutoCode.Plugins.DependencyInjection
{
    /// <summary>
    /// 编译时依赖注入注册生成器 v2 - 基于 AutoCode Engine。
    /// 增强功能：Keyed Services、开放泛型、装饰器注册、HostedService、模块隔离。
    /// </summary>
    [Generator]
    public class DependencyInjectionGenerator : IIncrementalGenerator
    {
        private static readonly HashSet<string> LifetimeInterfaceNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "IScoped", "ISingleton", "ITransient", "IDependencyBase"
        };

        private static readonly HashSet<string> SpecialInterfaces = new HashSet<string>(StringComparer.Ordinal)
        {
            "IHostedService", "IHealthCheck"
        };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 收集所有实现了生命周期接口的类
            var registrations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, ct) => ExtractRegistration(ctx, ct))
                .Where(static r => r != null)
                .Collect();

            // 读取配置
            var configProvider = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_DI_Namespace", out var ns);
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_DI_MethodName", out var method);
                provider.GlobalOptions.TryGetValue("build_property.AutoCode_DI_ModulePerAssembly", out var module);
                return new DIConfig
                {
                    Namespace = ns ?? "AutoCode.DependencyInjection",
                    MethodName = method ?? "AddAutoDI",
                    ModulePerAssembly = module == "true"
                };
            });

            var combined = registrations.Combine(configProvider);

            context.RegisterSourceOutput(combined, static (spc, pair) =>
            {
                var items = pair.Left.Where(r => r != null).Cast<ServiceRegistration>().ToList();
                var config = pair.Right;

                if (items.Count == 0) return;

                var source = GenerateSource(items, config);
                spc.AddSource("AutoDependencyInjection.g.cs", SourceText.From(source, Encoding.UTF8));
            });
        }

        private static ServiceRegistration? ExtractRegistration(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
            if (classSymbol == null || classSymbol.IsAbstract || classSymbol.IsStatic)
                return null;

            // 查找生命周期接口
            string? lifetime = null;
            string? serviceKey = null;

            foreach (var iface in classSymbol.AllInterfaces)
            {
                switch (iface.Name)
                {
                    case "IScoped": lifetime = "Scoped"; break;
                    case "ISingleton": lifetime = "Singleton"; break;
                    case "ITransient": lifetime = "Transient"; break;
                }

                // 检查 Keyed 接口（如 IScopedKeyed）
                if (iface.Name.EndsWith("Keyed") && iface.IsGenericType && iface.TypeArguments.Length == 1)
                {
                    var baseName = iface.Name.Replace("Keyed", "");
                    lifetime = baseName switch
                    {
                        "IScoped" => "Scoped",
                        "ISingleton" => "Singleton",
                        "ITransient" => "Transient",
                        _ => lifetime
                    };
                    // Key 是泛型参数的常量值（通常通过特性获取）
                }
            }

            if (lifetime == null)
                return null;

            // 获取服务接口（排除生命周期接口和特殊接口）
            var serviceInterfaces = classSymbol.Interfaces
                .Where(i => !LifetimeInterfaceNames.Contains(i.Name))
                .Where(i => !SpecialInterfaces.Contains(i.Name))
                .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .ToList();

            // 检测特殊接口
            var isHostedService = classSymbol.AllInterfaces.Any(i => i.Name == "IHostedService");
            var isHealthCheck = classSymbol.AllInterfaces.Any(i => i.Name == "IHealthCheck");

            // 检测开放泛型
            var isOpenGeneric = classSymbol.IsGenericType;

            // 检测 [ServiceKey] 特性
            var keyAttr = classSymbol.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name == "ServiceKeyAttribute");
            if (keyAttr != null && keyAttr.ConstructorArguments.Length > 0)
            {
                serviceKey = keyAttr.ConstructorArguments[0].Value?.ToString();
            }

            var implementationType = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var assemblyName = classSymbol.ContainingAssembly?.Name ?? "";

            return new ServiceRegistration
            {
                ImplementationType = implementationType,
                ImplementationName = classSymbol.Name,
                ServiceInterfaces = serviceInterfaces,
                Lifetime = lifetime,
                IsHostedService = isHostedService,
                IsHealthCheck = isHealthCheck,
                IsOpenGeneric = isOpenGeneric,
                ServiceKey = serviceKey,
                AssemblyName = assemblyName
            };
        }

        private static string GenerateSource(List<ServiceRegistration> registrations, DIConfig config)
        {
            var writer = new CodeWriter();
            writer.AutoGeneratedHeader();
            writer.Using(
                "System",
                "Microsoft.Extensions.DependencyInjection",
                "Microsoft.Extensions.DependencyInjection.Extensions");
            writer.FileScopedNamespace(config.Namespace);

            writer.Class("AutoDependencyInjection", c =>
            {
                c.Public().Static().Partial();
                c.Doc("编译时依赖注入注册（由 AutoCode DI 插件自动生成）",
                    "替代运行时反射扫描，兼容 NativeAOT / Trimming");

                // 主注册方法
                c.Method(config.MethodName, m =>
                {
                    m.Public().Static();
                    m.Doc("注册所有标记了 IScoped/ISingleton/ITransient 的服务");
                    m.ThisParameter("IServiceCollection", "services");
                    m.Returns("IServiceCollection");
                    m.Body(b =>
                    {
                        // 常规服务注册
                        foreach (var reg in registrations.Where(r => !r.IsHostedService && !r.IsHealthCheck))
                        {
                            GenerateRegistrationLine(b, reg);
                        }

                        b.Blank();
                        b.Comment("Hosted Services");
                        foreach (var reg in registrations.Where(r => r.IsHostedService))
                        {
                            b.Line($"services.AddHostedService<{reg.ImplementationType}>();");
                        }

                        b.Blank();
                        b.Comment("Health Checks");
                        foreach (var reg in registrations.Where(r => r.IsHealthCheck))
                        {
                            var name = reg.ImplementationName.Replace("HealthCheck", "").ToLowerInvariant();
                            b.Line($"services.AddHealthChecks().AddCheck<{reg.ImplementationType}>(\"{name}\");");
                        }

                        b.Blank();
                        b.Return("services");
                    });
                });

                // 按模块（程序集）生成独立注册方法
                if (config.ModulePerAssembly)
                {
                    var assemblies = registrations
                        .GroupBy(r => r.AssemblyName)
                        .Where(g => !string.IsNullOrEmpty(g.Key));

                    foreach (var group in assemblies)
                    {
                        var moduleName = SanitizeName(group.Key);
                        c.Method($"Add{moduleName}Services", m =>
                        {
                            m.Public().Static();
                            m.Doc($"注册 {group.Key} 程序集中的服务");
                            m.ThisParameter("IServiceCollection", "services");
                            m.Returns("IServiceCollection");
                            m.Body(b =>
                            {
                                foreach (var reg in group)
                                {
                                    GenerateRegistrationLine(b, reg);
                                }
                                b.Return("services");
                            });
                        });
                    }
                }
            });

            return writer.Build();
        }

        private static void GenerateRegistrationLine(Engine.CodeBuilder.BodyBuilder b, ServiceRegistration reg)
        {
            if (reg.IsOpenGeneric)
            {
                // 开放泛型注册
                var openImpl = reg.ImplementationType;
                if (reg.ServiceInterfaces.Count > 0)
                {
                    foreach (var svc in reg.ServiceInterfaces)
                    {
                        b.Line($"services.TryAdd{reg.Lifetime}(typeof({svc}), typeof({openImpl}));");
                    }
                }
                return;
            }

            if (reg.ServiceKey != null)
            {
                // Keyed Service (.NET 8+)
                if (reg.ServiceInterfaces.Count > 0)
                {
                    foreach (var svc in reg.ServiceInterfaces)
                    {
                        b.Line($"services.TryAddKeyed{reg.Lifetime}<{svc}, {reg.ImplementationType}>(\"{reg.ServiceKey}\");");
                    }
                }
                else
                {
                    b.Line($"services.TryAddKeyed{reg.Lifetime}<{reg.ImplementationType}>(\"{reg.ServiceKey}\");");
                }
                return;
            }

            if (reg.ServiceInterfaces.Count > 0)
            {
                foreach (var svc in reg.ServiceInterfaces)
                {
                    b.Line($"services.TryAdd{reg.Lifetime}<{svc}, {reg.ImplementationType}>();");
                }
            }
            else
            {
                // 无服务接口时注册自身
                b.Line($"services.TryAdd{reg.Lifetime}<{reg.ImplementationType}>();");
            }
        }

        private static string SanitizeName(string name)
        {
            // 移除非法字符，PascalCase
            var parts = name.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(p =>
                p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1) : ""));
        }
    }

    internal sealed class ServiceRegistration
    {
        public string ImplementationType { get; set; } = "";
        public string ImplementationName { get; set; } = "";
        public List<string> ServiceInterfaces { get; set; } = new List<string>();
        public string Lifetime { get; set; } = "Scoped";
        public bool IsHostedService { get; set; }
        public bool IsHealthCheck { get; set; }
        public bool IsOpenGeneric { get; set; }
        public string? ServiceKey { get; set; }
        public string AssemblyName { get; set; } = "";
    }

    internal sealed class DIConfig
    {
        public string Namespace { get; set; } = "AutoCode.DependencyInjection";
        public string MethodName { get; set; } = "AddAutoDI";
        public bool ModulePerAssembly { get; set; }
    }
}
