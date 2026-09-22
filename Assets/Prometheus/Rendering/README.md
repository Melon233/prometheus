# Universal Render Pipeline

## 管线归属

项目在 Unity 6000.6.2f1 与 URP 17.6.0 下维护六份不可变的 URP Asset：PC Forward Low/Mid、PC Deferred Low/Mid、Mobile Forward Low/Mid。平台、渲染路径和画质是三个独立维度；移动端固定使用 Forward，PC 可以通过 `PrometheusRenderQualityController.ApplyRenderPath` 在 Forward 与 Deferred 之间切换，PC 和移动端都只暴露 Low 与 Mid 两档画质。

`PrometheusRenderQualityController` 在首个场景加载前读取 `Resources/PrometheusRenderingSettings`，根据 `Application.isMobilePlatform`、外部启动路径和启动画质直接选择对应的序列化 URP Asset。运行时不再克隆并修改共享管线，因此 Low 与 Mid 不会相互污染 RendererData，移动端也不会误用 PC Deferred Renderer。

Low 是严格的最低画质：URP Asset 关闭主光源和附加实时光源、所有实时阴影、HDR、MSAA、Depth Texture、Opaque Texture 与可选 Renderer Feature；相机同步关闭后处理、抖动、实时阴影、HDR 和抗锯齿。PC Low 使用 0.75 Render Scale，Mobile Low 使用 0.65 Render Scale，并分别降低纹理和 LOD 预算。

Mid 是均衡档：PC 使用 1.0 Render Scale、HDR、主光源软阴影、60 米阴影距离、2 级联和最多 4 个附加逐像素光；Forward 使用 2x MSAA，Deferred 使用 SMAA，并仅在 PC Deferred Mid 中启用可选 Shiny SSR。移动端使用 0.85 Render Scale、无 HDR、2x MSAA、主光源硬阴影、25 米单级联和最多 2 个附加逐像素光，保留常规后处理但不包含桌面屏幕空间 Renderer Feature。

## Asset layout

The project-owned rendering boundary is organized as follows:

- `Pipeline` 包含六份 URP Asset，以及 PC Forward、PC Deferred Low、PC Deferred Mid、Mobile Forward 四份 RendererData。
- `Settings/Resources/PrometheusRenderingSettings.asset` 保存六份管线引用、PC 启动路径、PC/Mobile 启动画质和四份平台画质配置。
- `Settings/PrometheusEnvironmentProfile.asset` contains daily curves and the spring, summer, autumn, and winter palettes.
- `Runtime` contains the pre-scene quality bootstrap and the world environment controller.
- `Editor` contains the idempotent asset generator at `Prometheus/Rendering/Create Or Update Rendering Assets`.
- `ShaderLibrary/PrometheusEnvironment.hlsl` defines the shared time, season, sun, ambient, fog, and quality shader-global contract.
- `Shaders/World`, `Shaders/Character`, `Shaders/Effects`, and `Shaders/UI` own project shader families while `Shaders/Hovl` retains migrated third-party replacements.

## Unity defaults and project takeover

Unity initially distributes rendering responsibility across several independent configuration layers:

1. `GraphicsSettings` selects the default Render Pipeline Asset and project-wide shader inclusion behavior.
2. Each `QualitySettings` entry can override that pipeline asset and also owns non-URP values such as LOD, texture mip limits, anisotropic filtering, and vertical synchronization.
3. `UniversalRenderPipelineAsset` owns renderer selection, HDR, render scale, MSAA, light modes, shadow support, shadow atlases, cascades, depth texture, opaque texture, and pipeline batching.
4. `UniversalRendererData` owns forward or deferred rendering, renderer features, layer masks, depth behavior, stencil defaults, and pass scheduling.
5. Camera additional data can override post-processing, depth, opaque texture, anti-aliasing, renderer index, and shadow participation per camera.
6. Volumes own exposure, color grading, bloom, fog-like post effects, and other camera-local presentation values.
7. Materials and shaders own surface inputs, lighting response, passes, depth state, culling, transparency, and project-specific visual rules.

