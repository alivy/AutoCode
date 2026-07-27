 using AutoCode.Model;
 using AutoCode.Model.InterfaceAttribute;
 using Models;
 using System.ComponentModel;

namespace DotTemplate.APP
 {
     /// <summary>
    /// 指定DotTemplate参数1相对路径模板
    /// 指定DotTemplate参数2相对路径生成文件
    /// 指定DotTemplate参数3生成文件名称
    /// </summary>

	public class DotTemplateS3Caller 
	{
        
		/// <summary>
        /// 关于订单的查询信息
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="num2">计算值</param>
        /// <returns></returns>

		public static int BookingQuery(string str,int num2)
		{
		    return DotTemplateS3.BookingQuery(str,num2);
		}
		
		
		internal static string CacheQuery(string str)
		{
		    return DotTemplateS3.CacheQuery(str);
		}
		
		
		public static void Excute()
		{
		   DotTemplateS3.Excute();
		}
		
	}
}