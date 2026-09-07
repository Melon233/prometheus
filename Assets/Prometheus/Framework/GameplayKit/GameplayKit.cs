using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 对外暴露玩法世界的只读状态和公共 System 查询能力。
    /// 该契约不包含任何具体玩法领域概念：当前上场角色、背包、任务等状态一律由对应 System 自己发布。
    /// </summary>
    public interface IGameplayKit : IKitContract
    {
        /// <summary>GameplayKit 是否已经完成全部 System 初始化和初始世界内容创建。</summary>
        bool IsReady { get; }

        /// <summary>获取当前单局中指定契约对应的唯一公共 System；未注册时抛出异常。</summary>
        /// <typeparam name="TContract">注册该 System 时使用的接口契约。</typeparam>
        TContract GetSystem<TContract>() where TContract : class, ISystemContract;

        /// <summary>尝试获取当前单局中指定契约对应的唯一公共 System。</summary>
        /// <typeparam name="TContract">注册该 System 时使用的接口契约。</typeparam>
        /// <param name="system">已注册时返回对应实例，否则返回空。</param>
        /// <returns>存在该契约的注册记录时返回 true。</returns>
        bool TryGetSystem<TContract>(out TContract system) where TContract : class, ISystemContract;
    }

    /// <summary>
    /// 单局公共 System 的容器与生命周期驱动器。
    /// 它只负责"按确定顺序初始化、按相位驱动、按逆序释放"，
    /// 而"这一局由哪些 System 组成"完全交给玩法层的 <see cref="IGameplaySystemInstaller"/> 决定，
    /// 因此框架层不持有任何具体玩法 System 的类型依赖。
    /// </summary>
    internal sealed class GameplayKit : Kit, IGameplayKit, IGameplaySystemRegistry
    {
        /// <summary>保存 System 契约到实例的唯一映射。</summary>
        private readonly XMap<Type, XSystem> systems = new XMap<Type, XSystem>();

        /// <summary>保存确定性的初始化顺序，并在释放时按相反顺序遍历。</summary>
        private readonly List<XSystem> systemInitializationOrder = new List<XSystem>();

        /// <summary>由玩法层提供的单局组合根，决定本局注册哪些 System 和创建哪些初始内容。</summary>
        private readonly IGameplaySystemInstaller installer;

        /// <summary>
        /// 当前单局唯一的实体驱动端口，在注册阶段由实现了该端口的 System 自动认领。
        /// 它需要在帧内固定相位驱动，且必须先于其他 System 释放，因此单独保存引用。
        /// </summary>
        private IEntityDriver entityDriver;

        private bool isDisposing;
        private bool isDisposed;

        /// <summary>创建一个尚未安装任何 System 的空容器；安装器可以为空，供测试直接手工注册 System。</summary>
        /// <param name="installer">玩法层组合根；为空时 GameplayKit 只接受外部手工注册。</param>
        public GameplayKit(IGameplaySystemInstaller installer = null)
        {
            this.installer = installer;
        }

        /// <inheritdoc />
        public bool IsReady { get; private set; }

        /// <summary>等待 AssetKit 就绪后，把本局的组合过程完全交给玩法层安装器。</summary>
        public override async UniTask AfterNewAsync()
        {
            ThrowIfDisposed();
            if (installer == null) throw new InvalidOperationException("GameplayKit requires a gameplay system installer before AfterNewAsync.");
            await Core.Asset.WaitUntilReadyAsync();
            await installer.InstallAsync(this);
        }

        /// <inheritdoc />
        public void AddSystem<TContract>(XSystem system) where TContract : class, ISystemContract
        {
            ThrowIfDisposed();
            if (isDisposing) throw new InvalidOperationException("GameplayKit cannot register a system while it is disposing.");
            if (IsReady) throw new InvalidOperationException("GameplayKit cannot register a system after it is ready.");
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (!(system is TContract)) throw new ArgumentException($"System '{system.GetType().FullName}' does not implement contract '{typeof(TContract).FullName}'.", nameof(system));
            Type contractType = typeof(TContract);
            if (systems.HasKey(contractType)) throw new InvalidOperationException($"GameplayKit already contains a system registered as '{contractType.FullName}'.");
            if (system is IEntityDriver registeredDriver)
            {
                if (entityDriver != null) throw new InvalidOperationException("GameplayKit already contains an entity driver; a single run can only have one entity container.");
                entityDriver = registeredDriver;
            }

            systems.Add(contractType, system);
            systemInitializationOrder.Add(system);
        }

        /// <inheritdoc />
        public TContract GetSystem<TContract>() where TContract : class, ISystemContract
        {
            ThrowIfDisposed();
            if (!systems.TryGet(typeof(TContract), out XSystem system)) throw new InvalidOperationException($"GameplayKit does not contain a system registered as '{typeof(TContract).FullName}'.");
            return system as TContract ?? throw new InvalidCastException($"Registered system '{system.GetType().FullName}' cannot be cast to '{typeof(TContract).FullName}'.");
        }

        /// <inheritdoc />
        public bool TryGetSystem<TContract>(out TContract system) where TContract : class, ISystemContract
        {
            ThrowIfDisposed();
            if (systems.TryGet(typeof(TContract), out XSystem registeredSystem) && registeredSystem is TContract typedSystem)
            {
                system = typedSystem;
                return true;
            }

            system = null;
            return false;
        }

        /// <summary>在 Entry 等待全部 Kit 异步任务完成后，按注册顺序初始化全部 System，再由安装器创建初始世界内容。</summary>
        public override void AfterNew()
        {
            ThrowIfDisposed();
            if (IsReady) return;
            if (!Core.Asset.IsReady) throw new InvalidOperationException("AssetKit must be ready before GameplayKit initializes systems.");
            foreach (XSystem system in systemInitializationOrder) system.AfterNew();
            installer?.CreateInitialContent();
            IsReady = true;
        }

        /// <summary>
        /// 按系统相位驱动当前玩法世界。
        /// 实体更新固定夹在前置与后置 System 之间，且帧首帧尾各执行一次安全回收，
        /// 使实体在任何 System 观察它的时刻都处于确定状态。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        public override void OnUpdate(float dt)
        {
            if (isDisposed) return;
            entityDriver?.DrainPendingRemovals();
            if (!IsReady) return;

            foreach (XSystem system in systemInitializationOrder) system.BeforeEntityUpdate(dt);

            entityDriver?.UpdateEntities(dt);

            foreach (XSystem system in systemInitializationOrder) system.OnUpdate(dt);

            entityDriver?.DrainPendingRemovals();
        }

        /// <summary>
        /// 先释放实体驱动，使全部 Entity 早于依赖其数据的 System 完成回收，再逆序释放其余 System。
        /// </summary>
        public override void Dispose()
        {
            if (isDisposed || isDisposing) return;
            isDisposing = true;
            IsReady = false;
            try
            {
                if (entityDriver is IDisposable disposableDriver) disposableDriver.Dispose();
            }
            finally
            {
                try
                {
                    for (int index = systemInitializationOrder.Count - 1; index >= 0; index--)
                    {
                        XSystem system = systemInitializationOrder[index];
                        if (!ReferenceEquals(system, entityDriver)) system.Dispose();
                    }
                }
                finally
                {
                    systemInitializationOrder.Clear();
                    systems.Dispose();
                    entityDriver = null;
                    isDisposed = true;
                    isDisposing = false;
                }
            }
        }

        /// <summary>阻止已释放 Kit 被再次使用，避免静默写入已经清空的 System 容器。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(GameplayKit));
        }
    }
}
