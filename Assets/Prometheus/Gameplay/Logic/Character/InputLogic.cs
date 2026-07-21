using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class InputLogic : Logic
    {
        private SpineComponent spineComp;
        public InputComponent inputComp;
        public override void AfterNew()
        {
            LogicGroup = LogicGroup.Input;
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out spineComp);
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override bool CanDisable()
        {
            return false;
        }

        public override void OnEnable()
        {
        }

        public override void OnDisable()
        {
        }

        public override void OnUpdate(float dt)
        {
            inputComp.moveDir = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            inputComp.wasJumpPressedThisFrame = Input.GetKeyDown(KeyCode.Space);
            inputComp.wasAtkPressedThisFrame = Input.GetMouseButtonDown(0);
            inputComp.wasSkillPressedThisFrame = Input.GetKeyDown(KeyCode.E);
            inputComp.wasUltPressedThisFrame = Input.GetKeyDown(KeyCode.R);
            inputComp.wasDodgePressedThisFrame = Input.GetMouseButtonDown(1);

            if (inputComp.moveDir != Vector2.zero ||
                inputComp.wasJumpPressedThisFrame ||
                inputComp.wasAtkPressedThisFrame ||
                inputComp.wasDodgePressedThisFrame)
                spineComp.spineAnimator.AnimationState.ClearTrack(1);
        }


        public override void OnDispose()
        {
        }
    }
}