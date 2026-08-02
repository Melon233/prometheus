using System;
using Xuan.Prometheus.Actor;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>把 PossessionSystem 在 Entity 更新前生成的控制帧写入旧 InputComponent 兼容字段。</summary>
    public class InputLogic : Logic
    {
        /// <summary>当前 Entity 的兼容输入组件。</summary>
        public InputComponent inputComp;

        /// <summary>当前单局唯一的控制权与控制帧系统。</summary>
        private PossessionSystem possessionSystem;

        /// <inheritdoc />
        public override void AfterNew()
        {
            OrderTag = OrderTag.Input;
            ControlRequirement = LogicControlRequirement.None;
            if (!Entity.TryGetComp(out inputComp)) throw new InvalidOperationException($"Entity '{Entity.GetType().FullName}' requires InputComponent before InputLogic initialization.");
            possessionSystem = Entity.GameplayKit.GetSystem<PossessionSystem>();
        }

        /// <inheritdoc />
        public override bool CanEnable()
        {
            return true;
        }

        /// <inheritdoc />
        public override bool CanDisable()
        {
            return false;
        }

        /// <inheritdoc />
        public override void OnEnable()
        {
        }

        /// <inheritdoc />
        public override void OnDisable()
        {
        }

        /// <inheritdoc />
        public override void OnUpdate(float dt)
        {
            if (possessionSystem.TryGetControlFrame(Entity.EntityId, out ControlFrame frame)) inputComp.ApplyControlFrame(frame);
            else inputComp.ClearFrameInput();
        }

        /// <inheritdoc />
        public override void OnDispose()
        {
            inputComp?.ClearFrameInput();
            inputComp = null;
            possessionSystem = null;
        }
    }
}
