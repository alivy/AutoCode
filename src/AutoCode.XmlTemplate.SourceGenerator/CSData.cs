using AutoCode.XmlTemplate.SourceGenerator;
using DotLiquid;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static AutoCode.DotTemplate.SourceGenerator.CSData.ClassInfo;

namespace AutoCode.DotTemplate.SourceGenerator
{
    /// <summary>
    /// 获取cs文件的数据
    /// </summary>
    public class CSData
    {
        /// <summary>
        /// 命名空间
        /// </summary>
        public string NameSpace { get; set; }

        /// <summary>
        /// ClassDeclarationSyntax
        /// </summary>
        public List<ClassDeclarationSyntax> ClassSyntaxeList { get; set; } = new List<ClassDeclarationSyntax>();
        /// <summary>
        /// 类集合
        /// </summary>
        public List<ClassInfo> ClassInfos(Compilation compilation)
        {
            if (!ClassSyntaxeList.Any())
                return new List<ClassInfo>();
            Func<ClassDeclarationSyntax, ClassInfo> convertFunc = (classSyntax) =>
            {
                var calss = classSyntax.ClassConvert(compilation);
                calss.NameSpace = NameSpace;
                return calss;
            };
            return ClassSyntaxeList.Select(convertFunc).ToList(); ;
        }

      


        /// <summary>
        /// 类信息
        /// </summary>
        public class ClassInfo : ILiquidizable
        {
            /// <summary>
            /// using集合
            /// </summary>
            public List<UsingInfo> Usings { get; set; } = new List<UsingInfo>();

            /// <summary>
            /// 命名空间
            /// </summary>
            public string? NameSpace { get; set; }
            /// <summary>
            /// 修饰符
            /// </summary>
            public string? Modifier { get; set; }
            /// <summary>
            /// 类名
            /// </summary>
            public string DefName { get; set; }

            /// <summary>
            /// 当前cs文件路径
            /// </summary>
            public string ClassPath { get; set; }

            /// <summary>
            /// 当前cs的备注
            /// </summary>
           // public List<RemarkInfo> Remarks { get; set; } = new List<RemarkInfo>();

            /// <summary>
            /// 当前cs的备注
            /// </summary>
            public List<string> Remarks { get; set; }

            /// <summary>
            /// 继承的
            /// </summary>
            public List<InheritInfo> Inherits { get; set; } = new List<InheritInfo>();

            /// <summary>
            /// 特性集合
            /// </summary>
            public List<AttributeInfo> Attributes { get; set; } = new List<AttributeInfo>();

            /// <summary>
            ///方法集合
            /// </summary>
            public List<MethodInfo> Methods { get; set; } = new List<MethodInfo>();

            /// <summary>
            ///字段集合
            /// </summary>
            public List<FiledInfo> Fileds { get; set; } = new List<FiledInfo>();

            /// <summary>
            /// 属性集合
            /// </summary>
            public List<PropertyInfo> Propertys { get; set; } = new List<PropertyInfo>();

            /// <summary>
            /// 嵌套子类
            /// </summary>
            public List<ClassInfo> ClassInfos { get; set; } = new List<ClassInfo> ();
            public object ToLiquid()
            {
                return new
                {
                    Usings = Usings,
                    NameSpace = NameSpace,
                    Modifier = Modifier,
                    DefName = DefName,
                    Remarks = Remarks,
                    Inherits = Inherits,
                    Attributes = Attributes,
                    Methods = Methods,
                    Fileds = Fileds,
                    Propertys = Propertys,
                    ClassInfos = ClassInfos
                };
            }

            /// <summary>
            /// 继承的信息
            /// </summary>
            public class InheritInfo : ILiquidizable
            {
                /// <summary>
                /// 继承名称
                /// </summary>
                public string DefName { get; set; }

                public object ToLiquid()
                {
                    return new { DefName = DefName };
                }
            }

            /// <summary>
            /// 特性信息
            /// </summary>
            public class AttributeInfo : ILiquidizable
            {
                /// <summary>
                /// 特性名称
                /// </summary>
                public string DefName { get; set; }

