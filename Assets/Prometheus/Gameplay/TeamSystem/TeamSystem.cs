using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>管理三个独立小队 Entity 的固定槽位、上场切换、输入控制权、显隐和 HUD 观察目标。</summary>
    internal sealed class TeamSystem : XSystem, ITeamSystem, IInputReceiver
    {
        /// <summary>定义当前本地小队固定支持的上场角色配置数量。</summary>
        public const int Capacity = 3;

        /// <summary>每个固定小队槽位使用的角色预制体 YooAsset 地址；数量必须与 Capacity 一致。</summary>
        /// <remarks>「小队成员是谁」是本系统的领域知识，因此地址私有；小队的创建也由本系统自己完成。</remarks>
        private static readonly string[] MemberAddresses = { "Yefa", "Yousaer", "Senyin" };

        /// <summary>保存三个固定槽位的运行时成员数据。</summary>
        private readonly TeamMemberRuntime[] members = new TeamMemberRuntime[Capacity];

        /// <summary>构造注入的输入系统，用于迁移当前上场成员的玩法动作租约。</summary>
        private readonly IInputSystem inputSystem;

        /// <summary>构造注入的实体容器，用于创建与回收本小队的成员实体。</summary>
        private readonly IEntitySystem entitySystem;

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

        /// <summary>创建小队系统；两个依赖都以契约注入，因此依赖关系写在签名上而不是散落在方法体里。</summary>
        /// <param name="inputSystem">提供输入租约的输入系统。</param>
        /// <param name="entitySystem">承载小队成员实体的实体容器。</param>
        public TeamSystem(IInputSystem inputSystem, IEntitySystem entitySystem)
        {
            this.inputSystem = inputSystem ?? throw new ArgumentNullException(nameof(inputSystem));
            this.entitySystem = entitySystem ?? throw new ArgumentNullException(nameof(entitySystem));
        }

        /// <summary>获取当前上场成员的零基槽位；没有可用成员时为负一。</summary>
        public int ActiveSlotIndex => activeSlotIndex;

        /// <summary>获取当前上场的独立 Entity；没有可用成员时返回空。</summary>
        public Entity ActiveMember => IsValidSlot(activeSlotIndex) && members[activeSlotIndex] != null ? members[activeSlotIndex].Entity : null;

        /// <summary>获取当前上场成员的运行时编号；没有可用成员时返回零。</summary>
        public int ActiveEntityId => ActiveMember == null ? 0 : ActiveMember.EntityId;

        /// <inheritdoc />
        public bool IsAlive => !isDisposed;

        /// <summary>绑定数字键选择输入，并订阅实体移除通知；成员实体在进入世界时才创建。</summary>
        public override void AfterNew()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(TeamSystem));
            teamSelectionLease = inputSystem.AcquireControl(inputSystem.DefaultSourceId, this, InputActionMask.TeamSelection, InputContexts.Gameplay);
            // 订阅而不是被实体容器直接调用：实体容器在依赖图底层，不能反过来认识小队（否则 Team 到 Input 到 Entity 成环）。
            Core.Event.AddListener<EntityRemovedEvent>(OnEntityRemoved);
        }

        /// <summary>
        /// 进入世界时创建三个固定槽位的成员实体并完成一次原子初始化。
        ///
        /// 小队成员是绑定场景的实体，因此归世界相位而不是会话相位：
        /// 换场景时旧成员随场景销毁，进入新场景再按同一份配置重建。
        /// </summary>
        /// <param name="world">本次进入的世界上下文。</param>
        public override UniTask OnWorldEnterAsync(WorldContext world)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(TeamSystem));
            // 出生位姿由世界上下文给出而不是本系统持有：同一个世界首次进入用配置的出生点，
            // 从副本弹回时用记录的返回点，只有持有世界栈的流程知道该用哪个。
            CreateMembers(world.SpawnPosition, world.SpawnRotation);
            return UniTask.CompletedTask;
        }

        /// <summary>离开世界时释放成员引用与上场输入租约；成员实体本身由实体容器统一回收。</summary>
        public override void OnWorldExit()
        {
            if (isDisposed) return;
            activeMemberInputLease?.Dispose();
            activeMemberInputLease = null;
            for (int slotIndex = 0; slotIndex < Capacity; slotIndex++) members[slotIndex] = null;
            activeSlotIndex = -1;
            pendingSlotIndex = -1;
            pendingSwitchFrame = default;
            hasPendingSwitchFrame = false;
            isInitialized = false;
        }

        /// <summary>从三个固定槽位配置创建独立 PlayerEntity，并在全部成员就绪后原子初始化槽位。</summary>
        /// <param name="spawnPosition">本次进入世界的出生坐标。</param>
        /// <param name="spawnRotation">本次进入世界的出生朝向。</param>
        private void CreateMembers(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            List<Entity> createdMembers = new List<Entity>(Capacity);
            try
            {
                for (int slotIndex = 0; slotIndex < Capacity; slotIndex++)
                {
                    int entityId = 0;
                    try
                    {
                        PlayerEntity member = new PlayerEntity(MemberAddresses[slotIndex], spawnPosition, spawnRotation, PersistentRoot.Shared);
                        entityId = entitySystem.AddEntity(member);
                        member.AfterNew();
                        createdMembers.Add(member);
                    }
                    catch
                    {
                        if (entityId > 0) entitySystem.RemoveEntity(entityId);
                        throw;
                    }
                }
                InitializeMembers(createdMembers);
            }
            catch
            {
                for (int index = createdMembers.Count - 1; index >= 0; index--)
                {
                    Entity member = createdMembers[index];
                    if (member != null && !member.IsDespawningOrDisposed) entitySystem.RemoveEntity(member.EntityId);
                }
                throw;
            }
        }

        /// <summary>把三个已经完成 Entity 初始化的成员绑定到固定槽位，并默认让第一个成员上场。</summary>
        private void InitializeMembers(IReadOnlyList<Entity> teamMembers)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(TeamSystem));
            if (isInitialized) throw new InvalidOperationException("TeamSystem members can only be initialized once per world.");
            if (teamMembers == null) throw new ArgumentNullException(nameof(teamMembers));
            if (teamMembers.Count != Capacity) throw new ArgumentException($"TeamSystem requires exactly {Capacity} members.", nameof(teamMembers));
            HashSet<int> entityIds = new HashSet<int>();
            for (int slotIndex = 0; slotIndex < Capacity; slotIndex++)
            {
                Entity entity = teamMembers[slotIndex] ?? throw new ArgumentException($"Team member slot {slotIndex} cannot be null.", nameof(teamMembers));
                if (!entity.IsActive) throw new ArgumentException($"Team member slot {slotIndex} must complete Entity.AfterNew before team initialization.", nameof(teamMembers));
                if (!entityIds.Add(entity.EntityId)) throw new ArgumentException($"Team member EntityId {entity.EntityId} is configured more than once.", nameof(teamMembers));
                members[slotIndex] = CreateMemberRuntime(entity, slotIndex);
            }
            isInitialized = true;
            for (int slotIndex = 0; slotIndex < Capacity; slotIndex++) DeactivateMember(members[slotIndex], false);
            activeSlotIndex = 0;
            // var trs = new TeamTransferState(new Vector3(475f, 0.9518f, -492f), Quaternion.identity, Vector3.zero, false);
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
        /// <param name="evt">实体容器广播的移除通知；与本小队无关的实体会被直接忽略。</param>
        private void OnEntityRemoved(EntityRemovedEvent evt)
        {
            if (!isInitialized || evt == null) return;
            int removedSlotIndex = FindMemberSlot(evt.EntityId);
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
            Core.Event.RemoveListener<EntityRemovedEvent>(OnEntityRemoved);
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
            Core.Event.Invoke(new ActiveTeamMemberChangedEvent(previousEntityId, currentEntityId, previousSlotIndex, currentSlotIndex));
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
        private int FindMemberSlot(int entityId)
        {
            for (int slotIndex = 0; slotIndex < Capacity; slotIndex++)
            {
                if (members[slotIndex] != null && members[slotIndex].Entity.EntityId == entityId) return slotIndex;
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
