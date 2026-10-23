// Builds a RenderDocFrame asset (exact frame replay) from the folder written by `run_in_renderdoc.py frame`.
// Menu: Tools > RenderDoc To Unity > Import Frame Replay...
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace RenderDocToUnity
{
    [Serializable] class JFrameClear { public float[] color; public float depth; public long stencil; }
    [Serializable] class JFrameLoads { public string color, depth, stencil; }
    [Serializable] class JFrameValidate { public string target, file; public int eid; }
    [Serializable] class JFrameSampler { public string filter, mip, u, v, w, compare; }
    [Serializable] class JFrameTex { public string name, res; public JFrameSampler sampler; public bool depth; public string[] swizzle; public string history_of; }
    [Serializable] class JFramePatch { public int offset; public string kind, layout; public float[] model; public int @object = -1; public int level, component; }
    [Serializable] class JFrameObject { public float[] model; public int[] ops; public bool movable; }
    [Serializable] class JFrameLevel { public int level, width, height; }
    [Serializable] class JFramePair { public float x, y; }
    [Serializable] class JFrameRayFlat { public int location; public float depth; public JFramePair[] ndc; public float[] w; }
    [Serializable] class JFrameCB { public int block; public string data; public JFramePatch[] patches; }
    [Serializable] class JFrameCamera { public float[] view, proj; }
    [Serializable]
    class JFrameOp
    {
        public string op, src, dst, depth, state, mesh;
        public int eid, family, instances, first_instance, base_vertex;
        public string[] attachments, colors;
        public JFrameClear[] clears;
        public JFrameLoads loads;
        public JFrameValidate validate;
        public float[] viewport, scissor;
        public JFrameTex[] textures;
        public JFrameCB[] vs_cb, ps_cb;
        public int[] objects;
        public int position_location = -1;
        public JFrameRayFlat[] view_rays;
    }
    [Serializable] class JFrameInit { public string encoding; public string[] files; public int slices; public bool srgb; }
    [Serializable] class JFrameRes { public string id, format, unity_format, kind; public int width, height, depth, array, mips; public bool rt, is_depth; public JFrameInit init; }
    [Serializable] class JFrameRaw { public int location, offset; }
    [Serializable] class JFrameMesh { public string id, file; public int vertex_count, index_offset, index_count, base_vertex; public JFrameRaw[] raw; }
    [Serializable] class JFrame { public JFrameCamera camera; public bool rows_bottom_up; public string format, capture, final; public JFrameRes[] resources; public JFrameMesh[] meshes; public JFrameOp[] ops; public JFrameObject[] objects; public JFrameLevel[] screen_levels; }

    public static class RenderDocFrameImporter
    {
        [MenuItem("Tools/RenderDoc To Unity/Import Frame Replay...")]
        static void Menu()
        {
            string dir = EditorUtility.OpenFolderPanel("Folder containing frame.json", "", "");
            if (string.IsNullOrEmpty(dir)) return;
            var go = Import(dir, "Assets/RenderDocImports");
            Selection.activeGameObject = go;
            RenderDocFrameWindow.Open();
        }

        public static GameObject Import(string sourceDir, string targetFolder)
        {
            var j = JsonUtility.FromJson<JFrame>(File.ReadAllText(Path.Combine(sourceDir, "frame.json")));
            if (j == null || j.format == null || !j.format.StartsWith("renderdoc-to-unity-frame/")) throw new InvalidDataException("Not a frame.json");
            string name = Path.GetFileNameWithoutExtension(j.capture ?? "frame").Replace('.', '_') + "_frame";
            string root = $"{targetFolder.TrimEnd('/')}/{name}";
            foreach (var sub in new[] { "", "/Shaders", "/Meshes", "/Textures", "/Materials" }) EnsureFolder(root + sub);

            var asset = ScriptableObject.CreateInstance<RenderDocFrame>();
            asset.capture = j.capture;
            asset.sourceDir = sourceDir;
            var resIndex = new Dictionary<string, int>();
            for (int i = 0; i < j.resources.Length; i++) resIndex[j.resources[i].id] = i;

            // ---- shaders -> materials
            var shaderList = JsonUtility.FromJson<ShadersFile>(File.ReadAllText(Path.Combine(sourceDir, "shaders", "shaders.json")));
            foreach (var s in shaderList.shaders) File.Copy(Path.Combine(sourceDir, s.file), $"{root}/Shaders/{Path.GetFileName(s.file)}", true);
            AssetDatabase.Refresh();
            var matIndex = new Dictionary<string, int>();
            var mats = new List<Material>();
            int badShaders = 0;
            foreach (var s in shaderList.shaders)
            {
                var sh = AssetDatabase.LoadAssetAtPath<Shader>($"{root}/Shaders/{Path.GetFileName(s.file)}");
                if (sh == null || ShaderUtil.ShaderHasError(sh)) { badShaders++; Debug.LogWarning($"[RenderDocToUnity] frame shader {s.shader_name} failed to compile; its draws are skipped"); continue; }
                var m = new Material(sh) { name = $"frame_fam{s.family}_{s.state}" };
                AssetDatabase.CreateAsset(m, $"{root}/Materials/{m.name}.mat");
                matIndex[$"{s.family}_{s.state}"] = mats.Count;
                mats.Add(m);
            }
            asset.materials = mats.ToArray();

            // ---- meshes: raw attributes in UV channels by location, zero positions, local indices
            var meshIndex = new Dictionary<string, int>();
            var meshes = new List<Mesh>();
            var positionLocation = new Dictionary<string, int>();
            foreach (var o in j.ops)
                if (o.op == "draw" && o.position_location >= 0 && !string.IsNullOrEmpty(o.mesh) && !positionLocation.ContainsKey(o.mesh))
                    positionLocation[o.mesh] = o.position_location;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var m in j.meshes)
                {
                    byte[] data = File.ReadAllBytes(Path.Combine(sourceDir, m.file));
                    int vc = Mathf.Max(1, m.vertex_count);
                    var mesh = new Mesh { name = m.id, indexFormat = vc > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                    mesh.SetVertices(new Vector3[vc]);
                    foreach (var r in m.raw ?? Array.Empty<JFrameRaw>())
                    {
                        var v = new List<Vector4>(vc);
                        for (int i = 0; i < vc; i++)
                        {
                            int o = r.offset + i * 16;
                            v.Add(new Vector4(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8), BitConverter.ToSingle(data, o + 12)));
                        }
                        mesh.SetUVs(r.location, v);
                        if (positionLocation.TryGetValue(m.id, out int pl) && pl == r.location)
                            mesh.SetVertices(v.Select(x => new Vector3(x.x, x.y, x.z)).ToList());
                    }
                    var idx = new int[m.index_count];
                    Buffer.BlockCopy(data, m.index_offset, idx, 0, m.index_count * 4);
                    mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
                    // object meshes get their real bounds (selection, framing); others are drawn by command buffer, never culled
                    if (positionLocation.ContainsKey(m.id)) mesh.RecalculateBounds();
                    else mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
                    AssetDatabase.CreateAsset(mesh, $"{root}/Meshes/{m.id}.asset");
                    meshIndex[m.id] = meshes.Count;
                    meshes.Add(mesh);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            asset.meshes = meshes.ToArray();

            // ---- resources: descriptions + initial contents
            var resources = new List<FrameResource>(new FrameResource[j.resources.Length]);
            for (int i = 0; i < j.resources.Length; i++)
            {
                var r = j.resources[i];
                var fr = new FrameResource { id = r.id, format = r.format, unityFormat = r.unity_format, kind = r.kind, width = r.width, height = r.height,
                                             depth = Mathf.Max(1, r.depth), array = Mathf.Max(1, r.array), mips = Mathf.Max(1, r.mips), rt = r.rt, isDepth = r.is_depth };
                if (r.init != null && r.init.files != null && r.init.files.Length > 0)
                {
                    try
                    {
                        if (r.init.encoding == "raw_depth") fr.initDepth = BuildDepth(sourceDir, r, $"{root}/Textures/{Safe(r.id)}_depth.asset");
                        else if (r.init.encoding == "raw") fr.init = BuildRaw(sourceDir, r, $"{root}/Textures/{Safe(r.id)}.asset");
                        else fr.init = BuildPng(sourceDir, r, root);
                    }
                    catch (Exception e) { Debug.LogWarning($"[RenderDocToUnity] initial contents of {r.id} ({r.format} {r.kind}) not imported: {e.Message}"); }
                }
                var lvl = (j.screen_levels ?? Array.Empty<JFrameLevel>()).FirstOrDefault(l => r.rt && l.width == r.width && l.height == r.height);
                if (lvl != null) { fr.screenLevel = lvl.level; fr.screenRoundUp = RoundsUp(j, lvl); }
                resources[i] = fr;
            }

            // Image views with a component swizzle (e.g. Unity's Alpha8 = R8 viewed as 000R) cannot be expressed on a
            // Unity texture: bake each swizzled view into its own RGBA texture resource.
            var swizzled = new Dictionary<string, int>();
            int Swizzled(JFrameTex t)
            {
                if (!resIndex.TryGetValue(t.res ?? "", out int x)) return -1;
                if (t.swizzle == null || t.swizzle.Length != 4 || SwizzleIsNoop(j.resources[x].unity_format, t.swizzle)) return x;
                string key = t.res + "|" + string.Join(",", t.swizzle);
                if (swizzled.TryGetValue(key, out int y)) return y;
                var r = j.resources[x];
                Texture tex = null;
                if (!r.rt && r.init != null && r.init.encoding == "raw" && r.kind == "2d")
                {
                    try { tex = BuildSwizzled(sourceDir, r, t.swizzle, $"{root}/Textures/{Safe(r.id)}_{string.Join("", t.swizzle.Select(c => c.Substring(0, 1)))}.asset"); }
                    catch (Exception e) { Debug.LogWarning($"[RenderDocToUnity] swizzled view of {r.id} not built: {e.Message}"); }
                }
                if (tex == null) { swizzled[key] = x; return x; }
                var src = resources[x];
                resources.Add(new FrameResource { id = src.id + " (" + string.Join(",", t.swizzle) + ")", format = src.format, unityFormat = tex.graphicsFormat.ToString(), kind = "2d",
                                                  width = src.width, height = src.height, mips = src.mips, init = tex });
                swizzled[key] = resources.Count - 1;
                return resources.Count - 1;
            }

            // ---- ops
            var ops = new List<FrameOp>();
            foreach (var o in j.ops)
            {
                var op = new FrameOp { eid = o.eid };
                if (o.op == "pass")
                {
                    op.type = FrameOpType.Pass;
                    var cols = new List<int>(); var clears = new List<Color>();
                    for (int a = 0; a < (o.attachments?.Length ?? 0); a++)
                    {
                        string id = o.attachments[a];
                        if (string.IsNullOrEmpty(id) || !resIndex.TryGetValue(id, out int ri)) continue;
                        var cv = a < (o.clears?.Length ?? 0) ? o.clears[a] : null;
                        if (j.resources[ri].is_depth)
                        {
                            op.depthTarget = ri;
                            if (cv != null) { op.clearDepth = cv.depth; op.clearStencil = (int)cv.stencil; }
                        }
                        else
                        {
                            cols.Add(ri);
                            clears.Add(cv?.color != null && cv.color.Length >= 4 ? new Color(cv.color[0], cv.color[1], cv.color[2], cv.color[3]) : Color.clear);
                        }
                    }
                    op.colors = cols.ToArray(); op.clearColors = clears.ToArray();
                    if (o.loads != null) { op.colorLoad = o.loads.color; op.depthLoad = o.loads.depth; op.stencilLoad = o.loads.stencil; }
                    if (o.validate != null && !string.IsNullOrEmpty(o.validate.file) && resIndex.TryGetValue(o.validate.target, out int vt))
                    { op.validateFile = o.validate.file; op.validateTarget = vt; }
                }
                else if (o.op == "draw")
                {
                    op.type = FrameOpType.Draw;
                    op.material = matIndex.TryGetValue($"{o.family}_{o.state}", out int mi) ? mi : -1;
                    op.mesh = meshIndex.TryGetValue(o.mesh ?? "", out int mj) ? mj : -1;
                    op.instances = Mathf.Max(1, o.instances); op.firstInstance = o.first_instance; op.baseVertex = o.base_vertex;
                    op.viewport = o.viewport ?? Array.Empty<float>(); op.scissor = o.scissor ?? Array.Empty<float>();
                    op.colors = (o.colors ?? Array.Empty<string>()).Select(c => !string.IsNullOrEmpty(c) && resIndex.TryGetValue(c, out int x) ? x : -1).ToArray();
                    op.depthTarget = !string.IsNullOrEmpty(o.depth) && resIndex.TryGetValue(o.depth, out int dx) ? dx : -1;
                    op.cbuffers = (o.vs_cb ?? Array.Empty<JFrameCB>()).Select(c => Cb("rdv_", c))
                        .Concat((o.ps_cb ?? Array.Empty<JFrameCB>()).Select(c => Cb("rdp_", c))).ToArray();
                    op.textures = (o.textures ?? Array.Empty<JFrameTex>()).Select(t => new FrameTexBind
                    {
                        name = t.name, resource = Swizzled(t), depth = t.depth,
                        historyOf = !string.IsNullOrEmpty(t.history_of) && resIndex.TryGetValue(t.history_of, out int hx) ? hx : -1,
                    }).ToArray();
                    for (int ti = 0; ti < op.textures.Length; ti++)
                        if (o.textures[ti].sampler != null && op.textures[ti].resource >= 0) ApplySampler(resources[op.textures[ti].resource], o.textures[ti].sampler);
                    op.objects = o.objects ?? Array.Empty<int>();
                    op.viewRays = (o.view_rays ?? Array.Empty<JFrameRayFlat>()).Select(r => new FrameViewRay
                    {
                        location = r.location,
                        depth = r.depth,
                        ndc = (r.ndc ?? Array.Empty<JFramePair>()).Select(n => new Vector2(n.x, n.y)).ToArray(),
                        w = r.w ?? Array.Empty<float>(),
                    }).ToArray();
                    foreach (var cb in op.cbuffers)
                        foreach (var pt in cb.patches)
                            if (pt.kind == CameraValue.Screen)
                            {
                                var lv = (j.screen_levels ?? Array.Empty<JFrameLevel>()).FirstOrDefault(l => l.level == pt.level);
                                pt.roundUp = lv != null && RoundsUp(j, lv);
                            }
                }
                else if (o.op == "blit")
                {
                    op.type = FrameOpType.Blit;
                    op.src = resIndex.TryGetValue(o.src ?? "", out int s) ? s : -1;
                    op.dst = resIndex.TryGetValue(o.dst ?? "", out int d) ? d : -1;
                }
                else continue;
                ops.Add(op);
            }
            asset.resources = resources.ToArray();
            asset.ops = ops.ToArray();
            asset.finalResource = resIndex.TryGetValue(j.final ?? "", out int fi) ? fi : -1;
            asset.rowsBottomUp = j.rows_bottom_up;
            if (asset.finalResource >= 0) { asset.captureWidth = j.resources[asset.finalResource].width; asset.captureHeight = j.resources[asset.finalResource].height; }
            asset.objects = (j.objects ?? Array.Empty<JFrameObject>()).Select(o => new FrameObject
            {
                model = o.model != null && o.model.Length == 16 ? RowMajor(o.model) : Matrix4x4.identity,
                ops = o.ops ?? Array.Empty<int>(),
                movable = o.movable,
            }).ToArray();
            if (j.camera?.view != null && j.camera.view.Length == 16 && j.camera.proj != null && j.camera.proj.Length == 16)
            {
                asset.hasCamera = true;
                asset.captureView = RowMajor(j.camera.view);
                asset.captureProj = RowMajor(j.camera.proj);
            }
            foreach (var r in resources) if (r.init != null) { r.init.filterMode = r.filter; r.init.wrapModeU = r.wrapU; r.init.wrapModeV = r.wrapV; EditorUtility.SetDirty(r.init); }
            string assetPath = $"{root}/{name}.asset";
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            asset = AssetDatabase.LoadAssetAtPath<RenderDocFrame>(assetPath);   // saving can reload the asset; use the live instance

            var go = asset.hasCamera && asset.objects.Length > 0 ? BuildScene(asset, root, name) : new GameObject("RenderDocFrameReplay_" + name);
            Undo.RegisterCreatedObjectUndo(go, "Import RenderDoc frame");
            // offline replay of the exact capture (Frame Replay window: step through passes, validate)
            var playerGo = new GameObject("Capture Replay (validation)");
            playerGo.transform.SetParent(go.transform, false);
            var player = playerGo.AddComponent<RenderDocFramePlayer>();
            player.frame = asset;
            player.Replay();
            Debug.Log($"[RenderDocToUnity] frame {name}: {ops.Count} ops ({ops.Count(x => x.type == FrameOpType.Draw)} draws, {ops.Count(x => x.type == FrameOpType.Pass)} passes), " +
                      $"{resources.Count} resources, {meshes.Count} meshes, {mats.Count} materials, shaders failed {badShaders}. Asset: {root}/{name}.asset");
            return go;
        }

        static bool RoundsUp(JFrame j, JFrameLevel l)
        {
            var f = j.resources.FirstOrDefault(r => r.id == j.final);
            return f != null && l.level > 0 && (l.width != f.width >> l.level || l.height != f.height >> l.level);
        }

        /// <summary>
        /// Scene hierarchy: a root with <see cref="RenderDocScene"/> (activates the replay pipeline) and one GameObject per
        /// captured object, placed by its object matrix, with a MeshRenderer per draw it appears in on screen (for
        /// selection, framing and bounds; the pipeline renders them with the captured passes).
        /// </summary>
        public static GameObject BuildScene(RenderDocFrame asset, string root, string name)
        {
            string pipePath = $"{root}/RenderDocReplayPipeline.asset";
            var pipe = AssetDatabase.LoadAssetAtPath<RenderDocPipelineAsset>(pipePath);
            if (pipe == null) { pipe = ScriptableObject.CreateInstance<RenderDocPipelineAsset>(); AssetDatabase.CreateAsset(pipe, pipePath); }

            var go = new GameObject("RenderDocScene_" + name);
            go.SetActive(false);
            var scene = go.AddComponent<RenderDocScene>();
            scene.frame = asset;
            scene.pipeline = pipe;
            var objectsRoot = new GameObject("Objects").transform;
            objectsRoot.SetParent(go.transform, false);
            for (int i = 0; i < asset.objects.Length; i++)
            {
                var fo = asset.objects[i];
                var draws = fo.ops.Where(k => k >= 0 && k < asset.ops.Length && asset.ops[k].type == FrameOpType.Draw).ToArray();
                // on-screen draws: those writing a screen-sized colour target (not shadow casters, not reductions)
                var visible = draws.Where(k => asset.ops[k].colors.Any(c => c >= 0 && asset.resources[c].screenLevel == 0)).ToArray();
                string label = visible.Length > 0 && asset.ops[visible[0]].mesh >= 0 ? asset.meshes[asset.ops[visible[0]].mesh].name : "shadow";
                var obj = new GameObject($"Object{i:000}_{label}");
                obj.transform.SetParent(objectsRoot, false);
                SetFromMatrix(obj.transform, fo.model);
                var ro = obj.AddComponent<RenderDocObject>();
                ro.index = i;
                ro.initialLocalToWorld = obj.transform.localToWorldMatrix;
                ro.movable = fo.movable;
                // mesh vertices are in the object's space of the captured matrix: compensate the TRS approximation
                var local = obj.transform.localToWorldMatrix.inverse * fo.model;
                foreach (var k in visible)
                {
                    var op = asset.ops[k];
                    if (op.mesh < 0 || op.material < 0) continue;
                    var part = new GameObject($"Draw{op.eid}");
                    part.transform.SetParent(obj.transform, false);
                    SetLocalFromMatrix(part.transform, local);
                    part.AddComponent<MeshFilter>().sharedMesh = asset.meshes[op.mesh];
                    var mr = part.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = asset.materials[op.material];
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            // main camera at the capture's viewpoint
            var cam = Camera.main;
            if (cam != null)
            {
                scene.CapturePose(out var pos, out var rot, out var fov);
                Undo.RecordObject(cam.transform, "Place camera");
                cam.transform.SetPositionAndRotation(pos, rot);
                Undo.RecordObject(cam, "Camera FOV");
                cam.fieldOfView = fov;
            }
            go.SetActive(true);
            return go;
        }

        static void SetFromMatrix(Transform t, Matrix4x4 m)
        {
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
            var scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0) scale.x = -scale.x;    // mirrored object
            t.position = m.GetColumn(3);
            if (scale.y > 1e-8f && scale.z > 1e-8f) t.rotation = Quaternion.LookRotation(z / scale.z, y / scale.y);
            t.localScale = scale;
        }

        static void SetLocalFromMatrix(Transform t, Matrix4x4 m)
        {
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
            var scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0) scale.x = -scale.x;
            t.localPosition = m.GetColumn(3);
            if (scale.y > 1e-8f && scale.z > 1e-8f) t.localRotation = Quaternion.LookRotation(z / scale.z, y / scale.y);
            t.localScale = scale;
        }

        static void ApplySampler(FrameResource r, JFrameSampler s)
        {
            r.filter = s.filter != null && s.filter.Contains("Point") ? FilterMode.Point
                     : s.mip != null && s.mip.Contains("Linear") ? FilterMode.Trilinear : FilterMode.Bilinear;
            r.wrapU = Wrap(s.u); r.wrapV = Wrap(s.v);
            if (!string.IsNullOrEmpty(s.compare) && s.compare != "AlwaysTrue" && s.compare != "Never") r.compareSampler = true;
        }

        static TextureWrapMode Wrap(string a) =>
            a == null ? TextureWrapMode.Repeat : a.Contains("Mirror") ? TextureWrapMode.Mirror : (a.Contains("Clamp") || a.Contains("Border")) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;

        static Texture BuildRaw(string dir, JFrameRes r, string path)
        {
            if (!Enum.TryParse(r.unity_format, out GraphicsFormat gf)) throw new NotSupportedException("format " + r.format);
            int mips = Mathf.Max(1, r.mips);
            var flags = mips > 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None;
            // Formats that cannot be sampled from a Texture on this platform (e.g. B10G11R11 on D3D11) are widened
            // losslessly to RGBA half; the player blits such initial contents instead of copying them.
            bool widen = !SystemInfo.IsFormatSupported(gf, GraphicsFormatUsage.Sample) && gf == GraphicsFormat.B10G11R11_UFloatPack32;
            if (widen) gf = GraphicsFormat.R16G16B16A16_SFloat;
            byte[] Read(int slice, int mip)
            {
                var raw = File.ReadAllBytes(Path.Combine(dir, r.init.files[slice * mips + mip]));
                return widen ? WidenR11G11B10(raw) : raw;
            }
            Texture tex;
            if (r.kind == "cube")
            {
                var c = new Cubemap(r.width, gf, flags, mips);
                for (int f = 0; f < 6; f++) for (int m = 0; m < mips; m++) c.SetPixelData(Read(f, m), m, (CubemapFace)f);
                c.Apply(false, false);
                tex = c;
            }
            else if (r.kind == "2darray")
            {
                var a = new Texture2DArray(r.width, r.height, Mathf.Max(1, r.array), gf, flags, mips);
                for (int s = 0; s < a.depth; s++) for (int m = 0; m < mips; m++) a.SetPixelData(Read(s, m), m, s);
                a.Apply(false, false);
                tex = a;
            }
            else if (r.kind == "3d")
            {
                var t3 = new Texture3D(r.width, r.height, Mathf.Max(1, r.depth), gf, flags, mips);
                for (int m = 0; m < mips; m++) t3.SetPixelData(Read(0, m), m);
                t3.Apply(false, false);
                tex = t3;
            }
            else
            {
                var t = new Texture2D(r.width, r.height, gf, mips, flags);
                for (int m = 0; m < mips; m++) t.SetPixelData(Read(0, m), m);
                t.Apply(false, false);
                tex = t;
            }
            tex.name = r.id;
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        /// <summary>True when sampling the format without a swizzle already yields the same result (missing components read as 0,0,1).</summary>
        static bool SwizzleIsNoop(string uf, string[] swz)
        {
            int ch = uf.StartsWith("R8_") || uf.StartsWith("R16_") || uf.StartsWith("R32_") || uf.StartsWith("D") ? 1
                   : uf.StartsWith("R8G8_") || uf.StartsWith("R16G16_") || uf.StartsWith("R32G32_") ? 2
                   : uf.Contains("B10G11R11") || uf.StartsWith("R8G8B8_") || uf.StartsWith("B8G8R8_") ? 3 : 4;
            string[] names = { "Red", "Green", "Blue", "Alpha" };
            for (int c = 0; c < 4; c++)
            {
                string expect = c < ch ? names[c] : c == 3 ? "One" : "Zero";
                if (swz[c] != expect && !(c < ch && swz[c] == names[c]) && !(c >= ch && swz[c] == names[c])) return false;
            }
            return true;
        }

        static Texture BuildSwizzled(string dir, JFrameRes r, string[] swz, string path)
        {
            // 8-bit UNORM/SRGB formats only; output RGBA8 with the view's swizzle applied
            int ch; int[] order;
            switch (r.unity_format)
            {
                case "R8_UNorm": ch = 1; order = new[] { 0 }; break;
                case "R8G8_UNorm": ch = 2; order = new[] { 0, 1 }; break;
                case "R8G8B8A8_UNorm": case "R8G8B8A8_SRGB": ch = 4; order = new[] { 0, 1, 2, 3 }; break;
                case "B8G8R8A8_UNorm": case "B8G8R8A8_SRGB": ch = 4; order = new[] { 2, 1, 0, 3 }; break;
                default: throw new NotSupportedException(r.unity_format);
            }
            bool srgb = r.unity_format.EndsWith("SRGB");
            int mips = Mathf.Max(1, r.mips);
            var t = new Texture2D(r.width, r.height, srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm, mips, mips > 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None);
            for (int m = 0; m < mips; m++)
            {
                var raw = File.ReadAllBytes(Path.Combine(dir, r.init.files[m]));
                int n = raw.Length / ch;
                var o = new byte[n * 4];
                for (int i = 0; i < n; i++)
                    for (int c = 0; c < 4; c++)
                    {
                        string sw = swz[c];
                        int comp = sw == "Red" ? 0 : sw == "Green" ? 1 : sw == "Blue" ? 2 : sw == "Alpha" ? 3 : -1;
                        byte v;
                        if (sw == "One") v = 255;
                        else if (comp < 0) v = 0;
                        else if (comp < ch) v = raw[i * ch + order[comp]];
                        else v = (byte)(comp == 3 ? 255 : 0);   // missing components read as (0,0,0,1)
                        o[i * 4 + c] = v;
                    }
                t.SetPixelData(o, m);
            }
            t.Apply(false, false);
            t.name = r.id + " swizzled";
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        static byte[] WidenR11G11B10(byte[] raw)
        {
            // 11/10-bit unsigned floats share half's 5-bit exponent and bias, so each converts by a mantissa shift
            var o = new byte[raw.Length * 2];
            for (int i = 0; i < raw.Length / 4; i++)
            {
                uint v = BitConverter.ToUInt32(raw, i * 4);
                ushort r = (ushort)((v & 0x7FF) << 4), g = (ushort)(((v >> 11) & 0x7FF) << 4), b = (ushort)(((v >> 22) & 0x3FF) << 5);
                Buffer.BlockCopy(new[] { r, g, b, (ushort)0x3C00 }, 0, o, i * 8, 8);
            }
            return o;
        }

        static Texture BuildPng(string dir, JFrameRes r, string root)
        {
            // block-compressed: decoded PNG(s), imported unflipped (rows stay in memory order like the replayed targets)
            var t = new Texture2D(2, 2, r.init.srgb ? TextureFormat.RGBA32 : TextureFormat.RGBA32, false, !r.init.srgb);
            if (!t.LoadImage(File.ReadAllBytes(Path.Combine(dir, r.init.files[0])))) throw new InvalidDataException("png");
            var px = t.GetPixels32(); int w = t.width, h = t.height;
            var flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++) Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);   // PNG top row -> texture row 0
            t.SetPixels32(flipped); t.Apply(true, false);
            t.name = r.id;
            AssetDatabase.CreateAsset(t, $"{root}/Textures/{Safe(r.id)}.asset");
            return t;
        }

        static Texture2D BuildDepth(string dir, JFrameRes r, string path)
        {
            byte[] raw = File.ReadAllBytes(Path.Combine(dir, r.init.files[0]));
            int n = r.width * r.height;
            int bpp = raw.Length / Mathf.Max(1, n);
            bool stencil = bpp == 4 && r.format.StartsWith("D24S8");   // D24S8: depth in the low 24 bits, stencil in the high byte
            int ch = stencil ? 2 : 1;
            var v = new float[n * ch];
            for (int i = 0; i < n; i++)
            {
                if (r.format == "D16") v[i] = BitConverter.ToUInt16(raw, i * 2) / 65535f;
                else if (r.format == "D32" || (bpp == 4 && r.format.StartsWith("D32"))) v[i] = BitConverter.ToSingle(raw, i * bpp);
                else if (bpp >= 4)
                {
                    uint x = BitConverter.ToUInt32(raw, i * bpp);
                    v[i * ch] = (x & 0xFFFFFF) / 16777215f;
                    if (stencil) v[i * ch + 1] = x >> 24;
                }
            }
            var t = new Texture2D(r.width, r.height, stencil ? GraphicsFormat.R32G32_SFloat : GraphicsFormat.R32_SFloat, TextureCreationFlags.None) { name = r.id + "_depth", filterMode = FilterMode.Point };
            t.SetPixelData(v, 0);
            t.Apply(false, false);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        static string Safe(string id) => id.Replace("::", "_");

        static Matrix4x4 RowMajor(float[] f)
        {
            var m = new Matrix4x4();
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) m[r, c] = f[r * 4 + c];
            return m;
        }

        static FrameCB Cb(string prefix, JFrameCB c) => new FrameCB
        {
            name = prefix + c.block,
            data = Convert.FromBase64String(c.data),
            patches = (c.patches ?? Array.Empty<JFramePatch>())
                .Where(p => Enum.TryParse<CameraValue>(p.kind.Replace("_", ""), true, out _))
                .Select(p => new FrameCBPatch
                {
                    offset = p.offset,
                    kind = (CameraValue)Enum.Parse(typeof(CameraValue), p.kind.Replace("_", ""), true),
                    rowMajor = p.layout == "row",
                    model = p.model != null && p.model.Length == 16 ? RowMajor(p.model) : Matrix4x4.identity,
                    obj = p.@object,
                    level = p.level,
                    component = p.component,
                }).ToArray(),
        };

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
