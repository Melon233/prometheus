using System;
using System.Collections.Generic;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务变量的命名空间所有者，拥有 <c>quest</c> 根段。
    ///
    /// 它**不持有存储**：值统一存在共享的 <see cref="VariableStore"/> 里。
    /// 本类型只负责 <c>quest.*</c> 独有的两条语义：
    /// <list type="number">
    /// <item>「击败 5 只怪」的计数器未写入时读作 <b>0</b> 而不是解析失败——计数即变量（设计文档 §5.3）。</item>
    /// <item><c>quest.&lt;id&gt;.status</c> 是**现算值**，不入档，否则版本更新新增的任务在旧存档上会读到过期状态（铁律 Q5）。</item>
    /// </list>
    ///
    /// 与剧情变量之间不再是「互相注册为投影」。旧模型在图上是一个环——解析 <c>flag.x</c> 会走到剧情存储，
    /// 剧情存储的投影里又有本存储——因此必须靠重入守卫才不会无限递归。
    /// 改成按根段路由之后环消失了，守卫也随之删除。
    /// </summary>
    public sealed class QuestVariables : IStoryVariableResolver, IVariableNamespace
    {
        /// <summary>任务变量使用的根命名空间。</summary>
        public const string QuestRoot = "quest";

        /// <summary>推导值 <c>quest.&lt;id&gt;.status</c> 的末段名。</summary>
        public const string StatusLeaf = "status";

        /// <summary>本所有者拥有的根段。</summary>
        private static readonly string[] OwnedRoots = { QuestRoot };

        /// <summary>承载全部命名空间实际值的共享存储。</summary>
        private readonly VariableStore store;

        /// <summary>按任务标识解析当前状态；由任务系统在构造时注入。</summary>
        private readonly Func<string, QuestStatus?> statusResolver;

        /// <summary>创建任务变量命名空间并登记到存储。</summary>
        /// <param name="statusResolver">按任务标识解析状态的委托；返回空表示该任务未注册。</param>
        /// <param name="sharedStore">会话共享的变量存储；为空表示自建一个只含本命名空间的独立存储，供测试与独立工具使用。</param>
        public QuestVariables(Func<string, QuestStatus?> statusResolver, VariableStore sharedStore = null)
        {
            this.statusResolver = statusResolver ?? throw new ArgumentNullException(nameof(statusResolver));
            store = sharedStore ?? new VariableStore();
            store.Register(this);
        }

        /// <summary>获取承载本命名空间的共享存储；跨命名空间的读取一律经由它路由。</summary>
        public VariableStore Store => store;

        /// <inheritdoc />
        public IReadOnlyList<string> Roots => OwnedRoots;

        /// <inheritdoc />
        public bool AllowsWrite => true;

        /// <summary>任意变量发生变化时触发，参数为完整点分路径。</summary>
        /// <remarks>
        /// 转发共享存储的通知，因此订阅者同时能收到 <c>flag.*</c> 的变化。
        /// 这正是组合根不再需要「把剧情的变化转发给任务」那行接线的原因。
        /// </remarks>
        public event Action<string> Changed
        {
            add => store.Changed += value;
            remove => store.Changed -= value;
        }

        /// <summary>获取本命名空间当前已写入的变量数量；不含现算值与缺省值。</summary>
        public int Count => store.Count(this);

        /// <summary>按完整点分路径写入一个任务变量；只允许写 <c>quest.*</c>。</summary>
        /// <param name="path">形如 <c>quest.q_hunt.kills</c> 的完整路径。</param>
        /// <param name="value">要写入的值。</param>
        public void Set(string path, StoryValue value)
        {
            RequireOwnedPath(path);
            store.Set(path, value);
        }

        /// <summary>按完整点分路径读取一个任务变量；未写入的计数器读作零。</summary>
        /// <param name="path">完整点分路径。</param>
        public StoryValue Get(string path)
        {
            return store.TryResolve(path, out StoryValue value) ? value : StoryValue.None;
        }

        /// <summary>清除一个任务变量；任务重置时使用。</summary>
        /// <param name="path">完整点分路径。</param>
        public void Remove(string path)
        {
            store.Remove(path);
        }

        /// <summary>清除某个任务的全部变量；放弃并重置任务时使用。</summary>
        /// <param name="questId">任务稳定标识。</param>
        public void RemoveQuest(string questId)
        {
            store.RemoveByPrefix(string.Concat(QuestRoot, ".", questId, "."));
        }

        /// <inheritdoc />
        /// <remarks>解析走**整个存储**而不只是本命名空间，因此任务条件能直接读 <c>flag.*</c> 与 <c>var.*</c>。</remarks>
        public bool TryResolve(string path, out StoryValue value)
        {
            return store.TryResolve(path, out value);
        }

        /// <inheritdoc />
        public bool IsKnownRoot(string root)
        {
            return store.IsKnownRoot(root);
        }

        /// <inheritdoc />
        /// <remarks><c>quest.&lt;id&gt;.status</c> 现算而不入档，使版本更新新增的任务在旧存档上自然正确。</remarks>
        public bool TryResolveDerived(string path, out StoryValue value)
        {
            value = StoryValue.None;
            if (!path.EndsWith("." + StatusLeaf, StringComparison.Ordinal)) return false;
            int idStart = QuestRoot.Length + 1;
            int idLength = path.Length - idStart - StatusLeaf.Length - 1;
            if (idLength <= 0) return false;
            string questId = path.Substring(idStart, idLength);
            // 任务标识本身不允许再含点，否则无法把「任务名」与「变量名」区分开。
            if (questId.IndexOf('.') >= 0) return false;
            QuestStatus? status = statusResolver(questId);
            if (!status.HasValue) return false;
            value = new StoryValue(status.Value.ToString());
            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// 未写入的任务变量读作零：计数器不需要预先声明就能参与条件判断，
        /// 这正是「变量即计数器」模型能消掉事件去重表的前提。
        /// </remarks>
        public bool TryGetDefault(string path, out StoryValue value)
        {
            value = new StoryValue(0d);
            return true;
        }

        /// <summary>导出本命名空间已写入变量的副本，供存档序列化使用；不含现算值。</summary>
        public IReadOnlyDictionary<string, StoryValue> Capture()
        {
            return store.Capture(this);
        }

        /// <summary>用快照覆盖本命名空间的全部任务变量。</summary>
        /// <param name="snapshot">要写入的快照；为空表示只清空。</param>
        public void Restore(IReadOnlyDictionary<string, StoryValue> snapshot)
        {
            store.Restore(this, snapshot);
        }

        /// <summary>清空本命名空间的全部任务变量。</summary>
        public void Clear()
        {
            store.Clear(this);
        }

        /// <summary>构造一个任务变量的完整路径。</summary>
        /// <param name="questId">任务稳定标识。</param>
        /// <param name="name">变量末段名。</param>
        public static string Path(string questId, string name)
        {
            return string.Concat(QuestRoot, ".", questId, ".", name);
        }

        /// <summary>校验写入路径落在本命名空间内；其他命名空间对本入口是只读的。</summary>
        private static void RequireOwnedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Quest variable path cannot be empty.", nameof(path));
            if (!path.StartsWith(QuestRoot + ".", StringComparison.Ordinal)) throw new ArgumentException($"Quest variable path '{path}' must start with '{QuestRoot}.'; other namespaces are read-only here.", nameof(path));
        }
    }
}
