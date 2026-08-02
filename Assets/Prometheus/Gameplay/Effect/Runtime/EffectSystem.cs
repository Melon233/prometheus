using System;
using UnityEngine;
using Xuan.Prometheus.Actor;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// EffectSystem 是单局 GameplayKit 独占的公共效果系统，负责持有并推进唯一的 EffectRuntime。
    /// 它是普通 C# System，不依赖 MonoBehaviour、场景对象或进程级单例，因此多个战斗上下文可以彼此隔离。
    /// </summary>
    public sealed class EffectSystem : XSystem
    {
        private readonly int randomSeed;
        private readonly bool logTrace;
        private readonly EffectLibrary defaultLibrary;
        private EffectRuntime runtime;
        private ActorSimulationSystem simulationSystem;
        private bool isDisposed;

        /// <summary>
        /// 创建一个单局效果系统，并显式接收正式玩法使用的持久化效果库。
        /// </summary>
        /// <param name="library">当前单局使用的默认效果库；必须由玩法入口显式注入。</param>
        /// <param name="randomSeed">EffectRuntime 使用的确定性随机种子。</param>
        /// <param name="traceEnabled">是否把 EffectRuntime 诊断信息转发到 Unity Console。</param>
        public EffectSystem(EffectLibrary library, int randomSeed = 1977, bool traceEnabled = false)
        {
            defaultLibrary = library != null ? library : throw new ArgumentNullException(nameof(library));
            this.randomSeed = randomSeed;
            logTrace = traceEnabled;
        }

        /// <summary>
        /// 当前 System 是否已经释放。
        /// </summary>
        public bool IsDisposed => isDisposed;

        /// <summary>
        /// 获取当前单局唯一的效果运行时。
        /// </summary>
        public EffectRuntime Runtime
        {
            get
            {
                ThrowIfDisposed();
                EnsureRuntime();
                return runtime;
            }
        }

        /// <summary>
        /// 获取正式玩法入口使用的默认效果库。
        /// </summary>
        public EffectLibrary DefaultLibrary
        {
            get
            {
                ThrowIfDisposed();
                return defaultLibrary;
            }
        }

        /// <summary>
        /// 在 Entity 初始化前准备 EffectRuntime，保证 EffectLogic 可以立即注册规则。
        /// </summary>
        /// <param name="gameplayKit">持有当前 EffectSystem 的单局 GameplayKit。</param>
        public override void AfterNew(IGameplayKit gameplayKit)
        {
            if (gameplayKit == null)
                throw new ArgumentNullException(nameof(gameplayKit));

            ThrowIfDisposed();
            EnsureRuntime();
            simulationSystem = gameplayKit.GetSystem<ActorSimulationSystem>();
            simulationSystem.SimulationTickCompleted += OnSimulationTickCompleted;
        }

        /// <summary>
        /// EffectRuntime 已由 ActorSimulationSystem 的固定 Tick 后置阶段推进；渲染帧更新不再改变任何战斗时间。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        public override void OnUpdate(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt), dt, "Effect frame delta time must be finite and non-negative.");
        }

        /// <summary>
        /// 释放当前单局全部效果和触发注册；持久化配置资产仍由 Unity 管理。
        /// </summary>
        public override void Dispose()
        {
            if (isDisposed)
                return;

            if (simulationSystem != null) simulationSystem.SimulationTickCompleted -= OnSimulationTickCompleted;
            simulationSystem = null;
            runtime?.Dispose();
            runtime = null;
            isDisposed = true;
        }

        /// <summary>
        /// 延迟创建当前单局唯一的 EffectRuntime，并按配置连接诊断日志。
        /// </summary>
        private void EnsureRuntime()
        {
            if (runtime != null)
                return;

            runtime = new EffectRuntime(randomSeed);
            if (logTrace)
                runtime.Trace += LogTrace;
        }

        /// <summary>在本 Tick 全部 Actor 完成运动与命中解析后推进效果持续时间、周期结算和触发冷却，确保回放与卡帧补步使用同一模拟时钟。</summary>
        private void OnSimulationTickCompleted(long simulationTick, float fixedDeltaTime)
        {
            if (isDisposed || runtime == null) return;
            runtime.Tick(fixedDeltaTime);
        }

        /// <summary>
        /// 将 EffectRuntime 诊断信息转发到 Unity Console。
        /// </summary>
        private static void LogTrace(string message)
        {
            Debug.Log($"[EffectRuntime] {message}");
        }

        /// <summary>
        /// 防止已释放的单局效果系统被重新访问。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(EffectSystem));
        }
    }
}
