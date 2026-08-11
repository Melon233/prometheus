using UnityEngine;
using UnityInput = UnityEngine.Input;

namespace Xuan.Prometheus.Input
{
    /// <summary>封装项目当前使用的 Unity Legacy Input，并确保所有键位只在 InputSystem 中集中采样。</summary>
    public sealed class UnityLegacyInputSource : IInputSource
    {
        /// <summary>当前本地键鼠输入源的稳定标识。</summary>
        public const string LocalSourceId = "Local";

        /// <inheritdoc />
        public string SourceId => LocalSourceId;

        /// <inheritdoc />
        public InputFrame Sample(long frameId)
        {
            Vector2 move = new Vector2(UnityInput.GetAxisRaw("Horizontal"), UnityInput.GetAxisRaw("Vertical"));
            InputButtonState attack = ReadMouseButton(0);
            InputButtonState skill = ReadKey(KeyCode.E);
            InputButtonState ultimate = ReadKey(KeyCode.R);
            InputButtonState dodge = ReadMouseButton(1);
            InputButtonState jump = ReadKey(KeyCode.Space);
            InputButtonState specialAttack = default;
            InputButtonState toggleSprint = ReadKey(KeyCode.LeftShift);
            InputButtonState toggleWalk = ReadKey(KeyCode.LeftControl);
            InputButtonState submit = Merge(ReadKey(KeyCode.Return), ReadKey(KeyCode.KeypadEnter));
            InputButtonState cancel = ReadKey(KeyCode.Escape);
            InputButtonState selectTeamMember1 = ReadKey(KeyCode.Alpha1);
            InputButtonState selectTeamMember2 = ReadKey(KeyCode.Alpha2);
            InputButtonState selectTeamMember3 = ReadKey(KeyCode.Alpha3);
            return new InputFrame(frameId, move, move, attack, skill, ultimate, dodge, jump, specialAttack, toggleSprint, toggleWalk, submit, cancel, selectTeamMember1, selectTeamMember2, selectTeamMember3);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <summary>读取指定键盘按键的完整帧状态。</summary>
        private static InputButtonState ReadKey(KeyCode keyCode)
        {
            return new InputButtonState(UnityInput.GetKeyDown(keyCode), UnityInput.GetKey(keyCode), UnityInput.GetKeyUp(keyCode));
        }

        /// <summary>读取指定鼠标按键的完整帧状态。</summary>
        private static InputButtonState ReadMouseButton(int button)
        {
            return new InputButtonState(UnityInput.GetMouseButtonDown(button), UnityInput.GetMouseButton(button), UnityInput.GetMouseButtonUp(button));
        }

        /// <summary>把多个物理按键合并为同一个语义动作。</summary>
        private static InputButtonState Merge(InputButtonState left, InputButtonState right)
        {
            return new InputButtonState(left.PressedThisFrame || right.PressedThisFrame, left.Held || right.Held, left.ReleasedThisFrame || right.ReleasedThisFrame);
        }
    }
}
