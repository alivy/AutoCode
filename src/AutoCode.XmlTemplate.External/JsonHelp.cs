using Newtonsoft.Json;
using System;

namespace AutoCode.DotTemplate.External
{
    public class JsonHelp
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <returns></returns>
        public static string SerializeObject(object obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }
    }
}
