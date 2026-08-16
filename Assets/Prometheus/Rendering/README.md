# Universal Render Pipeline

## Pipeline ownership

The project uses Universal Render Pipeline 17.3.0 under Unity 6000.3.10f1. Prometheus owns one serialized pipeline asset at `Assets/Prometheus/Rendering/Pipeline/PrometheusUniversalRenderPipeline.asset`, one default forward renderer at `Assets/Prometheus/Rendering/Pipeline/PrometheusForwardRenderer.asset`, and one dedicated deferred SSGI renderer at `Assets/Prometheus/Rendering/Pipeline/PrometheusDeferredSsgiRenderer.asset`. `GraphicsSettings.defaultRenderPipeline` and every legacy Unity quality-level override reference that same pipeline asset, so Unity quality names never select different rendering assets.

`PrometheusRenderQualityController` loads `PrometheusRenderingSettings` before the first scene, clones the single serialized pipeline asset once, assigns that non-persistent clone to `QualitySettings.renderPipeline`, and applies one complete `PrometheusRenderQualityProfile`. Runtime quality changes always mutate this one runtime copy. A Unity quality-index change cannot replace it because the controller immediately reasserts the runtime copy and the current Prometheus profile.

Camera Depth Texture and Camera Opaque Texture remain project-wide requirements because the migrated Hovl soft particles and refraction effects consume them. Scene cameras inherit the pipeline's default renderer unless an intentional override selects an index that currently exists in the pipeline renderer list; stale renderer indices from imported scenes are invalid because URP falls back and emits a warning every rendered frame. The SRP batcher remains enabled, dynamic batching and Adaptive Performance remain disabled, and all runtime quality profiles share per-pixel main and additional Lit lighting plus main, additional, and soft-shadow shader capabilities. Quality profiles change budgets such as render scale, MSAA, shadow atlas resolution, shadow distance, cascade count, additional-light count, texture mip limit, LOD bias, anisotropic filtering, vertical synchronization, and target frame rate; they do not remove shader capabilities and cause runtime shader-variant gaps.

## Asset layout

The project-owned rendering boundary is organized as follows:

- `Pipeline` contains the only serialized URP asset, the default forward renderer, and the dedicated deferred SSGI renderer.
- `Settings/Resources/PrometheusRenderingSettings.asset` contains startup quality, invariant pipeline requirements, and every code-controlled quality profile.
- `Settings/PrometheusEnvironmentProfile.asset` contains daily curves and the spring, summer, autumn, and winter palettes.
- `Runtime` contains the pre-scene quality bootstrap and the world environment controller.
- `Editor` contains the idempotent asset generator at `Prometheus/Rendering/Create Or Update Rendering Assets`.
- `ShaderLibrary/PrometheusEnvironment.hlsl` defines the shared time, season, sun, ambient, fog, and quality shader-global contract.
- `Shaders/World`, `Shaders/Character`, and `Shaders/Effects` own project shader families while `Shaders/Hovl` retains migrated third-party replacements.

## Unity defaults and project takeover

Unity initially distributes rendering responsibility across several independent configuration layers:

1. `GraphicsSettings` selects the default Render Pipeline Asset and project-wide shader inclusion behavior.
2. Each `QualitySettings` entry can override that pipeline asset and also owns non-URP values such as LOD, texture mip limits, anisotropic filtering, and vertical synchronization.
3. `UniversalRenderPipelineAsset` owns renderer selection, HDR, render scale, MSAA, light modes, shadow support, shadow atlases, cascades, depth texture, opaque texture, and pipeline batching.
4. `UniversalRendererData` owns forward or deferred rendering, renderer features, layer masks, depth behavior, stencil defaults, and pass scheduling.
5. Camera additional data can override post-processing, depth, opaque texture, anti-aliasing, renderer index, and shadow participation per camera.
6. Volumes own exposure, color grading, bloom, fog-like post effects, and other camera-local presentation values.
7. Materials and shaders own surface inputs, lighting response, passes, depth state, culling, transparency, and project-specific visual rules.

Prometheus takes over layers one through four by assigning one project pipeline everywhere, applying runtime budgets through `PrometheusRenderQualityController`, and reserving renderer features for explicitly authored project passes. Camera and Volume overrides remain available, but every use must be intentional and documented because they can otherwise bypass global quality or environment decisions. Materials remain Lit unless their function is inherently emissive, additive, UI, or another explicitly documented exception.

