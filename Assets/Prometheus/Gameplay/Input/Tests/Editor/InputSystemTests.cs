using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace Xuan.Prometheus.Input.Tests
{
    /// <summary>验证输入系统的唯一注册、单次采样、动作级路由、优先级恢复和逐帧状态清理。</summary>
    public sealed class InputSystemTests : InputTestFixture
    {
        /// <summary>验证 InputSystem 是由 GameplayKit 托管的普通 C# System，而不是 MonoBehaviour。</summary>
        [Test]
        public void InputSystem_IsPlainGameplaySystemInsteadOfMonoBehaviour()
        {
            Assert.That(typeof(XSystem).IsAssignableFrom(typeof(InputSystem)), Is.True);
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(InputSystem)), Is.False);
        }

        /// <summary>验证 GameplayKit 配置阶段会注册且只暴露一个 InputSystem。</summary>
        [Test]
        public void GameplayKit_RegistersUniqueInputSystem()
        {
            GameObject runtimeRoot = new GameObject("InputSystemTests.RuntimeRoot");
            EffectLibrary effectLibrary = ScriptableObject.CreateInstance<EffectLibrary>();
            AssetKit assetKit = new AssetKit();
            GameplayKit gameplayKit = new GameplayKit(assetKit);
            try
            {
                GameplayStartupOptions options = new GameplayStartupOptions(AssetKit.DefaultPackageName, runtimeRoot.transform, effectLibrary, "Player", "Enemy", Array.Empty<Transform>(), 0);
                gameplayKit.Configure(options);
                InputSystem inputSystem = gameplayKit.GetSystem<InputSystem>();
                Assert.That(inputSystem, Is.Not.Null);
                Assert.That(inputSystem.DefaultSourceId, Is.EqualTo(UnityInputActionSource.LocalSourceId));
            }
            finally
            {
                gameplayKit.Dispose();
                assetKit.Dispose();
                UnityEngine.Object.DestroyImmediate(effectLibrary);
                UnityEngine.Object.DestroyImmediate(runtimeRoot);
            }
        }

        /// <summary>验证一个输入源每帧只采样一次，并能把不同动作分别交给多个非 Entity 接收者。</summary>
        [Test]
        public void BeforeEntityUpdate_SamplesOnceAndRoutesSplitActions()
        {
            FakeInputSource source = new FakeInputSource("Test", new Vector2(0.75f, -0.25f), true, true);
            RecordingReceiver movementReceiver = new RecordingReceiver();
            RecordingReceiver attackReceiver = new RecordingReceiver();
            using (InputSystem inputSystem = new InputSystem(source))
            {
                inputSystem.AcquireControl(source.SourceId, movementReceiver, InputActionMask.Move, InputContexts.Gameplay);
                inputSystem.AcquireControl(source.SourceId, attackReceiver, InputActionMask.Attack, InputContexts.Gameplay);
                inputSystem.BeforeEntityUpdate(0.016f);
                Assert.That(source.SampleCount, Is.EqualTo(1));
                Assert.That(inputSystem.CurrentFrameId, Is.EqualTo(1));
                Assert.That(movementReceiver.LastActions, Is.EqualTo(InputActionMask.Move));
                Assert.That(movementReceiver.LastMove, Is.EqualTo(source.Move));
                Assert.That(attackReceiver.LastActions, Is.EqualTo(InputActionMask.Attack));
                Assert.That(attackReceiver.AttackPressedThisFrame, Is.True);
                Assert.That(attackReceiver.AttackHeld, Is.True);
            }
            Assert.That(source.IsDisposed, Is.True);
        }

        /// <summary>验证高优先级目标只接管重叠动作，释放租约后低优先级目标在下一帧恢复。</summary>
        [Test]
        public void HigherPriorityPartialBinding_OverridesAndReleaseRestoresDefaultBinding()
        {
            FakeInputSource source = new FakeInputSource("Test", Vector2.right, true, true);
            RecordingReceiver defaultReceiver = new RecordingReceiver();
            RecordingReceiver takeoverReceiver = new RecordingReceiver();
            using (InputSystem inputSystem = new InputSystem(source))
            {
                inputSystem.AcquireControl(source.SourceId, defaultReceiver, InputActionMask.Gameplay, InputContexts.Gameplay);
                ControlLease takeoverLease = inputSystem.AcquireControl(source.SourceId, takeoverReceiver, InputActionMask.Move, InputContexts.Gameplay, 10);
                inputSystem.BeforeEntityUpdate(0.016f);
                Assert.That(defaultReceiver.LastActions, Is.EqualTo(InputActionMask.Gameplay & ~InputActionMask.Move));
                Assert.That(takeoverReceiver.LastActions, Is.EqualTo(InputActionMask.Move));
                takeoverLease.Dispose();
                inputSystem.BeforeEntityUpdate(0.016f);
                Assert.That(defaultReceiver.LastActions, Is.EqualTo(InputActionMask.Gameplay));
                Assert.That(takeoverReceiver.LastActions, Is.EqualTo(InputActionMask.None));
                Assert.That(inputSystem.BindingCount, Is.EqualTo(1));
            }
        }

        /// <summary>验证同一仲裁层级不能注册语义不明确的重叠独占动作。</summary>
        [Test]
        public void SameTierExclusiveOverlap_IsRejected()
        {
            FakeInputSource source = new FakeInputSource("Test", Vector2.zero, false, false);
            using (InputSystem inputSystem = new InputSystem(source))
            {
                inputSystem.AcquireControl(source.SourceId, new RecordingReceiver(), InputActionMask.Move, InputContexts.Gameplay);
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => inputSystem.AcquireControl(source.SourceId, new RecordingReceiver(), InputActionMask.Move | InputActionMask.Attack, InputContexts.Gameplay));
                StringAssert.Contains(nameof(InputActionMask.Move), exception.Message);
            }
        }

        /// <summary>验证 Shared 模式允许多个接收者在同一优先级获得同一动作。</summary>
        [Test]
        public void SharedBinding_DeliversTheSameActionToMultipleReceivers()
        {
            FakeInputSource source = new FakeInputSource("Test", Vector2.up, false, false);
            RecordingReceiver first = new RecordingReceiver();
            RecordingReceiver second = new RecordingReceiver();
            using (InputSystem inputSystem = new InputSystem(source))
            {
                inputSystem.AcquireControl(source.SourceId, first, InputActionMask.Move, InputContexts.Gameplay, 0, InputDeliveryMode.Shared);
                inputSystem.AcquireControl(source.SourceId, second, InputActionMask.Move, InputContexts.Gameplay, 0, InputDeliveryMode.Shared);
                inputSystem.BeforeEntityUpdate(0.016f);
                Assert.That(first.LastActions, Is.EqualTo(InputActionMask.Move));
                Assert.That(second.LastActions, Is.EqualTo(InputActionMask.Move));
            }
        }

        /// <summary>验证 Observer 能看到输入但不会阻止独占接收者获得相同动作。</summary>
        [Test]
        public void ObserverBinding_DoesNotCompeteWithExclusiveBinding()
        {
            FakeInputSource source = new FakeInputSource("Test", Vector2.zero, true, false);
            RecordingReceiver owner = new RecordingReceiver();
            RecordingReceiver observer = new RecordingReceiver();
            using (InputSystem inputSystem = new InputSystem(source))
            {
                inputSystem.AcquireControl(source.SourceId, owner, InputActionMask.Attack, InputContexts.Gameplay);
                inputSystem.AcquireControl(source.SourceId, observer, InputActionMask.Attack, InputContexts.Debug, 0, InputDeliveryMode.Observe);
                inputSystem.BeforeEntityUpdate(0.016f);
                Assert.That(owner.LastActions, Is.EqualTo(InputActionMask.Attack));
                Assert.That(observer.LastActions, Is.EqualTo(InputActionMask.Attack));
            }
        }

        /// <summary>验证屏幕虚拟设备会把摇杆和按钮完整转换为按下、持续与释放状态，而不是单帧点击脉冲。</summary>
        [Test]
        public void UnityInputActionSource_VirtualControlsPreserveMoveAndButtonLifecycle()
        {
            PrometheusVirtualInputDevice.EnsureRegistered();
            InputDevice device = UnityInputSystem.AddDevice(PrometheusVirtualInputDevice.LayoutName);
            UnityInputActionSource source = new UnityInputActionSource();
            try
            {
                PrometheusVirtualInputState pressedState = new PrometheusVirtualInputState { move = Vector2.right };
                pressedState.SetButton(PrometheusVirtualInputDevice.AttackBit, true);
                pressedState.SetButton(PrometheusVirtualInputDevice.OpenBagBit, true);
                UnityInputSystem.QueueStateEvent(device, pressedState);
                UnityInputSystem.Update();
                InputFrame pressedFrame = source.Sample(1);
                Assert.That(pressedFrame.Move, Is.EqualTo(Vector2.right));
                Assert.That(pressedFrame.Attack.PressedThisFrame, Is.True);
                Assert.That(pressedFrame.Attack.Held, Is.True);
                Assert.That(pressedFrame.OpenBag.PressedThisFrame, Is.True);
                UnityInputSystem.Update();
                InputFrame heldFrame = source.Sample(2);
                Assert.That(heldFrame.Attack.PressedThisFrame, Is.False);
                Assert.That(heldFrame.Attack.Held, Is.True);
                Assert.That(heldFrame.Attack.ReleasedThisFrame, Is.False);
                UnityInputSystem.QueueStateEvent(device, default(PrometheusVirtualInputState));
                UnityInputSystem.Update();
                InputFrame releasedFrame = source.Sample(3);
                Assert.That(releasedFrame.Move, Is.EqualTo(Vector2.zero));
                Assert.That(releasedFrame.Attack.Held, Is.False);
                Assert.That(releasedFrame.Attack.ReleasedThisFrame, Is.True);
                Assert.That(releasedFrame.OpenBag.ReleasedThisFrame, Is.True);
            }
            finally
            {
                source.Dispose();
                UnityInputSystem.RemoveDevice(device);
            }
        }

        /// <summary>验证主键盘数字键一二三使用真实控件路径，并分别产生对应小队槽位的按下沿。</summary>
        [Test]
        public void UnityInputActionSource_NumberKeysSelectCorrespondingTeamSlots()
        {
            Keyboard keyboard = UnityInputSystem.AddDevice<Keyboard>();
            UnityInputActionSource source = new UnityInputActionSource(() => false);
            try
            {
                Press(keyboard.digit1Key);
                InputFrame firstFrame = source.Sample(1);
                Assert.That(firstFrame.SelectTeamMember1.PressedThisFrame, Is.True);
                Assert.That(firstFrame.SelectTeamMember2.PressedThisFrame, Is.False);
                Assert.That(firstFrame.SelectTeamMember3.PressedThisFrame, Is.False);
                Release(keyboard.digit1Key);
                source.Sample(2);
                Press(keyboard.digit2Key);
                InputFrame secondFrame = source.Sample(3);
                Assert.That(secondFrame.SelectTeamMember1.PressedThisFrame, Is.False);
                Assert.That(secondFrame.SelectTeamMember2.PressedThisFrame, Is.True);
                Assert.That(secondFrame.SelectTeamMember3.PressedThisFrame, Is.False);
                Release(keyboard.digit2Key);
                source.Sample(4);
                Press(keyboard.digit3Key);
                InputFrame thirdFrame = source.Sample(5);
                Assert.That(thirdFrame.SelectTeamMember1.PressedThisFrame, Is.False);
                Assert.That(thirdFrame.SelectTeamMember2.PressedThisFrame, Is.False);
                Assert.That(thirdFrame.SelectTeamMember3.PressedThisFrame, Is.True);
            }
            finally
            {
                source.Dispose();
                UnityInputSystem.RemoveDevice(keyboard);
            }
        }

        /// <summary>验证一次被 UI 命中的鼠标操作在松开前不会触发攻击，而对应虚拟按钮动作仍只产生自身语义。</summary>
        [Test]
        public void UnityInputActionSource_UiPointerPressIsConsumedWithoutLeakingAttack()
        {
            Mouse mouse = UnityInputSystem.AddDevice<Mouse>();
            PrometheusVirtualInputDevice.EnsureRegistered();
            InputDevice virtualDevice = UnityInputSystem.AddDevice(PrometheusVirtualInputDevice.LayoutName);
            bool isPointerOverUi = true;
            UnityInputActionSource source = new UnityInputActionSource(() => isPointerOverUi);
            try
            {
                PrometheusVirtualInputState bagState = default;
                bagState.SetButton(PrometheusVirtualInputDevice.OpenBagBit, true);
                Press(mouse.leftButton, queueEventOnly: true);
                UnityInputSystem.QueueStateEvent(virtualDevice, bagState);
                UnityInputSystem.Update();
                InputFrame uiPressedFrame = source.Sample(1);
                Assert.That(uiPressedFrame.OpenBag.PressedThisFrame, Is.True);
                Assert.That(uiPressedFrame.Attack.IsActive, Is.False);
                isPointerOverUi = false;
                UnityInputSystem.Update();
                InputFrame uiHeldFrame = source.Sample(2);
                Assert.That(uiHeldFrame.Attack.IsActive, Is.False);
                Release(mouse.leftButton, queueEventOnly: true);
                UnityInputSystem.QueueStateEvent(virtualDevice, default(PrometheusVirtualInputState));
                UnityInputSystem.Update();
                InputFrame uiReleasedFrame = source.Sample(3);
                Assert.That(uiReleasedFrame.Attack.IsActive, Is.False);
                Press(mouse.leftButton);
                InputFrame gameplayPressedFrame = source.Sample(4);
                Assert.That(gameplayPressedFrame.Attack.PressedThisFrame, Is.True);
                Assert.That(gameplayPressedFrame.Attack.Held, Is.True);
            }
            finally
            {
                source.Dispose();
                UnityInputSystem.RemoveDevice(virtualDevice);
                UnityInputSystem.RemoveDevice(mouse);
            }
        }

        /// <summary>验证点击屏幕攻击按钮时，鼠标指针分支被 UI 消费后仍只保留一次虚拟攻击语义。</summary>
        [Test]
        public void UnityInputActionSource_OnScreenAttackProducesSingleMergedAttackState()
        {
            Mouse mouse = UnityInputSystem.AddDevice<Mouse>();
            PrometheusVirtualInputDevice.EnsureRegistered();
            InputDevice virtualDevice = UnityInputSystem.AddDevice(PrometheusVirtualInputDevice.LayoutName);
            UnityInputActionSource source = new UnityInputActionSource(() => true);
            try
            {
                PrometheusVirtualInputState attackState = default;
                attackState.SetButton(PrometheusVirtualInputDevice.AttackBit, true);
                Press(mouse.leftButton, queueEventOnly: true);
                UnityInputSystem.QueueStateEvent(virtualDevice, attackState);
                UnityInputSystem.Update();
                InputFrame frame = source.Sample(1);
                Assert.That(frame.Attack.PressedThisFrame, Is.True);
                Assert.That(frame.Attack.Held, Is.True);
                Assert.That(frame.Attack.ReleasedThisFrame, Is.False);
            }
            finally
            {
                source.Dispose();
                UnityInputSystem.RemoveDevice(virtualDevice);
                UnityInputSystem.RemoveDevice(mouse);
            }
        }

        /// <summary>验证 HUD Prefab 的摇杆和全部按钮都只绑定一个正确的屏幕 Input System 控件。</summary>
        [Test]
        public void HudPanelPrefab_UsesOnScreenControlsForEveryInteractiveBinding()
        {
            const string prefabPath = "Assets/BundleResources/UI/Hud/Prefabs/HudPanel.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);
            UIComponentBinder binder = prefab.GetComponent<UIComponentBinder>();
            Assert.That(binder, Is.Not.Null);
            string[] expectedControls = { PrometheusVirtualInputDevice.OpenLotteryControl, PrometheusVirtualInputDevice.MoveControl, PrometheusVirtualInputDevice.UltimateControl, PrometheusVirtualInputDevice.OpenMiniMapControl, PrometheusVirtualInputDevice.OpenQuestControl, PrometheusVirtualInputDevice.OpenMenuControl, PrometheusVirtualInputDevice.JumpControl, PrometheusVirtualInputDevice.AttackControl, PrometheusVirtualInputDevice.DodgeControl, PrometheusVirtualInputDevice.SkillControl, PrometheusVirtualInputDevice.ToggleWalkControl, PrometheusVirtualInputDevice.ToggleSprintControl, PrometheusVirtualInputDevice.OpenGuideControl, PrometheusVirtualInputDevice.OpenEventControl, PrometheusVirtualInputDevice.OpenCharacterControl, PrometheusVirtualInputDevice.OpenBagControl };
            for (int index = 0; index < expectedControls.Length; index++)
            {
                Button button = binder.Bindings[index].Component as Button;
                Assert.That(button, Is.Not.Null, $"HUD binding {index} must reference a Button.");
                Assert.That(button.GetComponents<OnScreenControl>().Length, Is.EqualTo(1), $"HUD binding {index} must contain exactly one OnScreenControl.");
                OnScreenControl control = button.GetComponent<OnScreenControl>();
                Assert.That(control.controlPath, Is.EqualTo(PrometheusVirtualInputDevice.BuildControlPath(expectedControls[index])));
            }
            OnScreenStick stick = (binder.Bindings[1].Component as Button).GetComponent<OnScreenStick>();
            Assert.That(stick, Is.Not.Null);
            Assert.That(stick.movementRange, Is.EqualTo(50f));
            Assert.That(stick.behaviour, Is.EqualTo(OnScreenStick.Behaviour.RelativePositionWithStaticOrigin));
        }

        /// <summary>验证 InputComponent 会合并同帧输入，并能在下一输入帧开始前完整清除旧状态。</summary>
        [Test]
        public void InputComponent_MergesAndClearsPerFrameCommands()
        {
            InputComponent inputComponent = new InputComponent();
            InputFrame frame = CreateFrame(1, Vector2.right, true, true);
            inputComponent.ApplyInput(frame, InputActionMask.Move | InputActionMask.Attack);
            Assert.That(inputComponent.hasInputThisFrame, Is.True);
            Assert.That(inputComponent.moveDir, Is.EqualTo(Vector2.right));
            Assert.That(inputComponent.wasAtkPressedThisFrame, Is.True);
            Assert.That(inputComponent.wasAtkPressed, Is.True);
            inputComponent.ResetInput();
            Assert.That(inputComponent.hasInputThisFrame, Is.False);
            Assert.That(inputComponent.moveDir, Is.EqualTo(Vector2.zero));
            Assert.That(inputComponent.wasAtkPressedThisFrame, Is.False);
            Assert.That(inputComponent.wasAtkPressed, Is.False);
        }

        /// <summary>创建测试使用的最小输入快照。</summary>
        private static InputFrame CreateFrame(long frameId, Vector2 move, bool attackPressedThisFrame, bool attackHeld)
        {
            InputButtonState attack = new InputButtonState(attackPressedThisFrame, attackHeld, false);
            return new InputFrame(frameId, move, move, attack, default, default, default, default, default, default, default, default, default, default, default, default);
        }

        /// <summary>提供可计数且不依赖 UnityEngine.Input 的确定性输入源。</summary>
        private sealed class FakeInputSource : IInputSource
        {
            private readonly bool attackPressedThisFrame;
            private readonly bool attackHeld;

            /// <summary>创建具有固定移动和攻击状态的测试输入源。</summary>
            public FakeInputSource(string sourceId, Vector2 move, bool attackPressedThisFrame, bool attackHeld)
            {
                SourceId = sourceId;
                Move = move;
                this.attackPressedThisFrame = attackPressedThisFrame;
                this.attackHeld = attackHeld;
            }

            /// <inheritdoc />
            public string SourceId { get; }

            /// <summary>获取固定的移动输入。</summary>
            public Vector2 Move { get; }

            /// <summary>获取累计采样次数。</summary>
            public int SampleCount { get; private set; }

            /// <summary>获取输入源是否已经随 InputSystem 释放。</summary>
            public bool IsDisposed { get; private set; }

            /// <inheritdoc />
            public InputFrame Sample(long frameId)
            {
                SampleCount++;
                return CreateFrame(frameId, Move, attackPressedThisFrame, attackHeld);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        /// <summary>记录 InputSystem 实际分发结果的普通非 Entity 接收者。</summary>
        private sealed class RecordingReceiver : IInputReceiver
        {
            /// <inheritdoc />
            public bool IsAlive { get; set; } = true;

            /// <summary>获取当前帧累计收到的动作。</summary>
            public InputActionMask LastActions { get; private set; }

            /// <summary>获取最近一次分发的完整输入快照，供按钮状态断言使用。</summary>
            public InputFrame LastFrame { get; private set; }

            /// <summary>获取当前帧收到的移动输入。</summary>
            public Vector2 LastMove { get; private set; }

            /// <summary>获取当前帧收到的攻击按下沿。</summary>
            public bool AttackPressedThisFrame { get; private set; }

            /// <summary>获取当前帧收到的攻击持续状态。</summary>
            public bool AttackHeld { get; private set; }

            /// <inheritdoc />
            public void ResetInput()
            {
                LastActions = InputActionMask.None;
                LastFrame = default;
                LastMove = Vector2.zero;
                AttackPressedThisFrame = false;
                AttackHeld = false;
            }

            /// <inheritdoc />
            public void ReceiveInput(in InputFrame frame, InputActionMask actions)
            {
                LastActions |= actions;
                LastFrame = frame;
                if ((actions & InputActionMask.Move) != 0) LastMove = Vector2.ClampMagnitude(LastMove + frame.Move, 1f);
                if ((actions & InputActionMask.Attack) != 0)
                {
                    AttackPressedThisFrame |= frame.Attack.PressedThisFrame;
                    AttackHeld |= frame.Attack.Held;
                }
            }
        }
    }
}
