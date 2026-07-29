using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public class InputComponent : Component
    {
        public bool hasInputThisFrame;
        public Vector2 moveDir;
        public bool wasAtkPressedThisFrame;
        public bool wasAtkPressed;
        public bool wasSkillPressedThisFrame;
        public bool wasUltPressedThisFrame;
        public bool wasDodgePressedThisFrame;
        public bool wasJumpPressedThisFrame;
        public bool wasSpecialAtkPressedThisFrame;
        public bool wasToggleSprintPressedThisFrame;
        public bool wasToggleWalkPressedThisFrame;

    }
}