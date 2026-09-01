using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using SceneHandle = YooAsset.SceneHandle;

namespace Xuan.Prometheus.Asset
{
    /// <summary>
    /// 定义 Core 向其他 Kit 提供的资源能力，调用方不需要直接依赖 ResourcePackage、AssetHandle 或 SceneHandle。
    /// </summary>
    public interface IAssetKit : IDisposable
    {
        /// <summary>
        /// 默认资源包是否已经可以加载资源。
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// 初始化指定的 YooAsset 资源包。
        /// </summary>
        IEnumerator Initialize(string packageName = AssetKit.DefaultPackageName);

        /// <summary>
        /// 同步加载并缓存指定类型资源。
        /// </summary>
        TAsset LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object;

        /// <summary>
        /// 异步加载并缓存指定类型资源。
        /// </summary>
        IEnumerator LoadAssetAsync<TAsset>(string location, Action<TAsset> onCompleted, Action<string> onFailed = null, uint priority = 0) where TAsset : UnityEngine.Object;

        /// <summary>
        /// 在场景根节点同步实例化预制体。
        /// </summary>
        GameObject InstantiateSync(string location, bool isActive = true);

        /// <summary>
        /// 在指定父节点下同步实例化预制体。
        /// </summary>
        GameObject InstantiateSync(string location, Transform parent, bool worldPositionStays = false, bool isActive = true);

        /// <summary>
        /// 在指定世界位置、旋转和可选父节点下同步实例化预制体。
        /// </summary>
        GameObject InstantiateSync(string location, Vector3 position, Quaternion rotation, Transform parent = null, bool isActive = true);

        /// <summary>
        /// 异步加载预制体并在指定父节点下创建实例。
        /// </summary>
        IEnumerator InstantiateAsync(string location, Action<GameObject> onCompleted, Transform parent = null, bool worldPositionStays = false, bool isActive = true, Action<string> onFailed = null, uint priority = 0);

        /// <summary>等待 AssetKit 的 AfterNewAsync 完成资源包初始化。</summary>
        UniTask WaitUntilReadyAsync();

        /// <summary>异步加载并切换到指定 YooAsset 场景。</summary>
        UniTask<Scene> LoadSceneAsync(string location);

        /// <summary>
        /// 释放指定资源地址的缓存句柄。
        /// </summary>
        bool ReleaseAsset(string location);

        /// <summary>
        /// 释放当前 AssetKit 持有的全部资源句柄。
        /// </summary>
        void ReleaseAllAssets();

        /// <summary>
        /// 请求资源包卸载已经没有句柄引用的资源。
        /// </summary>
        UnloadUnusedAssetsOperation UnloadUnusedAssetsAsync();
    }

    /// <summary>
    /// 提供基于 YooAsset 的实例资源入口，负责默认资源包初始化、资源加载、预制体实例化和句柄释放。
    /// 每个 Core 持有自己的 AssetKit 和句柄缓存，跨模块资源访问统一通过 Core.Asset。
    /// </summary>
    public sealed class AssetKit : Kit, IAssetKit
    {
        /// <summary>
        /// 项目默认资源包名称。
        /// </summary>
        public const string DefaultPackageName = "DefaultPackage";
        /// <summary>
        /// 按资源地址缓存有效句柄，确保返回的资源对象和已实例化对象使用期间其依赖资源不会被提前卸载。
        /// </summary>
        private readonly Dictionary<string, AssetHandle> assetHandles = new Dictionary<string, AssetHandle>(StringComparer.Ordinal);
        /// <summary>向依赖 AssetKit 的其他 Kit 广播资源包异步初始化结果。</summary>
        private readonly UniTaskCompletionSource initializationCompletion = new UniTaskCompletionSource();

        private ResourcePackage defaultPackage;
        /// <summary>持有当前通过 AssetKit 加载的场景句柄，保证场景依赖包在玩法运行期间保持有效。</summary>
        private SceneHandle activeSceneHandle;
        /// <summary>由 Core 在并发异步初始化开始前写入的目标资源包名称。</summary>
        private string configuredPackageName;
        private string initializedPackageName;
        private bool isInitializing;
        private bool isDisposed;

        /// <summary>
        /// 默认资源包是否已完成初始化并加载有效资源清单。
        /// </summary>
        public bool IsReady => !isDisposed && defaultPackage != null && defaultPackage.InitializeStatus == EOperationStatus.Succeeded && defaultPackage.PackageValid;


