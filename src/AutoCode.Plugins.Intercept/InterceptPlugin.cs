using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using AutoCode.Engine.Plugin;
using AutoCode.Engine.Pipeline;

namespace AutoCode.Plugins.Intercept
{
    /// <summary>
    /// Intercept 插件 - 将通用方法拦截器接入 AutoCode 管线。
    /// 编译时 AOP，替代 Castle DynamicProxy 等运行时动态代理方案。
    /// </summary>
    public class InterceptPlugin : GenerationPluginBase
    {
        public override string Name => "intercept";
        public override string Description => "编译时 AOP 通用方法拦截器（Log/Cache/Retry/CircuitBreaker/Metrics/Throttle）";
        public override Version Version => new Version(1, 0, 0);
        public override int Priority => 50; // 在大多数插件之前执行
        public override PluginTrigger Trigger => PluginTrigger.Attribute;

        protected override string AttributeFullName => "AutoCode.Model.AutoInterceptAttribute";

        public override IEnumerable<GeneratedFile> Generate(GenerationContext context)
        {
            // 实际生成逻辑由 InterceptGenerator (IIncrementalGenerator) 独立完成。
            // 此插件主要用于管线注册、配置控制和 Hook 扩展。
            yield break;
        }
    }
}
