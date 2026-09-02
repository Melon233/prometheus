using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 世界大地图面板：使用 WorldSystem 提供的静态地图和 POI 数据，在独立视口中支持拖拽、滚轮缩放和标记显示。
    /// 地图资源缺失时保留可打开的空面板，待编辑器拍摄工具生成 WorldMapDefinition 后自动显示纹理。
    /// </summary>
    [UIPanelConfig("MapPanel", UIPanelLayer.Normal, UIPanelClosePolicy.Destroy)]
    public sealed class MapPanel : MapPanelBase
    {
        /// <summary>地图视口边距，避免地图内容贴住全屏边界。</summary>
        private const float ViewportPadding = 0f;

        /// <summary>大地图允许的最小缩放倍数，1 倍表示地图适配视口。</summary>
        private const float MinimumZoom = 1f;

        /// <summary>大地图允许的最大缩放倍数。</summary>
        private const float MaximumZoom = 10f;

        /// <summary>保存当前玩法世界地图数据源。</summary>
        private IWorldSystem worldSystem;

        /// <summary>裁剪地图内容的全屏视口。</summary>
        private RectTransform viewport;

        /// <summary>承载地图纹理和所有 POI 标记的可平移缩放内容节点。</summary>
        private RectTransform mapContent;

        /// <summary>显示静态地图纹理的 RawImage。</summary>
        private RawImage mapImage;

        /// <summary>承载随地图一起移动的 POI 和玩家标记；标记在创建和缩放时应用逆缩放。</summary>
        private RectTransform markerRoot;

        /// <summary>保存由 MapPanel Prefab 固定绑定的关闭按钮。</summary>
        private Button closeButton;

        /// <summary>保存最近一次 WorldSystem 发布的玩家位置。</summary>
        private Vector3 playerPosition;

        /// <summary>标记是否已经接收过玩家位置。</summary>
        private bool hasPlayerPosition;

        /// <summary>当前地图内容缩放倍数；实际值从 WorldSystem 的单局缓存读取。</summary>
        private float zoom = MinimumZoom;

        /// <summary>首次创建面板时建立地图视口、地图内容和输入接收器。</summary>
        protected override void OnInitialize()
        {
            BuildMapView();
        }

        /// <summary>打开面板时解析 WorldSystem、订阅地图事实并立即重放当前地图状态。</summary>
        protected override void OnOpen()
        {
            if (!Core.Gameplay.TryGetSystem(out worldSystem)) throw new InvalidOperationException($"{nameof(MapPanel)} requires {nameof(IWorldSystem)}.");
            Core.Event.AddListener<WorldMapReadyEvent>(Event.WorldMapReady, OnWorldMapReady);
            Core.Event.AddListener<WorldMapPoiChangedEvent>(Event.WorldMapPoiChanged, OnWorldMapPoiChanged);
            zoom = Mathf.Clamp(worldSystem.MapZoom, MinimumZoom, MaximumZoom);
            if (worldSystem.TryGetPlayerPosition(out Vector3 currentPosition))
            {
                playerPosition = currentPosition;
                hasPlayerPosition = true;
            }
            RefreshMap();
            CenterOnPlayer();
        }

        /// <summary>关闭面板时解除地图事件监听，避免面板销毁后继续接收 WorldSystem 通知。</summary>
        protected override void OnClose()
        {
            UnsubscribeMapEvents();
        }

        /// <summary>响应生成基类绑定的左上角关闭按钮。</summary>
        protected override void OnCloseButtonClick()
        {
            Close();
        }

        /// <summary>最终释放时再次解除监听并销毁动态创建的 UI 对象。</summary>
        protected override void OnUnbind()
        {
            UnsubscribeMapEvents();
            if (markerRoot != null) DestroyUiObject(markerRoot.gameObject);
            closeButton = null;
            viewport = null;
            mapContent = null;
            mapImage = null;
            markerRoot = null;
            worldSystem = null;
            hasPlayerPosition = false;
        }

        /// <summary>读取地图模板中的固定视口、地图内容和关闭按钮，并挂接运行时输入接收器。</summary>
        private void BuildMapView()
        {
            Image backdrop = Root.GetComponentInChildren<Image>(true);
            if (backdrop != null) backdrop.color = new Color(0.015f, 0.02f, 0.03f, 0.86f);
            mapImage = MapImage;
            closeButton = CloseButton;
            RectTransform contentTransform = mapImage.transform.parent as RectTransform;
            if (contentTransform == null) throw new InvalidOperationException("MapPanel MapImage must be a child of a RectTransform map content node.");
            mapContent = contentTransform;
            viewport = mapContent.parent as RectTransform;
            if (viewport == null) throw new InvalidOperationException("MapPanel map content must be a child of the map viewport.");
            if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
            MapPanelInput input = viewport.GetComponent<MapPanelInput>() ?? viewport.gameObject.AddComponent<MapPanelInput>();
            input.Initialize(this);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(ViewportPadding, ViewportPadding);
            viewport.offsetMax = new Vector2(-ViewportPadding, -ViewportPadding);
            mapContent.anchorMin = new Vector2(0.5f, 0.5f);
            mapContent.anchorMax = new Vector2(0.5f, 0.5f);
            mapContent.pivot = new Vector2(0.5f, 0.5f);
            mapContent.anchoredPosition = Vector2.zero;
            mapImage.raycastTarget = false;
            mapImage.color = Color.white;
            GameObject markerObject = new GameObject("Map Markers", typeof(RectTransform));
            markerRoot = markerObject.GetComponent<RectTransform>();
            markerRoot.SetParent(mapContent, false);
            markerRoot.anchorMin = Vector2.zero;
            markerRoot.anchorMax = Vector2.one;
            markerRoot.offsetMin = Vector2.zero;
            markerRoot.offsetMax = Vector2.zero;
            Image closeImage = closeButton.GetComponent<Image>();
            if (closeImage == null) throw new InvalidOperationException("MapPanel CloseButton must have an Image component.");
            closeImage.sprite = WorldMapIconCatalog.LoadCloseIcon();
            closeImage.preserveAspect = true;
            closeImage.color = Color.white;
            closeButton.targetGraphic = closeImage;
            RectTransform buttonRect = closeButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(0f, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.anchoredPosition = new Vector2(24f, -24f);
            buttonRect.sizeDelta = new Vector2(64f, 64f);
        }

        /// <summary>刷新地图纹理、地图内容比例和全部 POI 标记。</summary>
        private void RefreshMap()
        {
            if (mapImage == null || worldSystem == null) return;
            mapImage.texture = worldSystem.MapTexture;
            float aspect = worldSystem.MapWorldLength > 0f && worldSystem.MapWorldWidth > 0f ? worldSystem.MapWorldLength / worldSystem.MapWorldWidth : 1f;
            Vector2 viewportSize = viewport.rect.size;
            float viewportWidth = viewportSize.x > 0f ? viewportSize.x : 1920f;
            float viewportHeight = viewportSize.y > 0f ? viewportSize.y : 1080f;
            float width = Mathf.Max(viewportWidth, viewportHeight * aspect);
            float height = width / aspect;
            mapContent.sizeDelta = new Vector2(width, height);
            mapContent.localScale = Vector3.one * zoom;
            ClampMapContentPosition();
            RebuildMarkers();
        }

        /// <summary>首次打开地图时把玩家的归一化坐标放到视口中心；该方法不会在后续位置事件中调用，避免覆盖用户拖动结果。</summary>
        private void CenterOnPlayer()
        {
            if (!hasPlayerPosition || viewport == null || mapContent == null || worldSystem.MapTexture == null) return;
            Vector2 playerUv = worldSystem.WorldToMapNormalized(playerPosition);
            Vector2 scaledMapSize = mapContent.rect.size * zoom;
            mapContent.anchoredPosition = new Vector2((0.5f - playerUv.x) * scaledMapSize.x, (0.5f - playerUv.y) * scaledMapSize.y);
            ClampMapContentPosition();
        }

        /// <summary>重新创建地图上的玩家和 POI 标记，标记位置统一通过 WorldSystem 的地图坐标接口换算。</summary>
        private void RebuildMarkers()
        {
            ClearMarkers();
            if (worldSystem.MapTexture == null) return;
            if (hasPlayerPosition) CreateMarker("Player", WorldMapIconCatalog.LoadPlayerIcon(), worldSystem.WorldToMapNormalized(playerPosition), new Vector2(40f, 40f), false, null);
            for (int index = 0; index < worldSystem.AllPois.Count; index++)
            {
                PoiEntity poi = worldSystem.AllPois[index];
                if (poi == null || poi.Config == null || poi.IsConsumed) continue;
                bool teleportable = poi.Config.PoiType == PoiType.Statue || poi.Config.PoiType == PoiType.TeleAnchor;
                // POI 根节点可能挂在带有场景偏移的父节点下；运行时实际 Transform 才是静态地图拍摄所使用的权威世界坐标。
                CreateMarker(poi.Config.Id, WorldMapIconCatalog.LoadPoiIcon(poi.Config.PoiType), worldSystem.WorldToMapNormalized(poi.bindGo.transform.position), new Vector2(36f, 36f), teleportable, poi.Config.Id);
            }
        }

        /// <summary>创建一个使用归一化锚点定位的地图标记；神像和传送锚点额外绑定传送点击回调。</summary>
        private void CreateMarker(string markerName, Sprite sprite, Vector2 normalizedPosition, Vector2 size, bool teleportable, string poiId)
        {
            GameObject markerObject = new GameObject(markerName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            markerObject.transform.SetParent(markerRoot, false);
            RectTransform markerRect = markerObject.GetComponent<RectTransform>();
            markerRect.anchorMin = normalizedPosition;
            markerRect.anchorMax = normalizedPosition;
            markerRect.sizeDelta = size;
            markerRect.anchoredPosition = Vector2.zero;
            Image markerImage = markerObject.GetComponent<Image>();
            markerImage.sprite = sprite;
            markerImage.preserveAspect = sprite != null;
            markerImage.color = Color.white;
            markerImage.raycastTarget = teleportable;
            markerRect.localScale = Vector3.one * WorldMapUiMath.CalculateMarkerInverseScale(zoom);
            if (teleportable)
            {
                Button markerButton = markerObject.AddComponent<Button>();
                markerButton.targetGraphic = markerImage;
                markerButton.onClick.AddListener(() => OnTeleportMarkerClick(poiId));
            }
        }

        /// <summary>删除地图内容节点下现有的全部标记。</summary>
        private void ClearMarkers()
        {
            if (markerRoot == null) return;
            for (int index = markerRoot.childCount - 1; index >= 0; index--) DestroyUiObject(markerRoot.GetChild(index).gameObject);
        }

        /// <summary>解除三个地图事件监听。</summary>
        private void UnsubscribeMapEvents()
        {
            Core.Event.RemoveListener<WorldMapReadyEvent>(Event.WorldMapReady, OnWorldMapReady);
            Core.Event.RemoveListener<WorldMapPoiChangedEvent>(Event.WorldMapPoiChanged, OnWorldMapPoiChanged);
        }

        /// <summary>地图资源变化时重新绑定纹理并重建标记。</summary>
        private void OnWorldMapReady(WorldMapReadyEvent eventData)
        {
            RefreshMap();
        }

        /// <summary>POI 状态变化时重新读取 WorldSystem 的 POI 集合。</summary>
        private void OnWorldMapPoiChanged(WorldMapPoiChangedEvent eventData)
        {
            RefreshMap();
        }

        /// <summary>面板打开期间每帧读取当前玩家实体位置，替代额外 MonoBehaviour 的更新脚本。</summary>
        protected override void OnUpdate(float dt)
        {
            UpdatePlayerPosition();
        }

        /// <summary>响应地图视口的拖拽输入。</summary>
        private void OnMapDragged(Vector2 delta)
        {
            mapContent.anchoredPosition += delta;
            ClampMapContentPosition();
        }

        /// <summary>响应鼠标滚轮或触控板滚动输入，并以当前指针所在地图点作为缩放锚点。</summary>
        private void OnMapScrolled(float delta, Vector2 screenPosition, Camera eventCamera)
        {
            float previousZoom = zoom;
            float nextZoom = Mathf.Clamp(zoom + delta * 0.1f, MinimumZoom, MaximumZoom);
            if (Mathf.Approximately(previousZoom, nextZoom)) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPosition, eventCamera, out Vector2 viewportPoint);
            Vector2 mapPoint = (viewportPoint - mapContent.anchoredPosition) / previousZoom;
            zoom = nextZoom;
            worldSystem.MapZoom = zoom;
            mapContent.localScale = Vector3.one * zoom;
            mapContent.anchoredPosition = viewportPoint - mapPoint * zoom;
            UpdateMarkerScales();
            ClampMapContentPosition();
        }

        /// <summary>面板打开期间每帧读取当前玩家实体坐标并更新玩家地图标记，不使用高频全局位置事件。</summary>
        private void UpdatePlayerPosition()
        {
            if (worldSystem == null || !worldSystem.TryGetPlayerPosition(out Vector3 currentPosition)) return;
            if (hasPlayerPosition && playerPosition == currentPosition) return;
            playerPosition = currentPosition;
            hasPlayerPosition = true;
            RebuildMarkers();
        }

        /// <summary>重新应用所有地图标记的逆缩放，避免滚轮缩放改变图标屏幕尺寸。</summary>
        private void UpdateMarkerScales()
        {
            if (markerRoot == null) return;
            float inverseScale = WorldMapUiMath.CalculateMarkerInverseScale(zoom);
            for (int index = 0; index < markerRoot.childCount; index++) markerRoot.GetChild(index).localScale = Vector3.one * inverseScale;
        }

        /// <summary>限制地图内容中心在地图尺寸的一半范围内；地图小于视口时保持居中。</summary>
        private void ClampMapContentPosition()
        {
            if (viewport == null || mapContent == null) return;
            Vector2 viewportSize = viewport.rect.size;
            Vector2 contentSize = Vector2.Scale(mapContent.rect.size, new Vector2(zoom, zoom));
            float maxX = contentSize.x >= viewportSize.x ? contentSize.x * 0.5f : 0f;
            float maxY = contentSize.y >= viewportSize.y ? contentSize.y * 0.5f : 0f;
            mapContent.anchoredPosition = new Vector2(Mathf.Clamp(mapContent.anchoredPosition.x, -maxX, maxX), Mathf.Clamp(mapContent.anchoredPosition.y, -maxY, maxY));
        }

        /// <summary>点击神像或传送锚点图标后请求 WorldSystem 执行传送，成功后关闭大地图。</summary>
        private void OnTeleportMarkerClick(string poiId)
        {
            if (worldSystem.TryTeleportToPoi(poiId)) Close();
        }

        /// <summary>按当前运行环境销毁动态创建的地图 UI 对象。</summary>
        private static void DestroyUiObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>接收视口拖拽和滚轮事件，并转发给纯 C# MapPanel 控制器。</summary>
        private sealed class MapPanelInput : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
        {
            private MapPanel owner;

            /// <summary>绑定当前动态输入节点对应的面板控制器。</summary>
            public void Initialize(MapPanel panel)
            {
                owner = panel ?? throw new ArgumentNullException(nameof(panel));
            }

            /// <summary>开始拖拽时无需额外状态，接口实现用于接收 Unity EventSystem 的指针流。</summary>
            public void OnBeginDrag(PointerEventData eventData)
            {
            }

            /// <summary>把当前帧指针位移传给地图控制器。</summary>
            public void OnDrag(PointerEventData eventData)
            {
                owner.OnMapDragged(eventData.delta);
            }

            /// <summary>结束拖拽时无需额外状态。</summary>
            public void OnEndDrag(PointerEventData eventData)
            {
            }

            /// <summary>把滚轮增量传给地图控制器。</summary>
            public void OnScroll(PointerEventData eventData)
            {
                owner.OnMapScrolled(eventData.scrollDelta.y, eventData.position, eventData.pressEventCamera);
            }
        }
    }
}
