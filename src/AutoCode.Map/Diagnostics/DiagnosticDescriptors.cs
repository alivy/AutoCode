using Microsoft.CodeAnalysis;

namespace AutoCode.Map.Diagnostics
{
    internal static class DiagnosticCategories
    {
        public const string Mapper = "Mapper";
    }

    /// <summary>
    /// 映射器诊断描述符
    /// </summary>
    public static class DiagnosticDescriptors
    {
        /// <summary>
        /// C# 语言版本不支持
        /// </summary>
        public static readonly DiagnosticDescriptor LanguageVersionNotSupported =
            new DiagnosticDescriptor(
                "RMG046",
                "The used C# language version is not supported by Mapperly, Mapperly requires at least C# 9.0",
                "Mapperly does not support the C# language version {0} but requires at C# least version {1}",
                DiagnosticCategories.Mapper,
                DiagnosticSeverity.Error,
                true
            );

        /// <summary>
        /// 映射目标类型不可访问
        /// </summary>
        public static readonly DiagnosticDescriptor NoParameterlessConstructorFound =
            new DiagnosticDescriptor(
                "RMG002",
                "No accessible parameterless constructor found",
                "{0} has no accessible parameterless constructor",
                DiagnosticCategories.Mapper,
                DiagnosticSeverity.Error,
                true
            );

        /// <summary>
        /// 无法创建映射
        /// </summary>
        public static readonly DiagnosticDescriptor CouldNotCreateMapping =
            new DiagnosticDescriptor(
                "RMG008",
                "Could not create mapping",
                "Could not create mapping from {0} to {1}. Consider implementing the mapping manually.",
                DiagnosticCategories.Mapper,
                DiagnosticSeverity.Error,
                true
            );
    }
}
