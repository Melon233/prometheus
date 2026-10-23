using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace RenderDocToUnity
{
    /// <summary>
    /// Records a captured frame into a CommandBuffer: every render target is recreated (screen-relative ones at the
    /// requested screen size), initial contents restored, then the captured passes, draws (decompiled shaders, raw
    /// constant buffers) and blits run in order. With a <see cref="View"/>, camera/object/screen-derived constants are
    /// recomputed (view and projection of a Unity camera, object matrices from Transforms, buffer sizes).
    /// One instance per output size (per camera); not thread-safe.
    /// </summary>
    public sealed class RenderDocFrameRenderer : IDisposable
    {
        public readonly RenderDocFrame frame;
        public RenderTexture[] targets = Array.Empty<RenderTexture>();
        public int ScreenWidth { get; private set; }
        public int ScreenHeight { get; private set; }
        public bool invertCulling = true;       // Unity flips culling when rendering into RenderTextures (D3D-style APIs)
        public bool convertClearDepth = true;   // Unity converts clear depth on reversed-Z platforms
        /// <summary>
        /// Diagnostic: start screen-sized targets cleared instead of with their captured initial contents. If validation then
        /// fails, part of the game's frame was drawn before the capture began (e.g. emulators: RenderDoc splits frames at the
        /// display thread's swap, not the game's) - that geometry exists only as initial contents and cannot follow a free view.
        /// </summary>
        public bool freshScreenTargets;
        public RenderTexture Final => frame.finalResource >= 0 && frame.finalResource < targets.Length ? targets[frame.finalResource] : null;

        readonly Dictionary<long, (GraphicsBuffer gb, float[] floats, Vector4[] vecs)> buffers = new Dictionary<long, (GraphicsBuffer, float[], Vector4[])>();
        readonly Dictionary<int, Mesh> rayMeshes = new Dictionary<int, Mesh>();   // per op: copy of the mesh with this view's rays
        Material writeDepth;
        readonly Material[] writeStencil = new Material[8];   // one per stencil bit (Ref/WriteMask are material render state)

        public RenderDocFrameRenderer(RenderDocFrame frame) { this.frame = frame; }

        /// <summary>Viewpoint and scene state for one rendering.</summary>
        public sealed class View
        {
            public Matrix4x4 view, proj;
            public Matrix4x4[] objectMatrices;  // per frame object: object-to-world (null entry = hidden); null array = captured
            /// <summary>Viewpoint and resolution are the captured ones: temporal history from the capture is still valid.</summary>
            public bool atCapture;
            internal Matrix4x4 viewProj, invViewProj, invView, worldToCamera, cameraToWorld, viewProjRot, invViewProjRot, invProj;
            internal Vector3 position;

            public View(Matrix4x4 view, Matrix4x4 proj, Matrix4x4[] objectMatrices = null)
            {
                this.view = view; this.proj = proj; this.objectMatrices = objectMatrices;
                invView = view.inverse; invProj = proj.inverse;
                viewProj = proj * view; invViewProj = viewProj.inverse;
                worldToCamera = Matrix4x4.Scale(new Vector3(1, 1, -1)) * view; cameraToWorld = worldToCamera.inverse;
                var rot = view; rot.SetColumn(3, new Vector4(0, 0, 0, 1));
                viewProjRot = proj * rot; invViewProjRot = viewProjRot.inverse;
                position = invView.GetColumn(3);
            }

            internal Matrix4x4? Object(int i) => objectMatrices == null || i < 0 || i >= objectMatrices.Length ? (Matrix4x4?)null : objectMatrices[i];
            internal bool Hidden(int i) => objectMatrices != null && i >= 0 && i < objectMatrices.Length && objectMatrices[i] == Zero;
        }

        /// <summary>Marks a hidden (disabled / deleted) object in <see cref="View.objectMatrices"/>.</summary>
        public static readonly Matrix4x4 Zero = new Matrix4x4();

        /// <summary>Projection for a Unity camera that keeps the capture's depth range (and so every depth-derived constant).</summary>
        public Matrix4x4 ProjectionFor(float verticalFovDegrees, float aspect)
        {
            var p = frame.captureProj;
            float y = 1f / Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            // the captured field of view and aspect keep the captured projection exactly (its TAA jitter included)
            if (Mathf.Abs(y - p.m11) < 1e-3f * p.m11 && Mathf.Abs(y / aspect - p.m00) < 1e-3f * Mathf.Abs(p.m00)) return p;
            p.m11 = y; p.m00 = y / aspect; p.m02 = 0; p.m12 = 0;
            return p;
        }

        // ------------------------------------------------------------------ targets

        public int LevelWidth(FrameResource r, int screen) => r.screenLevel <= 0 ? screen : (r.screenRoundUp ? (screen + (1 << r.screenLevel) - 1) >> r.screenLevel : screen >> r.screenLevel);

        Vector2Int SizeOf(FrameResource r) => r.screenLevel < 0
            ? new Vector2Int(r.width, r.height)
            : new Vector2Int(Mathf.Max(1, LevelWidth(r, ScreenWidth)), Mathf.Max(1, LevelWidth(r, ScreenHeight)));

        /// <summary>Allocate targets for the given screen size (captured size when 0). Returns true when they were (re)created.</summary>
        public bool Ensure(int screenWidth = 0, int screenHeight = 0)
        {
            if (screenWidth <= 0 || screenHeight <= 0) { screenWidth = frame.captureWidth; screenHeight = frame.captureHeight; }
            if (screenWidth == ScreenWidth && screenHeight == ScreenHeight && TargetsAlive()) return false;
            ReleaseTargets();
            ScreenWidth = screenWidth; ScreenHeight = screenHeight;
            targets = new RenderTexture[frame.resources.Length];
            for (int i = 0; i < frame.resources.Length; i++)
            {
                var r = frame.resources[i];
                if (!r.rt || r.kind != "2d") continue;
                if (!Enum.TryParse(r.unityFormat, out GraphicsFormat gf)) continue;
                var size = SizeOf(r);
                var desc = new RenderTextureDescriptor(size.x, size.y) { msaaSamples = 1, mipCount = 1, useMipMap = false };
                if (r.isDepth) { desc.graphicsFormat = GraphicsFormat.None; desc.depthStencilFormat = gf; }
                else { desc.graphicsFormat = gf; desc.depthStencilFormat = GraphicsFormat.None; }
                if (r.isDepth && r.compareSampler) desc.shadowSamplingMode = ShadowSamplingMode.CompareDepths;
                var rt = new RenderTexture(desc) { name = r.id, filterMode = r.filter, wrapModeU = r.wrapU, wrapModeV = r.wrapV, hideFlags = HideFlags.DontSave };
                rt.Create();
                targets[i] = rt;
            }
            return true;
        }

        public bool TargetsAlive()
        {
            if (targets.Length != frame.resources.Length) return false;
            for (int i = 0; i < targets.Length; i++)
                if (frame.resources[i].rt && frame.resources[i].kind == "2d" && Enum.TryParse(frame.resources[i].unityFormat, out GraphicsFormat _)
                    && (targets[i] == null || !targets[i].IsCreated())) return false;
            return true;
        }

        void ReleaseTargets()
        {
            foreach (var t in targets) if (t != null) { t.Release(); UnityEngine.Object.DestroyImmediate(t); }
            targets = Array.Empty<RenderTexture>();
        }

        public void Dispose()
        {
            ReleaseTargets();
            foreach (var b in buffers.Values) b.gb?.Release();
            buffers.Clear();
            foreach (var m in rayMeshes.Values) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            rayMeshes.Clear();
            if (writeDepth != null) UnityEngine.Object.DestroyImmediate(writeDepth);
            for (int b = 0; b < writeStencil.Length; b++) if (writeStencil[b] != null) UnityEngine.Object.DestroyImmediate(writeStencil[b]);
        }

        // ------------------------------------------------------------------ recording

        /// <summary>Record the frame (up to <paramref name="stopAfterOp"/>, -1 = all). <paramref name="view"/> null = exactly as captured.</summary>
        public void Record(CommandBuffer cmd, View view, int stopAfterOp = -1)
        {
            if (!TargetsAlive()) Ensure(ScreenWidth, ScreenHeight);
            InitContents(cmd, freshScreenTargets);
            cmd.SetInvertCulling(invertCulling);
            int last = stopAfterOp < 0 ? frame.ops.Length - 1 : Mathf.Min(stopAfterOp, frame.ops.Length - 1);
            for (int i = 0; i <= last; i++)
            {
                var op = frame.ops[i];
                switch (op.type)
                {
                    case FrameOpType.Pass: RecordPass(cmd, op); break;
                    case FrameOpType.Draw: RecordDraw(cmd, i, op, view); break;
                    case FrameOpType.Blit: RecordBlit(cmd, op); break;
                }
            }
            cmd.SetInvertCulling(false);
        }

        /// <param name="fresh">Screen-sized targets start cleared (far depth, stencil 0, colour 0) instead of with initial contents.</param>
        void InitContents(CommandBuffer cmd, bool fresh)
        {
            for (int i = 0; i < frame.resources.Length; i++)
            {
                var r = frame.resources[i];
                var rt = targets[i];
                if (rt == null) continue;
                if (fresh && r.screenLevel >= 0 && (r.initDepth != null || r.init != null) && !IsHistory(i))
                {
                    cmd.SetRenderTarget(rt);
                    float far = convertClearDepth && SystemInfo.usesReversedZBuffer ? 0f : 1f;
                    if (r.isDepth) cmd.ClearRenderTarget(RTClearFlags.Depth | RTClearFlags.Stencil, Color.clear, far, 0);
                    else cmd.ClearRenderTarget(false, true, Color.clear);
                    continue;
                }
                if (r.isDepth && r.initDepth != null)
                {
                    if (writeDepth == null) writeDepth = new Material(Shader.Find("Hidden/RenderDocToUnity/WriteDepth")) { hideFlags = HideFlags.HideAndDontSave };
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetTexture("_RDDepth", r.initDepth);
                    mpb.SetVector("_RDDstSize", new Vector4(rt.width, rt.height, 0, 0));
                    cmd.SetRenderTarget(rt);
                    cmd.DrawProcedural(Matrix4x4.identity, writeDepth, 0, MeshTopology.Triangles, 3, 1, mpb);
                    // stencil carried over too (e.g. GL frames that never clear: deferred lighting tests last frame's classification)
                    if (r.initDepth.graphicsFormat == GraphicsFormat.R32G32_SFloat && GraphicsFormatUtility.IsStencilFormat(rt.depthStencilFormat))
                        for (int b = 0; b < 8; b++)
                        {
                            if (writeStencil[b] == null)
                            {
                                writeStencil[b] = new Material(writeDepth) { hideFlags = HideFlags.HideAndDontSave };
                                writeStencil[b].SetFloat("_RDStencilBit", 1 << b);
                            }
                            cmd.DrawProcedural(Matrix4x4.identity, writeStencil[b], 1, MeshTopology.Triangles, 3, 1, mpb);
                        }
                }
                else if (!r.isDepth && r.init != null)
                {
                    if (r.init.width == rt.width && r.init.height == rt.height && r.init.graphicsFormat == rt.graphicsFormat) cmd.CopyTexture(r.init, 0, 0, rt, 0, 0);
                    else cmd.Blit(r.init, rt);   // widened at import, or screen-relative at another size
                }
            }
        }

        HashSet<int> historyResources;

        /// <summary>Resource read as temporal history (previous frame's image): legitimately carried over, never "fresh".</summary>
        bool IsHistory(int res)
        {
            if (historyResources == null)
            {
                historyResources = new HashSet<int>();
                foreach (var op in frame.ops)
                    if (op.textures != null)
                        foreach (var tb in op.textures) if (tb.historyOf >= 0) historyResources.Add(tb.resource);
            }
            return historyResources.Contains(res);
        }

        RenderTargetIdentifier Id(int res) => new RenderTargetIdentifier(targets[res]);

        void RecordPass(CommandBuffer cmd, FrameOp op)
        {
            var colors = new List<int>();
            foreach (var c in op.colors) if (c >= 0 && targets[c] != null) colors.Add(c);
            int depth = op.depthTarget >= 0 && targets[op.depthTarget] != null ? op.depthTarget : -1;
            if (op.colorLoad == "clear")
                for (int i = 0; i < colors.Count; i++)
                {
                    cmd.SetRenderTarget(Id(colors[i]));
                    cmd.ClearRenderTarget(false, true, i < op.clearColors.Length ? op.clearColors[i] : Color.clear);
                }
            if (depth >= 0 && (op.depthLoad == "clear" || op.stencilLoad == "clear"))
            {
                cmd.SetRenderTarget(Id(depth));
                var flags = (op.depthLoad == "clear" ? RTClearFlags.Depth : 0) | (op.stencilLoad == "clear" ? RTClearFlags.Stencil : 0);
                float d = convertClearDepth && SystemInfo.usesReversedZBuffer ? 1f - op.clearDepth : op.clearDepth;
                cmd.ClearRenderTarget(flags, Color.clear, d, (uint)op.clearStencil);
            }
            if (colors.Count == 0 && depth >= 0) cmd.SetRenderTarget(Id(depth));
            else if (colors.Count > 0)
            {
                var ids = new RenderTargetIdentifier[colors.Count];
                for (int i = 0; i < ids.Length; i++) ids[i] = Id(colors[i]);
                cmd.SetRenderTarget(ids, depth >= 0 ? Id(depth) : ids[0]);
            }
        }

        int TargetOf(FrameOp op)
        {
            foreach (var c in op.colors) if (c >= 0 && targets[c] != null) return c;
            return op.depthTarget >= 0 && targets[op.depthTarget] != null ? op.depthTarget : -1;
        }

        void RecordDraw(CommandBuffer cmd, int index, FrameOp op, View view)
        {
            if (op.material < 0 || op.mesh < 0) return;
            var mat = frame.materials[op.material];
            var mesh = frame.meshes[op.mesh];
            if (mat == null || mesh == null) return;
            if (view != null && op.viewRays.Length > 0) mesh = RayMesh(index, op, mesh, view);
            if (view != null && op.objects.Length > 0)
            {
                bool any = false;
                foreach (var o in op.objects) if (!view.Hidden(o)) { any = true; break; }
                if (!any) return;                       // every object it draws is disabled or deleted
            }
            // viewport / scissor scale with screen-relative targets
            int t = TargetOf(op);
            float sx = 1, sy = 1;
            if (t >= 0) { var r = frame.resources[t]; sx = (float)targets[t].width / r.width; sy = (float)targets[t].height / r.height; }
            var rtSize = t >= 0 ? new Vector2(targets[t].width, targets[t].height) : Vector2.one;
            if (op.viewport.Length >= 4)
                cmd.SetViewport(new Rect(op.viewport[0] * sx, op.viewport[1] * sy, op.viewport[2] * sx, op.viewport[3] * sy));
            bool scissor = op.scissor.Length >= 4 && (op.scissor[0] > 0 || op.scissor[1] > 0 || op.scissor[2] * sx < rtSize.x - 0.5f || op.scissor[3] * sy < rtSize.y - 0.5f);
            if (scissor) cmd.EnableScissorRect(new Rect(op.scissor[0] * sx, op.scissor[1] * sy, op.scissor[2] * sx, op.scissor[3] * sy));
            // constant buffers: bound globally right before the draw (commands execute in order)
            for (int c = 0; c < op.cbuffers.Length; c++)
            {
                var cb = op.cbuffers[c];
                if (cb.data == null || cb.data.Length == 0) continue;
                var gb = ConstantBuffer(index, c, cb, view, out int size);
                cmd.SetGlobalConstantBuffer(gb, cb.name, 0, size);
            }
            for (int inst = 0; inst < Mathf.Max(1, op.instances); inst++)
            {
                var mpb = new MaterialPropertyBlock();
                foreach (var tb in op.textures)
                {
                    if (tb.resource < 0) continue;
                    // temporal history holds the captured viewpoint; from any other view use the current frame (converged, no motion)
                    int ri = view != null && !view.atCapture && tb.historyOf >= 0 && targets[tb.historyOf] != null ? tb.historyOf : tb.resource;
                    var res = frame.resources[ri];
                    if (targets[ri] != null)
                    {
                        if (res.isDepth) mpb.SetTexture(tb.name, targets[ri], RenderTextureSubElement.Depth);
                        else mpb.SetTexture(tb.name, targets[ri]);
                    }
                    else if (res.init != null) mpb.SetTexture(tb.name, res.init);
                }
                mpb.SetInteger("rd_InstanceIndex", op.firstInstance + inst);
                mpb.SetInteger("rd_BaseVertex", op.baseVertex);
                cmd.DrawMesh(mesh, Matrix4x4.identity, mat, 0, 0, mpb);
            }
            if (scissor) cmd.DisableScissorRect();
        }

        GraphicsBuffer ConstantBuffer(int opIndex, int cbIndex, FrameCB cb, View view, out int size)
        {
            size = (cb.data.Length + 15) / 16 * 16;
            long key = ((long)opIndex << 16) | (uint)cbIndex;
            bool patched = cb.patches != null && cb.patches.Length > 0;
            bool fresh = !buffers.TryGetValue(key, out var e) || e.gb == null || !e.gb.IsValid();
            if (fresh)
            {
                e = (new GraphicsBuffer(GraphicsBuffer.Target.Constant, size / 16, 16), new float[size / 4], new Vector4[size / 16]);
                buffers[key] = e;
            }
            if (fresh || patched)
            {
                Buffer.BlockCopy(cb.data, 0, e.floats, 0, cb.data.Length);
                if (patched && view != null) ApplyPatches(e.floats, cb.patches, view);
                var f = e.floats;
                for (int k = 0; k < e.vecs.Length; k++) e.vecs[k] = new Vector4(f[k * 4], f[k * 4 + 1], f[k * 4 + 2], f[k * 4 + 3]);
                e.gb.SetData(e.vecs);
            }
            return e.gb;
        }

        /// <summary>Copy of a fullscreen mesh whose per-corner view rays point along the given view's frustum.</summary>
        Mesh RayMesh(int index, FrameOp op, Mesh source, View v)
        {
            if (!rayMeshes.TryGetValue(index, out var m) || m == null)
            {
                m = UnityEngine.Object.Instantiate(source);
                m.hideFlags = HideFlags.HideAndDontSave;
                rayMeshes[index] = m;
            }
            var p = v.proj;
            var list = new List<Vector4>();
            foreach (var ray in op.viewRays)
            {
                list.Clear();
                for (int k = 0; k < ray.ndc.Length; k++)
                {
                    var n = ray.ndc[k];
                    var u = new Vector3((n.x - p.m02) / p.m00 * ray.depth, (n.y - p.m12) / p.m11 * ray.depth, -ray.depth);
                    Vector3 world = v.invView.MultiplyVector(u);
                    list.Add(new Vector4(world.x, world.y, world.z, k < ray.w.Length ? ray.w[k] : 1f));
                }
                m.SetUVs(ray.location, list);
            }
            return m;
        }

        static void WriteMatrix(float[] f, int at, Matrix4x4 m, bool rowMajor)
        {
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    f[at + (rowMajor ? r * 4 + c : c * 4 + r)] = m[r, c];
        }

        void ApplyPatches(float[] f, FrameCBPatch[] patches, View v)
        {
            foreach (var p in patches)
            {
                int at = p.offset / 4;
                switch (p.kind)
                {
                    case CameraValue.CameraPos:
                        if (at + 3 <= f.Length) { f[at] = v.position.x; f[at + 1] = v.position.y; f[at + 2] = v.position.z; }
                        continue;
                    case CameraValue.Screen:
                        if (at < f.Length)
                        {
                            var r = new FrameResource { screenLevel = p.level, screenRoundUp = p.roundUp };
                            float w = Mathf.Max(1, LevelWidth(r, ScreenWidth)), h = Mathf.Max(1, LevelWidth(r, ScreenHeight));
                            f[at] = p.component == 0 ? w : p.component == 1 ? h : p.component == 2 ? 1f / w : 1f / h;
                        }
                        continue;
                }
                if (at + 16 > f.Length) continue;
                Matrix4x4 m;
                switch (p.kind)
                {
                    case CameraValue.ViewProj: m = v.viewProj; break;
                    case CameraValue.InvViewProj: m = v.invViewProj; break;
                    case CameraValue.View: m = v.view; break;
                    case CameraValue.InvView: m = v.invView; break;
                    case CameraValue.WorldToCamera: m = v.worldToCamera; break;
                    case CameraValue.CameraToWorld: m = v.cameraToWorld; break;
                    case CameraValue.ViewProjRot: m = v.viewProjRot; break;
                    case CameraValue.InvViewProjRot: m = v.invViewProjRot; break;
                    case CameraValue.Proj: m = v.proj; break;
                    case CameraValue.InvProj: m = v.invProj; break;
                    case CameraValue.ModelViewProj:
                        m = v.viewProj * (p.obj >= 0 ? (v.Object(p.obj) ?? frame.objects[p.obj].model) : p.model);
                        break;
                    case CameraValue.ObjectToWorld:
                        m = v.Object(p.obj) ?? frame.objects[p.obj].model;
                        break;
                    case CameraValue.WorldToObject:
                    {
                        var o = v.Object(p.obj) ?? frame.objects[p.obj].model;
                        m = o == Zero ? Zero : o.inverse;
                        break;
                    }
                    default: continue;
                }
                WriteMatrix(f, at, m, p.rowMajor);
            }
        }

        void RecordBlit(CommandBuffer cmd, FrameOp op)
        {
            if (op.src < 0 || op.dst < 0 || targets[op.dst] == null) return;
            Texture src = targets[op.src] != null ? (Texture)targets[op.src] : frame.resources[op.src].init;
            if (src == null) return;
            var dst = targets[op.dst];
            if (src.width == dst.width && src.height == dst.height && src.graphicsFormat == dst.graphicsFormat)
                cmd.CopyTexture(src, 0, 0, dst, 0, 0);
            else
                cmd.Blit(src, dst);
        }
    }
}
