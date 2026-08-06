using UnityEngine;
using Xuan.PrometheusCS.Simulation;

namespace Xuan.PrometheusCS.Presentation
{
    /// <summary>
    /// CubePlayerView 是玩家方块的 Unity 表现对象，只把模拟快照映射到 Transform，不包含移动规则。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CubePlayerView : MonoBehaviour
    {
        [SerializeField] private float planeHeight = 0.5f;
        private long lastRenderedTick = -1L;

        /// <summary>获取最近一次成功渲染的模拟快照。</summary>
        public PlayerMovementSnapshot LastSnapshot { get; private set; }

        /// <summary>获取表现对象是否已经收到至少一份模拟快照。</summary>
        public bool HasSnapshot { get; private set; }

        /// <summary>为场景生成器配置方块中心距离 XZ 平面的高度。</summary>
        public void Configure(float configuredPlaneHeight)
        {
            planeHeight = configuredPlaneHeight;
        }

        /// <summary>
        /// 把最新模拟坐标应用到 GameObject，并拒绝可能由异步链路送达的过期快照。
        /// </summary>
        public void Render(PlayerMovementSnapshot snapshot)
        {
            if (snapshot.TickNumber < lastRenderedTick) return;
            lastRenderedTick = snapshot.TickNumber;
            LastSnapshot = snapshot;
            HasSnapshot = true;
            transform.position = new Vector3(snapshot.PositionX, planeHeight, snapshot.PositionZ);
        }
    }
}
