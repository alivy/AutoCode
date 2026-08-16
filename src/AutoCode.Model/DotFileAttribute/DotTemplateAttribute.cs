using System;

namespace AutoCode.Model
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class DotTemplateAttribute : Attribute
    {
        /// <summary>
        /// 模版文件
        /// 可使用相对路径或者绝对路径
        /// 注意：在使用相对路径时，是相对当前特性标记.cs文件的相对路径，并非项目的相对路劲
        /// </summary>
        public string DotFileName { get; set; } = string.Empty;

        /// <summary>
        /// 生成文件路径
        /// 可使用相对路径或者绝对路径
        /// 注意：在使用相对路径时，是相对当前特性标记.cs文件的相对路径，并非项目的相对路劲
        /// 如果参数为空，则自动生成到模板文件路径下
        /// </summary>
        public string GeneratePath { get; set; } = string.Empty;

        /// <summary>
        /// 生成文件名称
        /// 默认命名为源文件名+Copy.cs
        /// 可以直接命名
        /// 也可以使用json文件命名规则替换，例{{DefName}}Copy.cs
        /// </summary>
        public string FileName { get; set; } =string.Empty;

        /// <summary>
        ///是否内置系统文件
        /// </summary>
        public bool IsSysFile { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inFile">是否内置文件</param>
        /// <param name="fileName">生成文件名称</param>
        public DotTemplateAttribute(bool sysFile, string fileName)
        {
            IsSysFile = sysFile;
            FileName = fileName;
        }

        /// <summary>
        /// 指定模板文件自动构建
        /// </summary>
        /// <param name="dotFileName"></param>
        public DotTemplateAttribute(string dotFileName)
        {
            DotFileName = dotFileName;
        }

        /// <summary>
        /// 指定模板文件和生产文件路劲构建
        /// </summary>
        /// <param name="dotFileName">xml模版文件名称</param>
        /// <param name="generatePath">生成文件路径</param>
        public DotTemplateAttribute(string dotFileName, string generatePath)
        {
            DotFileName = dotFileName;
            GeneratePath = generatePath;
        }


        /// <summary>
        /// 指定模板文件和生产文件路劲构建以及生产文件名称
        /// </summary>
        /// <param name="dotFileName">xml模版文件名称</param>
        /// <param name="generatePath">生成文件路径</param>
        ///  <param name="fileName">生成文件名称</param>
        public DotTemplateAttribute(string dotFileName, string generatePath,string fileName)
        {
            DotFileName = dotFileName;
            GeneratePath = generatePath;
            FileName = fileName;
        }
    }
}
