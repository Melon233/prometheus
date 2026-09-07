namespace Xuan.Prometheus
{
    /// <summary>
    /// 保存大地图面板的视图状态。
    /// MapPanel 的关闭策略是 Destroy，实例不跨越一次开关；缩放档位又需要在重新打开时延续，
    /// 因此这份纯界面状态保存在 UI 层的独立持有者中，而不是塞进玩法 System。
    /// </summary>
    public static class MapPanelViewState
    {
        /// <summary>表示尚未由玩家调整过缩放，面板应回退到地图配置的初始缩放。</summary>
        public const float UnsetZoom = 0f;

        /// <summary>玩家上次离开大地图时的缩放档位；为 <see cref="UnsetZoom"/> 表示尚未调整过。</summary>
        public static float Zoom { get; set; } = UnsetZoom;
    }
}
