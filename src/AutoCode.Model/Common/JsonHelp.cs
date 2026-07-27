using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoCode.Model.Common
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
