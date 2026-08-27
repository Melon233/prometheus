using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>描述 MixDuration 矩阵中的一个有向覆盖单元格；From 与 To 的顺序不同会被视为不同的过渡。</summary>
    [Serializable]
    public sealed class AnimationMixDurationEntry
    {
        [SerializeField, Tooltip("过渡开始时正在播放的动画语义；None 表示 Setup Pose。")]
        private AnimationSemantic from;

        [SerializeField, Tooltip("过渡结束时需要播放的动画语义；None 表示 Setup Pose。")]
        private AnimationSemantic to;

        [SerializeField, Min(0f), Tooltip("该动画方向组合使用的混合时长，单位为秒。")]
        private float duration = AnimationMixDurationMatrix.FallbackDuration;

        /// <summary>创建一个可序列化的有向 MixDuration 覆盖单元格。</summary>
        public AnimationMixDurationEntry(AnimationSemantic from, AnimationSemantic to, float duration)
        {
            this.from = from;
            this.to = to;
            this.duration = Mathf.Max(0f, duration);
        }

        /// <summary>获取过渡源动画语义。</summary>
        public AnimationSemantic From => from;

        /// <summary>获取过渡目标动画语义。</summary>
        public AnimationSemantic To => to;

        /// <summary>获取经过非负数约束的混合时长。</summary>
        public float Duration => Mathf.Max(0f, duration);

        /// <summary>更新已有单元格的混合时长，并阻止负数配置进入运行时。</summary>
        internal void SetDuration(float value)
        {
            duration = Mathf.Max(0f, value);
        }

        /// <summary>规范化序列化数据，修复手工编辑 YAML 时可能写入的负数。</summary>
        internal void Normalize()
        {
            duration = Mathf.Max(0f, duration);
        }
    }

    /// <summary>保存动画语义之间的有向 MixDuration 矩阵；未显式覆盖的任意单元格统一返回默认值。</summary>
    [Serializable]
    public sealed class AnimationMixDurationMatrix : ISerializationCallbackReceiver
    {
        /// <summary>定义新矩阵和旧资源缺少矩阵字段时共同使用的安全默认混合时长。</summary>
        public const float FallbackDuration = 0.2f;

        [SerializeField, Min(0f), Tooltip("所有未添加覆盖项的动画过渡使用该混合时长，单位为秒。")]
        private float defaultDuration = FallbackDuration;

        [SerializeField, Tooltip("矩阵的稀疏覆盖单元格；完整矩阵请通过 AnimationLibrary Inspector 中的按钮打开。")]
        private List<AnimationMixDurationEntry> overrides = new List<AnimationMixDurationEntry>();

        [NonSerialized] private Dictionary<long, float> overrideIndex;

        /// <summary>获取或设置所有未覆盖单元格的默认混合时长。</summary>
        public float DefaultDuration
        {
            get => Mathf.Max(0f, defaultDuration);
            set
            {
                defaultDuration = Mathf.Max(0f, value);
                InvalidateIndex();
            }
        }

        /// <summary>获取当前显式覆盖的矩阵单元格数量。</summary>
        public int OverrideCount => overrides == null ? 0 : overrides.Count;

        /// <summary>读取指定有向动画组合的 MixDuration；没有覆盖项时返回矩阵默认值。</summary>
        public float GetMixDuration(AnimationSemantic from, AnimationSemantic to)
        {
            EnsureIndex();
            return overrideIndex.TryGetValue(CreateKey(from, to), out float duration) ? duration : DefaultDuration;
        }

        /// <summary>尝试读取指定矩阵单元格的显式覆盖值，用于编辑器区分覆盖值与默认值。</summary>
        public bool TryGetOverride(AnimationSemantic from, AnimationSemantic to, out float duration)
        {
            EnsureIndex();
            return overrideIndex.TryGetValue(CreateKey(from, to), out duration);
        }

        /// <summary>新增或更新一个有向矩阵单元格；反向组合不会被同步修改。</summary>
        public void SetMixDuration(AnimationSemantic from, AnimationSemantic to, float duration)
        {
            EnsureOverrides();
            float normalizedDuration = Mathf.Max(0f, duration);
            for (int index = overrides.Count - 1; index >= 0; index--)
            {
                AnimationMixDurationEntry entry = overrides[index];
                if (entry == null || entry.From != from || entry.To != to) continue;
                entry.SetDuration(normalizedDuration);
                RemoveDuplicateEntries(from, to, index);
                InvalidateIndex();
                return;
            }
            overrides.Add(new AnimationMixDurationEntry(from, to, normalizedDuration));
            InvalidateIndex();
        }

        /// <summary>删除一个有向矩阵单元格的全部显式覆盖，使该组合重新使用默认时长。</summary>
        public bool RemoveMixDuration(AnimationSemantic from, AnimationSemantic to)
        {
            if (overrides == null) return false;
            bool removed = false;
            for (int index = overrides.Count - 1; index >= 0; index--)
            {
                AnimationMixDurationEntry entry = overrides[index];
                if (entry == null || entry.From != from || entry.To != to) continue;
                overrides.RemoveAt(index);
                removed = true;
            }
            if (removed) InvalidateIndex();
            return removed;
        }

        /// <summary>清除全部显式覆盖，使整个矩阵恢复为统一默认时长。</summary>
        public void ClearOverrides()
        {
            EnsureOverrides();
            overrides.Clear();
            InvalidateIndex();
        }

        /// <summary>规范化默认值、空项、负数和重复单元格，并保留重复组合中最后配置的值。</summary>
        public void Normalize()
        {
            defaultDuration = Mathf.Max(0f, defaultDuration);
            EnsureOverrides();
            HashSet<long> retainedKeys = new HashSet<long>();
            for (int index = overrides.Count - 1; index >= 0; index--)
            {
                AnimationMixDurationEntry entry = overrides[index];
                if (entry == null)
                {
                    overrides.RemoveAt(index);
                    continue;
                }
                entry.Normalize();
                if (retainedKeys.Add(CreateKey(entry.From, entry.To))) continue;
                overrides.RemoveAt(index);
            }
            InvalidateIndex();
        }

        /// <summary>序列化前不创建运行时索引，保证资源只保存可配置数据。</summary>
        public void OnBeforeSerialize()
        {
        }

        /// <summary>反序列化后丢弃非序列化索引，使首次查询能够读取最新配置。</summary>
        public void OnAfterDeserialize()
        {
            InvalidateIndex();
        }

        /// <summary>确保覆盖列表在旧资源缺少字段时仍可安全使用。</summary>
        private void EnsureOverrides()
        {
            if (overrides == null) overrides = new List<AnimationMixDurationEntry>();
        }

        /// <summary>按最后配置优先的规则建立常数时间查询索引。</summary>
        private void EnsureIndex()
        {
            if (overrideIndex != null) return;
            overrideIndex = new Dictionary<long, float>();
            if (overrides == null) return;
            for (int index = 0; index < overrides.Count; index++)
            {
                AnimationMixDurationEntry entry = overrides[index];
                if (entry == null) continue;
                overrideIndex[CreateKey(entry.From, entry.To)] = entry.Duration;
            }
        }

        /// <summary>删除指定保留位置之外的重复组合，避免 Inspector 和运行时对同一单元格产生歧义。</summary>
        private void RemoveDuplicateEntries(AnimationSemantic from, AnimationSemantic to, int retainedIndex)
        {
            for (int index = overrides.Count - 1; index >= 0; index--)
            {
                if (index == retainedIndex) continue;
                AnimationMixDurationEntry entry = overrides[index];
                if (entry == null || entry.From != from || entry.To != to) continue;
                overrides.RemoveAt(index);
                if (index < retainedIndex) retainedIndex--;
            }
        }

        /// <summary>将两个有符号枚举值无冲突地组合成字典键。</summary>
        private static long CreateKey(AnimationSemantic from, AnimationSemantic to)
        {
            return ((long)(int)from << 32) | (uint)(int)to;
        }

        /// <summary>使运行时查询索引失效，下一次读取时会按最新序列化配置重建。</summary>
        private void InvalidateIndex()
        {
            overrideIndex = null;
        }
    }
}
