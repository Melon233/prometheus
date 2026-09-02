# 世界地图系统

## 运行时职责

地图的事实来源是 `WorldSystem`，不再注册或运行独立的 `MinimapSystem`。`WorldSystem` 持有 `WorldMapDefinition` 和 POI 集合，并通过 `Core.Event` 发布地图资源就绪与 POI 状态变化事件；玩家位置由地图 UI 每帧直接读取当前玩家实体坐标。

地图定义包含地图纹理、左下角世界原点、世界 X 轴长度和世界 Z 轴宽度。所有坐标转换都使用 `WorldMapDefinition.WorldToNormalized`，因此 HUD 小地图和大地图不会各自维护一套换算常量。POI 图标定位读取 `PoiEntity.bindGo.transform.position` 的实际世界坐标，以兼容场景中带父节点偏移的 POI；`PoiConfig.Position` 仅作为导出与缓存字段，不作为运行时 UI 定位来源。

## UI 分工

- `HudPanel` 直接创建小地图 RawImage 和 POI 标记。面板通过 `UIPanel.OnUpdate` 每帧读取 `WorldSystem.TryGetPlayerPosition`，按玩家归一化坐标计算局部视口并保持玩家标记位于视口中心；视口比例为 0.2，以保证小地图有足够的地图放大比例，不订阅玩家位置高频事件。
- HUD Prefab 中的 `MiniMap` 及其旧模板装饰 Image 均保持禁用，HUD 运行时创建透明命中层接收点击并沿生成的 Button 绑定链路打开 `MapPanel`；地图 RawImage 和 POI 标记均关闭射线拦截，POI 层固定置于地图内容之上。地图纹理尚未就绪时会禁用 RawImage，避免 Unity 默认白色纹理形成白圈。
- `MapPanel` 使用 `MapPanel.prefab` 作为 UIKit 面板根节点。`MapImage` 和 `CloseButton` 由根节点 `UIComponentBinder` 固定绑定，面板运行时建立可拖拽、可缩放的全局地图视口，并通过 `UIPanel.OnUpdate` 每帧显示玩家和 POI 标记。
- 两个面板只订阅地图资源和 POI 状态事件；玩家坐标由各自的 UI 驱动逐帧读取当前实体 Transform，不通过高频全局事件传递。

小地图和大地图使用同一张静态地图纹理及同一坐标系；小地图通过局部 `uvRect` 跟随玩家，大地图通过可平移内容显示玩家标记。动态玩家、敌人、UI 和特效不会被拍摄进纹理。

## 图标与大地图交互

`WorldMapIconCatalog` 统一读取八种 POI 图标（`UI_TeleAnchor`、`UI_Statue`、`UI_Chest`、`UI_SpiritCore`、`UI_Gathering`、`UI_Dungeon`、`UI_Boss`、`UI_MonsterCamp`）、关闭图标 `UI_Close` 和原有角色位置图标 `UI_MarkLocalAvatar`。HUD 小地图和 `MapPanel` 通过同一个目录加载入口，避免两处资源地址不一致。

`Assets/BundleCollectorSetting.asset` 为该目录配置了 `AddressByFileName + CollectAll` 收集器，因此这些 PNG 会进入 YooAsset 的 `DefaultPackage`，运行时可以通过文件名地址加载为 `Sprite`。

打开大地图后，`MapPanel` 将地图纹理按 `WorldMapDefinition` 的世界长宽比例铺满全屏视口，首次缩放读取 `WorldMapDefinition.InitialZoom`（运行时限制在 1 到 4 倍）；之后的滚轮缩放写入 `WorldSystem.MapZoom`，同一局再次打开时继续使用缓存值。鼠标拖动地图内容、滚轮调整缩放；滚轮缩放始终以屏幕中心为锚点，缩放前后保持中心对应的地图点不变，不受鼠标位置影响。地图内容的可移动边界以地图尺寸一半为范围，允许视口在地图边缘外扩半个视口。地图标记跟随地图平移，但应用 `1 / zoom` 逆缩放保持固定屏幕尺寸。关闭按钮使用 `UI_Close`，由 Binder 自动注册 `OnCloseButtonClick`。

神像和传送锚点标记使用 `Button` 绑定点击回调，点击后调用 `WorldSystem.TryTeleportToPoi`。传送成功时 `WorldSystem` 会暂停玩家 `CharacterController`、设置目标坐标并清空移动状态，HUD 和大地图在下一帧直接读取该坐标；其余 POI 只显示图标，不注册传送操作。

## 编辑器拍摄

通过菜单 `Prometheus/World/Map Capture` 打开 `WorldMapCaptureWindow`，设置地图左下角原点、世界长度、世界宽度、纹理宽度、拍摄高度和 LayerMask 后执行拍摄。相机使用透明清屏色，拍摄范围内没有场景内容的像素会以 Alpha=0 写入 PNG，不会被填充为黑色。

工具使用临时正交相机在编辑器中生成 PNG，默认保存到 `Assets/BundleResources/UI/Common/Atlas/WorldMap_<Scene>.png`，并创建或更新 `Assets/BundleResources/Config/Global/WorldMapDefinition.asset`。运行时只读取生成的静态资源，不会创建俯拍相机或 RenderTexture。

## 事件时序

1. `WorldSystem.AfterNew` 通过 AssetKit 读取 `WorldMapDefinition`，发布 `WorldMapReady`。
2. POI 场景扫描完成后发布 `WorldMapPoiChanged`，面板从 `WorldSystem.AllPois` 读取完整集合。
3. HUD 和 `MapPanel` 的运行时驱动每帧读取当前玩家实体位置并更新地图视口；AOI 刷新和网络同步仍按低频 tick 执行。
4. 服务器交互确认后发布 POI 变化事件，两个面板重建受影响的标记。

地图定义尚未生成时，WorldSystem 会保留空地图状态，面板可以正常打开但不显示纹理；生成资源后重新启动玩法即可加载。
