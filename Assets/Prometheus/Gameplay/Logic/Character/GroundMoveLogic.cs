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
            SetMoveMode(MoveMode.Run);
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
            motionComp.curVelo.y = -2f;
        }

        public override void OnDisable()
        {
            motionComp.curVelo.x = 0f;
            motionComp.curVelo.z = 0f;
        }

        public override void OnUpdate(float dt)
        {
            var curMoveMode = motionComp.moveMode;
            if (inputComp.wasToggleSprintPressedThisFrame)
                if (curMoveMode != MoveMode.Sprint) SetMoveMode(MoveMode.Sprint);
                else SetMoveMode(MoveMode.Run);
            else if (inputComp.wasToggleWalkPressedThisFrame)
                if (curMoveMode != MoveMode.Walk) SetMoveMode(MoveMode.Walk);
                else SetMoveMode(MoveMode.Run);
            if (inputComp.moveDir != Vector2.zero)
            {
                motionComp.curVelo = new Vector3(inputComp.moveDir.x, -2f, inputComp.moveDir.y) * motionComp.curMoveSpeed;
                groundMoveExecutor.Execute(motionComp.moveMode);
            }
            else
            {
                motionComp.curVelo.x = 0f;
                motionComp.curVelo.z = 0f;
                idleExecutor.Execute();
            }
        }


        public override void OnDispose()
        {
            evtComp.RemoveListener<AttackedStartEvent>(OnAttackedStart);
            evtComp.RemoveListener<AttackedEndEvent>(OnAttackedEnd);
        }
        public void SetMoveMode(MoveMode mode)
        {
            switch (mode)
            {
                case MoveMode.Walk:
                    motionComp.moveMode = MoveMode.Walk;
                    motionComp.curMoveSpeed = motionComp.propertyConfig.walkSpeed;
                    break;
                case MoveMode.Run:
                    motionComp.moveMode = MoveMode.Run;
                    motionComp.curMoveSpeed = motionComp.propertyConfig.runSpeed;
                    break;
                case MoveMode.Sprint:
                    motionComp.moveMode = MoveMode.Sprint;
                    motionComp.curMoveSpeed = motionComp.propertyConfig.sprintSpeed;
                    break;
            }
        }
    }
}