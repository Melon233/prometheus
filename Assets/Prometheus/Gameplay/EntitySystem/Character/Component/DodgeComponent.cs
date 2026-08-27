namespace Xuan.Prometheus
{
    /// <summary>保存玩家闪避 Logic 的运行状态，使调度条件与动画会话生命周期保持一致。</summary>
    public class DodgeComponent : Component.Component
    {
        /// <summary>指示闪避动画会话是否仍在运行；只有会话结束或被更高优先级动画抢占时才会重置。</summary>
        public bool isDodging;
    }
}