Prometheus 通过六份明确资产接管前三层，并由 `CameraSystem` 把第四层相机开关同步到当前档位。业务设置界面不要直接修改 URP Asset 或 RendererData；画质调用 `PrometheusRenderQualityController.ApplyQuality`，PC 渲染路径调用 `PrometheusRenderQualityController.ApplyRenderPath`。Unity 原生 QualitySettings 只保留 Low/Mid 两个兼容入口，外部调用 `QualitySettings.SetQualityLevel` 后控制器会在当前平台和当前路径内重新选择正确资产。

## Day, night, and seasons

`PrometheusEnvironmentProfile` evaluates normalized day time with midnight at `0`, sunrise at `0.25`, noon at `0.5`, and sunset at `0.75`. It owns sun elevation, sun color and intensity, Trilight ambient colors and intensity, reflection intensity, fog color and density, and explicit palettes for all four seasons.

`PrometheusEnvironmentController` requires an authored profile and one directional world light. It writes the evaluated result to that light, `RenderSettings`, and the shared shader globals. Seasonal interpolation always progresses from the selected season to the next ordered season. Runtime quality changes also change the sun between hard and soft shadows according to the active profile while the pipeline retains the shader capability required by every quality level.

The initial environment controller does not create a moon, weather, cloud shadows, precipitation, vegetation geometry changes, snow accumulation, or seasonal texture swaps. Those systems should consume the same normalized time, ordered season transition, and shader-global contract instead of maintaining independent clocks.

To activate the environment in a scene, add `PrometheusEnvironmentController` to one persistent world object, assign `PrometheusEnvironmentProfile.asset`, and assign the scene's directional sun. The controller deliberately fails when either required reference is missing instead of silently reverting to Unity RenderSettings.

## Gradient Sun Skybox

`Prometheus/Rendering/Skybox/Gradient Sun` 是项目自有的 URP 天空盒 Shader。其材质 Inspector 使用一个 HDR Gradient Bar 编辑天底、地平线和天顶之间的完整颜色分布，并将最多 8 个颜色键、8 个透明度键和 Gradient Mode 直接序列化进材质；它不生成渐变贴图，因此复制、移动或打包材质时不会产生外部纹理依赖。Shader 支持 Blend、Fixed 和 Perceptual Blend，后者在 Oklab 空间插值后返回线性 RGB。

Shader 从 URP Main Light 读取 Lighting Settings 中 Sun Source 的实时方向和颜色。太阳盘与光晕始终随 Sun Source 旋转；`Sun Rotation Influence` 为 `0` 时渐变固定为世界空间从下到上，为 `1` 时整个渐变轴跟随太阳方向旋转。修改当前场景 Skybox 材质的 Gradient Bar 或太阳参数时，`PrometheusGradientSkyboxShaderGUI` 会调用 `DynamicGI.UpdateEnvironment`，同步刷新默认 Ambient Probe；连续旋转太阳时若要求漫反射环境光也逐帧变化，时间系统仍需在太阳更新后显式请求环境捕获，不能只依靠天空盒画面的实时变化。

## World grid shader

`Prometheus/Rendering/World/Grid Lit` provides a UV-independent, object-anchored triplanar grid for opaque world geometry. `Cell Side Length` and `Line Width` are physical world-unit dimensions; `Base Color` and `Line Color` define the two grid colors. `Grid Offset` adjusts alignment relative to the object pivot, while `Projection Sharpness` controls transitions between the three planar projections. The material remains fully Lit and supports URP shadows, additional lights, baked GI, fog, metallic, smoothness, and optional Prometheus seasonal tinting. Its main-light shadow coordinates follow URP Lit's variant policy: non-cascade variants may interpolate vertex shadow coordinates, while cascade variants select and transform the correct cascade per pixel so large, low-density meshes do not produce triangle-shaped shadow bands.

