using System;
using System.Collections.Generic;

namespace AutoCode.Engine.Plugin
{
    /// <summary>
    /// 生成插件接口 - 所有代码生成器的统一契约。
    /// 每个插件负责一种代码生成能力（如 Mapper、DTO、Controller 等）。
    /// </summary>
    public interface IGenerationPlugin
    {
        /// <summary>插件唯一名称</summary>
        string Name { get; }

        /// <summary>插件描述</summary>
        string Description { get; }

        /// <summary>插件版本</summary>
        Version Version { get; }

        /// <summary>执行优先级（数字越小越先执行，默认 100）</summary>
        int Priority { get; }

        /// <summary>触发方式</summary>
        Pipeline.PluginTrigger Trigger { get; }

        /// <summary>此插件依赖的其他插件名称（确保执行顺序）</summary>
        IReadOnlyList<string> Dependencies { get; }

        /// <summary>
        /// 判断插件是否在当前上下文中启用
        /// </summary>
        bool IsEnabled(Pipeline.GenerationContext context);

        /// <summary>
        /// 判断插件是否能处理指定的类型符号
        /// </summary>
        bool CanProcess(Microsoft.CodeAnalysis.INamedTypeSymbol typeSymbol, Pipeline.GenerationContext context);

        /// <summary>
        /// 执行代码生成
        /// </summary>
        IEnumerable<Pipeline.GeneratedFile> Generate(Pipeline.GenerationContext context);
    }

    /// <summary>
    /// 插件基类 - 提供默认实现，简化插件开发
    /// </summary>
    public abstract class GenerationPluginBase : IGenerationPlugin
    {
        public abstract string Name { get; }
        public virtual string Description => "";
        public virtual Version Version => new Version(1, 0, 0);
        public virtual int Priority => 100;
        public virtual Pipeline.PluginTrigger Trigger => Pipeline.PluginTrigger.Attribute;
        public virtual IReadOnlyList<string> Dependencies => Array.Empty<string>();

        /// <summary>关联的特性全名（用于 Attribute 触发模式）</summary>
        protected abstract string AttributeFullName { get; }

        public virtual bool IsEnabled(Pipeline.GenerationContext context)
        {
            return context.Config.GetBoolean($"plugins.{Name}.enabled", true);
        }

        public virtual bool CanProcess(Microsoft.CodeAnalysis.INamedTypeSymbol typeSymbol, Pipeline.GenerationContext context)
        {
            // 默认检查是否有对应特性
            foreach (var attr in typeSymbol.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                if (attrName == AttributeFullName || attr.AttributeClass?.Name + "Attribute" == AttributeFullName)
                    return true;
            }
            return false;
        }

        public abstract IEnumerable<Pipeline.GeneratedFile> Generate(Pipeline.GenerationContext context);

        /// <summary>
        /// 获取类型上的指定特性数据
        /// </summary>
        protected Microsoft.CodeAnalysis.AttributeData? GetAttribute(
            Microsoft.CodeAnalysis.INamedTypeSymbol typeSymbol)
        {
            foreach (var attr in typeSymbol.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                if (attrName == AttributeFullName || attr.AttributeClass?.Name + "Attribute" == AttributeFullName)
                    return attr;
            }
            return null;
        }

        /// <summary>
        /// 创建生成文件
        /// </summary>
        protected Pipeline.GeneratedFile CreateFile(string fileName, string content)
        {
            return new Pipeline.GeneratedFile(fileName, content, Name);
        }
    }

    /// <summary>
    /// 插件元数据特性 - 标记在程序集级别用于自动发现
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class AutoCodePluginAttribute : Attribute
    {
        /// <summary>插件类型</summary>
        public Type PluginType { get; }

        public AutoCodePluginAttribute(Type pluginType)
        {
            PluginType = pluginType;
        }
    }
}
