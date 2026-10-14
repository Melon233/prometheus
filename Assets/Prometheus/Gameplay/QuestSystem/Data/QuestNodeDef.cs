using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务的一个节点：一组「用什么方式播放哪段演出」的触发器，加上一组必须全部完成的目标。
    /// 节点激活时触发器与目标同时生效；全部目标完成后节点结束，触发器随之失效。
    /// </summary>
    [Serializable]
    public sealed class QuestNodeDef
    {
        [SerializeField] [Tooltip("节点标题文本 id，显示在 HUD 任务追踪条上。")]
        private int titleTextId;

        [SerializeReference] [SerializeReferencePicker] [Tooltip("本节点激活期间生效的演出触发器。")]
        private List<QuestCutsceneTrigger> triggers = new List<QuestCutsceneTrigger>();

        [SerializeReference] [SerializeReferencePicker] [Tooltip("本节点的目标，全部完成才进入下一个节点。")]
        private List<QuestObjective> objectives = new List<QuestObjective>();

        /// <summary>节点标题文本 id。</summary>
        public int TitleTextId => titleTextId;

        /// <summary>演出触发器。</summary>
        public IReadOnlyList<QuestCutsceneTrigger> Triggers => triggers;

        /// <summary>目标。</summary>
        public IReadOnlyList<QuestObjective> Objectives => objectives;
    }
}