Grid Lit 同时实现 `UniversalForward` 与 `UniversalGBuffer` Pass，并由两条路径共享同一份程序化网格 SurfaceData。默认 Forward Renderer 使用前者，专用 Deferred Renderer 使用后者；GBuffer 必须写入网格 Albedo、金属度响应、Occlusion、世界法线、Smoothness 和烘焙 GI，屏幕空间 Renderer Feature 直接从 `_GBuffer0` 与 `_GBuffer1` 读取接收面的材质数据。禁止将主 Pass 改为 `UniversalForwardOnly`，否则网格虽然仍能显示直射光，却会因为 GBuffer Albedo 缺失而无法接收间接光。

## Material migration rules

Unity's URP material upgrader owns Standard-to-URP/Lit conversion so texture, color, transparency, metallic, smoothness, emission, and render-queue data are preserved by the package implementation. Legacy additive and alpha particle materials use `Universal Render Pipeline/Particles/Unlit`; their original main texture, tint, texture transform, blend mode, and soft-particle distance are copied from each material instead of using hard-coded expected values.

Spine atlas materials that used `Spine/Skeleton`, `Spine/Skeleton Lit`, or `Spine/Sprite/Unlit` use the matching shaders from the embedded Spine URP shader package. Spine UI, outline-graphic, and special blend-mode materials remain on their dedicated shaders because the embedded package does not provide equivalent URP variants for those modes and their unlit passes remain SRP-compatible.
`Universal Render Pipeline/Spine/Sprite` materials retain their authored blend, emission, and normal modes during migration. A main texture imported with `Alpha Is Transparency` uses Standard Alpha rather than Premultiplied Alpha, an assigned visible emission texture enables emission, and a material without a normal map uses one fixed-normal mode. In particular, flat animated character meshes use `_FIXED_NORMALS_VIEWSPACE` so per-triangle mesh normals cannot appear as lighting folds.

The embedded Spine URP package is version 3.8.2 and originally targeted URP 7.1.5. Its 3D Sprite lighting input now uses URP 17 `InputData`, and its 2D passes now use `SurfaceData2D`, `InputData2D`, and the current combined shape-light helper without duplicate declarations. Keep these compatibility changes when updating package contents unless the replacement package explicitly supports URP 17.

The original Hovl source package remains unchanged. Materials that previously depended on Surface Shaders or `GrabPass` are redirected to URP-native replacements under `Assets/Prometheus/Rendering/Shaders/Hovl`. `BlendDistort` and `Distortion` sample `_CameraOpaqueTexture`; all depth-aware replacements sample `_CameraDepthTexture`.

## 已移除的 SSGI 链路

Unity 6000.6 的 URP 17.6 删除了 Compatibility Mode 及其 `ScriptableRenderPass.Configure` / `Execute` 入口，只保留 `RecordRenderGraph`。导入的 `Assets/Trd/MF.SSGI` 完全建立在被删除的 API 上，无法在该版本编译，因此已从项目中移除：Renderer 不再挂载 SSGI Feature，Volume Profile 不再包含 `SSGIVolumeComponent`，`CameraSystem` 不再按类型名添加 `SSGICamera`，对应的 `link.xml` 一并删除。PC Deferred Mid 现在只保留可选 Shiny SSR。若要恢复屏幕空间 GI，需要引入一个原生支持 RenderGraph 的方案，而不是恢复当前这套 Pass；静态室内漫反射仍由烘焙 GI 或 Adaptive Probe Volumes 负责。

## Shiny SSR 可选链路

`PrometheusPcDeferredMidRenderer` 承载 `ShinySSRR.ShinySSRR`。生成器从 Shiny 自带 Renderer 克隆一份 SSR Feature 作为项目 Renderer 的子资源。业务设置调用 `PrometheusRenderQualityController.SetScreenSpaceReflectionsEnabled(bool)` 保存玩家意图；Low、Forward 或 Mobile 会强制关闭有效 SSR Pass，重新进入 PC Deferred Mid 后才恢复玩家选择。

