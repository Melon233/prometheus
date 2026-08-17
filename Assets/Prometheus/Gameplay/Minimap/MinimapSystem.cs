using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>在单局初始化时俯拍一次活动场景，并持续把当前玩家位置映射到固定朝北且不处理旋转的小地图视图。</summary>
    public sealed class MinimapSystem : XSystem
    {
        /// <summary>定义场景俯拍纹理的正方形边长，地图只在系统初始化时渲染一次。</summary>
        private const int CaptureResolution = 1024;

        /// <summary>定义 HUD 一次显示整张地图的比例，数值越小代表地图放大倍数越高。</summary>
        private const float ViewportWorldFraction = 0.35f;

        /// <summary>定义俯拍范围相对场景几何包围盒的额外边距，避免边缘建筑贴住地图纹理边界。</summary>
        private const float CaptureBoundsPadding = 1.1f;

        /// <summary>缓存 MaskUIShader 中用于把 RawImage 地图 UV 还原为局部零到一遮罩 UV 的属性编号。</summary>
        private static readonly int MaskUvTransformId = Shader.PropertyToID("_MaskUvTransform");

        /// <summary>保存当前单局的实体查询入口。</summary>
        private EntitySystem entitySystem;

        /// <summary>保存小队切换事件所在的全局事件总线。</summary>
        private IEventKit eventKit;

        /// <summary>保存初始化时生成的一次性场景俯拍纹理。</summary>
        private RenderTexture mapTexture;

        /// <summary>保存正方形地图在世界 XZ 平面上的中心。</summary>
        private Vector2 mapWorldCenter;

        /// <summary>保存正方形地图覆盖的世界边长。</summary>
        private float mapWorldSize;

        /// <summary>保存当前上场角色的根节点，只用于逐帧读取世界位置。</summary>
        private Transform activePlayerTransform;

        /// <summary>保存当前绑定的小地图 RawImage；HUD 关闭时解除引用。</summary>
        private RawImage boundMapImage;

        /// <summary>保存当前小地图实例独占的径向虚化材质。</summary>
        private Material boundMaskMaterial;

        /// <summary>标记系统已经释放，阻止缓存 HUD 再次绑定失效纹理。</summary>
        private bool isDisposed;

        /// <summary>获取当前单局初始化时生成的静态场景俯拍纹理。</summary>
        public RenderTexture MapTexture => mapTexture;

        /// <summary>解析系统依赖、生成俯拍纹理，并在初始成员发布前订阅小队切换事件。</summary>
        /// <param name="gameplayKit">持有当前小地图系统的单局玩法世界。</param>
        public override void AfterNew(IGameplayKit gameplayKit)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(MinimapSystem));
            if (gameplayKit == null) throw new ArgumentNullException(nameof(gameplayKit));
            entitySystem = gameplayKit.GetSystem<EntitySystem>();
            eventKit = Core.Event ?? throw new InvalidOperationException("MinimapSystem requires EventKit.");
            CaptureActiveScene();
            eventKit.AddListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, OnActiveTeamMemberChanged);
        }

        /// <summary>在角色完成当帧移动后更新地图 UV 和圆心玩家点，地图与玩家点都不处理角色旋转。</summary>
        /// <param name="dt">当前帧增量时间；静态地图映射不依赖时间积分。</param>
        public override void OnUpdate(float dt)
        {
            if (boundMapImage == null) return;
            UpdateBoundView();
        }

        /// <summary>把 HUD 创建的小地图图层和独占虚化材质绑定到当前系统。</summary>
        /// <param name="mapImage">承载俯拍纹理并通过 uvRect 平移的 RawImage。</param>
        /// <param name="maskMaterial">使用 MaskUIShader 的小地图独占径向虚化材质。</param>
        public void BindView(RawImage mapImage, Material maskMaterial)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(MinimapSystem));
            if (mapImage == null) throw new ArgumentNullException(nameof(mapImage));
            if (maskMaterial == null) throw new ArgumentNullException(nameof(maskMaterial));
            if (!maskMaterial.HasProperty(MaskUvTransformId)) throw new ArgumentException("Minimap mask material must use MaskUIShader and expose _MaskUvTransform.", nameof(maskMaterial));
            if (boundMapImage != null && boundMapImage != mapImage) throw new InvalidOperationException("MinimapSystem already owns another HUD view.");
            boundMapImage = mapImage;
            boundMaskMaterial = maskMaterial;
            boundMapImage.texture = mapTexture;
            UpdateBoundView();
        }

        /// <summary>解除指定 HUD 小地图视图；重复解除已经释放的同一生命周期不会产生额外副作用。</summary>
        /// <param name="mapImage">请求解除的 RawImage。</param>
        public void UnbindView(RawImage mapImage)
        {
            if (boundMapImage == null) return;
            if (mapImage != boundMapImage) throw new InvalidOperationException("MinimapSystem cannot unbind a HUD view it does not own.");
            boundMapImage.texture = null;
            boundMapImage = null;
            boundMaskMaterial = null;
        }

        /// <summary>解除事件和 HUD 引用，并释放系统独占的俯拍 RenderTexture。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            if (eventKit != null) eventKit.RemoveListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, OnActiveTeamMemberChanged);
            if (boundMapImage != null) UnbindView(boundMapImage);
            if (mapTexture != null)
            {
                mapTexture.Release();
                DestroyRuntimeObject(mapTexture);
            }
            mapTexture = null;
            activePlayerTransform = null;
            entitySystem = null;
            eventKit = null;
            isDisposed = true;
        }

        /// <summary>根据上场成员编号切换位置来源；没有存活成员时中心玩家点会隐藏。</summary>
        /// <param name="eventData">包含新上场成员 EntityId 的同步切换事件。</param>
        private void OnActiveTeamMemberChanged(ActiveTeamMemberChangedEvent eventData)
        {
            if (eventData == null) throw new ArgumentNullException(nameof(eventData));
            if (eventData.CurrentEntityId == 0)
            {
                activePlayerTransform = null;
                return;
            }
            if (!entitySystem.TryGetEntity(eventData.CurrentEntityId, out Entity entity)) throw new InvalidOperationException($"MinimapSystem cannot find active team member Entity {eventData.CurrentEntityId}.");
            if (entity.bindGo == null) throw new InvalidOperationException($"MinimapSystem cannot track Entity {entity.EntityId} without a bound GameObject.");
            activePlayerTransform = entity.bindGo.transform;
        }

        /// <summary>统计活动场景几何范围，并由临时正交相机从世界上方向下生成一张保持北方朝上的静态地图。</summary>
        private void CaptureActiveScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded) throw new InvalidOperationException("MinimapSystem requires one loaded active scene.");
            int captureLayerMask = CreateCaptureLayerMask();
            Bounds sceneBounds = CalculateSceneBounds(activeScene, captureLayerMask);
            float halfWorldSize = Mathf.Max(sceneBounds.extents.x, sceneBounds.extents.z) * CaptureBoundsPadding;
            mapWorldCenter = new Vector2(sceneBounds.center.x, sceneBounds.center.z);
            mapWorldSize = halfWorldSize * 2f;
            mapTexture = new RenderTexture(CaptureResolution, CaptureResolution, 24, RenderTextureFormat.ARGB32) { name = $"{activeScene.name} Minimap Snapshot", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false };
            if (!mapTexture.Create()) throw new InvalidOperationException("MinimapSystem failed to create the scene capture RenderTexture.");
            GameObject captureCameraObject = new GameObject("[Minimap Capture Camera]");
            Camera captureCamera = null;
            try
            {
                captureCamera = captureCameraObject.AddComponent<Camera>();
                float captureHeight = sceneBounds.max.y + Mathf.Max(sceneBounds.size.y, 100f);
                captureCameraObject.transform.SetPositionAndRotation(new Vector3(sceneBounds.center.x, captureHeight, sceneBounds.center.z), Quaternion.Euler(90f, 0f, 0f));
                captureCamera.enabled = false;
                captureCamera.orthographic = true;
                captureCamera.orthographicSize = halfWorldSize;
                captureCamera.aspect = 1f;
                captureCamera.nearClipPlane = 0.1f;
                captureCamera.farClipPlane = captureHeight - sceneBounds.min.y + 10f;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = new Color(0.025f, 0.035f, 0.045f, 1f);
                captureCamera.cullingMask = captureLayerMask;
                captureCamera.allowHDR = false;
                captureCamera.allowMSAA = false;
                captureCamera.useOcclusionCulling = false;
                captureCamera.targetTexture = mapTexture;
                captureCamera.Render();
            }
            finally
            {
                if (captureCamera != null) captureCamera.targetTexture = null;
                DestroyRuntimeObject(captureCameraObject);
            }
        }

        /// <summary>计算活动场景内会被俯拍相机绘制的 Renderer 与 Terrain 联合世界包围盒。</summary>
        /// <param name="activeScene">当前正在运行的活动场景。</param>
        /// <param name="captureLayerMask">俯拍相机允许绘制的 LayerMask。</param>
        /// <returns>包含全部地图几何的世界空间包围盒。</returns>
        private static Bounds CalculateSceneBounds(Scene activeScene, int captureLayerMask)
        {
            Bounds sceneBounds = default;
            bool hasBounds = false;
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!renderer.enabled || renderer.gameObject.scene != activeScene || (captureLayerMask & 1 << renderer.gameObject.layer) == 0) continue;
                EncapsulateBounds(ref sceneBounds, ref hasBounds, renderer.bounds);
            }
            Terrain[] terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int index = 0; index < terrains.Length; index++)
            {
                Terrain terrain = terrains[index];
                if (!terrain.enabled || terrain.gameObject.scene != activeScene || (captureLayerMask & 1 << terrain.gameObject.layer) == 0) continue;
                Vector3 terrainSize = terrain.terrainData.size;
                Bounds terrainBounds = new Bounds(terrain.transform.position + terrainSize * 0.5f, terrainSize);
                EncapsulateBounds(ref sceneBounds, ref hasBounds, terrainBounds);
            }
            if (!hasBounds) throw new InvalidOperationException($"MinimapSystem cannot find capturable world geometry in scene '{activeScene.name}'.");
            return sceneBounds;
        }

        /// <summary>把一个有效包围盒合并进场景范围，并处理第一项没有初始中心的问题。</summary>
        /// <param name="combinedBounds">累计中的场景世界包围盒。</param>
        /// <param name="hasBounds">累计包围盒是否已经接收过第一项。</param>
        /// <param name="bounds">当前需要合并的世界包围盒。</param>
        private static void EncapsulateBounds(ref Bounds combinedBounds, ref bool hasBounds, Bounds bounds)
        {
            if (!hasBounds)
            {
                combinedBounds = bounds;
                hasBounds = true;
                return;
            }
            combinedBounds.Encapsulate(bounds);
        }

        /// <summary>排除 UI、角色和敌人层，保证初始化俯拍只记录场景本身而不把动态单位烘进地图。</summary>
        /// <returns>可直接赋给俯拍 Camera.cullingMask 的位掩码。</returns>
        private static int CreateCaptureLayerMask()
        {
            int uiLayer = ResolveLayer("UI");
            int characterLayer = ResolveLayer("Character");
            int enemyLayer = ResolveLayer("Enemy");
            return ~((1 << uiLayer) | (1 << characterLayer) | (1 << enemyLayer));
        }

        /// <summary>解析项目必须存在的地图过滤层，并在项目层配置被破坏时立即报告。</summary>
        /// <param name="layerName">需要解析的 Layer 名称。</param>
        /// <returns>Layer 在零到三十一范围内的索引。</returns>
        private static int ResolveLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) throw new InvalidOperationException($"MinimapSystem requires project layer '{layerName}'.");
            return layer;
        }

        /// <summary>把玩家世界坐标换算成整张地图 UV，并移动 RawImage 采样窗口使玩家点始终位于圆心。</summary>
        private void UpdateBoundView()
        {
            Vector2 mapUv = activePlayerTransform == null ? new Vector2(0.5f, 0.5f) : WorldToMapUv(activePlayerTransform.position);
            Rect viewport = new Rect(mapUv.x - ViewportWorldFraction * 0.5f, mapUv.y - ViewportWorldFraction * 0.5f, ViewportWorldFraction, ViewportWorldFraction);
            boundMapImage.uvRect = viewport;
            Vector4 maskUvTransform = new Vector4(1f / viewport.width, 1f / viewport.height, -viewport.x / viewport.width, -viewport.y / viewport.height);
            boundMaskMaterial.SetVector(MaskUvTransformId, maskUvTransform);
        }

        /// <summary>把世界 XZ 坐标映射为北方朝上的整张地图零到一 UV。</summary>
        /// <param name="worldPosition">需要映射的世界位置。</param>
        /// <returns>X 对应 U、Z 对应 V 的地图坐标。</returns>
        private Vector2 WorldToMapUv(Vector3 worldPosition)
        {
            return new Vector2(0.5f + (worldPosition.x - mapWorldCenter.x) / mapWorldSize, 0.5f + (worldPosition.z - mapWorldCenter.y) / mapWorldSize);
        }

        /// <summary>按照当前 Unity 运行环境销毁小地图系统独占创建的对象或纹理。</summary>
        /// <param name="runtimeObject">需要释放的 Unity 对象。</param>
        private static void DestroyRuntimeObject(UnityEngine.Object runtimeObject)
        {
            if (runtimeObject == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(runtimeObject);
            else UnityEngine.Object.DestroyImmediate(runtimeObject);
        }
    }
}
