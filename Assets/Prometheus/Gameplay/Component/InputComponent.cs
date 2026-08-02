using UnityEngine;
using Xuan.Prometheus.Actor;

namespace Xuan.Prometheus.Component
{
    /// <summary>保存当前 Entity 从 PossessionSystem 获得的单帧兼容输入状态，使旧玩法 Logic 不需要直接依赖控制器实现。</summary>
    public class InputComponent : Component
    {
        /// <summary>当前帧是否包含任意连续输入、瞬时按钮或保持按钮。</summary>
        public bool hasInputThisFrame;

        /// <summary>当前帧的二维移动方向。</summary>
        public Vector2 moveDir;

        /// <summary>当前帧是否按下普通攻击。</summary>
        public bool wasAtkPressedThisFrame;

        /// <summary>当前帧普通攻击是否仍处于保持状态。</summary>
        public bool wasAtkPressed;

        /// <summary>当前帧是否按下主动技能。</summary>
        public bool wasSkillPressedThisFrame;

        /// <summary>当前帧是否按下终结技。</summary>
        public bool wasUltPressedThisFrame;

        /// <summary>当前帧是否按下闪避。</summary>
        public bool wasDodgePressedThisFrame;

        /// <summary>当前帧是否按下跳跃。</summary>
        public bool wasJumpPressedThisFrame;

        /// <summary>当前帧是否按下特殊攻击。</summary>
        public bool wasSpecialAtkPressedThisFrame;

        /// <summary>当前帧是否请求切换冲刺模式。</summary>
        public bool wasToggleSprintPressedThisFrame;

        /// <summary>当前帧是否请求切换行走模式。</summary>
        public bool wasToggleWalkPressedThisFrame;

        /// <summary>清除只允许存活一个 Entity 更新帧的瞬时按钮，防止缺少新控制帧时重复消费上帧命令。</summary>
        public void ClearTransientButtons()
        {
            wasAtkPressedThisFrame = false;
            wasSkillPressedThisFrame = false;
            wasUltPressedThisFrame = false;
            wasDodgePressedThisFrame = false;
            wasJumpPressedThisFrame = false;
            wasSpecialAtkPressedThisFrame = false;
            wasToggleSprintPressedThisFrame = false;
            wasToggleWalkPressedThisFrame = false;
        }

        /// <summary>清除全部单帧输入状态，包括连续移动、保持攻击和瞬时按钮，用于本帧没有合法 ControlFrame 的情况。</summary>
        public void ClearFrameInput()
        {
            ClearTransientButtons();
            hasInputThisFrame = false;
            moveDir = Vector2.zero;
            wasAtkPressed = false;
        }

        /// <summary>将经过 PossessionSystem 分领域仲裁的 ControlFrame 映射到旧玩法 Logic 使用的兼容字段。</summary>
        /// <param name="frame">当前 Entity 在本帧获得的最终控制帧。</param>
        public void ApplyControlFrame(ControlFrame frame)
        {
            ClearFrameInput();
            hasInputThisFrame = frame.HasAnyInput;
            moveDir = frame.Move;
            wasAtkPressedThisFrame = HasButton(frame.PressedButtons, ControlButton.Attack);
            wasAtkPressed = HasButton(frame.HeldButtons, ControlButton.Attack);
            wasSkillPressedThisFrame = HasButton(frame.PressedButtons, ControlButton.Skill);
            wasUltPressedThisFrame = HasButton(frame.PressedButtons, ControlButton.Ultimate);
            wasDodgePressedThisFrame = HasButton(frame.PressedButtons, ControlButton.Dodge);
            wasJumpPressedThisFrame = HasButton(frame.PressedButtons, ControlButton.Jump);
            wasSpecialAtkPressedThisFrame = HasButton(frame.PressedButtons, ControlButton.SpecialAttack);
            wasToggleSprintPressedThisFrame = HasButton(frame.PressedButtons, ControlButton.SprintToggle);
            wasToggleWalkPressedThisFrame = HasButton(frame.PressedButtons, ControlButton.WalkToggle);
        }

        /// <summary>判断按钮位集合是否包含指定按钮。</summary>
        private static bool HasButton(ControlButton buttons, ControlButton button)
        {
            return (buttons & button) != 0;
        }
    }
}
