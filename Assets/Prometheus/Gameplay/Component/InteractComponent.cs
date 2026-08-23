using System.Collections.Generic;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Component
{
    /// <summary>
    /// 保存玩家当前交互半径内的可交互 POI 配置列表，并提供可监听的修订号供 UI 刷新交互栏。
    /// 只保存纯数据配置（PoiConfig），交互时由 UI 层按 Id 解析实体，避免感应逻辑反向依赖 WorldSystem。
    /// </summary>
    public sealed class InteractComponent : Component
    {
        /// <summary>附近交互物列表的变化版本；UI 通过 EntitySystem.Listen 监听它并在变化时重新读取列表。</summary>
        private readonly ModifiableProperty revision = new ModifiableProperty();

        /// <summary>当前可交互 POI 配置列表（按感应进入顺序）。</summary>
        private readonly List<PoiConfig> nearby = new List<PoiConfig>();

        /// <summary>获取附近交互物列表的可监听版本字段。</summary>
        public ModifiableProperty RevisionProperty => revision;

        /// <summary>添加一个附近交互物；重复添加返回 false。</summary>
        public bool AddNearby(PoiConfig config)
        {
            if (config == null || nearby.Contains(config)) return false;
            nearby.Add(config);
            revision.SetBaseValue(revision.Value + 1f);
            return true;
        }

        /// <summary>移除一个附近交互物；不存在返回 false。</summary>
        public bool RemoveNearby(PoiConfig config)
        {
            if (config == null || !nearby.Remove(config)) return false;
            revision.SetBaseValue(revision.Value + 1f);
            return true;
        }

        /// <summary>把当前列表复制到复用缓冲区，避免列表逐帧刷新产生临时集合。</summary>
        public void CopyNearby(List<PoiConfig> buffer)
        {
            buffer.Clear();
            buffer.AddRange(nearby);
        }
    }
}
