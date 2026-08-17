# MinimapSystem

## 目标

`MinimapSystem` 为每个 GameplayKit 实例生成并管理一张静态场景俯拍地图。地图固定以世界 `+Z` 为上方，不随玩家旋转；HUD 通过移动地图采样窗口让玩家始终位于圆心。玩家标记由 UI 自行配置，系统不创建、不绑定也不控制标记。

## 生成流程

1. `GameplayKit.Configure` 在 `TeamSystem` 之前注册 `MinimapSystem`。
2. `MinimapSystem.AfterNew` 在角色和敌人实例创建前统计活动场景中可渲染的场景几何，排除 `UI`、`Character` 和 `Enemy` Layer。
3. 系统创建一台临时正交相机，从世界上方沿 `-Y` 方向把整个场景渲染到 1024×1024 RenderTexture。
4. 俯拍完成后立即销毁临时相机；后续帧只保留静态纹理，不承担第二台相机的持续渲染成本。
5. `ActiveTeamMemberChangedEvent` 只负责切换玩家位置来源，换人不会重拍地图，也不会读取角色旋转。

## HUD 映射

`HudPanel` 只在现有 `MiniMapButton` 内动态创建 `RawImage`，并把它固定为容器的第一个子节点，不修改 UIComponentBinder 的生成字段表。`MinimapSystem` 每帧在角色移动完成后把世界 `X/Z` 换算为地图 `U/V`，再设置 RawImage 的 `uvRect`。需要玩家标记时应直接在 UI Prefab 中配置，Prefab 子节点会显示在地图之上；标记不进入小地图系统的创建、绑定、显隐或旋转链路。

## 径向虚化

地图使用 `Prometheus/UI/Alpha Mask`。Shader 保存在 `Shaders/UI/Resources`，确保只有运行时代码引用时仍会进入 Player 构建。Shader 不再读取外部 alpha 贴图，而是根据控件中心的归一化径向距离直接计算透明度。

`HudPanel` 通过代码写入 `_FadeStartDistance=0.78` 和 `_FadeCompleteDistance=1`。开始距离以内完全不透明，达到完全距离后透明度为零；过渡区间使用五次 smootherstep `t³(t(6t-15)+10)`，最终透明度为 `1-smootherstep(t)`，使透明度、一阶变化率和二阶变化率都能在区间两端连续衔接。

RawImage 的 `uvRect` 会改变主纹理 UV，因此系统继续同步写入 `_MaskUvTransform`，把主纹理 UV 还原为控件局部零到一坐标后再计算中心距离，保证地图移动时虚化边界不跟着地图内容移动。

## 扩展约束

- 需要手工控制地图范围时，应增加显式场景配置组件，不要在 HUD 中保存世界坐标常量。
- 动态任务点、敌人点和传送点应作为独立 UI 图标映射到同一世界范围，不要重新拍摄整张地图。
- 改变地图缩放时只调整采样窗口比例；玩家标记的样式、显隐和旋转由 UI 层独立管理。
