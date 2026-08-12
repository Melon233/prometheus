using System;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>把 Logic 阶段汇总的三维速度一次性提交给 CharacterController；重力和落地事实统一由 GravityLogic 管理。</summary>
    public sealed class MotionLogic : Logic
    {
        private MotionComponent motionComp;
        /// <summary>保存可选的小队成员状态；敌人等非小队实体保持为空。</summary>
        private TeamMemberComponent teamMemberComponent;

        /// <summary>在全部玩法速度计算之后运行，确保玩家与敌人每帧只通过该逻辑提交合成位移。</summary>
        public override void AfterNew()
        {
            OrderTag = OrderTag.AfterGameplay;
            ControlRequirement = LogicControlRequirement.None;
            if (!Entity.TryGetComp(out motionComp) || motionComp == null) throw new InvalidOperationException($"Entity '{Entity.bindGo.name}' requires MotionComponent for motion integration.");
            if (motionComp.cc == null) throw new InvalidOperationException($"Entity '{Entity.bindGo.name}' MotionComponent requires CharacterController.");
            Entity.TryGetComp(out teamMemberComponent);
        }

        /// <summary>实体存活期间始终允许基础位移积分。</summary>
        public override bool CanEnable()
        {
            return teamMemberComponent == null || teamMemberComponent.IsOnField;
        }

        /// <summary>基础位移不会因主动玩法 Logic 的状态变化而停用。</summary>
        public override bool CanDisable()
        {
            return teamMemberComponent != null && !teamMemberComponent.IsOnField;
        }

        /// <summary>启用运动出口并允许 Root Motion 桥接组件继续累计动画位移。</summary>
        public override void OnEnable()
        {
            motionComp.SetRootMotionEnabled(true);
        }

        /// <summary>基础位移逻辑不会通过普通调度路径禁用。</summary>
        public override void OnDisable()
        {
            motionComp.SetRootMotionEnabled(false);
            motionComp.curVelo = UnityEngine.Vector3.zero;
            motionComp.landThisFrame = false;
            motionComp.wasGroundedLastFrame = false;
        }

        /// <summary>把玩法速度位移与上一动画帧累计的 Root Motion 合并后一次性提交，CharacterController 的结果在下一帧由 GravityLogic 解释。</summary>
        public override void OnUpdate(float dt)
        {
            if (dt <= 0f) return;
            motionComp.cc.Move(motionComp.curVelo * dt + motionComp.ConsumeRootMotionDelta());
        }

        /// <summary>回收时清除运行时组件引用。</summary>
        public override void OnDispose()
        {
            motionComp.SetRootMotionEnabled(false);
            motionComp = null;
            teamMemberComponent = null;
        }
    }
}
