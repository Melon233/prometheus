using System;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;
using Xuan.Prometheus.Stamina;
using Cfg = global::Prometheus.Config;

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

        /// <summary>
        /// 请求重击动画并扣除体力。
        ///
        /// 体力在**动作真正开始之后**才扣：先扣再发现动画抢不到主轨的话，玩家会白白损失一次重击。
        /// 因此顺序是「查得起 → 起手 → 扣除」，而不是「扣除 → 起手」。
        /// </summary>
        private void TryStartSpecialAttack()
        {
            SpecialAttackExecutor configuration = SpineComponent.animationLib.specialAttackExecutor;
            if (configuration == null) return;

            string talentId = ResolveTalentId("SpecialAttack");
            Cfg.AttackSegmentRow segment = SegmentTable.Get(talentId, 0, 0);
            IStaminaSystem staminaSystem = Core.Gameplay.GetSystem<IStaminaSystem>();
            if (!staminaSystem.CanAfford(Entity.EntityId, segment.StaminaCost)) return;

            TalentAbilityValues values = specialAttackComponent.TalentConfig.SpecialAttack.Ability;
            AnimationPlayback playback = SpineComponent.TryPlay(configuration.Semantic, ActionOwner, AnimationPriority.SpecialAttack, false, values.AnimationSpeed, true);
            PlayerCombatHitContext hitContext = new PlayerCombatHitContext(specialAttackComponent.ColliderProxy, talentId, 0, EffectTag.Attack | EffectTag.SpecialAttack, DamageActionType.SpecialAttack, specialAttackComponent.TalentScale);
            if (!BeginAction(playback, hitContext, true, configuration.Vfx)) return;
            staminaSystem.TryConsume(Entity.EntityId, segment.StaminaCost);
        }

        /// <summary>实体回收时丢弃特殊攻击组件引用。</summary>
        protected override void OnActionDisposed()
        {
            specialAttackComponent = null;
        }
    }
}
