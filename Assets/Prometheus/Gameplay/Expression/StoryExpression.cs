using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Expression
{
    /// <summary>
    /// 一段已编译的剧情条件表达式。
    /// 编译结果按源文本缓存并可跨上下文复用；求值过程为纯函数，不依赖 Unity 运行时。
    /// </summary>
    public sealed class StoryExpression
    {
        /// <summary>缓存已编译表达式，避免同一条件在每个节拍重复解析。</summary>
        private static readonly Dictionary<string, StoryExpression> Cache = new Dictionary<string, StoryExpression>(StringComparer.Ordinal);

        private readonly StoryExpressionNode root;

        /// <summary>由编译入口创建一个表达式实例。</summary>
        private StoryExpression(string source, StoryExpressionNode root)
        {
            Source = source;
            this.root = root;
        }

        /// <summary>获取表达式的原始文本。</summary>
        public string Source { get; }

        /// <summary>编译一段表达式文本；相同文本会复用缓存结果。</summary>
        /// <param name="source">形如 <c>flag.a &amp;&amp; var.n &gt;= 3</c> 的条件文本。</param>
        /// <returns>可重复求值的编译结果。</returns>
        public static StoryExpression Compile(string source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            lock (Cache)
            {
                if (Cache.TryGetValue(source, out StoryExpression cached)) return cached;
            }
            StoryExpression compiled = new StoryExpression(source, StoryExpressionCompiler.Compile(source));
            lock (Cache)
            {
                Cache[source] = compiled;
                return compiled;
            }
        }

        /// <summary>清空编译缓存；仅供测试与编辑器工具在重载配置后调用。</summary>
        public static void ClearCache()
        {
            lock (Cache) Cache.Clear();
        }

        /// <summary>在给定解析器下求值并返回原始值。</summary>
        public StoryValue Evaluate(IStoryVariableResolver variables, IStoryFunctionResolver functions = null)
        {
            return root.Evaluate(new StoryEvalScope(variables, functions, Source));
        }

        /// <summary>在给定解析器下求值并按条件语义折叠为布尔。</summary>
        public bool EvaluateBool(IStoryVariableResolver variables, IStoryFunctionResolver functions = null)
        {
            return Evaluate(variables, functions).AsBool();
        }

        /// <summary>收集表达式引用的全部标识符路径与函数名，供编辑期校验使用。</summary>
        /// <param name="identifiers">接收标识符路径的集合。</param>
        /// <param name="functions">接收函数名的集合。</param>
        public void CollectIdentifiers(ICollection<string> identifiers, ICollection<string> functions)
        {
            if (identifiers == null) throw new ArgumentNullException(nameof(identifiers));
            if (functions == null) throw new ArgumentNullException(nameof(functions));
            root.CollectIdentifiers(identifiers, functions);
        }

        /// <summary>
        /// 校验表达式可以编译，且其全部标识符的根命名空间被指定解析器承认。
        /// 资产导入与编辑器工具应调用该入口，把配置错误暴露在运行之前。
        /// </summary>
        /// <param name="source">待校验的表达式文本。</param>
        /// <param name="resolver">用于承认标识符根命名空间的解析器。</param>
        /// <param name="error">校验失败时返回错误描述。</param>
        /// <returns>表达式合法时返回 true。</returns>
        public static bool Validate(string source, IStoryVariableResolver resolver, out string error)
        {
            try
            {
                StoryExpression expression = Compile(source);
                if (resolver != null)
                {
                    List<string> identifiers = new List<string>();
                    List<string> functions = new List<string>();
                    expression.CollectIdentifiers(identifiers, functions);
                    for (int index = 0; index < identifiers.Count; index++)
                    {
                        string identifier = identifiers[index];
                        int dot = identifier.IndexOf('.');
                        string rootName = dot < 0 ? identifier : identifier.Substring(0, dot);
                        if (resolver.IsKnownRoot(rootName)) continue;
                        error = $"Story expression references unknown namespace '{rootName}' in identifier '{identifier}'.";
                        return false;
                    }
                }
                error = null;
                return true;
            }
            catch (StoryExpressionException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Source;
        }
    }
}
