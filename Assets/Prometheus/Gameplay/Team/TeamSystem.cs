using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>管理三个独立小队 Entity 的固定槽位、上场切换、输入控制权、显隐和 HUD 观察目标。</summary>
    public sealed class TeamSystem : XSystem, IInputReceiver
    {
        /// <summary>定义当前本地小队固定支持的上场角色配置数量。</summary>
        public const int Capacity = 3;

        /// <summary>保存三个固定槽位的运行时成员数据。</summary>
        private readonly TeamMemberRuntime[] members = new TeamMemberRuntime[Capacity];

        /// <summary>保存所属单局玩法世界，输入适配器通过它按 EntityId 解析成员。</summary>
        private IGameplayKit gameplayKit;

        /// <summary>保存单局输入系统，用于迁移当前上场成员的玩法动作租约。</summary>
        private InputSystem inputSystem;

        /// <summary>保存全局事件总线，只用于发布当前上场成员变化事实。</summary>
        private IEventKit eventKit;

        /// <summary>保存数字键一二三的独占输入租约。</summary>
        private ControlLease teamSelectionLease;

        /// <summary>保存当前上场成员的玩法输入租约，切换时会先释放旧租约再绑定新成员。</summary>
        private ControlLease activeMemberInputLease;

        /// <summary>保存当前上场成员的零基槽位；尚未初始化或没有可用成员时为负一。</summary>
        private int activeSlotIndex = -1;

        /// <summary>保存当前输入帧请求的零基槽位；没有切换请求时为负一。</summary>
        private int pendingSlotIndex = -1;

        /// <summary>保存触发本次切换的完整输入快照，使切入成员可以在同一帧直接接管移动和动作输入。</summary>
        private InputFrame pendingSwitchFrame;

        /// <summary>标记当前是否保存了与槽位请求配对的输入快照。</summary>
        private bool hasPendingSwitchFrame;

        /// <summary>标记三个成员是否已经完成唯一一次运行时绑定。</summary>
        private bool isInitialized;

        /// <summary>标记当前系统是否已经释放，阻止失效输入接收者继续存活。</summary>
        private bool isDisposed;

        /// <summary>获取当前上场成员的零基槽位；没有可用成员时为负一。</summary>
        public int ActiveSlotIndex => activeSlotIndex;

        /// <summary>获取当前上场的独立 Entity；没有可用成员时返回空。</summary>
        public Entity ActiveMember => IsValidSlot(activeSlotIndex) && members[activeSlotIndex] != null ? members[activeSlotIndex].Entity : null;

        /// <summary>获取当前上场成员的运行时编号；没有可用成员时返回零。</summary>
        public int ActiveEntityId => ActiveMember == null ? 0 : ActiveMember.EntityId;

        /// <inheritdoc />
        public bool IsAlive => !isDisposed;

        /// <summary>绑定所属 GameplayKit 和数字键选择输入，成员 Entity 随后再由 GameplayKit 创建。</summary>
        public override void AfterNew(IGameplayKit ownerGameplayKit)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(TeamSystem));
            gameplayKit = ownerGameplayKit ?? throw new ArgumentNullException(nameof(ownerGameplayKit));
            inputSystem = gameplayKit.GetSystem<InputSystem>();
            eventKit = Core.Event ?? throw new InvalidOperationException("TeamSystem requires EventKit.");
            teamSelectionLease = inputSystem.AcquireControl(inputSystem.DefaultSourceId, this, InputActionMask.TeamSelection, InputContexts.Gameplay);
        }

        /// <summary>把三个已经完成 Entity 初始化的成员绑定到固定槽位，并默认让第一个成员上场。</summary>
        public void InitializeMembers(IReadOnlyList<Entity> teamMembers)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(TeamSystem));
            if (gameplayKit == null || inputSystem == null || eventKit == null) throw new InvalidOperationException("TeamSystem must complete AfterNew before members are initialized.");
            if (isInitialized) throw new InvalidOperationException("TeamSystem members can only be initialized once.");
            if (teamMembers == null) throw new ArgumentNullException(nameof(teamMembers));
            if (teamMembers.Count != Capacity) throw new ArgumentException($"TeamSystem requires exactly {Capacity} members.", nameof(teamMembers));
            HashSet<int> entityIds = new HashSet<int>();
            for (int slotIndex = 0; slotIndex < Capacity; slotIndex++)
            {
                Entity entity = teamMembers[slotIndex] ?? throw new ArgumentException($"Team member slot {slotIndex} cannot be null.", nameof(teamMembers));
                if (!ReferenceEquals(entity.GameplayKit, gameplayKit)) throw new ArgumentException($"Team member slot {slotIndex} belongs to another GameplayKit.", nameof(teamMembers));
                if (!entity.IsActive) throw new ArgumentException($"Team member slot {slotIndex} must complete Entity.AfterNew before team initialization.", nameof(teamMembers));
                if (!entityIds.Add(entity.EntityId)) throw new ArgumentException($"Team member EntityId {entity.EntityId} is configured more than once.", nameof(teamMembers));
                members[slotIndex] = CreateMemberRuntime(entity, slotIndex);
            }
            isInitialized = true;
            for (int slotIndex = 0; slotIndex < Capacity; slotIndex++) DeactivateMember(members[slotIndex], false);
            activeSlotIndex = 0;
            ActivateMember(members[activeSlotIndex], default);
            BindActiveMemberInput();
            PublishActiveMemberTransition(0, ActiveEntityId, -1, activeSlotIndex);
        }

        /// <summary>获取指定零基槽位中的成员；空槽位或越界请求返回失败。</summary>
        public bool TryGetMember(int slotIndex, out Entity member)
        {
            if (IsValidSlot(slotIndex) && members[slotIndex] != null)
            {
                member = members[slotIndex].Entity;
                return true;
            }
            member = null;
            return false;
        }

        /// <summary>切换到指定零基槽位，成功时同步迁移位置、显隐、行为门禁、输入和 HUD 目标。</summary>
        public bool SwitchToSlot(int slotIndex)
        {
            if (!isInitialized || isDisposed || !IsSelectableSlot(slotIndex) || slotIndex == activeSlotIndex) return false;
            int previousSlotIndex = activeSlotIndex;
            TeamMemberRuntime previousMember = IsValidSlot(previousSlotIndex) ? members[previousSlotIndex] : null;
            TeamTransferState transferState = CaptureTransferState(previousMember);
            int previousEntityId = previousMember == null ? 0 : previousMember.Entity.EntityId;
            activeMemberInputLease?.Dispose();
            activeMemberInputLease = null;
            if (previousMember != null) DeactivateMember(previousMember, true);
            activeSlotIndex = slotIndex;
            ActivateMember(members[activeSlotIndex], transferState);
            BindActiveMemberInput();
            PublishActiveMemberTransition(previousEntityId, ActiveEntityId, previousSlotIndex, activeSlotIndex);
            return true;
        }

        /// <summary>在实体正式回收前移除其小队槽位；若移除当前成员则自动切入下一个存活成员。</summary>
        internal void UnregisterMember(Entity entity)
        {
            if (!isInitialized || entity == null) return;
            int removedSlotIndex = FindMemberSlot(entity);
            if (!IsValidSlot(removedSlotIndex)) return;
            TeamMemberRuntime removedMember = members[removedSlotIndex];
            bool removedActiveMember = removedSlotIndex == activeSlotIndex;
            if (!removedActiveMember)
            {
                members[removedSlotIndex] = null;
                return;
            }
            TeamTransferState transferState = CaptureTransferState(removedMember);
            int previousEntityId = removedMember.Entity.EntityId;
            activeMemberInputLease?.Dispose();
            activeMemberInputLease = null;
            DeactivateMember(removedMember, true);
            members[removedSlotIndex] = null;
            activeSlotIndex = FindNextSelectableSlot(removedSlotIndex);
            if (IsValidSlot(activeSlotIndex))
            {
                ActivateMember(members[activeSlotIndex], transferState);
                BindActiveMemberInput();
            }
            PublishActiveMemberTransition(previousEntityId, ActiveEntityId, removedSlotIndex, activeSlotIndex);
        }

        /// <summary>在 InputSystem 完成本帧采样后处理唯一一次数字键切换，确保旧成员输入能在 Entity 更新前清空。</summary>
        public override void BeforeEntityUpdate(float dt)
        {
            int requestedSlotIndex = pendingSlotIndex;
            InputFrame switchFrame = pendingSwitchFrame;
            bool shouldReplayGameplayInput = hasPendingSwitchFrame;
            pendingSlotIndex = -1;
            pendingSwitchFrame = default;
            hasPendingSwitchFrame = false;
            if (requestedSlotIndex >= 0 && SwitchToSlot(requestedSlotIndex) && shouldReplayGameplayInput) members[activeSlotIndex].InputComponent.ApplyInput(switchFrame, InputActionMask.Gameplay);
        }

        /// <inheritdoc />
        public void ResetInput()
        {
            pendingSlotIndex = -1;
            pendingSwitchFrame = default;
            hasPendingSwitchFrame = false;
        }

        /// <summary>读取数字键一二三的按下沿，并在同帧多键输入时稳定选择最靠前的槽位。</summary>
        public void ReceiveInput(in InputFrame frame, InputActionMask actions)
        {
            int requestedSlotIndex = -1;
            if ((actions & InputActionMask.SelectTeamMember1) != 0 && frame.SelectTeamMember1.PressedThisFrame) requestedSlotIndex = 0;
            else if ((actions & InputActionMask.SelectTeamMember2) != 0 && frame.SelectTeamMember2.PressedThisFrame) requestedSlotIndex = 1;
            else if ((actions & InputActionMask.SelectTeamMember3) != 0 && frame.SelectTeamMember3.PressedThisFrame) requestedSlotIndex = 2;
            if (requestedSlotIndex < 0) return;
            pendingSlotIndex = requestedSlotIndex;
            pendingSwitchFrame = frame;
            hasPendingSwitchFrame = true;
        }

        /// <summary>释放输入租约与运行时引用；成员 Entity 的最终销毁仍由 GameplayKit 统一负责。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            activeMemberInputLease?.Dispose();
            teamSelectionLease?.Dispose();
            activeMemberInputLease = null;
            teamSelectionLease = null;
            for (int slotIndex = 0; slotIndex < Capacity; slotIndex++) members[slotIndex] = null;
            activeSlotIndex = -1;
            pendingSlotIndex = -1;
            pendingSwitchFrame = default;
            hasPendingSwitchFrame = false;
            isInitialized = false;
            eventKit = null;
            inputSystem = null;
            gameplayKit = null;
            isDisposed = true;
        }

        /// <summary>解析成员必需组件、绑定固定槽位，并把组件引用集中到系统运行时记录。</summary>
        private static TeamMemberRuntime CreateMemberRuntime(Entity entity, int slotIndex)
        {
            if (!entity.TryGetComp(out TeamMemberComponent teamMemberComponent)) throw new InvalidOperationException($"Team member Entity {entity.EntityId} requires TeamMemberComponent.");
            if (!entity.TryGetComp(out PropertyComponent propertyComponent)) throw new InvalidOperationException($"Team member Entity {entity.EntityId} requires PropertyComponent.");
            if (!entity.TryGetComp(out InputComponent inputComponent)) throw new InvalidOperationException($"Team member Entity {entity.EntityId} requires InputComponent.");
            if (!entity.TryGetComp(out SpineComponent spineComponent)) throw new InvalidOperationException($"Team member Entity {entity.EntityId} requires SpineComponent.");
            if (!entity.TryGetComp(out MotionComponent motionComponent)) throw new InvalidOperationException($"Team member Entity {entity.EntityId} requires MotionComponent.");
            if (!entity.TryGetComp(out VfxComponent vfxComponent)) throw new InvalidOperationException($"Team member Entity {entity.EntityId} requires VfxComponent.");
            teamMemberComponent.Initialize(slotIndex);
            return new TeamMemberRuntime(entity, teamMemberComponent, propertyComponent, inputComponent, spineComponent, motionComponent, vfxComponent);
        }

        /// <summary>让成员退出场景控制权，立即清空输入和动画并施加 OffField 行为门禁，同时保留 Entity 与 Effect 生命周期。</summary>
        private static void DeactivateMember(TeamMemberRuntime member, bool interruptCurrentAction)
        {
            if (member == null) return;
            member.InputComponent.ResetInput();
            if (interruptCurrentAction) member.SpineComponent.ClearTrack(0, AnimationEndReason.Interrupted);
            member.VfxComponent.StopAll();
            member.MotionComponent.curVelo = Vector3.zero;
            member.MotionComponent.landThisFrame = false;
            member.MotionComponent.wasGroundedLastFrame = false;
            if (member.OffFieldModifier == null) member.OffFieldModifier = member.PropertyComponent.AddControlStateModifier(ControlState.OffField);
            member.TeamMemberComponent.SetOnField(false);
            if (member.Entity.bindGo != null) member.Entity.bindGo.SetActive(false);
        }

        /// <summary>让成员在交接位置进入场景，移除 OffField 门禁并恢复其独立运行对象的显示。</summary>
        private static void ActivateMember(TeamMemberRuntime member, TeamTransferState transferState)
        {
            if (member == null) return;
            if (member.Entity.bindGo == null) throw new InvalidOperationException($"Team member Entity {member.Entity.EntityId} has no bound GameObject.");
            if (transferState.HasSource) member.Entity.bindGo.transform.SetPositionAndRotation(transferState.Position, transferState.Rotation);
            if (member.OffFieldModifier != null)
            {
                member.PropertyComponent.RemoveControlStateModifier(member.OffFieldModifier);
                member.OffFieldModifier = null;
            }
            member.InputComponent.ResetInput();
            member.MotionComponent.curVelo = transferState.HasSource ? transferState.Velocity : Vector3.zero;
            member.MotionComponent.wasGroundedLastFrame = transferState.HasSource && transferState.WasGrounded;
            member.MotionComponent.landThisFrame = false;
            member.TeamMemberComponent.SetOnField(true);
            member.Entity.bindGo.SetActive(true);
        }

        /// <summary>为当前成员重新申请全部玩法动作输入，旧租约已经由切换流程提前释放。</summary>
        private void BindActiveMemberInput()
        {
            activeMemberInputLease?.Dispose();
            activeMemberInputLease = ActiveMember == null ? null : inputSystem.AcquireEntityControl(ActiveEntityId, InputActionMask.Gameplay, InputContexts.Gameplay);
        }

        /// <summary>通知所有观察者切换 EntityId；HUD 随后通过 EntitySystem 立即读取新成员当前字段。</summary>
        private void PublishActiveMemberTransition(int previousEntityId, int currentEntityId, int previousSlotIndex, int currentSlotIndex)
        {
            if (eventKit == null) return;
            eventKit.Invoke(Event.ActiveTeamMemberChanged, new ActiveTeamMemberChangedEvent(previousEntityId, currentEntityId, previousSlotIndex, currentSlotIndex));
        }

        /// <summary>捕获切换瞬间的位置、朝向和速度，使新成员在同一战斗位置无缝接管。</summary>
        private static TeamTransferState CaptureTransferState(TeamMemberRuntime member)
        {
            if (member == null || member.Entity.bindGo == null) return default;
            Transform transform = member.Entity.bindGo.transform;
            bool wasGrounded = member.MotionComponent.cc != null && member.MotionComponent.cc.isGrounded;
            return new TeamTransferState(transform.position, transform.rotation, member.MotionComponent.curVelo, wasGrounded);
        }

        /// <summary>从指定槽位之后循环查找第一个仍可切入的成员。</summary>
        private int FindNextSelectableSlot(int removedSlotIndex)
        {
            for (int offset = 1; offset <= Capacity; offset++)
            {
                int candidateSlotIndex = (removedSlotIndex + offset) % Capacity;
                if (IsSelectableSlot(candidateSlotIndex)) return candidateSlotIndex;
            }
            return -1;
        }

        /// <summary>按对象身份查找成员当前占用的固定槽位。</summary>
        private int FindMemberSlot(Entity entity)
        {
            for (int slotIndex = 0; slotIndex < Capacity; slotIndex++)
            {
                if (members[slotIndex] != null && ReferenceEquals(members[slotIndex].Entity, entity)) return slotIndex;
            }
            return -1;
        }

        /// <summary>判断目标槽位是否存在一个仍存活、未进入回收流程的成员。</summary>
        private bool IsSelectableSlot(int slotIndex)
        {
            if (!IsValidSlot(slotIndex) || members[slotIndex] == null) return false;
            TeamMemberRuntime member = members[slotIndex];
            return member.Entity.IsActive && !member.PropertyComponent.IsDead;
        }

        /// <summary>判断零基槽位是否位于固定三人小队范围内。</summary>
        private static bool IsValidSlot(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < Capacity;
        }

        /// <summary>集中保存一个槽位的 Entity 与切换所需组件引用，运行态不写回共享配置资产。</summary>
        private sealed class TeamMemberRuntime
        {
            /// <summary>创建一个完成组件解析的小队成员运行时记录。</summary>
            public TeamMemberRuntime(Entity entity, TeamMemberComponent teamMemberComponent, PropertyComponent propertyComponent, InputComponent inputComponent, SpineComponent spineComponent, MotionComponent motionComponent, VfxComponent vfxComponent)
            {
                Entity = entity;
                TeamMemberComponent = teamMemberComponent;
                PropertyComponent = propertyComponent;
                InputComponent = inputComponent;
                SpineComponent = spineComponent;
                MotionComponent = motionComponent;
                VfxComponent = vfxComponent;
            }

            /// <summary>获取成员独立拥有的 Entity。</summary>
            public Entity Entity { get; }

            /// <summary>获取成员槽位和上场状态组件。</summary>
            public TeamMemberComponent TeamMemberComponent { get; }

            /// <summary>获取成员数值与控制状态组件。</summary>
            public PropertyComponent PropertyComponent { get; }

            /// <summary>获取成员逐帧输入状态组件。</summary>
            public InputComponent InputComponent { get; }

            /// <summary>获取成员统一动画会话组件。</summary>
            public SpineComponent SpineComponent { get; }

            /// <summary>获取成员位移运行态组件。</summary>
            public MotionComponent MotionComponent { get; }

            /// <summary>获取成员动作特效槽位组件。</summary>
            public VfxComponent VfxComponent { get; }

            /// <summary>获取 TeamSystem 当前持有的 OffField 控制状态句柄。</summary>
            public ControlStateModifier OffFieldModifier { get; set; }
        }

        /// <summary>保存一次成员交接需要复制的世界姿态和运动状态。</summary>
        private readonly struct TeamTransferState
        {
            /// <summary>创建一份来源有效的交接状态。</summary>
            public TeamTransferState(Vector3 position, Quaternion rotation, Vector3 velocity, bool wasGrounded)
            {
                HasSource = true;
                Position = position;
                Rotation = rotation;
                Velocity = velocity;
                WasGrounded = wasGrounded;
            }

            /// <summary>获取当前结构是否包含旧成员来源。</summary>
            public bool HasSource { get; }

            /// <summary>获取切换瞬间的世界位置。</summary>
            public Vector3 Position { get; }

            /// <summary>获取切换瞬间的世界旋转。</summary>
            public Quaternion Rotation { get; }

            /// <summary>获取切换瞬间的合成速度。</summary>
            public Vector3 Velocity { get; }

            /// <summary>获取旧成员切换瞬间是否接地。</summary>
            public bool WasGrounded { get; }
        }
    }
}
