using System;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Npc
{
    /// <summary>管理单局唯一 NPC 交互会话，并向演出、对话和任务适配器发布请求。</summary>
    internal sealed class NpcSystem : XSystem, INpcSystem
    {
        private NpcInteractionContext? activeInteraction;

        /// <summary>外部适配器订阅该事件后负责启动叙事流程或对话 UI。</summary>
        public event Action<NpcInteractionContext> InteractionRequested;

        /// <summary>获取当前活动交互；没有活动会话时为空。</summary>
        public NpcInteractionContext? ActiveInteraction => activeInteraction;

        /// <summary>尝试为 NPC 创建唯一交互会话并发布请求。</summary>
        public bool TryBeginInteraction(PoiMono npc)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            if (npc.Config == null || activeInteraction.HasValue) return false;
            NpcDefinition definition = npc.Config.Npc;
            if (definition == null) return false;
            definition.Validate();
            NpcInteractionContext context = new NpcInteractionContext(npc.Config.Id, definition.NpcId, definition.DefaultInteractionId);
            activeInteraction = context;
            InteractionRequested?.Invoke(context);
            return true;
        }

        /// <summary>完成指定 POI 的活动交互；Id 不匹配时保持当前会话。</summary>
        public bool CompleteInteraction(string poiId)
        {
            if (!activeInteraction.HasValue || !string.Equals(activeInteraction.Value.PoiId, poiId, StringComparison.Ordinal)) return false;
            activeInteraction = null;
            return true;
        }

        /// <summary>取消指定 POI 的活动交互，供场景卸载和外部中断使用。</summary>
        public bool CancelInteraction(string poiId)
        {
            return CompleteInteraction(poiId);
        }

        /// <summary>释放当前会话和全部外部订阅。</summary>
        public override void Dispose()
        {
            activeInteraction = null;
            InteractionRequested = null;
        }
    }
}
