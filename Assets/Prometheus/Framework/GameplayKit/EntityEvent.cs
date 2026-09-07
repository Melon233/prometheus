using System;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 为所有携带确定运行时 EntityId 的全局事实提供统一只读标识。
    /// 该基类只表达"这条事实属于某个已注册实体"，不包含任何玩法领域语义，因此属于框架层。
    /// </summary>
    public abstract class EntityEvent : IEvent
    {
        /// <summary>创建一条属于已注册实体的全局事实，并拒绝未分配的运行时编号。</summary>
        /// <param name="entityId">由 EntitySystem 分配的单局唯一运行时编号。</param>
        protected EntityEvent(int entityId)
        {
            if (entityId <= 0) throw new ArgumentOutOfRangeException(nameof(entityId), entityId, "Entity event requires a positive runtime entity ID.");
            EntityId = entityId;
        }

        /// <summary>获取产生当前事实的实体运行时编号。</summary>
        public int EntityId { get; }
    }
}