Do not call `QualitySettings.SetQualityLevel` from game settings UI. Call `PrometheusRenderQualityController.ApplyQuality` with a `PrometheusRenderQualityLevel`. Unity quality levels remain only as platform bootstrap compatibility entries and all point to the same serialized pipeline asset.

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

Grid Lit 同时实现 `UniversalForward` 与 `UniversalGBuffer` Pass，并由两条路径共享同一份程序化网格 SurfaceData。默认 Forward Renderer 使用前者，专用 Deferred SSGI Renderer 使用后者；GBuffer 必须写入网格 Albedo、金属度响应、Occlusion、世界法线、Smoothness 和烘焙 GI，因为 MF.SSGI 的 Deferred 合成直接从 `_GBuffer0` 与 `_GBuffer1` 读取接收面的材质数据。禁止将主 Pass 改为 `UniversalForwardOnly`，否则网格虽然仍能显示直射光，却会因为 GBuffer Albedo 缺失而无法接收间接光。

## Material migration rules

Unity's URP material upgrader owns Standard-to-URP/Lit conversion so texture, color, transparency, metallic, smoothness, emission, and render-queue data are preserved by the package implementation. Legacy additive and alpha particle materials use `Universal Render Pipeline/Particles/Unlit`; their original main texture, tint, texture transform, blend mode, and soft-particle distance are copied from each material instead of using hard-coded expected values.

Spine atlas materials that used `Spine/Skeleton`, `Spine/Skeleton Lit`, or `Spine/Sprite/Unlit` use the matching shaders from the embedded Spine URP shader package. Spine UI, outline-graphic, and special blend-mode materials remain on their dedicated shaders because the embedded package does not provide equivalent URP variants for those modes and their unlit passes remain SRP-compatible.
`Universal Render Pipeline/Spine/Sprite` materials retain their authored blend, emission, and normal modes during migration. A main texture imported with `Alpha Is Transparency` uses Standard Alpha rather than Premultiplied Alpha, an assigned visible emission texture enables emission, and a material without a normal map uses one fixed-normal mode. In particular, flat animated character meshes use `_FIXED_NORMALS_VIEWSPACE` so per-triangle mesh normals cannot appear as lighting folds.

The embedded Spine URP package is version 3.8.2 and originally targeted URP 7.1.5. Its 3D Sprite lighting input now uses URP 17 `InputData`, and its 2D passes now use `SurfaceData2D`, `InputData2D`, and the current combined shape-light helper without duplicate declarations. Keep these compatibility changes when updating package contents unless the replacement package explicitly supports URP 17.

The original Hovl source package remains unchanged. Materials that previously depended on Surface Shaders or `GrabPass` are redirected to URP-native replacements under `Assets/Prometheus/Rendering/Shaders/Hovl`. `BlendDistort` and `Distortion` sample `_CameraOpaqueTexture`; all depth-aware replacements sample `_CameraDepthTexture`.

## MF.SSGI 兼容链路

当前导入的 `Assets/MF.SSGI` 仍实现为传统 `ScriptableRenderPass.Execute`，因此依赖 `UniversalRenderPipelineGlobalSettings.asset` 中已经启用的 URP Compatibility Mode。Unity 6000.3 还要求当前构建目标定义 `URP_COMPATIBILITY_MODE`；只有资源中的 `m_EnableRenderCompatibilityMode: 1` 而没有该编译符号时，`RenderGraphSettings.enableRenderCompatibilityMode` 会固定返回 `false`，相机继续走 `ExecuteRenderGraph`，MF.SSGI Feature 虽然存在却不会执行。其相机颜色与深度附件必须使用 URP 17 的 `RTHandle` API，并且只能在 `ScriptableRenderPass` 的执行调用链内即时取得；禁止在 `AddRenderPasses`、跨相机字段或跨帧状态中缓存相机附件。若未来关闭 Compatibility Mode，必须先将 MF.SSGI 的绘制、临时纹理和最终合成完整迁移到 `RecordRenderGraph`，不能仅关闭开关后保留当前 Pass。

Prometheus 管线固定保留两类 Renderer：默认的 `PrometheusForwardRenderer` 不挂载 SSGI，供普通相机、反射探针和不需要屏幕空间间接光的视图使用；`PrometheusDeferredSsgiRenderer` 使用 Deferred 渲染并独占一个启用的 `MF.SSGI.SSGIFeature`。该 Feature 的 `UseDeferredRendering` 必须与 Renderer 的实际渲染模式一致，因为开启时 Shader 会读取 `_GBuffer0` 与 `_GBuffer1`，不能把同一配置直接挂到 Forward Renderer。

