namespace Xuan.Prometheus
{
    /// <summary>
    /// GameplayKit 对外开放的 System 注册端口。
    /// 只有玩法层的安装器需要它；把注册能力独立成端口后，
    /// GameplayKit 不必公开自身具体类型就能接受外部组合。
    /// </summary>
    public interface IGameplaySystemRegistry
    {
        /// <summary>
        /// 以接口契约注册一个会话内唯一的 System，并把它按契约类型返回，供后续系统直接构造注入。
        ///
        /// 返回契约而不是 void，是为了让组合根写成一条依赖链：
        /// 后一个系统要用前一个，就必须先拿到前一个的返回值，于是**注册顺序由 C# 强制**，
        /// 不再是一条需要靠注释维护的约定，依赖成环也直接变成编译错误。
        /// </summary>
        /// <typeparam name="TContract">对外发布的 System 接口契约。</typeparam>
        /// <param name="system">由 GameplayKit 独占并负责释放的 System 实例。</param>
        /// <returns>刚注册的实例，按契约类型返回。</returns>
        TContract AddSystem<TContract>(XSystem system) where TContract : class, ISystemContract;
    }

    /// <summary>
    /// 由玩法层实现的会话组合根。
    ///
    /// 它只回答一个问题：**这一局由哪些 System 组成**。
    /// 配置资产由各 System 在自己的 AfterNewAsync 里加载，世界内容由各 System 在世界相位建立，
    /// 因此本接口是同步的，也不再有"创建初始内容"的步骤。
    /// </summary>
    public interface IGameplaySystemInstaller
    {
        /// <summary>构造并注册本会话的全部 System；实现内只允许 new 与注册，禁止加载资源或访问已注册系统。</summary>
        /// <param name="registry">GameplayKit 提供的注册端口。</param>
        void Install(IGameplaySystemRegistry registry);
    }

    /// <summary>
    /// 实体宿主驱动实体生命周期跃迁的特权端口。
    /// Entity 以显式接口实现该端口，因此普通持有者写 entity.DisposeImmediately() 无法通过编译，
    /// 只有明确把实体当作"自己登记的对象"来操作的宿主，才会写出到该端口的转换。
    /// 这把原本依赖"同一个程序集"的隐式约定，变成了可检索、可审查的显式能力声明。
    /// </summary>
    public interface IEntityLifecycleController
    {
        /// <summary>写入运行时编号并登记宿主，使实体从 Created 进入 Registered。</summary>
        /// <param name="entityId">当前单局唯一的运行时编号。</param>
        /// <param name="entityOwner">登记该实体的宿主容器。</param>
        void BindEntityId(int entityId, IEntityOwner entityOwner);

        /// <summary>标记首次回收请求，使实体立即停止参与逐帧更新。</summary>
        /// <param name="delay">表现对象的延迟销毁时间。</param>
        /// <returns>本次调用确实完成首次标记时返回 true。</returns>
        bool MarkDespawnRequested(float delay);

        /// <summary>在安全边界执行一次最终清理；禁用、注销和解绑阶段各自只会执行一次。</summary>
        /// <returns>本次调用确实执行了清理时返回 true。</returns>
        bool DisposeImmediately();
    }

    /// <summary>
    /// 实体宿主向 Entity 暴露的回收端口。
    /// Entity 在注册时就取得宿主引用，因此请求自我回收不需要经过任何全局查询，
    /// 框架层的 Entity 也就不必认识具体的实体容器类型。
    /// </summary>
    public interface IEntityOwner
    {
        /// <summary>请求宿主在本帧安全边界移除指定实体。</summary>
        /// <param name="entityId">目标实体的运行时编号。</param>
        /// <param name="destroyDelay">表现对象的延迟销毁时间。</param>
        /// <returns>本次调用确实登记了回收请求时返回 true。</returns>
        bool RequestRemoveEntity(int entityId, float destroyDelay = 0f);
    }

    /// <summary>
    /// 实体容器向 GameplayKit 暴露的逐帧驱动端口。
    /// GameplayKit 需要在固定的帧内相位驱动实体更新与安全回收，
    /// 但不需要认识实体容器的具体类型，因此该能力以端口形式声明在框架层。
    /// 一个 GameplayKit 中最多只能存在一个实现该端口的 System。
    /// </summary>
    public interface IEntityDriver
    {
        /// <summary>在安全边界执行本帧累积的实体回收请求。</summary>
        void DrainPendingRemovals();

        /// <summary>驱动全部处于活跃状态的实体完成当帧更新。</summary>
        /// <param name="dt">当前帧增量时间。</param>
        void UpdateEntities(float dt);
    }
}
