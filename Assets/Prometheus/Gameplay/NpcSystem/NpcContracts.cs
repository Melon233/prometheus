namespace Xuan.Prometheus.Npc
{
    /// <summary>一次 NPC 交互的结束原因。</summary>
    public enum NpcInteractionResult
    {
        /// <summary>交互被拒绝：已有活动会话，或该 NPC 当前无话可说。</summary>
        Rejected,

        /// <summary>剧情完整演绎结束。</summary>
        Completed,

        /// <summary>剧情被跳过；对世界状态而言与完整演绎等价。</summary>
        Skipped,

        /// <summary>交互被中止：玩家退出、chunk 卸载或系统释放。</summary>
        Aborted
    }
}
