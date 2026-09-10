using System;
using UnityEngine;

namespace Xuan.Prometheus.Npc
{
    /// <summary>
    /// NPC 的静态配置。
    ///
    /// 这里**只有身份与兜底闲聊**：「这个 NPC 现在该说什么」是当前任务状态的函数，不是 NPC 的属性，
    /// 因此对话分流表配在任务步骤上（<c>DialogueBinding</c>），不配在这里。
    /// 若把分流表配在 NPC 上，新增一个任务就要回头改所有涉及的 NPC 资产，改动面随任务数线性增长，
    /// 且两份配置必然漂移。
    ///
    /// 剧情按**地址**引用而不是硬引用：本资产是 <c>PoiConfig</c> 的直接字段、随场景常驻，
    /// 硬引用会把整棵剧情树连同它引用的 Timeline、动画、特效在进场景时一次性拖进内存。
    /// </summary>
    [CreateAssetMenu(fileName = "NpcDefinition", menuName = "Prometheus/Npc/Npc Definition")]
    public sealed class NpcDefinition : ScriptableObject
    {
        [SerializeField] [Tooltip("跨场景、存档与任务使用的稳定 NPC 标识。")]
        private string npcId;

        [SerializeField] [Tooltip("表现层显示的 NPC 名称；接入 TextMap 后改为文案键。")]
        private string displayName;

        [SerializeField] [Tooltip("没有任何任务对话命中时演绎的兜底闲聊剧情地址；留空表示该 NPC 无话可说。")]
        private string idleStoryLocation;

        /// <summary>获取 NPC 稳定标识。</summary>
        public string NpcId => npcId;

        /// <summary>获取 NPC 显示名称。</summary>
        public string DisplayName => displayName;

        /// <summary>获取兜底闲聊剧情的资源地址。</summary>
        public string IdleStoryLocation => idleStoryLocation;

        /// <summary>校验 NPC 身份；缺少稳定标识的定义无法参与任何任务绑定。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(npcId)) throw new InvalidOperationException($"NpcDefinition '{name}' requires a non-empty NpcId.");
        }

        /// <summary>写入身份与兜底剧情；供编辑器工具与测试使用。</summary>
        /// <param name="id">NPC 稳定标识。</param>
        /// <param name="display">显示名称。</param>
        /// <param name="idleStory">兜底闲聊剧情地址。</param>
        public void Configure(string id, string display = null, string idleStory = null)
        {
            npcId = id;
            displayName = display;
            idleStoryLocation = idleStory;
        }
    }
}
