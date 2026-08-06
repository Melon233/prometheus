using System;
using Xuan.PrometheusCS.Simulation;

namespace Xuan.PrometheusCS.Presentation
{
    /// <summary>
    /// CubePlayerPresenter 接收模拟层只读快照并驱动 View，集中表达模拟数据到 Unity 表现的映射关系。
    /// </summary>
    public sealed class CubePlayerPresenter
    {
        private readonly CubePlayerView view;

        /// <summary>创建 Presenter 并绑定唯一的方块表现对象。</summary>
        public CubePlayerPresenter(CubePlayerView configuredView)
        {
            view = configuredView != null ? configuredView : throw new ArgumentNullException(nameof(configuredView));
        }

        /// <summary>把模拟层发布的不可变快照提交给 View。</summary>
        public void Present(PlayerMovementSnapshot snapshot)
        {
            view.Render(snapshot);
        }
    }
}
