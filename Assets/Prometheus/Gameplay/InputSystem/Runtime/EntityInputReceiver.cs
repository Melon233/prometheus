using System;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Input
{
    /// <summary>把 InputSystem 分发的动作片段写入指定 Entity 的逐帧 InputComponent，而不长期持有 Entity 引用。</summary>
    public sealed class EntityInputReceiver : IInputReceiver
    {
        private readonly EntitySystem entitySystem;
        private readonly int entityId;

        /// <summary>创建一个通过 Core.Gameplay 的 EntitySystem 和运行时编号定位 Entity 的输入接收适配器。</summary>
        public EntityInputReceiver(int entityId)
        {
            entitySystem = Core.Gameplay.GetSystem<EntitySystem>();
            this.entityId = entityId > 0 ? entityId : throw new ArgumentOutOfRangeException(nameof(entityId), entityId, "Entity runtime ID must be positive.");
        }

        /// <summary>获取目标 Entity 的运行时编号。</summary>
        public int EntityId => entityId;

        /// <inheritdoc />
        public bool IsAlive => TryGetInputComponent(out _);

        /// <inheritdoc />
        public void ResetInput()
        {
            if (TryGetInputComponent(out InputComponent inputComponent)) inputComponent.ResetInput();
        }

        /// <inheritdoc />
        public void ReceiveInput(in InputFrame frame, InputActionMask actions)
        {
            if (TryGetInputComponent(out InputComponent inputComponent)) inputComponent.ApplyInput(frame, actions);
        }

        /// <summary>只在 GameplayKit 和 EntitySystem 中的目标 Entity 都处于有效运行状态时取得逐帧输入缓冲区。</summary>
        private bool TryGetInputComponent(out InputComponent inputComponent)
        {
            inputComponent = null;
            if (!Core.Gameplay.IsReady) return false;
            if (!entitySystem.TryGetEntity(entityId, out Entity entity) || entity == null || !entity.IsActive) return false;
            return entity.TryGetComp(out inputComponent) && inputComponent != null;
        }
    }
}