需要 SSGI 的游戏相机必须同时满足两个条件：挂载 `MF.SSGI.SSGICamera`，并在 `UniversalAdditionalCameraData` 中选择当前管线里包含 `MF.SSGI.SSGIFeature` 的 Renderer。示例场景保持 `CameraLeft` 继承默认 Forward Renderer、`CameraRight` 选择专用 SSGI Renderer 的左右对照。业务代码和测试不得写死 Renderer 下标；回归测试从当前管线的 Feature 归属动态解析 Renderer，并验证每个 `SSGICamera` 的实际选择。

`PrometheusRenderingAssetGenerator` 会创建或修复这两个 Renderer，将导入示例中的 SSGI Feature 克隆为项目 Renderer 的子资源，并重建管线 Renderer 列表。生成器还会为当前构建目标补齐 `URP_COMPATIBILITY_MODE`，并执行插件文档指定的 `Tools/SSGI/Add SSGI to 'Always included shaders'`，确保 `Shader.Find` 使用的运行时 Shader 不会在 Player 构建时被剥离。MF.SSGI 示例 Renderer 是生成输入而不是运行时依赖；如果插件、示例 Feature 或 Shader 安装菜单缺失，生成过程会立即失败并报告缺失资源，不能静默生成一个没有 SSGI 的 Renderer。

示例场景 Play Mode 脚本会把显示分辨率缩放到自身配置的 `renderScale`，SSGI Quality 资源还会再次应用 `SSGIRenderScale`。这两个值只影响测试画面的采样清晰度和稳定速度，不代表 Renderer、Volume 或 Feature 链路缺失；判断功能是否接通应先在 Frame Debugger 确认右相机没有继续走 `ExecuteRenderGraph`，再检查右相机的 Renderer、`SSGICamera`、全局 Volume 和 SSGI Feature。插件文档要求使用 Debug Window 依次观察 `Screen Capture`、`Light Capture`、`SSGI Color` 与 `Final Denoised Color`，先判断中间光照纹理是否产出，再调整 `SSGI Range Min/Max`、`Scan Depth 2D Range Factor`、质量和 Volume 强度。

`YefaScene Postprocess Volume` 面向当前约 10×10×6 米的暗室测试采用纯 Additive 合成：`PreMultiply=0` 保留 URP 直射结果，`FinalIntensity=1` 不再把整个画面压到三分之一，`LightFalloffDistance=4` 让回弹光在房间尺度内传播，`LightIntensity=2` 提供可见的单次回弹，`ShadowIntensity=1` 只阻挡间接光而不额外压暗原始画面。专用 Renderer 的 `ScanDepth2DRangeFactor=0.35`，用于让画面内受光地板的单次回弹覆盖当前 6 米高墙面；默认值 `0.15` 只会覆盖墙面下部。MF.SSGI 仍是屏幕空间方案：相机至少需要保留一小段受光地板或其他反射源，正对墙且把地板完全裁出画面后，蓝色历史采样会在时域重投影中逐渐衰减，搜索半径再大也不能取得屏幕外光源。同一共面区域不会由自身直射亮斑产生物理上不存在的自照明；必须支持任意镜头构图时，静态室内漫反射由烘焙 GI 或 Adaptive Probe Volumes 提供，SSGI 只叠加动态屏幕内反弹，不能继续无上限提高 SSGI 强度或依赖 Reflection Probe 的镜面式 fallback 来伪造。

### 减少 SSGI 拖影

MF.SSGI 的主要拖影来源是 Quality 资源中的跨帧重投影。当前专用 Renderer 使用的质量资源开启了 `MultiFrameCellSize=3`，并以较高的历史能量保留率复用过去帧；相机转动、物体移动或遮挡关系突变时，历史间接光会比当前几何消失得更慢。排查时先把 `MultiFrameCellSize` 临时设为 `0`：若拖影消失，就能确认问题来自多帧复用而不是空间降噪、SSR 或材质。`2` 不是合法的折中值，MF.SSGI 的实现只应使用 `0` 或不小于 `3` 的 Cell Size。

正式配置优先保留 `MultiFrameCellSize=3`，把 `MultiFrameEnergyFalloff` 从接近完全保留历史的配置逐步降低到约 `0.90–0.94`，并把 Advanced 资源中的 `MultiFrameDistanceThreshold` 从宽松值逐步收紧到约 `0.02–0.04`。每次只改一个参数，并在 MF.SSGI Debug Window 中同时观察 Motion Vectors、Reprojection 和 Final Denoised Color：Falloff 越低，错误历史消失越快但噪声越明显；Distance Threshold 越小，重投影越不容易串到相邻表面但历史采样命中率会下降。所有移动 Renderer 必须输出有效 Motion Vector，顶点动画或自定义 Shader 也必须提供与运动一致的 MotionVectors Pass，否则仅调阈值无法彻底消除动态物体拖影。若要求镜头快速运动时完全无历史残留，使用 `MultiFrameCellSize=0` 并通过更高单帧采样或更强空间降噪承担额外成本。

