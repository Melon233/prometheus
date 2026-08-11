using System;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    /// <summary>独立负责技能输入、技能动画序列和角色元素技能命中窗口。</summary>
    public sealed class SkillLogic : PlayerCombatActionLogic
    {
        private SkillComponent skillComponent;

        /// <summary>技能使用独立动画所有者。</summary>
        protected override AnimationOwner ActionOwner => AnimationOwner.Skill;

        /// <summary>技能要求 CanUseActiveSkill，因此会同时受到行动门禁和 Silence 约束。</summary>
        protected override LogicControlRequirement RequiredControl => LogicControlRequirement.ActiveSkill;

        /// <summary>获取技能组件并绑定技能独占碰撞代理。</summary>
        protected override void OnActionInitialized()
        {
            if (!Entity.TryGetComp(out skillComponent)) throw new InvalidOperationException("SkillLogic requires SkillComponent.");
            if (skillComponent.TalentConfig == null) throw new InvalidOperationException("SkillLogic requires SkillComponent.TalentConfig.");
            BindHitbox(skillComponent.ColliderProxy);
        }

        /// <summary>仅消费本帧技能输入，并依靠动画优先级拒绝被大招占用的同帧请求。</summary>
        public override void OnUpdate(float dt)
        {
            if (InputComponent.wasSkillPressedThisFrame) TryStartSkill();
        }

        /// <summary>播放技能起手到主体的 AnimationLine 序列，并建立角色元素技能命中上下文。</summary>
        private void TryStartSkill()
        {
            SkillExecutor configuration = SpineComponent.animationLib.skillExecutor;
            if (configuration == null) return;
            TalentAbilityValues values = skillComponent.TalentConfig.Skill;
            AnimationPlayback playback = SpineComponent.TryPlaySequence(configuration.StartSemantic, configuration.Semantic, ActionOwner, AnimationPriority.Skill, false, values.AnimationSpeed, true);
            PlayerCombatHitContext hitContext = new PlayerCombatHitContext(skillComponent.ColliderProxy, values.DamageMultiplier, values.DamageOffset, EffectTag.Attack | EffectTag.Skill, skillComponent.AbilityId, DamageActionType.Skill);
            BeginAction(playback, hitContext, configuration.AudioClip, true, configuration.Vfx);
        }

        /// <summary>实体回收时丢弃技能组件引用。</summary>
        protected override void OnActionDisposed()
        {
            skillComponent = null;
        }
    }
}
