using System;
using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using Xuan.Prometheus.Ai;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Actor
{
    /// <summary>标识 ActorRuntimeLogic 初始化时需要安装的后备控制配置；任意 Pawn 获得 Possession 租约后都会优先消费该控制器的控制帧。</summary>
    public enum ActorControlRole
    {
        None,
        Player,
        EnemyAi
    }

    /// <summary>把行为时间轴上的稳定 GameplayEvent 编号转发到 Entity 局部事件总线。</summary>
    public sealed class ActorGameplayEvent : IEvent
    {
        /// <summary>创建一条行为时间轴事件。</summary>
        public ActorGameplayEvent(string eventId)
        {
            EventId = !string.IsNullOrWhiteSpace(eventId) ? eventId : throw new ArgumentException("Actor gameplay event ID cannot be empty.", nameof(eventId));
        }

        /// <summary>获取资产声明的稳定事件编号。</summary>
        public string EventId { get; }
    }

    /// <summary>
    /// 在渲染帧采样与固定 Tick 消费之间保存玩家控制数据，并以 PossessionGeneration 隔离不同控制拓扑产生的瞬时输入。
    /// </summary>
    public sealed class ActorControlFrameBuffer
    {
        private ControlFrame latestFrame;
        private ControlButton bufferedPressedButtons;
        private uint activeGeneration;
        private bool hasFrame;
        private bool hasGeneration;

        /// <summary>获取缓冲器当前是否持有一个仍然有效的控制帧。</summary>
        public bool HasFrame => hasFrame;

        /// <summary>采集一个最新控制帧；控制代数变化时先清除旧控制者尚未消费的瞬时按钮。</summary>
        public void Capture(ControlFrame frame)
        {
            if (!hasGeneration || activeGeneration != frame.PossessionGeneration)
            {
                bufferedPressedButtons = ControlButton.None;
                activeGeneration = frame.PossessionGeneration;
                hasGeneration = true;
            }
            latestFrame = frame;
            bufferedPressedButtons |= frame.PressedButtons;
            hasFrame = true;
        }

        /// <summary>在 Pawn 当前没有控制帧时清除连续输入和未消费瞬时按钮，避免重新接管后重放失效操作。</summary>
        public void Clear(ulong frameId)
        {
            latestFrame = ControlFrame.Empty(frameId, 0u);
            bufferedPressedButtons = ControlButton.None;
            activeGeneration = 0u;
            hasFrame = false;
            hasGeneration = false;
        }

        /// <summary>为一个固定 Tick 生成控制快照；Input 能力不可用时主动丢弃瞬时按钮且不输出任何控制意图。</summary>
        public bool TryConsume(bool inputCapabilityAvailable, out ControlFrame frame)
        {
            if (!hasFrame)
            {
                frame = latestFrame;
                return false;
            }
            if (!inputCapabilityAvailable)
            {
                bufferedPressedButtons = ControlButton.None;
                frame = ControlFrame.Empty(latestFrame.FrameId, latestFrame.PossessionGeneration);
                return false;
            }
            frame = new ControlFrame(latestFrame.FrameId, latestFrame.PossessionGeneration, latestFrame.Move, latestFrame.Facing, bufferedPressedButtons, latestFrame.HeldButtons, latestFrame.EffectiveScopes);
            bufferedPressedButtons = ControlButton.None;
            return true;
        }
    }

    /// <summary>
    /// 将通用 Capability、Control、Behavior、Motion、Spine、Hitbox 和 AI 适配器组合为一个可由固定 Tick 驱动的 GameplayObject 运行时。
    /// </summary>
    public sealed class ActorRuntimeLogic : Logic.Logic, IActorPhasedSimulationParticipant, IActorFrameCaptureParticipant, IBehaviorSimulationSink, IEnemyAiAgent
    {
        private readonly ActorControlRole controlRole;
        private readonly Collider[] perceptionBuffer = new Collider[16];
        private readonly Dictionary<string, BehaviorProgram> programsById = new Dictionary<string, BehaviorProgram>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActorBehaviorDefinition> behaviorDefinitionsById = new Dictionary<string, ActorBehaviorDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, CapabilityBlockHandle> capabilityBlocksByClipId = new Dictionary<string, CapabilityBlockHandle>(StringComparer.Ordinal);
        private readonly HashSet<string> rootMotionCompensationClipIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<PendingHitSample> pendingHitSamples = new List<PendingHitSample>();
        private readonly HashSet<PendingHitKey> pendingHitSampleKeys = new HashSet<PendingHitKey>();
        private readonly List<PendingHitClose> pendingHitCloses = new List<PendingHitClose>();
        private readonly List<ActorBehaviorDefinition> basicAttackBehaviors = new List<ActorBehaviorDefinition>();
        private ActorAuthoringComponent authoring;
        private PropertyComponent propertyComponent;
        private SpineComponent spineComponent;
        private EffectComponent effectComponent;
        private EventComponent eventComponent;
        private EnemyAiComponent enemyAiComponent;
        private CharacterController characterController;
        private CapabilityRegistry capabilities;
        private IActorMotionModel motionModel;
        private BehaviorController behaviorController;
        private ActorPresentationRuntime presentationRuntime;
        private ActorHitQueryRuntime hitQueryRuntime;
        private ActorSimulationSystem simulationSystem;
        private PossessionSystem possessionSystem;
        private EnemyAiBrain enemyBrain;
        private ActorBehaviorDefinition activeBehaviorDefinition;
        private Action<bool> enemyAttackFinished;
        private Vector3 requestedMoveDirection;
        private Vector3 requestedFacingDirection;
        private float requestedMoveSpeed;
        private Vector3 authoredDisplacement;
        private bool jumpRequested;
        private readonly ActorControlFrameBuffer controlFrameBuffer = new ActorControlFrameBuffer();
        private long currentSimulationTick;
        private long comboExpiresAtTick;
        private int nextComboIndex;
        private int heldAttackTicks;
        private uint heldAttackControlGeneration;
        private bool sprinting;
        private bool specialAttackTriggeredForCurrentHold;
        private bool hasHeldAttackControlGeneration;
        private bool attackedSuspended;
        private bool dead;
        private bool registered;
        private ControlScope activeEnemyAiScopes;
        private string pendingBehaviorVariantId;
        private string activeBehaviorVariantId;
        private BehaviorHandle behaviorEndedDuringCurrentStep;
        private BehaviorPhase behaviorEndedPhaseDuringCurrentStep;

        /// <summary>创建一个由指定来源控制的通用 Actor 运行时。</summary>
        public ActorRuntimeLogic(ActorControlRole controlRole)
        {
            this.controlRole = controlRole;
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
        }

        /// <inheritdoc />
        public long SimulationId => Entity == null ? 0L : Entity.EntityId;

        /// <inheritdoc />
        public bool IsSimulationActive => registered && Entity != null && Entity.IsActive && !dead;

        /// <inheritdoc />
        public Vector3 Position => Entity.bindGo.transform.position;

        /// <inheritdoc />
        public bool CanAct => IsSimulationActive && !attackedSuspended && propertyComponent != null && propertyComponent.CanAct && !propertyComponent.IsDead && capabilities.HasAll(ActorCapability.BasicAttack);

        /// <inheritdoc />
        public bool CanMove => IsSimulationActive && !attackedSuspended && propertyComponent != null && propertyComponent.CanMove && !propertyComponent.IsDead && capabilities.HasAll(ActorCapability.Move);

        /// <summary>获取当前对象是否允许命中解析器接受新的命中结果。</summary>
        public bool CanReceiveHit => IsSimulationActive && capabilities != null && capabilities.HasAll(ActorCapability.ReceiveHit);

        /// <inheritdoc />
        public void CaptureFrame(float frameDeltaTime)
        {
            if (possessionSystem == null) return;
            if (possessionSystem.TryGetControlFrame(Entity.EntityId, out ControlFrame frame) && (frame.EffectiveScopes & (ControlScope.Locomotion | ControlScope.Facing | ControlScope.Action)) != ControlScope.None)
            {
                controlFrameBuffer.Capture(frame);
            }
            else
            {
                controlFrameBuffer.Clear(possessionSystem.LastPreparedFrameId);
            }
        }

        /// <summary>缓存 Prefab 与玩法组件、创建全部独立运行时并注册固定 Tick 参与者。</summary>
        public override void AfterNew()
        {
            RequireComponent(out authoring);
            RequireComponent(out propertyComponent);
            RequireComponent(out spineComponent);
            RequireComponent(out effectComponent);
            RequireComponent(out eventComponent);
            authoring.ValidateOrThrow();
            propertyComponent.SetBaseValue(PropertyType.MoveSpeed, authoring.Definition.MoveSpeed);
            characterController = Entity.bindGo.GetComponent<CharacterController>();
            SkeletonRootMotion legacyRootMotion = Entity.bindGo.GetComponent<SkeletonRootMotion>();
            if (legacyRootMotion != null) legacyRootMotion.enabled = false;
            capabilities = new CapabilityRegistry(authoring.Definition.DefaultCapabilities);
            motionModel = authoring.Definition.MotionModel.CreateRuntime(new ActorMotionContext(Entity.bindGo.transform, characterController));
            simulationSystem = Entity.GameplayKit.GetSystem<ActorSimulationSystem>();
            Entity.GameplayKit.TryGetSystem(out possessionSystem);
            Entity.GameplayKit.TryGetSystem(out CameraDirectorSystem cameraDirector);
            presentationRuntime = new ActorPresentationRuntime(authoring, spineComponent, cameraDirector, simulationSystem.TickRate);
            hitQueryRuntime = new ActorHitQueryRuntime(authoring, Entity, propertyComponent, effectComponent);
            behaviorController = new BehaviorController(this);
            BuildBehaviorCache();
            eventComponent.AddListener<AttackedStartEvent>(OnAttackedStart);
            eventComponent.AddListener<AttackedEndEvent>(OnAttackedEnd);
            eventComponent.AddListener<ControlStateChangedEvent>(OnControlStateChanged);
            eventComponent.AddListener<DieEvent>(OnDie);
            if (controlRole == ActorControlRole.Player) effectComponent.RegisterCombatFlowTriggers(Entity);
            if (controlRole == ActorControlRole.EnemyAi) InitializeEnemyBrain();
            simulationSystem.RegisterParticipant(this);
            registered = true;
        }

        /// <inheritdoc />
        public override bool CanEnable()
        {
            return !dead;
        }

        /// <inheritdoc />
        public override bool CanDisable()
        {
            return false;
        }

        /// <inheritdoc />
        public override void OnEnable()
        {
            RefreshEnemyBrainState();
        }

        /// <inheritdoc />
        public override void OnDisable()
        {
            CancelActiveBehavior();
            enemyBrain?.Suspend();
        }

        /// <summary>普通 Entity 帧更新不推进模拟；输入采样、固定 Tick 与表现分别由 ActorSimulationSystem 的明确阶段驱动。</summary>
        public override void OnUpdate(float dt)
        {
        }

        /// <inheritdoc />
        public void SimulateTick(long simulationTick, float fixedDeltaTime)
        {
            PrepareSimulationTick(simulationTick, fixedDeltaTime);
            ApplySimulationMotion(simulationTick, fixedDeltaTime);
            ResolveSimulationTick(simulationTick, fixedDeltaTime);
            CommitSimulationTick(simulationTick, fixedDeltaTime);
        }

        /// <inheritdoc />
        public void PrepareSimulationTick(long simulationTick, float fixedDeltaTime)
        {
            if (simulationTick != currentSimulationTick) DiscardUnresolvedHitWork();
            if (!IsSimulationActive) return;
            currentSimulationTick = simulationTick;
            authoredDisplacement = Vector3.zero;
            jumpRequested = false;
            requestedMoveDirection = Vector3.zero;
            requestedFacingDirection = Vector3.zero;
            requestedMoveSpeed = 0f;
            if (controlFrameBuffer.HasFrame) SimulatePossessedControl(fixedDeltaTime);
            else if (controlRole == ActorControlRole.EnemyAi) SimulateEnemyAi(ControlScope.Locomotion | ControlScope.Facing | ControlScope.Action, fixedDeltaTime);
            if (capabilities.HasAll(ActorCapability.Rotate) && requestedFacingDirection.sqrMagnitude > 0.000001f) spineComponent.SetFaceDir(requestedFacingDirection.x);
            BehaviorProgram steppedProgram = behaviorController.ActiveProgram;
            BehaviorHandle steppedHandle = behaviorController.ActiveHandle;
            string steppedVariantId = activeBehaviorVariantId;
            BehaviorPhase startPhase = default;
            bool hasStartPhase = steppedProgram != null && behaviorController.TryGetPhase(steppedHandle, out startPhase);
            behaviorEndedDuringCurrentStep = default;
            behaviorEndedPhaseDuringCurrentStep = default;
            behaviorController.Step();
            if (hasStartPhase)
            {
                if (behaviorController.TryGetPhase(steppedHandle, out BehaviorPhase endPhase)) AccumulateAuthoredMotion(steppedProgram, steppedVariantId, startPhase.RawValue, endPhase.RawValue);
                else if (behaviorEndedDuringCurrentStep == steppedHandle) AccumulateAuthoredMotion(steppedProgram, steppedVariantId, startPhase.RawValue, behaviorEndedPhaseDuringCurrentStep.RawValue);
            }
            if (behaviorController.IsActive && behaviorController.TryGetPhase(behaviorController.ActiveHandle, out BehaviorPhase activePhase)) presentationRuntime.AdvanceToTick(activePhase.Tick);
            if (!propertyComponent.CanAct && behaviorController.IsActive) CancelActiveBehavior();
        }

        /// <inheritdoc />
        public void ApplySimulationMotion(long simulationTick, float fixedDeltaTime)
        {
            if (!IsSimulationActive || simulationTick != currentSimulationTick) return;
            Vector3 movement = CanMove ? requestedMoveDirection : Vector3.zero;
            float speed = CanMove ? requestedMoveSpeed : 0f;
            motionModel.Simulate(new ActorMotionIntent(movement, speed, jumpRequested, authoredDisplacement), fixedDeltaTime);
            if (motionModel.Snapshot.LandedThisTick) presentationRuntime.NotifyLanded();
        }

        /// <inheritdoc />
        public void ResolveSimulationTick(long simulationTick, float fixedDeltaTime)
        {
            if (simulationTick != currentSimulationTick) return;
            List<Exception> exceptions = null;
            bool canResolveHits = IsSimulationActive && !attackedSuspended && propertyComponent != null && propertyComponent.CanAct && !propertyComponent.IsDead;
            if (canResolveHits)
            {
                for (int index = 0; index < pendingHitSamples.Count; index++)
                {
                    PendingHitSample pending = pendingHitSamples[index];
                    try
                    {
                        hitQueryRuntime.Sample(pending.Handle, pending.Clip, pending.SignalDefinition);
                    }
                    catch (Exception exception)
                    {
                        AddPendingHitException(ref exceptions, "sample", pending.Handle, pending.Clip, exception);
                    }
                }
            }
            for (int index = 0; index < pendingHitCloses.Count; index++)
            {
                PendingHitClose pending = pendingHitCloses[index];
                try
                {
                    hitQueryRuntime.Close(pending.Handle, pending.Clip);
                }
                catch (Exception exception)
                {
                    AddPendingHitException(ref exceptions, "close", pending.Handle, pending.Clip, exception);
                }
            }
            ClearPendingHitCollections();
            if (exceptions != null) throw new AggregateException($"Actor '{authoring.name}' failed to resolve one or more deferred hit windows at simulation tick '{simulationTick}'.", exceptions);
        }

        /// <inheritdoc />
        public void CommitSimulationTick(long simulationTick, float fixedDeltaTime)
        {
            if (simulationTick != currentSimulationTick) return;
            hitQueryRuntime.CommitSignals();
        }

        /// <inheritdoc />
        public void Present(float frameDeltaTime, float interpolationAlpha)
        {
            if (!registered || presentationRuntime == null || motionModel == null || dead || attackedSuspended) return;
            if (behaviorController.IsActive && behaviorController.TryGetPhase(behaviorController.ActiveHandle, out BehaviorPhase activePhase)) presentationRuntime.PresentBehavior(frameDeltaTime, interpolationAlpha, activePhase);
            else presentationRuntime.PresentLocomotion(motionModel.Snapshot, sprinting, frameDeltaTime);
        }

        /// <summary>对称注销固定 Tick、事件和 AI，并按照依赖逆序释放行为、命中、表现、运动与能力运行时。</summary>
        public override void OnDispose()
        {
            if (registered && simulationSystem != null) simulationSystem.UnregisterParticipant(SimulationId);
            registered = false;
            if (eventComponent != null)
            {
                eventComponent.RemoveListener<AttackedStartEvent>(OnAttackedStart);
                eventComponent.RemoveListener<AttackedEndEvent>(OnAttackedEnd);
                eventComponent.RemoveListener<ControlStateChangedEvent>(OnControlStateChanged);
                eventComponent.RemoveListener<DieEvent>(OnDie);
            }
            enemyBrain?.Dispose();
            enemyBrain = null;
            behaviorController?.Dispose();
            behaviorController = null;
            hitQueryRuntime?.Dispose();
            hitQueryRuntime = null;
            presentationRuntime?.Dispose();
            presentationRuntime = null;
            motionModel?.Dispose();
            motionModel = null;
            capabilities?.Dispose();
            capabilities = null;
            programsById.Clear();
            behaviorDefinitionsById.Clear();
            basicAttackBehaviors.Clear();
            capabilityBlocksByClipId.Clear();
            rootMotionCompensationClipIds.Clear();
            ClearPendingHitCollections();
            enemyAttackFinished = null;
            pendingBehaviorVariantId = null;
            activeBehaviorVariantId = null;
            heldAttackTicks = 0;
            heldAttackControlGeneration = 0u;
            specialAttackTriggeredForCurrentHold = false;
            hasHeldAttackControlGeneration = false;
            activeEnemyAiScopes = ControlScope.None;
            behaviorEndedDuringCurrentStep = default;
            behaviorEndedPhaseDuringCurrentStep = default;
            controlFrameBuffer.Clear(0u);
        }

        /// <inheritdoc />
        public void OnBehaviorStarted(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase)
        {
            if (!behaviorDefinitionsById.TryGetValue(program.ProgramId, out activeBehaviorDefinition)) throw new InvalidOperationException($"Actor '{authoring.name}' cannot resolve active behavior '{program.ProgramId}'.");
            if (string.IsNullOrWhiteSpace(pendingBehaviorVariantId)) throw new InvalidOperationException($"Actor '{authoring.name}' started behavior '{program.ProgramId}' without a resolved presentation variant.");
            activeBehaviorVariantId = pendingBehaviorVariantId;
            presentationRuntime.BeginBehavior(handle, activeBehaviorDefinition, activeBehaviorVariantId, phase.RateRaw);
        }

        /// <inheritdoc />
        public void OnClipEntered(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase)
        {
            switch (clip.Kind)
            {
                case SimulationClipKind.HitWindow:
                    HitWindowClip enteredHitWindow = (HitWindowClip)clip;
                    hitQueryRuntime.Open(handle, enteredHitWindow);
                    QueuePendingHitSample(handle, enteredHitWindow, activeBehaviorDefinition.HitSignal);
                    break;
                case SimulationClipKind.Motion:
                    if (TryResolveActiveMotionBinding((MotionClip)clip, out ActorMotionBindingDefinition enteredMotionBinding) && enteredMotionBinding.BakedDisplacementCount > 0 && rootMotionCompensationClipIds.Add(clip.ClipId)) presentationRuntime.SetRootMotionPoseCompensation(true);
                    break;
                case SimulationClipKind.CapabilityBlock:
                    capabilityBlocksByClipId.Add(clip.ClipId, capabilities.AcquireBlock(clip, ((CapabilityBlockClip)clip).BlockedCapabilities));
                    break;
                case SimulationClipKind.GameplayEvent: eventComponent.Invoke(new ActorGameplayEvent(((GameplayEventClip)clip).EventId)); break;
            }
        }

        /// <inheritdoc />
        public void OnClipSampled(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase)
        {
            if (clip is HitWindowClip hitWindow) QueuePendingHitSample(handle, hitWindow, activeBehaviorDefinition.HitSignal);
        }

        /// <inheritdoc />
        public void OnClipExited(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase, BehaviorEndReason reason)
        {
            if (clip is HitWindowClip hitWindow)
            {
                if (reason != BehaviorEndReason.Completed) RemovePendingHitSample(handle, hitWindow);
                pendingHitCloses.Add(new PendingHitClose(handle, hitWindow));
            }
            if (clip is MotionClip && rootMotionCompensationClipIds.Remove(clip.ClipId)) presentationRuntime.SetRootMotionPoseCompensation(rootMotionCompensationClipIds.Count > 0);
            if (capabilityBlocksByClipId.TryGetValue(clip.ClipId, out CapabilityBlockHandle blockHandle))
            {
                capabilities.Release(blockHandle);
                capabilityBlocksByClipId.Remove(clip.ClipId);
            }
        }

        /// <inheritdoc />
        public void OnBehaviorEnded(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase, BehaviorEndReason reason)
        {
            behaviorEndedDuringCurrentStep = handle;
            behaviorEndedPhaseDuringCurrentStep = phase;
            presentationRuntime.AdvanceToTick(Mathf.Min(phase.Tick, program.DurationTicks));
            rootMotionCompensationClipIds.Clear();
            presentationRuntime.EndBehavior(handle);
            activeBehaviorDefinition = null;
            activeBehaviorVariantId = null;
            Action<bool> finished = enemyAttackFinished;
            enemyAttackFinished = null;
            finished?.Invoke(reason == BehaviorEndReason.Completed);
        }

        /// <inheritdoc />
        public bool TryAcquireTarget(float radius, int layerMask, string requiredTag, out Transform target)
        {
            int count = Physics.OverlapSphereNonAlloc(Position, radius, perceptionBuffer, layerMask, QueryTriggerInteraction.UseGlobal);
            PropertyComponent selected = null;
            float selectedDistance = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                Collider candidate = perceptionBuffer[index];
                perceptionBuffer[index] = null;
                PropertyComponent candidateProperty = candidate == null ? null : candidate.GetComponentInParent<PropertyComponent>();
                if (!IsValidAiTarget(candidateProperty, requiredTag)) continue;
                float distance = (candidateProperty.transform.position - Position).sqrMagnitude;
                if (distance >= selectedDistance) continue;
                selected = candidateProperty;
                selectedDistance = distance;
            }
            target = selected == null ? null : selected.transform;
            return target != null;
        }

        /// <inheritdoc />
        public bool IsTargetValid(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return false;
            PropertyComponent targetProperty = target.GetComponentInParent<PropertyComponent>();
            return IsValidAiTarget(targetProperty, enemyAiComponent.Definition.TargetTag);
        }

        /// <inheritdoc />
        public void Move(Vector3 worldDirection, float speed, float deltaTime)
        {
            if ((activeEnemyAiScopes & ControlScope.Locomotion) == 0) return;
            if (!CanMove) return;
            requestedMoveDirection = worldDirection;
            float authoredBaseSpeed = Mathf.Max(0.0001f, authoring.Definition.MoveSpeed);
            requestedMoveSpeed = Mathf.Max(0f, speed) * propertyComponent.MoveSpeed / authoredBaseSpeed;
        }

        /// <inheritdoc />
        public void StopMovement()
        {
            if ((activeEnemyAiScopes & ControlScope.Locomotion) == 0) return;
            requestedMoveDirection = Vector3.zero;
            requestedMoveSpeed = 0f;
        }

        /// <inheritdoc />
        public void Face(Vector3 worldDirection)
        {
            if ((activeEnemyAiScopes & ControlScope.Facing) == 0) return;
            if (capabilities.HasAll(ActorCapability.Rotate) && worldDirection.sqrMagnitude > 0.000001f) spineComponent.SetFaceDir(worldDirection.x);
        }

        /// <inheritdoc />
        public void PlayIdle()
        {
        }

        /// <inheritdoc />
        public void PlayMove()
        {
        }

        /// <inheritdoc />
        public bool TryStartAttack(Action<bool> onFinished)
        {
            if ((activeEnemyAiScopes & ControlScope.Action) == 0) return false;
            if (!CanAct || basicAttackBehaviors.Count == 0 || behaviorController.IsActive) return false;
            bool started = TryStartBehavior(basicAttackBehaviors[0]);
            if (started) enemyAttackFinished = onFinished;
            return started;
        }

        /// <inheritdoc />
        public void CancelAttack()
        {
            CancelActiveBehavior();
        }

        /// <summary>读取任意 Pawn 的仲裁控制帧并产生本 Tick 的移动、朝向和行为启动请求；控制器可以来自玩家、AI、载具或剧情系统。</summary>
        private void SimulatePossessedControl(float fixedDeltaTime)
        {
            if (!controlFrameBuffer.TryConsume(!attackedSuspended && capabilities.HasAll(ActorCapability.Input), out ControlFrame frame))
            {
                ResetHeldAttackSpecial();
                hasHeldAttackControlGeneration = false;
                return;
            }
            ControlScope gameplayScopes = frame.EffectiveScopes & (ControlScope.Locomotion | ControlScope.Facing | ControlScope.Action);
            if (controlRole == ActorControlRole.EnemyAi)
            {
                ControlScope fallbackScopes = (ControlScope.Locomotion | ControlScope.Facing | ControlScope.Action) & ~gameplayScopes;
                if (fallbackScopes != ControlScope.None) SimulateEnemyAi(fallbackScopes, fixedDeltaTime);
            }
            if (!hasHeldAttackControlGeneration || heldAttackControlGeneration != frame.PossessionGeneration)
            {
                ResetHeldAttackSpecial();
                heldAttackControlGeneration = frame.PossessionGeneration;
                hasHeldAttackControlGeneration = true;
            }
            if ((gameplayScopes & ControlScope.Locomotion) != 0) requestedMoveDirection = new Vector3(frame.Move.x, 0f, frame.Move.y);
            if ((gameplayScopes & ControlScope.Facing) != 0) requestedFacingDirection = new Vector3(frame.Facing.x, 0f, frame.Facing.y);
            ControlButton pressed = frame.PressedButtons;
            if ((gameplayScopes & ControlScope.Locomotion) != 0)
            {
                if ((pressed & ControlButton.SprintToggle) != 0) sprinting = !sprinting;
                if ((pressed & ControlButton.SprintToggle) != 0) propertyComponent.SetBaseValue(PropertyType.MoveSpeed, sprinting ? authoring.Definition.SprintSpeed : authoring.Definition.MoveSpeed);
                requestedMoveSpeed = propertyComponent.MoveSpeed;
                jumpRequested = (pressed & ControlButton.Jump) != 0 && capabilities.HasAll(ActorCapability.Jump) && propertyComponent.CanMove;
                if ((pressed & ControlButton.Dodge) != 0) TryStartFirstCommand(ActorBehaviorCommand.Dodge);
            }
            if ((gameplayScopes & ControlScope.Action) != 0)
            {
                if ((pressed & ControlButton.Attack) != 0) TryAdvanceBasicAttack();
                if ((pressed & ControlButton.Skill) != 0) TryStartFirstCommand(ActorBehaviorCommand.Skill);
                if ((pressed & ControlButton.Ultimate) != 0) TryStartFirstCommand(ActorBehaviorCommand.Ultimate);
                if ((pressed & ControlButton.SpecialAttack) != 0 && TryStartFirstCommand(ActorBehaviorCommand.SpecialAttack)) specialAttackTriggeredForCurrentHold = true;
                UpdateHeldAttackSpecial(frame.HeldButtons);
            }
            else
            {
                ResetHeldAttackSpecial();
            }
        }

        /// <summary>在明确允许的控制领域内推进后备 AI，并在退出时恢复空作用域，防止 AI 回调越过本 Tick 的租约边界。</summary>
        private void SimulateEnemyAi(ControlScope scopes, float fixedDeltaTime)
        {
            activeEnemyAiScopes = scopes;
            try
            {
                enemyBrain?.Tick(fixedDeltaTime);
            }
            finally
            {
                activeEnemyAiScopes = ControlScope.None;
            }
        }

        /// <summary>把旧角色按住普攻触发重击的规则转换为确定性固定 Tick 输入语义，并保证每次按住最多启动一次特殊攻击。</summary>
        private void UpdateHeldAttackSpecial(ControlButton heldButtons)
        {
            if ((heldButtons & ControlButton.Attack) == 0)
            {
                ResetHeldAttackSpecial();
                return;
            }
            if (specialAttackTriggeredForCurrentHold || heldAttackTicks == int.MaxValue) return;
            heldAttackTicks++;
            if (heldAttackTicks < authoring.Definition.HeldAttackSpecialTriggerTicks) return;
            if (behaviorController.IsActive)
            {
                if (activeBehaviorDefinition == null || activeBehaviorDefinition.Command != ActorBehaviorCommand.BasicAttack) return;
                behaviorController.Cancel(behaviorController.ActiveHandle);
            }
            specialAttackTriggeredForCurrentHold = TryStartFirstCommand(ActorBehaviorCommand.SpecialAttack);
        }

        /// <summary>清除一次按住普攻期间累积的固定 Tick 与触发标记，但保留当前控制代数用于下一帧比较。</summary>
        private void ResetHeldAttackSpecial()
        {
            heldAttackTicks = 0;
            specialAttackTriggeredForCurrentHold = false;
        }

        /// <summary>按照资产 CommandIndex 推进普通攻击连段，并只在当前行为进入连携窗口后替换。</summary>
        private void TryAdvanceBasicAttack()
        {
            if (!capabilities.HasAll(ActorCapability.BasicAttack) || basicAttackBehaviors.Count == 0 || !propertyComponent.CanAct) return;
            if (currentSimulationTick > comboExpiresAtTick) nextComboIndex = 0;
            if (behaviorController.IsActive)
            {
                if (activeBehaviorDefinition == null || activeBehaviorDefinition.Command != ActorBehaviorCommand.BasicAttack || !behaviorController.TryGetPhase(behaviorController.ActiveHandle, out BehaviorPhase phase) || phase.Tick < activeBehaviorDefinition.ChainFromTick) return;
                behaviorController.Cancel(behaviorController.ActiveHandle);
            }
            ActorBehaviorDefinition nextBehavior = basicAttackBehaviors[nextComboIndex % basicAttackBehaviors.Count];
            if (!TryStartBehavior(nextBehavior)) return;
            nextComboIndex = (nextComboIndex + 1) % basicAttackBehaviors.Count;
            comboExpiresAtTick = currentSimulationTick + 120;
        }

        /// <summary>启动指定命令序号最小的行为资产。</summary>
        private bool TryStartFirstCommand(ActorBehaviorCommand command)
        {
            if (behaviorController.IsActive) return false;
            if ((command == ActorBehaviorCommand.Skill || command == ActorBehaviorCommand.Ultimate) && (!propertyComponent.CanUseActiveSkill || !capabilities.HasAll(ActorCapability.ActiveSkill))) return false;
            if (command == ActorBehaviorCommand.Dodge && (!CanMove || !capabilities.HasAll(ActorCapability.Dodge))) return false;
            if (command == ActorBehaviorCommand.SpecialAttack && !CanAct) return false;
            ActorBehaviorDefinition selected = null;
            foreach (ActorBehaviorDefinition candidate in authoring.Definition.Behaviors)
            {
                if (candidate == null || candidate.Command != command || selected != null && selected.CommandIndex <= candidate.CommandIndex) continue;
                selected = candidate;
            }
            return selected != null && TryStartBehavior(selected);
        }

        /// <summary>使用攻击速度快照创建一个新的权威行为实例。</summary>
        private bool TryStartBehavior(ActorBehaviorDefinition behavior)
        {
            if (behavior == null || behaviorController.IsActive || !programsById.TryGetValue(behavior.BehaviorId, out BehaviorProgram program)) return false;
            int rateRaw = Mathf.Max(1, Mathf.RoundToInt(BehaviorPhase.One * Mathf.Max(0.01f, propertyComponent.AtkSpeed)));
            pendingBehaviorVariantId = ResolveBehaviorVariantId(behavior);
            try
            {
                return behaviorController.TryStart(program, rateRaw, out _);
            }
            finally
            {
                pendingBehaviorVariantId = null;
            }
        }

        /// <summary>在行为启动前依据当前 Tick 的移动输入选择一次 Variant，随后模拟位移与表现始终共享该稳定结果。</summary>
        private string ResolveBehaviorVariantId(ActorBehaviorDefinition behavior)
        {
            return requestedMoveDirection.sqrMagnitude > 0.000001f && behavior.TryGetPresentationVariant("Moving", out _) ? "Moving" : "Default";
        }

        /// <summary>解析当前行为 Variant 实际会消费的运动绑定；不匹配 Variant 的 MotionClip 既不移动对象，也不启用 Spine 姿势抵消。</summary>
        private bool TryResolveActiveMotionBinding(MotionClip motionClip, out ActorMotionBindingDefinition motionBinding)
        {
            if (!authoring.Definition.TryGetMotionBinding(motionClip.MotionId, out motionBinding)) throw new InvalidOperationException($"Actor '{authoring.name}' cannot resolve motion binding '{motionClip.MotionId}'.");
            return string.IsNullOrWhiteSpace(motionBinding.RequiredVariantId) || string.Equals(motionBinding.RequiredVariantId, activeBehaviorVariantId, StringComparison.Ordinal);
        }

        /// <summary>按行为相位实际跨越的 Q16 区间积分全部 MotionClip，精确支持低于一倍与高于一倍攻速且不会重复消费同一 Tick 位移。</summary>
        private void AccumulateAuthoredMotion(BehaviorProgram program, string variantId, long startPhaseRaw, long endPhaseRaw)
        {
            if (program == null || endPhaseRaw <= startPhaseRaw) return;
            IReadOnlyList<SimulationClip> clips = program.SimulationClips;
            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                if (!(clips[clipIndex] is MotionClip motionClip)) continue;
                if (!authoring.Definition.TryGetMotionBinding(motionClip.MotionId, out ActorMotionBindingDefinition motionBinding)) throw new InvalidOperationException($"Actor '{authoring.name}' cannot resolve motion binding '{motionClip.MotionId}'.");
                if (!string.IsNullOrWhiteSpace(motionBinding.RequiredVariantId) && !string.Equals(motionBinding.RequiredVariantId, variantId, StringComparison.Ordinal)) continue;
                int firstBehaviorTick = Math.Max(motionClip.StartTick, checked((int)(startPhaseRaw >> BehaviorPhase.FractionBits)));
                int endBehaviorTickExclusive = Math.Min(motionClip.EndTick, checked((int)(((endPhaseRaw - 1L) >> BehaviorPhase.FractionBits) + 1L)));
                Vector3 integratedLocalDisplacement = Vector3.zero;
                for (int behaviorTick = firstBehaviorTick; behaviorTick < endBehaviorTickExclusive; behaviorTick++)
                {
                    float tickOverlapRatio = ActorMotionIntervalMath.GetTickOverlapRatio(startPhaseRaw, endPhaseRaw, motionClip.StartTick, motionClip.EndTick, behaviorTick);
                    if (tickOverlapRatio <= 0f) continue;
                    Vector3 localDisplacement = motionBinding.GetLocalDisplacement(behaviorTick);
                    if (motionBinding.BakedDisplacementCount > 0) localDisplacement = presentationRuntime.ConvertBakedRootMotion(localDisplacement);
                    integratedLocalDisplacement += localDisplacement * tickOverlapRatio;
                }
                authoredDisplacement += Entity.bindGo.transform.TransformVector(integratedLocalDisplacement);
            }
        }

        /// <summary>把一次命中窗口采样稳定地合并到当前 Tick；Enter 与 Sample 在 Tick 0 同时触发时只执行一次物理查询。</summary>
        private void QueuePendingHitSample(BehaviorHandle handle, HitWindowClip clip, ActorHitSignalDefinition signalDefinition)
        {
            PendingHitKey key = new PendingHitKey(handle.InstanceId, clip.ClipId);
            if (!pendingHitSampleKeys.Add(key)) return;
            pendingHitSamples.Add(new PendingHitSample(key, handle, clip, signalDefinition));
        }

        /// <summary>在行为取消或释放时移除尚未结算的命中意图，确保被打断的行为不会在全局运动阶段之后继续造成伤害。</summary>
        private void RemovePendingHitSample(BehaviorHandle handle, HitWindowClip clip)
        {
            PendingHitKey key = new PendingHitKey(handle.InstanceId, clip.ClipId);
            if (!pendingHitSampleKeys.Remove(key)) return;
            for (int index = pendingHitSamples.Count - 1; index >= 0; index--)
            {
                if (pendingHitSamples[index].Key != key) continue;
                pendingHitSamples.RemoveAt(index);
                return;
            }
        }

        /// <summary>丢弃上一个因运动阶段异常而未结算的查询意图，并补关已经退出的窗口，使下一 Tick 可以从一致状态继续。</summary>
        private void DiscardUnresolvedHitWork()
        {
            for (int index = 0; index < pendingHitCloses.Count; index++) hitQueryRuntime?.Close(pendingHitCloses[index].Handle, pendingHitCloses[index].Clip);
            ClearPendingHitCollections();
        }

        /// <summary>清空当前 Tick 的全部延迟命中容器，不改变仍然活跃且尚未请求关闭的命中窗口。</summary>
        private void ClearPendingHitCollections()
        {
            pendingHitSamples.Clear();
            pendingHitSampleKeys.Clear();
            pendingHitCloses.Clear();
        }

        /// <summary>为延迟命中阶段收集带有行为实例与窗口编号的异常，允许其他窗口继续完成采样和关闭。</summary>
        private static void AddPendingHitException(ref List<Exception> exceptions, string operation, BehaviorHandle handle, HitWindowClip clip, Exception exception)
        {
            if (exceptions == null) exceptions = new List<Exception>();
            exceptions.Add(new InvalidOperationException($"Failed to {operation} hit window '{clip.ClipId}' for behavior instance '{handle.InstanceId}'.", exception));
        }

        /// <summary>从任意受击、控制、死亡或回收路径取消当前行为。</summary>
        private void CancelActiveBehavior()
        {
            if (behaviorController != null && behaviorController.IsActive) behaviorController.Cancel(behaviorController.ActiveHandle);
            else
            {
                Action<bool> finished = enemyAttackFinished;
                enemyAttackFinished = null;
                finished?.Invoke(false);
            }
        }

        /// <summary>编译共享行为资产并建立稳定命令查找表，每个 Actor 只持有自己的 BehaviorController 状态。</summary>
        private void BuildBehaviorCache()
        {
            IReadOnlyList<ActorBehaviorDefinition> behaviors = authoring.Definition.Behaviors;
            for (int index = 0; index < behaviors.Count; index++)
            {
                ActorBehaviorDefinition behavior = behaviors[index];
                programsById.Add(behavior.BehaviorId, behavior.BuildProgram());
                behaviorDefinitionsById.Add(behavior.BehaviorId, behavior);
                if (behavior.Command == ActorBehaviorCommand.BasicAttack) basicAttackBehaviors.Add(behavior);
            }
            basicAttackBehaviors.Sort((left, right) => left.CommandIndex.CompareTo(right.CommandIndex));
        }

        /// <summary>初始化现有资产化 EnemyAiBrain，使其只依赖通用控制、行为和运动能力。</summary>
        private void InitializeEnemyBrain()
        {
            RequireComponent(out enemyAiComponent);
            if (enemyAiComponent.Definition == null) throw new InvalidOperationException($"Enemy actor '{authoring.name}' requires an EnemyAiDefinition.");
            enemyBrain = new EnemyAiBrain(enemyAiComponent.Definition, this, Entity.EntityId);
            enemyBrain.Start();
        }

        /// <summary>受击表现开始时取消权威行为并挂起 AI，避免表现覆盖后仍然移动或命中。</summary>
        private void OnAttackedStart(AttackedStartEvent evt)
        {
            attackedSuspended = true;
            CancelActiveBehavior();
            RefreshEnemyBrainState();
        }

        /// <summary>受击表现结束时按控制状态恢复 AI。</summary>
        private void OnAttackedEnd(AttackedEndEvent evt)
        {
            attackedSuspended = false;
            RefreshEnemyBrainState();
        }

        /// <summary>控制状态变化时立即取消已经失去 Act 权限的行为并刷新 AI。</summary>
        private void OnControlStateChanged(ControlStateChangedEvent evt)
        {
            if (!propertyComponent.CanAct) CancelActiveBehavior();
            RefreshEnemyBrainState();
        }

        /// <summary>死亡时永久停止固定模拟、行为命中和 AI，但保留旧死亡表现与延迟回收链。</summary>
        private void OnDie(DieEvent evt)
        {
            dead = true;
            CancelActiveBehavior();
            enemyBrain?.Suspend();
        }

        /// <summary>根据受击、死亡和 Property 控制状态决定 EnemyAiBrain 的唯一运行状态。</summary>
        private void RefreshEnemyBrainState()
        {
            if (enemyBrain == null) return;
            if (!dead && !attackedSuspended && propertyComponent.CanAct) enemyBrain.Resume();
            else enemyBrain.Suspend();
        }

        /// <summary>判断 PropertyComponent 是否是当前 AI 可以感知的存活敌对目标。</summary>
        private bool IsValidAiTarget(PropertyComponent targetProperty, string requiredTag)
        {
            if (targetProperty == null || targetProperty == propertyComponent || targetProperty.Entity == null || targetProperty.IsDead || !targetProperty.Entity.IsActive || !targetProperty.gameObject.activeInHierarchy) return false;
            return string.IsNullOrWhiteSpace(requiredTag) || targetProperty.CompareTag(requiredTag);
        }

        /// <summary>从 Entity 获取必需组件并在 Prefab 漏配时抛出明确异常。</summary>
        private void RequireComponent<TComponent>(out TComponent component) where TComponent : IComponent
        {
            if (!Entity.TryGetComp(out component) || ReferenceEquals(component, null)) throw new InvalidOperationException($"Actor '{Entity.bindGo.name}' requires component '{typeof(TComponent).FullName}'.");
        }

        /// <summary>以行为实例编号和 Clip 稳定编号标识当前 Tick 的唯一命中采样意图。</summary>
        private readonly struct PendingHitKey : IEquatable<PendingHitKey>
        {
            /// <summary>创建一个不可变延迟命中键。</summary>
            internal PendingHitKey(long behaviorInstanceId, string clipId)
            {
                BehaviorInstanceId = behaviorInstanceId;
                ClipId = clipId;
            }

            /// <summary>获取 BehaviorController 分配的行为实例编号。</summary>
            private long BehaviorInstanceId { get; }

            /// <summary>获取行为程序内区分大小写的 Clip 稳定编号。</summary>
            private string ClipId { get; }

            /// <inheritdoc />
            public bool Equals(PendingHitKey other)
            {
                return BehaviorInstanceId == other.BehaviorInstanceId && string.Equals(ClipId, other.ClipId, StringComparison.Ordinal);
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is PendingHitKey other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                unchecked
                {
                    return (BehaviorInstanceId.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(ClipId);
                }
            }

            /// <summary>比较两个延迟命中键是否属于同一窗口。</summary>
            public static bool operator ==(PendingHitKey left, PendingHitKey right)
            {
                return left.Equals(right);
            }

            /// <summary>比较两个延迟命中键是否属于不同窗口。</summary>
            public static bool operator !=(PendingHitKey left, PendingHitKey right)
            {
                return !left.Equals(right);
            }
        }

        /// <summary>保存一次将在全局运动阶段后执行的命中查询及其不可变伤害配置。</summary>
        private readonly struct PendingHitSample
        {
            /// <summary>创建一条延迟命中采样记录。</summary>
            internal PendingHitSample(PendingHitKey key, BehaviorHandle handle, HitWindowClip clip, ActorHitSignalDefinition signalDefinition)
            {
                Key = key;
                Handle = handle;
                Clip = clip;
                SignalDefinition = signalDefinition;
            }

            /// <summary>获取用于当前 Tick 去重的稳定窗口键。</summary>
            internal PendingHitKey Key { get; }

            /// <summary>获取创建窗口的行为实例句柄。</summary>
            internal BehaviorHandle Handle { get; }

            /// <summary>获取需要解析的命中窗口。</summary>
            internal HitWindowClip Clip { get; }

            /// <summary>获取行为启动时绑定的命中信号配置。</summary>
            internal ActorHitSignalDefinition SignalDefinition { get; }
        }

        /// <summary>保存一次将在本 Tick 所有采样完成后执行的命中窗口关闭请求。</summary>
        private readonly struct PendingHitClose
        {
            /// <summary>创建一条延迟关闭记录。</summary>
            internal PendingHitClose(BehaviorHandle handle, HitWindowClip clip)
            {
                Handle = handle;
                Clip = clip;
            }

            /// <summary>获取创建窗口的行为实例句柄。</summary>
            internal BehaviorHandle Handle { get; }

            /// <summary>获取需要关闭的命中窗口。</summary>
            internal HitWindowClip Clip { get; }
        }
    }
}
