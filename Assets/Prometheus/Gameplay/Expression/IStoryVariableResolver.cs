using System.Collections.Generic;

namespace Xuan.Prometheus.Expression
{
    /// <summary>为剧情表达式提供标识符取值能力；实现必须是纯查询，不得产生副作用。</summary>
    public interface IStoryVariableResolver
    {
        /// <summary>按点分路径读取一个标识符的值。</summary>
        /// <param name="path">形如 <c>flag.seen_intro</c> 的完整标识符路径。</param>
        /// <param name="value">解析成功时返回对应值。</param>
        /// <returns>路径存在时返回 true；不存在时返回 false 并由调用方决定容错策略。</returns>
        bool TryResolve(string path, out StoryValue value);

        /// <summary>判断一个标识符根命名空间是否被当前解析器承认，供编辑期校验使用。</summary>
        /// <param name="root">标识符的第一段，例如 <c>flag</c>、<c>var</c>、<c>quest</c>。</param>
        /// <returns>该命名空间被承认时返回 true。</returns>
        bool IsKnownRoot(string root);
    }

    /// <summary>为剧情表达式提供内置函数调用能力。</summary>
    public interface IStoryFunctionResolver
    {
        /// <summary>按函数名与实参求值一个内置函数。</summary>
        /// <param name="name">函数的完整点分名称，例如 <c>item.count</c>。</param>
        /// <param name="arguments">已求值的实参列表。</param>
        /// <param name="value">调用成功时返回结果。</param>
        /// <returns>函数存在且调用成功时返回 true。</returns>
        bool TryInvoke(string name, IReadOnlyList<StoryValue> arguments, out StoryValue value);
    }
}
