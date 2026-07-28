using AutoCode.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace AutoCode.Analyzers.Analyzers
{
    /// <summary>
    /// AC004: 分层违规检测 - Controller 不应直接引用数据层类型
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LayerViolationAnalyzer : DiagnosticAnalyzer
    {
        private static readonly string[] DataLayerSuffixes = { "DbContext", "Repository", "DbSet" };
        private static readonly string[] ControllerSuffixes = { "Controller" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(AutoCodeDiagnosticDescriptors.LayerViolation);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var namedType = (INamedTypeSymbol)context.Symbol;
            if (namedType.TypeKind != TypeKind.Class)
                return;

            // 只检查 Controller 类
            if (!ControllerSuffixes.Any(s => namedType.Name.EndsWith(s)))
                return;

            // 检查构造函数参数和字段类型是否引用了数据层
            var membersToCheck = namedType.GetMembers()
                .OfType<IFieldSymbol>()
                .Select(f => f.Type)
                .Concat(namedType.InstanceConstructors
                    .SelectMany(c => c.Parameters)
                    .Select(p => p.Type));

            foreach (var type in membersToCheck)
            {
                var typeName = type.Name;
                if (DataLayerSuffixes.Any(s => typeName.EndsWith(s)))
                {
                    var diagnostic = Diagnostic.Create(
                        AutoCodeDiagnosticDescriptors.LayerViolation,
                        namedType.Locations.FirstOrDefault(),
                        namedType.Name,
                        typeName);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}
