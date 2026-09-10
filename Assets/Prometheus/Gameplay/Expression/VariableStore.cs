using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Expression
{
    /// <summary>
    /// 会话内唯一的变量存储：所有命名空间的值都存在这里，按路径根段路由到各自的所有者。
    ///
    /// 它替代了「任务与剧情各持一个存储、互相注册为投影」的旧模型。旧模型有两个固有毛病：
    /// 互投影在图上是一个环，解析 <c>flag.x</c> 会走到对方、对方的投影里又有自己，
    /// 因此必须靠重入守卫才不会无限递归；而两个存储的字典、快照、清理代码是逐行重复的。
    ///
    /// 这里没有环：解析一次路径 = 取根段 + 一次字典查找 + 问所有者要现算值或缺省值。
    /// 谁都读得到谁，但没有人需要认识谁——所有权只由根段决定。
    ///
    /// 本类型不依赖任何 Unity 运行时，可以整体搬到服务端做权威求值。
    /// </summary>
    public sealed class VariableStore : IStoryVariableResolver
    {
        /// <summary>按完整点分路径索引的全部已写入值；路径自带根段，因此不需要按所有者分开存。</summary>
        private readonly Dictionary<string, StoryValue> values = new Dictionary<string, StoryValue>(StringComparer.Ordinal);

        /// <summary>根段到所有者的映射；一个所有者可以登记多个根段。</summary>
        private readonly Dictionary<string, IVariableNamespace> namespaces = new Dictionary<string, IVariableNamespace>(StringComparer.Ordinal);

        /// <summary>复用的键清扫缓冲，避免前缀删除时在遍历中修改集合，也避免每次分配。</summary>
        private readonly List<string> removalBuffer = new List<string>();

        /// <summary>任意变量发生变化时触发，参数为完整点分路径。</summary>
        /// <remarks>
        /// 订阅者拿到的是**全部**命名空间的变化，这正是想要的：任务系统只需订阅一次，
        /// 就能同时被 <c>quest.*</c> 与 <c>flag.*</c> 的变化弄脏，不必认识是谁写的。
        /// </remarks>
        public event Action<string> Changed;

        /// <summary>登记一个命名空间所有者；同一根段只能有一个所有者。</summary>
        /// <param name="owner">要登记的命名空间。</param>
        public void Register(IVariableNamespace owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            IReadOnlyList<string> roots = owner.Roots;
            if (roots == null || roots.Count == 0) throw new ArgumentException("A variable namespace must declare at least one root.", nameof(owner));
            for (int index = 0; index < roots.Count; index++)
            {
                string root = roots[index];
                if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A variable namespace root cannot be empty.", nameof(owner));
                if (namespaces.TryGetValue(root, out IVariableNamespace existing) && !ReferenceEquals(existing, owner))
                    throw new InvalidOperationException($"Variable root '{root}' is already owned by '{existing.GetType().Name}'.");
                namespaces[root] = owner;
            }
        }

        /// <inheritdoc />
        public bool TryResolve(string path, out StoryValue value)
        {
            value = StoryValue.None;
            if (string.IsNullOrEmpty(path)) return false;
            // 已写入的值优先：它是最具体的答案。
            if (values.TryGetValue(path, out value)) return true;
            if (!namespaces.TryGetValue(RootOf(path), out IVariableNamespace owner))
            {
                value = StoryValue.None;
                return false;
            }
            // 其次问所有者要现算值，最后才落到缺省值——顺序与旧的两个存储各自的解析顺序一致。
            if (owner.TryResolveDerived(path, out value)) return true;
            if (owner.TryGetDefault(path, out value)) return true;
            value = StoryValue.None;
            return false;
        }

        /// <inheritdoc />
        public bool IsKnownRoot(string root)
        {
            return !string.IsNullOrEmpty(root) && namespaces.ContainsKey(root);
        }

        /// <summary>按完整点分路径写入一个值；根段必须有所有者且该所有者接受写入。</summary>
        /// <param name="path">形如 <c>quest.q_hunt.kills</c> 的完整路径。</param>
        /// <param name="value">要写入的值。</param>
        public void Set(string path, StoryValue value)
        {
            IVariableNamespace owner = RequireOwner(path);
            if (!owner.AllowsWrite) throw new InvalidOperationException($"Variable namespace '{RootOf(path)}' is read-only; '{path}' cannot be written.");
            // 值没变就不广播：条件重算由变化驱动，重复广播会让帧末 flush 多跑无谓的轮次。
            if (values.TryGetValue(path, out StoryValue existing) && existing.Equals(value)) return;
            values[path] = value;
            Changed?.Invoke(path);
        }

        /// <summary>清除一个已写入的值；未写入过时是空操作。</summary>
        /// <param name="path">完整点分路径。</param>
        /// <returns>确实移除了一个值时返回 true。</returns>
        public bool Remove(string path)
        {
            if (string.IsNullOrEmpty(path) || !values.Remove(path)) return false;
            Changed?.Invoke(path);
            return true;
        }

        /// <summary>清除全部以指定前缀开头的值；任务放弃重置这类整组清理使用。</summary>
        /// <param name="prefix">路径前缀，例如 <c>quest.q_hunt.</c>。</param>
        public void RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            removalBuffer.Clear();
            foreach (KeyValuePair<string, StoryValue> pair in values)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) removalBuffer.Add(pair.Key);
            }
            for (int index = 0; index < removalBuffer.Count; index++)
            {
                values.Remove(removalBuffer[index]);
                Changed?.Invoke(removalBuffer[index]);
            }
            removalBuffer.Clear();
        }

        /// <summary>统计指定所有者名下已写入的值数量；不含现算值与缺省值。</summary>
        /// <param name="owner">目标命名空间。</param>
        public int Count(IVariableNamespace owner)
        {
            int count = 0;
            foreach (KeyValuePair<string, StoryValue> pair in values)
            {
                if (Owns(owner, pair.Key)) count++;
            }
            return count;
        }

        /// <summary>
        /// 导出指定所有者名下的全部已写入值，供存档序列化使用。
        /// 按所有者切片而不是整库导出，因此各系统仍然各存各的档，读一份档而不读另一份时每份仍然自洽。
        /// </summary>
        /// <param name="owner">目标命名空间。</param>
        public IReadOnlyDictionary<string, StoryValue> Capture(IVariableNamespace owner)
        {
            Dictionary<string, StoryValue> snapshot = new Dictionary<string, StoryValue>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, StoryValue> pair in values)
            {
                if (Owns(owner, pair.Key)) snapshot[pair.Key] = pair.Value;
            }
            return snapshot;
        }

        /// <summary>用快照覆盖指定所有者名下的全部值；其他命名空间不受影响。</summary>
        /// <param name="owner">目标命名空间。</param>
        /// <param name="snapshot">要写入的快照；为空表示只清空。</param>
        public void Restore(IVariableNamespace owner, IReadOnlyDictionary<string, StoryValue> snapshot)
        {
            Clear(owner);
            if (snapshot == null) return;
            foreach (KeyValuePair<string, StoryValue> pair in snapshot)
            {
                if (!Owns(owner, pair.Key)) throw new InvalidOperationException($"Snapshot path '{pair.Key}' does not belong to the restoring namespace.");
                values[pair.Key] = pair.Value;
                Changed?.Invoke(pair.Key);
            }
        }

        /// <summary>清空指定所有者名下的全部值；登记关系保留。</summary>
        /// <param name="owner">目标命名空间。</param>
        public void Clear(IVariableNamespace owner)
        {
            removalBuffer.Clear();
            foreach (KeyValuePair<string, StoryValue> pair in values)
            {
                if (Owns(owner, pair.Key)) removalBuffer.Add(pair.Key);
            }
            for (int index = 0; index < removalBuffer.Count; index++)
            {
                values.Remove(removalBuffer[index]);
                Changed?.Invoke(removalBuffer[index]);
            }
            removalBuffer.Clear();
        }

        /// <summary>取出一条路径的根段，即第一个点之前的部分。</summary>
        /// <param name="path">完整点分路径。</param>
        public static string RootOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int separator = path.IndexOf('.');
            return separator < 0 ? path : path.Substring(0, separator);
        }

        /// <summary>判断一条路径是否归指定所有者。</summary>
        private bool Owns(IVariableNamespace owner, string path)
        {
            return namespaces.TryGetValue(RootOf(path), out IVariableNamespace resolved) && ReferenceEquals(resolved, owner);
        }

        /// <summary>取得一条路径的所有者；没有所有者属于配置错误，不做兜底。</summary>
        private IVariableNamespace RequireOwner(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Variable path cannot be empty.", nameof(path));
            string root = RootOf(path);
            if (!namespaces.TryGetValue(root, out IVariableNamespace owner)) throw new InvalidOperationException($"Variable root '{root}' has no registered namespace; '{path}' cannot be written.");
            return owner;
        }
    }
}
