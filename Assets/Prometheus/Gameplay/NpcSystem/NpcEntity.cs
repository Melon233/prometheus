using System;
using UnityEngine;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Npc
{
    /// <summary>复用 PoiEntity 世界生命周期并组合 NPC 专属组件与逻辑。</summary>
    public sealed class NpcEntity : PoiEntity
    {
        /// <summary>使用场景 POI、NPC 定义和运行时状态创建 NPC 实体。</summary>
        public NpcEntity(GameObject bindGameObject, PoiConfig poiConfig, NpcDefinition definition, NpcRuntimeState state = null) : base(bindGameObject, poiConfig, new NpcLogic())
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            definition.Validate();
            AddComp(new NpcComponent { Definition = definition, State = state ?? new NpcRuntimeState() });
        }

        /// <summary>获取当前 NPC 静态定义。</summary>
        public NpcDefinition Definition => TryGetComp(out NpcComponent component) ? component.Definition : null;

        /// <summary>获取当前 NPC 运行时状态。</summary>
        public NpcRuntimeState RuntimeState => TryGetComp(out NpcComponent component) ? component.State : null;
    }
}
