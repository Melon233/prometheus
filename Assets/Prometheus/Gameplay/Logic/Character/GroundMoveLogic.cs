using System;
using Spine;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class GroundMoveLogic : Logic
    {
        SpineComponent spineComp;
        InputComponent inputComp;
        MotionComponent motionComp;
        GroundMoveExecutor groundMoveExecutor;
        IdleExecutor idleExecutor;
        EventComponent evtComp;

        public override void AfterNew()
        {
            LogicGroup = OrderTag.Gameplay;
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out motionComp);
            Entity.TryGetComp(out evtComp);
            groundMoveExecutor = spineComp.animationLib.groundMoveExecutor;
            idleExecutor = spineComp.animationLib.idleExecutor;
            evtComp.AddListener<AttackedStartEvent>(OnAttackedStart);
            evtComp.AddListener<AttackedEndEvent>(OnAttackedEnd);
        }

        private void OnAttackedEnd(AttackedEndEvent @event)
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<JumpLogic>();
        }

        private void OnAttackedStart(AttackedStartEvent @event)
        {
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<JumpLogic>();
        }

        public override bool CanEnable()
        {
            return motionComp.cc.isGrounded;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override void OnEnable()
        {
            motionComp.baseSpeed.y = -2f;
        }

        public override void OnDisable()
        {
            motionComp.baseSpeed.x = 0f;
            motionComp.baseSpeed.z = 0f;
        }

        public override void OnUpdate(float dt)
        {
            if (inputComp.moveDir != Vector2.zero)
            {
                motionComp.baseSpeed = new Vector3(inputComp.moveDir.x, -2f, inputComp.moveDir.y) * motionComp.walkVelo;
                groundMoveExecutor.Execute();
            }
            else
            {
                motionComp.baseSpeed.x = 0f;
                motionComp.baseSpeed.z = 0f;
                idleExecutor.Execute();
            }
        }


        public override void OnDispose()
        {
            evtComp.RemoveListener<AttackedStartEvent>(OnAttackedStart);
            evtComp.RemoveListener<AttackedEndEvent>(OnAttackedEnd);
        }
    }
}