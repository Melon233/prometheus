using System.Collections.Generic;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>定义固定容量小队的成员查询和上场成员切换入口。</summary>
    public interface ITeamSystem : ISystemContract
    {
        /// <summary>获取当前上场槽位。</summary>
        int ActiveSlotIndex { get; }

        /// <summary>获取当前上场成员。</summary>
        Entity ActiveMember { get; }

        /// <summary>获取当前上场实体编号。</summary>
        int ActiveEntityId { get; }

        /// <summary>使用完整成员列表初始化小队。</summary>
        void InitializeMembers(IReadOnlyList<Entity> teamMembers);

        /// <summary>尝试读取指定槽位成员。</summary>
        bool TryGetMember(int slotIndex, out Entity member);

        /// <summary>尝试切换到指定槽位。</summary>
        bool SwitchToSlot(int slotIndex);

        /// <summary>从小队中移除即将回收的实体。</summary>
        void UnregisterMember(Entity entity);
    }
}
