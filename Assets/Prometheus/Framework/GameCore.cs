using System;
using System.Collections;
using System.Collections.Generic;
using Xuan.Prometheus.Asset;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 表示一次独立游戏运行上下文，统一注册、初始化、更新和释放所有 Kit。
    /// GameCore 本身是普通 C# 对象，其生命周期由场景中的 Entry 组件驱动。
    /// </summary>
    public sealed class GameCore : IDisposable
    {
        private readonly XMap<Type, Kit> kits = new XMap<Type, Kit>();
        private readonly List<Kit> kitInitializationOrder = new List<Kit>();
        private bool isInitializing;
        private bool isDisposed;

        /// <summary>
        /// 创建并注册当前正式启动流程需要的 AssetKit、UIKit 和 GameplayKit。
        /// 依赖项先注册，保证初始化时资源先于 UI 和玩法就绪，释放时玩法和 UI 先于资源销毁。
        /// </summary>
        public GameCore()
        {
            AssetKit assetKit = new AssetKit();
            UIKit uiKit = new UIKit(assetKit);
            GameplayKit gameplayKit = new GameplayKit(assetKit);
            RegisterKit<IAssetKit>(assetKit);
            RegisterKit<IUIKit>(uiKit);
            RegisterKit<IGameplayKit>(gameplayKit);
        }

        /// <summary>
        /// 所有已注册 Kit 是否完成初始化并可以进入逐帧更新。
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// 初始化资源包和玩法世界。
        /// 资源初始化是异步步骤，全部前置条件完成后才调用各 Kit 的 AfterNew。
        /// </summary>
        /// <param name="options">由 Entry 提供的场景启动参数。</param>
        public IEnumerator Initialize(GameplayStartupOptions options)
        {
            ThrowIfDisposed();

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (IsReady)
                yield break;

            if (isInitializing)
                throw new InvalidOperationException("GameCore initialization is already in progress.");

            isInitializing = true;
            try
            {
                IGameplayKit gameplayContract = GetKit<IGameplayKit>();
                GameplayKit gameplayKit = gameplayContract as GameplayKit ?? throw new InvalidCastException($"Kit registered as '{typeof(IGameplayKit).FullName}' is not '{typeof(GameplayKit).FullName}'.");
                IAssetKit assetKit = GetKit<IAssetKit>();
                gameplayKit.Configure(options);
                yield return assetKit.Initialize(options.PackageName);

                foreach (Kit kit in kitInitializationOrder)
                    kit.AfterNew();

                IsReady = true;
            }
            finally
            {
                isInitializing = false;
            }
        }

        /// <summary>
        /// 获取指定契约对应的 Kit，业务代码不需要依赖内部注册容器。
        /// </summary>
        /// <typeparam name="TKit">注册 Kit 使用的接口类型。</typeparam>
        /// <returns>与接口类型对应的 Kit 实例。</returns>
        public TKit GetKit<TKit>() where TKit : class
        {
            ThrowIfDisposed();

            if (!kits.TryGet(typeof(TKit), out Kit kit))
                throw new InvalidOperationException($"GameCore does not contain a kit registered as '{typeof(TKit).FullName}'.");

            return kit as TKit ?? throw new InvalidCastException($"Registered kit '{kit.GetType().FullName}' does not implement '{typeof(TKit).FullName}'.");
        }

        /// <summary>
        /// 在 GameCore 就绪后按注册顺序驱动全部 Kit。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        public void OnUpdate(float dt)
        {
            if (!IsReady || isDisposed)
                return;

            foreach (Kit kit in kitInitializationOrder)
                kit.OnUpdate(dt);
        }

        /// <summary>
        /// 按注册顺序逆序释放 Kit，使 GameplayKit 先销毁实例，AssetKit 再释放资源句柄。
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
                return;

            IsReady = false;

            for (int index = kitInitializationOrder.Count - 1; index >= 0; index--)
                kitInitializationOrder[index].Dispose();

            kitInitializationOrder.Clear();
            kits.Dispose();
            isInitializing = false;
            isDisposed = true;
        }

        /// <summary>
        /// 以接口契约注册 Kit，并记录确定性的生命周期顺序。
        /// </summary>
        private void RegisterKit<TContract>(Kit kit) where TContract : class
        {
            if (kit == null)
                throw new ArgumentNullException(nameof(kit));

            if (!(kit is TContract))
                throw new ArgumentException($"Kit '{kit.GetType().FullName}' does not implement '{typeof(TContract).FullName}'.", nameof(kit));

            if (kits.HasKey(typeof(TContract)))
                throw new InvalidOperationException($"A kit is already registered as '{typeof(TContract).FullName}'.");

            kits.Add(typeof(TContract), kit);
            kitInitializationOrder.Add(kit);
        }

        /// <summary>
        /// 防止已经释放的游戏上下文被重新初始化或访问。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(GameCore));
        }
    }
}
