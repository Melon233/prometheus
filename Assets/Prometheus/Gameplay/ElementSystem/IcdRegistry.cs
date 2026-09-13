using System.Collections.Generic;

namespace Xuan.Prometheus.Elements
{
    /// <summary>
    /// 标识一个 ICD 组。
    ///
    /// 分组按 `(施加者, 天赋, 段落组)` 而不是按元素：同一角色的普攻与战技是两条独立的 ICD，
    /// 互相不影响彼此的计数与窗口。
    /// </summary>
    public readonly struct IcdKey : System.IEquatable<IcdKey>
    {
        /// <summary>施加者实体编号。</summary>
        public readonly int SourceEntityId;
        /// <summary>具体天赋标识。</summary>
        public readonly string TalentId;
        /// <summary>同一天赋内的独立段落组；使用共享策略时为 0。</summary>
        public readonly int GroupId;

        /// <summary>创建一个 ICD 分组键。</summary>
        public IcdKey(int sourceEntityId, string talentId, int groupId)
        {
            SourceEntityId = sourceEntityId;
            TalentId = talentId ?? string.Empty;
            GroupId = groupId;
        }

        /// <inheritdoc />
        public bool Equals(IcdKey other)
        {
            return SourceEntityId == other.SourceEntityId && GroupId == other.GroupId && TalentId == other.TalentId;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is IcdKey other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked { return (SourceEntityId * 397 ^ GroupId) * 397 ^ TalentId.GetHashCode(); }
        }
    }

    /// <summary>
    /// 元素附着的内部冷却注册表。
    ///
    /// 默认规则是 2.5 秒或 3 次命中先到者重置，因此第 1、4、7… 次命中附着生效。
    /// 时间由调用方以绝对秒数传入，本类不认识 Unity 时钟，可以脱离引擎单独验证。
    /// </summary>
    public sealed class IcdRegistry
    {
        /// <summary>保存一个 ICD 组的窗口起点与窗口内命中计数。</summary>
        private struct IcdWindow
        {
            /// <summary>窗口开始的绝对时间。</summary>
            public float StartTime;
            /// <summary>窗口内已经发生的命中次数。</summary>
            public int HitCount;
        }

        /// <summary>保存全部活动 ICD 窗口。</summary>
        private readonly Dictionary<IcdKey, IcdWindow> windows = new Dictionary<IcdKey, IcdWindow>();

        /// <summary>
        /// 判定本次命中是否应当写入附着，并推进该组的窗口状态。
        ///
        /// 判定与推进必须一次完成：分成两个方法会让调用方有机会「只查询不推进」，
        /// 从而把窗口计数与实际命中次数解耦，这类错误在实机里表现为反应频率忽高忽低，极难排查。
        /// </summary>
        /// <param name="key">ICD 分组键。</param>
        /// <param name="now">当前绝对时间（秒）。</param>
        /// <param name="windowSeconds">窗口时长；小于等于 0 表示每次命中必附着。</param>
        /// <param name="hitCount">窗口内允许的命中次数。</param>
        /// <returns>本次命中是否写入附着。</returns>
        public bool RegisterHit(in IcdKey key, float now, float windowSeconds, int hitCount)
        {
            if (windowSeconds <= 0f || hitCount <= 1) return true;

            if (!windows.TryGetValue(key, out IcdWindow window) || now - window.StartTime >= windowSeconds)
            {
                // 窗口尚未开始或已经超时：本次命中附着，并开启新窗口。
                windows[key] = new IcdWindow { StartTime = now, HitCount = 1 };
                return true;
            }

            // 计数在 1..hitCount 之间环绕，计数为 1 的那一次附着，
            // 因此生效序列是第 1、4、7… 次。注意环绕要一步到位：
            // 先归零再靠下一次递增，会让第 4 次落在 1 却仍被当作「未到窗口起点」而漏掉附着。
            window.HitCount++;
            if (window.HitCount > hitCount) window.HitCount = 1;
            windows[key] = window;
            return window.HitCount == 1;
        }

        /// <summary>移除某个施加者的全部 ICD 窗口，供实体回收时清理。</summary>
        public void RemoveSource(int sourceEntityId)
        {
            List<IcdKey> removing = null;
            foreach (KeyValuePair<IcdKey, IcdWindow> entry in windows)
            {
                if (entry.Key.SourceEntityId != sourceEntityId) continue;
                removing ??= new List<IcdKey>();
                removing.Add(entry.Key);
            }
            if (removing == null) return;
            for (int index = 0; index < removing.Count; index++) windows.Remove(removing[index]);
        }

        /// <summary>清空全部窗口。</summary>
        public void Clear()
        {
            windows.Clear();
        }
    }
}
