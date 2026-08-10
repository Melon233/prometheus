using Spine;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Logic
{
    /// <summary>负责玩家攻击输入仲裁、玩法阻塞、命中盒和动画事件；SpineComponent 仅负责 AnimationLine 播放控制。</summary>
    public sealed class TalentLogic : Logic, ITriggerHandler
    {
        private InputComponent inputComponent;
        private SpineComponent spineComponent;
        private AttackComponent attackComponent;
        private SpecialAttackComponent specialAttackComponent;
        private SkillComponent skillComponent;
        private UltimateComponent ultimateComponent;
        private PropertyComponent propertyComponent;
        private EffectComponent effectComponent;
        private VfxComponent vfxComponent;
        /// <summary>记录当前玩家动作贡献的 Root 状态句柄，保证叠加来源能够按身份精确释放。</summary>
        private ControlStateModifier movementLockModifier;
        private AnimationPlayback activePlayback;
        private ColliderProxy activeCollider;
        private AudioClip activeAudio;
        private YefaVfx activeVfx;
        private bool activeHasVfx;
        private bool activeAllowsCombo;

        /// <summary>将攻击碰撞命中转换为效果系统信号，动画系统不直接处理伤害数值。</summary>
        public void OnTriggerEnter(Collider other)
        {
            if (!Entity.IsActive || other == null || !other.CompareTag("Enemy")) return;
            PropertyComponent targetProperty = other.GetComponent<PropertyComponent>();
            if (targetProperty == null || targetProperty.Entity == null || targetProperty.IsDead || !targetProperty.Entity.IsActive)
            {
                Debug.LogWarning($"无法获取敌人 PropertyComponent 或实体绑定：{other.name}");
                return;
            }
            float requestedDamage = propertyComponent.GetCalculatedDamage();
            EffectSignal signal = new EffectSignal(EffectSignalType.HitConfirmed, Entity, targetProperty.Entity, Entity, requestedDamage, requestedDamage, EffectTag.Attack | EffectTag.NormalAttack | EffectTag.Fire, "Player.NormalAttack", position: other.transform.position);
            effectComponent.Runtime.Publish(signal);
        }

        /// <summary>缓存全部玩法组件并建立 ColliderProxy 与实体事件关系。</summary>
        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            Entity.TryGetComp(out inputComponent);
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out attackComponent);
            Entity.TryGetComp(out propertyComponent);
            Entity.TryGetComp(out specialAttackComponent);
            Entity.TryGetComp(out skillComponent);
            Entity.TryGetComp(out ultimateComponent);
            Entity.TryGetComp(out effectComponent);
            Entity.TryGetComp(out vfxComponent);
            attackComponent.atkCollider.handler = this;
            skillComponent.colliderProxy.handler = this;
            ultimateComponent.colliderProxy.handler = this;
            specialAttackComponent.colliderProxy.handler = this;
            effectComponent.RegisterCombatFlowTriggers(Entity);
            DisableAttackColliders();
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override bool CanDisable()
        {
            return false;
        }

        public override void OnEnable()
        {
        }

        /// <summary>按唯一优先顺序消费同帧输入，防止多个独立 if 在同一帧重复覆盖主动画轨道。</summary>
        public override void OnUpdate(float dt)
        {
            if ((attackComponent.elapsedComboTime += dt) > attackComponent.maxComboInterval) attackComponent.nextComboIndex = 0;
            bool specialReady = UpdateSpecialAttackCharge(dt);
            if (propertyComponent.CanUseActiveSkill && inputComponent.wasUltPressedThisFrame)
            {
                TryStartUltimate();
                return;
            }
            if (propertyComponent.CanUseActiveSkill && inputComponent.wasSkillPressedThisFrame)
            {
                TryStartSkill();
                return;
            }
            if (specialReady)
            {
                TryStartSpecialAttack();
                return;
            }
            if (inputComponent.wasAtkPressedThisFrame && attackComponent.canCombo) TryStartNormalAttack();
        }

        /// <summary>更新长按攻击计时并返回本帧是否应尝试启动特殊攻击。</summary>
        private bool UpdateSpecialAttackCharge(float dt)
        {
            if (!inputComponent.wasAtkPressed)
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

        /// <summary>解析当前连段并尝试启动普通攻击；配置无效或被更高优先级动画占用时保持现有状态。</summary>
        private void TryStartNormalAttack()
        {
            AttackExecutor configuration = spineComponent.animationLib.atkExecutor;
            bool moving = inputComponent.moveDir != Vector2.zero;
            if (!configuration.TryGetSelection(attackComponent.nextComboIndex, moving, out AttackAnimationSelection selection)) return;
            spineComponent.SetFaceDir(inputComponent.moveDir);
            AnimationPlayback playback = spineComponent.TryPlay(selection.Semantic, AnimationOwner.PlayerAction, AnimationPriority.Attack, false, propertyComponent.AtkSpeed, true);
            if (!BeginAction(playback, attackComponent.atkCollider, selection.AudioClip, selection.HasVfx, selection.Vfx, true)) return;
            attackComponent.nextComboIndex++;
            int configuredMaxIndex = Mathf.Min(attackComponent.maxComboIndex, Mathf.Max(0, configuration.Count - 1));
            if (attackComponent.nextComboIndex > configuredMaxIndex) attackComponent.nextComboIndex = 0;
            attackComponent.elapsedComboTime = 0f;
        }

        /// <summary>尝试启动特殊攻击，高于普通攻击但低于闪避和主动技能。</summary>
        private void TryStartSpecialAttack()
        {
            SpecialAttackExecutor configuration = spineComponent.animationLib.specialAttackExecutor;
            AnimationPlayback playback = spineComponent.TryPlay(configuration.Semantic, AnimationOwner.PlayerAction, AnimationPriority.SpecialAttack, false, 1f, true);
            BeginAction(playback, specialAttackComponent.colliderProxy, configuration.AudioClip, true, configuration.Vfx, false);
        }

        /// <summary>尝试启动技能起手到主体的 AnimationLine 序列。</summary>
        private void TryStartSkill()
        {
            SkillExecutor configuration = spineComponent.animationLib.skillExecutor;
            AnimationPlayback playback = spineComponent.TryPlaySequence(configuration.StartSemantic, configuration.Semantic, AnimationOwner.PlayerAction, AnimationPriority.Skill, false, 1f, true);
            BeginAction(playback, skillComponent.colliderProxy, configuration.AudioClip, true, configuration.Vfx, false);
        }

        /// <summary>尝试启动最高玩家主动动作优先级的终结技。</summary>
        private void TryStartUltimate()
        {
            UltimateExecutor configuration = spineComponent.animationLib.ultimateExecutor;
            AnimationPlayback playback = spineComponent.TryPlay(configuration.Semantic, AnimationOwner.PlayerAction, AnimationPriority.Ultimate, false, 1f, true);
            BeginAction(playback, ultimateComponent.colliderProxy, configuration.AudioClip, true, configuration.Vfx, false);
        }

        /// <summary>绑定一次成功播放的玩法上下文并通过来源句柄贡献 Root 状态；被拒绝的播放不会改变任何玩法状态。</summary>
        private bool BeginAction(AnimationPlayback playback, ColliderProxy colliderProxy, AudioClip audioClip, bool hasVfx, YefaVfx vfx, bool allowsCombo)
        {
            if (playback == null) return false;
            activePlayback = playback;
            activeCollider = colliderProxy;
            activeAudio = audioClip;
            activeHasVfx = hasVfx;
            activeVfx = vfx;
            activeAllowsCombo = allowsCombo;
            attackComponent.currentAnimation = playback;
            if (allowsCombo) attackComponent.canCombo = false;
            playback.EventReceived += OnAnimationEvent;
            playback.Finished += OnAnimationFinished;
            AcquireMovementLock();
            return true;
        }

        /// <summary>由当前 Logic 解释命中窗口事件，并在窗口开始时触发对应音效和特效。</summary>
        private void OnAnimationEvent(AnimationPlayback source, Spine.Event animationEvent)
        {
            if (!ReferenceEquals(source, activePlayback)) return;
            if (animationEvent.Data.Name == spineComponent.animationLib.hitStart)
            {
                SetColliderEnabled(activeCollider, true);
                if (activeHasVfx) vfxComponent.Play(activeVfx);
                if (activeAudio != null) AudioKit.Ins.Play(activeAudio);
            }
            else if (animationEvent.Data.Name == spineComponent.animationLib.hitEnd)
            {
                SetColliderEnabled(activeCollider, false);
                if (activeAllowsCombo) attackComponent.canCombo = true;
            }
        }

        /// <summary>自然完成和任意优先级抢占共享同一个幂等清理入口。</summary>
        private void OnAnimationFinished(AnimationPlayback source, AnimationEndReason reason)
        {
            if (!ReferenceEquals(source, activePlayback)) return;
            SetColliderEnabled(activeCollider, false);
            activePlayback = null;
            activeCollider = null;
            activeAudio = null;
            activeHasVfx = false;
            activeAllowsCombo = false;
            attackComponent.currentAnimation = null;
            attackComponent.canCombo = true;
            ReleaseMovementLock();
        }

        /// <summary>控制状态禁用 TalentLogic 时主动停止自己的动画并立即关闭全部攻击碰撞体。</summary>
        public override void OnDisable()
        {
            spineComponent.Stop(AnimationOwner.PlayerAction);
            DisableAttackColliders();
            attackComponent.currentAnimation = null;
            ReleaseMovementLock();
        }

        /// <summary>回收时关闭碰撞体、解绑代理并精确释放当前动作持有的 Root 状态。</summary>
        public override void OnDispose()
        {
            DisableAttackColliders();
            if (attackComponent != null && attackComponent.atkCollider != null && ReferenceEquals(attackComponent.atkCollider.handler, this)) attackComponent.atkCollider.handler = null;
            if (specialAttackComponent != null && specialAttackComponent.colliderProxy != null && ReferenceEquals(specialAttackComponent.colliderProxy.handler, this)) specialAttackComponent.colliderProxy.handler = null;
            if (skillComponent != null && skillComponent.colliderProxy != null && ReferenceEquals(skillComponent.colliderProxy.handler, this)) skillComponent.colliderProxy.handler = null;
            if (ultimateComponent != null && ultimateComponent.colliderProxy != null && ReferenceEquals(ultimateComponent.colliderProxy.handler, this)) ultimateComponent.colliderProxy.handler = null;
            ReleaseMovementLock();
            activePlayback = null;
            effectComponent = null;
        }

        /// <summary>首次进入玩家动作时添加一份来源明确的 Root 状态，连续动作替换复用同一份状态贡献。</summary>
        private void AcquireMovementLock()
        {
            if (movementLockModifier == null) movementLockModifier = propertyComponent.AddControlStateModifier(ControlState.Root);
        }

        /// <summary>动作自然完成、被 Stun 打断或实体回收时精确移除当前动作持有的 Root 状态。</summary>
        private void ReleaseMovementLock()
        {
            if (movementLockModifier == null) return;
            propertyComponent.RemoveControlStateModifier(movementLockModifier);
            movementLockModifier = null;
        }

        /// <summary>关闭玩家全部攻击命中盒，兼容部分预制体尚未初始化 ColliderProxy.cod 的构造阶段。</summary>
        private void DisableAttackColliders()
        {
            SetColliderEnabled(attackComponent == null ? null : attackComponent.atkCollider, false);
            SetColliderEnabled(specialAttackComponent == null ? null : specialAttackComponent.colliderProxy, false);
            SetColliderEnabled(skillComponent == null ? null : skillComponent.colliderProxy, false);
            SetColliderEnabled(ultimateComponent == null ? null : ultimateComponent.colliderProxy, false);
        }

        /// <summary>安全切换 ColliderProxy 内部碰撞体。</summary>
        private static void SetColliderEnabled(ColliderProxy colliderProxy, bool enabled)
        {
            if (colliderProxy != null && colliderProxy.cod != null) colliderProxy.cod.enabled = enabled;
        }
    }
}
