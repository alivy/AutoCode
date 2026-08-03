using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using AutoCode.Engine.Template;
using AutoCode.Model;

namespace AutoCode.Generators.Custom
{
    /// <summary>
    /// 自定义配方生成器 - 一个 IIncrementalGenerator 处理所有用户定义的代码生成配方。
    /// 
    /// 工作流程：
    /// 1. 从 autocode.json 读取 customGenerators 配置
    /// 2. 从 AdditionalFiles 读取 .liquid 模板内容
    /// 3. 收集所有类声明，与配方匹配（Attribute 触发 / 类名模式匹配）
    /// 4. 用 SimpleTemplateEngine 渲染模板 → AddSource 产出 .g.cs
    /// </summary>
    [Generator]
    public class CustomRecipeGenerator : IIncrementalGenerator
    {
        private const string CustomGenerateAttrName = "CustomGenerate";
        private const string CustomGenerateAttrFullName = "CustomGenerateAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // ═══ 1. 读取模板文件（AdditionalFiles: *.liquid / *.template）═══
            var templateFiles = context.AdditionalTextsProvider
                .Where(static f => f.Path.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase)
                                || f.Path.EndsWith(".template", StringComparison.OrdinalIgnoreCase))
                .Collect();

            // ═══ 2. 读取配方配置（从 autocode.json via MSBuild）═══
            var configText = context.AnalyzerConfigOptionsProvider
                .Select((provider, _) => LoadConfigJson(provider));

