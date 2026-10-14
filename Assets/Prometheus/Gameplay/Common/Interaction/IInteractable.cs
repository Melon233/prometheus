namespace Xuan.Prometheus
{
    /// <summary>
    /// 可被玩家交互栏列出并点击的场景对象。
    /// 玩家身上的交互感应（InteractLogic）只认识这个端口，因此 POI 与任务交互点可以共用同一条
    /// 「感应进入 → 交互栏显示 → 点击交互」链路，而感应逻辑与 HUD 都不需要认识 PoiSystem 或 QuestSystem。
    /// 实现者必须是挂在带碰撞体的 GameObject（或其父级）上的组件，感应通过 GetComponentInParent 找到它。
    /// </summary>
    public interface IInteractable
    {
        /// <summary>交互栏上显示的文案。</summary>
        string InteractLabel { get; }

        /// <summary>玩家点击交互栏项时调用；具体行为由实现者所属的系统决定。</summary>
        void Interact();
    }
}
