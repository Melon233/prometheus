# Universal Render Pipeline

## Pipeline ownership

The project uses Universal Render Pipeline 17.3.0 under Unity 6000.3.10f1. `GraphicsSettings.defaultRenderPipeline` is assigned to a URP asset, and every entry in `QualitySettings` owns a URP asset generated from that quality level's previous Built-in settings. The assets are stored in `Assets/URPDefaultResources`; all quality assets share `Default_Forward_Renderer.asset` because the project currently requires one forward renderer configuration.

Every quality asset enables Camera Depth Texture for soft-particle intersection fading and Camera Opaque Texture for refraction. These are project-level requirements of the migrated Hovl effects, not optional camera overrides. Cameras without `UniversalAdditionalCameraData` inherit both settings directly from the active pipeline asset.

## Material migration rules

Unity's URP material upgrader owns Standard-to-URP/Lit conversion so texture, color, transparency, metallic, smoothness, emission, and render-queue data are preserved by the package implementation. Legacy additive and alpha particle materials use `Universal Render Pipeline/Particles/Unlit`; their original main texture, tint, texture transform, blend mode, and soft-particle distance are copied from each material instead of using hard-coded expected values.

Spine atlas materials that used `Spine/Skeleton`, `Spine/Skeleton Lit`, or `Spine/Sprite/Unlit` use the matching shaders from the embedded Spine URP shader package. Spine UI, outline-graphic, and special blend-mode materials remain on their dedicated shaders because the embedded package does not provide equivalent URP variants for those modes and their unlit passes remain SRP-compatible.

The embedded Spine URP package is version 3.8.2 and originally targeted URP 7.1.5. Its 3D Sprite lighting input now uses URP 17 `InputData`, and its 2D passes now use `SurfaceData2D`, `InputData2D`, and the current combined shape-light helper without duplicate declarations. Keep these compatibility changes when updating package contents unless the replacement package explicitly supports URP 17.

The original Hovl source package remains unchanged. Materials that previously depended on Surface Shaders or `GrabPass` are redirected to URP-native replacements under `Assets/Prometheus/Rendering/Shaders/Hovl`. `BlendDistort` and `Distortion` sample `_CameraOpaqueTexture`; all depth-aware replacements sample `_CameraDepthTexture`.

## Adding rendering content

New opaque or lit materials must use URP shaders. New refraction effects must sample the URP opaque texture rather than add a `GrabPass`. New soft particles must use the URP depth texture. Do not assign Built-in `Standard`, legacy particle shaders, the migrated Built-in Spine shaders, or the four replaced Hovl shaders to project materials.

Run `Prometheus.Rendering.EditorTests` after importing or changing materials, shaders, quality levels, renderer data, or pipeline assets. The tests dynamically enumerate all current quality levels and project materials, so the expected result follows the project configuration instead of relying on a fixed material count or fixed quality count.
