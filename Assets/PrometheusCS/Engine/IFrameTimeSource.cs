namespace Xuan.PrometheusCS.Engine
{
    /// <summary>
    /// IFrameTimeSource 抽象 Unity 帧时间，使组合入口不必直接读取静态 Time API。
    /// </summary>
    public interface IFrameTimeSource
    {
        /// <summary>获取当前表现帧距上一帧经过的秒数。</summary>
        float DeltaTime { get; }
    }
}
