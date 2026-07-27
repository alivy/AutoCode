using APP.WebAPI.Core.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace APP.WebAPI.Core.DependencyInjection
{
    public static  class DependencyInjectionServiceCollectionExtensions
    {
        /// <summary>
        /// 添加依赖注入接口
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddDependencyInjection(this IServiceCollection services)
        {
            // 添加内部依赖注入扫描拓展
            services.AddInnerDependencyInjection();

            return services;
        }

        /// <summary>
        /// 添加扫描注入
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        private static IServiceCollection AddInnerDependencyInjection(this IServiceCollection services)
        {
            // 查找所有需要依赖注入的类型
            var injectTypes = AppCore.Types
                .Where(u => typeof(IDependencyBase).IsAssignableFrom(u) && u.IsClass && !u.IsInterface && !u.IsAbstract).Distinct();

            var lifetimeInterfaces = new[] { typeof(ITransient), typeof(IScoped), typeof(ISingleton) };

            // 执行依赖注入
            foreach (var type in injectTypes)
            {
                var interfaces = type.GetInterfaces();

                var serviceInterfaces = interfaces.Where(i => i != typeof(IDependencyBase)
                        && !lifetimeInterfaces.Contains(i)
                        && !type.IsGenericType
                        && !i.IsGenericType ||
                            (type.IsGenericType
                                && i.IsGenericType
                                && type.GetGenericArguments().Length == i.GetGenericArguments().Length));
                Func<ServiceLifetime?> lifeTimeFunc = () =>
                {
                    var dependencyType = interfaces.Last(u => lifetimeInterfaces.Contains(u));
                    return GetServiceLifetime(dependencyType);
                };
                //获取生命周期
                var lifetime = lifeTimeFunc();
                //注册服务
                RegisterService(services, serviceInterfaces, type, lifetime);
            }

            return services;
        }

        /// <summary>
        /// 注册服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="serviceInterfaces">能被注册的接口</param>
        /// <param name="implementationType">类型</param>
        /// <param name="lifetime">生命周期</param>
        private static void RegisterService(IServiceCollection services, IEnumerable<Type> serviceInterfaces, Type implementationType, ServiceLifetime? lifetime)
        {
            if (lifetime != null)
            {
                if (serviceInterfaces.Count() == 0)
                {
                    var serviceDescriptor = ServiceDescriptor.Describe(implementationType, implementationType, lifetime.Value);
                    services.TryAdd(serviceDescriptor);
                }

                foreach (var serviceInterface in serviceInterfaces)
                {
                    var serviceDescriptor = ServiceDescriptor.Describe(serviceInterface, implementationType, lifetime.Value);
                    services.TryAdd(serviceDescriptor);
                }
            }
        }

        /// <summary>
        /// 获取服务生命周期
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private static ServiceLifetime? GetServiceLifetime(Type type)
        {
            if (typeof(ITransient).GetTypeInfo().IsAssignableFrom(type))
            {
                return ServiceLifetime.Transient;
            }

            if (typeof(ISingleton).GetTypeInfo().IsAssignableFrom(type))
            {
                return ServiceLifetime.Singleton;
            }

            if (typeof(IScoped).GetTypeInfo().IsAssignableFrom(type))
            {
                return ServiceLifetime.Scoped;
            }

            return null;
        }
    }
}
