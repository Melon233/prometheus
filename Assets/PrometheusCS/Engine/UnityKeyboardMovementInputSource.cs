using UnityEngine;
using Xuan.PrometheusCS.Simulation;

namespace Xuan.PrometheusCS.Engine
{
    /// <summary>
    /// UnityKeyboardMovementInputSource 使用 Unity 输入 API 读取 WASD，并在引擎边界把它转换为纯 C# Command。
    /// </summary>
    public sealed class UnityKeyboardMovementInputSource : IPlayerMovementInputSource
    {
        /// <summary>读取 A/D 作为 X 轴、S/W 作为 Z 轴，允许相反方向互相抵消。</summary>
        public MovePlayerCommand CaptureCommand()
        {
            float horizontal = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float vertical = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            return new MovePlayerCommand(horizontal, vertical);
        }
    }
}
