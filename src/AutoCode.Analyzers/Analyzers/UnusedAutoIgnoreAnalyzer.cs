using AutoCode.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace AutoCode.Analyzers.Analyzers
{
    /// <summary>
    /// AC003: 检测 [AutoIgnore] 标记在非公共成员上（无意义）
    /// 非公共成员本身就不会被包含在生成的接口中，[AutoIgnore] 是多余的
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class UnusedAutoIgnoreAnalyzer : DiagnosticAnalyzer
    {
        private const string AutoIgnoreAttributeFullName =
            "AutoCode.Model.InterfaceAttribute.AutoIgnoreAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(AutoCodeDiagnosticDescriptors.UnusedAutoIgnore);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
            context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
            context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;
            CheckNonPublicMember(context, method);
        }

        private static void AnalyzeProperty(SymbolAnalysisContext context)
        {
            var property = (IPropertySymbol)context.Symbol;
            CheckNonPublicMember(context, property);
        }

        private static void AnalyzeField(SymbolAnalysisContext context)
        {
            var field = (IFieldSymbol)context.Symbol;
            CheckNonPublicMember(context, field);
        }

        private static void CheckNonPublicMember(SymbolAnalysisContext context, ISymbol symbol)
        {
            // 只检查非公共成员
            if (symbol.DeclaredAccessibility == Accessibility.Public)
                return;

            // 检查是否有 [AutoIgnore] 特性
            var autoIgnoreAttr = symbol.GetAttributes()
                .FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == AutoIgnoreAttributeFullName
                    || a.AttributeClass?.Name == "AutoIgnoreAttribute");

            if (autoIgnoreAttr == null)
                return;

            var diagnostic = Diagnostic.Create(
                AutoCodeDiagnosticDescriptors.UnusedAutoIgnore,
                symbol.Locations.FirstOrDefault(),
                symbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
