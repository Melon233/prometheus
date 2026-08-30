using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Xuan.Prometheus.Input
{
    /// <summary>集中定义键鼠、手柄快捷键和屏幕摇杆绑定，并把 Unity Input Action 状态转换为项目输入快照。</summary>
    public sealed class UnityInputActionSource : IInputSource
    {
        /// <summary>获取当前本地混合设备输入源的稳定标识。</summary>
        public const string LocalSourceId = "Local";

        /// <summary>获取运行时 Action Map 的稳定名称。</summary>
        public const string GameplayMapName = "Gameplay";

        private readonly InputActionAsset actionAsset;
        private readonly InputActionMap gameplayMap;
        private readonly InputAction move;
        private readonly InputAction navigate;
        private readonly InputAction attack;
        private readonly InputAction pointerAttack;
        private readonly InputAction skill;
        private readonly InputAction ultimate;
        private readonly InputAction dodge;
        private readonly InputAction jump;
        private readonly InputAction toggleSprint;
        private readonly InputAction toggleWalk;
        private readonly InputAction submit;
        private readonly InputAction cancel;
        private readonly InputAction selectTeamMember1;
        private readonly InputAction selectTeamMember2;
        private readonly InputAction selectTeamMember3;
        private readonly InputAction openLottery;
        private readonly InputAction openMiniMap;
        private readonly InputAction openQuest;
        private readonly InputAction openMenu;
        private readonly InputAction openGuide;
        private readonly InputAction openEvent;
        private readonly InputAction openCharacter;
        private readonly InputAction openBag;
        private readonly Func<bool> isPointerOverUi;
        private bool isPointerPressConsumedByUi;
        private bool isDisposed;

        /// <summary>创建并启用一份由当前输入源独占生命周期的运行时 InputActionAsset。</summary>
        public UnityInputActionSource() : this(IsPointerOverCurrentUi)
        {
        }

        /// <summary>创建输入源并注入指针是否位于 UI 上方的判断，保证一次鼠标操作只被 UI 或玩法消费。</summary>
        /// <param name="isPointerOverUi">鼠标左键按下时判断当前指针是否已命中 UI 的函数。</param>
        public UnityInputActionSource(Func<bool> isPointerOverUi)
        {
            this.isPointerOverUi = isPointerOverUi ?? throw new ArgumentNullException(nameof(isPointerOverUi));
            PrometheusVirtualInputDevice.EnsureRegistered();
            actionAsset = ScriptableObject.CreateInstance<InputActionAsset>();
            actionAsset.name = "PrometheusRuntimeInputActions";
            gameplayMap = new InputActionMap(GameplayMapName);
            actionAsset.AddActionMap(gameplayMap);
            move = AddVectorAction("Move");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            move.AddBinding(PrometheusVirtualInputDevice.MovePath);
            navigate = AddVectorAction("Navigate");
            navigate.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            navigate.AddBinding("<Gamepad>/dpad");
            attack = AddButtonAction("Attack", null, "<Gamepad>/buttonSouth");
            pointerAttack = AddButtonAction("PointerAttack", "<Mouse>/leftButton");
            skill = AddButtonAction("Skill", "<Keyboard>/e", "<Gamepad>/buttonWest");
            ultimate = AddButtonAction("Ultimate", "<Keyboard>/r", "<Gamepad>/rightShoulder");
            dodge = AddButtonAction("Dodge", "<Mouse>/rightButton", "<Gamepad>/buttonEast");
            jump = AddButtonAction("Jump", "<Keyboard>/space", "<Gamepad>/buttonNorth");
            toggleSprint = AddButtonAction("ToggleSprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            toggleWalk = AddButtonAction("ToggleWalk", "<Keyboard>/leftCtrl", "<Gamepad>/dpad/down");
            submit = AddButtonAction("Submit", "<Keyboard>/enter", "<Gamepad>/buttonSouth");
            submit.AddBinding("<Keyboard>/numpadEnter");
            cancel = AddButtonAction("Cancel", "<Keyboard>/escape", "<Gamepad>/buttonEast");
            selectTeamMember1 = AddButtonAction("SelectTeamMember1", "<Keyboard>/1");
            selectTeamMember2 = AddButtonAction("SelectTeamMember2", "<Keyboard>/2");
            selectTeamMember3 = AddButtonAction("SelectTeamMember3", "<Keyboard>/3");
            openLottery = AddButtonAction("OpenLottery", "<Keyboard>/l");
            openMiniMap = AddButtonAction("OpenMiniMap", "<Keyboard>/m");
            openQuest = AddButtonAction("OpenQuest", "<Keyboard>/j");
            openMenu = AddButtonAction("OpenMenu", "<Keyboard>/p");
            openGuide = AddButtonAction("OpenGuide", "<Keyboard>/g");
            openEvent = AddButtonAction("OpenEvent", "<Keyboard>/f5");
            openCharacter = AddButtonAction("OpenCharacter", "<Keyboard>/c");
            openBag = AddButtonAction("OpenBag", "<Keyboard>/b");
            gameplayMap.Enable();
        }

        /// <inheritdoc />
        public string SourceId => LocalSourceId;

        /// <summary>获取运行时 Action Asset，供按键提示、重绑定和诊断系统读取。</summary>
        public InputActionAsset Actions => actionAsset;

        /// <inheritdoc />
        public InputFrame Sample(long frameId)
        {
            InputButtonState specialAttack = default;
            return new InputFrame(frameId, move.ReadValue<Vector2>(), navigate.ReadValue<Vector2>(), ReadAttack(), ReadButton(skill), ReadButton(ultimate), ReadButton(dodge), ReadButton(jump), specialAttack, ReadButton(toggleSprint), ReadButton(toggleWalk), ReadButton(submit), ReadButton(cancel), ReadButton(selectTeamMember1), ReadButton(selectTeamMember2), ReadButton(selectTeamMember3), ReadButton(openLottery), ReadButton(openMiniMap), ReadButton(openQuest), ReadButton(openMenu), ReadButton(openGuide), ReadButton(openEvent), ReadButton(openCharacter), ReadButton(openBag));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (isDisposed) return;
            gameplayMap.Disable();
            if (Application.isPlaying) UnityEngine.Object.Destroy(actionAsset);
            else UnityEngine.Object.DestroyImmediate(actionAsset);
            isDisposed = true;
        }

        /// <summary>在当前 Action Map 中创建二维值动作。</summary>
        private InputAction AddVectorAction(string actionName)
        {
            return gameplayMap.AddAction(actionName, InputActionType.Value, expectedControlLayout: "Vector2");
        }

        /// <summary>创建快捷键按钮动作，并按需绑定键鼠和手柄；UI Button 点击不进入 InputAction。</summary>
        private InputAction AddButtonAction(string actionName, string primaryPath, string secondaryPath = null)
        {
            InputAction action = gameplayMap.AddAction(actionName, InputActionType.Button, expectedControlLayout: "Button");
            if (!string.IsNullOrEmpty(primaryPath)) action.AddBinding(primaryPath);
            if (!string.IsNullOrEmpty(secondaryPath)) action.AddBinding(secondaryPath);
            return action;
        }

        /// <summary>把 Unity Input Action 的按下、持续和释放查询合并为项目按钮状态。</summary>
        private static InputButtonState ReadButton(InputAction action)
        {
            return new InputButtonState(action.WasPressedThisFrame(), action.IsPressed(), action.WasReleasedThisFrame());
        }

        /// <summary>合并手柄攻击与未被 UI 消费的鼠标攻击，保证界面点击不会泄漏成玩法攻击。</summary>
        private InputButtonState ReadAttack()
        {
            return MergeButtonStates(ReadButton(attack), ReadPointerAttack());
        }

        /// <summary>在鼠标按下沿锁定本次操作的消费者，被 UI 消费后持续到松开都不会泄漏给玩法。</summary>
        private InputButtonState ReadPointerAttack()
        {
            bool pressedThisFrame = pointerAttack.WasPressedThisFrame();
            bool releasedThisFrame = pointerAttack.WasReleasedThisFrame();
            if (pressedThisFrame) isPointerPressConsumedByUi = isPointerOverUi() || !IsPointerInsideGameView();
            if (!isPointerPressConsumedByUi) return ReadButton(pointerAttack);
            if (releasedThisFrame || !pointerAttack.IsPressed()) isPointerPressConsumedByUi = false;
            return default;
        }

        /// <summary>把两个互斥设备来源的按钮边沿合并成单一语义动作。</summary>
        private static InputButtonState MergeButtonStates(InputButtonState left, InputButtonState right)
        {
            return new InputButtonState(left.PressedThisFrame || right.PressedThisFrame, left.Held || right.Held, left.ReleasedThisFrame || right.ReleasedThisFrame);
        }

        /// <summary>通过当前 EventSystem 判断鼠标指针是否命中 UI，场景没有事件系统时允许玩法接收点击。</summary>
        private static bool IsPointerOverCurrentUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        /// <summary>判断鼠标按下位置是否仍在运行中的 GameView 屏幕范围内，SceneView 或编辑器其他区域不应触发玩法攻击。</summary>
        private static bool IsPointerInsideGameView()
        {
            if (Mouse.current == null) return false;
            Vector2 position = Mouse.current.position.ReadValue();
            return position.x >= 0f && position.x <= Screen.width && position.y >= 0f && position.y <= Screen.height;
        }
    }
}
