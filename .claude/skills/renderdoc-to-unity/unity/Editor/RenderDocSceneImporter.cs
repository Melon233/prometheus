// RenderDoc -> Unity scene importer (Editor only).
// Reads the folder produced by `run_in_renderdoc.py extract` (scene.json, meshes/*.bin, textures/*.png,
// shaders/*.shader + shaders.json, reference.png) and rebuilds it as Unity assets and a GameObject hierarchy.
//
// Menu : Tools > RenderDoc To Unity > Import Extracted Scene...
// Code : RenderDocToUnity.RenderDocSceneImporter.Import(sourceDir, new ImportOptions { ... })
//
// Material modes
// - Replay (default when shaders/shaders.json exists): every draw uses a RenderDocReplay/* shader generated from its
//   captured SPIR-V, with the captured constant-buffer bytes and textures bound per renderer (RenderDocReplayDraw).
//   Needs the Editor on D3D11/D3D12. Families whose shader failed to compile fall back to Simple.
// - Simple: URP Lit / Standard (or a shader you name) with the guessed albedo texture.
//
// Coordinate notes
// - World matrices are VP^-1 * MVP, i.e. the capturing engine's world space. Unity captures need no conversion;
//   for right-handed engines enable Mirror X (negates X and fixes winding).
// - RenderDoc writes textures top-row-first; Unity's v=0 is the bottom row. Textures are flipped vertically on import
//   so both captured UVs (Replay) and decoded UVs (Simple) sample the same texels as on the GPU.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RenderDocToUnity
{
    [Serializable] public class StreamLayout { public int offset = -1; public int components; }
    [Serializable] public class RawStream { public int location; public int offset; }
    [Serializable] public class MeshLayout { public StreamLayout position, normal, uv0, color, indices; public RawStream[] raw; }
    [Serializable] public class MeshEntry { public string id, file, space; public int vertex_count, first_event; public MeshLayout layout; public string[] attributes; }
    [Serializable] public class TextureEntry { public string id, file, format, resource, kind; public string[] faces; public int width, height; public bool srgb; }
    [Serializable] public class CameraEntry { public int index, draws; public float[] vp, position, forward, up; public float fov_y_deg, aspect; public string vp_source; public string[] targets; }
    [Serializable] public class CBEntry { public int block; public string data; }
    [Serializable] public class TexBind { public string name, resource, kind, texture; }
    [Serializable] public class ReplayEntry { public int family = -1; public string state; public CBEntry[] vs_cb, ps_cb; public TexBind[] textures; }
    [Serializable] public class ObjectEntry { public int @event, instance, instance_index, camera; public string name, mesh, mode, bake_reason, shader, albedo, marker; public float fit_error; public float[] world; public string[] textures, targets; public bool blend, depth_write; public string cull; public ReplayEntry replay; }
    [Serializable] public class ReferenceEntry { public string file, target, kind; public int @event, width, height; }
    [Serializable] public class SceneFile { public string format, capture, api, renderdoc; public CameraEntry[] cameras; public ReferenceEntry reference; public MeshEntry[] meshes; public TextureEntry[] textures; public ObjectEntry[] objects; }
    [Serializable] public class ShaderFileEntry { public int family; public string state, file, shader_name; }
    [Serializable] public class ShadersFile { public ShaderFileEntry[] shaders; }

    public enum MaterialMode { Replay, Simple }

    [Serializable]
    public class ImportOptions
    {
        public string targetFolder = "Assets/RenderDocImports";   // assets go to <targetFolder>/<captureName>
        public MaterialMode materialMode = MaterialMode.Replay;   // falls back to Simple when no replay shaders exist
        public bool flipTextures = true;                          // RenderDoc PNGs are top-row-first
        public bool mirrorX = false;
        public bool importTextures = true;
        public bool createCameras = true;
        public bool createReferenceOverlay = true;
        public bool groupByShader = true;
        public bool showTransparent = false;                     // blended draws (clouds, fog cards, VFX) start hidden
        public string shaderName = "";                            // Simple mode: empty = URP Lit if available, else Standard
    }

    public static class RenderDocSceneImporter
    {
        [MenuItem("Tools/RenderDoc To Unity/Import Extracted Scene...")]
        static void ImportMenu()
        {
            string dir = EditorUtility.OpenFolderPanel("Folder containing scene.json", "", "");
            if (string.IsNullOrEmpty(dir)) return;
            ImportWindow.Open(dir);
        }

        public static GameObject Import(string sourceDir, ImportOptions opt)
        {
            var scene = JsonUtility.FromJson<SceneFile>(File.ReadAllText(Path.Combine(sourceDir, "scene.json")));
            if (scene == null || scene.format == null || !scene.format.StartsWith("renderdoc-to-unity/"))
                throw new InvalidDataException("Not a renderdoc-to-unity scene.json");

            string name = SanitizeName(Path.GetFileNameWithoutExtension(scene.capture ?? "capture"));
            string root = $"{opt.targetFolder.TrimEnd('/')}/{name}";
            foreach (var sub in new[] { "", "/Meshes", "/Materials", "/Textures", "/Shaders" }) EnsureFolder(root + sub);

            string shadersJson = Path.Combine(sourceDir, "shaders", "shaders.json");
            bool replay = opt.materialMode == MaterialMode.Replay && File.Exists(shadersJson);

            var textures = ImportTextures(sourceDir, root, scene, opt);
            var replayShaders = replay ? ImportShaders(sourceDir, root, shadersJson) : new Dictionary<string, Shader>();
            var meshes = ImportMeshes(sourceDir, root, scene, opt, replay);

            // ---- materials
            Shader simpleShader = ResolveShader(opt.shaderName);
            Shader transparentShader = Shader.Find("Sprites/Default");
            var materials = new Dictionary<string, Material>();
            Material SimpleMaterial(ObjectEntry o, bool useTexture)
            {
                string tex = useTexture && !string.IsNullOrEmpty(o.albedo) && textures.ContainsKey(o.albedo) ? o.albedo : "none";
                bool transparent = o.blend && transparentShader != null;
                bool twoSided = o.cull == "NoCull";
                string key = "simple_" + tex + (transparent ? "_alpha" : "") + (twoSided ? "_2s" : "");
                if (materials.TryGetValue(key, out var mat)) return mat;
                mat = new Material(transparent ? transparentShader : simpleShader) { name = "mat_" + key };
                if (tex != "none")
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", textures[tex]);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", textures[tex]);
                }
                if (transparent) mat.color = new Color(1, 1, 1, 0.6f);
                if (twoSided && mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
                AssetDatabase.CreateAsset(mat, $"{root}/Materials/{mat.name}.mat");
                return materials[key] = mat;
            }
            Material ReplayMaterial(ObjectEntry o)
            {
                if (o.replay == null || o.replay.family < 0) return null;
                string key = $"fam{o.replay.family}_{o.replay.state}";
                if (materials.TryGetValue("replay_" + key, out var mat)) return mat;
                if (!replayShaders.TryGetValue(key, out var sh)) return null;
                mat = new Material(sh) { name = "mat_replay_" + key, enableInstancing = false };
                AssetDatabase.CreateAsset(mat, $"{root}/Materials/{mat.name}.mat");
                return materials["replay_" + key] = mat;
            }

            // ---- hierarchy
            var rootGo = new GameObject("RenderDocCapture_" + name);
            Undo.RegisterCreatedObjectUndo(rootGo, "Import RenderDoc capture");
            var groups = new Dictionary<string, Transform>();
            Transform GroupFor(ObjectEntry o)
            {
                string key = $"Camera{o.camera}" + (o.blend ? "/Transparent" : "") + (opt.groupByShader ? $"/{o.shader}" : "");
                if (groups.TryGetValue(key, out var tr)) return tr;
                Transform parent = rootGo.transform;
                foreach (var part in key.Split('/'))
                {
                    var child = parent.Find(part);
                    if (child == null)
                    {
                        child = new GameObject(part).transform;
                        child.SetParent(parent, false);
                        if (part == "Transparent") child.gameObject.SetActive(opt.showTransparent);
                    }
                    parent = child;
                }
                return groups[key] = parent;
            }

            int sheared = 0, replayed = 0, fallback = 0;
            foreach (var o in scene.objects)
            {
                if (!meshes.TryGetValue(o.mesh, out var mesh)) continue;
                var go = new GameObject($"E{o.@event}_{o.mesh}" + (o.instance > 0 ? $"_i{o.instance}" : "") + (o.mode == "baked" ? "_baked" : ""));
                go.transform.SetParent(GroupFor(o), false);
                Matrix4x4 w = ToMatrix(o.world, opt.mirrorX);
                if (!ApplyMatrix(go.transform, w))
                {
                    // Shear cannot be expressed by a Transform: bake this instance into its own mesh copy.
                    mesh = BakeMatrix(mesh, w, $"{root}/Meshes/{o.mesh}_e{o.@event}_sheared.asset");
                    sheared++;
                }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                Material mat = replay ? ReplayMaterial(o) : null;
                if (mat != null)
                {
                    renderer.sharedMaterial = mat;
                    var rp = go.AddComponent<RenderDocReplayDraw>();
                    rp.constantBuffers = (o.replay.vs_cb ?? Array.Empty<CBEntry>()).Select(c => new RenderDocReplayDraw.ConstantBuffer { name = "rdv_" + c.block, data = Convert.FromBase64String(c.data) })
                        .Concat((o.replay.ps_cb ?? Array.Empty<CBEntry>()).Select(c => new RenderDocReplayDraw.ConstantBuffer { name = "rdp_" + c.block, data = Convert.FromBase64String(c.data) })).ToArray();
                    rp.textures = (o.replay.textures ?? Array.Empty<TexBind>())
                        .Where(t => !string.IsNullOrEmpty(t.texture) && textures.ContainsKey(t.texture))
                        .Select(t => new RenderDocReplayDraw.TextureBinding { name = t.name, texture = textures[t.texture] }).ToArray();
                    rp.instanceIndex = o.instance_index;
                    rp.Apply();
                    replayed++;
                }
                else
                {
                    // In Replay mode UV channel 0 holds raw attribute 0, so a fallback material cannot use the albedo UVs.
                    renderer.sharedMaterial = SimpleMaterial(o, !replay);
                    if (replay) fallback++;
                }
                var info = go.AddComponent<RenderDocDrawInfo>();
                info.eventId = o.@event; info.mode = o.mode; info.bakeReason = o.bake_reason; info.shader = o.shader;
                info.fitError = o.fit_error; info.textures = o.textures; info.marker = o.marker; info.instance = o.instance;
            }

            // ---- cameras + reference overlay
            Camera main = null;
            if (opt.createCameras && scene.cameras != null)
            {
                foreach (var c in scene.cameras)
                {
                    var camGo = new GameObject($"CaptureCamera{c.index}");
                    camGo.transform.SetParent(rootGo.transform, false);
                    camGo.transform.SetPositionAndRotation(V3(c.position, opt.mirrorX), Quaternion.LookRotation(V3(c.forward, opt.mirrorX), V3(c.up, opt.mirrorX)));
                    var cam = camGo.AddComponent<Camera>();
                    cam.fieldOfView = c.fov_y_deg;
                    cam.nearClipPlane = 0.1f;
                    cam.farClipPlane = 10000f;
                    cam.enabled = c.index == 0;
                    if (c.index == 0) main = cam;
                }
            }
            if (opt.createReferenceOverlay && main != null && scene.reference != null)
            {
                var refTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{root}/Textures/reference.png");
                if (refTex != null) CreateOverlay(main, refTex, $"{root}/Materials/mat_reference_overlay.mat");
            }

            AssetDatabase.SaveAssets();
            Selection.activeGameObject = rootGo;
            var modes = scene.objects.GroupBy(o => o.mode).Select(g => $"{g.Key}={g.Count()}")
                .Concat(new[] { $"transparent={scene.objects.Count(o => o.blend)}" + (opt.showTransparent ? "" : " (hidden)") });
            Debug.Log($"[RenderDocToUnity] {name}: {scene.objects.Length} objects ({string.Join(", ", modes)}), {meshes.Count} meshes, {textures.Count} textures, " +
                      $"{materials.Count} materials, {scene.cameras?.Length ?? 0} cameras, sheared->baked {sheared}" +
                      (replay ? $", replay shaders {replayShaders.Count}, replayed draws {replayed}, fallback {fallback}" : ", simple materials") + $". Assets: {root}");
            return rootGo;
        }

        // ------------------------------------------------------------------ import steps

        static Dictionary<string, Texture> ImportTextures(string sourceDir, string root, SceneFile scene, ImportOptions opt)
        {
            var result = new Dictionary<string, Texture>();
            if (!opt.importTextures || scene.textures == null) return result;
            foreach (var t in scene.textures)
            {
                if (t.kind == "cube" && t.faces != null)
                    for (int f = 0; f < t.faces.Length; f++) CopyPng(Path.Combine(sourceDir, t.faces[f]), $"{root}/Textures/{t.id}_f{f}.png", opt.flipTextures);
                else
                    CopyPng(Path.Combine(sourceDir, t.file), $"{root}/Textures/{t.id}.png", opt.flipTextures);
            }
            if (scene.reference != null && !string.IsNullOrEmpty(scene.reference.file))
                File.Copy(Path.Combine(sourceDir, scene.reference.file), $"{root}/Textures/reference.png", true);   // shown as an image: not flipped
            AssetDatabase.Refresh();
            foreach (var t in scene.textures)
            {
                if (t.kind == "cube" && t.faces != null && t.faces.Length == 6)
                {
                    var cube = BuildCubemap(root, t);
                    if (cube != null) result[t.id] = cube;
                    continue;
                }
                string p = $"{root}/Textures/{t.id}.png";
                if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                {
                    ti.sRGBTexture = t.srgb;
                    ti.wrapMode = TextureWrapMode.Repeat;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(Mathf.Max(t.width, t.height)));
                    ti.SaveAndReimport();
                }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (tex != null) result[t.id] = tex;
            }
            return result;
        }

        static Cubemap BuildCubemap(string root, TextureEntry t)
        {
            var faces = new Texture2D[6];
            for (int f = 0; f < 6; f++)
            {
                faces[f] = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!faces[f].LoadImage(File.ReadAllBytes($"{root}/Textures/{t.id}_f{f}.png"))) return null;
            }
            int size = faces[0].width;
            var cube = new Cubemap(size, TextureFormat.RGBA32, false) { name = t.id };
            for (int f = 0; f < 6; f++) cube.SetPixels(faces[f].GetPixels(), (CubemapFace)f);   // +X -X +Y -Y +Z -Z, same as Vulkan layers
            cube.Apply();
            foreach (var f in faces) Object.DestroyImmediate(f);
            AssetDatabase.CreateAsset(cube, $"{root}/Textures/{t.id}.cubemap");
            return cube;
        }

        static Dictionary<string, Shader> ImportShaders(string sourceDir, string root, string shadersJson)
        {
            var result = new Dictionary<string, Shader>();
            var list = JsonUtility.FromJson<ShadersFile>(File.ReadAllText(shadersJson));
            if (list?.shaders == null) return result;
            foreach (var s in list.shaders) File.Copy(Path.Combine(sourceDir, s.file), $"{root}/Shaders/{Path.GetFileName(s.file)}", true);
            AssetDatabase.Refresh();
            foreach (var s in list.shaders)
            {
                var sh = AssetDatabase.LoadAssetAtPath<Shader>($"{root}/Shaders/{Path.GetFileName(s.file)}");
                if (sh == null || ShaderUtil.ShaderHasError(sh) || !sh.isSupported)
                {
                    Debug.LogWarning($"[RenderDocToUnity] replay shader {s.shader_name} did not compile; family {s.family} falls back to a simple material");
                    continue;
                }
                result[$"fam{s.family}_{s.state}"] = sh;
            }
            return result;
        }

        static Dictionary<string, Mesh> ImportMeshes(string sourceDir, string root, SceneFile scene, ImportOptions opt, bool replay)
        {
            var meshes = new Dictionary<string, Mesh>();
            int built = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var m in scene.meshes)
                {
                    if (++built % 25 == 0 && EditorUtility.DisplayCancelableProgressBar("RenderDoc To Unity", $"Mesh {built}/{scene.meshes.Length}", built / (float)scene.meshes.Length))
                        throw new OperationCanceledException("Import cancelled");
                    var mesh = BuildMesh(Path.Combine(sourceDir, m.file), m, opt, replay);
                    mesh.name = $"{m.id}_e{m.first_event}";
                    AssetDatabase.CreateAsset(mesh, $"{root}/Meshes/{m.id}.asset");
                    meshes[m.id] = mesh;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }
            return meshes;
        }

        static Mesh BuildMesh(string path, MeshEntry m, ImportOptions opt, bool replay)
        {
            byte[] data = File.ReadAllBytes(path);
            int vc = m.vertex_count;
            var mesh = new Mesh { indexFormat = vc > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var pos = ReadVec3(data, m.layout.position.offset, vc);
            if (opt.mirrorX) for (int i = 0; i < vc; i++) pos[i].x = -pos[i].x;
            mesh.SetVertices(pos);
            if (m.layout.normal != null && m.layout.normal.offset >= 0)
            {
                var n = ReadVec3(data, m.layout.normal.offset, vc);
                if (opt.mirrorX) for (int i = 0; i < vc; i++) n[i].x = -n[i].x;
                mesh.SetNormals(n);
            }
            if (replay && m.layout.raw != null && m.layout.raw.Length > 0)
            {
                // Original vertex inputs: location k -> UV channel k (TEXCOORDk in the replay shader), bit-exact float4.
                foreach (var r in m.layout.raw)
                {
                    var v = new List<Vector4>(vc);
                    for (int i = 0; i < vc; i++)
                    {
                        int o = r.offset + i * 16;
                        v.Add(new Vector4(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8), BitConverter.ToSingle(data, o + 12)));
                    }
                    mesh.SetUVs(r.location, v);
                }
            }
            else if (m.layout.uv0 != null && m.layout.uv0.offset >= 0)
            {
                var uv = new Vector2[vc];
                for (int i = 0; i < vc; i++)
                {
                    int o = m.layout.uv0.offset + i * 8;
                    uv[i] = new Vector2(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4));
                }
                mesh.SetUVs(0, uv);
            }
            if (m.layout.color != null && m.layout.color.offset >= 0)
            {
                var col = new Color32[vc];
                for (int i = 0; i < vc; i++)
                {
                    int o = m.layout.color.offset + i * 4;
                    col[i] = new Color32(data[o], data[o + 1], data[o + 2], data[o + 3]);
                }
                mesh.SetColors(col);
            }
            int ic = m.layout.indices.components;
            var idx = new int[ic];
            Buffer.BlockCopy(data, m.layout.indices.offset, idx, 0, ic * 4);
            if (opt.mirrorX)
                for (int i = 0; i + 2 < ic; i += 3) { int t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
            mesh.SetIndices(idx, MeshTopology.Triangles, 0);
            if (m.layout.normal == null || m.layout.normal.offset < 0) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ helpers

        static void CopyPng(string src, string dst, bool flip)
        {
            if (!flip) { File.Copy(src, dst, true); return; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(src))) { Object.DestroyImmediate(tex); File.Copy(src, dst, true); return; }
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            var flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++) Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            tex.SetPixels32(flipped);
            File.WriteAllBytes(dst, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static Vector3[] ReadVec3(byte[] data, int offset, int vc)
        {
            var r = new Vector3[vc];
            for (int i = 0; i < vc; i++)
            {
                int o = offset + i * 12;
                r[i] = new Vector3(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8));
            }
            return r;
        }

        static Matrix4x4 ToMatrix(float[] f, bool mirrorX)
        {
            var m = new Matrix4x4();
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) m[r, c] = f[r * 4 + c];
            if (mirrorX)
            {
                var s = Matrix4x4.Scale(new Vector3(-1, 1, 1));
                m = s * m * s;
            }
            return m;
        }

        // Decomposes into TRS (negative determinant -> negative X scale). Returns false when the matrix has shear.
        static bool ApplyMatrix(Transform t, Matrix4x4 m)
        {
            Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
            float sx = c0.magnitude, sy = c1.magnitude, sz = c2.magnitude;
            if (sx < 1e-8f || sy < 1e-8f || sz < 1e-8f) return false;
            if (Vector3.Dot(Vector3.Cross(c0, c1), c2) < 0) { sx = -sx; c0 = -c0; }
            Vector3 x = c0 / Mathf.Abs(sx), y = c1 / sy, z = c2 / sz;
            if (Mathf.Abs(Vector3.Dot(x, y)) > 1e-3f || Mathf.Abs(Vector3.Dot(y, z)) > 1e-3f || Mathf.Abs(Vector3.Dot(x, z)) > 1e-3f) return false;
            t.localPosition = m.GetColumn(3);
            t.localRotation = Quaternion.LookRotation(z, y);
            t.localScale = new Vector3(sx, sy, sz);
            return true;
        }

        static Mesh BakeMatrix(Mesh src, Matrix4x4 m, string assetPath)
        {
            var dst = Object.Instantiate(src);
            var v = dst.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
            dst.vertices = v;
            dst.RecalculateNormals();
            dst.RecalculateBounds();
            AssetDatabase.CreateAsset(dst, AssetDatabase.GenerateUniqueAssetPath(assetPath));
            return dst;
        }

        // A half-transparent quad just in front of the capture camera showing the captured frame; toggle it to compare.
        static void CreateOverlay(Camera cam, Texture2D tex, string matPath)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ReferenceOverlay (enable to compare)";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(cam.transform, false);
            float d = cam.nearClipPlane * 1.5f;
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = tex.height > 0 ? tex.width / (float)tex.height : cam.aspect;
            go.transform.localPosition = new Vector3(0, 0, d);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(h * aspect, h, 1f);
            var mat = new Material(Shader.Find("Sprites/Default")) { name = "mat_reference_overlay", mainTexture = tex, color = new Color(1, 1, 1, 0.5f) };
            AssetDatabase.CreateAsset(mat, matPath);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
        }

        static Shader ResolveShader(string name)
        {
            if (!string.IsNullOrEmpty(name) && Shader.Find(name) != null) return Shader.Find(name);
            if (GraphicsSettings.currentRenderPipeline != null && Shader.Find("Universal Render Pipeline/Lit") != null)
                return Shader.Find("Universal Render Pipeline/Lit");
            return Shader.Find("Standard");
        }

        static Vector3 V3(float[] a, bool mirrorX) => new Vector3(mirrorX ? -a[0] : a[0], a[1], a[2]);

        static string SanitizeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace('.', '_');
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    class ImportWindow : EditorWindow
    {
        string source;
        ImportOptions opt = new ImportOptions();

        public static void Open(string dir)
        {
            var w = GetWindow<ImportWindow>(true, "RenderDoc To Unity");
            w.source = dir;
            w.minSize = new Vector2(440, 280);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Source", source, EditorStyles.wordWrappedLabel);
            opt.targetFolder = EditorGUILayout.TextField("Target folder", opt.targetFolder);
            opt.materialMode = (MaterialMode)EditorGUILayout.EnumPopup(new GUIContent("Material mode", "Replay: decompiled captured shaders (D3D11/12 editor). Simple: Lit + albedo guess."), opt.materialMode);
            opt.flipTextures = EditorGUILayout.Toggle(new GUIContent("Flip textures", "RenderDoc PNGs are top-row-first"), opt.flipTextures);
            opt.mirrorX = EditorGUILayout.Toggle(new GUIContent("Mirror X", "Enable for right-handed source engines"), opt.mirrorX);
            opt.importTextures = EditorGUILayout.Toggle("Import textures", opt.importTextures);
            opt.createCameras = EditorGUILayout.Toggle("Create capture cameras", opt.createCameras);
            opt.createReferenceOverlay = EditorGUILayout.Toggle("Reference overlay", opt.createReferenceOverlay);
            opt.groupByShader = EditorGUILayout.Toggle("Group by shader", opt.groupByShader);
            opt.showTransparent = EditorGUILayout.Toggle(new GUIContent("Show transparent", "Blended draws start hidden under Camera*/Transparent"), opt.showTransparent);
            using (new EditorGUI.DisabledScope(opt.materialMode != MaterialMode.Simple))
                opt.shaderName = EditorGUILayout.TextField(new GUIContent("Simple shader", "Empty = URP Lit or Standard"), opt.shaderName);
            GUILayout.Space(8);
            if (GUILayout.Button("Import", GUILayout.Height(30)))
            {
                try { RenderDocSceneImporter.Import(source, opt); Close(); }
                catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("RenderDoc To Unity", e.Message, "OK"); }
            }
        }
    }
}
