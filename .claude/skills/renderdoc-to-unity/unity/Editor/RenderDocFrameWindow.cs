// RenderDoc Frame Replay window: step through a replayed frame, preview any render target, and validate every pass
// against the ground truth exported from the capture (validate/*.raw).
// Menu: Window > Analysis > RenderDoc Frame Replay
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
    public class RenderDocFrameWindow : EditorWindow
    {
        RenderDocFramePlayer player;
        int previewResource = -1;
        Vector2 scroll;
        string validation = "";
        public List<(int op, int eid, string target, float mean, float max, bool flipped)> results = new List<(int, int, string, float, float, bool)>();
        public List<float> resultsBad = new List<float>();   // per result: share of pixels off by more than 0.1

        [MenuItem("Window/Analysis/RenderDoc Frame Replay")]
        public static void Open() => GetWindow<RenderDocFrameWindow>("RenderDoc Frame Replay");

        /// <summary>Open the window with the offline replay stopped after <paramref name="opIndex"/>, previewing <paramref name="target"/>.</summary>
        public static void ShowOp(RenderDocFramePlayer player, int opIndex, int target)
        {
            var w = GetWindow<RenderDocFrameWindow>("RenderDoc Frame Replay");
            w.player = player;
            player.stopAfterOp = opIndex;
            player.Replay();
            w.previewResource = target;
            w.Repaint();
        }

        void OnGUI()
        {
            #if UNITY_2023_1_OR_NEWER
            var found = player != null ? player : FindAnyObjectByType<RenderDocFramePlayer>();
#else
            var found = player != null ? player : FindObjectOfType<RenderDocFramePlayer>();
#endif
            player = (RenderDocFramePlayer)EditorGUILayout.ObjectField("Player", found, typeof(RenderDocFramePlayer), true);
            if (player == null || player.frame == null) { EditorGUILayout.HelpBox("Import a frame (Tools > RenderDoc To Unity > Import Frame Replay...) and select its player.", MessageType.Info); return; }
            var f = player.frame;
            EditorGUI.BeginChangeCheck();
            int stop = EditorGUILayout.IntSlider("Execute up to op", player.stopAfterOp < 0 ? f.ops.Length - 1 : player.stopAfterOp, 0, f.ops.Length - 1);
            player.invertCulling = EditorGUILayout.Toggle("Invert culling", player.invertCulling);
            player.convertClearDepth = EditorGUILayout.Toggle("Convert clear depth (reversed-Z)", player.convertClearDepth);
            if (EditorGUI.EndChangeCheck()) { player.stopAfterOp = stop >= f.ops.Length - 1 ? -1 : stop; player.Replay(); }
            var op = f.ops[Mathf.Clamp(stop, 0, f.ops.Length - 1)];
            EditorGUILayout.LabelField($"op {stop}: {op.type} eid {op.eid}" + (op.type == FrameOpType.Draw ? $"  material {(op.material >= 0 ? f.materials[op.material]?.name : "-")}" : ""));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Replay")) player.Replay();
                if (GUILayout.Button("Validate all passes")) Validate();
                if (GUILayout.Button(new GUIContent("Validate completeness", "Replay again with screen-sized targets starting cleared. If passes then differ, part of the frame was drawn before the capture began: it only exists as initial contents and cannot follow a free camera."))) ValidateCompleteness();
                if (GUILayout.Button("Show final")) previewResource = f.finalResource;
            }
            var scene = RenderDocScene.Active;
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(scene == null || Camera.main == null))
            {
                if (GUILayout.Button(new GUIContent("Main Camera to capture view", "Put the Main Camera at the captured viewpoint and field of view")))
                {
                    scene.CapturePose(out var pos, out var rot, out var fov);
                    Undo.RecordObject(Camera.main.transform, "Capture view");
                    Camera.main.transform.SetPositionAndRotation(pos, rot);
                    Undo.RecordObject(Camera.main, "Capture view");
                    Camera.main.fieldOfView = fov;
                }
                if (GUILayout.Button(new GUIContent("Scene View to capture view", "Align the Scene view camera with the captured viewpoint")))
                    AlignSceneView(scene);
                if (GUILayout.Button(new GUIContent("Validate scene render", "Render the scene with the Main Camera at the capture's resolution and compare with the captured image")))
                    validation = ValidateScene(scene);
            }
            var names = f.resources.Select((r, i) => $"{i}: {r.id} {r.format} {r.width}x{r.height}" + (r.rt ? "" : " (input)")).ToArray();
            previewResource = EditorGUILayout.Popup("Preview", Mathf.Clamp(previewResource, -1, names.Length - 1), names);
            Texture tex = previewResource >= 0 && previewResource < player.targets.Length && player.targets[previewResource] != null
                ? player.targets[previewResource] : (previewResource >= 0 ? f.resources[previewResource].init : null);
            if (tex != null)
            {
                float w = position.width - 10, h = w * tex.height / Mathf.Max(1, tex.width);
                var rect = GUILayoutUtility.GetRect(w, Mathf.Min(h, position.height * 0.55f));
                // targets keep the capture's memory order: row 0 is the top unless the capture is GL-style bottom-up
                if (Event.current.type == EventType.Repaint) DrawTarget(rect, tex, f.rowsBottomUp);
            }
            if (!string.IsNullOrEmpty(validation))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                EditorGUILayout.TextArea(validation);
                EditorGUILayout.EndScrollView();
            }
        }

        public string Validate()
        {
            var f = player.frame;
            results.Clear();
            resultsBad.Clear();
            var sb = new System.Text.StringBuilder("pass end op | eid | target | mean abs diff | max | pixels > 0.1 | orientation\n");
            int keep = player.stopAfterOp;
            for (int i = 0; i < f.ops.Length; i++)
            {
                var op = f.ops[i];
                if (op.type != FrameOpType.Pass || string.IsNullOrEmpty(op.validateFile) || op.validateTarget < 0) continue;
                int end = i + 1;
                while (end < f.ops.Length && f.ops[end].type == FrameOpType.Draw) end++;
                player.stopAfterOp = end - 1;
                player.Replay();
                var rt = player.targets[op.validateTarget];
                var res = f.resources[op.validateTarget];
                string path = Path.Combine(f.sourceDir, op.validateFile);
                if (rt == null || !File.Exists(path)) continue;
                var truth = Decode(File.ReadAllBytes(path), res);
                if (truth == null) { sb.Append($"{end - 1} | {op.eid} | {res.id} {res.format} | (format not decodable)\n"); continue; }
                var mine = ReadBack(rt);
                var (mean, max, flipped) = Compare(truth, mine, rt.width, rt.height, Channels(res.unityFormat));
                results.Add((end - 1, op.eid, res.id, mean, max, flipped));
                resultsBad.Add(lastBadFraction);
                bool differs = mean > 0.02f || lastBadFraction > 0.002f;   // exact replay: localized errors count too
                sb.Append($"{end - 1} | {op.eid} | {res.id} {res.format} | {mean:F4} | {max:F3} | {lastBadFraction:P2} | {(flipped ? "FLIPPED" : "ok")}{(differs ? "  <-- differs" : "")}\n");
            }
            player.stopAfterOp = keep;
            player.Replay();
            validation = sb.ToString();
            Repaint();
            return validation;
        }

        /// <summary>
        /// Is the captured range a complete frame? Validates twice: as captured, then with every screen-sized target starting
        /// cleared. A complete frame clears (or fully redraws) its screen targets, so both agree. If they differ, draws made
        /// before the capture began (emulators: RenderDoc splits frames at the display thread's swap, not the game's) left
        /// geometry that exists only as initial contents - exact at the capture view, stale for a free camera.
        /// Temporal history (TAA) is carried over legitimately and only affects its own pass.
        /// </summary>
        public string ValidateCompleteness()
        {
            Validate();
            var normal = results.ToList();
            var normalBad = resultsBad.ToList();
            bool keep = player.freshScreenTargets;
            player.freshScreenTargets = true;
            Validate();
            player.freshScreenTargets = keep;
            player.Replay();
            var sb = new System.Text.StringBuilder("completeness: pass | eid | target | mean as captured -> starting cleared | pixels > 0.1\n");
            int firstBad = -1, n = Math.Min(normal.Count, results.Count);
            var affectedEids = new List<int>();
            bool finalAffected = false;
            for (int i = 0; i < n; i++)
            {
                float dm = results[i].mean - normal[i].mean, db = resultsBad[i] - normalBad[i];
                bool affected = dm > 0.0015f || db > 0.002f;
                if (affected) { affectedEids.Add(normal[i].eid); if (firstBad < 0) firstBad = i; if (i == n - 1) finalAffected = true; }
                sb.Append($"{normal[i].op} | {normal[i].eid} | {normal[i].target} | {normal[i].mean:F4} -> {results[i].mean:F4} | {normalBad[i]:P2} -> {resultsBad[i]:P2}{(affected ? "  <-- depends on contents from before the capture" : "")}\n");
            }
            // Truncation (G-buffer/depth drawn before the capture) spreads to the final image; previous-frame inputs that the
            // tools do not substitute (e.g. a "...Last" depth for reprojection) stay local to their pass.
            sb.Insert(0, firstBad < 0
                ? "COMPLETE: no pass depends on what was drawn before the capture began - free camera is fine.\n\n"
                : !finalAffected
                ? $"COMPLETE (temporal inputs): passes at eid {string.Join(", ", affectedEids)} read previous-frame data (e.g. last frame's depth); the effect stays local " +
                  "and the final image does not depend on it. Free camera is fine; those effects may lag slightly away from the capture view.\n\n"
                : $"TRUNCATED: from eid {normal[firstBad].eid} the frame depends on contents drawn before the capture began, through to the final image. Exact at the " +
                  "capture view; a free camera will show that part stale (fixed ground/terrain, wrong occlusion or lighting). Re-capture until the frame starts inside " +
                  "the range (see references/capture-modes.md).\n\n");
            validation = sb.ToString();
            Repaint();
            return validation;
        }

        public static void AlignSceneView(RenderDocScene scene)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || scene == null) return;
            scene.CapturePose(out var position, out var rotation, out var fov);
            view.orthographic = false;
            var settings = view.cameraSettings;
            settings.fieldOfView = fov;
            view.cameraSettings = settings;
            // the Scene view camera sits `distance` behind the pivot, distance = size / sin(fov / 2)
            const float distance = 5f;
            view.LookAt(position + rotation * Vector3.forward * distance, rotation, distance * Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad), false, true);
            view.Repaint();
        }

        /// <summary>Render the live scene with the Main Camera at the capture's size and compare with the captured final image.</summary>
        public static string ValidateScene(RenderDocScene scene)
        {
            var f = scene.frame;
            var cam = Camera.main;
            var finalPass = f.ops.LastOrDefault(o => o.type == FrameOpType.Pass && o.validateTarget == f.finalResource && !string.IsNullOrEmpty(o.validateFile));
            if (cam == null || finalPass == null) return "no Main Camera or no captured final image";
            var rt = new RenderTexture(f.captureWidth, f.captureHeight, 24, GraphicsFormat.R8G8B8A8_SRGB);
            var keep = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = keep;
            var mine = ReadBack(rt);
            rt.Release();
            Object.DestroyImmediate(rt);
            var res = f.resources[f.finalResource];
            var truth = Decode(File.ReadAllBytes(Path.Combine(f.sourceDir, finalPass.validateFile)), res);
            // the camera target holds linear values (sRGB storage decoded on read); the capture's UNORM bytes are gamma-encoded
            if (res.unityFormat.EndsWith("_UNorm") && QualitySettings.activeColorSpace == ColorSpace.Linear)
                for (int i = 0; i < mine.Length; i++)
                    mine[i] = new Color(Mathf.LinearToGammaSpace(mine[i].r), Mathf.LinearToGammaSpace(mine[i].g), Mathf.LinearToGammaSpace(mine[i].b), 1);
            var (mean, max, flipped) = Compare(truth, mine, f.captureWidth, f.captureHeight, 3);
            return $"scene render (Main Camera, {f.captureWidth}x{f.captureHeight}) vs captured final image: mean abs diff {mean:F4}, max {max:F3}, pixels > 0.1: {lastBadFraction:P2} (supersampled edges add some), rows {(flipped ? "flipped" : "same order")}";
        }

        static Material displayMaterial;

        /// <summary>
        /// Draw a replayed target in editor GUI with its true colours. Editor GUI gamma-encodes its output in linear-space
        /// projects, so 8-bit UNORM targets (which hold gamma-encoded values) are decoded first; float targets hold linear
        /// values. Rows: targets keep the capture's memory order.
        /// </summary>
        public static void DrawTarget(Rect rect, Texture tex, bool rowsBottomUp)
        {
            if (displayMaterial == null)
                displayMaterial = new Material(Shader.Find("Hidden/RenderDocToUnity/Display")) { hideFlags = HideFlags.HideAndDontSave };
            var gf = tex.graphicsFormat;
            bool gammaStored = QualitySettings.activeColorSpace == ColorSpace.Linear && (gf == GraphicsFormat.R8G8B8A8_UNorm || gf == GraphicsFormat.B8G8R8A8_UNorm);
            displayMaterial.SetFloat("_Decode", gammaStored ? 1f : 0f);
            displayMaterial.mainTextureScale = new Vector2(1, rowsBottomUp ? 1 : -1);    // GUI draws v = 0 at the bottom
            displayMaterial.mainTextureOffset = new Vector2(0, rowsBottomUp ? 0 : 1);
            Graphics.DrawTexture(rect, tex, displayMaterial);
        }

        static int Channels(string uf) => uf.StartsWith("R8_") || uf.StartsWith("R16_") || uf.StartsWith("R32_") ? 1 : uf.StartsWith("R8G8_") || uf.StartsWith("R16G16_") || uf.StartsWith("R32G32_") ? 2 : uf.Contains("B10G11R11") ? 3 : 4;

        static Color[] ReadBack(RenderTexture rt)
        {
            var tmp = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(rt, tmp);
            var prev = RenderTexture.active;
            RenderTexture.active = tmp;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
            var px = t.GetPixels();
            Object.DestroyImmediate(t);
            return px;
        }

        /// <summary>Share of compared pixels off by more than 0.1 in the last Compare: catches errors confined to a small
        /// object (e.g. one character) that the mean hides.</summary>
        static float lastBadFraction;

        static (float, float, bool) Compare(Color[] truth, Color[] mine, int w, int h, int ch)
        {
            float Diff(bool flip, out float max, out float badFrac)
            {
                double sum = 0; max = 0; int n = 0, bad = 0;
                for (int y = 0; y < h; y += 2)
                    for (int x = 0; x < w; x += 2)
                    {
                        var a = truth[y * w + x];
                        var b = mine[(flip ? (h - 1 - y) : y) * w + x];
                        float d = Mathf.Abs(Mathf.Clamp01(a.r) - Mathf.Clamp01(b.r));
                        if (ch > 1) d += Mathf.Abs(Mathf.Clamp01(a.g) - Mathf.Clamp01(b.g));
                        if (ch > 2) d += Mathf.Abs(Mathf.Clamp01(a.b) - Mathf.Clamp01(b.b));
                        d /= Mathf.Min(ch, 3);
                        sum += d; n++; if (d > max) max = d;
                        if (d > 0.1f) bad++;
                    }
                badFrac = bad / (float)Math.Max(1, n);
                return (float)(sum / Math.Max(1, n));
            }
            float m0 = Diff(false, out float x0, out float b0), m1 = Diff(true, out float x1, out float b1);
            lastBadFraction = m0 <= m1 ? b0 : b1;
            return m0 <= m1 ? (m0, x0, false) : (m1, x1, true);
        }

        static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        static float Half(ushort h) => Mathf.HalfToFloat(h);

        static float UFloat(uint bits, int mant)   // unsigned 10/11-bit float (5-bit exponent)
        {
            uint e = (bits >> mant) & 31, m = bits & ((1u << mant) - 1);
            if (e == 0) return m / (float)(1 << mant) * Mathf.Pow(2, -14);
            if (e == 31) return 65504f;
            return (1 + m / (float)(1 << mant)) * Mathf.Pow(2, (int)e - 15);
        }

        /// <summary>Decode captured raw texels to linear RGBA in memory order (row 0 = first row).</summary>
        static Color[] Decode(byte[] raw, FrameResource r)
        {
            int n = r.width * r.height;
            var c = new Color[n];
            string f = r.unityFormat;
            for (int i = 0; i < n; i++)
            {
                switch (f)
                {
                    case "R8G8B8A8_UNorm": c[i] = new Color(raw[i * 4] / 255f, raw[i * 4 + 1] / 255f, raw[i * 4 + 2] / 255f, raw[i * 4 + 3] / 255f); break;
                    case "R8G8B8A8_SRGB": c[i] = new Color(SrgbToLinear(raw[i * 4] / 255f), SrgbToLinear(raw[i * 4 + 1] / 255f), SrgbToLinear(raw[i * 4 + 2] / 255f), raw[i * 4 + 3] / 255f); break;
                    case "B8G8R8A8_UNorm": c[i] = new Color(raw[i * 4 + 2] / 255f, raw[i * 4 + 1] / 255f, raw[i * 4] / 255f, raw[i * 4 + 3] / 255f); break;
                    case "B8G8R8A8_SRGB": c[i] = new Color(SrgbToLinear(raw[i * 4 + 2] / 255f), SrgbToLinear(raw[i * 4 + 1] / 255f), SrgbToLinear(raw[i * 4] / 255f), raw[i * 4 + 3] / 255f); break;
                    case "R8_UNorm": c[i] = new Color(raw[i] / 255f, 0, 0, 1); break;
                    case "R8G8_UNorm": c[i] = new Color(raw[i * 2] / 255f, raw[i * 2 + 1] / 255f, 0, 1); break;
                    case "R16_SFloat": c[i] = new Color(Half(BitConverter.ToUInt16(raw, i * 2)), 0, 0, 1); break;
                    case "R16G16_SFloat": c[i] = new Color(Half(BitConverter.ToUInt16(raw, i * 4)), Half(BitConverter.ToUInt16(raw, i * 4 + 2)), 0, 1); break;
                    case "R16G16B16A16_SFloat": c[i] = new Color(Half(BitConverter.ToUInt16(raw, i * 8)), Half(BitConverter.ToUInt16(raw, i * 8 + 2)), Half(BitConverter.ToUInt16(raw, i * 8 + 4)), Half(BitConverter.ToUInt16(raw, i * 8 + 6))); break;
                    case "R32_SFloat": c[i] = new Color(BitConverter.ToSingle(raw, i * 4), 0, 0, 1); break;
                    case "R32G32B32A32_SFloat": c[i] = new Color(BitConverter.ToSingle(raw, i * 16), BitConverter.ToSingle(raw, i * 16 + 4), BitConverter.ToSingle(raw, i * 16 + 8), BitConverter.ToSingle(raw, i * 16 + 12)); break;
                    case "B10G11R11_UFloatPack32":
                    {
                        uint v = BitConverter.ToUInt32(raw, i * 4);
                        c[i] = new Color(UFloat(v & 0x7FF, 6), UFloat((v >> 11) & 0x7FF, 6), UFloat((v >> 22) & 0x3FF, 5), 1);
                        break;
                    }
                    case "A2B10G10R10_UNormPack32":
                    {
                        uint v = BitConverter.ToUInt32(raw, i * 4);
                        c[i] = new Color((v & 1023) / 1023f, ((v >> 10) & 1023) / 1023f, ((v >> 20) & 1023) / 1023f, (v >> 30) / 3f);
                        break;
                    }
                    default: return null;
                }
            }
            return c;
        }
    }
}
