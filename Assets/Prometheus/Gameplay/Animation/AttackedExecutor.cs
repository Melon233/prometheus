using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>保存单段或双段受击 AnimationLine；受击音效由 AnimationLine 的 FMOD 绑定负责。</summary>
    [Serializable]
    public sealed class AttackedExecutor
    {
        [SerializeField] private AnimationLine attackedLine;
        [SerializeField] private AnimationLine nextAttackedLine;

        public AnimationSemantic Semantic => attackedLine == null ? AnimationSemantic.None : attackedLine.Semantic;
        public bool HasRecoveryAnimation => nextAttackedLine != null;
        public AnimationSemantic RecoverySemantic => nextAttackedLine == null ? AnimationSemantic.None : nextAttackedLine.Semantic;
        /// <summary>收集受击主体与可选恢复 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (attackedLine != null) destination.Add(attackedLine);
            if (nextAttackedLine != null) destination.Add(nextAttackedLine);
        }
    }
}
