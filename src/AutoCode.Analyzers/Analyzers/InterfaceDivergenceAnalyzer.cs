using AutoCode.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace AutoCode.Analyzers.Analyzers
{
    /// <summary>
    /// AC002: 检测 [AutoInterface] 类的公共成员与生成接口不一致
    /// 当类添加了新的公共成员但接口未更新时触发
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class InterfaceDivergenceAnalyzer : DiagnosticAnalyzer
    {
        private const string AutoInterfaceAttributeFullName =
            "AutoCode.Model.InterfaceAttribute.AutoInterfaceAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(AutoCodeDiagnosticDescriptors.InterfaceDivergence);

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

            // 只分析标记了 [AutoInterface] 的类
            if (!HasAutoInterfaceAttribute(namedType))
                return;

            // 获取实现的接口
            var interfaces = namedType.Interfaces;
            if (interfaces.Length == 0)
                return;

            // 获取类的所有公共普通方法和属性
            var publicMembers = namedType.GetMembers()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public)
                .Where(m => m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol)
                .Where(m => !HasAutoIgnoreAttribute(m))
                .ToList();

            // 获取所有接口成员的并集
            var allInterfaceMemberNames = new HashSet<string>(interfaces
                .SelectMany(i => i.GetMembers())
                .Where(m => m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol)
                .Select(m => m.Name));

            // 找出类中有但接口中没有的成员
            foreach (var member in publicMembers)
            {
                if (!allInterfaceMemberNames.Contains(member.Name))
                {
                    var primaryInterface = interfaces.FirstOrDefault();
                    var diagnostic = Diagnostic.Create(
                        AutoCodeDiagnosticDescriptors.InterfaceDivergence,
                        member.Locations.FirstOrDefault(),
                        namedType.Name,
                        member.Name,
                        primaryInterface?.Name ?? "generated interface");
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static bool HasAutoInterfaceAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == AutoInterfaceAttributeFullName
                || a.AttributeClass?.Name == "AutoInterfaceAttribute");
        }

        private static bool HasAutoIgnoreAttribute(ISymbol symbol)
        {
            return symbol.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "AutoIgnoreAttribute");
        }
    }
}
