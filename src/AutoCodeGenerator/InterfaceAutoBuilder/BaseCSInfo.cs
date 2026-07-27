using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoCode.SourceGenerator.InterfaceAutoBuilder
{
    /// <summary>
    /// 获取.cs文件基础信息
    /// </summary>
    public class BaseCSInfo
    {
        /// <summary>
        /// 获得命名空间
        /// </summary>
        public NamespaceDeclarationSyntax NamespaceDeclaration { get; set; }
        /// <summary>
        ///  using指令信息
        /// </summary>
        public List<UsingDirectiveSyntax> UsingDirectives { get; set; } = new List<UsingDirectiveSyntax>();
        /// <summary>
        /// 
        /// </summary>
        public ClassDeclarationSyntax ClassDeclarations { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public InterfaceAttributeInfo InterfaceInfo { get; set; } = new InterfaceAttributeInfo();


        /// <summary>
        /// 接口特性信息
        /// </summary>
        public class InterfaceAttributeInfo
        {
            /// <summary>
            /// 接口名称
            /// </summary>
            public string InterfaceName { get; set; }
            /// <summary>
            /// 接口文件生成路劲
            /// </summary>
            public string InterfacePath { get; set; }
        }
    }
}
