using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 对外暴露玩法会话的只读状态和公共 System 查询能力。
    /// 该契约不包含任何具体玩法领域概念：当前上场角色、背包、任务等状态一律由对应 System 自己发布。
    /// 会话的生命周期是框架级组合概念（不是玩法领域概念），因此可以公开。
    /// </summary>
    public interface IGameplayKit : IKit
    {
        /// <summary>当前是否存在已完成初始化的玩法会话。</summary>
        bool IsReady { get; }

        /// <summary>
        /// 按组合根的声明建立一个新的玩法会话：构造并注册全部 System，再依次驱动异步与同步初始化相位。
        /// 会话由入口在 Kit 就绪且场景加载完成后建立，随 Core 释放而销毁。
        /// </summary>
        /// <param name="installer">玩法层提供的会话组合根。</param>
        UniTask CreateSessionAsync(IGameplaySystemInstaller installer);

        /// <summary>销毁当前会话：先回收全部实体，再按注册顺序的逆序释放全部 System。</summary>
        void DestroySession();

        /// <summary>获取当前会话中指定契约对应的唯一公共 System；未注册时抛出异常。</summary>
        /// <typeparam name="TContract">注册该 System 时使用的接口契约。</typeparam>
        TContract GetSystem<TContract>() where TContract : class, ISystem;

        /// <summary>尝试获取当前会话中指定契约对应的唯一公共 System；没有会话时返回 false。</summary>
        /// <typeparam name="TContract">注册该 System 时使用的接口契约。</typeparam>
        /// <param name="system">已注册时返回对应实例，否则返回空。</param>
        /// <returns>存在会话且存在该契约的注册记录时返回 true。</returns>
        bool TryGetSystem<TContract>(out TContract system) where TContract : class, ISystem;
    }

    /// <summary>
    /// 玩法会话的工厂与生命周期驱动器。
    ///
    /// 它自己是 App 段的 Kit（随 Core 创建、随进程结束释放），而 System 是 Session 段的
    /// （Kit 就绪后建立，可单独销毁），两者生命周期不同，因此本类不**是**会话，而是**造**会话：
    /// 销毁会话时 Kit 仍然保留，可以在同一个 Core 上重建会话。
    ///
    /// "这一局由哪些 System 组成"完全交给玩法层的 <see cref="IGameplaySystemInstaller"/>，
    /// 因此框架层不持有任何具体玩法 System 的类型依赖。
    /// </summary>
    internal sealed class GameplayKit : IGameplayKit, IGameplaySystemRegistry
    {
        /// <summary>保存 System 契约到实例的唯一映射。</summary>
        private readonly XMap<Type, ISystem> systems = new XMap<Type, ISystem>();

        /// <summary>保存确定性的初始化顺序，并在释放时按相反顺序遍历。</summary>
        private readonly List<ISystem> systemInitializationOrder = new List<ISystem>();

        /// <summary>
        /// 当前会话唯一的实体驱动端口，在注册阶段由实现了该端口的 System 自动认领。
        /// 它需要在帧内固定相位驱动，且必须先于其他 System 释放，因此单独保存引用。
        /// </summary>
        private IEntityDriver entityDriver;

        /// <summary>标记正在装配会话，阻止装配过程中查询尚未初始化完成的系统。</summary>
        private bool isInstalling;

        private bool isDisposingSession;
        private bool isDisposed;

        /// <inheritdoc />
        public bool IsReady { get; private set; }

        /// <inheritdoc />
        public async UniTask CreateSessionAsync(IGameplaySystemInstaller installer)
        {
            ThrowIfDisposed();
            if (installer == null) throw new ArgumentNullException(nameof(installer));
            if (IsReady || isInstalling) throw new InvalidOperationException("A gameplay session already exists; destroy it before creating another.");
            // 会话里的每个 System 都可能在 AfterNewAsync 里按地址加载自己的配置，因此资源包必须先就绪。
            await Core.Asset.WaitUntilReadyAsync();
            isInstalling = true;
            try
            {
                installer.Install(this);
                UniTask[] tasks = new UniTask[systemInitializationOrder.Count];
                for (int index = 0; index < systemInitializationOrder.Count; index++) tasks[index] = systemInitializationOrder[index].AfterNewAsync();
                await UniTask.WhenAll(tasks);
            }
            finally
            {
                isInstalling = false;
            }

            // AfterNew 必须在全部 AfterNewAsync 之后：前者允许调用注入的依赖，而依赖的配置在后者里才就位。
            foreach (ISystem system in systemInitializationOrder) system.AfterNew();
            IsReady = true;
        }

        /// <inheritdoc />
        public void DestroySession()
        {
            // 用「有没有注册过东西」而不是「就绪了没有」作为条件：装配到一半失败时同样要走完整释放。
            if (isDisposed || isDisposingSession || systemInitializationOrder.Count == 0) return;
            isDisposingSession = true;
            IsReady = false;
            try
            {
                // 先释放实体驱动，使全部 Entity 早于依赖其数据的 System 完成回收。
                if (entityDriver is IDisposable disposableDriver) disposableDriver.Dispose();
                for (int index = systemInitializationOrder.Count - 1; index >= 0; index--)
                {
                    ISystem system = systemInitializationOrder[index];
                    if (!ReferenceEquals(system, entityDriver)) system.Dispose();
                }
            }
            finally
            {
                systemInitializationOrder.Clear();
                // XMap.Dispose 只是清空两个容器，因此同一个实例可以直接承载下一个会话。
                systems.Dispose();
                entityDriver = null;
                isDisposingSession = false;
            }
        }

        /// <inheritdoc />
        public TContract AddSystem<TContract>(ISystem system) where TContract : class, ISystem
        {
            ThrowIfDisposed();
            if (IsReady) throw new InvalidOperationException("GameplayKit cannot register a system after the session is ready.");
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (!(system is TContract contract)) throw new ArgumentException($"System '{system.GetType().FullName}' does not implement contract '{typeof(TContract).FullName}'.", nameof(system));
            Type contractType = typeof(TContract);
            if (systems.HasKey(contractType)) throw new InvalidOperationException($"GameplayKit already contains a system registered as '{contractType.FullName}'.");
            if (system is IEntityDriver registeredDriver)
            {
                if (entityDriver != null) throw new InvalidOperationException("GameplayKit already contains an entity driver; a single session can only have one entity container.");
                entityDriver = registeredDriver;
            }

            systems.Add(contractType, system);
            systemInitializationOrder.Add(system);
            return contract;
        }

        /// <inheritdoc />
        public TContract GetSystem<TContract>() where TContract : class, ISystem
        {
            ThrowIfDisposed();
            if (!systems.TryGet(typeof(TContract), out ISystem system)) throw new InvalidOperationException($"GameplayKit does not contain a system registered as '{typeof(TContract).FullName}'.");
            return system as TContract ?? throw new InvalidCastException($"Registered system '{system.GetType().FullName}' cannot be cast to '{typeof(TContract).FullName}'.");
        }

        /// <inheritdoc />
        public bool TryGetSystem<TContract>(out TContract system) where TContract : class, ISystem
        {
            ThrowIfDisposed();
            if (systems.TryGet(typeof(TContract), out ISystem registeredSystem) && registeredSystem is TContract typedSystem)
            {
                system = typedSystem;
                return true;
            }

            system = null;
            return false;
        }

        /// <summary>
        /// 按系统相位驱动当前玩法会话。
        /// 先更新实体，再按注册顺序更新全部 System（输入采样也在其中，因此输入延迟一帧生效），
        /// 且帧首帧尾各执行一次安全回收，使实体在任何 System 观察它的时刻都处于确定状态。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        public void OnUpdate(float dt)
        {
            if (isDisposed) return;
            // 安全回收先于就绪判断：待回收队列必须清空，否则失效实体会跨会话残留。
            entityDriver?.DrainPendingRemovals();
            if (!IsReady) return;

            entityDriver?.UpdateEntities(dt);

            foreach (ISystem system in systemInitializationOrder) system.OnUpdate(dt);

            entityDriver?.DrainPendingRemovals();
        }

        /// <summary>玩法会话工厂没有异步初始化工作，直接返回已完成任务。</summary>
        public UniTask AfterNewAsync()
        {
            return UniTask.CompletedTask;
        }

        /// <summary>玩法会话工厂没有同步初始化工作。</summary>
        public void AfterNew()
        {
        }

        /// <summary>进程结束时销毁尚存的会话；会话本身的释放顺序由 DestroySession 保证。</summary>
        public void Dispose()
        {
            if (isDisposed) return;
            DestroySession();
            systems.Dispose();
            isDisposed = true;
        }

        /// <summary>阻止已释放 Kit 被再次使用，避免静默写入已经清空的 System 容器。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(GameplayKit));
        }
    }
}
