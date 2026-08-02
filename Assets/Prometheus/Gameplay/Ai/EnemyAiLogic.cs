using System;
using System.Collections.Generic;
using Spine;
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
        private EffectComponent effectComponent;
        private EventComponent eventComponent;
        private EnemyAiBrain brain;
        private TrackEntry attackEntry;
        private Action<bool> attackFinished;
        private bool logicEnabled;
        private bool attackedSuspended;
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
        /// 缓存全部运行时适配器、创建独立 Brain，并订阅受击和死亡生命周期事件。
        /// </summary>
        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.Act;
            RequireComponent(out aiComponent);
            RequireComponent(out propertyComponent);
            RequireComponent(out attackComponent);
            RequireComponent(out spineComponent);
            RequireComponent(out effectComponent);
            RequireComponent(out eventComponent);
            if (aiComponent.Definition == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' does not reference an EnemyAiDefinition.");
            if (aiComponent.CharacterController == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' does not contain a CharacterController for Enemy AI movement.");
            if (attackComponent.atkCollider == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' does not contain an attack ColliderProxy.");
            attackComponent.atkCollider.handler = this;
            if (attackComponent.atkCollider.cod != null) attackComponent.atkCollider.cod.enabled = false;
            eventComponent.AddListener<AttackedStartEvent>(OnAttackedStart);
            eventComponent.AddListener<AttackedEndEvent>(OnAttackedEnd);
            eventComponent.AddListener<DieEvent>(OnDie);
            brain = new EnemyAiBrain(aiComponent.Definition, this, Entity.bindGo.GetInstanceID());
        }

        /// <summary>只要实体未死亡就允许 Entity 调度器重新启用 AI。</summary>
        public override bool CanEnable()
        {
            return !dead;
        }

        /// <summary>AI 不主动退出，由控制状态、受击或死亡生命周期负责挂起。</summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>记录 Entity 调度器已经允许 AI 运行，并统一刷新挂起状态。</summary>
        public override void OnEnable()
        {
            logicEnabled = true;
            RefreshBrainRunningState();
        }

        /// <summary>在 Stun 等控制状态禁用 Logic 时暂停 Brain 并取消攻击窗口。</summary>
        public override void OnDisable()
        {
            logicEnabled = false;
            RefreshBrainRunningState();
        }

        /// <summary>将 Unity 帧时间交给独立 Brain。</summary>
        public override void OnUpdate(float dt)
        {
            brain?.Tick(dt);
        }

        /// <summary>对称取消事件、碰撞代理和 Brain 生命周期；重复调用保持安全。</summary>
        public override void OnDispose()
        {
            if (eventComponent != null)
            {
                eventComponent.RemoveListener<AttackedStartEvent>(OnAttackedStart);
                eventComponent.RemoveListener<AttackedEndEvent>(OnAttackedEnd);
                eventComponent.RemoveListener<DieEvent>(OnDie);
            }

            if (attackComponent != null && attackComponent.atkCollider != null && ReferenceEquals(attackComponent.atkCollider.handler, this)) attackComponent.atkCollider.handler = null;
            brain?.Dispose();
            brain = null;
            brainRunning = false;
            DisableAttackCollider();
        }

        /// <inheritdoc />
        public bool TryAcquireTarget(float radius, int layerMask, string requiredTag, out Transform target)
        {
            int count = Physics.OverlapSphereNonAlloc(Position, radius, perceptionBuffer, layerMask, QueryTriggerInteraction.UseGlobal);
            PropertyComponent selectedProperty = null;
            float selectedDistanceSquared = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                Collider candidateCollider = perceptionBuffer[index];
                perceptionBuffer[index] = null;
                if (candidateCollider == null) continue;
                PropertyComponent candidateProperty = candidateCollider.GetComponentInParent<PropertyComponent>();
                if (!IsPropertyTargetValid(candidateProperty, requiredTag)) continue;
                float distanceSquared = (candidateProperty.transform.position - Position).sqrMagnitude;
                if (distanceSquared >= selectedDistanceSquared) continue;
                selectedProperty = candidateProperty;
                selectedDistanceSquared = distanceSquared;
            }

            target = selectedProperty == null ? null : selectedProperty.transform;
            return target != null;
        }

        /// <inheritdoc />
        public bool IsTargetValid(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return false;
            PropertyComponent targetProperty = target.GetComponentInParent<PropertyComponent>();
            return IsPropertyTargetValid(targetProperty, aiComponent.Definition.TargetTag);
        }

        /// <inheritdoc />
        public void Move(Vector3 worldDirection, float speed, float deltaTime)
        {
            if (!CanMove || deltaTime <= 0f || speed <= 0f) return;
            aiComponent.CharacterController.Move(worldDirection * speed * deltaTime);
        }

        /// <inheritdoc />
        public void StopMovement()
        {
        }

        /// <inheritdoc />
        public void Face(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude > 0.000001f) spineComponent.SetFaceDir(worldDirection.x);
        }

        /// <inheritdoc />
        public void PlayIdle()
        {
            if (dead || spineComponent.animationLib == null || spineComponent.animationLib.idleExecutor == null) return;
            spineComponent.animationLib.idleExecutor.Execute();
        }

        /// <inheritdoc />
        public void PlayMove()
        {
            if (dead || spineComponent.animationLib == null || spineComponent.animationLib.groundMoveExecutor == null) return;
            spineComponent.animationLib.groundMoveExecutor.Execute(MoveMode.Run);
        }

        /// <inheritdoc />
        public bool TryStartAttack(Action<bool> onFinished)
        {
            if (!CanAct || attackEntry != null || spineComponent.animationLib == null || spineComponent.animationLib.atkExecutor == null) return false;
            TrackEntry newEntry = spineComponent.animationLib.atkExecutor.Execute();
            if (newEntry == null) return false;
            attackEntry = newEntry;
            attackFinished = onFinished;
            hitTargets.Clear();
            newEntry.Complete += OnAttackComplete;
            newEntry.Interrupt += OnAttackInterrupted;
            return true;
        }

        /// <inheritdoc />
        public void CancelAttack()
        {
            DisableAttackCollider();
            if (attackEntry == null)
            {
                attackFinished = null;
                hitTargets.Clear();
                return;
            }

            Action<bool> callback = attackFinished;
            TrackEntry entryToStop = attackEntry;
            attackEntry = null;
            attackFinished = null;
            hitTargets.Clear();
            entryToStop.Complete -= OnAttackComplete;
            entryToStop.Interrupt -= OnAttackInterrupted;
            spineComponent.Stop(0, 0f);
            callback?.Invoke(false);
        }

        /// <summary>
        /// 将攻击碰撞命中转换为 EffectSignal，并确保一次攻击不会重复命中同一 PropertyComponent。
        /// </summary>
        public void OnTriggerEnter(Collider other)
        {
            if (!Entity.IsActive || dead || attackEntry == null || other == null) return;
            PropertyComponent targetProperty = other.GetComponentInParent<PropertyComponent>();
            if (!IsPropertyTargetValid(targetProperty, aiComponent.Definition.TargetTag)) return;
            int targetId = targetProperty.GetInstanceID();
            if (!hitTargets.Add(targetId)) return;
            float requestedDamage = propertyComponent.Atk;
            EffectSignal signal = new EffectSignal(EffectSignalType.HitConfirmed, Entity, targetProperty.Entity, Entity, requestedDamage, requestedDamage, EffectTag.Attack | EffectTag.NormalAttack, aiComponent.Definition.AttackSignalId, position: other.transform.position);
            effectComponent.Runtime.Publish(signal);
        }

        /// <summary>受击表现开始时暂停 Brain，防止移动和攻击动画覆盖受击动画。</summary>
        private void OnAttackedStart(AttackedStartEvent evt)
        {
            attackedSuspended = true;
            RefreshBrainRunningState();
        }

        /// <summary>受击表现结束后按照控制状态决定是否恢复 Brain。</summary>
        private void OnAttackedEnd(AttackedEndEvent evt)
        {
            attackedSuspended = false;
            RefreshBrainRunningState();
        }

        /// <summary>死亡事实发生时永久停止 Brain，确保死亡动画不再被 AI 表现覆盖。</summary>
        private void OnDie(DieEvent evt)
        {
            dead = true;
            RefreshBrainRunningState();
        }

        /// <summary>
        /// 汇总 Entity 控制状态、受击和死亡来源，只在最终运行状态变化时调用 Brain 生命周期。
        /// </summary>
        private void RefreshBrainRunningState()
        {
            if (brain == null) return;
            bool shouldRun = logicEnabled && !attackedSuspended && !dead;
            if (shouldRun == brainRunning) return;
            brainRunning = shouldRun;
            if (shouldRun) brain.Resume();
            else brain.Suspend();
        }

        /// <summary>攻击动画自然完成时结束本次攻击并开始冷却。</summary>
        private void OnAttackComplete(TrackEntry entry)
        {
            FinishAttack(entry, true);
        }

        /// <summary>攻击动画被其他表现替换时结束本次攻击但不消耗完整冷却。</summary>
        private void OnAttackInterrupted(TrackEntry entry)
        {
            FinishAttack(entry, false);
        }

        /// <summary>
        /// 保证攻击结束回调恰好执行一次，并在自然完成后恢复待机表现。
        /// </summary>
        private void FinishAttack(TrackEntry entry, bool completed)
        {
            if (!ReferenceEquals(entry, attackEntry)) return;
            Action<bool> callback = attackFinished;
            attackEntry.Complete -= OnAttackComplete;
            attackEntry.Interrupt -= OnAttackInterrupted;
            attackEntry = null;
            attackFinished = null;
            hitTargets.Clear();
            DisableAttackCollider();
            callback?.Invoke(completed);
            if (completed && !dead) PlayIdle();
        }

        /// <summary>关闭攻击碰撞体，兼容 ColliderProxy 尚未执行 Awake 的构造阶段。</summary>
        private void DisableAttackCollider()
        {
            if (attackComponent != null && attackComponent.atkCollider != null && attackComponent.atkCollider.cod != null) attackComponent.atkCollider.cod.enabled = false;
        }

        /// <summary>
        /// 判断 PropertyComponent 是否可作为敌对目标，并排除自身、死亡对象和错误标签。
        /// </summary>
        private bool IsPropertyTargetValid(PropertyComponent targetProperty, string requiredTag)
        {
            if (targetProperty == null || targetProperty == propertyComponent || targetProperty.Entity == null || targetProperty.IsDead || !targetProperty.Entity.IsActive || !targetProperty.gameObject.activeInHierarchy) return false;
            return string.IsNullOrWhiteSpace(requiredTag) || targetProperty.CompareTag(requiredTag);
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
