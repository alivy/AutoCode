using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoCode.Engine.Pipeline
{
    /// <summary>
    /// 生成上下文 - 传递给每个插件的完整环境信息。
    /// 包含编译信息、配置、诊断收集器、以及插件间共享数据。
    /// </summary>
    public class GenerationContext
    {
        /// <summary>Roslyn 编译对象</summary>
        public Compilation Compilation { get; }

        /// <summary>当前处理的语法节点（如果是单节点触发）</summary>
        public SyntaxNode? CurrentNode { get; set; }

        /// <summary>当前节点的语义模型</summary>
        public SemanticModel? SemanticModel { get; set; }

        /// <summary>当前处理的类型符号</summary>
        public INamedTypeSymbol? CurrentTypeSymbol { get; set; }

        /// <summary>所有候选类型符号（批量模式）</summary>
        public ImmutableArray<INamedTypeSymbol> CandidateTypes { get; set; }

        /// <summary>配置提供器</summary>
        public Config.IAutoCodeConfig Config { get; }

        /// <summary>诊断收集器</summary>
        public Diagnostics.IDiagnosticCollector Diagnostics { get; }

        /// <summary>取消令牌</summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>插件间共享数据袋（用于插件间传递信息）</summary>
        public SharedDataBag SharedData { get; }

        /// <summary>之前插件生成的所有文件（只读）</summary>
        public IReadOnlyList<GeneratedFile> PreviousOutputs { get; internal set; }

        /// <summary>MSBuild 全局选项提供器</summary>
        public AnalyzerConfigOptionsProvider? OptionsProvider { get; set; }

        public GenerationContext(
            Compilation compilation,
            Config.IAutoCodeConfig config,
            Diagnostics.IDiagnosticCollector diagnostics,
            CancellationToken cancellationToken = default)
        {
            Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            CancellationToken = cancellationToken;
            SharedData = new SharedDataBag();
            PreviousOutputs = Array.Empty<GeneratedFile>();
            CandidateTypes = ImmutableArray<INamedTypeSymbol>.Empty;
        }

        /// <summary>
        /// 获取指定命名空间下的所有类型
        /// </summary>
        public IEnumerable<INamedTypeSymbol> GetTypesInNamespace(string namespaceName)
        {
            var ns = FindNamespace(Compilation.GlobalNamespace, namespaceName);
            if (ns == null) yield break;

            foreach (var member in ns.GetMembers())
            {
                if (member is INamedTypeSymbol typeSymbol)
                    yield return typeSymbol;
            }
        }

        private static INamespaceSymbol? FindNamespace(INamespaceSymbol root, string targetName)
        {
            if (root.ToDisplayString() == targetName)
                return root;

            foreach (var member in root.GetNamespaceMembers())
            {
                var found = FindNamespace(member, targetName);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// 查找具有指定特性名称的所有类型
        /// </summary>
        public IEnumerable<INamedTypeSymbol> FindTypesWithAttribute(string attributeFullName)
        {
            foreach (var syntaxTree in Compilation.SyntaxTrees)
            {
                var semanticModel = Compilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot(CancellationToken);

                foreach (var classDecl in root.DescendantNodes())
                {
                    if (classDecl is ClassDeclarationSyntax cds)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(cds, CancellationToken) as INamedTypeSymbol;
                        if (symbol == null) continue;

                        foreach (var attr in symbol.GetAttributes())
                        {
                            if (attr.AttributeClass?.ToDisplayString() == attributeFullName
                                || attr.AttributeClass?.Name + "Attribute" == attributeFullName)
                            {
                                yield return symbol;
                                break;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 插件间共享数据袋 - 线程安全的键值存储
    /// </summary>
    public sealed class SharedDataBag
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        /// <summary>设置共享数据</summary>
        public void Set<T>(string key, T value) where T : class
        {
            lock (_lock)
            {
                _data[key] = value;
            }
        }

        /// <summary>获取共享数据</summary>
        public T? Get<T>(string key) where T : class
        {
            lock (_lock)
            {
                return _data.TryGetValue(key, out var value) ? value as T : null;
            }
        }

        /// <summary>尝试获取共享数据</summary>
        public bool TryGet<T>(string key, out T? value) where T : class
        {
            lock (_lock)
            {
                if (_data.TryGetValue(key, out var raw) && raw is T typed)
                {
                    value = typed;
                    return true;
                }
                value = null;
                return false;
            }
        }

        /// <summary>是否包含指定键</summary>
        public bool Contains(string key)
        {
            lock (_lock)
            {
                return _data.ContainsKey(key);
            }
        }
    }
}
