using System;

namespace Xuan.Prometheus.Npc
{
    /// <summary>定义 NPC 单会话交互的创建、完成、取消和观察入口。</summary>
    public interface INpcSystem : ISystemContract
    {
        /// <summary>当 NPC 交互需要演出或外部对话适配器处理时触发。</summary>
        event Action<NpcInteractionContext> InteractionRequested;

        /// <summary>获取当前活动交互上下文。</summary>
        NpcInteractionContext? ActiveInteraction { get; }

        /// <summary>尝试为指定 NPC 创建唯一交互会话。</summary>
        bool TryBeginInteraction(NpcEntity entity);

        /// <summary>完成指定实体的活动交互。</summary>
        bool CompleteInteraction(int entityId);

        /// <summary>取消指定实体的活动交互。</summary>
        bool CancelInteraction(int entityId);
    }
}
