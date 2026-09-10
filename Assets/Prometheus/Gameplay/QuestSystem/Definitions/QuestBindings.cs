using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 一条 NPC 对话绑定：在什么条件下，某个 NPC 该演哪段剧情、头顶挂什么标记。
    ///
    /// 绑定配在**任务**上而不是 NPC 上。理由是纯粹的工程理由：
    /// 「这个 NPC 现在该说什么」是当前任务状态的函数，不是 NPC 的属性。
    /// 配在 NPC 上，新增一个任务就要回头改所有涉及的 NPC 资产，改动面随任务数线性增长且两份配置必然漂移；
    /// 配在任务步骤上，新增任务只动一个资产。
    ///
    /// 这是**声明式绑定**而不是一次性动作：NPC 该说什么话永远是当前任务状态的纯函数，
    /// 读档、任务回滚、重新进场景都自动正确，不需要重放任何历史（铁律 Q1）。
    /// </summary>
    [Serializable]
    public sealed class DialogueBinding
    {
        [SerializeField] [Tooltip("在所属任务内唯一的绑定标识；会回填进 npc.talk_finished 的载荷。")]
        private string bindingId;

        [SerializeField] [Tooltip("该绑定作用的 NPC 稳定标识。")]
        private string npcId;

        [SerializeField] [Tooltip("生效条件表达式；留空表示恒真。")]
        private string condition;

        [SerializeField] [Tooltip("同一 NPC 有多条绑定命中时的优先级，高者胜出。")]
        private int priority;

        [SerializeField] [Tooltip("要演绎的剧情图资源地址（YooAsset location）。")]
        private string storyLocation;

        [SerializeField] [Tooltip("该绑定生效时 NPC 头顶的标记。")]
        private QuestMarker marker = QuestMarker.None;

        [SerializeField] [Tooltip("该对话用于显式接取的任务标识；留空表示不提供接取。")]
        private string offersQuestId;

        /// <summary>创建一条空绑定，供 Unity 序列化使用。</summary>
        public DialogueBinding()
        {
        }

        /// <summary>创建一条对话绑定。</summary>
        /// <param name="bindingId">在所属任务内唯一的绑定标识。</param>
        /// <param name="npcId">该绑定作用的 NPC 稳定标识。</param>
        /// <param name="storyLocation">要演绎的剧情图资源地址。</param>
        /// <param name="marker">该绑定生效时 NPC 头顶的标记。</param>
        /// <param name="condition">生效条件表达式。</param>
        /// <param name="priority">同 NPC 多绑定命中时的优先级。</param>
        public DialogueBinding(string bindingId, string npcId, string storyLocation, QuestMarker marker = QuestMarker.None, string condition = null, int priority = 0)
        {
            this.bindingId = bindingId;
            this.npcId = npcId;
            this.storyLocation = storyLocation;
            this.marker = marker;
            this.condition = condition;
            this.priority = priority;
        }

        /// <summary>获取绑定稳定标识。</summary>
        public string BindingId => bindingId;

        /// <summary>获取该绑定作用的 NPC 稳定标识。</summary>
        public string NpcId => npcId;

        /// <summary>获取生效条件表达式。</summary>
        public string Condition => condition;

        /// <summary>获取优先级。</summary>
        public int Priority => priority;

        /// <summary>获取要演绎的剧情图资源地址。</summary>
        public string StoryLocation => storyLocation;

        /// <summary>获取该绑定生效时 NPC 头顶的标记。</summary>
        public QuestMarker Marker => marker;

        /// <summary>获取该对话用于显式接取的任务标识。</summary>
        public string OffersQuestId => offersQuestId;

        /// <summary>设置显式接取的任务标识；供编辑器工具与测试使用。</summary>
        public DialogueBinding WithOffer(string questId)
        {
            offersQuestId = questId;
            return this;
        }
    }
}
