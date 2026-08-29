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
    /// <summary>保存屏幕摇杆写入 Unity Input System 的二维移动状态。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PrometheusVirtualInputState : IInputStateTypeInfo
    {
        /// <summary>定义屏幕虚拟设备状态的唯一四字符格式。</summary>
        public FourCC format => new FourCC('P', 'V', 'I', 'S');

        /// <summary>保存屏幕摇杆输出的二维移动值。</summary>
        [InputControl(name = PrometheusVirtualInputDevice.MoveControl, layout = "Stick", format = "VEC2", displayName = "Move")]
        public Vector2 move;

    }

    /// <summary>仅为 OnScreenStick 提供 Unity Input System 虚拟设备布局；普通 UI Button 由 UIKit 点击回调处理。</summary>
    [InputControlLayout(stateType = typeof(PrometheusVirtualInputState), displayName = "Prometheus Virtual Input", canRunInBackground = true)]
    public sealed class PrometheusVirtualInputDevice : InputDevice
    {
        /// <summary>获取注册到 Unity Input System 的稳定布局名称。</summary>
        public const string LayoutName = "PrometheusVirtualInput";

        /// <summary>获取屏幕摇杆对应的控件名称。</summary>
        public const string MoveControl = "move";

        /// <summary>获取用于 OnScreenStick 的完整控件路径。</summary>
        public static string MovePath => $"<{LayoutName}>/{MoveControl}";

        /// <summary>确保布局在 Prefab 中的 OnScreenControl 启用前已经完成注册。</summary>
        public static void EnsureRegistered()
        {
            if (UnityInputSystem.ListLayouts().Contains(LayoutName)) return;
            UnityInputSystem.RegisterLayout<PrometheusVirtualInputDevice>(LayoutName);
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
