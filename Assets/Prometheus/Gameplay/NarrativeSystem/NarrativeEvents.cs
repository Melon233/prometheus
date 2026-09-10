namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 描述剧情演出要求玩法 HUD 显隐的事实。
    ///
    /// 之所以走全局事件而不是直接持有面板引用：asmdef 的依赖方向是 UI → Gameplay，
    /// 玩法层无法命名 UI 程序集里的任何面板类型。事件是这个方向上唯一合法的通知手段，
    /// 同时也让「演出期间要隐藏哪些界面」这个决定留在 UI 层，玩法层只陈述需求。
    /// </summary>
    public sealed class NarrativeHudVisibilityEvent : IEvent
    {
        /// <summary>创建一条不可变的 HUD 显隐请求。</summary>
        /// <param name="visible">演出希望玩法 HUD 处于的显隐状态。</param>
        public NarrativeHudVisibilityEvent(bool visible)
        {
            Visible = visible;
        }

        /// <summary>获取演出希望玩法 HUD 处于的显隐状态。</summary>
        public bool Visible { get; }
    }
}