### Shiny SSR 可选链路

`PrometheusDeferredSsgiRenderer` 同时承载 `MF.SSGI.SSGIFeature` 与 `ShinySSRR.ShinySSRR`。生成器从 Shiny 自带 Renderer 克隆一份 SSR Feature 作为项目 Renderer 的子资源，强制启用 Deferred GBuffer 路径，并根据当前 SSGI 的 `RenderPassEvent` 动态保证 SSR 排在 SSGI 合成之后，使镜面反射能够读取已经叠加间接光的相机颜色。Feature Inspector 中的 Active 勾选是资源级“能力开关”，应保持启用；业务设置不要运行时改写共享 RendererData，而应调用 `PrometheusRenderQualityController.SetScreenSpaceReflectionsEnabled(bool)`。该接口映射到 Shiny 官方的 `ShinySSRR.isEnabled` Pass Gate，关闭后不再入队 SSR Pass，并通过 `ScreenSpaceReflectionsEnabled` 与 `ScreenSpaceReflectionsChanged` 暴露当前状态和变更通知。启动默认值保存在 `PrometheusRenderingSettings.screenSpaceReflectionsEnabledByDefault`，质量档切换不会擅自覆盖玩家的 SSR 选择。

生成器会在每个项目自有且已经包含 MF.SSGI 的 Volume Profile 中补充一个启用的 `ShinyScreenSpaceRaytracedReflections` 组件，只在组件缺失时应用 Shiny 的 Medium 初始预设，因此后续手工调整的反射距离、步数、粗糙度和强度不会被重复生成覆盖。Renderer Feature、运行时总开关、相机 Post Processing 条件以及 Volume 的 `reflectionsMultiplier` 和 `reflectionsMaxIntensity` 必须同时允许执行，任一层关闭都会看不到 SSR。当前初始配置关闭 Shiny 的 `temporalFilter`，避免将 SSR 自身的时域拖影误认为 SSGI 拖影；确需开启时，应单独观察反射边缘并调整 Shiny 的时域响应。

角色与敌人的 Spine Sprite 保持透明队列和 `ZWrite Off`，因此生成器会动态解析当前 `Character`、`Enemy` Layer 并把它们合入 Shiny Renderer Feature 的 Transparency Depth Prepass Layer Mask，使 SSR 射线能够命中角色而不改变正常透明绘制。角色朝向通过 `Skeleton.ScaleX` 正负值切换，会同时反转生成网格的三角形绕序；Shiny 的 Legacy Compatibility 与 RenderGraph 深度路径必须共用 `CullMode.Off` 的透明深度替换材质，否则其中一个朝向会被背面剔除并从反射中消失。该预通道只负责让角色作为反射来源参与射线命中；若未来要求角色表面本身接收 SSR，还必须为 Spine Shader 实现透明 `DepthNormals` Pass 并单独开启透明法线预通道，不能通过打开全部透明层或修改角色主材质的 ZWrite 代替。

Shiny SSR 只计算屏幕内镜面反射，不会为 MF.SSGI 增加第二次漫反射弹射，也不能反射相机外物体。它适合地面、水面、金属和高 Smoothness 材质；室内暗区的稳定多次漫反射仍由烘焙 GI 或 Adaptive Probe Volumes 负责。运行时 UI 的典型绑定只需要在开关回调中调用 `PrometheusRenderQualityController.SetScreenSpaceReflectionsEnabled(toggleValue)`，无需重新选择 Renderer 或替换 URP Asset。

## Adding rendering content

New opaque or lit materials must use URP shaders. New refraction effects must sample the URP opaque texture rather than add a `GrabPass`. New soft particles must use the URP depth texture. Do not assign Built-in `Standard`, legacy particle shaders, the migrated Built-in Spine shaders, or the four replaced Hovl shaders to project materials.

Run `Prometheus.Rendering.EditorTests` after importing or changing materials, shaders, quality levels, renderer data, or pipeline assets. The tests dynamically enumerate all current quality levels and project materials, so the expected result follows the project configuration instead of relying on a fixed material count or fixed quality count.
