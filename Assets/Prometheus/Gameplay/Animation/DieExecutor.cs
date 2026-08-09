using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>保存死亡 AnimationLine 配置，不负责实体回收。</summary>
    [Serializable]
    public sealed class DieExecutor
    {
        [SerializeField] private AnimationLine dieLine;

        /// <summary>获取死亡动画语义。</summary>
        public AnimationSemantic Semantic => dieLine == null ? AnimationSemantic.None : dieLine.Semantic;

        /// <summary>收集死亡 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (dieLine != null) destination.Add(dieLine);
        }
    }
}
