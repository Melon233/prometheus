using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Cutscene
{
    /// <summary>
    /// 演出的一步：同时开始的一组 Action。
    /// 推进规则由内容推导而不是单独配置：含 <see cref="DialogAction"/> 的步骤等待玩家点击（有选项时等待选择），
    /// 否则在 <see cref="Duration"/> 秒后自动进入下一步。Action 的效果会延续到后续步骤，
    /// 时长只决定「何时开始下一步」，不会截断尚未播完的镜头或移动。
    /// </summary>
    [Serializable]
    public sealed class CutsceneStep
    {
        [SerializeField] [Tooltip("不含对话的步骤持续多少秒后进入下一步；含对话的步骤忽略此值。")]
        private float duration = 1f;

        [SerializeReference] [SerializeReferencePicker] [Tooltip("本步同时开始的 Action。")]
        private List<CutsceneAction> actions = new List<CutsceneAction>();

        /// <summary>非对话步骤的持续时间（秒）。</summary>
        public float Duration => duration;

        /// <summary>本步的全部 Action。</summary>
        public IReadOnlyList<CutsceneAction> Actions => actions;

        /// <summary>本步的对话 Action；校验保证至多一个，不存在时为空。</summary>
        public DialogAction Dialog
        {
            get
            {
                for (int index = 0; index < actions.Count; index++)
                {
                    if (actions[index] is DialogAction dialog) return dialog;
                }
                return null;
            }
        }
    }
}