        /// <summary>在 AfterNewAsync 开始前配置当前 Core 使用的唯一 YooAsset 资源包。</summary>
        /// <param name="packageName">需要异步初始化的资源包名称。</param>
        public void Configure(string packageName)
        {
            ThrowIfDisposed();
            ValidatePackageName(packageName);
            if (configuredPackageName != null) throw new InvalidOperationException("AssetKit can only be configured once.");
            configuredPackageName = packageName;
        }

        /// <summary>通过现有初始化协程异步初始化配置的资源包，并向所有依赖 Kit 传播完成或失败结果。</summary>
        public override async UniTask AfterNewAsync()
        {
            if (configuredPackageName == null) throw new InvalidOperationException("AssetKit must be configured before AfterNewAsync.");
            try
            {
                await Initialize(configuredPackageName).ToUniTask();
                initializationCompletion.TrySetResult();
            }
            catch (Exception exception)
            {
                initializationCompletion.TrySetException(exception);
                throw;
            }
        }

        /// <inheritdoc />
        public UniTask WaitUntilReadyAsync()
        {
            return initializationCompletion.Task;
        }

        /// <summary>
        /// 初始化指定的 YooAsset 资源包，并在需要时请求版本和加载资源清单。
        /// 多个调用方同时初始化时，后续调用会等待首个初始化流程结束，不会重复创建初始化操作。
        /// </summary>
        /// <param name="packageName">资源包名称；为空时会抛出参数异常。</param>
        public IEnumerator Initialize(string packageName = DefaultPackageName)
        {
            ThrowIfDisposed();
            ValidatePackageName(packageName);

            if (IsReady)
            {
                EnsureSamePackage(packageName);
                yield break;
            }

            if (isInitializing)
            {
                while (isInitializing)
                    yield return null;

                if (!IsReady)
                    throw new InvalidOperationException($"AssetKit failed to initialize resource package '{packageName}'.");

                EnsureSamePackage(packageName);
                yield break;
            }

            isInitializing = true;
            try
            {
                if (!YooAssets.IsInitialized)
                    YooAssets.Initialize();

                if (!YooAssets.TryGetPackage(packageName, out ResourcePackage package))
                    package = YooAssets.CreatePackage(packageName);

                while (package.InitializeStatus == EOperationStatus.Processing)
                    yield return null;

                if (package.InitializeStatus != EOperationStatus.Succeeded)
                {
                    InitializePackageOperation initializeOperation = package.InitializePackageAsync(CreateInitializeOptions(packageName));
                    yield return initializeOperation;

                    if (initializeOperation.Status != EOperationStatus.Succeeded)
                        throw new InvalidOperationException($"Failed to initialize YooAsset package '{packageName}': {initializeOperation.Error}");
                }

                if (!package.PackageValid)
                {
                    RequestPackageVersionOperation versionOperation = package.RequestPackageVersionAsync();
                    yield return versionOperation;

                    if (versionOperation.Status != EOperationStatus.Succeeded)
                        throw new InvalidOperationException($"Failed to request YooAsset package version '{packageName}': {versionOperation.Error}");

                    LoadPackageManifestOperation manifestOperation = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(versionOperation.PackageVersion, 60));
                    yield return manifestOperation;

                    if (manifestOperation.Status != EOperationStatus.Succeeded)
                        throw new InvalidOperationException($"Failed to load YooAsset package manifest '{packageName}': {manifestOperation.Error}");
                }

                defaultPackage = package;
                initializedPackageName = packageName;
            }
            finally
            {
                isInitializing = false;
            }
        }

        /// <inheritdoc />
        public async UniTask<Scene> LoadSceneAsync(string location)
        {
            ValidateLocation(location);
            if (activeSceneHandle != null && activeSceneHandle.IsValid) throw new InvalidOperationException($"AssetKit already owns loaded scene '{activeSceneHandle.SceneName}'.");
            SceneHandle sceneHandle = GetReadyPackage().LoadSceneAsync(location, LoadSceneMode.Single);
            await UniTask.WaitUntil(() => sceneHandle.IsDone);
            if (sceneHandle.Status != EOperationStatus.Succeeded)
            {
                string error = sceneHandle.Error;
                sceneHandle.Release();
                throw new InvalidOperationException($"Failed to load scene '{location}' from package '{initializedPackageName}': {error}");
            }
            Scene loadedScene = sceneHandle.SceneObject;
            if (!loadedScene.IsValid() || !loadedScene.isLoaded)
            {
                sceneHandle.Release();
                throw new InvalidOperationException($"YooAsset completed scene '{location}' without a valid loaded Scene.");
            }
            activeSceneHandle = sceneHandle;
            return loadedScene;
        }

