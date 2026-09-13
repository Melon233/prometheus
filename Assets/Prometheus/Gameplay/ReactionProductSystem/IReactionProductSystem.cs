namespace Xuan.Prometheus.Reactions
{
    /// <summary>
    /// 把元素系统广播的伪元素状态变化落实为可见后果。
    ///
    /// 元素系统只知道「目标身上有一层冻结，还剩 3.2 秒」；让目标真的动不了、
    /// 让草原核到期时炸出草伤，都由本系统完成。这样元素系统仍然不认识属性面板、Effect 与伤害。
    ///
    /// 本接口对外只暴露释放语义：它是一条纯粹的事件消费链路，没有可供外部调用的入口。
    /// </summary>
    public interface IReactionProductSystem : ISystemContract
    {
        /// <summary>获取当前正在维持产物 Effect 的状态数量，供测试与调试确认没有泄漏。</summary>
        int ActiveProductCount { get; }
    }
}
