using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 剧情变量与旗标的运行时存储。
    /// 该类型不依赖任何 Unity 运行时，可以整体搬到服务端做权威求值，也可以在 EditMode 下完整测试。
    /// </summary>
    public sealed class StoryVariables : IStoryVariableResolver
    {
        /// <summary>布尔旗标使用的根命名空间。</summary>
        public const string FlagRoot = "flag";

        /// <summary>剧情变量使用的根命名空间。</summary>
        public const string VariableRoot = "var";

        /// <summary>保存按完整点分路径索引的变量值。</summary>
        private readonly Dictionary<string, StoryValue> values = new Dictionary<string, StoryValue>(StringComparer.Ordinal);

        /// <summary>保存外部只读投影解析器，例如任务状态与背包数量。</summary>
        private readonly List<IStoryVariableResolver> projections = new List<IStoryVariableResolver>();

        /// <summary>任意变量或旗标发生变化时触发，参数为完整点分路径。</summary>
        public event Action<string> Changed;

        /// <summary>获取当前已写入的变量数量。</summary>
        public int Count => values.Count;

        /// <summary>注册一个外部只读投影解析器；投影只参与读取，不接受写入。</summary>
        public void AddProjection(IStoryVariableResolver projection)
        {
            if (projection == null) throw new ArgumentNullException(nameof(projection));
            if (!projections.Contains(projection)) projections.Add(projection);
        }

        /// <summary>移除一个外部只读投影解析器。</summary>
        public void RemoveProjection(IStoryVariableResolver projection)
        {
            projections.Remove(projection);
        }

        /// <summary>写入一个布尔旗标。</summary>
        /// <param name="name">不含 <c>flag.</c> 前缀的旗标名；含前缀时前缀会被保留使用。</param>
        public void SetFlag(string name, bool value)
        {
            Set(Qualify(name, FlagRoot), new StoryValue(value));
        }

        /// <summary>读取一个布尔旗标；未写入过的旗标为 false。</summary>
        public bool GetFlag(string name)
        {
            return values.TryGetValue(Qualify(name, FlagRoot), out StoryValue value) && value.AsBool();
        }

        /// <summary>按完整点分路径写入一个变量值。</summary>
        /// <param name="path">形如 <c>var.coin</c> 或 <c>flag.seen_intro</c> 的完整路径。</param>
        public void Set(string path, StoryValue value)
        {
            string qualified = Qualify(path, VariableRoot);
            if (values.TryGetValue(qualified, out StoryValue existing) && existing.Equals(value)) return;
            values[qualified] = value;
            Changed?.Invoke(qualified);
        }

        /// <summary>按完整点分路径读取一个变量值；不存在时返回空值。</summary>
        public StoryValue Get(string path)
        {
            return TryResolve(Qualify(path, VariableRoot), out StoryValue value) ? value : StoryValue.None;
        }

        /// <inheritdoc />
        public bool TryResolve(string path, out StoryValue value)
        {
            if (string.IsNullOrEmpty(path))
            {
                value = StoryValue.None;
                return false;
            }
            if (values.TryGetValue(path, out value)) return true;
            for (int index = 0; index < projections.Count; index++)
            {
                if (projections[index].TryResolve(path, out value)) return true;
            }
            // 未写入过的旗标按 false 处理，使「尚未发生的事」不需要预先声明即可参与条件判断。
            if (path.StartsWith(FlagRoot + ".", StringComparison.Ordinal))
            {
                value = StoryValue.False;
                return true;
            }
            value = StoryValue.None;
            return false;
        }

        /// <inheritdoc />
        public bool IsKnownRoot(string root)
        {
            if (string.Equals(root, FlagRoot, StringComparison.Ordinal) || string.Equals(root, VariableRoot, StringComparison.Ordinal)) return true;
            for (int index = 0; index < projections.Count; index++)
            {
                if (projections[index].IsKnownRoot(root)) return true;
            }
            return false;
        }

        /// <summary>导出全部已写入变量的副本，供存档序列化使用；不包含外部投影。</summary>
        public IReadOnlyDictionary<string, StoryValue> Capture()
        {
            return new Dictionary<string, StoryValue>(values, StringComparer.Ordinal);
        }

        /// <summary>用快照覆盖当前全部变量。</summary>
        public void Restore(IReadOnlyDictionary<string, StoryValue> snapshot)
        {
            values.Clear();
            if (snapshot == null) return;
            foreach (KeyValuePair<string, StoryValue> pair in snapshot) values[pair.Key] = pair.Value;
        }

        /// <summary>清空全部变量，保留已注册的投影。</summary>
        public void Clear()
        {
            values.Clear();
        }

        /// <summary>为缺少根命名空间的名称补上默认根，使 DSL 可以写短名。</summary>
        private static string Qualify(string path, string defaultRoot)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Story variable path cannot be empty.", nameof(path));
            return path.IndexOf('.') >= 0 ? path : string.Concat(defaultRoot, ".", path);
        }
    }
}
