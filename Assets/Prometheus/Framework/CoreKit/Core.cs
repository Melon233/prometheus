using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Asset;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 游戏唯一运行时核心，负责注册、查询、初始化、更新和逆序释放全部 Kit。
    /// 业务代码既可以通过实例 GetKit 查询契约，也可以通过静态属性快速访问正式注册的基础模块。
    /// </summary>
    public sealed class Core : IDisposable
    {
        /// <summary>保存 Kit 契约到实现的唯一映射。</summary>
        private readonly XMap<Type, Kit> kits = new XMap<Type, Kit>();
        /// <summary>保存确定性的初始化顺序，并在释放时按相反顺序遍历。</summary>
        private readonly List<Kit> kitInitializationOrder = new List<Kit>();
        /// <summary>标记异步资源初始化正在进行，禁止重复启动入口流程。</summary>
        private bool isInitializing;
        /// <summary>标记当前 Core 已完成释放，阻止失效上下文继续被访问。</summary>
        private bool isDisposed;

        /// <summary>获取当前正式入口创建的唯一 Core 实例。</summary>
        public static Core Current { get; private set; }
        /// <summary>快速访问当前正式注册的资源模块；写入权限仅开放给同程序集组合根和友元测试程序集。</summary>
        public static IAssetKit Asset { get; internal set; }
        /// <summary>快速访问当前正式注册的全局事件模块；写入权限仅开放给同程序集组合根和友元测试程序集。</summary>
        public static IEventKit Event { get; internal set; }
        /// <summary>快速访问当前正式注册的 UI 模块；写入权限仅开放给同程序集组合根和友元测试程序集。</summary>
        public static IUIKit UI { get; internal set; }
        /// <summary>快速访问玩法会话工厂；写入权限仅开放给同程序集组合根和友元测试程序集。</summary>
        public static IGameplayKit Gameplay { get; internal set; }

        /// <summary>
        /// 创建唯一 Core，并注册四个基础模块。
        /// GameplayKit 在这里就位，但它只是**会话工厂**：本次构造不产生任何玩法 System。
        /// 具体由哪些 System 组成一局，要等登录之后由 <see cref="IGameplayKit.CreateSessionAsync"/> 决定，
        /// 因此从启动到登录界面只需要走完 Kit 初始化，不加载任何玩法配置。
        /// </summary>
        public Core()
        {
            if (Current != null) throw new InvalidOperationException("A Core instance is already active.");
            Current = this;
            RegisterKit<IAssetKit>(new AssetKit());
            RegisterKit<IEventKit>(new EventKit());
            RegisterKit<IUIKit>(new UIKit());
            RegisterKit<IGameplayKit>(new GameplayKit());
        }

        /// <summary>所有 Kit 是否都已完成异步 AfterNewAsync 和同步 AfterNew。</summary>
        public bool IsReady { get; private set; }

        /// <summary>为 Entry 创建所有 Kit 的异步初始化任务，使 Entry 可以通过 UniTask.WhenAll 统一等待。</summary>
        /// <returns>按照 Kit 注册顺序创建的异步初始化任务数组。</returns>
        public UniTask[] CreateAfterNewTasks()
        {
            ThrowIfDisposed();
            if (IsReady) throw new InvalidOperationException("Core is already initialized.");
            if (isInitializing) throw new InvalidOperationException("Core asynchronous initialization is already in progress.");
            isInitializing = true;
            UniTask[] tasks = new UniTask[kitInitializationOrder.Count];
            for (int index = 0; index < kitInitializationOrder.Count; index++) tasks[index] = kitInitializationOrder[index].AfterNewAsync();
            return tasks;
        }

        /// <summary>在 Entry 等待全部 AfterNewAsync 任务结束后，按注册顺序执行同步 AfterNew 并进入 Ready 状态。</summary>
        public void AfterNew()
        {
            ThrowIfDisposed();
            if (!isInitializing) throw new InvalidOperationException("Core must start and await AfterNewAsync tasks before AfterNew.");
            foreach (Kit kit in kitInitializationOrder) kit.AfterNew();
            IsReady = true;
            isInitializing = false;
        }

        /// <summary>获取指定契约对应的 Kit，使调用方不需要依赖内部注册容器。</summary>
        /// <typeparam name="TKit">注册 Kit 使用的接口类型。</typeparam>
        /// <returns>与接口类型对应的唯一 Kit 实例。</returns>
        public TKit GetKit<TKit>() where TKit : class, IKitContract
        {
            ThrowIfDisposed();
            if (!kits.TryGet(typeof(TKit), out Kit kit)) throw new InvalidOperationException($"Core does not contain a kit registered as '{typeof(TKit).FullName}'.");
            return kit as TKit ?? throw new InvalidCastException($"Registered kit '{kit.GetType().FullName}' does not implement '{typeof(TKit).FullName}'.");
        }

        /// <summary>在完整入口初始化完成后按注册顺序驱动全部 Kit。</summary>
        /// <param name="dt">当前帧增量时间。</param>
        public void OnUpdate(float dt)
        {
            if (!IsReady || isDisposed) return;
            foreach (Kit kit in kitInitializationOrder) kit.OnUpdate(dt);
        }

        /// <summary>按注册顺序逆序释放全部 Kit，并清空当前 Core 的静态快速入口。</summary>
        public void Dispose()
        {
            if (isDisposed) return;
            IsReady = false;
            for (int index = kitInitializationOrder.Count - 1; index >= 0; index--) kitInitializationOrder[index].Dispose();
            kitInitializationOrder.Clear();
            kits.Dispose();
            if (ReferenceEquals(Current, this)) Current = null;
            Asset = null;
            Event = null;
            UI = null;
            Gameplay = null;
            PersistentRoot.Reset();
            isInitializing = false;
            isDisposed = true;
        }

        /// <summary>以接口契约注册 Kit，并把该实例追加到确定性的生命周期序列。</summary>
        /// <typeparam name="TContract">外部查询该 Kit 时使用的接口契约。</typeparam>
        /// <param name="kit">由当前 Core 独占并负责释放的 Kit 实例。</param>
        private void RegisterKit<TContract>(Kit kit) where TContract : class, IKitContract
        {
            if (kit == null) throw new ArgumentNullException(nameof(kit));
            if (!(kit is TContract)) throw new ArgumentException($"Kit '{kit.GetType().FullName}' does not implement '{typeof(TContract).FullName}'.", nameof(kit));
            if (kits.HasKey(typeof(TContract))) throw new InvalidOperationException($"A kit is already registered as '{typeof(TContract).FullName}'.");
            kits.Add(typeof(TContract), kit);
            kitInitializationOrder.Add(kit);
            PublishStaticEntry(kit);
        }

        /// <summary>
        /// 把刚注册的 Kit 发布到对应静态快速入口。
        /// 这里是静态入口在正式链路上的唯一写入点：Kit 构造函数不产生任何全局副作用，
        /// 因此 new 一个 Kit 与把它接入当前 Core 是两件可以分别发生的事。
        /// </summary>
        /// <param name="kit">已经通过契约校验并加入生命周期序列的 Kit 实例。</param>
        private static void PublishStaticEntry(Kit kit)
        {
            if (kit is IAssetKit assetEntry) Asset = assetEntry;
            if (kit is IEventKit eventEntry) Event = eventEntry;
            if (kit is IUIKit uiEntry) UI = uiEntry;
            if (kit is IGameplayKit gameplayEntry) Gameplay = gameplayEntry;
        }

        /// <summary>阻止已经释放的 Core 被重新初始化或查询。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(Core));
        }
    }
}
