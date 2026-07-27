using AutoCode.Model;
using AutoCode.Model.InterfaceAttribute;
using System.ComponentModel;

namespace DotTemplate.APP
{
    /// <summary>
    /// 自动生成模板
    /// </summary>
    //[DotTemplate("DotTemplate/Template.dot", "CopyFile/", "Base{{DefName}}Copy.cs")]
    public class AutoTemplate : IAutoTemplate
    {
        /// <summary> 
        /// 字段1  
        /// </summary>
        public int FiledName1;

        /// <summary>
        /// 属性名      
        /// </summary>
        [DisplayName("字段名")]
        public string? PropertyName { get; set; }

        [DisplayName("方法名")]
        public void Method188()
        {

        }

        /// <summary>
        /// 方法1
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        public int Method12(int num)
        {
            return num;
        }
        /// <summary>
        /// 方法3
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public int Method3(string str)
        {
            return int.Parse(str);
        }

        public int Method4(string str)
        {
            return int.Parse(str);
        }
        public int Method5(int num)
        {
            return num;
        }
    }

}
