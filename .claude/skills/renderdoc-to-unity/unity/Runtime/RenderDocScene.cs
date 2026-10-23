using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace RenderDocToUnity
{
    /// <summary>
    /// Root of a reconstructed capture. While enabled it makes <see cref="RenderDocPipelineAsset"/> the active render
    /// pipeline, which renders every camera (Game view, Scene view, cameras with target textures) through the captured
    /// frame: the camera's view/projection and resolution, and the children's <see cref="RenderDocObject"/> Transforms,
    /// replace the captured values in the original shaders' constant buffers. Disabling it restores the previous pipeline.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class RenderDocScene : MonoBehaviour
    {
        public RenderDocFrame frame;
        [Tooltip("Pipeline that renders this scene; made active while this component is enabled.")]
        public RenderDocPipelineAsset pipeline;
        public bool activatePipeline = true;

        public static RenderDocScene Active { get; private set; }
        static bool dirty = true;
        public static void MarkDirty() => dirty = true;

        readonly Dictionary<Camera, RenderDocFrameRenderer> renderers = new Dictionary<Camera, RenderDocFrameRenderer>();
        RenderDocObject[] byIndex = new RenderDocObject[0];
        Material present;
        RenderPipelineAsset previousQualityPipeline;
        bool switched;

        void OnEnable()
        {
            Active = this;
            dirty = true;
            if (activatePipeline && pipeline != null && QualitySettings.renderPipeline != pipeline)
            {
                previousQualityPipeline = QualitySettings.renderPipeline;
                QualitySettings.renderPipeline = pipeline;
                switched = true;
            }
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
            if (switched && QualitySettings.renderPipeline == pipeline) QualitySettings.renderPipeline = previousQualityPipeline;
            switched = false;
            foreach (var r in renderers.Values) r.Dispose();
            renderers.Clear();
        }

        void OnTransformChildrenChanged() => dirty = true;

        void CollectObjects()
        {
            if (!dirty && byIndex.Length == (frame != null ? frame.objects.Length : 0)) return;
            byIndex = new RenderDocObject[frame != null ? frame.objects.Length : 0];
            foreach (var o in GetComponentsInChildren<RenderDocObject>(true))
                if (o.index >= 0 && o.index < byIndex.Length) byIndex[o.index] = o;
            dirty = false;
        }

        /// <summary>Unity world -> capture world. Identity when the capture's view matrix is itself left-handed like Unity's.</summary>
        public Matrix4x4 UnityToCaptureWorld =>
            frame != null && Det3(frame.captureView) > 0 ? Matrix4x4.Scale(new Vector3(1, 1, -1)) : Matrix4x4.identity;

        static bool Near(Matrix4x4 a, Matrix4x4 b, float tol)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > tol * Mathf.Max(1f, Mathf.Abs(b[i]))) return false;
            return true;
        }

        static float Det3(Matrix4x4 m) =>
            m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20);

        /// <summary>Unity camera pose whose view reproduces the captured camera.</summary>
        public void CapturePose(out Vector3 position, out Quaternion rotation, out float verticalFov)
        {
            var flipZ = Matrix4x4.Scale(new Vector3(1, 1, -1));
            var localToWorld = (flipZ * frame.captureView * UnityToCaptureWorld).inverse;
            position = localToWorld.GetColumn(3);
            rotation = Quaternion.LookRotation(localToWorld.GetColumn(2), localToWorld.GetColumn(1));
            verticalFov = 2f * Mathf.Atan(1f / frame.captureProj.m11) * Mathf.Rad2Deg;
        }

        public RenderDocFrameRenderer RendererFor(Camera cam)
        {
            // drop renderers of destroyed cameras (Scene views come and go)
            if (renderers.Count > 4)
                foreach (var dead in new List<Camera>(renderers.Keys))
                    if (dead == null) { renderers[dead].Dispose(); renderers.Remove(dead); }
            if (!renderers.TryGetValue(cam, out var r) || r.frame != frame)
            {
                r?.Dispose();
                r = new RenderDocFrameRenderer(frame);
                renderers[cam] = r;
            }
            return r;
        }

        /// <summary>The view a Unity camera sees: its own pose and field of view, the capture's depth range, the current objects.</summary>
        public RenderDocFrameRenderer.View ViewFor(Camera cam, int width, int height, RenderDocFrameRenderer r)
        {
            CollectObjects();
            var objs = new Matrix4x4[frame.objects.Length];
            for (int i = 0; i < objs.Length; i++)
            {
                var o = byIndex[i];
                objs[i] = o == null || !o.isActiveAndEnabled ? RenderDocFrameRenderer.Zero : o.ObjectMatrix(frame.objects[i].model);
            }
            var view = cam.worldToCameraMatrix * UnityToCaptureWorld;
            var proj = r.ProjectionFor(cam.fieldOfView, (float)width / Mathf.Max(1, height));
            bool atCapture = width == frame.captureWidth && height == frame.captureHeight && Near(view, frame.captureView, 1e-4f) && Near(proj, frame.captureProj, 2e-3f);
            return new RenderDocFrameRenderer.View(view, proj, objs) { atCapture = atCapture };
        }

        /// <summary>
        /// Record the whole frame for a camera at <paramref name="renderScale"/> times its pixel size (supersampling: every
        /// screen-relative buffer scales with it) and present the final image, box-filtered down, into <paramref name="target"/>.
        /// </summary>
        public void Render(CommandBuffer cmd, Camera cam, RenderTargetIdentifier target, int width, int height, bool targetRowsBottomUp, float renderScale = 1f)
        {
            var r = RendererFor(cam);
            int rw = Mathf.Max(1, Mathf.RoundToInt(width * renderScale)), rh = Mathf.Max(1, Mathf.RoundToInt(height * renderScale));
            r.Ensure(rw, rh);
            r.Record(cmd, ViewFor(cam, rw, rh, r));
            var final = r.Final;
            if (final == null) return;
            if (present == null) present = new Material(Shader.Find("Hidden/RenderDocToUnity/Present")) { hideFlags = HideFlags.HideAndDontSave };
            var gf = final.graphicsFormat;
            // 8-bit UNORM finals hold gamma-encoded values. Targets that hold linear values (sRGB storage, float/HDR
            // buffers like the editor Scene view) get them decoded; other non-sRGB targets are shown as stored.
            var tt = cam.targetTexture;
            bool gammaStored = QualitySettings.activeColorSpace == ColorSpace.Linear && (gf == GraphicsFormat.R8G8B8A8_UNorm || gf == GraphicsFormat.B8G8R8A8_UNorm);
            bool targetLinear = tt == null || tt.sRGB || GraphicsFormatUtility.IsHDRFormat(tt.graphicsFormat);
            // averaging (supersampling) happens in linear space; re-encode when the target stores gamma values as is
            bool encodeOut = gammaStored && !targetLinear;
            var mpb = new MaterialPropertyBlock();
            mpb.SetTexture("_RDFinal", final);
            // x: source gamma-encoded, y: flip rows (source bottom-up XOR target bottom-up), zw: source size
            mpb.SetVector("_RDParams", new Vector4(gammaStored ? 1 : 0, frame.rowsBottomUp != targetRowsBottomUp ? 1 : 0, final.width, final.height));
            mpb.SetVector("_RDTargetSize", new Vector4(width, height, encodeOut ? 1 : 0, 0));
            cmd.SetRenderTarget(target);
            cmd.SetViewport(new Rect(0, 0, width, height));
            cmd.DrawProcedural(Matrix4x4.identity, present, 0, MeshTopology.Triangles, 3, 1, mpb);
        }
    }
}
