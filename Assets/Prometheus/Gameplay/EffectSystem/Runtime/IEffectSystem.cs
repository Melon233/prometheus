namespace Xuan.Prometheus.Effects
{
    /// <summary>定义当前单局效果运行时及默认效果库的只读入口。</summary>
    public interface IEffectSystem : ISystemContract
    {
        /// <summary>获取系统是否已经释放。</summary>
        bool IsDisposed { get; }

        /// <summary>获取当前单局唯一的效果运行时。</summary>
        EffectRuntime Runtime { get; }

        /// <summary>获取当前单局默认效果配置库。</summary>
        EffectLibrary DefaultLibrary { get; }
    }
}
