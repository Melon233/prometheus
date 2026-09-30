using System.Collections.Generic;

namespace Xuan.Prometheus.Expression
{
    /// <summary>
    /// 一个变量命名空间的所有者。
    ///
    /// 它**不持有存储**——值统一存在 <see cref="VariableStore"/> 里，因为路径自带根段、天然唯一。
    /// 所有者只回答本命名空间独有的三件事：哪些根段归我、未存值时怎么现算、未存值又算不出时的缺省是什么。
    ///
    /// 这个划分替代了原先「两个存储互相注册为投影」的模型。互投影在图上是一个环，
    /// 需要重入守卫才不会无限递归；而按根段路由只是一次字典查找，图上没有环。
    /// </summary>
    public interface IVariableNamespace
    {
        /// <summary>本命名空间拥有的根段；一个所有者可以拥有多个根（例如剧情同时拥有 flag 与 var）。</summary>
        IReadOnlyList<string> Roots { get; }

        /// <summary>本命名空间是否接受写入；只读命名空间（例如将来投影进来的背包数量）返回 false。</summary>
        bool AllowsWrite { get; }

        /// <summary>
        /// 解析一个**现算值**：不入档、每次按当前状态推导出来的值。
        /// 典型例子是 <c>quest.&lt;id&gt;.status</c>——存进字典的话，版本更新新增的任务在旧存档上就会读到过期状态。
        /// </summary>
        /// <param name="path">完整点分路径，其根段必然属于本命名空间。</param>
        /// <param name="value">能推导时返回结果。</param>
        /// <returns>该路径是本命名空间的现算值时返回 true。</returns>
        bool TryResolveDerived(string path, out StoryValue value);

        /// <summary>
        /// 给出一个从未写入过的路径的缺省值。
        /// 这条决定了「尚未发生的事」不需要预先声明就能参与条件判断：
        /// <c>flag.*</c> 缺省为假，<c>quest.*</c> 缺省为零（计数即变量）。
        /// </summary>
        /// <param name="path">完整点分路径，其根段必然属于本命名空间。</param>
        /// <param name="value">存在缺省值时返回它。</param>
        /// <returns>本命名空间为该路径定义了缺省值时返回 true。</returns>
        bool TryGetDefault(string path, out StoryValue value);
    }
}
