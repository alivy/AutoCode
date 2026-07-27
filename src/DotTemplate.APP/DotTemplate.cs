using AutoCode.Model;
using AutoCode.Model.InterfaceAttribute;
using Models;
using System.ComponentModel;

namespace DotTemplate.APP
{
    #region 绝对路径文件生成
    /// <summary>
    /// 指定DotTemplate参数1绝对路径模板
    /// </summary>
    //[DotTemplate( @"F:\AmiaoCode\auto-code\src\DotTemplate.APP\DotTemplate\Template_Remark.dot")]
    public class DotTemplateV1
    {



    }

    /// <summary>
    /// 指定DotTemplate参数1绝对路径模板
    /// 指定DotTemplate参数2绝对路径生成文件
    /// </summary>
    //[DotTemplate( 
    //@"F:\AmiaoCode\auto-code\src\DotTemplate.APP\DotTemplate\Template_Remark.dot",
    //@"F:\AmiaoCode\auto-code\src\DotTemplate.APP\CopyFile\")]
    public class DotTemplateV2
    {

    }

    /// <summary>
    /// 指定DotTemplate参数1绝对路径模板
    /// 指定DotTemplate参数2绝对路径生成文件
    /// 指定DotTemplate参数3生成文件名称
    /// </summary>
    //[DotTemplate( 
    //@"F:\AmiaoCode\auto-code\src\DotTemplate.APP\DotTemplate\Template_Remark.dot",
    //@"F:\AmiaoCode\auto-code\src\DotTemplate.APP\CopyFile\",
    //"Base{{DefName}}Copy.cs")]
    public class DotTemplateV3
    {

    }
    #endregion


    #region 相对路径文件生成
    /// <summary>
    /// 指定DotTemplate参数1相对路径模板
    /// </summary>
    //[DotTemplate("DotTemplate/Template_Caller.dot")]
    public class DotTemplateS1
    {
        public static int BookingQuery()
        {
            return 2;
        }


        public static int CacheQuery()
        {
            return DBQuery();
        }

        [AutoIgnore]
        private static int DBQuery()
        {
            return 1;
        }

        internal static int DBQueryV2()
        {
            return 1;
        }
    }

    /// <summary>
    /// 指定DotTemplate参数1相对路径模板
    /// 指定DotTemplate参数2相对路径生成文件
    /// </summary>
    //[DotTemplate("DotTemplate/Template_Caller.dot", "CopyFile/")]
    public class DotTemplateS2
    {

    }

    /// <summary>
    /// 指定DotTemplate参数1相对路径模板
    /// 指定DotTemplate参数2相对路径生成文件
    /// 指定DotTemplate参数3生成文件名称
    /// </summary>
    [DotTemplate("DotTemplate/Template_Caller.dot", "CopyFile/", "{{DefName}}Caller.cs")]
    public class DotTemplateS3 : IDotTemplateS3
    {
        /// <summary>
        /// 关于订单的查询信息
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="num2">计算值</param>
        /// <returns></returns>
        public static int BookingQuery(string str, int num2)
        {   
            CacheQuery(str);
            return num2;
        }


        internal static string CacheQuery(string str)
        {
            return DBQuery(str);
        }

        [AutoIgnore]
        public static string DBQuery(string str)
        {
            return str + "DB";
        }

        public static void Excute()
        {
            throw new WarningException();
        }
    }

    interface IDotTemplateS3 { }


    /// <summary>
    /// 指定DotTemplate参数1相对路径模板
    /// 指定DotTemplate参数2相对路径生成文件，当标记为$Source.cs则生成内存文件
    /// 指定DotTemplate参数3生成文件名称
    /// </summary>
    [DotTemplate("DotTemplate/Template_Caller.dot", "$Source.cs", "{{DefName}}Caller.cs")]
    public class DotTemplateS4
    {
        /// <summary>
        /// 订单查询
        /// </summary>
        /// <param name="str"></param>
        /// <param name="num2"></param>
        /// <returns></returns>
        public static int BookingQueryV(string str, int num2)
        {
            DotTemplateS4Caller.BookingQueryV(str, num2);
            CacheQuery(str);
            return num2;
        }

        internal static string CacheQuery(string str)
        {
            return DBQuery(str);
        }

        [AutoIgnore]
        public static string DBQuery(string str)
        {
            return str + "DB";
        }


    }

    /// <summary>
    /// 指定DotTemplate参数1相对路径模板
    /// 指定DotTemplate参数2相对路径生成文件，当标记为$Source.cs则生成内存文件
    /// 指定DotTemplate参数3生成文件名称
    /// </summary>
    //[DotTemplate("DotTemplate/Template_Caller.dot", "$Source.cs", "{{DefName}}Caller.cs")]
    public class DotTemplateS5
    {
        public int Id { get; set; }

        public List<Person> Persons { get; set; }

        public Address address { get; set; }

        public Test test { get; set; }

        public class Person
        {
            public string Name { get; set; }
            public int Age { get; set; }
            public Address Address { get; set; }
        }
    }


    public class Address
    {
        public string Street { get; set; }
        public string City { get; set; }
    }

    #endregion

    #region AotoMap

    #endregion




}
