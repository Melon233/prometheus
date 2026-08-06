using UnityEngine;

namespace Xuan.PrometheusCS.Engine
{
    /// <summary>
    /// UnityFrameTimeSource 把 Unity 的帧时间作为普通浮点值暴露给组合层。
    /// </summary>
    public sealed class UnityFrameTimeSource : IFrameTimeSource
    {
        /// <summary>获取 Unity 当前帧的缩放后时间间隔。</summary>
        public float DeltaTime => Time.deltaTime;
    }
}
