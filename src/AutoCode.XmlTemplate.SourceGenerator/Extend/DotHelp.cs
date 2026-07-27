using DotLiquid;
using Jint;
using System;

namespace AutoCode.DotTemplate.SourceGenerator.Extend
{
    /// <summary>
    /// dot.js帮助类
    /// 
    /// </summary>
    public class DotHelp
    {
        /// <summary>
        /// 使用dot.js进行数据转换
        /// </summary>
        /// <param name="dotTemplate"></param>
        /// <param name="dotData"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static string DotConvert(string dotTemplate, string dotjs, string dotData)
        {
            try
            {  // 创建 Jint 引擎实例
                var engine = new Engine();
                // 引入 doT.js 库
                engine.Execute(dotjs);
                // 定义 doT 模板
                engine.Execute($"var template = doT.template('{dotTemplate}');");
                // 调用 doT 模板并传递数据
                var result = engine.Execute($"template({dotData});").GetCompletionValue().AsString();
                // 输出结果
                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error executing JavaScript: {ex.Message}");
            }
        }

        /// <summary>
        /// 使用DotLiquid进行数据转换
        /// </summary>
        public static string DotLiquidConvert(string dotTemplate, object obj)
        {
            Template template = Template.Parse(dotTemplate);
            return template.Render(Hash.FromAnonymousObject(obj));
        }

    }
}
