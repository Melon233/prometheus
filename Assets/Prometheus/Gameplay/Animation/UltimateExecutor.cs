using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>保存终结技 AnimationLine、音效和特效配置，不持有 UltimateComponent。</summary>
    [Serializable]
    public sealed class UltimateExecutor
    {
        [SerializeField] private AnimationLine ultimateLine;
        [SerializeField] private AudioClip ultimateAudio;
        [SerializeField] private YefaVfx ultVfx;

        public AnimationSemantic Semantic => ultimateLine == null ? AnimationSemantic.None : ultimateLine.Semantic;
        public AudioClip AudioClip => ultimateAudio;
        public YefaVfx Vfx => ultVfx;

        /// <summary>收集终结技 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (ultimateLine != null) destination.Add(ultimateLine);
        }
    }
}
