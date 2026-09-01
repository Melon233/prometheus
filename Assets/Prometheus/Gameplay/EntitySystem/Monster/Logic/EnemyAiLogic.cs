using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Ai
{
    /// <summary>
    /// 将通用 EnemyAiBrain 接入 Prometheus Entity、Spine、CharacterController 和 EffectSignal 战斗链路。
    /// </summary>
    public sealed class EnemyAiLogic : Logic.Logic, IEnemyAiAgent, ITriggerHandler
    {
        private readonly Collider[] perceptionBuffer = new Collider[16];
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private EnemyAiComponent aiComponent;
        private PropertyComponent propertyComponent;
        private AttackComponent attackComponent;
        private SpineComponent spineComponent;
        private MotionComponent motionComponent;
        private VfxComponent vfxComponent;
        private EffectComponent effectComponent;
        private EventComponent eventComponent;
        private EnemyAiBrain brain;
        private AnimationPlayback attackPlayback;
        private Action<bool> attackFinished;
        private bool logicEnabled;
        private bool brainRunning;
        private bool dead;

        /// <summary>获取当前宿主 Brain，完成 AfterNew 前为空。</summary>
        public EnemyAiBrain Brain => brain;

        /// <inheritdoc />
        public Vector3 Position => Entity.bindGo.transform.position;

        /// <inheritdoc />
        public bool CanAct => Entity != null && Entity.IsActive && propertyComponent != null && propertyComponent.CanAct && !propertyComponent.IsDead && !dead;

        /// <inheritdoc />
        public bool CanMove => Entity != null && Entity.IsActive && propertyComponent != null && propertyComponent.CanMove && !propertyComponent.IsDead && !dead;

        /// <summary>
        /// 缓存全部运行时适配器、创建独立 Brain，并订阅死亡生命周期事件；受击暂停由 Entity 行动能力门禁统一处理。
        /// </summary>
        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.Act;
            RequireComponent(out aiComponent);
            RequireComponent(out propertyComponent);
            RequireComponent(out attackComponent);
            RequireComponent(out spineComponent);
            RequireComponent(out motionComponent);
            RequireComponent(out vfxComponent);
            RequireComponent(out effectComponent);
            RequireComponent(out eventComponent);
            if (aiComponent.Definition == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' does not reference an EnemyAiDefinition.");
            if (aiComponent.CharacterController == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' does not contain a CharacterController for Enemy AI movement.");
            if (motionComponent.cc == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' MotionComponent does not reference a CharacterController.");
            if (!ReferenceEquals(motionComponent.cc, aiComponent.CharacterController)) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' must use the same CharacterController in MotionComponent and EnemyAiComponent.");
            if (attackComponent.PrimaryHitbox == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' does not contain an attack ColliderProxy.");
            attackComponent.PrimaryHitbox.handler = this;
            if (attackComponent.PrimaryHitbox.cod != null) attackComponent.PrimaryHitbox.cod.enabled = false;
            eventComponent.AddListener<DieEvent>(OnDie);
            brain = new EnemyAiBrain(aiComponent.Definition, this, Entity.bindGo.GetInstanceID());
        }

        /// <summary>只要实体未死亡且行动能力门禁允许，就允许 Entity 调度器启用 AI 宿主。</summary>
        public override bool CanEnable()
        {
            return !dead;
        }

        /// <summary>死亡后主动退出 AI 宿主；Stun 和 Attacked 由 Entity 的行动能力门禁停用当前 Logic。</summary>
        public override bool CanDisable()
        {
            return dead;
        }

        /// <summary>记录 Entity 调度器已经允许 AI 运行，并统一刷新挂起状态。</summary>
        public override void OnEnable()
        {
            logicEnabled = true;
            RefreshBrainRunningState();
        }

        /// <summary>宿主退出时暂停 Brain 并取消攻击窗口。</summary>
        public override void OnDisable()
        {
            logicEnabled = false;
            RefreshBrainRunningState();
        }

        /// <summary>Logic 通过行动能力门禁后才把 Unity 帧时间交给独立 Brain。</summary>
        public override void OnUpdate(float dt)
        {
            brain?.Tick(dt);
        }

        /// <summary>对称取消事件、碰撞代理和 Brain 生命周期；重复调用保持安全。</summary>
        public override void OnDispose()
        {
            if (eventComponent != null)
            {
                eventComponent.RemoveListener<DieEvent>(OnDie);
            }

            if (attackComponent != null && attackComponent.PrimaryHitbox != null && ReferenceEquals(attackComponent.PrimaryHitbox.handler, this)) attackComponent.PrimaryHitbox.handler = null;
            brain?.Dispose();
            brain = null;
            brainRunning = false;
            StopHorizontalMotion();
            DisableAttackCollider();
        }

        /// <inheritdoc />
        public bool TryAcquireTarget(float radius, int layerMask, string requiredTag, out Transform target)
        {
            int count = Physics.OverlapSphereNonAlloc(Position, radius, perceptionBuffer, layerMask, QueryTriggerInteraction.UseGlobal);
            Entity selectedEntity = null;
            float selectedDistanceSquared = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                Collider candidateCollider = perceptionBuffer[index];
                perceptionBuffer[index] = null;
                if (candidateCollider == null) continue;
                if (!TryResolveProperty(candidateCollider, out Entity candidateEntity, out PropertyComponent candidateProperty) || !IsPropertyTargetValid(candidateEntity, candidateProperty, requiredTag)) continue;
                float distanceSquared = (candidateEntity.bindGo.transform.position - Position).sqrMagnitude;
                if (distanceSquared >= selectedDistanceSquared) continue;
                selectedEntity = candidateEntity;
                selectedDistanceSquared = distanceSquared;
            }

            target = selectedEntity == null ? null : selectedEntity.bindGo.transform;
            return target != null;
        }

        /// <inheritdoc />
        public bool IsTargetValid(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return false;
            Collider targetCollider = target.GetComponent<Collider>();
            return TryResolveProperty(targetCollider, out Entity targetEntity, out PropertyComponent targetProperty) && IsPropertyTargetValid(targetEntity, targetProperty, aiComponent.Definition.TargetTag);
        }

        /// <inheritdoc />
        public void Move(Vector3 worldDirection, float speed, float deltaTime)
        {
            if (!CanMove || deltaTime <= 0f || speed <= 0f)
            {
                StopHorizontalMotion();
                return;
            }
            motionComponent.curVelo.x = worldDirection.x * speed;
            motionComponent.curVelo.z = worldDirection.z * speed;
        }

        /// <inheritdoc />
        public void StopMovement()
        {
            StopHorizontalMotion();
            if (spineComponent != null) spineComponent.Stop(AnimationOwner.GroundMove);
        }

        /// <inheritdoc />
        public void Face(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude > 0.000001f) spineComponent.SetFaceDir(worldDirection.x);
        }

        /// <inheritdoc />
        public void PlayIdle()
        {
            if (dead || spineComponent.animationLib == null) return;
            spineComponent.TryPlay(AnimationSemantic.Idle, AnimationOwner.Idle, AnimationPriority.Idle, true);
        }

        /// <inheritdoc />
        public void PlayMove()
        {
            if (dead || spineComponent.animationLib == null || spineComponent.animationLib.groundMoveExecutor == null) return;
            spineComponent.TryPlay(spineComponent.animationLib.groundMoveExecutor.GetSemantic(MoveMode.Run), AnimationOwner.GroundMove, AnimationPriority.Locomotion, true);
        }

        /// <inheritdoc />
        public bool TryStartAttack(Action<bool> onFinished)
        {
            if (!CanAct || attackPlayback != null || spineComponent.animationLib == null || spineComponent.animationLib.atkExecutor == null) return false;
            if (!spineComponent.animationLib.atkExecutor.TryGetSelection(0, false, out AttackAnimationSelection selection)) return false;
            AnimationPlayback newPlayback = spineComponent.TryPlay(selection.Semantic, AnimationOwner.EnemyAction, AnimationPriority.Attack, false, 1f, true);
            if (newPlayback == null) return false;
            attackPlayback = newPlayback;
            attackFinished = onFinished;
            hitTargets.Clear();
            newPlayback.CommandReceived += OnAttackAnimationCommand;
            newPlayback.Finished += OnAttackFinished;
            return true;
        }

        /// <inheritdoc />
        public void CancelAttack()
        {
            DisableAttackCollider();
            if (attackPlayback == null)
            {
                attackFinished = null;
                hitTargets.Clear();
                return;
            }
            spineComponent.Stop(AnimationOwner.EnemyAction);
        }

        /// <summary>
        /// 将攻击碰撞命中转换为 EffectSignal，并确保一次攻击不会重复命中同一 PropertyComponent。
        /// </summary>
        public void OnTriggerEnter(ColliderProxy source, Collider other)
        {
            if (!Entity.IsActive || dead || attackPlayback == null || source == null || other == null || !ReferenceEquals(source, attackComponent.PrimaryHitbox)) return;
            if (!TryResolveProperty(other, out Entity targetEntity, out PropertyComponent targetProperty) || !IsPropertyTargetValid(targetEntity, targetProperty, aiComponent.Definition.TargetTag)) return;
            int targetId = targetEntity.EntityId;
            if (!hitTargets.Add(targetId)) return;
            float requestedDamage = propertyComponent.Atk;
            DamageAttribute damageAttribute = propertyComponent.ResolveDamageAttribute(DamageActionType.NormalAttack);
            EffectSignal signal = new EffectSignal(EffectSignalType.HitConfirmed, Entity, targetProperty.Entity, Entity, requestedDamage, requestedDamage, EffectTag.Attack | EffectTag.NormalAttack, aiComponent.Definition.AttackSignalId, position: other.transform.position, damageAttribute: damageAttribute, damageActionType: DamageActionType.NormalAttack);
            effectComponent.Runtime.Publish(signal);
        }

        /// <summary>交互感应扩展接口的空实现：敌人逻辑不处理触发离开。</summary>
        public void OnTriggerExit(ColliderProxy source, Collider other) { }

        /// <summary>死亡事实发生时永久停止 Brain，确保死亡动画不再被 AI 表现覆盖。</summary>
        private void OnDie(DieEvent evt)
        {
            dead = true;
            StopHorizontalMotion();
            RefreshBrainRunningState();
        }

        /// <summary>
        /// 汇总 Entity 调度器与死亡状态，只在最终运行状态变化时调用 Brain 生命周期。
        /// </summary>
        private void RefreshBrainRunningState()
        {
            if (brain == null) return;
            bool shouldRun = logicEnabled && !dead;
            if (shouldRun == brainRunning) return;
            brainRunning = shouldRun;
            if (shouldRun) brain.Resume();
            else brain.Suspend();
        }

        /// <summary>解释敌人攻击 AnimationLine 的强类型命令并控制命中窗口与特效，不再读取 AnimationLibrary 事件名。</summary>
        private void OnAttackAnimationCommand(AnimationPlayback source, AnimationLineEventCommand command)
        {
            if (!ReferenceEquals(source, attackPlayback)) return;
            if (command == AnimationLineEventCommand.EnableHitbox)
            {
                if (attackComponent.PrimaryHitbox != null && attackComponent.PrimaryHitbox.cod != null) attackComponent.PrimaryHitbox.cod.enabled = true;
                if (spineComponent.animationLib.atkExecutor.TryGetSelection(0, false, out AttackAnimationSelection selection))
                {
                    if (selection.HasVfx) vfxComponent.Play(selection.Vfx);
                }
            }
            else if (command == AnimationLineEventCommand.DisableHitbox)
            {
                DisableAttackCollider();
            }
        }

        /// <summary>保证敌人攻击结束回调恰好执行一次，并只在自然完成后恢复待机表现。</summary>
        private void OnAttackFinished(AnimationPlayback source, AnimationEndReason reason)
        {
            if (!ReferenceEquals(source, attackPlayback)) return;
            bool completed = reason == AnimationEndReason.Completed;
            Action<bool> callback = attackFinished;
            attackPlayback = null;
            attackFinished = null;
            hitTargets.Clear();
            DisableAttackCollider();
            callback?.Invoke(completed);
            if (completed && !dead) PlayIdle();
        }

        /// <summary>关闭攻击碰撞体，兼容 ColliderProxy 尚未执行 Awake 的构造阶段。</summary>
        private void DisableAttackCollider()
        {
            if (attackComponent != null && attackComponent.PrimaryHitbox != null && attackComponent.PrimaryHitbox.cod != null) attackComponent.PrimaryHitbox.cod.enabled = false;
        }

        /// <summary>仅清除 AI 管理的水平速度，保留 GravityLogic 持有的竖直重力速度。</summary>
        private void StopHorizontalMotion()
        {
            if (motionComponent == null) return;
            motionComponent.curVelo.x = 0f;
            motionComponent.curVelo.z = 0f;
        }

        /// <summary>
        /// 判断 PropertyComponent 是否可作为敌对目标，并排除自身、死亡对象和错误标签。
        /// </summary>
        private bool IsPropertyTargetValid(Entity targetEntity, PropertyComponent targetProperty, string requiredTag)
        {
            if (targetEntity == null || targetProperty == null || targetProperty == propertyComponent || targetProperty.IsDead || !targetEntity.IsActive || targetEntity.bindGo == null || !targetEntity.bindGo.activeInHierarchy) return false;
            return string.IsNullOrWhiteSpace(requiredTag) || targetEntity.bindGo.CompareTag(requiredTag);
        }

        /// <summary>通过目标 ColliderProxy 在初始化阶段绑定的宿主引用解析 Entity 与纯 C# PropertyComponent。</summary>
        private static bool TryResolveProperty(Collider collider, out Entity targetEntity, out PropertyComponent targetProperty)
        {
            targetProperty = null;
            return ColliderProxy.TryGetHostEntity(collider, out targetEntity) && targetEntity.TryGetComp(out targetProperty);
        }

        /// <summary>
        /// 从 Entity 获取必需组件并在预制体漏配时抛出包含组件类型的明确异常。
        /// </summary>
        private void RequireComponent<TComponent>(out TComponent component) where TComponent : IComponent
        {
            if (!Entity.TryGetComp(out component) || ReferenceEquals(component, null)) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' requires component '{typeof(TComponent).FullName}' for Enemy AI.");
        }
    }
}
