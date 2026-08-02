using System;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Actor
{
    /// <summary>在 Entity 初始化和销毁边界把 PawnComponent 幂等注册到当前单局 PossessionSystem。</summary>
    public sealed class PawnRegistrationLogic : Xuan.Prometheus.Logic.Logic
    {
        /// <summary>当前 Entity 的 Pawn 身份组件。</summary>
        private PawnComponent pawnComponent;

        /// <summary>本次注册使用的单局 PossessionSystem。</summary>
        private PossessionSystem possessionSystem;

        /// <summary>已经成功注册的 Pawn 编号；零表示当前没有待注销项。</summary>
        private int registeredPawnId;

        /// <inheritdoc />
        public override void AfterNew()
        {
            OrderTag = OrderTag.Input;
            ControlRequirement = LogicControlRequirement.None;
            if (!Entity.TryGetComp(out pawnComponent)) throw new InvalidOperationException($"Entity '{Entity.GetType().FullName}' requires PawnComponent before PawnRegistrationLogic initialization.");
            PossessionSystem system = Entity.GameplayKit.GetSystem<PossessionSystem>();
            int pawnId = Entity.EntityId;
            system.RegisterPawn(pawnId);
            possessionSystem = system;
            registeredPawnId = pawnId;
            pawnComponent.MarkRegistered(pawnId);
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
        }

        /// <inheritdoc />
        public override void OnDispose()
        {
            int pawnId = registeredPawnId;
            registeredPawnId = 0;
            try
            {
                if (pawnId > 0) possessionSystem?.UnregisterPawn(pawnId);
            }
            finally
            {
                if (pawnId > 0) pawnComponent?.MarkUnregistered(pawnId);
                pawnComponent = null;
                possessionSystem = null;
            }
        }
    }
}