生成器会在每个项目自有的 Volume Profile 中补充一个启用的 `ShinyScreenSpaceRaytracedReflections` 组件，只在组件缺失时应用 Shiny 的 Medium 初始预设，因此后续手工调整的反射距离、步数、粗糙度和强度不会被重复生成覆盖。Renderer Feature、运行时总开关、相机 Post Processing 条件以及 Volume 的 `reflectionsMultiplier` 和 `reflectionsMaxIntensity` 必须同时允许执行，任一层关闭都会看不到 SSR。当前初始配置关闭 Shiny 的 `temporalFilter`，避免 SSR 自身的时域拖影；确需开启时，应单独观察反射边缘并调整 Shiny 的时域响应。

角色与敌人的 Spine Sprite 保持透明队列和 `ZWrite Off`，因此生成器会动态解析当前 `Character`、`Enemy` Layer 并把它们合入 Shiny Renderer Feature 的 Transparency Depth Prepass Layer Mask，使 SSR 射线能够命中角色而不改变正常透明绘制。角色朝向通过 `Skeleton.ScaleX` 正负值切换，会同时反转生成网格的三角形绕序；Shiny 的 Legacy Compatibility 与 RenderGraph 深度路径必须共用 `CullMode.Off` 的透明深度替换材质，否则其中一个朝向会被背面剔除并从反射中消失。该预通道只负责让角色作为反射来源参与射线命中；若未来要求角色表面本身接收 SSR，还必须为 Spine Shader 实现透明 `DepthNormals` Pass 并单独开启透明法线预通道，不能通过打开全部透明层或修改角色主材质的 ZWrite 代替。

Shiny SSR 只计算屏幕内镜面反射，不提供漫反射弹射，也不能反射相机外物体。它适合地面、水面、金属和高 Smoothness 材质；室内暗区的稳定多次漫反射仍由烘焙 GI 或 Adaptive Probe Volumes 负责。运行时 UI 的典型绑定只需要在开关回调中调用 `PrometheusRenderQualityController.SetScreenSpaceReflectionsEnabled(toggleValue)`，无需重新选择 Renderer 或替换 URP Asset。

## UI alpha mask shader

`Prometheus/UI/Alpha Mask` 保存在 `Shaders/UI/Resources`，保证运行时通过 `Resources.Load<Shader>("MaskUIShader")` 取得它时不会被 Player 构建裁剪。Shader 保留 Unity UI 的 Stencil、RectMask2D 与 AlphaClip 协议，不再读取外部 alpha 贴图，而是以控件中心为原点计算归一化径向距离。`_FadeStartDistance` 以内保持原始控件 alpha，`_FadeCompleteDistance` 以外透明度为零，中间固定使用五次 smootherstep `t³(t(6t-15)+10)` 并以 `1-smootherstep(t)` 作为透明度，使透明度、一阶变化率和二阶变化率在虚化区间两端连续衔接；主纹理仍只负责 RGB，顶点颜色 alpha 继续作为控件整体透明度。

`_MaskUvTransform` 用于把发生平移或缩放的主纹理 UV 还原为控件局部 UV，小地图的 RawImage 每次修改 `uvRect` 后必须同步更新该参数。每个需要不同虚化距离或 UV 变换的 UI 实例必须持有独立材质，不能修改共享 Material 资产；普通 Image 或 RawImage 在主 UV 未变化时保持 `_MaskUvTransform=(1,1,0,0)` 即可。

## Adding rendering content

New opaque or lit materials must use URP shaders. New refraction effects must sample the URP opaque texture rather than add a `GrabPass`. New soft particles must use the URP depth texture. Do not assign Built-in `Standard`, legacy particle shaders, the migrated Built-in Spine shaders, or the four replaced Hovl shaders to project materials.

Run `Prometheus.Rendering.EditorTests` after importing or changing materials, shaders, quality levels, renderer data, or pipeline assets. The tests dynamically enumerate all current quality levels and project materials, so the expected result follows the project configuration instead of relying on a fixed material count or fixed quality count.
