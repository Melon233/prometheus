using System;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>把 Logic 阶段汇总的三维速度一次性提交给 CharacterController，并记录接地与落地事实。</summary>
    public sealed class MotionLogic : Logic
    {
        private MotionComponent motionComp;

        /// <summary>在全部玩法速度计算之后运行，确保玩家与敌人每帧只通过该逻辑提交合成位移。</summary>
        public override void AfterNew()
        {
            OrderTag = OrderTag.AfterGameplay;
            ControlRequirement = LogicControlRequirement.None;
            if (!Entity.TryGetComp(out motionComp) || motionComp == null) throw new InvalidOperationException($"Entity '{Entity.bindGo.name}' requires MotionComponent for motion integration.");
            if (motionComp.cc == null) throw new InvalidOperationException($"Entity '{Entity.bindGo.name}' MotionComponent requires CharacterController.");
        }

        /// <summary>实体存活期间始终允许基础位移积分。</summary>
        public override bool CanEnable()
        {
            return true;
        }

        /// <summary>基础位移不会因主动玩法 Logic 的状态变化而停用。</summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>启用时无需额外状态切换。</summary>
        public override void OnEnable()
        {
        }

        /// <summary>基础位移逻辑不会通过普通调度路径禁用。</summary>
        public override void OnDisable()
        {
        }

        /// <summary>提交合成速度并在移动后生成当前帧落地标记。</summary>
        public override void OnUpdate(float dt)
        {
            if (dt <= 0f) return;
            motionComp.cc.Move(motionComp.curVelo * dt);
            motionComp.landThisFrame = motionComp.cc.isGrounded && !motionComp.wasGroundedLastFrame;
            motionComp.wasGroundedLastFrame = motionComp.cc.isGrounded;
        }

        /// <summary>回收时清除运行时组件引用。</summary>
        public override void OnDispose()
        {
            motionComp = null;
        }
    }
}
