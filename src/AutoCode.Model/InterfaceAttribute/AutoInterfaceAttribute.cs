using System;

namespace AutoCode.Model.InterfaceAttribute
{

    /// <summary>
    /// 自动接口生成
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class AutoInterfaceAttribute : Attribute
    {
        /// <summary>
        /// 接口名称
        /// 默认为当前类加 I 为接口名
        /// </summary>
        public string InterfaceName { get; set; }

        /// <summary>
        /// 生成接口文件路径
        /// 目前支持绝对路径
        /// 如果配置为"/"标识符时，使用生成类文件夹为指定路径
        /// TODO:后续支持相对路径
        /// </summary>
        public string InterfacePath { get; set; } = string.Empty;

        /// <summary>
        /// 
        /// </summary>
        public AutoInterfaceAttribute()
        {
            InterfaceName = string.Empty;
        }

        public AutoInterfaceAttribute(string interfaceName)
        {
            InterfaceName = interfaceName;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="interfaceName"></param>
        /// <param name="path"></param>
        public AutoInterfaceAttribute(string interfaceName, string path)
        {
            InterfaceName = interfaceName;
            InterfacePath = path;
        }

    }
}
