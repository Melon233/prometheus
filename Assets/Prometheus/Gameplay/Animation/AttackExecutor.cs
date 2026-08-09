using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>把一段普通攻击的原地动画、移动动画、音效和特效组合为不可错位的配置行。</summary>
    [Serializable]
    public sealed class AttackAnimationDefinition
    {
        [SerializeField] private AnimationLine animationLine;
        [SerializeField] private AnimationLine movingAnimationLine;
        [SerializeField] private AudioClip audioClip;
        [SerializeField] private bool hasVfx;
        [SerializeField] private YefaVfx vfx;

        /// <summary>根据移动状态选择动画语义，移动版本为空时回退到原地版本。</summary>
        public AnimationSemantic GetSemantic(bool moving)
        {
            AnimationLine selectedLine = moving && movingAnimationLine != null ? movingAnimationLine : animationLine;
            return selectedLine == null ? AnimationSemantic.None : selectedLine.Semantic;
        }

        /// <summary>把本段攻击引用的正式 AnimationLine 添加到动画库索引输入。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (animationLine != null) destination.Add(animationLine);
            if (movingAnimationLine != null && movingAnimationLine != animationLine) destination.Add(movingAnimationLine);
        }

        /// <summary>获取攻击命中窗口开始时播放的音效。</summary>
        public AudioClip AudioClip => audioClip;

        /// <summary>获取本段攻击是否配置特效。</summary>
        public bool HasVfx => hasVfx;

        /// <summary>获取本段攻击使用的特效编号。</summary>
        public YefaVfx Vfx => vfx;
    }

    /// <summary>表示一次已经解析完成的攻击表现配置，Logic 使用它驱动播放、命中盒、音效和特效。</summary>
    public readonly struct AttackAnimationSelection
    {
        /// <summary>创建一份攻击表现选择结果。</summary>
        public AttackAnimationSelection(AnimationSemantic semantic, AudioClip audioClip, bool hasVfx, YefaVfx vfx)
        {
            Semantic = semantic;
            AudioClip = audioClip;
            HasVfx = hasVfx;
            Vfx = vfx;
        }

        public AnimationSemantic Semantic { get; }
        public AudioClip AudioClip { get; }
        public bool HasVfx { get; }
        public YefaVfx Vfx { get; }
    }

    /// <summary>保存普通攻击组合配置，并把上下文选择结果转换成稳定动画语义。</summary>
    [Serializable]
    public sealed class AttackExecutor
    {
        [SerializeField] private List<AttackAnimationDefinition> attacks = new List<AttackAnimationDefinition>();

        /// <summary>获取可用攻击段数。</summary>
        public int Count => attacks == null ? 0 : attacks.Count;

        /// <summary>按连段下标和移动状态解析攻击配置；任何必要动画缺失都会返回失败而不是产生越界异常。</summary>
        public bool TryGetSelection(int index, bool moving, out AttackAnimationSelection selection)
        {
            if (attacks == null || index < 0 || index >= attacks.Count || attacks[index] == null)
            {
                selection = default;
                return false;
            }
            AttackAnimationDefinition definition = attacks[index];
            AnimationSemantic semantic = definition.GetSemantic(moving);
            if (semantic == AnimationSemantic.None)
            {
                selection = default;
                return false;
            }
            selection = new AttackAnimationSelection(semantic, definition.AudioClip, definition.HasVfx, definition.Vfx);
            return true;
        }

        /// <summary>收集全部普通攻击 AnimationLine，供 AnimationLibrary 建立语义索引。</summary>
        internal void CollectLines(List<AnimationLine> destination)
        {
            if (attacks == null) return;
            for (int index = 0; index < attacks.Count; index++) attacks[index]?.CollectLines(destination);
        }
    }
}
