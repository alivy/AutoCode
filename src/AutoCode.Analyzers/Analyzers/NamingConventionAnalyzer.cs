using AutoCode.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace AutoCode.Analyzers.Analyzers
{
    /// <summary>
    /// AC006: 命名规范强制 - Service/Controller/Interface/DTO 命名约定
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class NamingConventionAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(AutoCodeDiagnosticDescriptors.NamingConvention);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var namedType = (INamedTypeSymbol)context.Symbol;
            if (namedType.TypeKind != TypeKind.Class && namedType.TypeKind != TypeKind.Interface)
                return;

            // 跳过生成代码
            if (namedType.GetAttributes().Any(a => a.AttributeClass?.Name == "GeneratedCodeAttribute"))
                return;

            var name = namedType.Name;
            var interfaces = namedType.AllInterfaces;

            // 规则1: 实现 IScoped/ISingleton/ITransient 的类应以 Service 结尾
            var hasLifetimeInterface = interfaces.Any(i =>
                i.Name == "IScoped" || i.Name == "ISingleton" || i.Name == "ITransient");

            if (hasLifetimeInterface && namedType.TypeKind == TypeKind.Class)
            {
                if (!name.EndsWith("Service") && !name.EndsWith("Repository")
                    && !name.EndsWith("Handler") && !name.EndsWith("Provider"))
                {
                    Report(context, namedType, name, "Service");
                }
            }

            // 规则2: 继承 ControllerBase 的类应以 Controller 结尾
            var baseType = namedType.BaseType;
            while (baseType != null)
            {
                if (baseType.Name == "ControllerBase" || baseType.Name == "Controller")
                {
                    if (!name.EndsWith("Controller"))
                    {
                        Report(context, namedType, name, "Controller");
                    }
                    break;
                }
                baseType = baseType.BaseType;
            }
        }

        private static void Report(SymbolAnalysisContext context, INamedTypeSymbol type, string currentName, string expectedSuffix)
        {
            var diagnostic = Diagnostic.Create(
                AutoCodeDiagnosticDescriptors.NamingConvention,
                type.Locations.FirstOrDefault(),
                currentName,
                expectedSuffix);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
