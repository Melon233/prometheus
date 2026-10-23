using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RenderDocToUnity
{
    /// <summary>
    /// Render pipeline for reconstructed captures. Every camera renders the active <see cref="RenderDocScene"/>: the
    /// captured passes (G-buffer, lighting, shadows, fog, clouds, post-processing) run with the camera's view,
    /// projection and pixel size and the scene objects' Transforms, then the final image is presented to the camera
    /// target. Game view and Scene view go through the same path, so they show the same rendering.
    /// </summary>
    [CreateAssetMenu(menuName = "Rendering/RenderDoc Replay Pipeline", fileName = "RenderDocReplayPipeline")]
    public class RenderDocPipelineAsset : RenderPipelineAsset<RenderDocPipeline>
    {
        public Color emptyColor = new Color(0.19f, 0.19f, 0.2f, 1f);
        [Tooltip("Supersampling: the captured frame renders at this multiple of the camera's pixel size and is box-filtered " +
                 "down (2 = 4 samples per pixel). The capture's own anti-aliasing (jittered TAA) cannot run from a free viewpoint, " +
                 "and its half/quarter-resolution effects (fog, clouds, shadows, bloom) gain resolution with it. Cost grows with the square.")]
        [Range(1f, 4f)] public float renderScale = 2f;
        protected override RenderPipeline CreatePipeline() => new RenderDocPipeline(this);
    }

    public class RenderDocPipeline : RenderPipeline
    {
        readonly RenderDocPipelineAsset asset;
        public RenderDocPipeline(RenderDocPipelineAsset asset) { this.asset = asset; }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras) => Render(context, new List<Camera>(cameras));

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (var cam in cameras)
            {
                context.SetupCameraProperties(cam);
                var cmd = new CommandBuffer { name = "RenderDoc scene: " + cam.name };
                var scene = RenderDocScene.Active;
                int w = cam.pixelWidth, h = cam.pixelHeight;
                bool sceneCamera = cam.cameraType == CameraType.Game || cam.cameraType == CameraType.SceneView;
                if (scene != null && scene.frame != null && scene.frame.hasCamera && sceneCamera && w > 0 && h > 0)
                {
                    // camera target textures (Scene view, cameras with a targetTexture) hold rows bottom-up on D3D-style
                    // APIs; the Game view and the back buffer are top-down
                    bool targetRowsBottomUp = SystemInfo.graphicsUVStartsAtTop && cam.targetTexture != null;
                    scene.Render(cmd, cam, BuiltinRenderTextureType.CameraTarget, w, h, targetRowsBottomUp, asset.renderScale);
                    cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                    cmd.ClearRenderTarget(true, false, Color.clear);   // fresh depth for gizmos and handles
                }
                else
                {
                    cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                    cmd.ClearRenderTarget(true, true, asset.emptyColor);
                }
                context.ExecuteCommandBuffer(cmd);
                cmd.Release();
#if UNITY_EDITOR
                if (cam.cameraType == CameraType.SceneView) ScriptableRenderContext.EmitWorldGeometryForSceneView(cam);
                if (Handles.ShouldRenderGizmos())
                {
                    context.DrawGizmos(cam, GizmoSubset.PreImageEffects);
                    context.DrawGizmos(cam, GizmoSubset.PostImageEffects);
                }
#endif
                context.Submit();
            }
        }
    }
}
