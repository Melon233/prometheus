using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>负责地面移动数值与移动动画请求；移动动画优先级高于落地动画，因此输入可以即时打断落地表现。</summary>
    public sealed class GroundMoveLogic : Logic
    {
        private SpineComponent spineComponent;
        private InputComponent inputComponent;
        private MotionComponent motionComponent;
        private EventComponent eventComponent;
        private PropertyComponent propertyComponent;

        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.Move;
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out inputComponent);
            Entity.TryGetComp(out motionComponent);
            Entity.TryGetComp(out eventComponent);
            Entity.TryGetComp(out propertyComponent);
            SetMoveMode(MoveMode.Run);
            eventComponent.AddListener<AttackedStartEvent>(OnAttackedStart);
            eventComponent.AddListener<AttackedEndEvent>(OnAttackedEnd);
        }

        private void OnAttackedEnd(AttackedEndEvent evt)
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<JumpLogic>();
        }

        private void OnAttackedStart(AttackedStartEvent evt)
        {
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<JumpLogic>();
        }

        public override bool CanEnable()
        {
            return motionComponent.cc.isGrounded && inputComponent.moveDir != Vector2.zero;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override void OnEnable()
        {
            motionComponent.curVelo.y = -2f;
        }

        /// <summary>停止本 Logic 的循环移动动画，使较低优先级待机在松开输入后能够接管轨道。</summary>
        public override void OnDisable()
        {
            motionComponent.curVelo.x = 0f;
            motionComponent.curVelo.z = 0f;
            spineComponent.Stop(AnimationOwner.GroundMove);
        }

        public override void OnUpdate(float dt)
        {
            MoveMode currentMoveMode = motionComponent.moveMode;
            if (inputComponent.wasToggleSprintPressedThisFrame)
            {
                SetMoveMode(currentMoveMode == MoveMode.Sprint ? MoveMode.Run : MoveMode.Sprint);
            }
            else if (inputComponent.wasToggleWalkPressedThisFrame)
            {
                SetMoveMode(currentMoveMode == MoveMode.Walk ? MoveMode.Run : MoveMode.Walk);
            }
            motionComponent.curVelo = new Vector3(inputComponent.moveDir.x, -2f, inputComponent.moveDir.y) * propertyComponent.MoveSpeed;
            GroundMoveExecutor configuration = spineComponent.animationLib.groundMoveExecutor;
            spineComponent.TryPlay(configuration.GetSemantic(motionComponent.moveMode), AnimationOwner.GroundMove, AnimationPriority.Locomotion, true);
        }

        public override void OnDispose()
        {
            eventComponent.RemoveListener<AttackedStartEvent>(OnAttackedStart);
            eventComponent.RemoveListener<AttackedEndEvent>(OnAttackedEnd);
        }

        /// <summary>更新移动模式及其基础速度，最终速度仍由 PropertyComponent modifier 聚合。</summary>
        public void SetMoveMode(MoveMode mode)
        {
            motionComponent.moveMode = mode;
            switch (mode)
            {
                case MoveMode.Walk:
                    propertyComponent.SetBaseValue(PropertyType.MoveSpeed, motionComponent.propertyConfig.walkSpeed);
                    break;
                case MoveMode.Sprint:
                    propertyComponent.SetBaseValue(PropertyType.MoveSpeed, motionComponent.propertyConfig.sprintSpeed);
                    break;
                default:
                    propertyComponent.SetBaseValue(PropertyType.MoveSpeed, motionComponent.propertyConfig.runSpeed);
                    break;
            }
        }
    }
}
