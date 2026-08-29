using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>保存待机动画配置，不持有任何实体运行态。</summary>
    [Serializable]
    public sealed class IdleExecutor
    {
        [SerializeField] private AnimationLine idleLine;

        /// <summary>获取待机动画的稳定语义。</summary>
        public AnimationSemantic Semantic => idleLine == null ? AnimationSemantic.None : idleLine.Semantic;

        /// <summary>收集待机 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (idleLine != null) destination.Add(idleLine);
        }
    }
}
