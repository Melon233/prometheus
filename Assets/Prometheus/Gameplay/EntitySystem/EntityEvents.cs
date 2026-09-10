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

    /// <summary>
    /// 携带已被实体容器移除的实体运行时编号的全局通知，发布者保证同一实体只通知一次。
    ///
    /// 它存在的理由是打破一个真实的依赖环：实体容器需要让小队系统注销失效成员，
    /// 而小队系统本身依赖输入系统、输入系统又依赖实体容器。
    /// 这条边表达的本来就是"某件事已经发生"而不是"请对方做点什么"，
    /// 因此用事件比用调用更准确——发布者不需要知道有谁在关心。
    /// 与 <see cref="EntityDiedEvent"/> 区分：死亡是玩法结算，移除是容器托管关系的终止，
    /// 一个实体可以被移除而从未死亡（例如离开世界时的整体回收）。
    /// </summary>
    public sealed class EntityRemovedEvent : EntityEvent
    {
        /// <summary>创建一条实体移除通知。</summary>
        /// <param name="entityId">已从容器移除的实体运行时编号。</param>
        public EntityRemovedEvent(int entityId) : base(entityId)
        {
        }
    }
}
