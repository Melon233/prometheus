using System;
using System.Collections.Generic;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 剧情变量与旗标的命名空间所有者，拥有 <c>flag</c> 与 <c>var</c> 两个根段。
    ///
    /// 它**不持有存储**：值统一存在共享的 <see cref="VariableStore"/> 里。
    /// 本类型只做三件事——声明拥有哪些根、给出 <c>flag.*</c> 的缺省值、
    /// 以及提供一层写短名的便利（DSL 里写 <c>coin</c> 等价于 <c>var.coin</c>）。
    ///
    /// 与任务变量之间不再是「互相注册为投影」：两边写进同一个存储、按根段各管各的，
    /// 因此谁都读得到谁，却没有环，也就不需要重入守卫。
    ///
    /// 该类型不依赖任何 Unity 运行时，可以整体搬到服务端做权威求值，也可以在 EditMode 下完整测试。
    /// </summary>
    public sealed class StoryVariables : IStoryVariableResolver, IVariableNamespace
    {
        /// <summary>布尔旗标使用的根命名空间。</summary>
        public const string FlagRoot = "flag";

        /// <summary>剧情变量使用的根命名空间。</summary>
        public const string VariableRoot = "var";

        /// <summary>本所有者拥有的根段。</summary>
        private static readonly string[] OwnedRoots = { FlagRoot, VariableRoot };

        /// <summary>承载全部命名空间实际值的共享存储。</summary>
        private readonly VariableStore store;

        /// <summary>
        /// 创建剧情变量命名空间并登记到存储。
        /// </summary>
        /// <param name="sharedStore">会话共享的变量存储；为空表示自建一个只含本命名空间的独立存储，供测试与独立工具使用。</param>
        public StoryVariables(VariableStore sharedStore = null)
        {
            store = sharedStore ?? new VariableStore();
            store.Register(this);
        }

        /// <summary>获取承载本命名空间的共享存储；跨命名空间的读取一律经由它路由。</summary>
        public VariableStore Store => store;

        /// <inheritdoc />
        public IReadOnlyList<string> Roots => OwnedRoots;

        /// <inheritdoc />
        public bool AllowsWrite => true;

        /// <summary>任意变量或旗标发生变化时触发，参数为完整点分路径。</summary>
        /// <remarks>转发共享存储的通知，因此订阅者同样能收到其他命名空间的变化。</remarks>
        public event Action<string> Changed
        {
            add => store.Changed += value;
            remove => store.Changed -= value;
        }

        /// <summary>获取本命名空间当前已写入的变量数量。</summary>
        public int Count => store.Count(this);

        /// <summary>写入一个布尔旗标。</summary>
        /// <param name="name">不含 <c>flag.</c> 前缀的旗标名；含前缀时前缀会被保留使用。</param>
        /// <param name="value">要写入的旗标值。</param>
        public void SetFlag(string name, bool value)
        {
            Set(Qualify(name, FlagRoot), new StoryValue(value));
        }

        /// <summary>读取一个布尔旗标；未写入过的旗标为 false。</summary>
        /// <param name="name">不含 <c>flag.</c> 前缀的旗标名。</param>
        public bool GetFlag(string name)
        {
            return store.TryResolve(Qualify(name, FlagRoot), out StoryValue value) && value.AsBool();
        }

        /// <summary>按完整点分路径写入一个变量值；只允许写本命名空间拥有的根段。</summary>
        /// <param name="path">形如 <c>var.coin</c> 或 <c>flag.seen_intro</c> 的完整路径；不含点时按 <c>var.</c> 补全。</param>
        /// <param name="value">要写入的值。</param>
        public void Set(string path, StoryValue value)
        {
            string qualified = Qualify(path, VariableRoot);
            RequireOwnedPath(qualified);
            store.Set(qualified, value);
        }

        /// <summary>按完整点分路径读取一个变量值；不存在时返回空值。</summary>
        /// <param name="path">完整路径；不含点时按 <c>var.</c> 补全。</param>
        public StoryValue Get(string path)
        {
            return store.TryResolve(Qualify(path, VariableRoot), out StoryValue value) ? value : StoryValue.None;
        }

        /// <inheritdoc />
        /// <remarks>解析走**整个存储**而不只是本命名空间，因此剧情条件能直接读 <c>quest.*</c>。</remarks>
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
        /// <remarks>剧情变量全部是实存值，没有现算值。</remarks>
        public bool TryResolveDerived(string path, out StoryValue value)
        {
            value = StoryValue.None;
            return false;
        }

        /// <inheritdoc />
        /// <remarks>未写入过的旗标按 false 处理，使「尚未发生的事」不需要预先声明即可参与条件判断。</remarks>
        public bool TryGetDefault(string path, out StoryValue value)
        {
            if (path.StartsWith(FlagRoot + ".", StringComparison.Ordinal))
            {
                value = StoryValue.False;
                return true;
            }
            value = StoryValue.None;
            return false;
        }

        /// <summary>导出本命名空间已写入变量的副本，供存档序列化使用。</summary>
        public IReadOnlyDictionary<string, StoryValue> Capture()
        {
            return store.Capture(this);
        }

        /// <summary>用快照覆盖本命名空间的全部变量。</summary>
        /// <param name="snapshot">要写入的快照；为空表示只清空。</param>
        public void Restore(IReadOnlyDictionary<string, StoryValue> snapshot)
        {
            store.Restore(this, snapshot);
        }

        /// <summary>清空本命名空间的全部变量。</summary>
        public void Clear()
        {
            store.Clear(this);
        }

        /// <summary>为缺少根命名空间的名称补上默认根，使 DSL 可以写短名。</summary>
        private static string Qualify(string path, string defaultRoot)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Story variable path cannot be empty.", nameof(path));
            return path.IndexOf('.') >= 0 ? path : string.Concat(defaultRoot, ".", path);
        }

        /// <summary>校验写入路径落在本命名空间拥有的根段内；其他命名空间对本入口是只读的。</summary>
        private static void RequireOwnedPath(string path)
        {
            if (path.StartsWith(FlagRoot + ".", StringComparison.Ordinal) || path.StartsWith(VariableRoot + ".", StringComparison.Ordinal)) return;
            throw new ArgumentException($"Story variable path '{path}' must start with '{FlagRoot}.' or '{VariableRoot}.'.", nameof(path));
        }
    }
}
