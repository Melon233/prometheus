using System;

namespace Xuan.Prometheus.Npc
{
    /// <summary>保存不依赖场景表现对象的 NPC 运行时状态。</summary>
    [Serializable]
    public sealed class NpcRuntimeState
    {
        /// <summary>NPC 是否已解锁并允许进入交互判断。</summary>
        public bool IsUnlocked = true;

        /// <summary>业务系统用于切换对话和任务入口的稳定阶段值。</summary>
        public int Stage;
    }

    /// <summary>描述一次 NPC 交互请求，供对话、演出和任务适配器消费。</summary>
    public readonly struct NpcInteractionContext
    {
        /// <summary>创建一份不暴露 NpcLogic 内部状态的交互请求。</summary>
        public NpcInteractionContext(int entityId, string poiId, string npcId, string interactionId)
        {
            EntityId = entityId;
            PoiId = poiId;
            NpcId = npcId;
            InteractionId = interactionId;
        }

        /// <summary>获取当前单局 NPC 实体编号。</summary>
        public int EntityId { get; }

        /// <summary>获取承载 NPC 的世界 POI 标识。</summary>
        public string PoiId { get; }

        /// <summary>获取 NPC 稳定业务标识。</summary>
        public string NpcId { get; }

        /// <summary>获取需要由外部适配器解释的交互入口标识。</summary>
        public string InteractionId { get; }
    }
}