        /// <summary>
        /// 同步加载指定类型资源并缓存其句柄；重复加载同一地址时直接复用缓存资源。
        /// </summary>
        /// <typeparam name="TAsset">期望的 Unity 资源类型。</typeparam>
        /// <param name="location">YooAsset 资源地址。</param>
        /// <returns>已完成加载的资源对象。</returns>
        public TAsset LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            return GetOrLoadHandleSync<TAsset>(location).GetAssetObject<TAsset>();
        }

        /// <summary>
        /// 异步加载指定类型资源并缓存其句柄；加载完成后通过回调返回资源对象。
        /// </summary>
        /// <typeparam name="TAsset">期望的 Unity 资源类型。</typeparam>
        /// <param name="location">YooAsset 资源地址。</param>
        /// <param name="onCompleted">加载成功回调；允许为空。</param>
        /// <param name="onFailed">加载失败回调；为空时会向 Console 输出错误。</param>
        /// <param name="priority">YooAsset 加载优先级。</param>
        public IEnumerator LoadAssetAsync<TAsset>(string location, Action<TAsset> onCompleted, Action<string> onFailed = null, uint priority = 0) where TAsset : UnityEngine.Object
        {
            ValidateLocation(location);

            if (TryGetCachedAsset(location, out TAsset cachedAsset))
            {
                onCompleted?.Invoke(cachedAsset);
                yield break;
            }

            AssetHandle handle = GetReadyPackage().LoadAssetAsync<TAsset>(location, priority);
            yield return handle;

            if (handle.Status != EOperationStatus.Succeeded)
            {
                string error = $"Failed to load asset '{location}' from package '{initializedPackageName}': {handle.Error}";
                handle.Release();
                ReportAsyncFailure(error, onFailed);
                yield break;
            }

            TAsset loadedAsset = handle.GetAssetObject<TAsset>();
            if (loadedAsset == null)
            {
                string error = $"Loaded asset '{location}' cannot be cast to '{typeof(TAsset).FullName}'.";
                handle.Release();
                ReportAsyncFailure(error, onFailed);
                yield break;
            }

            if (assetHandles.TryGetValue(location, out AssetHandle existingHandle) && existingHandle.IsValid)
            {
                handle.Release();
                loadedAsset = GetAssetFromHandle<TAsset>(location, existingHandle);
            }
            else
            {
                assetHandles[location] = handle;
            }

            onCompleted?.Invoke(loadedAsset);
        }

        /// <summary>
        /// 在场景根节点同步实例化预制体，实例默认激活。
        /// </summary>
        /// <param name="location">GameObject 预制体的 YooAsset 地址。</param>
        /// <param name="isActive">实例创建后是否激活。</param>
        /// <returns>创建完成的场景实例。</returns>
        public GameObject InstantiateSync(string location, bool isActive = true)
        {
            return InstantiateFromHandle(location, GetOrLoadHandleSync<GameObject>(location), new InstantiateOptions(isActive));
        }

        /// <summary>
        /// 在指定父节点下同步实例化预制体。
        /// </summary>
        /// <param name="location">GameObject 预制体的 YooAsset 地址。</param>
        /// <param name="parent">新实例的父节点。</param>
        /// <param name="worldPositionStays">是否保留预制体的世界坐标。</param>
        /// <param name="isActive">实例创建后是否激活。</param>
        /// <returns>创建完成的场景实例。</returns>
        public GameObject InstantiateSync(string location, Transform parent, bool worldPositionStays = false, bool isActive = true)
        {
            return InstantiateFromHandle(location, GetOrLoadHandleSync<GameObject>(location), new InstantiateOptions(isActive, parent, worldPositionStays));
        }

        /// <summary>
        /// 在指定位置、旋转和可选父节点下同步实例化预制体。
        /// </summary>
        /// <param name="location">GameObject 预制体的 YooAsset 地址。</param>
        /// <param name="position">实例的世界位置。</param>
        /// <param name="rotation">实例的世界旋转。</param>
        /// <param name="parent">实例的父节点；允许为空。</param>
        /// <param name="isActive">实例创建后是否激活。</param>
        /// <returns>创建完成的场景实例。</returns>
        public GameObject InstantiateSync(string location, Vector3 position, Quaternion rotation, Transform parent = null, bool isActive = true)
        {
            return InstantiateFromHandle(location, GetOrLoadHandleSync<GameObject>(location), new InstantiateOptions(isActive, parent, position, rotation));
        }

        /// <summary>
        /// 异步加载预制体，并在指定父节点下创建实例。
        /// 资源加载是异步的；Unity 2021 中最终 GameObject 克隆仍在主线程同步完成。
        /// </summary>
        /// <param name="location">GameObject 预制体的 YooAsset 地址。</param>
        /// <param name="onCompleted">实例创建成功回调；允许为空。</param>
        /// <param name="parent">新实例的父节点；允许为空。</param>
        /// <param name="worldPositionStays">是否保留预制体的世界坐标。</param>
        /// <param name="isActive">实例创建后是否激活。</param>
        /// <param name="onFailed">加载或实例化失败回调；为空时会向 Console 输出错误。</param>
        /// <param name="priority">YooAsset 加载优先级。</param>
        public IEnumerator InstantiateAsync(string location, Action<GameObject> onCompleted, Transform parent = null, bool worldPositionStays = false, bool isActive = true, Action<string> onFailed = null, uint priority = 0)
        {
            GameObject prefab = null;
            string loadError = null;
            yield return LoadAssetAsync<GameObject>(location, asset => prefab = asset, error => loadError = error, priority);

            if (prefab == null)
            {
                ReportAsyncFailure(loadError ?? $"Failed to load prefab '{location}'.", onFailed);
                yield break;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, worldPositionStays);
            if (instance == null)
            {
                ReportAsyncFailure($"Failed to instantiate prefab '{location}'.", onFailed);
                yield break;
            }

            instance.SetActive(isActive);
            onCompleted?.Invoke(instance);
        }

        /// <summary>
        /// 释放指定地址对应的缓存句柄。
        /// 调用方必须确保所有依赖该资源的场景实例均已销毁或不再使用。
        /// </summary>
        /// <param name="location">需要释放的 YooAsset 资源地址。</param>
        /// <returns>找到并释放句柄时返回 true，否则返回 false。</returns>
        public bool ReleaseAsset(string location)
        {
            ThrowIfDisposed();
            ValidateLocation(location);

            if (!assetHandles.TryGetValue(location, out AssetHandle handle))
                return false;

            assetHandles.Remove(location);

            if (handle.IsValid)
                handle.Release();

            return true;
        }

        /// <summary>
        /// 释放本类缓存的全部资源句柄。
        /// 该方法不会销毁场景实例，也不会销毁 YooAsset 全局系统或资源包。
        /// </summary>
        public void ReleaseAllAssets()
        {
            foreach (AssetHandle handle in assetHandles.Values)
            {
                if (handle.IsValid)
                    handle.Release();
            }

            assetHandles.Clear();
        }

        /// <summary>
        /// 请求 YooAsset 清理已经没有有效句柄引用的资源。
        /// 调用方可以 yield return 返回的操作以等待清理完成。
        /// </summary>
        /// <returns>YooAsset 的异步卸载操作。</returns>
        public UnloadUnusedAssetsOperation UnloadUnusedAssetsAsync()
        {
            return GetReadyPackage().UnloadUnusedAssetsAsync();
        }

        /// <summary>
        /// 释放全部缓存句柄并清除当前资源包引用。
        /// YooAssets 是第三方全局子系统，可能被其他上下文使用，因此这里不销毁 YooAssets 本身。
        /// </summary>
        public override void Dispose()
        {
            if (isDisposed)
                return;

            if (activeSceneHandle != null && activeSceneHandle.IsValid) activeSceneHandle.Release();
            activeSceneHandle = null;
            ReleaseAllAssets();
            defaultPackage = null;
            configuredPackageName = null;
            initializedPackageName = null;
            isInitializing = false;
            isDisposed = true;
        }

        /// <summary>
        /// 获取或同步创建指定资源的缓存句柄。
        /// </summary>
        private AssetHandle GetOrLoadHandleSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            ValidateLocation(location);

            if (assetHandles.TryGetValue(location, out AssetHandle cachedHandle))
            {
                if (cachedHandle.IsValid)
                {
                    GetAssetFromHandle<TAsset>(location, cachedHandle);
                    return cachedHandle;
                }

                assetHandles.Remove(location);
            }

            AssetHandle handle = GetReadyPackage().LoadAssetSync<TAsset>(location);
            if (handle.Status != EOperationStatus.Succeeded)
            {
                string error = handle.Error;
                handle.Release();
                throw new InvalidOperationException($"Failed to load asset '{location}' from package '{initializedPackageName}': {error}");
            }

            GetAssetFromHandle<TAsset>(location, handle);
            assetHandles.Add(location, handle);
            return handle;
        }

        /// <summary>
        /// 尝试从有效缓存句柄中获取指定类型资源。
        /// </summary>
        private bool TryGetCachedAsset<TAsset>(string location, out TAsset asset) where TAsset : UnityEngine.Object
        {
            if (assetHandles.TryGetValue(location, out AssetHandle handle))
            {
                if (handle.IsValid)
                {
                    asset = GetAssetFromHandle<TAsset>(location, handle);
                    return true;
                }

                assetHandles.Remove(location);
            }

            asset = null;
            return false;
        }

        /// <summary>
        /// 从句柄读取并验证资源类型，避免同一地址被以不兼容类型重复访问。
        /// </summary>
        private static TAsset GetAssetFromHandle<TAsset>(string location, AssetHandle handle) where TAsset : UnityEngine.Object
        {
            TAsset asset = handle.GetAssetObject<TAsset>();
            if (asset == null)
                throw new InvalidOperationException($"Asset '{location}' cannot be cast to '{typeof(TAsset).FullName}'.");

            return asset;
        }

        /// <summary>
        /// 使用已验证的 GameObject 句柄实例化对象，并统一生成失败异常。
        /// </summary>
        private static GameObject InstantiateFromHandle(string location, AssetHandle handle, InstantiateOptions options)
        {
            GameObject instance = handle.InstantiateSync(options);
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate prefab '{location}'.");

            return instance;
        }

        /// <summary>
        /// 获取已经就绪的默认资源包，未初始化时提供明确的调用顺序错误。
        /// </summary>
        private ResourcePackage GetReadyPackage()
        {
            ThrowIfDisposed();

            if (!IsReady)
                throw new InvalidOperationException("AssetKit is not initialized. Yield return the registered AssetKit.Initialize() operation before loading assets.");

            return defaultPackage;
        }

        /// <summary>
        /// 防止同一静态入口在运行期间被无意切换到另一个默认资源包。
        /// </summary>
        private void EnsureSamePackage(string packageName)
        {
            if (!string.Equals(initializedPackageName, packageName, StringComparison.Ordinal))
                throw new InvalidOperationException($"AssetKit is already initialized with package '{initializedPackageName}' and cannot switch to '{packageName}'.");
        }

        /// <summary>
        /// 校验资源包名称，避免 YooAsset 在更深层抛出缺少业务上下文的异常。
        /// </summary>
        private static void ValidatePackageName(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("YooAsset package name cannot be empty.", nameof(packageName));
        }

        /// <summary>
        /// 校验资源地址，确保所有加载和释放 API 使用相同的地址约束。
        /// </summary>
        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                throw new ArgumentException("YooAsset location cannot be empty.", nameof(location));
        }

        /// <summary>
        /// 统一处理协程异步 API 的失败结果；有失败回调时交给调用方，否则输出可诊断错误。
        /// </summary>
        private static void ReportAsyncFailure(string error, Action<string> onFailed)
        {
            if (onFailed != null)
            {
                onFailed(error);
                return;
            }

            Debug.LogError(error);
        }

        /// <summary>
        /// 根据运行环境创建 YooAsset 初始化参数：编辑器使用模拟构建，Player 使用内置离线资源。
        /// </summary>
        private static InitializePackageOptions CreateInitializeOptions(string packageName)
        {
#if UNITY_EDITOR
            PackageBuildResult buildResult = EditorSimulateBuildInvoker.Build(packageName, (int)EBundleType.VirtualAssetBundle);
            return new EditorSimulateModeOptions { EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory) };
#else
            return new OfflinePlayModeOptions { BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters() };
#endif
        }

        /// <summary>
        /// 阻止已经由 Core 释放的 AssetKit 继续加载或释放单个资源。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(AssetKit));
        }
    }
}
