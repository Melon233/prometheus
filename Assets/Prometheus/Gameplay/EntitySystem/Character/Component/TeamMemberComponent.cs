using System;

namespace Xuan.Prometheus.Component
{
    /// <summary>保存一个独立 Entity 在本地三人小队中的固定槽位与当前上场状态。</summary>
    public sealed class TeamMemberComponent : Component
    {
        /// <summary>当成员的上场状态实际改变时通知只读表现 Logic。</summary>
        public event Action<bool> OnFieldStateChanged;

        /// <summary>获取成员所在的零基小队槽位；加入小队前为负一。</summary>
        public int SlotIndex { get; private set; } = -1;

        /// <summary>获取成员当前是否拥有场景显示、行为与本地输入控制权。</summary>
        public bool IsOnField { get; private set; }

        /// <summary>获取当前组件是否已经绑定到一个确定的小队槽位。</summary>
        public bool IsInitialized => SlotIndex >= 0;

        /// <summary>由 TeamSystem 为成员绑定唯一槽位，重复初始化会立即暴露生命周期错误。</summary>
        internal void Initialize(int slotIndex)
        {
            if (slotIndex < 0) throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Team slot index cannot be negative.");
            if (IsInitialized) throw new InvalidOperationException($"Team member component is already assigned to slot {SlotIndex}.");
            SlotIndex = slotIndex;
        }

        /// <summary>由 TeamSystem 原子更新上场状态，并只在状态实际变化时通知表现层。</summary>
        internal void SetOnField(bool isOnField)
        {
            if (IsOnField == isOnField) return;
            IsOnField = isOnField;
            OnFieldStateChanged?.Invoke(isOnField);
        }
    }
}
