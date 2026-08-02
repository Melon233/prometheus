using System;
using NUnit.Framework;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证 Pawn 生命周期、控制帧兼容映射以及正式更新阶段接入。</summary>
    public sealed class ActorInputBridgeTests
    {
        /// <summary>验证完整 ControlFrame 能映射全部旧字段，并且瞬时清理不会误清连续或保持输入。</summary>
        [Test]
        public void InputComponent_AppliesControlFrameAndClearsFrameScopedState()
        {
            InputComponent component = new InputComponent();
            ControlButton pressed = ControlButton.Attack | ControlButton.Skill | ControlButton.Ultimate | ControlButton.Dodge | ControlButton.Jump | ControlButton.SpecialAttack | ControlButton.SprintToggle | ControlButton.WalkToggle;
            component.ApplyControlFrame(new ControlFrame(10, 3, new Vector2(0.5f, -1f), Vector2.right, pressed, ControlButton.Attack));
            Assert.That(component.hasInputThisFrame, Is.True);
            Assert.That(component.moveDir, Is.EqualTo(new Vector2(0.5f, -1f)));
            Assert.That(component.wasAtkPressedThisFrame, Is.True);
            Assert.That(component.wasAtkPressed, Is.True);
            Assert.That(component.wasSkillPressedThisFrame, Is.True);
            Assert.That(component.wasUltPressedThisFrame, Is.True);
            Assert.That(component.wasDodgePressedThisFrame, Is.True);
            Assert.That(component.wasJumpPressedThisFrame, Is.True);
            Assert.That(component.wasSpecialAtkPressedThisFrame, Is.True);
            Assert.That(component.wasToggleSprintPressedThisFrame, Is.True);
            Assert.That(component.wasToggleWalkPressedThisFrame, Is.True);
            component.ClearTransientButtons();
            Assert.That(component.wasAtkPressedThisFrame, Is.False);
            Assert.That(component.wasSkillPressedThisFrame, Is.False);
            Assert.That(component.wasUltPressedThisFrame, Is.False);
            Assert.That(component.wasDodgePressedThisFrame, Is.False);
            Assert.That(component.wasJumpPressedThisFrame, Is.False);
            Assert.That(component.wasSpecialAtkPressedThisFrame, Is.False);
            Assert.That(component.wasToggleSprintPressedThisFrame, Is.False);
            Assert.That(component.wasToggleWalkPressedThisFrame, Is.False);
            Assert.That(component.moveDir, Is.EqualTo(new Vector2(0.5f, -1f)));
            Assert.That(component.wasAtkPressed, Is.True);
            component.ClearFrameInput();
            Assert.That(component.hasInputThisFrame, Is.False);
            Assert.That(component.moveDir, Is.EqualTo(Vector2.zero));
            Assert.That(component.wasAtkPressed, Is.False);
        }

        /// <summary>验证 PawnRegistrationLogic 使用 EntityId 注册，并在重复销毁调用时只执行一次有效注销。</summary>
        [Test]
        public void PawnRegistrationLogic_RegistersEntityIdAndUnregistersIdempotently()
        {
            PossessionSystem system = new PossessionSystem();
            StubGameplayKit gameplayKit = new StubGameplayKit(system);
            PawnTestEntity entity = new PawnTestEntity(false);
            entity.BindForActorTests(gameplayKit, 77);
            entity.AfterNew();
            Assert.That(entity.TryGetComp(out PawnComponent pawn), Is.True);
            Assert.That(pawn.IsRegistered, Is.True);
            Assert.That(pawn.PawnId, Is.EqualTo(77));
            system.PrepareFrame(1, 0.016f);
            Assert.That(system.TryGetControlFrame(77, out _), Is.False, "已注册但没有租约的 Pawn 不应收到伪造控制帧，否则敌人 AI 后备控制会被静默禁用。");
            Assert.That(system.HasEffectiveControl(77, ControlScope.All), Is.False);
            Assert.That(entity.TryGetLogic(out PawnRegistrationLogic registrationLogic), Is.True);
            registrationLogic.OnDispose();
            registrationLogic.OnDispose();
            Assert.That(pawn.IsRegistered, Is.False);
            Assert.That(pawn.PawnId, Is.Zero);
            system.PrepareFrame(2, 0.016f);
            Assert.That(system.TryGetControlFrame(77, out _), Is.False);
            system.Dispose();
        }

        /// <summary>验证 InputLogic 只消费 PossessionSystem 的当前 Pawn 帧，并在帧缺失时清除所有旧输入。</summary>
        [Test]
        public void InputLogic_ConsumesPossessionFrameAndClearsMissingFrame()
        {
            PossessionSystem system = new PossessionSystem();
            system.RegisterController(new FixedController(1, new Vector2(-0.25f, 1f), ControlButton.Attack | ControlButton.Jump | ControlButton.SpecialAttack, ControlButton.Attack));
            StubGameplayKit gameplayKit = new StubGameplayKit(system);
            PawnTestEntity entity = new PawnTestEntity(true);
            entity.BindForActorTests(gameplayKit, 91);
            entity.AfterNew();
            system.AcquireLease(new ControlLeaseRequest(1, 91, ControlScope.All, 0));
            system.PrepareFrame(1, 0.016f);
            Assert.That(entity.TryGetLogic(out InputLogic inputLogic), Is.True);
            Assert.That(entity.TryGetComp(out InputComponent input), Is.True);
            inputLogic.OnUpdate(0.016f);
            Assert.That(input.moveDir, Is.EqualTo(new Vector2(-0.25f, 1f)));
            Assert.That(input.wasAtkPressedThisFrame, Is.True);
            Assert.That(input.wasAtkPressed, Is.True);
            Assert.That(input.wasJumpPressedThisFrame, Is.True);
            Assert.That(input.wasSpecialAtkPressedThisFrame, Is.True);
            system.UnregisterPawn(91);
            inputLogic.OnUpdate(0.016f);
            Assert.That(input.hasInputThisFrame, Is.False);
            Assert.That(input.moveDir, Is.EqualTo(Vector2.zero));
            Assert.That(input.wasAtkPressedThisFrame, Is.False);
            Assert.That(input.wasAtkPressed, Is.False);
            Assert.That(input.wasJumpPressedThisFrame, Is.False);
            Assert.That(input.wasSpecialAtkPressedThisFrame, Is.False);
            Assert.That(entity.TryGetLogic(out PawnRegistrationLogic registrationLogic), Is.True);
            registrationLogic.OnDispose();
            inputLogic.OnDispose();
            system.Dispose();
        }

        /// <summary>验证控制采样和镜头推进只覆写其正式生命周期阶段，不会在普通 System Update 重复执行。</summary>
        [Test]
        public void Systems_OverrideOnlyTheirDedicatedUpdatePhases()
        {
            Assert.That(typeof(PossessionSystem).GetMethod(nameof(XSystem.OnBeforeEntityUpdate)).DeclaringType, Is.EqualTo(typeof(PossessionSystem)));
            Assert.That(typeof(PossessionSystem).GetMethod(nameof(XSystem.OnUpdate)).DeclaringType, Is.EqualTo(typeof(XSystem)));
            Assert.That(typeof(CameraDirectorSystem).GetMethod(nameof(XSystem.OnLateUpdate)).DeclaringType, Is.EqualTo(typeof(CameraDirectorSystem)));
            Assert.That(typeof(CameraDirectorSystem).GetMethod(nameof(XSystem.OnUpdate)).DeclaringType, Is.EqualTo(typeof(XSystem)));
        }

        /// <summary>组合 Pawn 注册与可选输入桥接 Logic 的最小测试 Entity。</summary>
        private sealed class PawnTestEntity : Entity
        {
            /// <summary>创建最小测试 Entity。</summary>
            internal PawnTestEntity(bool includeInput)
            {
                AddComp<PawnComponent>();
                AddLogic<PawnRegistrationLogic>();
                if (!includeInput) return;
                AddComp<InputComponent>();
                AddLogic<InputLogic>();
            }
        }

        /// <summary>只提供 PossessionSystem 查询能力的单局 GameplayKit 替身。</summary>
        private sealed class StubGameplayKit : IGameplayKit
        {
            /// <summary>测试使用的控制权系统。</summary>
            private readonly PossessionSystem possessionSystem;

            /// <summary>创建 GameplayKit 替身。</summary>
            internal StubGameplayKit(PossessionSystem possessionSystem)
            {
                this.possessionSystem = possessionSystem ?? throw new ArgumentNullException(nameof(possessionSystem));
            }

            /// <inheritdoc />
            public bool IsReady => true;

            /// <inheritdoc />
            public PlayerEntity Player => null;

            /// <inheritdoc />
            public int AddEntity(Entity entity)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public bool TryGetEntity(int entityId, out Entity entity)
            {
                entity = null;
                return false;
            }

            /// <inheritdoc />
            public bool RemoveEntity(int entityId)
            {
                return false;
            }

            /// <inheritdoc />
            public bool RequestRemoveEntity(int entityId, float destroyDelay = 0f)
            {
                return false;
            }

            /// <inheritdoc />
            public TSystem GetSystem<TSystem>() where TSystem : XSystem
            {
                if (possessionSystem is TSystem typedSystem) return typedSystem;
                throw new InvalidOperationException($"Test GameplayKit does not contain system '{typeof(TSystem).FullName}'.");
            }

            /// <inheritdoc />
            public bool TryGetSystem<TSystem>(out TSystem system) where TSystem : XSystem
            {
                system = possessionSystem as TSystem;
                return system != null;
            }
        }

        /// <summary>为输入桥接测试提供固定 ControlFrame 的纯测试控制器。</summary>
        private sealed class FixedController : IControllerRuntime
        {
            /// <summary>固定移动输入。</summary>
            private readonly Vector2 move;

            /// <summary>固定瞬时按钮。</summary>
            private readonly ControlButton pressed;

            /// <summary>固定保持按钮。</summary>
            private readonly ControlButton held;

            /// <summary>创建固定输出控制器。</summary>
            internal FixedController(int controllerId, Vector2 move, ControlButton pressed, ControlButton held)
            {
                ControllerId = controllerId;
                this.move = move;
                this.pressed = pressed;
                this.held = held;
            }

            /// <inheritdoc />
            public int ControllerId { get; }

            /// <inheritdoc />
            public ControlFrame Sample(ControllerSampleContext context)
            {
                return new ControlFrame(context.FrameId, 0, move, move, pressed, held);
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }
    }
}
