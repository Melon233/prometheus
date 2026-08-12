using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace Xuan.Prometheus.Input
{
    /// <summary>保存屏幕摇杆和 HUD 按钮写入 Unity Input System 的紧凑设备状态。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PrometheusVirtualInputState : IInputStateTypeInfo
    {
        /// <summary>定义屏幕虚拟设备状态的唯一四字符格式。</summary>
        public FourCC format => new FourCC('P', 'V', 'I', 'S');

        /// <summary>保存屏幕摇杆输出的二维移动值。</summary>
        [InputControl(name = PrometheusVirtualInputDevice.MoveControl, layout = "Stick", format = "VEC2", displayName = "Move")]
        public Vector2 move;

        /// <summary>保存全部屏幕按钮状态，每一位对应一个独立 Input Action 控件。</summary>
        [InputControl(name = PrometheusVirtualInputDevice.AttackControl, layout = "Button", bit = PrometheusVirtualInputDevice.AttackBit, displayName = "Attack")]
        [InputControl(name = PrometheusVirtualInputDevice.SkillControl, layout = "Button", bit = PrometheusVirtualInputDevice.SkillBit, displayName = "Skill")]
        [InputControl(name = PrometheusVirtualInputDevice.UltimateControl, layout = "Button", bit = PrometheusVirtualInputDevice.UltimateBit, displayName = "Ultimate")]
        [InputControl(name = PrometheusVirtualInputDevice.DodgeControl, layout = "Button", bit = PrometheusVirtualInputDevice.DodgeBit, displayName = "Dodge")]
        [InputControl(name = PrometheusVirtualInputDevice.JumpControl, layout = "Button", bit = PrometheusVirtualInputDevice.JumpBit, displayName = "Jump")]
        [InputControl(name = PrometheusVirtualInputDevice.ToggleSprintControl, layout = "Button", bit = PrometheusVirtualInputDevice.ToggleSprintBit, displayName = "Toggle Sprint")]
        [InputControl(name = PrometheusVirtualInputDevice.ToggleWalkControl, layout = "Button", bit = PrometheusVirtualInputDevice.ToggleWalkBit, displayName = "Toggle Walk")]
        [InputControl(name = PrometheusVirtualInputDevice.OpenLotteryControl, layout = "Button", bit = PrometheusVirtualInputDevice.OpenLotteryBit, displayName = "Open Lottery")]
        [InputControl(name = PrometheusVirtualInputDevice.OpenMiniMapControl, layout = "Button", bit = PrometheusVirtualInputDevice.OpenMiniMapBit, displayName = "Open Mini Map")]
        [InputControl(name = PrometheusVirtualInputDevice.OpenQuestControl, layout = "Button", bit = PrometheusVirtualInputDevice.OpenQuestBit, displayName = "Open Quest")]
        [InputControl(name = PrometheusVirtualInputDevice.OpenMenuControl, layout = "Button", bit = PrometheusVirtualInputDevice.OpenMenuBit, displayName = "Open Menu")]
        [InputControl(name = PrometheusVirtualInputDevice.OpenGuideControl, layout = "Button", bit = PrometheusVirtualInputDevice.OpenGuideBit, displayName = "Open Guide")]
        [InputControl(name = PrometheusVirtualInputDevice.OpenEventControl, layout = "Button", bit = PrometheusVirtualInputDevice.OpenEventBit, displayName = "Open Event")]
        [InputControl(name = PrometheusVirtualInputDevice.OpenCharacterControl, layout = "Button", bit = PrometheusVirtualInputDevice.OpenCharacterBit, displayName = "Open Character")]
        [InputControl(name = PrometheusVirtualInputDevice.OpenBagControl, layout = "Button", bit = PrometheusVirtualInputDevice.OpenBagBit, displayName = "Open Bag")]
        public ushort buttons;

        /// <summary>按位设置一个测试或外部注入按钮，同时保留其余按钮状态。</summary>
        public void SetButton(int bit, bool isPressed)
        {
            ushort mask = (ushort)(1 << bit);
            buttons = isPressed ? (ushort)(buttons | mask) : (ushort)(buttons & ~mask);
        }
    }

    /// <summary>为 OnScreenStick 和 OnScreenButton 提供共享的 Unity Input System 虚拟设备布局。</summary>
    [InputControlLayout(stateType = typeof(PrometheusVirtualInputState), displayName = "Prometheus Virtual Input", canRunInBackground = true)]
    public sealed class PrometheusVirtualInputDevice : InputDevice
    {
        /// <summary>获取注册到 Unity Input System 的稳定布局名称。</summary>
        public const string LayoutName = "PrometheusVirtualInput";

        /// <summary>获取屏幕摇杆对应的控件名称。</summary>
        public const string MoveControl = "move";

        /// <summary>获取普通攻击屏幕按钮对应的控件名称。</summary>
        public const string AttackControl = "attack";

        /// <summary>获取技能屏幕按钮对应的控件名称。</summary>
        public const string SkillControl = "skill";

        /// <summary>获取终结技屏幕按钮对应的控件名称。</summary>
        public const string UltimateControl = "ultimate";

        /// <summary>获取闪避屏幕按钮对应的控件名称。</summary>
        public const string DodgeControl = "dodge";

        /// <summary>获取跳跃屏幕按钮对应的控件名称。</summary>
        public const string JumpControl = "jump";

        /// <summary>获取冲刺切换屏幕按钮对应的控件名称。</summary>
        public const string ToggleSprintControl = "toggleSprint";

        /// <summary>获取行走切换屏幕按钮对应的控件名称。</summary>
        public const string ToggleWalkControl = "toggleWalk";

        /// <summary>获取打开抽奖界面屏幕按钮对应的控件名称。</summary>
        public const string OpenLotteryControl = "openLottery";

        /// <summary>获取打开小地图屏幕按钮对应的控件名称。</summary>
        public const string OpenMiniMapControl = "openMiniMap";

        /// <summary>获取打开任务界面屏幕按钮对应的控件名称。</summary>
        public const string OpenQuestControl = "openQuest";

        /// <summary>获取打开主菜单屏幕按钮对应的控件名称。</summary>
        public const string OpenMenuControl = "openMenu";

        /// <summary>获取打开引导界面屏幕按钮对应的控件名称。</summary>
        public const string OpenGuideControl = "openGuide";

        /// <summary>获取打开活动界面屏幕按钮对应的控件名称。</summary>
        public const string OpenEventControl = "openEvent";

        /// <summary>获取打开角色界面屏幕按钮对应的控件名称。</summary>
        public const string OpenCharacterControl = "openCharacter";

        /// <summary>获取打开背包界面屏幕按钮对应的控件名称。</summary>
        public const string OpenBagControl = "openBag";

        /// <summary>获取普通攻击按钮在状态位域中的索引。</summary>
        public const int AttackBit = 0;

        /// <summary>获取技能按钮在状态位域中的索引。</summary>
        public const int SkillBit = 1;

        /// <summary>获取终结技按钮在状态位域中的索引。</summary>
        public const int UltimateBit = 2;

        /// <summary>获取闪避按钮在状态位域中的索引。</summary>
        public const int DodgeBit = 3;

        /// <summary>获取跳跃按钮在状态位域中的索引。</summary>
        public const int JumpBit = 4;

        /// <summary>获取冲刺切换按钮在状态位域中的索引。</summary>
        public const int ToggleSprintBit = 5;

        /// <summary>获取行走切换按钮在状态位域中的索引。</summary>
        public const int ToggleWalkBit = 6;

        /// <summary>获取抽奖按钮在状态位域中的索引。</summary>
        public const int OpenLotteryBit = 7;

        /// <summary>获取小地图按钮在状态位域中的索引。</summary>
        public const int OpenMiniMapBit = 8;

        /// <summary>获取任务按钮在状态位域中的索引。</summary>
        public const int OpenQuestBit = 9;

        /// <summary>获取菜单按钮在状态位域中的索引。</summary>
        public const int OpenMenuBit = 10;

        /// <summary>获取引导按钮在状态位域中的索引。</summary>
        public const int OpenGuideBit = 11;

        /// <summary>获取活动按钮在状态位域中的索引。</summary>
        public const int OpenEventBit = 12;

        /// <summary>获取角色按钮在状态位域中的索引。</summary>
        public const int OpenCharacterBit = 13;

        /// <summary>获取背包按钮在状态位域中的索引。</summary>
        public const int OpenBagBit = 14;

        /// <summary>获取用于 OnScreenStick 的完整控件路径。</summary>
        public static string MovePath => BuildControlPath(MoveControl);

        /// <summary>确保布局在 Prefab 中的 OnScreenControl 启用前已经完成注册。</summary>
        public static void EnsureRegistered()
        {
            if (UnityInputSystem.ListLayouts().Contains(LayoutName)) return;
            UnityInputSystem.RegisterLayout<PrometheusVirtualInputDevice>(LayoutName);
        }

        /// <summary>把稳定控件名称转换为 OnScreenControl 和 InputAction 共用的完整路径。</summary>
        public static string BuildControlPath(string controlName)
        {
            return $"<{LayoutName}>/{controlName}";
        }

        /// <summary>在 Player 加载首个场景前注册屏幕虚拟设备布局。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterBeforeSceneLoad()
        {
            EnsureRegistered();
        }

        /// <summary>获取虚拟移动摇杆控件，供诊断和测试直接读取。</summary>
        public StickControl Move { get; private set; }

        /// <summary>在设备布局完成后缓存强类型移动控件。</summary>
        protected override void FinishSetup()
        {
            base.FinishSetup();
            Move = GetChildControl<StickControl>(MoveControl);
        }
    }
}
