using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>WorldMapSystem 已完成地图资源解析的通知；面板收到后重新绑定地图纹理。</summary>
    public sealed class WorldMapReadyEvent : IEvent
    {
        /// <summary>本次发布的地图定义；资源缺失时为空，面板应显示空地图区域。</summary>
        public WorldMapDefinition Definition { get; }

        /// <summary>创建地图资源就绪事件。</summary>
        public WorldMapReadyEvent(WorldMapDefinition definition)
        {
            Definition = definition;
        }
    }

    /// <summary>世界地图发布的玩家位置变化通知，HUD 和大地图据此更新玩家标记与视口。</summary>
    public sealed class WorldMapPlayerPositionChangedEvent : IEvent
    {
        /// <summary>当前上场玩家的世界位置。</summary>
        public Vector3 Position { get; }

        /// <summary>创建玩家位置变化事件。</summary>
        public WorldMapPlayerPositionChangedEvent(Vector3 position)
        {
            Position = position;
        }
    }
}
