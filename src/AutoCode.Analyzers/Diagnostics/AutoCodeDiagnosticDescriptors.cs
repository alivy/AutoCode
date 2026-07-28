using Microsoft.CodeAnalysis;

namespace AutoCode.Analyzers.Diagnostics
{
    /// <summary>
    /// AutoCode 诊断分类
    /// </summary>
    internal static class DiagnosticCategories
    {
        public const string Usage = "AutoCode.Usage";
        public const string Design = "AutoCode.Design";
    }

    /// <summary>
    /// AutoCode 诊断描述符定义
    /// </summary>
    public static class AutoCodeDiagnosticDescriptors
    {
        /// <summary>
        /// AC001: 类实现了接口但缺少 [AutoInterface] 特性
        /// </summary>
        public static readonly DiagnosticDescriptor MissingAutoInterface =
            new DiagnosticDescriptor(
                "AC001",
                "类实现了接口但缺少 [AutoInterface] 特性",
                "类 '{0}' 实现了接口 '{1}' 但未标记 [AutoInterface]，建议添加该特性以自动生成接口",
                DiagnosticCategories.Usage,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description: "当类实现了接口且所有公共成员都在接口中时，可以使用 [AutoInterface] 特性自动生成接口，减少手动维护。");

        /// <summary>
        /// AC002: [AutoInterface] 类的公共成员与生成接口不一致
        /// </summary>
        public static readonly DiagnosticDescriptor InterfaceDivergence =
            new DiagnosticDescriptor(
                "AC002",
                "接口与实现类成员不一致",
                "类 '{0}' 的公共成员 '{1}' 未在接口 '{2}' 中定义，可能存在接口与实现不同步",
                DiagnosticCategories.Design,
                DiagnosticSeverity.Info,
                isEnabledByDefault: true,
                description: "当 [AutoInterface] 标记的类添加了新的公共成员但接口未更新时，会触发此提示。");

        /// <summary>
        /// AC003: [AutoIgnore] 标记在非公共成员上（无意义）
        /// </summary>
        public static readonly DiagnosticDescriptor UnusedAutoIgnore =
            new DiagnosticDescriptor(
                "AC003",
                "[AutoIgnore] 标记在非公共成员上",
                "成员 '{0}' 不是公共成员，[AutoIgnore] 标记无意义，建议移除",
                DiagnosticCategories.Usage,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description: "[AutoIgnore] 仅对公共成员有意义，因为非公共成员本身就不会被包含在生成的接口中。");
    }
}
