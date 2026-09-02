using System;
using Xuan.Prometheus.Film;

namespace Xuan.Prometheus.Npc
{
    /// <summary>管理单局唯一 NPC 交互会话，并向演出、对话和任务适配器发布请求。</summary>
    internal sealed class NpcSystem : XSystem, INpcSystem
    {
        private NpcInteractionContext? activeInteraction;
        private NpcInteractionCoordinator coordinator;

        /// <summary>外部适配器订阅该事件后负责启动 Film 或对话 UI。</summary>
        public event Action<NpcInteractionContext> InteractionRequested;

        /// <summary>获取当前活动交互；没有活动会话时为空。</summary>
        public NpcInteractionContext? ActiveInteraction => activeInteraction;

        /// <summary>通过 Core.Gameplay 建立当前单局 NPC 与演出系统的交互协调器。</summary>
        public override void AfterNew()
        {
            coordinator = new NpcInteractionCoordinator(Core.Gameplay.GetSystem<IFilmSystem>(), CompleteInteractionFromCoordinator);
            InteractionRequested += coordinator.Start;
        }

        /// <summary>尝试为 NPC 创建唯一交互会话并发布请求。</summary>
        public bool TryBeginInteraction(NpcEntity entity)
        {
            if (coordinator == null) throw new InvalidOperationException("NpcSystem must complete AfterNew before interaction.");
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (!entity.IsActive || activeInteraction.HasValue) return false;
            NpcDefinition definition = entity.Definition;
            NpcInteractionContext context = new NpcInteractionContext(entity.EntityId, entity.Config.Id, definition.NpcId, definition.DefaultInteractionId);
            activeInteraction = context;
            InteractionRequested?.Invoke(context);
            return true;
        }

        /// <summary>完成指定实体的活动交互；实体编号不匹配时保持当前会话。</summary>
        public bool CompleteInteraction(int entityId)
        {
            if (!activeInteraction.HasValue || activeInteraction.Value.EntityId != entityId) return false;
            activeInteraction = null;
            return true;
        }

        /// <summary>接收协调器完成通知并忽略已完成会话的幂等返回值。</summary>
        private void CompleteInteractionFromCoordinator(int entityId)
        {
            CompleteInteraction(entityId);
        }

        /// <summary>取消指定实体的活动交互，供 NPC 回收和外部中断使用。</summary>
        public bool CancelInteraction(int entityId)
        {
            coordinator?.Cancel(entityId);
            return CompleteInteraction(entityId);
        }

        /// <summary>释放当前会话和全部外部订阅。</summary>
        public override void Dispose()
        {
            activeInteraction = null;
            InteractionRequested = null;
            coordinator = null;
        }
    }
}
