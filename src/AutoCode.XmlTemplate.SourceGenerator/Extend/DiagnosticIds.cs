using System;
using System.Collections.Generic;
using System.Text;

namespace AutoCode.DotTemplate.SourceGenerator.Extend
{
    /// <summary>
    ///用于消息诊断管理
    ///前三位用于告警编码类型管理，后两位为自定义自增编码
    ///110：异常Error
    ///120：告警Warning
    ///119：提示消息Info
    /// </summary>
    public class DiagnosticIds
    {
        /// <summary>
        /// 用于系统异常消息输出
        /// </summary>
        public const string SysError = "SG11001";
        /// <summary>
        /// 用于检测
        /// </summary>
        public const string FileError = "SG11002";

        /// <summary>
        /// 用于系统异常消息输出
        /// </summary>
        public const string FilePathWarn = "SG12001";
      
    }
}
