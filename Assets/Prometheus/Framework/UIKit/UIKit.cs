using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Asset;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 定义业务层访问 UI 系统所需的稳定能力，业务代码不需要接触资源句柄、Prefab 实例或内部面板记录。
    /// </summary>
    public interface IUIKit
    {
        /// <summary>
        /// 打开指定类型的单例面板；面板已经打开时直接返回当前控制器，已缓存时复用原实例。
        /// </summary>
        /// <typeparam name="TPanel">带有 UIPanelConfigAttribute 配置的具体面板类型。</typeparam>
        /// <returns>已经完成绑定、初始化并进入打开状态的面板控制器。</returns>
        TPanel OpenPanel<TPanel>() where TPanel : UIPanel;

        /// <summary>
        /// 关闭指定类型的单例面板，并按照面板配置选择缓存实例或销毁实例。
        /// </summary>
        /// <typeparam name="TPanel">需要关闭的具体面板类型。</typeparam>
        void ClosePanel<TPanel>() where TPanel : UIPanel;

        /// <summary>
        /// 尝试读取已经由 UIKit 创建的面板控制器，未创建的面板不会因此被打开。
        /// </summary>
        /// <typeparam name="TPanel">需要查询的具体面板类型。</typeparam>
        /// <param name="panel">找到记录时返回对应控制器，否则返回空。</param>
        /// <returns>UIKit 中存在该面板记录时返回 true。</returns>
        bool TryGetPanel<TPanel>(out TPanel panel) where TPanel : UIPanel;

        /// <summary>
        /// 判断指定面板当前是否处于可见的打开状态。
        /// </summary>
        /// <typeparam name="TPanel">需要查询的具体面板类型。</typeparam>
        /// <returns>面板已经打开且实例处于激活状态时返回 true。</returns>
        bool IsPanelOpen<TPanel>() where TPanel : UIPanel;

        /// <summary>
        /// 获取当前正在显示和更新的世界锚点 UI 实例数量。
        /// </summary>
        int ActiveWorldUICount { get; }

        /// <summary>
        /// 生成一个持续跟随目标 Transform 的世界空间 UI，适合角色血条、名称和状态标记。
        /// </summary>
        WorldUIHandle SpawnWorldUI(string assetAddress, Transform followTarget, Vector3 worldOffset, float lifetime = 0f);

        /// <summary>
        /// 在固定世界坐标生成一个世界空间 UI，适合伤害飘字和一次性提示。
        /// </summary>
        WorldUIHandle SpawnWorldUI(string assetAddress, Vector3 worldPosition, float lifetime = 0f);

        /// <summary>在固定世界坐标生成一个投影到屏幕空间 Overlay Canvas 的 UI，适合必须显示在场景模型之上的伤害飘字。</summary>
        WorldUIHandle SpawnScreenSpaceWorldUI(string assetAddress, Vector3 worldPosition, float lifetime = 0f);

        /// <summary>
        /// 主动回收一个世界空间 UI 租约；已经失效或属于其他 UIKit 的句柄会被安全忽略。
        /// </summary>
        void ReleaseWorldUI(WorldUIHandle handle);

        /// <summary>
        /// 指定世界 UI 使用和朝向的相机；传入空时恢复自动查找 MainCamera。
        /// </summary>
        void SetWorldUICamera(Camera camera);
    }

    /// <summary>
    /// 管理 2D 面板类型注册、Prefab 实例、纯 C# 控制器、显示层级，以及多实例世界 UI 的跟随、计时和对象池。
    /// Prefab 资源句柄始终由 AssetKit 缓存和释放，UIKit 只管理使用这些资源创建出来的场景实例。
    /// </summary>
    public sealed class UIKit : Kit, IUIKit
    {
        private const string RootName = "[UIKit]";
        private const string WorldRootName = "[UIKit.World]";
        /// <summary>屏幕空间世界锚点 Canvas 的运行时根节点名称。</summary>
        private const string WorldOverlayRootName = "[UIKit.WorldOverlay]";
        private const int WorldUIPoolCapacityPerAsset = 32;
        /// <summary>使世界锚点飘字位于场景模型之上但低于普通 UIKit 面板。</summary>
        private const int WorldOverlaySortingOrder = -1;
        private const float WorldCanvasScale = 0.01f;
        /// <summary>相机不可用或世界锚点位于相机背后时使用的屏幕外隐藏坐标。</summary>
        private static readonly Vector2 HiddenScreenPosition = new Vector2(-100000f, -100000f);
        private readonly IAssetKit assetKit;
        private readonly Dictionary<Type, UIPanelRecord> panelRecords = new Dictionary<Type, UIPanelRecord>();
        private readonly Dictionary<UIPanelLayer, RectTransform> layerRoots = new Dictionary<UIPanelLayer, RectTransform>();
        private readonly HashSet<Type> openingPanelTypes = new HashSet<Type>();
        private readonly List<WorldUIRecord> activeWorldUIRecords = new List<WorldUIRecord>();
        private readonly Dictionary<string, Stack<WorldUIRecord>> worldUIPools = new Dictionary<string, Stack<WorldUIRecord>>(StringComparer.Ordinal);
        private GameObject rootObject;
        private RectTransform cacheRoot;
        private GameObject worldRootObject;
        private RectTransform worldCanvasRoot;
        private RectTransform worldCacheRoot;
        private Canvas worldCanvas;
        /// <summary>持有独立屏幕空间世界锚点 Canvas 的跨场景根对象。</summary>
        private GameObject worldOverlayRootObject;
        /// <summary>接收世界坐标投影结果和屏幕动画偏移的 Overlay Canvas 根变换。</summary>
        private RectTransform worldOverlayCanvasRoot;
        private Camera worldUICamera;
        private bool isInitialized;
        private bool isDisposed;

        /// <summary>
        /// 创建依赖指定 AssetKit 的 UI 模块；资源系统必须先完成初始化，UIKit 才能打开面板。
        /// </summary>
        /// <param name="assetKit">负责加载并缓存 UI Prefab 的资源模块。</param>
        public UIKit(IAssetKit assetKit)
        {
            this.assetKit = assetKit ?? throw new ArgumentNullException(nameof(assetKit));
            Core.UI = this;
        }

        /// <summary>
        /// 扫描全部已加载程序集中的面板配置，并创建跨场景保留的 UI 根节点和显示层级。
        /// </summary>
        public override void AfterNew()
        {
            ThrowIfDisposed();

            if (isInitialized)
                return;

            UIPanelTypeRegistry.Rebuild();
            CreateRoot();
            CreateWorldRoot();
            CreateWorldOverlayRoot();
            isInitialized = true;
        }

        /// <summary>
        /// 打开指定类型的单例面板；首次打开会同步加载 Prefab、绑定组件并创建纯 C# 控制器。
        /// </summary>
        /// <typeparam name="TPanel">带有 UIPanelConfigAttribute 配置的具体面板类型。</typeparam>
        /// <returns>处于打开状态的面板控制器。</returns>
        public TPanel OpenPanel<TPanel>() where TPanel : UIPanel
        {
            return (TPanel)OpenPanel(typeof(TPanel));
        }

        /// <summary>
        /// 关闭指定类型的面板；重复关闭或关闭尚未创建的面板是安全的空操作。
        /// </summary>
        /// <typeparam name="TPanel">需要关闭的具体面板类型。</typeparam>
        public void ClosePanel<TPanel>() where TPanel : UIPanel
        {
            ClosePanel(typeof(TPanel));
        }

        /// <summary>
        /// 尝试读取已经创建的指定类型面板，不会隐式触发资源加载。
        /// </summary>
        /// <typeparam name="TPanel">需要查询的具体面板类型。</typeparam>
        /// <param name="panel">找到记录时返回对应控制器，否则返回空。</param>
        /// <returns>找到面板记录且控制器类型正确时返回 true。</returns>
        public bool TryGetPanel<TPanel>(out TPanel panel) where TPanel : UIPanel
        {
            if (panelRecords.TryGetValue(typeof(TPanel), out UIPanelRecord record) && record.Controller is TPanel typedPanel)
            {
                panel = typedPanel;
                return true;
            }

            panel = null;
            return false;
        }

        /// <summary>
        /// 判断指定面板记录是否处于打开状态。
        /// </summary>
        /// <typeparam name="TPanel">需要查询的具体面板类型。</typeparam>
        /// <returns>面板处于打开状态时返回 true。</returns>
        public bool IsPanelOpen<TPanel>() where TPanel : UIPanel
        {
            return panelRecords.TryGetValue(typeof(TPanel), out UIPanelRecord record) && record.State == UIPanelState.Open && record.Instance != null && record.Instance.activeSelf;
        }

        /// <summary>
        /// 获取当前正在显示和逐帧更新的世界空间 UI 数量。
        /// </summary>
        public int ActiveWorldUICount => activeWorldUIRecords.Count;

        /// <summary>
        /// 在世界空间生成持续跟随指定目标的 UI，并返回可访问根组件、可选 Binder 和主动回收的租约句柄。
        /// </summary>
        public WorldUIHandle SpawnWorldUI(string assetAddress, Transform followTarget, Vector3 worldOffset, float lifetime = 0f)
        {
            if (followTarget == null)
                throw new ArgumentNullException(nameof(followTarget));

            return SpawnWorldUIInternal(assetAddress, followTarget, followTarget.position, worldOffset, lifetime, true, WorldUIRenderSpace.WorldSpace);
        }

        /// <summary>
        /// 在固定世界坐标生成 UI，并返回可访问根组件、可选 Binder 和主动回收的租约句柄。
        /// </summary>
        public WorldUIHandle SpawnWorldUI(string assetAddress, Vector3 worldPosition, float lifetime = 0f)
        {
            return SpawnWorldUIInternal(assetAddress, null, worldPosition, Vector3.zero, lifetime, false, WorldUIRenderSpace.WorldSpace);
        }

        /// <summary>在固定世界坐标生成一个屏幕空间 Overlay UI，并保留世界坐标更新、生命周期和对象池能力。</summary>
        public WorldUIHandle SpawnScreenSpaceWorldUI(string assetAddress, Vector3 worldPosition, float lifetime = 0f)
        {
            return SpawnWorldUIInternal(assetAddress, null, worldPosition, Vector3.zero, lifetime, false, WorldUIRenderSpace.ScreenSpaceOverlay);
        }

        /// <summary>
        /// 主动回收有效的世界 UI 租约；实例会按资源地址进入对象池等待复用。
        /// </summary>
        public void ReleaseWorldUI(WorldUIHandle handle)
        {
            if (!IsWorldUIHandleValid(handle))
                return;

            ReleaseWorldUIRecord(handle.Record, true);
        }

        /// <summary>
        /// 设置世界 UI Canvas 使用的相机，并立即同步 Canvas 引用和朝向。
        /// </summary>
        public void SetWorldUICamera(Camera camera)
        {
            ThrowIfDisposed();
            worldUICamera = camera;

            if (worldCanvas != null)
                worldCanvas.worldCamera = camera;

            if (worldCanvasRoot != null && camera != null)
                worldCanvasRoot.rotation = camera.transform.rotation;
        }

        /// <summary>
        /// 驱动世界 UI 的相机朝向、目标跟随、固定位置和自动回收计时。
        /// </summary>
        /// <param name="dt">当前帧增量时间。</param>
        public override void OnUpdate(float dt)
        {
            if (!isInitialized || isDisposed)
                return;

            UpdateWorldUI(dt);
        }

        /// <summary>
        /// 关闭并释放全部面板实例，随后销毁 UIKit 根节点；AssetKit 会在 GameCore 的后续逆序释放阶段统一释放资源句柄。
        /// </summary>
        public override void Dispose()
        {
            if (isDisposed)
                return;

            foreach (UIPanelRecord record in new List<UIPanelRecord>(panelRecords.Values))
            {
                try
                {
                    ReleaseRecord(record, true);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            panelRecords.Clear();
            openingPanelTypes.Clear();
            layerRoots.Clear();
            DestroyAllWorldUI();
            DestroyUnityObject(rootObject);
            DestroyUnityObject(worldRootObject);
            DestroyUnityObject(worldOverlayRootObject);
            rootObject = null;
            cacheRoot = null;
            worldRootObject = null;
            worldCanvasRoot = null;
            worldCacheRoot = null;
            worldCanvas = null;
            worldOverlayRootObject = null;
            worldOverlayCanvasRoot = null;
            worldUICamera = null;
            isInitialized = false;
            isDisposed = true;
            UIPanelTypeRegistry.Clear();
        }

        /// <summary>
        /// 供 UIPanel.Close 调用的非泛型关闭入口，确保控制器无需通过反射构造泛型方法。
        /// </summary>
        /// <param name="panelType">需要关闭的具体面板类型。</param>
        internal void ClosePanel(Type panelType)
        {
            EnsureReady();

            if (panelType == null)
                throw new ArgumentNullException(nameof(panelType));

            if (!panelRecords.TryGetValue(panelType, out UIPanelRecord record) || record.State != UIPanelState.Open)
                return;

            try
            {
                record.Controller.InternalClose();
            }
            finally
            {
                if (record.Descriptor.ClosePolicy == UIPanelClosePolicy.Cache)
                {
                    record.Instance.SetActive(false);
                    record.Instance.transform.SetParent(cacheRoot, false);
                    record.State = UIPanelState.Cached;
                }
                else
                {
                    panelRecords.Remove(panelType);
                    ReleaseRecord(record, false);
                }
            }
        }

        /// <summary>
        /// 验证句柄是否仍属于当前 UIKit、对应活动记录并匹配实例的最新复用版本。
        /// </summary>
        internal bool IsWorldUIHandleValid(WorldUIHandle handle)
        {
            return handle != null && handle.Owner == this && handle.Record != null && handle.Record.IsActive && handle.Record.Instance != null && handle.Record.Handle == handle && handle.Record.Version == handle.Version;
        }

        /// <summary>
        /// 将有效世界 UI 切换为跟随目标模式，并立即同步一次世界坐标。
        /// </summary>
        internal void ConfigureWorldUIFollow(WorldUIHandle handle, Transform followTarget, Vector3 worldOffset)
        {
            if (!IsWorldUIHandleValid(handle))
                throw new InvalidOperationException("Cannot configure an invalid world UI handle.");

            if (followTarget == null)
                throw new ArgumentNullException(nameof(followTarget));

            WorldUIRecord record = handle.Record;
            record.FollowTarget = followTarget;
            record.WorldOffset = worldOffset;
            record.IsFollowing = true;
            ApplyWorldUITransform(record);
        }

        /// <summary>
        /// 将有效世界 UI 切换为固定坐标模式，并立即同步新的世界坐标。
        /// </summary>
        internal void ConfigureWorldUIPosition(WorldUIHandle handle, Vector3 worldPosition)
        {
            if (!IsWorldUIHandleValid(handle))
                throw new InvalidOperationException("Cannot configure an invalid world UI handle.");

            WorldUIRecord record = handle.Record;
            record.FollowTarget = null;
            record.FixedWorldPosition = worldPosition;
            record.WorldOffset = Vector3.zero;
            record.IsFollowing = false;
            ApplyWorldUITransform(record);
        }

        /// <summary>更新屏幕空间世界锚点 UI 的投影后像素偏移，并立即刷新其 Canvas 坐标。</summary>
        internal void ConfigureWorldUIScreenOffset(WorldUIHandle handle, Vector2 screenOffset)
        {
            if (!IsWorldUIHandleValid(handle)) throw new InvalidOperationException("Cannot configure an invalid world UI handle.");
            WorldUIRecord record = handle.Record;
            if (record.RenderSpace != WorldUIRenderSpace.ScreenSpaceOverlay) throw new InvalidOperationException("Screen offset can only be configured for a screen-space world UI handle.");
            record.ScreenOffset = screenOffset;
            ApplyWorldUITransform(record);
        }

        /// <summary>
        /// 更新有效世界 UI 的自动回收时间，零表示持续显示直到主动回收或跟随目标销毁。
        /// </summary>
        internal void ConfigureWorldUILifetime(WorldUIHandle handle, float lifetime)
        {
            if (!IsWorldUIHandleValid(handle))
                throw new InvalidOperationException("Cannot configure an invalid world UI handle.");

            ValidateWorldUILifetime(lifetime);
            handle.Record.RemainingLifetime = lifetime;
        }

        /// <summary>
        /// 从指定资源地址的对象池获取或创建实例，并建立一份新的世界 UI 租约。
        /// </summary>
        private WorldUIHandle SpawnWorldUIInternal(string assetAddress, Transform followTarget, Vector3 worldPosition, Vector3 worldOffset, float lifetime, bool isFollowing, WorldUIRenderSpace renderSpace)
        {
            EnsureReady();
            ValidateWorldUIAssetAddress(assetAddress);
            ValidateWorldUILifetime(lifetime);
            WorldUIRecord record = AcquireWorldUIRecord(assetAddress);
            record.Version++;
            record.FollowTarget = followTarget;
            record.FixedWorldPosition = worldPosition;
            record.WorldOffset = worldOffset;
            record.ScreenOffset = Vector2.zero;
            record.RemainingLifetime = lifetime;
            record.IsFollowing = isFollowing;
            record.IsActive = true;
            record.RenderSpace = renderSpace;
            WorldUIHandle handle = new WorldUIHandle(this, record, record.Version);
            record.Handle = handle;
            Transform instanceTransform = record.Instance.transform;
            instanceTransform.SetParent(ResolveWorldUIParent(renderSpace), false);
            instanceTransform.localScale = Vector3.one;
            instanceTransform.localRotation = Quaternion.identity;
            if (renderSpace == WorldUIRenderSpace.ScreenSpaceOverlay && instanceTransform is RectTransform screenRectTransform)
            {
                screenRectTransform.anchorMin = Vector2.one * 0.5f;
                screenRectTransform.anchorMax = Vector2.one * 0.5f;
                screenRectTransform.anchoredPosition3D = Vector3.zero;
            }
            instanceTransform.SetAsLastSibling();
            ApplyWorldUITransform(record);
            activeWorldUIRecords.Add(record);
            record.Instance.SetActive(true);
            return handle;
        }

        /// <summary>
        /// 优先复用指定资源地址的缓存记录，没有有效缓存时通过 AssetKit 创建新的 Prefab 实例。
        /// </summary>
        private WorldUIRecord AcquireWorldUIRecord(string assetAddress)
        {
            if (!worldUIPools.TryGetValue(assetAddress, out Stack<WorldUIRecord> pool))
            {
                pool = new Stack<WorldUIRecord>();
                worldUIPools.Add(assetAddress, pool);
            }

            while (pool.Count > 0)
            {
                WorldUIRecord cachedRecord = pool.Pop();
                if (cachedRecord.Instance != null)
                    return cachedRecord;
            }

            GameObject instance = assetKit.InstantiateSync(assetAddress, worldCanvasRoot, false, false);
            if (!(instance.transform is RectTransform))
            {
                DestroyUnityObject(instance);
                throw new InvalidOperationException($"World UI prefab '{assetAddress}' must use RectTransform on its root object.");
            }

            instance.TryGetComponent<UIComponentBinder>(out UIComponentBinder binder);
            return new WorldUIRecord(assetAddress, instance, binder);
        }

        /// <summary>
        /// 每帧更新世界 Canvas 朝向、世界锚点投影、所有跟随实例位置和有限生命周期实例的回收计时。
        /// </summary>
        private void UpdateWorldUI(float dt)
        {
            RefreshWorldUICamera();

            if (worldCanvasRoot != null && worldUICamera != null)
                worldCanvasRoot.rotation = worldUICamera.transform.rotation;

            float safeDeltaTime = Mathf.Max(0f, dt);
            for (int index = activeWorldUIRecords.Count - 1; index >= 0; index--)
            {
                WorldUIRecord record = activeWorldUIRecords[index];
                if (record.Instance == null)
                {
                    ReleaseWorldUIRecord(record, false);
                    continue;
                }

                if (record.IsFollowing && record.FollowTarget == null)
                {
                    ReleaseWorldUIRecord(record, true);
                    continue;
                }

                ApplyWorldUITransform(record);
                if (record.RemainingLifetime <= 0f)
                    continue;

                record.RemainingLifetime -= safeDeltaTime;
                if (record.RemainingLifetime <= 0f)
                    ReleaseWorldUIRecord(record, true);
            }
        }

        /// <summary>
        /// 根据跟随或固定模式解析世界坐标，并按记录的渲染空间应用世界变换或屏幕投影。
        /// </summary>
        private void ApplyWorldUITransform(WorldUIRecord record)
        {
            if (record == null || record.Instance == null)
                return;

            Vector3 worldPosition = record.IsFollowing && record.FollowTarget != null ? record.FollowTarget.position + record.WorldOffset : record.FixedWorldPosition + record.WorldOffset;
            if (record.RenderSpace == WorldUIRenderSpace.ScreenSpaceOverlay)
            {
                ApplyScreenSpaceWorldUITransform(record, worldPosition);
                return;
            }

            Transform instanceTransform = record.Instance.transform;
            instanceTransform.position = worldPosition;

            if (worldCanvasRoot != null)
                instanceTransform.rotation = worldCanvasRoot.rotation;
        }

        /// <summary>把世界坐标投影到独立 Overlay Canvas，并在目标位于相机背后时移动到不可见区域。</summary>
        private void ApplyScreenSpaceWorldUITransform(WorldUIRecord record, Vector3 worldPosition)
        {
            if (!(record.Instance.transform is RectTransform rectTransform)) return;
            if (worldUICamera == null || worldOverlayCanvasRoot == null)
            {
                rectTransform.anchoredPosition = HiddenScreenPosition;
                return;
            }

            Vector3 screenPosition = worldUICamera.WorldToScreenPoint(worldPosition);
            if (screenPosition.z <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(worldOverlayCanvasRoot, screenPosition, null, out Vector2 canvasPosition))
            {
                rectTransform.anchoredPosition = HiddenScreenPosition;
                return;
            }

            rectTransform.anchoredPosition = canvasPosition + record.ScreenOffset;
            rectTransform.localRotation = Quaternion.identity;
        }

        /// <summary>根据渲染空间返回实例应该挂接的 World Space 或 Screen Space Overlay Canvas 根节点。</summary>
        private RectTransform ResolveWorldUIParent(WorldUIRenderSpace renderSpace)
        {
            RectTransform parent = renderSpace == WorldUIRenderSpace.ScreenSpaceOverlay ? worldOverlayCanvasRoot : worldCanvasRoot;
            if (parent == null) throw new InvalidOperationException($"World UI render root '{renderSpace}' has not been created.");
            return parent;
        }

        /// <summary>
        /// 在相机引用丢失时自动重新查找 MainCamera，并同步到世界空间 Canvas。
        /// </summary>
        private void RefreshWorldUICamera()
        {
            if (worldUICamera == null)
                worldUICamera = Camera.main;

            if (worldCanvas != null && worldCanvas.worldCamera != worldUICamera)
                worldCanvas.worldCamera = worldUICamera;
        }

        /// <summary>
        /// 使活动记录失效并按容量决定进入对象池或直接销毁实例。
        /// </summary>
        private void ReleaseWorldUIRecord(WorldUIRecord record, bool allowPooling)
        {
            if (record == null || !record.IsActive)
                return;

            activeWorldUIRecords.Remove(record);
            record.IsActive = false;
            record.FollowTarget = null;
            record.IsFollowing = false;
            record.ScreenOffset = Vector2.zero;
            record.RemainingLifetime = 0f;
            WorldUIHandle handle = record.Handle;
            record.Handle = null;
            handle?.Invalidate();

            if (record.Instance == null)
                return;

            record.Instance.SetActive(false);
            if (allowPooling && worldUIPools.TryGetValue(record.AssetAddress, out Stack<WorldUIRecord> pool) && pool.Count < WorldUIPoolCapacityPerAsset)
            {
                record.Instance.transform.SetParent(worldCacheRoot, false);
                pool.Push(record);
                return;
            }

            DestroyUnityObject(record.Instance);
        }

        /// <summary>
        /// 销毁全部活动和缓存世界 UI，并清空对象池；该流程只在 UIKit 最终释放时执行。
        /// </summary>
        private void DestroyAllWorldUI()
        {
            for (int index = activeWorldUIRecords.Count - 1; index >= 0; index--)
                ReleaseWorldUIRecord(activeWorldUIRecords[index], false);

            activeWorldUIRecords.Clear();
            foreach (Stack<WorldUIRecord> pool in worldUIPools.Values)
            {
                while (pool.Count > 0)
                    DestroyUnityObject(pool.Pop().Instance);
            }

            worldUIPools.Clear();
        }

        /// <summary>
        /// 校验世界 UI 资源地址，避免 AssetKit 在更深层抛出缺少调用上下文的错误。
        /// </summary>
        private static void ValidateWorldUIAssetAddress(string assetAddress)
        {
            if (string.IsNullOrWhiteSpace(assetAddress))
                throw new ArgumentException("World UI asset address cannot be empty.", nameof(assetAddress));
        }

        /// <summary>
        /// 校验自动回收时间，禁止负数、非数字和无穷值进入逐帧计时。
        /// </summary>
        private static void ValidateWorldUILifetime(float lifetime)
        {
            if (lifetime < 0f || float.IsNaN(lifetime) || float.IsInfinity(lifetime))
                throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "World UI lifetime must be a finite value greater than or equal to zero.");
        }

        /// <summary>
        /// 执行非泛型面板打开流程，并处理首次创建、缓存恢复和重复打开三种状态。
        /// </summary>
        private UIPanel OpenPanel(Type panelType)
        {
            EnsureReady();

            if (panelRecords.TryGetValue(panelType, out UIPanelRecord existingRecord))
            {
                if (existingRecord.State == UIPanelState.Open)
                    return existingRecord.Controller;

                if (existingRecord.State == UIPanelState.Cached)
                    return ReopenCachedPanel(existingRecord);

                throw new InvalidOperationException($"Panel '{panelType.FullName}' is currently in state '{existingRecord.State}' and cannot be opened again.");
            }

            if (!openingPanelTypes.Add(panelType))
                throw new InvalidOperationException($"Panel '{panelType.FullName}' is already being opened recursively.");

            GameObject instance = null;
            UIPanel controller = null;
            try
            {
                UIPanelDescriptor descriptor = UIPanelTypeRegistry.Get(panelType);
                RectTransform layerRoot = GetLayerRoot(descriptor.Layer);
                instance = assetKit.InstantiateSync(descriptor.AssetAddress, layerRoot, false, false);
                instance.name = panelType.Name;
                StretchToParent(instance.transform as RectTransform);
                if (!instance.TryGetComponent<UIComponentBinder>(out var binder))
                    throw new InvalidOperationException($"UI prefab '{descriptor.AssetAddress}' must contain exactly one {nameof(UIComponentBinder)} on its root object.");

                controller = descriptor.CreateController();
                controller.InternalAttach(this, instance, binder);
                controller.InternalBind();
                controller.InternalInitialize();
                UIPanelRecord record = new UIPanelRecord(descriptor, controller, instance, binder, UIPanelState.Opening);
                panelRecords.Add(panelType, record);
                instance.SetActive(true);
                controller.InternalOpen();
                record.State = UIPanelState.Open;
                return controller;
            }
            catch
            {
                panelRecords.Remove(panelType);

                if (controller != null)
                    controller.InternalRelease();

                DestroyUnityObject(instance);
                throw;
            }
            finally
            {
                openingPanelTypes.Remove(panelType);
            }
        }

        /// <summary>
        /// 将缓存层中的面板实例重新放入原显示层，并再次触发打开生命周期。
        /// </summary>
        private UIPanel ReopenCachedPanel(UIPanelRecord record)
        {
            RectTransform layerRoot = GetLayerRoot(record.Descriptor.Layer);
            record.Instance.transform.SetParent(layerRoot, false);
            StretchToParent(record.Instance.transform as RectTransform);
            record.Instance.SetActive(true);
            try
            {
                record.Controller.InternalOpen();
                record.State = UIPanelState.Open;
                return record.Controller;
            }
            catch
            {
                record.Instance.SetActive(false);
                record.Instance.transform.SetParent(cacheRoot, false);
                record.State = UIPanelState.Cached;
                throw;
            }
        }

        /// <summary>
        /// 释放一条面板记录持有的控制器和实例；资源句柄仍由 AssetKit 统一持有。
        /// </summary>
        private static void ReleaseRecord(UIPanelRecord record, bool closeFirst)
        {
            if (closeFirst && record.Controller.IsOpen)
                record.Controller.InternalClose();

            record.Controller.InternalRelease();
            record.State = UIPanelState.Disposed;
            DestroyUnityObject(record.Instance);
        }

        /// <summary>
        /// 创建屏幕空间 UI 根节点、按顺序排列的显示层以及默认禁用的缓存层。
        /// </summary>
        private void CreateRoot()
        {
            rootObject = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(rootObject);
            Canvas canvas = rootObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            CanvasScaler scaler = rootObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            layerRoots.Add(UIPanelLayer.Background, CreateLayer(nameof(UIPanelLayer.Background), true));
            layerRoots.Add(UIPanelLayer.Normal, CreateLayer(nameof(UIPanelLayer.Normal), true));
            layerRoots.Add(UIPanelLayer.Popup, CreateLayer(nameof(UIPanelLayer.Popup), true));
            layerRoots.Add(UIPanelLayer.Overlay, CreateLayer(nameof(UIPanelLayer.Overlay), true));
            cacheRoot = CreateLayer("Cache", false);
        }

        /// <summary>
        /// 创建独立于屏幕空间面板的 World Space Canvas，并建立默认禁用的世界 UI 对象池节点。
        /// Canvas 使用百分之一缩放，使常见 UI 像素尺寸能够自然映射到合理的世界尺寸。
        /// </summary>
        private void CreateWorldRoot()
        {
            worldRootObject = new GameObject(WorldRootName, typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(worldRootObject);
            int uiLayer = LayerMask.NameToLayer("UI");
            worldRootObject.layer = uiLayer >= 0 ? uiLayer : 0;
            worldCanvasRoot = worldRootObject.GetComponent<RectTransform>();
            worldCanvasRoot.sizeDelta = new Vector2(1920f, 1080f);
            worldCanvasRoot.position = Vector3.zero;
            worldCanvasRoot.localScale = Vector3.one * WorldCanvasScale;
            worldUICamera = worldUICamera != null ? worldUICamera : Camera.main;
            worldCanvas = worldRootObject.GetComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.worldCamera = worldUICamera;
            worldCanvas.sortingOrder = 0;

            if (worldUICamera != null)
                worldCanvasRoot.rotation = worldUICamera.transform.rotation;

            GameObject cacheObject = new GameObject("Cache", typeof(RectTransform));
            cacheObject.layer = worldRootObject.layer;
            worldCacheRoot = cacheObject.GetComponent<RectTransform>();
            worldCacheRoot.SetParent(worldCanvasRoot, false);
            StretchToParent(worldCacheRoot);
            cacheObject.SetActive(false);
        }

        /// <summary>创建独立于静态面板和世界空间血条的 Screen Space Overlay Canvas，专门承载由世界坐标投影的伤害飘字。</summary>
        private void CreateWorldOverlayRoot()
        {
            worldOverlayRootObject = new GameObject(WorldOverlayRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.Object.DontDestroyOnLoad(worldOverlayRootObject);
            int uiLayer = LayerMask.NameToLayer("UI");
            worldOverlayRootObject.layer = uiLayer >= 0 ? uiLayer : 0;
            worldOverlayCanvasRoot = worldOverlayRootObject.GetComponent<RectTransform>();
            Canvas overlayCanvas = worldOverlayRootObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = WorldOverlaySortingOrder;
            CanvasScaler scaler = worldOverlayRootObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        /// <summary>
        /// 在 UIKit 根节点下创建一个全屏 RectTransform 层。
        /// </summary>
        private RectTransform CreateLayer(string layerName, bool isActive)
        {
            GameObject layerObject = new GameObject(layerName, typeof(RectTransform));
            RectTransform layerTransform = layerObject.GetComponent<RectTransform>();
            layerTransform.SetParent(rootObject.transform, false);
            StretchToParent(layerTransform);
            layerObject.SetActive(isActive);
            return layerTransform;
        }

        /// <summary>
        /// 获取指定显示层，并在配置无效时提供明确异常。
        /// </summary>
        private RectTransform GetLayerRoot(UIPanelLayer layer)
        {
            if (!layerRoots.TryGetValue(layer, out RectTransform layerRoot))
                throw new InvalidOperationException($"UIKit does not contain a root for panel layer '{layer}'.");

            return layerRoot;
        }

        /// <summary>
        /// 将 RectTransform 重置为铺满父节点，保证面板 Prefab 在所有 UIKit 层中使用相同布局基准。
        /// </summary>
        private static void StretchToParent(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return;

            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// 根据当前运行环境选择延迟销毁或立即销毁 Unity 对象。
        /// </summary>
        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>
        /// 确保 UIKit 已完成 AfterNew 初始化并且尚未释放。
        /// </summary>
        private void EnsureReady()
        {
            ThrowIfDisposed();

            if (!isInitialized)
                throw new InvalidOperationException("UIKit is not initialized. Let GameCore complete Initialize() before opening panels.");
        }

        /// <summary>
        /// 阻止已经释放的 UIKit 被重新初始化或访问。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(UIKit));
        }

        /// <summary>
        /// 保存一个面板控制器、Prefab 实例、Binder、配置和当前状态之间的一一对应关系。
        /// </summary>
        private sealed class UIPanelRecord
        {
            /// <summary>
            /// 创建一条完整面板记录。
            /// </summary>
            public UIPanelRecord(UIPanelDescriptor descriptor, UIPanel controller, GameObject instance, UIComponentBinder binder, UIPanelState state)
            {
                Descriptor = descriptor;
                Controller = controller;
                Instance = instance;
                Binder = binder;
                State = state;
            }

            public UIPanelDescriptor Descriptor { get; }
            public UIPanel Controller { get; }
            public GameObject Instance { get; }
            public UIComponentBinder Binder { get; }
            public UIPanelState State { get; set; }
        }
    }
}
