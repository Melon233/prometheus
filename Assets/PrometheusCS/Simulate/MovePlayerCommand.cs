using System;

namespace Xuan.PrometheusCS.Simulation
{
    /// <summary>
    /// MovePlayerCommand 描述单个模拟帧的二维移动意图，Horizontal 对应 X 轴，Vertical 对应 Z 轴。
    /// </summary>
    public readonly struct MovePlayerCommand
    {
        /// <summary>获取已经限制到负一至正一范围的水平输入。</summary>
        public float Horizontal { get; }

        /// <summary>获取已经限制到负一至正一范围的纵向输入。</summary>
        public float Vertical { get; }

        /// <summary>
        /// 创建移动命令并拒绝无法参与确定计算的非有限数值。
        /// </summary>
        public MovePlayerCommand(float horizontal, float vertical)
        {
            if (!IsFinite(horizontal)) throw new ArgumentOutOfRangeException(nameof(horizontal), horizontal, "Horizontal input must be finite.");
            if (!IsFinite(vertical)) throw new ArgumentOutOfRangeException(nameof(vertical), vertical, "Vertical input must be finite.");
            Horizontal = ClampAxis(horizontal);
            Vertical = ClampAxis(vertical);
        }

        /// <summary>判断浮点值是否既不是 NaN 也不是正负无穷。</summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>把单轴输入限制到模拟层接受的标准范围。</summary>
        private static float ClampAxis(float value)
        {
            if (value < -1f) return -1f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
