using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>保存技能起手、主体 AnimationLine 和特效配置；音效由 AnimationLine 的 FMOD 绑定负责。</summary>
    [Serializable]
    public sealed class SkillExecutor
    {
        [SerializeField] private AnimationLine skillStartLine;
        [SerializeField] private AnimationLine skillLine;
        [SerializeField] private YefaVfx skillVfx;

        public AnimationSemantic StartSemantic => skillStartLine == null ? AnimationSemantic.None : skillStartLine.Semantic;
        public AnimationSemantic Semantic => skillLine == null ? AnimationSemantic.None : skillLine.Semantic;
        public YefaVfx Vfx => skillVfx;

        /// <summary>收集技能起手与主体 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (skillStartLine != null) destination.Add(skillStartLine);
            if (skillLine != null) destination.Add(skillLine);
        }
    }
}
