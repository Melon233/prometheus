using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>保存终结技 AnimationLine 和特效配置；音效由 AnimationLine 的 FMOD 绑定负责。</summary>
    [Serializable]
    public sealed class UltimateExecutor
    {
        [SerializeField] private AnimationLine ultimateLine;
        [SerializeField] private YefaVfx ultVfx;

        public AnimationSemantic Semantic => ultimateLine == null ? AnimationSemantic.None : ultimateLine.Semantic;
        public YefaVfx Vfx => ultVfx;

        /// <summary>收集终结技 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (ultimateLine != null) destination.Add(ultimateLine);
        }
    }
}
