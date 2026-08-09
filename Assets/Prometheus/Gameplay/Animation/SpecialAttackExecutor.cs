using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>保存特殊攻击 AnimationLine、音效和特效配置，不持有 SpecialAttackComponent。</summary>
    [Serializable]
    public sealed class SpecialAttackExecutor
    {
        [SerializeField] private AnimationLine specialAttackLine;
        [SerializeField] private AudioClip specialAttackAudio;
        [SerializeField] private YefaVfx specialAttackVfx;

        public AnimationSemantic Semantic => specialAttackLine == null ? AnimationSemantic.None : specialAttackLine.Semantic;
        public AudioClip AudioClip => specialAttackAudio;
        public YefaVfx Vfx => specialAttackVfx;

        /// <summary>收集特殊攻击 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (specialAttackLine != null) destination.Add(specialAttackLine);
        }
    }
}
