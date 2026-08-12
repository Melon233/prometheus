using System;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Logic
{
    /// <summary>独立负责普通攻击输入、连段推进、逐段碰撞体选择和逐段 HitConfirmed 信息。</summary>
    public sealed class NormalAttackLogic : PlayerCombatActionLogic
    {
        private AttackComponent attackComponent;

        /// <summary>普通攻击使用独立动画所有者，停止时不会误伤技能或大招会话。</summary>
        protected override AnimationOwner ActionOwner => AnimationOwner.NormalAttack;

        /// <summary>绑定旧碰撞体回退和全部逐段碰撞体，确保任何未激活段在初始化时保持关闭。</summary>
        protected override void OnActionInitialized()
        {
            if (!Entity.TryGetComp(out attackComponent)) throw new InvalidOperationException("NormalAttackLogic requires AttackComponent.");
            if (attackComponent.TalentConfig == null) throw new InvalidOperationException("NormalAttackLogic requires AttackComponent.TalentConfig.");
            BindHitbox(attackComponent.PrimaryHitbox);
            for (int stageIndex = 0; stageIndex < attackComponent.ConfiguredHitCount; stageIndex++) BindHitbox(attackComponent.GetConfiguredHitbox(stageIndex));
        }

        /// <summary>更新连段超时，并只在本帧普通攻击输入且当前允许接续时尝试启动下一段。</summary>
        public override void OnUpdate(float dt)
        {
            if ((attackComponent.elapsedComboTime += dt) > attackComponent.TalentConfig.NormalAttack.ComboInterval) attackComponent.nextComboIndex = 0;
            if (InputComponent.wasAtkPressedThisFrame && attackComponent.canCombo) TryStartNormalAttack();
        }

        /// <summary>按同一连段下标解析动画与命中配置，并把本段伤害倍率固化到当前动作上下文。</summary>
        private void TryStartNormalAttack()
        {
            AttackExecutor configuration = SpineComponent.animationLib.atkExecutor;
            int stageIndex = attackComponent.nextComboIndex;
            bool moving = InputComponent.moveDir != Vector2.zero;
            if (configuration == null || !configuration.TryGetSelection(stageIndex, moving, out AttackAnimationSelection animationSelection)) return;
            if (!attackComponent.TryGetHitSelection(stageIndex, out NormalAttackHitSelection hitSelection))
            {
                Debug.LogWarning($"普通攻击第 {stageIndex + 1} 段缺少有效命中配置，动作不会启动。", attackComponent);
                return;
            }
            AnimationPlayback playback = SpineComponent.TryPlay(animationSelection.Semantic, ActionOwner, AnimationPriority.Attack, false, PropertyComponent.AtkSpeed, true);
            PlayerCombatHitContext hitContext = new PlayerCombatHitContext(hitSelection.ColliderProxy, hitSelection.DamageMultiplier, hitSelection.DamageOffset, EffectTag.Attack | EffectTag.NormalAttack | hitSelection.AdditionalTags, hitSelection.AbilityId, DamageActionType.NormalAttack);
            if (!BeginAction(playback, hitContext, animationSelection.HasVfx, animationSelection.Vfx)) return;
            SpineComponent.SetFaceDir(InputComponent.moveDir);
            attackComponent.nextComboIndex++;
            int configuredStageCount = Mathf.Min(configuration.Count, Mathf.Min(attackComponent.ConfiguredHitCount, attackComponent.TalentConfig.NormalAttack.StageCount));
            int configuredMaxIndex = Mathf.Max(0, configuredStageCount - 1);
            if (attackComponent.nextComboIndex > configuredMaxIndex) attackComponent.nextComboIndex = 0;
            attackComponent.elapsedComboTime = 0f;
        }

        /// <summary>普通攻击成功取得动画所有权后记录当前会话并关闭接续窗口。</summary>
        protected override void OnActionStarted(AnimationPlayback playback)
        {
            attackComponent.currentAnimation = playback;
            attackComponent.canCombo = false;
        }

        /// <summary>当前段命中窗口结束时开放下一段普通攻击输入。</summary>
        protected override void OnHitWindowClosed()
        {
            attackComponent.canCombo = true;
        }

        /// <summary>当前段自然结束或被更高优先级动作抢占时恢复普通攻击接续状态。</summary>
        protected override void OnActionEnded(AnimationPlayback playback, AnimationEndReason reason)
        {
            if (ReferenceEquals(attackComponent.currentAnimation, playback)) attackComponent.currentAnimation = null;
            attackComponent.canCombo = true;
        }

        /// <summary>实体回收时丢弃普通攻击组件引用。</summary>
        protected override void OnActionDisposed()
        {
            attackComponent = null;
        }
    }
}
