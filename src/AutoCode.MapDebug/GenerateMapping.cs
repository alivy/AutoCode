using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoCode.MapDebug
{
    public class GenerateMapping
    {


        public static string GenerateMappingCode(INamedTypeSymbol classSymbol, Compilation compilation)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using Auto.MapModels;");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine($"namespace {classSymbol.ContainingNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static class {classSymbol.Name}Mapper");
            sb.AppendLine("    {");

            // Generate method for copying properties
            sb.AppendLine($"        public static void CopyTo(this {classSymbol.Name} source, {classSymbol.Name} target)");
            sb.AppendLine("        {");

            foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (IsSimpleType(member.Type))
                {
                    sb.AppendLine($"            target.{member.Name} = source.{member.Name};");
                }
                else
                {
                    var nestedType = member.Type as INamedTypeSymbol;
                    if (nestedType != null)
                    {
                        if (IsListType(nestedType, out var listItemType))
                        {
                            // 检查列表元素类型是否为简单类型
                            if (IsSimpleType(listItemType))
                            {
                                // 简单类型直接赋值
                                sb.AppendLine($"            if (source.{member.Name} != null)");
                                sb.AppendLine($"            {{");
                                sb.AppendLine($"                target.{member.Name} = new List<{listItemType.Name}>();");
                                sb.AppendLine($"                foreach (var item in source.{member.Name})");
                                sb.AppendLine($"                {{");
                                sb.AppendLine($"                    target.{member.Name}.Add(item);");
                                sb.AppendLine($"                }}");
                                sb.AppendLine($"            }}");
                            }
                            else
                            {
                                // 复杂类型使用直接赋值避免跨程序集 CopyTo 缺失
                                sb.AppendLine($"            if (source.{member.Name} != null)");
                                sb.AppendLine($"            {{");
                                sb.AppendLine($"                target.{member.Name} = new List<{listItemType.Name}>(source.{member.Name});");
                                sb.AppendLine($"            }}");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"            if (source.{member.Name} != null && target.{member.Name} == null)");
                            sb.AppendLine($"                target.{member.Name} = new {nestedType.Name}();");
                            sb.AppendLine($"            target.{member.Name} = source.{member.Name};");
                        }
                    }
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }


        private static bool IsSimpleType(ITypeSymbol typeSymbol)
        {
            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_Char:
                case SpecialType.System_DateTime:
                case SpecialType.System_Decimal:
                case SpecialType.System_Double:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_SByte:
                case SpecialType.System_Single:
                case SpecialType.System_String:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsListType(INamedTypeSymbol typeSymbol, out INamedTypeSymbol listItemType)
        {
            listItemType = null;

            if (typeSymbol.IsGenericType && typeSymbol.Name == "List" && typeSymbol.TypeArguments.Length == 1)
            {
                listItemType = typeSymbol.TypeArguments[0] as INamedTypeSymbol;
                return listItemType != null;
            }

            return false;
        }
    }
}
