using UnityEngine;
using Xuan.Prometheus.Input;

namespace Xuan.Prometheus.Component
{
    /// <summary>保存一个 Entity 在当前帧收到的控制命令；输入采样和控制权归 InputSystem 所有。</summary>
    public class InputComponent : Component
    {
        /// <summary>当前帧是否收到任何有效输入。</summary>
        public bool hasInputThisFrame;

        /// <summary>当前帧合并后的世界移动方向。</summary>
        public Vector2 moveDir;

        /// <summary>当前帧是否刚刚按下普通攻击。</summary>
        public bool wasAtkPressedThisFrame;

        /// <summary>普通攻击当前是否保持按住。</summary>
        public bool wasAtkPressed;

        /// <summary>当前帧是否刚刚按下技能。</summary>
        public bool wasSkillPressedThisFrame;

        /// <summary>当前帧是否刚刚按下终结技。</summary>
        public bool wasUltPressedThisFrame;

        /// <summary>当前帧是否刚刚按下闪避。</summary>
        public bool wasDodgePressedThisFrame;

        /// <summary>当前帧是否刚刚按下跳跃。</summary>
        public bool wasJumpPressedThisFrame;

        /// <summary>当前帧是否刚刚按下特殊攻击。</summary>
        public bool wasSpecialAtkPressedThisFrame;

        /// <summary>当前帧是否刚刚切换冲刺模式。</summary>
        public bool wasToggleSprintPressedThisFrame;

        /// <summary>当前帧是否刚刚切换行走模式。</summary>
        public bool wasToggleWalkPressedThisFrame;

        /// <summary>清除上一输入帧的方向、持续状态和所有瞬时按钮状态。</summary>
        public void ResetInput()
        {
            hasInputThisFrame = false;
            moveDir = Vector2.zero;
            wasAtkPressedThisFrame = false;
            wasAtkPressed = false;
            wasSkillPressedThisFrame = false;
            wasUltPressedThisFrame = false;
            wasDodgePressedThisFrame = false;
            wasJumpPressedThisFrame = false;
            wasSpecialAtkPressedThisFrame = false;
            wasToggleSprintPressedThisFrame = false;
            wasToggleWalkPressedThisFrame = false;
        }

        /// <summary>把一个输入源获准分发的动作合并进当前 Entity 的逐帧命令缓冲区。</summary>
        public void ApplyInput(in InputFrame frame, InputActionMask actions)
        {
            hasInputThisFrame |= frame.HasAny(actions);
            if ((actions & InputActionMask.Move) != 0) moveDir = Vector2.ClampMagnitude(moveDir + frame.Move, 1f);
            if ((actions & InputActionMask.Attack) != 0)
            {
                wasAtkPressedThisFrame |= frame.Attack.PressedThisFrame;
                wasAtkPressed |= frame.Attack.Held;
            }
            if ((actions & InputActionMask.Skill) != 0) wasSkillPressedThisFrame |= frame.Skill.PressedThisFrame;
            if ((actions & InputActionMask.Ultimate) != 0) wasUltPressedThisFrame |= frame.Ultimate.PressedThisFrame;
            if ((actions & InputActionMask.Dodge) != 0) wasDodgePressedThisFrame |= frame.Dodge.PressedThisFrame;
            if ((actions & InputActionMask.Jump) != 0) wasJumpPressedThisFrame |= frame.Jump.PressedThisFrame;
            if ((actions & InputActionMask.SpecialAttack) != 0) wasSpecialAtkPressedThisFrame |= frame.SpecialAttack.PressedThisFrame;
            if ((actions & InputActionMask.ToggleSprint) != 0) wasToggleSprintPressedThisFrame |= frame.ToggleSprint.PressedThisFrame;
            if ((actions & InputActionMask.ToggleWalk) != 0) wasToggleWalkPressedThisFrame |= frame.ToggleWalk.PressedThisFrame;
        }

        /// <summary>把 UIKit 普通 Button 提交的离散玩法动作合并进当前帧命令；点击只表达一次按下，不表达跨帧持续或释放。</summary>
        /// <param name="actions">一个或多个不包含移动值的玩法按钮动作。</param>
        public void ApplyButtonActions(InputActionMask actions)
        {
            if (actions == InputActionMask.None || (actions & ~InputActionMask.GameplayButtons) != 0) throw new System.ArgumentOutOfRangeException(nameof(actions), actions, "UI button actions must contain only gameplay button actions.");
            hasInputThisFrame = true;
            if ((actions & InputActionMask.Attack) != 0)
            {
                wasAtkPressedThisFrame = true;
                wasAtkPressed = true;
            }
            if ((actions & InputActionMask.Skill) != 0) wasSkillPressedThisFrame = true;
            if ((actions & InputActionMask.Ultimate) != 0) wasUltPressedThisFrame = true;
            if ((actions & InputActionMask.Dodge) != 0) wasDodgePressedThisFrame = true;
            if ((actions & InputActionMask.Jump) != 0) wasJumpPressedThisFrame = true;
            if ((actions & InputActionMask.SpecialAttack) != 0) wasSpecialAtkPressedThisFrame = true;
            if ((actions & InputActionMask.ToggleSprint) != 0) wasToggleSprintPressedThisFrame = true;
            if ((actions & InputActionMask.ToggleWalk) != 0) wasToggleWalkPressedThisFrame = true;
        }
    }
}
