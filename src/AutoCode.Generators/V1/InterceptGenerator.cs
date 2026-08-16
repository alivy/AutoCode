using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AutoCode.Intercept
{
    /// <summary>
    /// 通用方法拦截器生成器 v2 - 编译时 AOP。
    /// 支持：内置拦截器（Log/Cache/Retry/CircuitBreaker/Metrics/Throttle/Validate/Tracing/Transaction）
    ///       + 自定义拦截器（IInterceptHandler 扩展机制）
    ///       + 方法级精细控制（[SkipIntercept] / [InterceptOverride]）
    ///       + DI 自动注册 + 编译期诊断
    /// </summary>
    [Generator]
    public class InterceptGenerator : IIncrementalGenerator
    {
        // 诊断规则
        private static readonly DiagnosticDescriptor NoInterface = new(
            "AC9001", "[AutoIntercept] 需要接口",
            "类 '{0}' 标记了 [AutoIntercept] 但未实现任何接口，装饰器模式需要接口",
            "AutoCode.Intercept", DiagnosticSeverity.Warning, true);

        private static readonly DiagnosticDescriptor CacheOnVoid = new(
            "AC9002", "Cache 不能用于无返回值方法",
            "方法 '{0}' 返回 void/Task，[AutoIntercept(Cache)] 无法缓存无返回值方法",
            "AutoCode.Intercept", DiagnosticSeverity.Warning, true);

        private static readonly DiagnosticDescriptor CustomHandlerNotImpl = new(
            "AC9003", "自定义拦截器未实现接口",
            "类型 '{0}' 未实现 IInterceptHandler 接口",
            "AutoCode.Intercept", DiagnosticSeverity.Error, true);

        /// <summary>
        /// 开发者感知提示：告知已生成的 Args 类型及其结构，方便编写 Handler。
        /// 在 IDE 错误列表中显示为 Info 级别，编译后即可看到。
        /// </summary>
        private static readonly DiagnosticDescriptor ArgsGeneratedHint = new(
            "AC9100", "已生成强类型 Args",
            "✅ 已生成 '{0}' → 可用于 MethodHandlerBase<{0}, {1}>。参数: ({2})",
            "AutoCode.Intercept", DiagnosticSeverity.Info, true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 模式 A：类级别 [AutoIntercept]
            // 模式 B：方法级别 [AutoIntercept] / [CustomIntercept]（类上无标记）
            var sources = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cds &&
                        HasInterceptAttributes(cds),
                    transform: static (ctx, ct) => ExtractInterceptInfo(ctx, ct))
                .Where(static s => s != null);

            context.RegisterSourceOutput(sources, static (spc, info) =>
            {
                // 诊断
                foreach (var diag in info!.Diagnostics)
                    spc.ReportDiagnostic(diag);

                // 生成强类型 Args record（每个被拦截的方法一个）
                var argsOutput = GenerateArgsRecords(info);
                if (argsOutput != null)
                    spc.AddSource(argsOutput.Value.FileName, SourceText.From(argsOutput.Value.Content, Encoding.UTF8));

                // 生成拦截装饰器
                var output = GenerateInterceptedClass(info);
                if (output != null)
                    spc.AddSource(output.Value.FileName, SourceText.From(output.Value.Content, Encoding.UTF8));

                // 生成 DI 注册
                var diOutput = GenerateDIRegistration(info);
                if (diOutput != null)
                    spc.AddSource(diOutput.Value.FileName, SourceText.From(diOutput.Value.Content, Encoding.UTF8));
            });
        }

        /// <summary>
        /// 检测类或类中的方法是否有拦截相关特性
        /// </summary>
        private static bool HasInterceptAttributes(ClassDeclarationSyntax cds)
        {
            // 类级别 [AutoIntercept]
            if (cds.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
            {
                var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                return name == "AutoIntercept" || name == "AutoInterceptAttribute";
            }))
                return true;

            // 方法级别 [AutoIntercept] 或 [CustomIntercept]
            return cds.Members.OfType<MethodDeclarationSyntax>().Any(m =>
                m.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
                {
                    var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                    return name == "AutoIntercept" || name == "AutoInterceptAttribute"
                        || name == "CustomIntercept" || name == "CustomInterceptAttribute";
                }));
        }

        #region 信息提取

        private static InterceptInfo? ExtractInterceptInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.Node is not ClassDeclarationSyntax classDecl)
                return null;

            var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
            if (classSymbol == null) return null;

            var diagnostics = new List<Diagnostic>();

            // ═══ 判断模式：类级别 vs 方法级别 ═══
            var classAttr = classSymbol.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name == "AutoInterceptAttribute" ||
                a.AttributeClass?.Name == "AutoIntercept");

            bool isMethodLevelMode = classAttr == null;

            // 获取服务接口（可能由其他生成器产出，语义模型中可能未解析）
            var serviceInterface = classSymbol.Interfaces
                .FirstOrDefault(i => i.Name != "IScoped" && i.Name != "ISingleton"
                    && i.Name != "ITransient" && i.Name != "IDependencyBase");

            // 接口未解析时（由其他生成器产出），从语法树 base list 推断接口名
            string interfaceName;
            string interfaceShortName;
            if (serviceInterface != null)
            {
                interfaceName = serviceInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                interfaceShortName = serviceInterface.Name;
            }
            else
            {
                // 从 base list 语法中找 I 开头的标识符
                var baseNames = classDecl.BaseList?.Types
                    .Select(t => t.Type is IdentifierNameSyntax id ? id.Identifier.Text : t.Type.ToString())
                    .Where(n => n.StartsWith("I") && n.Length > 1 && char.IsUpper(n[1]))
                    .Where(n => n != "IScoped" && n != "ISingleton" && n != "ITransient" && n != "IDependencyBase")
                    .ToList() ?? new List<string>();

                if (baseNames.Count > 0)
                {
                    interfaceShortName = baseNames[0];
                    var ns = classSymbol.ContainingNamespace?.ToDisplayString() ?? "";
                    interfaceName = string.IsNullOrEmpty(ns) ? interfaceShortName : $"global::{ns}.{interfaceShortName}";
                }
                else if (!isMethodLevelMode)
                {
                    // 类级别模式必须有接口
                    diagnostics.Add(Diagnostic.Create(NoInterface, classDecl.Identifier.GetLocation(), classSymbol.Name));
                    return new InterceptInfo { Diagnostics = diagnostics };
                }
                else
                {
                    // 方法级模式：用类名推断接口
                    interfaceShortName = $"I{classSymbol.Name}";
                    var ns = classSymbol.ContainingNamespace?.ToDisplayString() ?? "";
                    interfaceName = string.IsNullOrEmpty(ns) ? interfaceShortName : $"global::{ns}.{interfaceShortName}";
                }
            }

            // 解析类级别拦截器类型（方法级模式下默认 None）
            var interceptors = InterceptFlags.None;
            var info = new InterceptInfo
            {
                Namespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                ClassName = classSymbol.Name,
                InterfaceName = interfaceName,
                InterfaceShortName = interfaceShortName,
                Interceptors = interceptors,
                Diagnostics = diagnostics,
                IsMethodLevelMode = isMethodLevelMode
            };

            if (classAttr != null)
            {
                // ═══ 模式 A：类级别 ═══
                if (classAttr.ConstructorArguments.Length > 0 && classAttr.ConstructorArguments[0].Value is int flagVal)
                    interceptors = (InterceptFlags)flagVal;
                else
                    interceptors = InterceptFlags.Log;

                info.Interceptors = interceptors;

                foreach (var named in classAttr.NamedArguments)
                {
                    switch (named.Key)
                    {
                        case "LogParameters": info.LogParameters = named.Value.Value is bool b1 && b1; break;
                        case "LogResult": info.LogResult = named.Value.Value is bool b2 && b2; break;
                        case "CacheDurationSeconds": info.CacheDurationSeconds = named.Value.Value is int i1 ? i1 : 300; break;
                        case "CacheKeyPrefix": info.CacheKeyPrefix = named.Value.Value as string; break;
                        case "MaxRetryCount": info.MaxRetryCount = named.Value.Value is int i2 ? i2 : 3; break;
                        case "RetryBaseDelayMs": info.RetryBaseDelayMs = named.Value.Value is int i3 ? i3 : 100; break;
                        case "CircuitFailureThreshold": info.CircuitFailureThreshold = named.Value.Value is int i4 ? i4 : 5; break;
                        case "CircuitBreakDurationSeconds": info.CircuitBreakDurationSeconds = named.Value.Value is int i5 ? i5 : 30; break;
                        case "MaxRequestsPerSecond": info.MaxRequestsPerSecond = named.Value.Value is int i6 ? i6 : 100; break;
                    }
                }
            }

            // 解析类级别 [CustomIntercept]
            var classCustomAttrs = classSymbol.GetAttributes()
                .Where(a => a.AttributeClass?.Name == "CustomInterceptAttribute" || a.AttributeClass?.Name == "CustomIntercept")
                .ToList();

            foreach (var ca in classCustomAttrs)
            {
                ParseCustomHandler(ca, info, diagnostics, classDecl);
            }
            info.CustomHandlers = info.CustomHandlers.OrderBy(h => h.Order).ToList();

            // ═══ 提取方法 ═══
            var excludeMethods = new HashSet<string>();
            var methodOverrides = new Dictionary<string, InterceptFlags>();
            var methodCustomHandlers = new Dictionary<string, List<CustomHandlerInfo>>();

            // 优先从接口语义获取方法；接口未解析时从类本身的 public 方法提取
            List<IMethodSymbol> methods;
            if (serviceInterface != null)
            {
                methods = serviceInterface.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.MethodKind == MethodKind.Ordinary)
                    .ToList();
            }
            else
            {
                methods = classSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.MethodKind == MethodKind.Ordinary
                        && m.DeclaredAccessibility == Accessibility.Public
                        && !m.IsStatic)
                    .ToList();
            }

            // 从实现类中读取方法级特性
            foreach (var member in classSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                var attrs = member.GetAttributes();

                // [SkipIntercept]
                if (attrs.Any(a => a.AttributeClass?.Name == "SkipInterceptAttribute" || a.AttributeClass?.Name == "SkipIntercept"))
                    excludeMethods.Add(member.Name);

                // [InterceptOverride]
                var overrideAttr = attrs.FirstOrDefault(a =>
                    a.AttributeClass?.Name == "InterceptOverrideAttribute" || a.AttributeClass?.Name == "InterceptOverride");
                if (overrideAttr != null && overrideAttr.ConstructorArguments.Length > 0 &&
                    overrideAttr.ConstructorArguments[0].Value is int ovFlags)
                {
                    methodOverrides[member.Name] = (InterceptFlags)ovFlags;
                }

                // ═══ 模式 B：方法级 [AutoIntercept] ═══
                var methodInterceptAttr = attrs.FirstOrDefault(a =>
                    a.AttributeClass?.Name == "AutoInterceptAttribute" || a.AttributeClass?.Name == "AutoIntercept");
                if (methodInterceptAttr != null && isMethodLevelMode)
                {
                    var mFlags = InterceptFlags.Log;
                    if (methodInterceptAttr.ConstructorArguments.Length > 0 &&
                        methodInterceptAttr.ConstructorArguments[0].Value is int mf)
                        mFlags = (InterceptFlags)mf;
                    methodOverrides[member.Name] = mFlags;
                }

                // ═══ 模式 B：方法级 [CustomIntercept] ═══
                var methodCustomAttrs = attrs
                    .Where(a => a.AttributeClass?.Name == "CustomInterceptAttribute" || a.AttributeClass?.Name == "CustomIntercept")
                    .ToList();
                if (methodCustomAttrs.Count > 0)
                {
                    var handlers = new List<CustomHandlerInfo>();
                    var tempInfo = new InterceptInfo();
                    foreach (var mca in methodCustomAttrs)
                        ParseCustomHandler(mca, tempInfo, diagnostics, classDecl);
                    methodCustomHandlers[member.Name] = tempInfo.CustomHandlers.OrderBy(h => h.Order).ToList();

                    // 方法级模式下，有 [CustomIntercept] 但没有 [AutoIntercept] 的方法也需要拦截
                    if (isMethodLevelMode && !methodOverrides.ContainsKey(member.Name))
                        methodOverrides[member.Name] = InterceptFlags.None; // 仅自定义拦截器
                }
            }

            foreach (var m in methods)
            {
                if (excludeMethods.Contains(m.Name)) continue;

                // 方法级模式：只拦截有标记的方法，其余透传
                InterceptFlags methodFlags;
                bool isPassthrough = false;
                if (isMethodLevelMode)
                {
                    if (!methodOverrides.ContainsKey(m.Name) && !methodCustomHandlers.ContainsKey(m.Name))
                    {
                        // 无标记 → 透传（仍需实现接口成员）
                        isPassthrough = true;
                        methodFlags = InterceptFlags.None;
                    }
                    else
                    {
                        methodFlags = methodOverrides.TryGetValue(m.Name, out var mf) ? mf : InterceptFlags.None;
                    }
                }
                else
                {
                    // 类级模式：所有方法默认继承类配置
                    methodFlags = methodOverrides.TryGetValue(m.Name, out var ov) ? ov : interceptors;
                }

                // 诊断：Cache 不能用于 void
                if (methodFlags.HasFlag(InterceptFlags.Cache) &&
                    (m.ReturnType.SpecialType == SpecialType.System_Void ||
                     m.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task"))
                {
                    diagnostics.Add(Diagnostic.Create(CacheOnVoid, classDecl.Identifier.GetLocation(), m.Name));
                    methodFlags &= ~InterceptFlags.Cache;
                }

                // 方法级自定义拦截器
                var methodHandlers = methodCustomHandlers.TryGetValue(m.Name, out var mh) ? mh : null;

                // ═══ AC9100：开发者感知提示 ═══
                // 当方法有 [CustomIntercept] 时，提示已生成的 Args 类型及其结构
                if (methodHandlers != null && methodHandlers.Count > 0 && !isPassthrough)
                {
                    var argsName = $"{m.Name}Args";
                    var paramDesc = string.Join(", ", m.Parameters.Select(p =>
                        $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {char.ToUpper(p.Name[0])}{p.Name.Substring(1)}"));
                    var returnTypeShort = m.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    // 异步方法提取内部类型
                    if (IsAsyncReturn(m.ReturnType))
                    {
                        var inner = ExtractAsyncInnerType(m.ReturnType.ToDisplayString());
                        returnTypeShort = inner.Contains(".") ? inner.Substring(inner.LastIndexOf('.') + 1) : inner;
                    }
                    if (paramDesc.Length == 0) paramDesc = "无参数";

                    var location = m.Locations.FirstOrDefault() ?? classDecl.Identifier.GetLocation();
                    diagnostics.Add(Diagnostic.Create(ArgsGeneratedHint, location,
                        argsName, returnTypeShort, paramDesc));
                }

                // Nullable 感知的类型显示格式（保留 string?/OrderInfo? 等可空注解，消除生成代码 CS8603）
                var nullableFormat = new SymbolDisplayFormat(
                    globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                    miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                        | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                        | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

                info.Methods.Add(new InterceptMethodInfo
                {
                    Name = m.Name,
                    ReturnType = m.ReturnType.ToDisplayString(nullableFormat),
                    IsAsync = IsAsyncReturn(m.ReturnType),
                    IsVoid = m.ReturnType.SpecialType == SpecialType.System_Void,
                    IsTaskNoResult = m.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task",
                    Flags = methodFlags,
                    IsPassthrough = isPassthrough,
                    CustomHandlers = methodHandlers,
                    Parameters = m.Parameters.Select(p => new ParamInfo
                    {
                        Name = p.Name,
                        Type = p.Type.ToDisplayString(nullableFormat),
                        IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated
                    }).ToList()
                });
            }

            // 如果没有任何方法需要拦截，跳过
            if (info.Methods.Count == 0 && !isMethodLevelMode)
                return null;

            return info;
        }

        /// <summary>解析 [CustomIntercept] 特性数据</summary>
        private static void ParseCustomHandler(AttributeData ca, InterceptInfo info, List<Diagnostic> diagnostics, ClassDeclarationSyntax classDecl)
        {
            if (ca.ConstructorArguments.Length > 0 && ca.ConstructorArguments[0].Value is INamedTypeSymbol handlerType)
            {
                var implementsHandler = handlerType.AllInterfaces.Any(i =>
                    i.Name == "IInterceptHandler" || i.Name == "IMethodHandler");
                if (!implementsHandler)
                {
                    diagnostics.Add(Diagnostic.Create(CustomHandlerNotImpl, classDecl.Identifier.GetLocation(), handlerType.Name));
                    return;
                }

                // 检测是强类型 IMethodHandler<,> 还是通用 IInterceptHandler
                bool isMethodHandler = handlerType.AllInterfaces.Any(i => i.Name == "IMethodHandler");

                int order = 100;
                foreach (var na in ca.NamedArguments)
                {
                    if (na.Key == "Order" && na.Value.Value is int ov) order = ov;
                }

                info.CustomHandlers.Add(new CustomHandlerInfo
                {
                    TypeName = handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ShortName = handlerType.Name,
                    Order = order,
                    IsMethodHandler = isMethodHandler
                });
            }
        }

        private static bool IsAsyncReturn(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol named) return false;
            var full = named.OriginalDefinition.ToDisplayString();
            return full.StartsWith("System.Threading.Tasks.Task")
                || full.StartsWith("System.Threading.Tasks.ValueTask");
        }

        #endregion

        #region 代码生成

        private static (string FileName, string Content)? GenerateInterceptedClass(InterceptInfo info)
        {
            if (info.Methods.Count == 0) return null;

            var decoratorName = $"Intercepted{info.ClassName}";
            var sb = new StringBuilder();
            // 合并所有方法的 flags（方法级模式下 classFlags=None，但方法可能有 Log/Cache/Metrics）
            var classFlags = info.Interceptors;
            foreach (var m in info.Methods)
                classFlags |= m.Flags;

            // 收集所有唯一的自定义拦截器（类级 + 方法级）
            var allHandlers = new List<CustomHandlerInfo>(info.CustomHandlers);
            foreach (var m in info.Methods)
            {
                if (m.CustomHandlers != null)
                {
                    foreach (var h in m.CustomHandlers)
                    {
                        if (!allHandlers.Any(x => x.TypeName == h.TypeName))
                            allHandlers.Add(h);
                    }
                }
            }
            bool hasCustom = allHandlers.Count > 0;

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// 由 AutoCode.Intercept v2 自动生成 - 编译时 AOP 拦截器");
            sb.AppendLine("// 内置拦截 + 自定义拦截器管线，零反射、NativeAOT 兼容");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Diagnostics;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using System.Threading.Tasks;");
            if (classFlags.HasFlag(InterceptFlags.Log))
                sb.AppendLine("using Microsoft.Extensions.Logging;");
            if (classFlags.HasFlag(InterceptFlags.Cache))
                sb.AppendLine("using Microsoft.Extensions.Caching.Memory;");
            if (classFlags.HasFlag(InterceptFlags.Metrics))
                sb.AppendLine("using System.Diagnostics.Metrics;");
            if (classFlags.HasFlag(InterceptFlags.Tracing))
                sb.AppendLine("using System.Diagnostics;");
            if (hasCustom)
                sb.AppendLine("using AutoCode.Model;");
            sb.AppendLine();

            var hasNs = !string.IsNullOrEmpty(info.Namespace);
            if (hasNs) { sb.AppendLine($"namespace {info.Namespace}"); sb.AppendLine("{"); }
            var ind = hasNs ? "    " : "";

            // 类声明
            sb.AppendLine($"{ind}/// <summary>");
            sb.AppendLine($"{ind}/// {info.ClassName} 的拦截装饰器（编译时 AOP v2）");
            sb.AppendLine($"{ind}/// 管线: {DescribeInterceptors(classFlags)}{(hasCustom ? " + Custom[" + string.Join(",", info.CustomHandlers.Select(h => h.ShortName)) + "]" : "")}");
            sb.AppendLine($"{ind}/// </summary>");
            sb.AppendLine($"{ind}public sealed class {decoratorName} : {info.InterfaceName}");
            sb.AppendLine($"{ind}{{");

            // 字段
            sb.AppendLine($"{ind}    private readonly {info.InterfaceName} _inner;");
            if (classFlags.HasFlag(InterceptFlags.Log))
                sb.AppendLine($"{ind}    private readonly ILogger<{decoratorName}> _logger;");
            if (classFlags.HasFlag(InterceptFlags.Cache))
                sb.AppendLine($"{ind}    private readonly IMemoryCache _cache;");
            if (classFlags.HasFlag(InterceptFlags.Metrics))
            {
                sb.AppendLine($"{ind}    private static readonly Meter _meter = new(\"{info.ClassName}\");");
                sb.AppendLine($"{ind}    private static readonly Histogram<double> _duration = _meter.CreateHistogram<double>(\"method_duration_ms\");");
                sb.AppendLine($"{ind}    private static readonly Counter<long> _successCount = _meter.CreateCounter<long>(\"method_success\");");
                sb.AppendLine($"{ind}    private static readonly Counter<long> _errorCount = _meter.CreateCounter<long>(\"method_error\");");
            }
            if (classFlags.HasFlag(InterceptFlags.Tracing))
                sb.AppendLine($"{ind}    private static readonly ActivitySource _activitySource = new(\"{info.ClassName}\");");
            if (classFlags.HasFlag(InterceptFlags.CircuitBreaker))
            {
                sb.AppendLine($"{ind}    private static int _consecutiveFailures;");
                sb.AppendLine($"{ind}    private static DateTime _circuitOpenUntil = DateTime.MinValue;");
                sb.AppendLine($"{ind}    private static readonly object _circuitLock = new object();");
            }
            if (classFlags.HasFlag(InterceptFlags.Throttle))
                sb.AppendLine($"{ind}    private static readonly SemaphoreSlim _throttle = new({info.MaxRequestsPerSecond}, {info.MaxRequestsPerSecond});");

            // 自定义拦截器字段
            foreach (var handler in allHandlers)
            {
                var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                sb.AppendLine($"{ind}    private readonly {handler.TypeName} {fieldName};");
            }
            sb.AppendLine();

            // 构造函数
            var ctorParams = new List<string> { $"{info.InterfaceName} inner" };
            var ctorAssigns = new List<string> { "_inner = inner;" };
            if (classFlags.HasFlag(InterceptFlags.Log))
            { ctorParams.Add($"ILogger<{decoratorName}> logger"); ctorAssigns.Add("_logger = logger;"); }
            if (classFlags.HasFlag(InterceptFlags.Cache))
            { ctorParams.Add("IMemoryCache cache"); ctorAssigns.Add("_cache = cache;"); }
            foreach (var handler in allHandlers)
            {
                var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                ctorParams.Add($"{handler.TypeName} {fieldName.TrimStart('_')}");
                ctorAssigns.Add($"{fieldName} = {fieldName.TrimStart('_')};");
            }

            sb.AppendLine($"{ind}    public {decoratorName}({string.Join(", ", ctorParams)})");
            sb.AppendLine($"{ind}    {{");
            foreach (var assign in ctorAssigns)
                sb.AppendLine($"{ind}        {assign}");
            sb.AppendLine($"{ind}    }}");

            // 生成每个方法
            foreach (var method in info.Methods)
            {
                sb.AppendLine();
                GenerateMethod(sb, ind, info, method);
            }

            sb.AppendLine($"{ind}}}");
            if (hasNs) sb.AppendLine("}");

            return ($"{decoratorName}.g.cs", sb.ToString());
        }

        private static void GenerateMethod(StringBuilder sb, string ind, InterceptInfo info, InterceptMethodInfo method)
        {
            var flags = method.Flags;
            var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var args = string.Join(", ", method.Parameters.Select(p => p.Name));
            var returnType = method.ReturnType;

            // 透传方法：无拦截，直接委托给 _inner
            if (method.IsPassthrough)
            {
                sb.AppendLine($"{ind}    public {(method.IsAsync ? "async " : "")}{returnType} {method.Name}({parameters})");
                if (method.IsVoid)
                    sb.AppendLine($"{ind}        => _inner.{method.Name}({args});");
                else if (method.IsAsync)
                    sb.AppendLine($"{ind}        => _inner.{method.Name}({args});");
                else
                    sb.AppendLine($"{ind}        => _inner.{method.Name}({args});");
                return;
            }

            // 方法级 handler 优先，否则回退到类级
            var effectiveHandlers = method.CustomHandlers ?? info.CustomHandlers;
            bool hasCustom = effectiveHandlers.Count > 0;
            bool hasMethodHandlers = effectiveHandlers.Any(h => h.IsMethodHandler);
            bool hasLegacyHandlers = effectiveHandlers.Any(h => !h.IsMethodHandler);

            sb.AppendLine($"{ind}    public {(method.IsAsync ? "async " : "")}{returnType} {method.Name}({parameters})");
            sb.AppendLine($"{ind}    {{");
            var b = $"{ind}        ";

            // ─── 构造拦截上下文 ───
            if (hasCustom)
            {
                if (hasMethodHandlers)
                {
                    var argsName = $"{method.Name}Args";
                    var argsCtor = string.Join(", ", method.Parameters.Select(p => p.Name));
                    sb.AppendLine($"{b}var __args = new {argsName}({argsCtor});");
                    sb.AppendLine($"{b}var __mctx = new MethodContext {{ ClassName = \"{info.ClassName}\", MethodName = \"{method.Name}\" }};");
                }
                if (hasLegacyHandlers)
                {
                    sb.AppendLine($"{b}var __ctx = new InterceptContext {{ ClassName = \"{info.ClassName}\", MethodName = \"{method.Name}\" }};");
                }
                sb.AppendLine();
            }

            // ─── Validate - Before ───
            if (flags.HasFlag(InterceptFlags.Validate))
            {
                sb.AppendLine($"{b}// ─── 参数校验 ───");
                foreach (var p in method.Parameters)
                {
                    if (p.Type == "string" || p.Type == "global::System.String")
                        sb.AppendLine($"{b}if (string.IsNullOrWhiteSpace({p.Name})) throw new ArgumentException(\"参数 {p.Name} 不能为空\", nameof({p.Name}));");
                    else if (IsReferenceType(p.Type) && !p.IsNullable)
                        sb.AppendLine($"{b}if ({p.Name} is null) throw new ArgumentNullException(nameof({p.Name}));");
                }
                sb.AppendLine();
            }

            // ─── 自定义拦截器 OnBefore ───
            if (hasCustom)
            {
                sb.AppendLine($"{b}// ─── 自定义拦截器 OnBefore ───");
                foreach (var handler in effectiveHandlers)
                {
                    var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                    if (handler.IsMethodHandler)
                        sb.AppendLine($"{b}{fieldName}.OnBefore(__args, __mctx);");
                    else
                        sb.AppendLine($"{b}{fieldName}.OnBefore(__ctx);");
                }
                // 短路检查（两种上下文都检查）
                if (hasMethodHandlers)
                {
                    sb.AppendLine($"{b}if (__mctx.ShortCircuit)");
                    if (!method.IsVoid && !method.IsTaskNoResult)
                    {
                        if (method.IsAsync)
                            sb.AppendLine($"{b}    return ({ExtractAsyncInnerType(returnType)})__mctx.Result!;");
                        else
                            sb.AppendLine($"{b}    return ({returnType})__mctx.Result!;");
                    }
                    else
                    {
                        sb.AppendLine($"{b}    return;");
                    }
                }
                if (hasLegacyHandlers)
                {
                    sb.AppendLine($"{b}if (__ctx.ShortCircuit)");
                    if (!method.IsVoid && !method.IsTaskNoResult)
                    {
                        if (method.IsAsync)
                            sb.AppendLine($"{b}    return ({ExtractAsyncInnerType(returnType)})__ctx.Result!;");
                        else
                            sb.AppendLine($"{b}    return ({returnType})__ctx.Result!;");
                    }
                    else
                    {
                        sb.AppendLine($"{b}    return;");
                    }
                }
                sb.AppendLine();
            }

            // ─── Throttle - Before ───
            if (flags.HasFlag(InterceptFlags.Throttle))
            {
                sb.AppendLine($"{b}// ─── 限流 ───");
                sb.AppendLine($"{b}{(method.IsAsync ? "await _throttle.WaitAsync();" : "_throttle.Wait();")}");
                sb.AppendLine($"{b}try");
                sb.AppendLine($"{b}{{");
                b = $"{ind}            ";
            }

            // ─── Cache - Before ───
            if (flags.HasFlag(InterceptFlags.Cache) && !method.IsVoid && !method.IsTaskNoResult)
            {
                var cacheKey = info.CacheKeyPrefix ?? $"{info.ClassName}.{method.Name}";
                var innerType = method.IsAsync ? ExtractAsyncInnerType(returnType) : returnType;
                // C# 模式匹配不允许可空注解类型（OrderInfo?），is 表达式需使用基础类型（消除 CS8116）
                var patternType = innerType.TrimEnd('?');
                sb.AppendLine($"{b}// ─── 缓存（Before: 命中短路）───");
                sb.AppendLine($"{b}var __cacheKey = $\"{cacheKey}:{string.Join(":", method.Parameters.Select(p => $"{{{p.Name}}}"))}\";");
                sb.AppendLine($"{b}if (_cache.TryGetValue(__cacheKey, out object? __cachedObj) && __cachedObj is {patternType} __cached)");
                sb.AppendLine($"{b}    return __cached;");
                sb.AppendLine();
            }

            // ─── CircuitBreaker - Before ───
            if (flags.HasFlag(InterceptFlags.CircuitBreaker))
            {
                sb.AppendLine($"{b}// ─── 熔断检查 ───");
                sb.AppendLine($"{b}if (DateTime.UtcNow < _circuitOpenUntil)");
                sb.AppendLine($"{b}    throw new InvalidOperationException(\"[CircuitBreaker] 熔断器已打开，请稍后重试\");");
                sb.AppendLine();
            }

            // ─── Tracing - Before ───
            if (flags.HasFlag(InterceptFlags.Tracing))
            {
                sb.AppendLine($"{b}// ─── 链路追踪 ───");
                sb.AppendLine($"{b}using var __activity = _activitySource.StartActivity(\"{info.ClassName}.{method.Name}\");");
                foreach (var p in method.Parameters)
                    sb.AppendLine($"{b}__activity?.SetTag(\"{p.Name}\", {p.Name}?.ToString());");
                sb.AppendLine();
            }

            // ─── Log - Before ───
            if (flags.HasFlag(InterceptFlags.Log))
            {
                var paramLog = info.LogParameters && method.Parameters.Count > 0
                    ? ", " + string.Join(", ", method.Parameters.Select(p => $"{p.Name}={{{p.Name}}}"))
                    : "";
                var paramArgs = info.LogParameters && method.Parameters.Count > 0
                    ? ", " + string.Join(", ", method.Parameters.Select(p => p.Name))
                    : "";
                sb.AppendLine($"{b}_logger.LogInformation(\"{method.Name} 开始{paramLog}\"{paramArgs});");
            }

            // ─── 计时 ───
            if (flags.HasFlag(InterceptFlags.Log) || flags.HasFlag(InterceptFlags.Metrics) || flags.HasFlag(InterceptFlags.Tracing) || hasCustom)
                sb.AppendLine($"{b}var __sw = Stopwatch.StartNew();");

            // ─── Retry 包裹 ───
            bool hasRetry = flags.HasFlag(InterceptFlags.Retry);
            if (hasRetry)
            {
                sb.AppendLine();
                sb.AppendLine($"{b}// ─── 重试（指数退避）───");
                sb.AppendLine($"{b}for (int __attempt = 1; ; __attempt++)");
                sb.AppendLine($"{b}{{");
                sb.AppendLine($"{b}    try");
                sb.AppendLine($"{b}    {{");
                b = $"{ind}                ";
            }

            // ─── try-catch ───
            bool needTryCatch = (flags.HasFlag(InterceptFlags.Log) || flags.HasFlag(InterceptFlags.CircuitBreaker)
                || flags.HasFlag(InterceptFlags.Metrics) || flags.HasFlag(InterceptFlags.Tracing) || hasCustom) && !hasRetry;
            if (needTryCatch)
            {
                sb.AppendLine($"{b}try");
                sb.AppendLine($"{b}{{");
                b += "    ";
            }

            // ─── 实际方法调用 + After ───
            GenerateMethodInvocation(sb, b, method, args, flags, info);

            // ─── catch ───
            if (needTryCatch)
            {
                b = b.Substring(0, b.Length - 4);
                sb.AppendLine($"{b}}}");
                sb.AppendLine($"{b}catch (Exception __ex)");
                sb.AppendLine($"{b}{{");
                GenerateExceptionHandling(sb, $"{b}    ", method, flags, info);
                sb.AppendLine($"{b}    throw;");
                sb.AppendLine($"{b}}}");
            }

            // ─── Retry catch ───
            if (hasRetry)
            {
                sb.AppendLine($"{ind}            }}");
                sb.AppendLine($"{ind}            catch (Exception __ex) when (__attempt < {info.MaxRetryCount})");
                sb.AppendLine($"{ind}            {{");
                if (flags.HasFlag(InterceptFlags.Log))
                    sb.AppendLine($"{ind}                _logger.LogWarning(__ex, \"{method.Name} 第{{Attempt}}次失败，准备重试\", __attempt);");
                if (effectiveHandlers.Count > 0)
                {
                    foreach (var handler in effectiveHandlers)
                    {
                        var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                        if (handler.IsMethodHandler)
                        {
                            sb.AppendLine($"{ind}                __mctx.Elapsed = __sw.Elapsed;");
                            sb.AppendLine($"{ind}                __mctx.AttemptNumber = __attempt;");
                            sb.AppendLine($"{ind}                {fieldName}.OnException(__args, __ex, __mctx);");
                        }
                        else
                        {
                            sb.AppendLine($"{ind}                __ctx.Elapsed = __sw.Elapsed;");
                            sb.AppendLine($"{ind}                __ctx.AttemptNumber = __attempt;");
                            sb.AppendLine($"{ind}                {fieldName}.OnException(__ctx, __ex);");
                        }
                    }
                }
                sb.AppendLine($"{ind}                {(method.IsAsync ? $"await Task.Delay(__attempt * {info.RetryBaseDelayMs});" : $"Thread.Sleep(__attempt * {info.RetryBaseDelayMs});")}");
                sb.AppendLine($"{ind}            }}");
                sb.AppendLine($"{ind}        }}");
            }

            // ─── Throttle finally ───
            if (flags.HasFlag(InterceptFlags.Throttle))
            {
                sb.AppendLine($"{ind}        }}");
                sb.AppendLine($"{ind}        finally {{ _throttle.Release(); }}");
            }

            sb.AppendLine($"{ind}    }}");
        }

        private static void GenerateMethodInvocation(StringBuilder sb, string b, InterceptMethodInfo method, string args, InterceptFlags flags, InterceptInfo info)
        {
            var hasResult = !method.IsVoid && !method.IsTaskNoResult;
            var effectiveHandlers = method.CustomHandlers ?? info.CustomHandlers;

            if (method.IsAsync && hasResult)
            {
                sb.AppendLine($"{b}var __result = await _inner.{method.Name}({args});");
                GenerateAfterIntercepts(sb, b, method, flags, info, effectiveHandlers, "__result");
                sb.AppendLine($"{b}return __result;");
            }
            else if (method.IsAsync)
            {
                sb.AppendLine($"{b}await _inner.{method.Name}({args});");
                GenerateAfterIntercepts(sb, b, method, flags, info, effectiveHandlers, null);
            }
            else if (method.IsVoid)
            {
                sb.AppendLine($"{b}_inner.{method.Name}({args});");
                GenerateAfterIntercepts(sb, b, method, flags, info, effectiveHandlers, null);
            }
            else
            {
                sb.AppendLine($"{b}var __result = _inner.{method.Name}({args});");
                GenerateAfterIntercepts(sb, b, method, flags, info, effectiveHandlers, "__result");
                sb.AppendLine($"{b}return __result;");
            }
        }

        private static void GenerateAfterIntercepts(StringBuilder sb, string b, InterceptMethodInfo method, InterceptFlags flags, InterceptInfo info, List<CustomHandlerInfo> handlers, string? resultVar)
        {
            // Log - After
            if (flags.HasFlag(InterceptFlags.Log))
                sb.AppendLine($"{b}_logger.LogInformation(\"{method.Name} 完成, 耗时 {{Elapsed}}ms\", __sw.ElapsedMilliseconds);");

            // Metrics - After
            if (flags.HasFlag(InterceptFlags.Metrics))
            {
                sb.AppendLine($"{b}_duration.Record(__sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>(\"method\", \"{method.Name}\"));");
                sb.AppendLine($"{b}_successCount.Add(1, new KeyValuePair<string, object?>(\"method\", \"{method.Name}\"));");
            }

            // Tracing - After
            if (flags.HasFlag(InterceptFlags.Tracing))
                sb.AppendLine($"{b}__activity?.SetTag(\"elapsed_ms\", __sw.ElapsedMilliseconds);");

            // CircuitBreaker - 成功重置
            if (flags.HasFlag(InterceptFlags.CircuitBreaker))
                sb.AppendLine($"{b}if (_consecutiveFailures > 0) Interlocked.Exchange(ref _consecutiveFailures, 0);");

            // Cache - After
            if (flags.HasFlag(InterceptFlags.Cache) && resultVar != null)
                sb.AppendLine($"{b}_cache.Set(__cacheKey, {resultVar}, TimeSpan.FromSeconds({info.CacheDurationSeconds}));");

            // 自定义拦截器 OnAfter
            if (handlers.Count > 0)
            {
                foreach (var handler in handlers)
                {
                    var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                    if (handler.IsMethodHandler)
                    {
                        sb.AppendLine($"{b}__mctx.Elapsed = __sw.Elapsed;");
                        sb.AppendLine($"{b}{fieldName}.OnAfter(__args, {(resultVar ?? "default!")}, __mctx);");
                    }
                    else
                    {
                        sb.AppendLine($"{b}__ctx.Elapsed = __sw.Elapsed;");
                        sb.AppendLine($"{b}{fieldName}.OnAfter(__ctx, {(resultVar ?? "null")});");
                    }
                }
            }
        }

        private static void GenerateExceptionHandling(StringBuilder sb, string b, InterceptMethodInfo method, InterceptFlags flags, InterceptInfo info)
        {
            var effectiveHandlers = method.CustomHandlers ?? info.CustomHandlers;

            if (flags.HasFlag(InterceptFlags.Log))
                sb.AppendLine($"{b}_logger.LogError(__ex, \"{method.Name} 异常, 耗时 {{Elapsed}}ms\", __sw.ElapsedMilliseconds);");
            if (flags.HasFlag(InterceptFlags.Metrics))
                sb.AppendLine($"{b}_errorCount.Add(1, new KeyValuePair<string, object?>(\"method\", \"{method.Name}\"));");
            if (flags.HasFlag(InterceptFlags.Tracing))
            {
                sb.AppendLine($"{b}__activity?.SetStatus(ActivityStatusCode.Error, __ex.Message);");
                sb.AppendLine($"{b}__activity?.RecordException(__ex);");
            }
            if (flags.HasFlag(InterceptFlags.CircuitBreaker))
            {
                sb.AppendLine($"{b}lock (_circuitLock)");
                sb.AppendLine($"{b}{{");
                sb.AppendLine($"{b}    if (++_consecutiveFailures >= {info.CircuitFailureThreshold})");
                sb.AppendLine($"{b}        _circuitOpenUntil = DateTime.UtcNow.AddSeconds({info.CircuitBreakDurationSeconds});");
                sb.AppendLine($"{b}}}");
            }
            // 自定义拦截器 OnException
            if (effectiveHandlers.Count > 0)
            {
                foreach (var handler in effectiveHandlers)
                {
                    var fieldName = $"_{char.ToLower(handler.ShortName[0])}{handler.ShortName.Substring(1)}";
                    if (handler.IsMethodHandler)
                    {
                        sb.AppendLine($"{b}__mctx.Elapsed = __sw.Elapsed;");
                        sb.AppendLine($"{b}{fieldName}.OnException(__args, __ex, __mctx);");
                    }
                    else
                    {
                        sb.AppendLine($"{b}__ctx.Elapsed = __sw.Elapsed;");
                        sb.AppendLine($"{b}{fieldName}.OnException(__ctx, __ex);");
                    }
                }
            }
        }

        #endregion

        #region DI 注册生成

        private static (string FileName, string Content)? GenerateDIRegistration(InterceptInfo info)
        {
            if (info.Methods.Count == 0) return null;

            var decoratorName = $"Intercepted{info.ClassName}";

            // 合并所有方法的 flags
            var allFlags = info.Interceptors;
            foreach (var m in info.Methods)
                allFlags |= m.Flags;

            // 收集所有自定义 handler
            var allHandlers = new List<CustomHandlerInfo>(info.CustomHandlers);
            foreach (var m in info.Methods)
            {
                if (m.CustomHandlers != null)
                    foreach (var h in m.CustomHandlers)
                        if (!allHandlers.Any(x => x.TypeName == h.TypeName))
                            allHandlers.Add(h);
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// AutoCode.Intercept - DI 自动注册（装饰器模式）");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();

            var hasNs = !string.IsNullOrEmpty(info.Namespace);
            if (hasNs) { sb.AppendLine($"namespace {info.Namespace}"); sb.AppendLine("{"); }
            var ind = hasNs ? "    " : "";

            sb.AppendLine($"{ind}/// <summary>");
            sb.AppendLine($"{ind}/// {info.ClassName} 拦截装饰器 DI 注册扩展");
            sb.AppendLine($"{ind}/// </summary>");
            sb.AppendLine($"{ind}public static class {decoratorName}Registration");
            sb.AppendLine($"{ind}{{");
            sb.AppendLine($"{ind}    /// <summary>注册 {decoratorName} 装饰器</summary>");
            sb.AppendLine($"{ind}    public static IServiceCollection AddIntercepted{info.ClassName}(this IServiceCollection services)");
            sb.AppendLine($"{ind}    {{");
            sb.AppendLine($"{ind}        services.AddScoped<{info.ClassName}>();");

            // 构造参数列表
            var ctorArgs = new List<string>();
            ctorArgs.Add($"sp.GetRequiredService<{info.ClassName}>()");
            if (allFlags.HasFlag(InterceptFlags.Log))
                ctorArgs.Add($"sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<{decoratorName}>>()");
            if (allFlags.HasFlag(InterceptFlags.Cache))
                ctorArgs.Add("sp.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()!");
            foreach (var handler in allHandlers)
                ctorArgs.Add($"sp.GetRequiredService<{handler.TypeName}>()");

            sb.AppendLine($"{ind}        services.AddScoped<{info.InterfaceName}>(sp => new {decoratorName}(");
            for (int i = 0; i < ctorArgs.Count; i++)
            {
                var comma = i < ctorArgs.Count - 1 ? "," : "));";
                sb.AppendLine($"{ind}            {ctorArgs[i]}{comma}");
            }
            sb.AppendLine($"{ind}        return services;");
            sb.AppendLine($"{ind}    }}");
            sb.AppendLine($"{ind}}}");

            if (hasNs) sb.AppendLine("}");

            return ($"{decoratorName}.DI.g.cs", sb.ToString());
        }

        /// <summary>
        /// 自动生成强类型 Args record。
        /// 每个被拦截的方法生成一个: record {MethodName}Args(ParamType1 Param1, ParamType2 Param2, ...)
        /// 用户的 IMethodHandler&lt;TArgs, TResult&gt; 直接引用这些类型，无需手动定义。
        /// </summary>
        private static (string FileName, string Content)? GenerateArgsRecords(InterceptInfo info)
        {
            if (info.Methods.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// AutoCode.Intercept - 强类型方法参数对象");
            sb.AppendLine("// 每个被拦截的方法自动生成一个 Args record，");
            sb.AppendLine("// 用户的 IMethodHandler<TArgs, TResult> 直接引用，无需手动定义。");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            var hasNs = !string.IsNullOrEmpty(info.Namespace);
            if (hasNs) { sb.AppendLine($"namespace {info.Namespace}"); sb.AppendLine("{"); }
            var ind = hasNs ? "    " : "";

            foreach (var method in info.Methods)
            {
                // 透传方法不需要 Args record
                if (method.IsPassthrough) continue;

                var argsName = $"{method.Name}Args";
                var paramList = string.Join(", ", method.Parameters.Select(p =>
                {
                    // 参数名转 PascalCase（record 属性规范）
                    var propName = char.ToUpper(p.Name[0]) + p.Name.Substring(1);
                    return $"{p.Type} {propName}";
                }));

                sb.AppendLine($"{ind}/// <summary>");
                sb.AppendLine($"{ind}/// {info.ClassName}.{method.Name} 方法的强类型参数。");
                sb.AppendLine($"{ind}/// 由 AutoCode.Intercept 自动生成，用于 IMethodHandler&lt;{argsName}, TResult&gt;。");
                sb.AppendLine($"{ind}/// </summary>");

                if (method.Parameters.Count == 0)
                {
                    // 无参方法生成空 record
                    sb.AppendLine($"{ind}public record {argsName};");
                }
                else
                {
                    sb.AppendLine($"{ind}public record {argsName}({paramList});");
                }
                sb.AppendLine();
            }

            if (hasNs) sb.AppendLine("}");

            return ($"{info.ClassName}.Args.g.cs", sb.ToString());
        }

        #endregion

        #region 辅助方法

        private static string DescribeInterceptors(InterceptFlags flags)
        {
            var parts = new List<string>();
            if (flags.HasFlag(InterceptFlags.Log)) parts.Add("Log");
            if (flags.HasFlag(InterceptFlags.Cache)) parts.Add("Cache");
            if (flags.HasFlag(InterceptFlags.Retry)) parts.Add("Retry");
            if (flags.HasFlag(InterceptFlags.CircuitBreaker)) parts.Add("CircuitBreaker");
            if (flags.HasFlag(InterceptFlags.Validate)) parts.Add("Validate");
            if (flags.HasFlag(InterceptFlags.Authorize)) parts.Add("Authorize");
            if (flags.HasFlag(InterceptFlags.Metrics)) parts.Add("Metrics");
            if (flags.HasFlag(InterceptFlags.Tracing)) parts.Add("Tracing");
            if (flags.HasFlag(InterceptFlags.Transaction)) parts.Add("Transaction");
            if (flags.HasFlag(InterceptFlags.Throttle)) parts.Add("Throttle");
            return string.Join(" → ", parts);
        }

        private static string ExtractAsyncInnerType(string returnType)
        {
            var start = returnType.IndexOf('<');
            var end = returnType.LastIndexOf('>');
            if (start >= 0 && end > start)
                return returnType.Substring(start + 1, end - start - 1);
            return "object";
        }

        /// <summary>判断类型字符串是否为引用类型（排除常见值类型）</summary>
        private static bool IsReferenceType(string type)
        {
            if (type.EndsWith("?")) return true;
            var valueTypes = new[] { "int", "long", "short", "byte", "bool", "decimal", "double", "float", "char",
                "global::System.Int32", "global::System.Int64", "global::System.Int16", "global::System.Byte",
                "global::System.Boolean", "global::System.Decimal", "global::System.Double", "global::System.Single",
                "global::System.Char", "global::System.DateTime", "global::System.Guid", "global::System.TimeSpan" };
            return !valueTypes.Contains(type);
        }

        #endregion
    }

    #region 内部模型

    [Flags]
    internal enum InterceptFlags
    {
        None = 0,
        Log = 1 << 0,
        Cache = 1 << 1,
        Retry = 1 << 2,
        CircuitBreaker = 1 << 3,
        Validate = 1 << 4,
        Authorize = 1 << 5,
        Metrics = 1 << 6,
        Tracing = 1 << 7,
        Transaction = 1 << 8,
        Audit = 1 << 9,
        Throttle = 1 << 10,
        Profiling = 1 << 11
    }

    internal class InterceptInfo
    {
        public string Namespace { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string InterfaceName { get; set; } = "";
        public string InterfaceShortName { get; set; } = "";
        public InterceptFlags Interceptors { get; set; }
        public bool IsMethodLevelMode { get; set; }
        public List<InterceptMethodInfo> Methods { get; set; } = new();
        public List<CustomHandlerInfo> CustomHandlers { get; set; } = new();
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public bool LogParameters { get; set; } = true;
        public bool LogResult { get; set; }
        public int CacheDurationSeconds { get; set; } = 300;
        public string? CacheKeyPrefix { get; set; }
        public int MaxRetryCount { get; set; } = 3;
        public int RetryBaseDelayMs { get; set; } = 100;
        public int CircuitFailureThreshold { get; set; } = 5;
        public int CircuitBreakDurationSeconds { get; set; } = 30;
        public int MaxRequestsPerSecond { get; set; } = 100;
    }

    internal class InterceptMethodInfo
    {
        public string Name { get; set; } = "";
        public string ReturnType { get; set; } = "";
        public bool IsAsync { get; set; }
        public bool IsVoid { get; set; }
        public bool IsTaskNoResult { get; set; }
        public InterceptFlags Flags { get; set; }
        /// <summary>透传方法（无拦截，直接调用 _inner）</summary>
        public bool IsPassthrough { get; set; }
        public List<ParamInfo> Parameters { get; set; } = new();
        /// <summary>方法级自定义拦截器（null 表示使用类级别）</summary>
        public List<CustomHandlerInfo>? CustomHandlers { get; set; }
    }

    internal class ParamInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsNullable { get; set; }
    }

    internal class CustomHandlerInfo
    {
        public string TypeName { get; set; } = "";
        public string ShortName { get; set; } = "";
        public int Order { get; set; } = 100;
        /// <summary>true = IMethodHandler<TArgs,TResult>（强类型），false = IInterceptHandler（通用）</summary>
        public bool IsMethodHandler { get; set; }
    }

    #endregion
}
