using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>保存前向和后向闪避 AnimationLine 配置，不管理闪避状态。</summary>
    [Serializable]
    public sealed class DodgeExecutor
    {
        [SerializeField] private AnimationLine dodgeFrontLine;
        [SerializeField] private AnimationLine dodgeBackLine;

        /// <summary>根据角色是否正在移动返回闪避动画语义。</summary>
        public AnimationSemantic GetSemantic(bool isMoving)
        {
            AnimationLine selectedLine = isMoving ? dodgeFrontLine : dodgeBackLine;
            return selectedLine == null ? AnimationSemantic.None : selectedLine.Semantic;
        }

        /// <summary>收集全部闪避 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (dodgeFrontLine != null) destination.Add(dodgeFrontLine);
            if (dodgeBackLine != null) destination.Add(dodgeBackLine);
        }
    }
}
