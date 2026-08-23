using System;
using UnityEngine;
using Xuan.Prometheus.Input;

namespace Xuan.Prometheus
{
    /// <summary>定义 HUD 点击和快捷键共同调用的稳定界面命令，不携带具体 Unity Button 引用。</summary>
    public enum HudCommandType
    {
        OpenLottery,
        OpenMiniMap,
        OpenQuest,
        OpenMenu,
        OpenGuide,
        OpenEvent,
        OpenCharacter,
        OpenBag
    }

    /// <summary>独立接收 HUD 快捷键并执行界面命令，使 UIPanel 只负责显示、数据绑定和 Button 点击。</summary>
    public sealed class HudCommandSystem : XSystem, IInputReceiver
    {
        /// <summary>保存 HUD 快捷键的独占控制租约，并随单局系统生命周期统一释放。</summary>
        private ControlLease shortcutLease;

        /// <summary>标记系统已经释放，供输入系统剔除失效接收者。</summary>
        private bool isDisposed;

        /// <inheritdoc />
        public bool IsAlive => !isDisposed;

        /// <summary>取得集中式输入系统并永久监听当前单局的 HUD 快捷键。</summary>
        public override void AfterNew(IGameplayKit gameplayKit)
        {
            if (gameplayKit == null) throw new ArgumentNullException(nameof(gameplayKit));
            InputSystem inputSystem = gameplayKit.GetSystem<InputSystem>();
            shortcutLease = inputSystem.AcquireControl(inputSystem.DefaultSourceId, this, InputActionMask.HudCommands, InputContexts.Gameplay);
        }

        /// <summary>执行点击和快捷键共用的 HUD 命令入口；具体界面完成后在对应分支调用 UIKit 打开面板。</summary>
        /// <param name="command">需要执行的稳定 HUD 命令。</param>
        public void Execute(HudCommandType command)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(HudCommandSystem));
            switch (command)
            {
                case HudCommandType.OpenLottery: Debug.Log($"[UIKit] {nameof(HudCommandSystem)} OpenLottery triggered."); break;
                case HudCommandType.OpenMiniMap: Debug.Log($"[UIKit] {nameof(HudCommandSystem)} OpenMiniMap triggered."); break;
                case HudCommandType.OpenQuest: Debug.Log($"[UIKit] {nameof(HudCommandSystem)} OpenQuest triggered."); break;
                case HudCommandType.OpenMenu: Debug.Log($"[UIKit] {nameof(HudCommandSystem)} OpenMenu triggered."); break;
                case HudCommandType.OpenGuide: Debug.Log($"[UIKit] {nameof(HudCommandSystem)} OpenGuide triggered."); break;
                case HudCommandType.OpenEvent: Debug.Log($"[UIKit] {nameof(HudCommandSystem)} OpenEvent triggered."); break;
                case HudCommandType.OpenCharacter: Debug.Log($"[UIKit] {nameof(HudCommandSystem)} OpenCharacter triggered."); break;
                case HudCommandType.OpenBag: Core.UI.OpenPanel<BagPanel>(); break;
                default: throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown HUD command.");
            }
        }

        /// <inheritdoc />
        public void ResetInput()
        {
        }

        /// <summary>把 InputAction 快捷键按下沿转换为稳定 HUD 命令，不访问任何 UIPanel 或 Button 实例。</summary>
        public void ReceiveInput(in InputFrame frame, InputActionMask actions)
        {
            if ((actions & InputActionMask.OpenLottery) != 0 && frame.OpenLottery.PressedThisFrame) Execute(HudCommandType.OpenLottery);
            if ((actions & InputActionMask.OpenMiniMap) != 0 && frame.OpenMiniMap.PressedThisFrame) Execute(HudCommandType.OpenMiniMap);
            if ((actions & InputActionMask.OpenQuest) != 0 && frame.OpenQuest.PressedThisFrame) Execute(HudCommandType.OpenQuest);
            if ((actions & InputActionMask.OpenMenu) != 0 && frame.OpenMenu.PressedThisFrame) Execute(HudCommandType.OpenMenu);
            if ((actions & InputActionMask.OpenGuide) != 0 && frame.OpenGuide.PressedThisFrame) Execute(HudCommandType.OpenGuide);
            if ((actions & InputActionMask.OpenEvent) != 0 && frame.OpenEvent.PressedThisFrame) Execute(HudCommandType.OpenEvent);
            if ((actions & InputActionMask.OpenCharacter) != 0 && frame.OpenCharacter.PressedThisFrame) Execute(HudCommandType.OpenCharacter);
            if ((actions & InputActionMask.OpenBag) != 0 && frame.OpenBag.PressedThisFrame) Execute(HudCommandType.OpenBag);
        }

        /// <summary>释放快捷键租约并使输入系统停止向当前命令系统分发动作。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            shortcutLease?.Dispose();
            shortcutLease = null;
            isDisposed = true;
        }
    }
}
