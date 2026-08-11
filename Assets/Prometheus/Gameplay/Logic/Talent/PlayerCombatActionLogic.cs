using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Logic
{
    /// <summary>保存一次玩家动作已经解析完成的碰撞体、伤害倍率和 EffectSignal 语义。</summary>
    public readonly struct PlayerCombatHitContext
    {
        /// <summary>创建一份在当前动画会话期间保持不变的命中上下文。</summary>
        public PlayerCombatHitContext(ColliderProxy colliderProxy, float damageMultiplier, float damageOffset, EffectTag tags, string abilityId, DamageActionType damageActionType)
        {
            ColliderProxy = colliderProxy;
            DamageMultiplier = Mathf.Max(0f, damageMultiplier);
            DamageOffset = damageOffset;
            Tags = tags;
            AbilityId = abilityId ?? string.Empty;
            DamageActionType = damageActionType;
        }

        /// <summary>获取当前命中窗口唯一允许触发信号的碰撞代理。</summary>
        public ColliderProxy ColliderProxy { get; }

        /// <summary>获取当前动作对最终攻击伤害应用的非负倍率。</summary>
        public float DamageMultiplier { get; }

        /// <summary>获取当前动作在倍率结算后追加的固定伤害偏移。</summary>
        public float DamageOffset { get; }

        /// <summary>获取当前动作发布到 EffectSignal 的完整标签。</summary>
        public EffectTag Tags { get; }

        /// <summary>获取当前动作发布到 EffectSignal 的能力编号。</summary>
        public string AbilityId { get; }

        /// <summary>获取当前动作解析伤害属性时使用的动作类别。</summary>
        public DamageActionType DamageActionType { get; }

        /// <summary>把角色已经完成通用攻击计算的伤害乘以当前动作段倍率，并约束结果不小于零。</summary>
        public float CalculateRequestedDamage(float calculatedDamage)
        {
            return Mathf.Max(0f, Mathf.Max(0f, calculatedDamage) * DamageMultiplier + DamageOffset);
        }
    }

    /// <summary>集中管理玩家战斗动作共享的动画会话、命中窗口、移动锁、转向锁和 HitConfirmed 发布链。</summary>
    public abstract class PlayerCombatActionLogic : Logic, ITriggerHandler
    {
        private readonly HashSet<ColliderProxy> boundHitboxes = new HashSet<ColliderProxy>();
        private ControlStateModifier movementLockModifier;
        private AnimationPlayback activePlayback;
        private PlayerCombatHitContext activeHitContext;
        private AudioClip activeAudio;
        private YefaVfx activeVfx;
        private bool activeHasVfx;
        private bool isRotationLocked;

        /// <summary>获取玩家输入组件，供具体动作 Logic 消费自己的输入。</summary>
        protected InputComponent InputComponent { get; private set; }

        /// <summary>获取角色统一动画播放器。</summary>
        protected SpineComponent SpineComponent { get; private set; }

        /// <summary>获取角色属性组件。</summary>
        protected PropertyComponent PropertyComponent { get; private set; }

        /// <summary>获取当前动作正在持有的动画会话。</summary>
        protected AnimationPlayback ActivePlayback => activePlayback;

        /// <summary>获取具体动作独占的动画所有者，防止一个 Logic 停止另一个动作。</summary>
        protected abstract AnimationOwner ActionOwner { get; }

        /// <summary>获取当前动作参与 Entity 控制状态门禁时需要的能力。</summary>
        protected virtual LogicControlRequirement RequiredControl => LogicControlRequirement.Act;

        /// <summary>缓存共享组件并把具体动作的碰撞代理交给派生 Logic 绑定。</summary>
        public sealed override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = RequiredControl;
            RequireComponent(out InputComponent inputComponent);
            RequireComponent(out SpineComponent spineComponent);
            RequireComponent(out PropertyComponent propertyComponent);
            RequireComponent(out EffectComponent effectComponent);
            RequireComponent(out VfxComponent vfxComponent);
            InputComponent = inputComponent;
            SpineComponent = spineComponent;
            PropertyComponent = propertyComponent;
            EffectComponent = effectComponent;
            VfxComponent = vfxComponent;
            OnActionInitialized();
        }

        /// <summary>动作输入监听在实体存活且控制状态允许期间始终启用。</summary>
        public sealed override bool CanEnable()
        {
            return true;
        }

        /// <summary>动作 Logic 只由阻塞、控制状态或实体生命周期停用。</summary>
        public sealed override bool CanDisable()
        {
            return false;
        }

        /// <summary>具体动作按帧检查输入，启用跃迁本身不立即播放动作。</summary>
        public sealed override void OnEnable()
        {
        }

        /// <summary>控制状态或玩法阻塞停用当前 Logic 时，只停止该 Logic 自己持有的动作。</summary>
        public sealed override void OnDisable()
        {
            StopActiveAction(AnimationEndReason.Stopped);
        }

        /// <summary>回收时停止当前动作、关闭并解绑全部所属碰撞代理，同时释放自身控制状态贡献。</summary>
        public sealed override void OnDispose()
        {
            StopActiveAction(AnimationEndReason.Disposed);
            foreach (ColliderProxy hitbox in boundHitboxes)
            {
                SetHitboxEnabled(hitbox, false);
                if (hitbox != null && ReferenceEquals(hitbox.handler, this)) hitbox.handler = null;
            }
            boundHitboxes.Clear();
            ReleaseMovementLock();
            ReleaseRotationLock();
            OnActionDisposed();
            InputComponent = null;
            SpineComponent = null;
            PropertyComponent = null;
            EffectComponent = null;
            VfxComponent = null;
        }

        /// <summary>验证回调来源就是当前命中窗口的碰撞体，再把逐段伤害倍率和命中语义发布为 HitConfirmed。</summary>
        public void OnTriggerEnter(ColliderProxy source, Collider other)
        {
            if (!Entity.IsActive || activePlayback == null || source == null || other == null || !ReferenceEquals(source, activeHitContext.ColliderProxy)) return;
            if (source.cod != null && !source.cod.enabled) return;
            PropertyComponent targetProperty = other.GetComponentInParent<PropertyComponent>();
            if (targetProperty == null || targetProperty.Entity == null || targetProperty.IsDead || !targetProperty.Entity.IsActive || !targetProperty.CompareTag("Enemy")) return;
            float requestedDamage = activeHitContext.CalculateRequestedDamage(PropertyComponent.GetCalculatedDamage());
            DamageAttribute damageAttribute = PropertyComponent.ResolveDamageAttribute(activeHitContext.DamageActionType);
            EffectSignal signal = new EffectSignal(EffectSignalType.HitConfirmed, Entity, targetProperty.Entity, Entity, requestedDamage, requestedDamage, activeHitContext.Tags, activeHitContext.AbilityId, position: other.transform.position, damageAttribute: damageAttribute, damageActionType: activeHitContext.DamageActionType);
            EffectComponent.Runtime.Publish(signal);
        }

        /// <summary>由具体动作绑定一枚自己拥有的碰撞代理，并在初始化时确保碰撞体关闭。</summary>
        protected void BindHitbox(ColliderProxy hitbox)
        {
            if (hitbox == null || !boundHitboxes.Add(hitbox)) return;
            if (hitbox.handler != null && !ReferenceEquals(hitbox.handler, this)) throw new InvalidOperationException($"ColliderProxy '{hitbox.name}' is already owned by another trigger handler.");
            hitbox.handler = this;
            SetHitboxEnabled(hitbox, false);
        }

        /// <summary>在动画已经成功取得主轨所有权后建立一次动作上下文，并锁定移动与转向。</summary>
        protected bool BeginAction(AnimationPlayback playback, PlayerCombatHitContext hitContext, AudioClip audioClip, bool hasVfx, YefaVfx vfx)
        {
            if (playback == null) return false;
            activePlayback = playback;
            activeHitContext = hitContext;
            activeAudio = audioClip;
            activeHasVfx = hasVfx;
            activeVfx = vfx;
            playback.EventReceived += OnAnimationEvent;
            playback.Finished += OnAnimationFinished;
            AcquireMovementLock();
            AcquireRotationLock();
            OnActionStarted(playback);
            return true;
        }

        /// <summary>具体动作初始化入口，用于获取专属组件并绑定全部可能使用的碰撞代理。</summary>
        protected abstract void OnActionInitialized();

        /// <summary>具体动作成功建立会话后的扩展入口。</summary>
        protected virtual void OnActionStarted(AnimationPlayback playback)
        {
        }

        /// <summary>具体动作命中窗口关闭后的扩展入口。</summary>
        protected virtual void OnHitWindowClosed()
        {
        }

        /// <summary>具体动作会话自然完成、被抢占或主动停止后的统一扩展入口。</summary>
        protected virtual void OnActionEnded(AnimationPlayback playback, AnimationEndReason reason)
        {
        }

        /// <summary>具体动作最终回收时清理自己的组件引用。</summary>
        protected virtual void OnActionDisposed()
        {
        }

        /// <summary>解释公共命中窗口事件，并使用当前动作独立配置的碰撞体、音效和特效。</summary>
        private void OnAnimationEvent(AnimationPlayback source, Spine.Event animationEvent)
        {
            if (!ReferenceEquals(source, activePlayback) || animationEvent == null || animationEvent.Data == null) return;
            if (animationEvent.Data.Name == SpineComponent.animationLib.hitStart)
            {
                SetHitboxEnabled(activeHitContext.ColliderProxy, true);
                if (activeHasVfx) VfxComponent.Play(activeVfx);
                if (activeAudio != null) AudioKit.Ins.Play(activeAudio);
            }
            else if (animationEvent.Data.Name == SpineComponent.animationLib.hitEnd)
            {
                SetHitboxEnabled(activeHitContext.ColliderProxy, false);
                OnHitWindowClosed();
            }
        }

        /// <summary>动画系统结束当前会话时进入幂等清理，并把准确结束原因交给具体动作。</summary>
        private void OnAnimationFinished(AnimationPlayback source, AnimationEndReason reason)
        {
            if (!ReferenceEquals(source, activePlayback)) return;
            FinishActiveAction(source, reason);
        }

        /// <summary>请求 SpineComponent 只停止当前具体动作所有者，失败时仍清理已失去主轨的本地上下文。</summary>
        private void StopActiveAction(AnimationEndReason reason)
        {
            AnimationPlayback playback = activePlayback;
            if (playback == null)
            {
                ReleaseMovementLock();
                ReleaseRotationLock();
                return;
            }
            if (SpineComponent != null && SpineComponent.Stop(ActionOwner, reason)) return;
            FinishActiveAction(playback, reason);
        }

        /// <summary>关闭当前命中盒、解绑动画回调并精确释放本次动作持有的控制状态句柄。</summary>
        private void FinishActiveAction(AnimationPlayback playback, AnimationEndReason reason)
        {
            if (!ReferenceEquals(playback, activePlayback)) return;
            SetHitboxEnabled(activeHitContext.ColliderProxy, false);
            playback.EventReceived -= OnAnimationEvent;
            playback.Finished -= OnAnimationFinished;
            activePlayback = null;
            activeHitContext = default;
            activeAudio = null;
            activeHasVfx = false;
            activeVfx = default;
            ReleaseMovementLock();
            ReleaseRotationLock();
            OnActionEnded(playback, reason);
        }

        /// <summary>首次进入动作时添加一份来源明确的 Root 状态，动作替换后由各自 Logic 精确交接。</summary>
        private void AcquireMovementLock()
        {
            if (movementLockModifier == null) movementLockModifier = PropertyComponent.AddControlStateModifier(ControlState.Root);
        }

        /// <summary>动作结束或回收时只移除当前 Logic 自己添加的 Root 状态。</summary>
        private void ReleaseMovementLock()
        {
            if (movementLockModifier == null || PropertyComponent == null) return;
            PropertyComponent.RemoveControlStateModifier(movementLockModifier);
            movementLockModifier = null;
        }

        /// <summary>动作成功开始时为 RotateLogic 增加一层来源明确的阻塞。</summary>
        private void AcquireRotationLock()
        {
            if (isRotationLocked || !Entity.TryGetLogic(out RotateLogic _)) return;
            Entity.BlockLogic<RotateLogic>();
            isRotationLocked = true;
        }

        /// <summary>动作完成、被抢占或回收时只释放当前 Logic 添加的转向阻塞。</summary>
        private void ReleaseRotationLock()
        {
            if (!isRotationLocked || Entity == null) return;
            Entity.UnBlockLogic<RotateLogic>();
            isRotationLocked = false;
        }

        /// <summary>安全切换 ColliderProxy 内部碰撞体，兼容 Awake 尚未运行的编辑器检查阶段。</summary>
        private static void SetHitboxEnabled(ColliderProxy hitbox, bool enabled)
        {
            if (hitbox != null && hitbox.cod != null) hitbox.cod.enabled = enabled;
        }

        /// <summary>从 Entity 获取具体组件，并在玩家预制体缺少依赖时抛出可定位的配置错误。</summary>
        private void RequireComponent<T>(out T component) where T : IComponent
        {
            if (!Entity.TryGetComp(out component)) throw new InvalidOperationException($"Player combat Logic '{GetType().Name}' requires component '{typeof(T).Name}'.");
        }

        /// <summary>获取 Effect 运行时组件，仅由共享命中发布链使用。</summary>
        private EffectComponent EffectComponent { get; set; }

        /// <summary>获取玩家特效组件，仅由公共命中窗口事件使用。</summary>
        private VfxComponent VfxComponent { get; set; }
    }
}
