using UnityEngine;
using Xuan.PrometheusCS.Engine;
using Xuan.PrometheusCS.Presentation;
using Xuan.PrometheusCS.Simulation;

namespace Xuan.PrometheusCS.Bootstrap
{
    /// <summary>
    /// Bootstrap 是 Demo 的唯一组合入口，负责创建纯模拟对象、Unity 引擎适配器和表现层 Presenter，并按单向数据流推进它们。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Bootstrap : MonoBehaviour
    {
        [SerializeField] private CubePlayerView playerView;
        [SerializeField, Min(0.01f)] private float movementSpeed = 5f;

        private PlayerMovementSimulation simulation;
        private IPlayerMovementInputSource inputSource;
        private IFrameTimeSource frameTimeSource;
        private CubePlayerPresenter presenter;

        /// <summary>
        /// 为编辑器场景生成器写入表现对象和移动速度；运行时依赖仍统一在 Awake 中完成装配。
        /// </summary>
        public void Configure(CubePlayerView configuredPlayerView, float configuredMovementSpeed)
        {
            if (configuredPlayerView == null) throw new System.ArgumentNullException(nameof(configuredPlayerView));
            playerView = configuredPlayerView;
            movementSpeed = Mathf.Max(0.01f, configuredMovementSpeed);
        }

        /// <summary>
        /// 创建各层对象并立即把模拟初始快照同步到表现层。
        /// </summary>
        private void Awake()
        {
            if (playerView == null)
            {
                Debug.LogError("PrometheusCS Bootstrap requires a CubePlayerView reference.", this);
                enabled = false;
                return;
            }
            simulation = new PlayerMovementSimulation(movementSpeed);
            inputSource = new UnityKeyboardMovementInputSource();
            frameTimeSource = new UnityFrameTimeSource();
            presenter = new CubePlayerPresenter(playerView);
            presenter.Present(simulation.CurrentSnapshot);
        }

        /// <summary>
        /// 每帧把 Unity 输入转换为 Command，推进纯模拟，再把不可变快照交给表现层。
        /// </summary>
        private void Update()
        {
            MovePlayerCommand command = inputSource.CaptureCommand();
            PlayerMovementSnapshot snapshot = simulation.Advance(command, frameTimeSource.DeltaTime);
            presenter.Present(snapshot);
        }

        /// <summary>
        /// 在 Inspector 编辑时约束移动速度，避免把非法配置带入模拟层构造函数。
        /// </summary>
        private void OnValidate()
        {
            movementSpeed = Mathf.Max(0.01f, movementSpeed);
        }
    }
}
