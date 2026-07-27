using APP.WebAPI.Core.Application;
using Microsoft.AspNetCore.Builder;
namespace APP.WebAPI.Core.Application
{

    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 初始化Api
        /// </summary>
        /// <param name="hostBuilder"></param>
        /// <returns></returns>
        public static WebApplicationBuilder InitAPI(this WebApplicationBuilder hostBuilder)
        {
            // 配置应用程序
            AppCore.ConfigureApplication(hostBuilder.WebHost);
            return hostBuilder;
        }
    }
}