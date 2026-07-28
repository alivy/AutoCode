namespace AutoCode.Model
{
    /// <summary>
    /// AutoCode MSBuild 配置选项
    /// 用户可在 .csproj 中通过 PropertyGroup 配置生成器行为
    /// </summary>
    /// <example>
    /// <code>
    /// &lt;PropertyGroup&gt;
    ///   &lt;AutoCode_InterfacePrefix&gt;I&lt;/AutoCode_InterfacePrefix&gt;
    ///   &lt;AutoCode_GenerateNullable&gt;true&lt;/AutoCode_GenerateNullable&gt;
    ///   &lt;AutoCode_MapMethodName&gt;CopyTo&lt;/AutoCode_MapMethodName&gt;
    /// &lt;/PropertyGroup&gt;
    /// </code>
    /// </example>
    public static class AutoCodeOptions
    {
        /// <summary>
        /// MSBuild 属性前缀
        /// </summary>
        public const string Prefix = "build_property.AutoCode_";

        /// <summary>
        /// 接口名前缀（默认 "I"）
        /// </summary>
        public const string InterfacePrefix = Prefix + "InterfacePrefix";

        /// <summary>
        /// 是否生成可空注解（默认 "true"）
        /// </summary>
        public const string GenerateNullable = Prefix + "GenerateNullable";

        /// <summary>
        /// 映射方法名（默认 "CopyTo"）
        /// </summary>
        public const string MapMethodName = Prefix + "MapMethodName";

        /// <summary>
        /// 模板输出文件后缀（默认 ".generated.cs"）
        /// </summary>
        public const string TemplateSuffix = Prefix + "TemplateSuffix";

        /// <summary>
        /// 是否启用分析器诊断（默认 "true"）
        /// </summary>
        public const string EnableDiagnostics = Prefix + "EnableDiagnostics";
    }
}
