using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>定义空中移动表现阶段。</summary>
    public enum AirMoveState
    {
        Jump,
        Fall,
        Land
    }

    /// <summary>保存跳跃、上升、下落和落地 AnimationLine 配置，不持有移动状态。</summary>
    [Serializable]
    public sealed class AirMoveExecutor
    {
        [SerializeField] private AnimationLine jumpLine;
        [SerializeField] private AnimationLine riseLine;
        [SerializeField] private AnimationLine fallLine;
        [SerializeField] private AnimationLine landLine;

        /// <summary>获取起跳动画语义。</summary>
        public AnimationSemantic JumpSemantic => jumpLine == null ? AnimationSemantic.None : jumpLine.Semantic;

        /// <summary>获取上升循环动画语义。</summary>
        public AnimationSemantic RiseSemantic => riseLine == null ? AnimationSemantic.None : riseLine.Semantic;

        /// <summary>获取下落循环动画语义。</summary>
        public AnimationSemantic FallSemantic => fallLine == null ? AnimationSemantic.None : fallLine.Semantic;

        /// <summary>获取落地动画语义。</summary>
        public AnimationSemantic LandSemantic => landLine == null ? AnimationSemantic.None : landLine.Semantic;

        /// <summary>收集全部空中阶段 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (jumpLine != null) destination.Add(jumpLine);
            if (riseLine != null) destination.Add(riseLine);
            if (fallLine != null) destination.Add(fallLine);
            if (landLine != null) destination.Add(landLine);
        }
    }
}