            // ═══ 3. 收集带 [CustomGenerate] 的类 ═══
            var customAttrClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cds &&
                        HasCustomGenerateAttribute(cds),
                    transform: static (ctx, ct) => ExtractClassRecipeInfo(ctx, ct))
                .Where(static x => x != null);

            // ═══ 4. 收集所有类（用于 classPattern 隐式匹配）═══
            var allClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, ct) => ExtractClassInfo(ctx, ct))
                .Where(static x => x != null);

            // ═══ 5. 合并数据源（分步合并，避免深层元组析构）═══
            var classData = customAttrClasses.Collect().Combine(allClasses.Collect());
            var configAndTemplates = configText.Combine(templateFiles);
            var combined = classData.Combine(configAndTemplates);

            // ═══ 6. 注册输出 ═══
            context.RegisterSourceOutput(combined, (spc, data) =>
            {
                var (classPair, configPair) = data;
                var (customClasses, allClassList) = classPair;
                var (configJson, templates) = configPair;

                if (string.IsNullOrWhiteSpace(configJson)) return;

                var recipes = RecipeConfigLoader.LoadFromJson(configJson);
                if (recipes.Count == 0) return;

                // 构建模板查找表
                var templateMap = BuildTemplateMap(templates);

                // 处理显式 [CustomGenerate("xxx")] 标记的类
                foreach (var classInfo in customClasses)
                {
                    if (classInfo == null) continue;
                    var recipe = recipes.FirstOrDefault(r =>
                        string.Equals(r.Name, classInfo.RecipeName, StringComparison.OrdinalIgnoreCase));
                    if (recipe == null) continue;

                    GenerateFromRecipe(spc, recipe, classInfo, templateMap);
                }

                // 处理 classPattern 隐式匹配的类
                foreach (var classInfo in allClassList)
                {
                    if (classInfo == null) continue;
                    foreach (var recipe in recipes)
                    {
                        if (ShouldTriggerByPattern(recipe, classInfo))
                            GenerateFromRecipe(spc, recipe, classInfo, templateMap);
                    }
                }
            });
        }

        #region 匹配逻辑

        private static bool HasCustomGenerateAttribute(ClassDeclarationSyntax cds)
        {
            return cds.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
            {
                var name = a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString();
                return name == CustomGenerateAttrName || name == CustomGenerateAttrFullName;
            });
        }

        private static bool ShouldTriggerByPattern(CodeGenRecipe recipe, ClassRecipeInfo classInfo)
        {
            var trigger = recipe.Trigger;

            // 有 attributeName 且类已有该 Attribute → 触发
            if (!string.IsNullOrEmpty(trigger.AttributeName))
            {
                if (classInfo.ExistingAttributes.Any(a =>
                    string.Equals(a, trigger.AttributeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, trigger.AttributeName + "Attribute", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            // classPattern 匹配
            if (!string.IsNullOrEmpty(trigger.ClassPattern))
            {
                if (!RecipeConfigLoader.MatchPattern(classInfo.ClassName, trigger.ClassPattern!))
                    return false;

                // 还需要满足其他条件
                if (trigger.RequiredProperties != null && trigger.RequiredProperties.Length > 0)
                {
                    if (!trigger.RequiredProperties.All(rp =>
                        classInfo.Properties.Any(p => string.Equals(p.Name, rp, StringComparison.OrdinalIgnoreCase))))
                        return false;
                }

                if (trigger.RequiredMethods != null && trigger.RequiredMethods.Length > 0)
                {
                    if (!trigger.RequiredMethods.All(rm =>
                        classInfo.Methods.Any(m => string.Equals(m.Name, rm, StringComparison.OrdinalIgnoreCase))))
                        return false;
                }

                if (trigger.RequiredInterfaces != null && trigger.RequiredInterfaces.Length > 0)
                {
                    if (!trigger.RequiredInterfaces.All(ri =>
                        classInfo.Interfaces.Any(i => string.Equals(i, ri, StringComparison.OrdinalIgnoreCase))))
                        return false;
                }

                return true;
            }

            return false;
        }

        #endregion

        #region 生成逻辑

        private static void GenerateFromRecipe(
            SourceProductionContext spc,
            CodeGenRecipe recipe,
            ClassRecipeInfo classInfo,
            Dictionary<string, string> templateMap)
        {
            // 查找模板内容
            var templateContent = FindTemplate(recipe.Output.Template, templateMap);
            if (string.IsNullOrEmpty(templateContent))
            {
                // 没有模板 → 使用默认生成的接口包装
                templateContent = GenerateDefaultTemplate(recipe, classInfo);
            }

            // 构建模板上下文
            var ctx = TemplateContext.FromClassInfo(
                classInfo.ClassName,
                classInfo.Namespace,
                classInfo.Methods.Select(m => new Engine.Template.MethodInfo
                {
                    Name = m.Name,
                    ReturnType = m.ReturnType,
                    Parameters = m.Parameters,
                    ArgumentNames = m.ArgumentNames,
                    XmlDoc = m.XmlDoc
                }),
                classInfo.Properties.Select(p => new Engine.Template.PropertyInfo
                {
                    Name = p.Name,
                    Type = p.Type,
                    IsNullable = p.IsNullable,
                    HasGetter = p.HasGetter,
                    HasSetter = p.HasSetter
                }),
                classInfo.Interfaces,
                recipe.Name);

            // 渲染模板
            var engine = new SimpleTemplateEngine();
            var code = engine.Render(templateContent, ctx);

            // 计算输出文件名
            var fileName = recipe.Output.FileName
                .Replace("{ClassName}", classInfo.ClassName)
                .Replace("{RecipeName}", Capitalize(recipe.Name));

            spc.AddSource(fileName, SourceText.From(code, Encoding.UTF8));
        }

        private static string FindTemplate(string templatePath, Dictionary<string, string> templateMap)
        {
            if (string.IsNullOrEmpty(templatePath)) return "";

            // 精确匹配
            if (templateMap.TryGetValue(templatePath, out var content))
                return content;

            // 文件名匹配
            var fileName = templatePath.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
            foreach (var kv in templateMap)
            {
                var keyFile = kv.Key.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
                if (string.Equals(keyFile, fileName, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }

            return "";
        }

        private static string GenerateDefaultTemplate(CodeGenRecipe recipe, ClassRecipeInfo classInfo)
        {
            var ns = recipe.Output.Namespace.Replace("{SourceNamespace}", classInfo.Namespace);
            var suffix = Capitalize(recipe.Name);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine($"// Generated by AutoCode CustomRecipe: {recipe.Name}");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Auto-generated {recipe.Title} for {classInfo.ClassName}.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public class {classInfo.ClassName}{suffix}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly {classInfo.ClassName} _inner;");
            sb.AppendLine();
            sb.AppendLine($"        public {classInfo.ClassName}{suffix}({classInfo.ClassName} inner)");
            sb.AppendLine("        {");
            sb.AppendLine("            _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));");
            sb.AppendLine("        }");

            foreach (var method in classInfo.Methods)
            {
                sb.AppendLine();
                if (method.ReturnType == "void" || method.ReturnType == "Task")
                {
                    sb.AppendLine($"        public {method.ReturnType} {method.Name}({method.Parameters})");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            _inner.{method.Name}({method.ArgumentNames});");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine($"        public {method.ReturnType} {method.Name}({method.Parameters})");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            return _inner.{method.Name}({method.ArgumentNames});");
                    sb.AppendLine("        }");
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        #endregion

        #region 数据提取

        private static ClassRecipeInfo? ExtractClassRecipeInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var cds = (ClassDeclarationSyntax)ctx.Node;
            var info = new ClassRecipeInfo();

            // 提取 RecipeName from [CustomGenerate("name")]
            foreach (var attrList in cds.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var name = attr.Name is IdentifierNameSyntax id ? id.Identifier.Text : attr.Name.ToString();
                    if (name == CustomGenerateAttrName || name == CustomGenerateAttrFullName)
                    {
                        if (attr.ArgumentList?.Arguments.Count > 0)
                        {
                            var arg = attr.ArgumentList.Arguments[0].Expression;
                            if (arg is LiteralExpressionSyntax literal)
                                info.RecipeName = literal.Token.ValueText;
                        }
                    }
                }
            }

            FillClassInfo(info, cds, ctx, ct);
            return string.IsNullOrEmpty(info.RecipeName) ? null : info;
        }

        private static ClassRecipeInfo? ExtractClassInfo(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var cds = (ClassDeclarationSyntax)ctx.Node;
            var info = new ClassRecipeInfo();
            FillClassInfo(info, cds, ctx, ct);
            return info;
        }

        private static void FillClassInfo(
            ClassRecipeInfo info, ClassDeclarationSyntax cds,
            GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            info.ClassName = cds.Identifier.Text;

            // 命名空间
            var ns = GetNamespace(cds);
            info.Namespace = ns;

            // 已有 Attribute
            info.ExistingAttributes = cds.AttributeLists
                .SelectMany(al => al.Attributes)
                .Select(a => a.Name is IdentifierNameSyntax id ? id.Identifier.Text : a.Name.ToString())
                .ToList();

            // 公共方法
            foreach (var method in cds.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!method.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)) continue;

                var returnType = method.ReturnType.ToString();
                var parameters = string.Join(", ",
                    method.ParameterList.Parameters.Select(p => p.ToString()));
                var argNames = string.Join(", ",
                    method.ParameterList.Parameters.Select(p => p.Identifier.Text));

                info.Methods.Add(new ClassMethodInfo
                {
                    Name = method.Identifier.Text,
                    ReturnType = returnType,
                    Parameters = parameters,
                    ArgumentNames = argNames
                });
            }

            // 公共属性
            foreach (var prop in cds.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (!prop.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)) continue;

                info.Properties.Add(new ClassPropertyInfo
                {
                    Name = prop.Identifier.Text,
                    Type = prop.Type.ToString(),
                    IsNullable = prop.Type.ToString().Contains("?"),
                    HasGetter = prop.AccessorList?.Accessors.Any(a =>
                        a.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration)) ?? false,
                    HasSetter = prop.AccessorList?.Accessors.Any(a =>
                        a.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SetAccessorDeclaration) ||
                        a.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.InitAccessorDeclaration)) ?? false
                });
            }

            // 接口
            if (cds.BaseList != null)
            {
                foreach (var baseType in cds.BaseList.Types)
                {
                    var typeName = baseType.Type.ToString();
                    if (typeName.StartsWith("I") && typeName.Length > 1 && char.IsUpper(typeName[1]))
                        info.Interfaces.Add(typeName);
                }
            }
        }

        private static string GetNamespace(ClassDeclarationSyntax cds)
        {
            var ns = cds.Parent;
            while (ns != null)
            {
                if (ns is NamespaceDeclarationSyntax nds)
                    return nds.Name.ToString();
                if (ns is FileScopedNamespaceDeclarationSyntax fsnds)
                    return fsnds.Name.ToString();
                ns = ns.Parent;
            }
            return "Global";
        }

        private static string LoadConfigJson(AnalyzerConfigOptionsProvider provider)
        {
            if (provider.GlobalOptions.TryGetValue("build_property.AutoCode_ConfigJson", out var json))
                return json;
            return "";
        }

        private static Dictionary<string, string> BuildTemplateMap(ImmutableArray<AdditionalText> files)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var content = file.GetText()?.ToString();
                if (!string.IsNullOrEmpty(content))
                    map[file.Path] = content!;
            }
            return map;
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        #endregion

        #region 内部模型

        private class ClassRecipeInfo
        {
            public string RecipeName { get; set; } = "";
            public string ClassName { get; set; } = "";
            public string Namespace { get; set; } = "Global";
            public List<string> ExistingAttributes { get; set; } = new List<string>();
            public List<ClassMethodInfo> Methods { get; set; } = new List<ClassMethodInfo>();
            public List<ClassPropertyInfo> Properties { get; set; } = new List<ClassPropertyInfo>();
            public List<string> Interfaces { get; set; } = new List<string>();
        }

        private class ClassMethodInfo
        {
            public string Name { get; set; } = "";
            public string ReturnType { get; set; } = "void";
            public string Parameters { get; set; } = "";
            public string ArgumentNames { get; set; } = "";
            public string? XmlDoc { get; set; }
        }

        private class ClassPropertyInfo
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public bool IsNullable { get; set; }
            public bool HasGetter { get; set; }
            public bool HasSetter { get; set; }
        }

        #endregion
    }
}
