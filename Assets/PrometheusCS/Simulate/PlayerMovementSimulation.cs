using System;

namespace Xuan.PrometheusCS.Simulation
{
    /// <summary>
    /// PlayerMovementSimulation 保存权威玩家坐标并执行 XZ 平面移动规则；该类型只依赖标准 C#，不引用任何 Unity API。
    /// </summary>
    public sealed class PlayerMovementSimulation
    {
        private readonly float movementSpeed;
        private float positionX;
        private float positionZ;
        private long tickNumber;

        /// <summary>获取当前模拟状态的不可变快照。</summary>
        public PlayerMovementSnapshot CurrentSnapshot => new PlayerMovementSnapshot(positionX, positionZ, tickNumber);

        /// <summary>
        /// 创建玩家移动模拟，并用每秒单位数表达移动速度。
        /// </summary>
        public PlayerMovementSimulation(float configuredMovementSpeed, float initialPositionX = 0f, float initialPositionZ = 0f)
        {
            if (!IsFinite(configuredMovementSpeed) || configuredMovementSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(configuredMovementSpeed), configuredMovementSpeed, "Movement speed must be finite and positive.");
            if (!IsFinite(initialPositionX)) throw new ArgumentOutOfRangeException(nameof(initialPositionX), initialPositionX, "Initial X position must be finite.");
            if (!IsFinite(initialPositionZ)) throw new ArgumentOutOfRangeException(nameof(initialPositionZ), initialPositionZ, "Initial Z position must be finite.");
            movementSpeed = configuredMovementSpeed;
            positionX = initialPositionX;
            positionZ = initialPositionZ;
        }

        /// <summary>
        /// 推进一次模拟；对角输入会归一化，因此同时按下两个方向不会获得额外移动速度。
        /// </summary>
        public PlayerMovementSnapshot Advance(MovePlayerCommand command, float deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "Delta time must be finite and non-negative.");
            float directionX = command.Horizontal;
            float directionZ = command.Vertical;
            float lengthSquared = directionX * directionX + directionZ * directionZ;
            if (lengthSquared > 1f)
            {
                float inverseLength = 1f / (float)Math.Sqrt(lengthSquared);
                directionX *= inverseLength;
                directionZ *= inverseLength;
            }
            float distance = movementSpeed * deltaTime;
            positionX += directionX * distance;
            positionZ += directionZ * distance;
            tickNumber++;
            return CurrentSnapshot;
        }

        /// <summary>判断浮点值是否可以安全参与模拟计算。</summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
