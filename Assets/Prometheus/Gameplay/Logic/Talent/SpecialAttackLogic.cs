using System;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    /// <summary>独立负责长按普通攻击蓄力、特殊攻击动画和特殊攻击命中窗口。</summary>
    public sealed class SpecialAttackLogic : PlayerCombatActionLogic
    {
        private SpecialAttackComponent specialAttackComponent;

        /// <summary>特殊攻击使用独立动画所有者。</summary>
        protected override AnimationOwner ActionOwner => AnimationOwner.SpecialAttack;

        /// <summary>获取特殊攻击组件并绑定它唯一拥有的碰撞代理。</summary>
        protected override void OnActionInitialized()
        {
            if (!Entity.TryGetComp(out specialAttackComponent)) throw new InvalidOperationException("SpecialAttackLogic requires SpecialAttackComponent.");
            if (specialAttackComponent.TalentConfig == null) throw new InvalidOperationException("SpecialAttackLogic requires SpecialAttackComponent.TalentConfig.");
            specialAttackComponent.InitializeRuntimeTimer();
            BindHitbox(specialAttackComponent.ColliderProxy);
        }

        /// <summary>只推进特殊攻击蓄力计时，并在达到阈值的唯一帧尝试启动动作。</summary>
        public override void OnUpdate(float dt)
        {
            if (UpdateSpecialAttackCharge(dt)) TryStartSpecialAttack();
        }

        /// <summary>松开攻击键时重置蓄力资格，持续按住达到阈值后只返回一次成功。</summary>
        private bool UpdateSpecialAttackCharge(float dt)
        {
            if (!InputComponent.wasAtkPressed)
            {
                specialAttackComponent.canSpecial = true;
                specialAttackComponent.specialTimer.Reset();
                return false;
            }
            if (!specialAttackComponent.canSpecial) return false;
            specialAttackComponent.specialTimer.OnUpdate(dt);
            if (!specialAttackComponent.specialTimer.IsTimeOut) return false;
            specialAttackComponent.specialTimer.Reset();
            specialAttackComponent.canSpecial = false;
            return true;
        }

        /// <summary>请求特殊攻击动画，并用 TalentConfig 的倍率、偏移和速度建立物理命中上下文。</summary>
        private void TryStartSpecialAttack()
        {
            SpecialAttackExecutor configuration = SpineComponent.animationLib.specialAttackExecutor;
            if (configuration == null) return;
            TalentAbilityValues values = specialAttackComponent.TalentConfig.SpecialAttack.Ability;
            AnimationPlayback playback = SpineComponent.TryPlay(configuration.Semantic, ActionOwner, AnimationPriority.SpecialAttack, false, values.AnimationSpeed, true);
            PlayerCombatHitContext hitContext = new PlayerCombatHitContext(specialAttackComponent.ColliderProxy, values.DamageMultiplier, values.DamageOffset, EffectTag.Attack | EffectTag.SpecialAttack, specialAttackComponent.AbilityId, DamageActionType.SpecialAttack);
            BeginAction(playback, hitContext, configuration.AudioClip, true, configuration.Vfx);
        }

        /// <summary>实体回收时丢弃特殊攻击组件引用。</summary>
        protected override void OnActionDisposed()
        {
            specialAttackComponent = null;
        }
    }
}
