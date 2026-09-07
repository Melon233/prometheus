namespace Xuan.Prometheus
{
    /// <summary>
    /// 携带已死亡实体运行时编号的全局死亡通知，发布者保证同一实体只通知一次。
    /// 与实体内 EventComponent 上的 DieEvent 区分：后者只通知该实体自身的 Logic，
    /// 本事件面向不持有该实体的其他 System（例如世界营地结算）。
    /// </summary>
    public sealed class EntityDiedEvent : EntityEvent
    {
        /// <summary>创建一条实体死亡通知。</summary>
        /// <param name="entityId">已完成致死结算的实体运行时编号。</param>
        public EntityDiedEvent(int entityId) : base(entityId)
        {
        }
    }
}
