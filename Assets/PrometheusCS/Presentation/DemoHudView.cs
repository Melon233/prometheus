using UnityEngine;

namespace Xuan.PrometheusCS.Presentation
{
    /// <summary>
    /// DemoHudView 使用 Unity Immediate Mode GUI 显示操作说明和模拟坐标，避免 Demo 依赖额外 UI 资产。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DemoHudView : MonoBehaviour
    {
        [SerializeField] private CubePlayerView playerView;

        /// <summary>为场景生成器绑定需要展示坐标的玩家 View。</summary>
        public void Configure(CubePlayerView configuredPlayerView)
        {
            playerView = configuredPlayerView;
        }

        /// <summary>绘制 WASD 操作说明、架构数据流和当前模拟坐标。</summary>
        private void OnGUI()
        {
            string coordinates = playerView != null && playerView.HasSnapshot ? $"X: {playerView.LastSnapshot.PositionX:0.00}    Z: {playerView.LastSnapshot.PositionZ:0.00}" : "Waiting for simulation snapshot...";
            GUI.Box(new Rect(16f, 16f, 390f, 92f), $"PrometheusCS Layered Architecture Demo\nWASD: Move the cube on the XZ plane\nCommand -> Simulation -> Snapshot -> Presentation\n{coordinates}");
        }
    }
}
