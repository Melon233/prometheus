using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    /// <summary>保存地面移动动画配置，不负责移动状态和播放生命周期。</summary>
    [Serializable]
    public sealed class GroundMoveExecutor
    {
        [SerializeField] private AnimationLine walkLine;
        [SerializeField] private AnimationLine runLine;
        [SerializeField] private AnimationLine sprintLine;

        /// <summary>根据移动模式返回角色动画库中配置的稳定动画语义。</summary>
        public AnimationSemantic GetSemantic(MoveMode moveMode)
        {
            switch (moveMode)
            {
                case MoveMode.Walk: return walkLine == null ? AnimationSemantic.None : walkLine.Semantic;
                case MoveMode.Sprint: return sprintLine == null ? AnimationSemantic.None : sprintLine.Semantic;
                default: return runLine == null ? AnimationSemantic.None : runLine.Semantic;
            }
        }

        /// <summary>收集全部地面移动 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (walkLine != null) destination.Add(walkLine);
            if (runLine != null) destination.Add(runLine);
            if (sprintLine != null) destination.Add(sprintLine);
        }
    }
}
