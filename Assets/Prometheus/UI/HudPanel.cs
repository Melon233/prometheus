using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SuperScrollView;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Input;
using Xuan.Prometheus.Logic.Talent;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Hud 面板业务控制器，用于验证 UIKit 的类型扫描、Prefab 加载、组件绑定、打开生命周期和关闭缓存能力。
    /// </summary>
    [UIPanelConfig("HudPanel", UIPanelLayer.Normal, UIPanelClosePolicy.Cache)]
    public sealed class HudPanel : HudPanelBase
    {
        /// <summary>定义小地图从中心向外保持完全不透明的归一化半径。</summary>
        private const float MinimapFadeStartDistance = 0.78f;

        /// <summary>定义小地图从中心向外变为完全透明的归一化半径。</summary>
        private const float MinimapFadeCompleteDistance = 1f;

        /// <summary>保存当前 HUD 实际订阅的事件总线实例，确保最终解绑时移除同一条监听。</summary>

        /// <summary>保存当前上场成员的运行时 EntityId，切人时以该编号重新建立全部字段监听。</summary>
        private int observedEntityId;

        /// <summary>保存当前单局的实体与强类型字段监听系统。</summary>
        private EntitySystem entitySystem;

        /// <summary>保存生命、上限、核心能量、技能冷却、大招状态、Buff 列表和交互列表对应的可释放监听。</summary>
        private readonly ListenHandle[] listenHandles = new ListenHandle[10];

        /// <summary>复用当前上场成员的持续型 Buff 快照，避免列表逐帧刷新时产生临时集合。</summary>
        private readonly List<EffectInstance> observedBuffs = new List<EffectInstance>();

        /// <summary>复用当前上场成员的附近交互物快照，供交互栏列表刷新。</summary>
        private readonly List<PoiConfig> observedInteracts = new List<PoiConfig>();

        /// <summary>标记当前缓存面板是否正在显示，关闭期间只记录目标编号而不持有字段监听。</summary>
        private bool isObserving;

        /// <summary>保存当前 HUD 绑定到的集中式输入系统，快捷键由 InputAction 仲裁，战斗按钮点击则提交定向实体命令。</summary>
        private InputSystem inputSystem;

        /// <summary>保存当前单局的小队系统，供三个头像按钮直接切换固定槽位。</summary>
        private TeamSystem teamSystem;

        /// <summary>保存独立 HUD 命令系统，普通点击只负责提交命令而不监听任何快捷键。</summary>
        private HudCommandSystem hudCommandSystem;

        /// <summary>保存当前单局的大世界 POI 系统，交互点击由它向服务器提交请求。</summary>
        private WorldSystem worldSystem;

        /// <summary>保存运行时创建在 MiniMapButton 内的地图 RawImage。</summary>
        private RawImage minimapImage;

        /// <summary>保存当前 HUD 独占的 MaskUIShader 材质，避免 uvRect 参数影响其他 UI。</summary>
        private Material minimapMaskMaterial;

        /// <summary>保存小地图 POI 标记的父节点，标记坐标与地图纹理使用同一套归一化视口。</summary>
        private RectTransform minimapPoiRoot;

        /// <summary>保存覆盖小地图区域的透明命中层，仅用于让点击事件沿 MiniMapButton 父级链路触发。</summary>
        private GameObject minimapHitArea;

        /// <summary>保存按地图归一化坐标定位的玩家标记，显示逻辑与大地图保持一致。</summary>
        private Image minimapPlayerMarker;

        /// <summary>保存最近一次从玩家实体读取的位置，HUD 打开后立即恢复地图视口。</summary>
        private Vector3 minimapPlayerPosition;

        /// <summary>标记是否已经收到有效玩家位置，避免地图资源未就绪时使用未初始化坐标。</summary>
        private bool hasMinimapPlayerPosition;

        /// <summary>组件绑定完成后只订阅小队成员切换事实；具体数值统一通过 EntitySystem 观察。</summary>
        protected override void OnBind()
        {
            Core.Event.AddListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, OnActiveTeamMemberChanged);
        }

        /// <summary>收到上场成员变化后保存新编号，并在面板显示期间原子替换全部字段监听。</summary>
        private void OnActiveTeamMemberChanged(ActiveTeamMemberChangedEvent eventData)
        {
            int entityId = eventData == null ? 0 : eventData.CurrentEntityId;
            observedEntityId = entityId;
            if (isObserving) BindObservedEntity(entityId);
        }

        /// <summary>从属性组件当前值刷新生命条和生命文本；监听注册时会立即执行一次。</summary>
        private void ApplyHealthState(PropertyComponent propertyComponent)
        {
            HpBar.fillAmount = propertyComponent.MaxHp > 0f ? Mathf.Clamp01(propertyComponent.Hp / propertyComponent.MaxHp) : 0f;
            Hp.text = $"{propertyComponent.Hp:0.##} / {propertyComponent.MaxHp:0.##}";
        }

        /// <summary>从属性组件当前值刷新核心能量条；当前值或上限变化都会走同一入口。</summary>
        private void ApplyCoreEnergyState(PropertyComponent propertyComponent)
        {
            EnergyImg.fillAmount = propertyComponent.CoreEnergyLimit > 0f ? Mathf.Clamp01(propertyComponent.CoreEnergy / propertyComponent.CoreEnergyLimit) : 0f;
        }

        /// <summary>
        /// 首次创建控制器时验证生成字段已经成功绑定。
        /// </summary>
        protected override void OnInitialize()
        {
            BuffList.InitListView(0, OnGetBuffItemByIndex);
            InteractBar.InitListView(0, OnGetInteractItemByIndex);
            CreateMinimapView();
            Debug.Log($"[UIKit] {nameof(HudPanel)} initialized with {Binder.Count} generated component binding(s).", Root);
        }

        /// <summary>每次 HUD 进入显示状态时解析当前上场成员并建立立即回调的字段监听。</summary>
        protected override void OnOpen()
        {
            Debug.Log($"[UIKit] {nameof(HudPanel)} opened.", Root);
            if (!Core.Gameplay.TryGetSystem(out entitySystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(EntitySystem)}.");
            if (!Core.Gameplay.TryGetSystem(out teamSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(TeamSystem)}.");
            if (!Core.Gameplay.TryGetSystem(out inputSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(InputSystem)}.");
            if (!Core.Gameplay.TryGetSystem(out hudCommandSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(HudCommandSystem)}.");
            if (!Core.Gameplay.TryGetSystem(out worldSystem)) throw new InvalidOperationException($"{nameof(HudPanel)} requires {nameof(WorldSystem)}.");
            SubscribeMinimapEvents();
            if (worldSystem.TryGetPlayerPosition(out Vector3 currentPosition))
            {
                minimapPlayerPosition = currentPosition;
                hasMinimapPlayerPosition = true;
            }
            RefreshMinimap();
            isObserving = true;
            BindObservedEntity(teamSystem.ActiveEntityId);
        }

        /// <summary>面板进入缓存关闭状态时释放字段监听，重新打开时会从当前值立即恢复。</summary>
        protected override void OnClose()
        {
            UnsubscribeMinimapEvents();
            isObserving = false;
            ReleaseValueListeners();
        }

        /// <summary>在现有 MiniMapButton 内创建静态地图图层和由代码参数控制的径向虚化材质。</summary>
        private void CreateMinimapView()
        {
            Shader maskShader = Resources.Load<Shader>("MaskUIShader");
            if (maskShader == null) throw new InvalidOperationException($"{nameof(HudPanel)} requires Resources shader 'MaskUIShader'.");
            DisableMinimapTemplateGraphics();
            minimapMaskMaterial = new Material(maskShader) { name = "Hud Minimap Alpha Mask" };
            ConfigureMinimapFade(minimapMaskMaterial, MinimapFadeStartDistance, MinimapFadeCompleteDistance);
            GameObject mapObject = new GameObject("Map Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            mapObject.layer = MiniMapButton.gameObject.layer;
            RectTransform mapRect = mapObject.GetComponent<RectTransform>();
            mapRect.SetParent(MiniMapButton.transform, false);
            // 把运行时地图固定在容器最底层，使 Prefab 中手工配置的玩家标记和其他装饰稳定显示在地图之上。
            mapRect.SetAsFirstSibling();
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = new Vector2(14f, 14f);
            mapRect.offsetMax = new Vector2(-14f, -14f);
            minimapImage = mapObject.GetComponent<RawImage>();
            minimapImage.color = Color.white;
            minimapImage.material = minimapMaskMaterial;
            minimapImage.raycastTarget = false;
            GameObject poiRootObject = new GameObject("POI Markers", typeof(RectTransform));
            poiRootObject.layer = MiniMapButton.gameObject.layer;
            minimapPoiRoot = poiRootObject.GetComponent<RectTransform>();
            minimapPoiRoot.SetParent(MiniMapButton.transform, false);
            minimapPoiRoot.anchorMin = new Vector2(0f, 0f);
            minimapPoiRoot.anchorMax = new Vector2(1f, 1f);
            minimapPoiRoot.offsetMin = new Vector2(14f, 14f);
            minimapPoiRoot.offsetMax = new Vector2(-14f, -14f);
            minimapPoiRoot.SetAsLastSibling();
            GameObject playerMarkerObject = CreateMinimapMarker("Player Marker", WorldMapIconCatalog.LoadPlayerIcon(), Color.white, minimapPoiRoot, new Vector2(28f, 28f));
            minimapPlayerMarker = playerMarkerObject.GetComponent<Image>();
            minimapPlayerMarker.rectTransform.anchorMin = Vector2.zero;
            minimapPlayerMarker.rectTransform.anchorMax = Vector2.zero;
            minimapPlayerMarker.rectTransform.anchoredPosition = Vector2.zero;
            minimapHitArea = new GameObject("MiniMap Hit Area", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            minimapHitArea.layer = MiniMapButton.gameObject.layer;
            RectTransform hitRect = minimapHitArea.GetComponent<RectTransform>();
            hitRect.SetParent(MiniMapButton.transform, false);
            hitRect.anchorMin = Vector2.zero;
            hitRect.anchorMax = Vector2.one;
            hitRect.offsetMin = Vector2.zero;
            hitRect.offsetMax = Vector2.zero;
            Image hitImage = minimapHitArea.GetComponent<Image>();
            hitImage.color = Color.clear;
            hitImage.raycastTarget = true;
            minimapHitArea.transform.SetAsFirstSibling();
        }

        /// <summary>关闭 MiniMapButton 模板中仅用于旧地图表现的 Graphic，避免旧圆形贴图覆盖新的地图视图。</summary>
        private void DisableMinimapTemplateGraphics()
        {
            for (int index = 0; index < MiniMapButton.transform.childCount; index++)
            {
                Transform child = MiniMapButton.transform.GetChild(index);
                Graphic graphic = child.GetComponent<Graphic>();
                if (graphic == null) continue;
                graphic.enabled = false;
                graphic.raycastTarget = false;
            }
        }

        /// <summary>订阅 WorldSystem 的地图资源和 POI 状态事实；玩家坐标由逐帧实体读取驱动。</summary>
        private void SubscribeMinimapEvents()
        {
            Core.Event.AddListener<WorldMapReadyEvent>(Event.WorldMapReady, OnWorldMapReady);
            Core.Event.AddListener<WorldMapPoiChangedEvent>(Event.WorldMapPoiChanged, OnWorldMapPoiChanged);
        }

        /// <summary>解除 HUD 小地图的 WorldSystem 监听，避免缓存面板关闭后继续更新已隐藏控件。</summary>
        private void UnsubscribeMinimapEvents()
        {
            Core.Event.RemoveListener<WorldMapReadyEvent>(Event.WorldMapReady, OnWorldMapReady);
            Core.Event.RemoveListener<WorldMapPoiChangedEvent>(Event.WorldMapPoiChanged, OnWorldMapPoiChanged);
        }

        /// <summary>地图资源就绪后重新绑定静态纹理和当前 POI 标记。</summary>
        private void OnWorldMapReady(WorldMapReadyEvent eventData)
        {
            RefreshMinimap();
        }

        /// <summary>每帧直接读取当前玩家实体坐标；只有坐标变化时才重算小地图视口和 POI 锚点。</summary>
        private void UpdateMinimapPlayerPosition()
        {
            if (worldSystem == null || !worldSystem.TryGetPlayerPosition(out Vector3 currentPosition)) return;
            if (hasMinimapPlayerPosition && minimapPlayerPosition == currentPosition) return;
            minimapPlayerPosition = currentPosition;
            hasMinimapPlayerPosition = true;
            RefreshMinimap();
        }

        /// <summary>POI 集合或状态变化后重建小地图上的 POI 标记。</summary>
        private void OnWorldMapPoiChanged(WorldMapPoiChangedEvent eventData)
        {
            RefreshMinimap();
        }

        /// <summary>根据 WorldSystem 地图定义设置纹理、局部视口和 POI 标记；地图未拍摄时显示空白视图。</summary>
        private void RefreshMinimap()
        {
            if (minimapImage == null || worldSystem == null) return;
            minimapImage.texture = worldSystem.MapTexture;
            minimapImage.enabled = worldSystem.MapTexture != null;
            if (worldSystem.MapTexture == null)
            {
                minimapImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                ConfigureMinimapMask(new Rect(0f, 0f, 1f, 1f));
                ClearMinimapPoiMarkers();
                return;
            }
            Rect viewport = new Rect(0f, 0f, 1f, 1f);
            if (hasMinimapPlayerPosition)
            {
                Vector2 playerUv = worldSystem.WorldToMapNormalized(minimapPlayerPosition);
                // 视口越小，地图显示比例越大；玩家始终作为视口中心点，避免小地图看起来过度缩小。
                const float viewportFraction = 0.2f;
                viewport = WorldMapUiMath.CalculateMinimapViewport(playerUv, viewportFraction);
            }
            minimapImage.uvRect = viewport;
            ConfigureMinimapMask(viewport);
            RebuildMinimapPoiMarkers(viewport);
        }

        /// <summary>同步径向虚化材质的 UV 还原参数，确保虚化边界固定在 HUD 控件而不是地图内容上。</summary>
        private void ConfigureMinimapMask(Rect viewport)
        {
            Vector4 maskUvTransform = new Vector4(1f / viewport.width, 1f / viewport.height, -viewport.x / viewport.width, -viewport.y / viewport.height);
            minimapMaskMaterial.SetVector("_MaskUvTransform", maskUvTransform);
        }

        /// <summary>按 WorldSystem 当前 POI 集合重建可见于小地图视口内的彩色标记。</summary>
        private void RebuildMinimapPoiMarkers(Rect viewport)
        {
            ClearMinimapPoiMarkers();
            for (int index = 0; index < worldSystem.AllPois.Count; index++)
            {
                PoiEntity poi = worldSystem.AllPois[index];
                if (poi == null || poi.Config == null || poi.IsConsumed) continue;
                // POI 根节点可能挂在带有场景偏移的父节点下；运行时实际 Transform 才是静态地图拍摄所使用的权威世界坐标。
                Vector2 poiUv = worldSystem.WorldToMapNormalized(poi.bindGo.transform.position);
                if (!WorldMapUiMath.TryGetViewportAnchor(poiUv, viewport, out Vector2 localUv)) continue;
                GameObject markerObject = CreateMinimapMarker(poi.Config.Id, WorldMapIconCatalog.LoadPoiIcon(poi.Config.PoiType), Color.white, minimapPoiRoot, new Vector2(28f, 28f));
                RectTransform markerRect = markerObject.GetComponent<RectTransform>();
                markerRect.anchorMin = localUv;
                markerRect.anchorMax = localUv;
                markerRect.anchoredPosition = Vector2.zero;
            }
            if (!hasMinimapPlayerPosition || minimapPlayerMarker == null) return;
            // 视口已经以玩家坐标为中心，玩家图标固定在视口中心，保证始终可见且不会随 POI 标记偏移。
            minimapPlayerMarker.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            minimapPlayerMarker.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            minimapPlayerMarker.rectTransform.anchoredPosition = Vector2.zero;
            minimapPlayerMarker.transform.SetAsLastSibling();
        }

        /// <summary>删除旧 POI 标记但保留固定在中心的玩家标记。</summary>
        private void ClearMinimapPoiMarkers()
        {
            if (minimapPoiRoot == null) return;
            for (int index = minimapPoiRoot.childCount - 1; index >= 0; index--)
            {
                Transform child = minimapPoiRoot.GetChild(index);
                if (minimapPlayerMarker != null && child == minimapPlayerMarker.transform) continue;
                DestroyUiResource(child.gameObject);
            }
        }

        /// <summary>创建不拦截 MiniMapButton 点击的简单圆点标记。</summary>
        private static GameObject CreateMinimapMarker(string markerName, Sprite sprite, Color color, RectTransform parent, Vector2 size)
        {
            GameObject markerObject = new GameObject(markerName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            markerObject.layer = parent.gameObject.layer;
            RectTransform markerRect = markerObject.GetComponent<RectTransform>();
            markerRect.SetParent(parent, false);
            markerRect.sizeDelta = size;
            Image markerImage = markerObject.GetComponent<Image>();
            markerImage.sprite = sprite;
            markerImage.preserveAspect = sprite != null;
            markerImage.color = color;
            markerImage.raycastTarget = false;
            return markerObject;
        }

        /// <summary>面板打开期间逐帧读取玩家实体坐标，地图 UI 的更新由 UIKit 面板生命周期统一驱动。</summary>
        protected override void OnUpdate(float dt)
        {
            if (isObserving) UpdateMinimapPlayerPosition();
        }

        /// <summary>把径向虚化区间写入当前小地图独占材质，区间内由 Shader 使用五次 smootherstep 平滑过渡。</summary>
        /// <param name="material">使用 MaskUIShader 的小地图材质。</param>
        /// <param name="fadeStartDistance">开始降低 alpha 的归一化半径。</param>
        /// <param name="fadeCompleteDistance">alpha 降为零的归一化半径。</param>
        private static void ConfigureMinimapFade(Material material, float fadeStartDistance, float fadeCompleteDistance)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (fadeStartDistance < 0f) throw new ArgumentOutOfRangeException(nameof(fadeStartDistance), fadeStartDistance, "Minimap fade start distance cannot be negative.");
            if (fadeCompleteDistance <= fadeStartDistance) throw new ArgumentOutOfRangeException(nameof(fadeCompleteDistance), fadeCompleteDistance, "Minimap fade complete distance must be greater than fade start distance.");
            material.SetFloat("_FadeStartDistance", fadeStartDistance);
            material.SetFloat("_FadeCompleteDistance", fadeCompleteDistance);
        }

        /// <summary>释放旧成员监听后，为新成员的生命、核心能量、技能冷却、大招状态和 Buff 列表建立独立监听。</summary>
        private void BindObservedEntity(int entityId)
        {
            ReleaseValueListeners();
            observedEntityId = entityId;
            if (entitySystem == null || entityId <= 0) return;
            listenHandles[0] = entitySystem.Listen<PropertyComponent>(entityId, component => component.HpProperty, ApplyHealthState);
            listenHandles[1] = entitySystem.Listen<PropertyComponent>(entityId, component => component.MaxHpProperty, ApplyHealthState);
            listenHandles[2] = entitySystem.Listen<PropertyComponent>(entityId, component => component.CoreEnergyProperty, ApplyCoreEnergyState);
            listenHandles[3] = entitySystem.Listen<PropertyComponent>(entityId, component => component.CoreEnergyLimitProperty, ApplyCoreEnergyState);
            listenHandles[4] = entitySystem.Listen<PropertyComponent>(entityId, component => component.UltEnergyProperty, _ => ApplyUltimateState(entityId));
            listenHandles[5] = entitySystem.Listen<PropertyComponent>(entityId, component => component.UltEnergyLimitProperty, _ => ApplyUltimateState(entityId));
            listenHandles[6] = entitySystem.Listen<UltimateComponent>(entityId, component => component.CooldownRemainingProperty, _ => ApplyUltimateState(entityId));
            listenHandles[7] = entitySystem.Listen<SkillComponent>(entityId, component => component.CooldownRemainingProperty, ApplySkillState);
            listenHandles[8] = entitySystem.Listen<EffectComponent>(entityId, component => component.BuffRevisionProperty, ApplyBuffState);
            listenHandles[9] = entitySystem.Listen<InteractComponent>(entityId, component => component.RevisionProperty, ApplyInteractState);
        }

        /// <summary>读取同一 Entity 的大招能量和冷却组件，以一次 UI 写入保持两类进度显示一致。</summary>
        private void ApplyUltimateState(int entityId)
        {
            if (entityId != observedEntityId || entitySystem == null || !entitySystem.TryGetEntity(entityId, out Logic.Entity entity)) return;
            if (!entity.TryGetComp(out PropertyComponent propertyComponent) || !entity.TryGetComp(out UltimateComponent ultimateComponent)) return;
            Ult.ApplyState(propertyComponent.UltEnergy, propertyComponent.UltEnergyLimit, ultimateComponent.CooldownRemaining, ultimateComponent.CooldownDuration);
        }

        /// <summary>从当前上场成员的技能组件读取完整冷却与剩余冷却，并同步技能区遮罩和一位小数文本。</summary>
        private void ApplySkillState(SkillComponent skillComponent)
        {
            Skill.ApplyState(skillComponent.CooldownRemaining, skillComponent.CooldownDuration);
        }

        /// <summary>从当前上场成员的 EffectComponent 复制持续型 Buff 快照，并刷新可见的循环列表项。</summary>
        private void ApplyBuffState(EffectComponent effectComponent)
        {
            effectComponent.CopyActiveBuffs(observedBuffs);
            BuffList.SetListItemCount(observedBuffs.Count, false);
            BuffList.RefreshAllShownItem();
        }

        /// <summary>按索引从复用快照创建或复用 Buff 列表项，并把 EffectInstance 的只读状态写入 BuffMono。</summary>
        private LoopListViewItem2 OnGetBuffItemByIndex(LoopListView2 listView, int index)
        {
            if (index < 0 || index >= observedBuffs.Count) return null;
            LoopListViewItem2 item = listView.NewListViewItem("Buff");
            if (item == null) throw new InvalidOperationException("HudPanel BuffList requires an item prefab named 'Buff'.");
            BuffMono buffMono = item.GetComponent<BuffMono>();
            if (buffMono == null) throw new InvalidOperationException("HudPanel BuffList item prefab requires BuffMono.");
            buffMono.Apply(observedBuffs[index]);
            return item;
        }

        /// <summary>从 InteractComponent 复制附近交互物快照，并刷新可见的交互栏列表项。</summary>
        private void ApplyInteractState(InteractComponent interactComponent)
        {
            interactComponent.CopyNearby(observedInteracts);
            InteractBar.SetListItemCount(observedInteracts.Count, false);
            InteractBar.RefreshAllShownItem();
        }

        /// <summary>按索引从复用快照创建或复用交互栏列表项，并把交互物类型与点击回调写入 InteractMono。</summary>
        private LoopListViewItem2 OnGetInteractItemByIndex(LoopListView2 listView, int index)
        {
            if (index < 0 || index >= observedInteracts.Count) return null;
            LoopListViewItem2 item = listView.NewListViewItem("Interact");
            if (item == null) throw new InvalidOperationException("HudPanel InteractBar requires an item prefab named 'Interact'.");
            InteractMono interactMono = item.GetComponent<InteractMono>();
            if (interactMono == null) throw new InvalidOperationException("HudPanel InteractBar item prefab requires InteractMono.");
            interactMono.Apply(observedInteracts[index], OnInteractClick);
            return item;
        }

        /// <summary>交互栏点击：按交互物类型映射操作，解析实体后向大世界系统提交交互请求。</summary>
        private void OnInteractClick(PoiConfig config)
        {
            if (worldSystem == null || config == null) return;
            Debug.Log($"[交互] 点击交互 {config.Id} ({config.PoiType})");
            if (worldSystem.TryGetPoiEntity(config.Id, out PoiEntity entity)) entity.OnInteract();
        }

        /// <summary>幂等释放当前成员的全部字段监听，防止缓存面板和旧角色继续互相持有。</summary>
        private void ReleaseValueListeners()
        {
            for (int index = 0; index < listenHandles.Length; index++)
            {
                listenHandles[index]?.Dispose();
                listenHandles[index] = null;
            }
            observedBuffs.Clear();
            observedInteracts.Clear();
            if (BuffList != null) BuffList.SetListItemCount(0, false);
            if (InteractBar != null) InteractBar.SetListItemCount(0, false);
        }

        /// <summary>HUD 最终释放前移除小队监听和全部字段监听，避免事件总线或属性继续持有失效控制器。</summary>
        protected override void OnUnbind()
        {
            ReleaseValueListeners();
            UnsubscribeMinimapEvents();
            Core.Event.RemoveListener<ActiveTeamMemberChangedEvent>(Event.ActiveTeamMemberChanged, OnActiveTeamMemberChanged);
            DestroyUiResource(minimapHitArea);
            DestroyUiResource(minimapMaskMaterial);
            inputSystem = null;
            teamSystem = null;
            hudCommandSystem = null;
            worldSystem = null;
            entitySystem = null;
            minimapImage = null;
            minimapMaskMaterial = null;
            minimapPoiRoot = null;
            minimapHitArea = null;
            minimapPlayerMarker = null;
            hasMinimapPlayerPosition = false;
            observedEntityId = 0;
            isObserving = false;
        }

        /// <summary>按照当前 Unity 运行环境释放 HUD 独占创建的材质或纹理资源。</summary>
        /// <param name="resource">需要释放的 Unity 对象。</param>
        private static void DestroyUiResource(UnityEngine.Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }

        /// <summary>把战斗按钮的一次点击定向提交给当前上场实体，并由输入阶段在实体更新前写入按钮命令。</summary>
        private void QueueCurrentEntityButtonAction(InputActionMask action)
        {
            inputSystem.QueueEntityButtonActions(observedEntityId, action);
        }

        /// <summary>点击抽奖按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnLotteryButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenLottery);
        }

        /// <summary>点击大招按钮时提交一次终结技玩法命令。</summary>
        protected override void OnUltButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Ultimate);
        }

        /// <summary>点击小地图按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnMiniMapButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenMiniMap);
        }

        /// <summary>点击任务按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnQuestButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenQuest);
        }

        /// <summary>点击菜单按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnMenuButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenMenu);
        }

        /// <summary>点击跳跃按钮时提交一次跳跃玩法命令。</summary>
        protected override void OnJumpButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Jump);
        }

        /// <summary>点击攻击按钮时提交一次普通攻击玩法命令。</summary>
        protected override void OnAtkButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Attack);
        }

        /// <summary>点击闪避按钮时提交一次闪避玩法命令。</summary>
        protected override void OnDodgeButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Dodge);
        }

        /// <summary>点击技能按钮时提交一次技能玩法命令。</summary>
        protected override void OnSkillButtonClick()
        {
            QueueCurrentEntityButtonAction(InputActionMask.Skill);
        }

        /// <summary>点击引导按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnGuideButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenGuide);
        }

        /// <summary>点击活动按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnEventButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenEvent);
        }

        /// <summary>点击角色按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnCharacterButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenCharacter);
        }

        /// <summary>点击背包按钮时直接执行与快捷键共用的界面业务入口。</summary>
        protected override void OnBagButtonClick()
        {
            hudCommandSystem.Execute(HudCommandType.OpenBag);
        }

        /// <summary>点击第一个头像时直接切换到第一个固定小队槽位。</summary>
        protected override void OnAvatar1Click()
        {
            teamSystem.SwitchToSlot(0);
        }

        /// <summary>点击第二个头像时直接切换到第二个固定小队槽位。</summary>
        protected override void OnAvatar2Click()
        {
            teamSystem.SwitchToSlot(1);
        }

        /// <summary>点击第三个头像时直接切换到第三个固定小队槽位。</summary>
        protected override void OnAvatar3Click()
        {
            teamSystem.SwitchToSlot(2);
        }

    }
}
