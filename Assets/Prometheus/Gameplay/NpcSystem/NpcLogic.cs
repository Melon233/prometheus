using System;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Npc
{
    /// <summary>负责 NPC 可交互判断和交互请求，不直接控制镜头、演出或 UI。</summary>
    public sealed class NpcLogic : PoiLogic
    {
        private NpcComponent npcComponent;

        /// <summary>绑定 NPC 组件并声明该逻辑不受角色战斗控制状态影响。</summary>
        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.None;
            if (!Entity.TryGetComp(out npcComponent)) throw new InvalidOperationException("NpcLogic requires NpcComponent.");
            npcComponent.Definition.Validate();
        }

        /// <summary>判断实体有效、NPC 已解锁且当前没有被回收。</summary>
        public bool CanInteract => Entity != null && Entity.IsActive && npcComponent != null && npcComponent.State.IsUnlocked;

        /// <summary>把 NPC 交互请求交给 NpcSystem 统一串行化。</summary>
        public override void OnInteract()
        {
            if (!CanInteract) return;
            if (Core.Gameplay.TryGetSystem(out INpcSystem npcSystem)) npcSystem.TryBeginInteraction((NpcEntity)Entity);
        }

        /// <summary>NPC 回收时取消仍属于该实体的活动交互。</summary>
        public override void OnDispose()
        {
            if (Entity != null && Core.Gameplay.TryGetSystem(out INpcSystem npcSystem)) npcSystem.CancelInteraction(Entity.EntityId);
            npcComponent = null;
        }
    }
}
