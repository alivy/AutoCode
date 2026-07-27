using APP.WebAPI.Core.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyModel;
using System.Reflection;
using System.Runtime.Loader;

namespace APP.WebAPI.Core.Application
{
    public static class AppCore
    {

        /// <summary>
        /// 配置对象
        /// </summary>
        public static IConfiguration Configuration;

        /// <summary>
        /// 有效程序集中的类型
        /// </summary>
        public static IEnumerable<Type> Types { get; }



        /// <summary>
        /// 构造函数
        /// </summary>
        static AppCore()
        {
            var assemblies = DependencyContext.Default.RuntimeLibraries
                             .Where(r => r.Type == "project" || r.Type == "package" && r.Name.StartsWith("APP."))
                             .Select(r => AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(r.Name)))
                             .ToHashSet();

            //程序集中的有效类型
            Types = assemblies.SelectMany(a => a.GetTypes().Where(t => t.IsPublic));
        }

        /// <summary>
        /// 配置应用程序
        /// </summary>
        /// <param name="hostBuilder"></param>
        public static void ConfigureApplication(IWebHostBuilder hostBuilder)
        {
            hostBuilder.ConfigureServices((hostContext, services) =>
            {
                // 静态化配置对象
                Configuration = hostContext.Configuration;
                AddFrameworkCoreService(services);
            });
        }

        /// <summary>
        /// 添加框架核心服务
        /// </summary>
        /// <param name="services"></param>
        static void AddFrameworkCoreService(IServiceCollection services)
        {
            services.AddControllers();
            services.AddEndpointsApiExplorer();
            // 注册 HttpContextAccessor 服务
            services.AddHttpContextAccessor();
            ///添加依赖注入
            services.AddDependencyInjection();
        }
    }
}
