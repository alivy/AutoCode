using DotLiquid;
using System;

namespace AutoCode.DotTemplate.SourceGenerator.Extend
{
    /// <summary>
    /// DotLiquid 模板帮助类
    /// </summary>
    public class DotHelp
    {
        /// <summary>
        /// 使用 DotLiquid 进行模板数据转换
        /// </summary>
        /// <param name="dotTemplate">DotLiquid 模板字符串</param>
        /// <param name="obj">数据对象</param>
        /// <returns>渲染后的字符串</returns>
        public static string DotLiquidConvert(string dotTemplate, object obj)
        {
            Template template = Template.Parse(dotTemplate);
            return template.Render(Hash.FromAnonymousObject(obj));
        }
    }
}