                /// <summary>
                /// 参数信息
                /// </summary>
                public List<Parameter> Parameters { get; set; } = new List<Parameter>();

                public static AttributeInfo CrteateAttribute(string defName, List<Parameter> parameters)
                {
                    return new AttributeInfo
                    {
                        DefName = defName,
                        Parameters = parameters
                    };
                }

                public object ToLiquid()
                {
                    return new
                    {
                        DefName = DefName,
                        Parameters = Parameters
                    };
                }

                /// <summary>
                /// 特性参数
                /// </summary>
                public class Parameter : ILiquidizable
                {
                    /// <summary>
                    /// 参数类型
                    /// </summary>
                    public string Type { get; set; }
                    /// <summary>
                    /// 名称
                    /// </summary>
                    public string DefName { get; set; }

                    public object ToLiquid()
                    {
                        return new
                        {
                            Type = Type,
                            DefName = DefName
                        };
                    }
                }

            }

            /// <summary>
            /// 方法信息
            /// </summary>
            public class MethodInfo : ILiquidizable
            {
                /// <summary>
                /// 修复符号
                /// </summary>
                public string Modifier { get; set; }

                /// <summary>
                /// 方法名
                /// </summary>
                public string DefName { get; set; }

                /// <summary>
                /// 返回类型
                /// </summary>
                public string Type { get; set; }

                /// <summary>
                /// 备注
                /// </summary>
                public List<string> Remarks { get; set; } = new List<string>();
                /// <summary>
                /// 参数
                /// </summary>
                public List<Parameter> Parameters { get; set; } = new List<Parameter>();

                public object ToLiquid()
                {
                    return new
                    {
                        Modifier = Modifier,
                        DefName = DefName,
                        Parameters = Parameters,
                        Type = Type,
                        Remarks = Remarks
                    };
                }

                /// <summary>
                /// 方法参数
                /// </summary>
                public class Parameter : ILiquidizable
                {
                    /// <summary>
                    /// 参数类型
                    /// </summary>
                    public string Type { get; set; }
                    /// <summary>
                    /// 参数名
                    /// </summary>
                    public string DefName { get; set; }

                    public object ToLiquid()
                    {
                        return new
                        {
                            Type,
                            DefName = DefName,
                        };
                    }
                }
            }

            /// <summary>
            /// 字段信息
            /// </summary>
            public class FiledInfo : ILiquidizable
            {
                /// <summary>
                /// 修饰符号
                /// </summary>
                public string Modifier { get; set; }

                /// <summary>
                /// 字段名
                /// </summary>
                public string DefName { get; set; }

                /// <summary>
                /// 类型
                /// </summary>
                public string Type { get; set; }

                public object ToLiquid()
                {
                    return new
                    {
                        Modifier,
                        DefName = DefName,
                        Type = Type
                    };
                }
            }

            /// <summary>
            /// 属性信息
            /// </summary>
            public class PropertyInfo : ILiquidizable
            {
                /// <summary>
                /// 修饰符号
                /// </summary>
                public string Modifier { get; set; }

                /// <summary>
                /// 字段名
                /// </summary>
                public string DefName { get; set; }

                /// <summary>
                /// 类型
                /// </summary>
                public string Type { get; set; }

                public object ToLiquid()
                {
                    return new { Type, DefName = DefName, Modifier = Modifier };
                }
            }


            /// <summary>
            /// 备注信息
            /// </summary>
            public class RemarkInfo : ILiquidizable
            {
                /// <summary>
                /// 名称
                /// </summary>
                public string DefName { get; set; }

                public object ToLiquid()
                {
                    return new { DefName = DefName };
                }
            }



            /// <summary>
            /// 备注信息
            /// </summary>
            public class UsingInfo : ILiquidizable
            {

                /// <summary>
                /// 名称
                /// </summary>
                public string DefName { get; set; }

                public object ToLiquid()
                {
                    return new { DefName = DefName };
                }
            }
        }

    }

}
