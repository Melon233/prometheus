using System;
using UnityEngine;

namespace Xuan.Prometheus.Npc
{
    /// <summary>保存 NPC 的稳定身份和第一阶段交互入口配置。</summary>
    [CreateAssetMenu(fileName = "NpcDefinition", menuName = "Prometheus/Npc/Npc Definition")]
    public sealed class NpcDefinition : ScriptableObject
    {
        [SerializeField] private string npcId;
        [SerializeField] private string displayName;
        [SerializeField] private string defaultInteractionId;

        /// <summary>获取跨场景、存档和任务系统使用的稳定 NPC 标识。</summary>
        public string NpcId => npcId;

        /// <summary>获取表现层显示的 NPC 名称。</summary>
        public string DisplayName => displayName;

        /// <summary>获取当前阶段默认请求的交互入口标识。</summary>
        public string DefaultInteractionId => defaultInteractionId;

        /// <summary>校验 NPC 身份和默认交互入口，避免运行时创建无效实体。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(npcId)) throw new InvalidOperationException($"NpcDefinition '{name}' requires a non-empty NpcId.");
            if (string.IsNullOrWhiteSpace(defaultInteractionId)) throw new InvalidOperationException($"NpcDefinition '{npcId}' requires a default interaction ID.");
        }
    }
}
