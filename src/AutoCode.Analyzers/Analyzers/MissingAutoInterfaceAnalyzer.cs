using AutoCode.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace AutoCode.Analyzers.Analyzers
{
    /// <summary>
    /// AC001: 检测类实现了接口但缺少 [AutoInterface] 特性
    /// 当类实现了 IXxx 接口且所有公共方法/属性都在接口中时，建议使用 [AutoInterface]
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MissingAutoInterfaceAnalyzer : DiagnosticAnalyzer
    {
        private const string AutoInterfaceAttributeFullName =
            "AutoCode.Model.InterfaceAttribute.AutoInterfaceAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(AutoCodeDiagnosticDescriptors.MissingAutoInterface);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            var namedType = (INamedTypeSymbol)context.Symbol;

            // 只分析类
            if (namedType.TypeKind != TypeKind.Class)
                return;

            // 跳过抽象类和静态类
            if (namedType.IsAbstract || namedType.IsStatic)
                return;

            // 检查是否已有 [AutoInterface] 特性
            if (HasAutoInterfaceAttribute(namedType))
                return;

            // 获取实现的接口
            var interfaces = namedType.Interfaces;
            if (interfaces.Length == 0)
                return;

            // 获取类的所有公共成员（方法和属性）
            var publicMembers = namedType.GetMembers()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public)
                .Where(m => m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol)
                .ToList();

            if (publicMembers.Count == 0)
                return;

            // 检查是否所有公共成员都在某个接口中
            foreach (var iface in interfaces)
            {
                var interfaceMembers = new HashSet<string>(iface.GetMembers()
                    .Where(m => m is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol)
                    .Select(m => m.Name));

                // 如果接口的成员覆盖了类的所有公共成员
                var allCovered = publicMembers.All(m => interfaceMembers.Contains(m.Name));
                if (allCovered && interfaceMembers.Count > 0)
                {
                    var diagnostic = Diagnostic.Create(
                        AutoCodeDiagnosticDescriptors.MissingAutoInterface,
                        namedType.Locations.FirstOrDefault(),
                        namedType.Name,
                        iface.Name);
                    context.ReportDiagnostic(diagnostic);
                    return; // 只报告一次
                }
            }
        }

        private static bool HasAutoInterfaceAttribute(INamedTypeSymbol symbol)
        {
            return symbol.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == AutoInterfaceAttributeFullName
                || a.AttributeClass?.Name == "AutoInterfaceAttribute");
        }
    }
}
