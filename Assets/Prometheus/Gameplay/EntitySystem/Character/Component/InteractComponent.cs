using System.Collections.Generic;

namespace Xuan.Prometheus.Component
{
    /// <summary>
    /// 保存玩家当前交互半径内的可交互对象列表，并提供可监听的修订号供 UI 刷新交互栏。
    /// 只认识 <see cref="IInteractable"/> 端口：POI 与任务交互点都实现它，感应逻辑与 HUD 因此不依赖 PoiSystem 或 QuestSystem。
    /// </summary>
    public sealed class InteractComponent : Component
    {
        /// <summary>附近交互物列表的变化版本；UI 通过 EntitySystem.Listen 监听它并在变化时重新读取列表。</summary>
        private readonly ModifiableProperty revision = new ModifiableProperty();

        /// <summary>当前可交互对象列表（按感应进入顺序）。</summary>
        private readonly List<IInteractable> nearby = new List<IInteractable>();

        /// <summary>获取附近交互物列表的可监听版本字段。</summary>
        public ModifiableProperty RevisionProperty => revision;

        /// <summary>添加一个附近交互物；重复添加返回 false。</summary>
        public bool AddNearby(IInteractable interactable)
        {
            if (interactable == null || nearby.Contains(interactable)) return false;
            nearby.Add(interactable);
            revision.SetBaseValue(revision.Value + 1f);
            return true;
        }

        /// <summary>移除一个附近交互物；不存在返回 false。</summary>
        public bool RemoveNearby(IInteractable interactable)
        {
            if (interactable == null || !nearby.Remove(interactable)) return false;
            revision.SetBaseValue(revision.Value + 1f);
            return true;
        }

        /// <summary>把当前列表复制到复用缓冲区，避免列表逐帧刷新产生临时集合。</summary>
        public void CopyNearby(List<IInteractable> buffer)
        {
            buffer.Clear();
            buffer.AddRange(nearby);
        }
    }
}
