using System;
using UnityEngine;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus
{
    /// <summary>订阅当前单局已经完成结算的伤害事实并播放命中音效，使命中反馈不再依赖受击或死亡动画是否成功切换。</summary>
    public sealed class CombatAudioPresentationSystem : XSystem
    {
        private readonly FmodAudioEvent damageHitAudioEvent;
        private readonly Func<FmodAudioEvent, Vector3, bool> playOneShot;
        private EffectRuntime runtime;
        private bool isDisposed;

        /// <summary>创建战斗音频表现系统；默认播放共享肉体命中事件，可注入兼容签名的播放器用于测试或替换音频后端。</summary>
        /// <param name="hitAudioEvent">所有实际伤害统一使用的命中音频事件。</param>
        /// <param name="audioPlayer">接收音频事件与世界坐标的一次性播放入口；为空时使用 FMOD 运行时。</param>
        public CombatAudioPresentationSystem(FmodAudioEvent hitAudioEvent = FmodAudioEvent.CombatSharedHit_Flesh, Func<FmodAudioEvent, Vector3, bool> audioPlayer = null)
        {
            damageHitAudioEvent = hitAudioEvent;
            playOneShot = audioPlayer ?? FmodAudioRuntime.PlayOneShot;
        }

        /// <summary>在实体创建前订阅当前 GameplayKit 唯一的 EffectRuntime，使本局全部普通、周期和致命伤害共享同一音频入口。</summary>
        /// <param name="gameplayKit">持有当前音频表现系统和效果系统的单局 GameplayKit。</param>
        public override void AfterNew(IGameplayKit gameplayKit)
        {
            if (gameplayKit == null) throw new ArgumentNullException(nameof(gameplayKit));
            if (isDisposed) throw new ObjectDisposedException(nameof(CombatAudioPresentationSystem));
            if (runtime != null) return;
            runtime = gameplayKit.GetSystem<EffectSystem>().Runtime;
            runtime.SignalProcessed += OnSignalProcessed;
        }

        /// <summary>取消只读信号订阅；GameplayKit 按逆注册顺序释放系统，因此该订阅会先于 EffectRuntime 解除。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            if (runtime != null) runtime.SignalProcessed -= OnSignalProcessed;
            runtime = null;
            isDisposed = true;
        }

        /// <summary>仅将大于零的 DamageApplied 视为真实命中，致命标记不会过滤，因此死亡攻击和普通攻击都会恰好播放一次。</summary>
        private void OnSignalProcessed(EffectSignal signal)
        {
            if (signal == null || signal.Type != EffectSignalType.DamageApplied || signal.Value <= 0f) return;
            playOneShot(damageHitAudioEvent, signal.Position);
        }
    }
}
