namespace Xuan.PrometheusCS.Simulation
{
    /// <summary>
    /// PlayerMovementSnapshot 是模拟层对外发布的不可变只读模型，表现层只能读取而不能反向修改模拟状态。
    /// </summary>
    public readonly struct PlayerMovementSnapshot
    {
        /// <summary>获取玩家在模拟空间中的 X 坐标。</summary>
        public float PositionX { get; }

        /// <summary>获取玩家在模拟空间中的 Z 坐标。</summary>
        public float PositionZ { get; }

        /// <summary>获取生成该快照的模拟帧编号，用于表现层拒绝过期快照。</summary>
        public long TickNumber { get; }

        /// <summary>创建一份完整且不可变的玩家移动快照。</summary>
        public PlayerMovementSnapshot(float positionX, float positionZ, long tickNumber)
        {
            PositionX = positionX;
            PositionZ = positionZ;
            TickNumber = tickNumber;
        }
    }
}
