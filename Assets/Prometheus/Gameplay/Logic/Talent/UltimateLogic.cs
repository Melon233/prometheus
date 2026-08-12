using System;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    /// <summary>独立负责大招输入、最高玩家主动动作优先级和角色元素大招命中窗口。</summary>
    public sealed class UltimateLogic : PlayerCombatActionLogic
    {
        private UltimateComponent ultimateComponent;

        /// <summary>大招使用独立动画所有者。</summary>
        protected override AnimationOwner ActionOwner => AnimationOwner.Ultimate;

        /// <summary>大招要求 CanUseActiveSkill，因此会同时受到行动门禁和 Silence 约束。</summary>
        protected override LogicControlRequirement RequiredControl => LogicControlRequirement.ActiveSkill;

        /// <summary>获取大招组件并绑定大招独占碰撞代理。</summary>
        protected override void OnActionInitialized()
        {
            if (!Entity.TryGetComp(out ultimateComponent)) throw new InvalidOperationException("UltimateLogic requires UltimateComponent.");
            if (ultimateComponent.TalentConfig == null) throw new InvalidOperationException("UltimateLogic requires UltimateComponent.TalentConfig.");
            BindHitbox(ultimateComponent.ColliderProxy);
        }

        /// <summary>仅消费本帧大招输入；PlayerEntity 注册顺序保证它先于其他攻击动作尝试取得主轨。</summary>
        public override void OnUpdate(float dt)
        {
            if (InputComponent.wasUltPressedThisFrame) TryStartUltimate();
        }

        /// <summary>播放大招 AnimationLine，并建立角色元素大招命中上下文。</summary>
        private void TryStartUltimate()
        {
            if (!ultimateComponent.CanRelease(PropertyComponent)) return;
            UltimateExecutor configuration = SpineComponent.animationLib.ultimateExecutor;
            if (configuration == null) return;
            TalentAbilityValues values = ultimateComponent.TalentConfig.Ultimate;
            AnimationPlayback playback = SpineComponent.TryPlay(configuration.Semantic, ActionOwner, AnimationPriority.Ultimate, false, values.AnimationSpeed, true);
            PlayerCombatHitContext hitContext = new PlayerCombatHitContext(ultimateComponent.ColliderProxy, values.DamageMultiplier, values.DamageOffset, EffectTag.Attack | EffectTag.Ultimate, ultimateComponent.AbilityId, DamageActionType.Ultimate);
            if (!BeginAction(playback, hitContext, true, configuration.Vfx)) return;
            PropertyComponent.ConsumeAllUltEnergy();
            ultimateComponent.BeginCooldown();
        }

        /// <summary>实体回收时丢弃大招组件引用。</summary>
        protected override void OnActionDisposed()
        {
            ultimateComponent = null;
        }
    }
}
