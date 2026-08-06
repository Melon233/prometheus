using Xuan.PrometheusCS.Simulation;

namespace Xuan.PrometheusCS.Engine
{
    /// <summary>
    /// IPlayerMovementInputSource 定义组合层需要的输入能力，使 Bootstrap 不依赖具体键盘实现。
    /// </summary>
    public interface IPlayerMovementInputSource
    {
        /// <summary>采集当前输入并转换为模拟层能够理解的移动命令。</summary>
        MovePlayerCommand CaptureCommand();
    }
}
