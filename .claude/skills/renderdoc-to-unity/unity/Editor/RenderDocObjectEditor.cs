// Inspector for a reconstructed object: every captured draw that renders it (G-buffer, shadow caster, forward,
// instanced slot...), with the mesh, material, textures and render targets each one uses. The scene object itself only
// carries the Transform; the replay pipeline draws these ops from the RenderDocFrame asset.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RenderDocToUnity
{
    [CustomEditor(typeof(RenderDocObject))]
    public class RenderDocObjectEditor : Editor
    {
        readonly HashSet<int> open = new HashSet<int>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var ro = (RenderDocObject)target;
            var scene = ro.GetComponentInParent<RenderDocScene>();
            var f = scene != null ? scene.frame : null;
            if (f == null || ro.index < 0 || ro.index >= f.objects.Length)
            {
                EditorGUILayout.HelpBox("Not under a RenderDocScene with frame data.", MessageType.Info);
                return;
            }
            var fo = f.objects[ro.index];
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                RenderDocWireframe.Enabled = EditorGUILayout.ToggleLeft(new GUIContent("Wireframe in Scene view", "Draw the selected object's captured meshes as lines (topology)"), RenderDocWireframe.Enabled);
                using (new EditorGUI.DisabledScope(!RenderDocWireframe.Enabled))
                    RenderDocWireframe.XRay = EditorGUILayout.ToggleLeft(new GUIContent("X-ray (hidden edges)", "On: every edge, including those behind surfaces. Off: only edges not occluded by the reconstructed scene."), RenderDocWireframe.XRay);
            }
            EditorGUILayout.HelpBox(
                "Rendered by the replay pipeline, not by MeshRenderers: the draws below run in captured order with their original " +
                "shaders, and this Transform replaces the object matrix in their constant buffers. The child Draw* renderers only serve " +
                "selection and bounds; disable this GameObject (not a renderer) to hide the object.", MessageType.None);
            EditorGUILayout.LabelField($"Draws ({fo.ops.Length})", EditorStyles.boldLabel);
            var player = FindAnyObjectByType<RenderDocFramePlayer>(FindObjectsInactive.Include);
            foreach (int k in fo.ops.OrderBy(x => x))
            {
                if (k < 0 || k >= f.ops.Length) continue;
                var op = f.ops[k];
                var pass = PassOf(f, k);
                string role = Role(f, pass);
                var mesh = op.mesh >= 0 && op.mesh < f.meshes.Length ? f.meshes[op.mesh] : null;
                var mat = op.material >= 0 && op.material < f.materials.Length ? f.materials[op.material] : null;
                string head = $"eid {op.eid}  ·  {role}" + (op.instances > 1 ? $"  ·  {op.instances} instances" : "");
                bool isOpen = open.Contains(k);
                bool now = EditorGUILayout.Foldout(isOpen, head, true);
                if (now != isOpen) { if (now) open.Add(k); else open.Remove(k); }
                if (!now) continue;
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.ObjectField("Mesh", mesh, typeof(Mesh), false);
                    if (mesh != null)
                        EditorGUILayout.LabelField(" ", $"{mesh.vertexCount} vertices, {mesh.GetIndexCount(0) / 3} triangles");
                    EditorGUILayout.ObjectField("Material", mat, typeof(Material), false);
                    if (mat != null && mat.shader != null)
                        EditorGUILayout.ObjectField("Shader", mat.shader, typeof(Shader), false);
                    EditorGUILayout.LabelField("Render targets", Targets(f, pass));
                    if (op.textures.Length > 0)
                    {
                        EditorGUILayout.LabelField("Textures");
                        using (new EditorGUI.IndentLevelScope())
                            foreach (var tb in op.textures)
                            {
                                var r = tb.resource >= 0 && tb.resource < f.resources.Length ? f.resources[tb.resource] : null;
                                if (r == null) { EditorGUILayout.LabelField(tb.name, "(unbound)"); continue; }
                                if (r.init != null && !r.rt)
                                    EditorGUILayout.ObjectField(tb.name, r.init, typeof(Texture), false);
                                else
                                    EditorGUILayout.LabelField(tb.name, $"{Describe(r)}{(r.rt ? " (rendered in this frame)" : "")}{(tb.historyOf >= 0 ? " [TAA history]" : "")}");
                            }
                    }
                    var kinds = op.cbuffers.SelectMany(c => c.patches).Select(p => p.kind).Distinct().ToArray();
                    if (kinds.Length > 0)
                        EditorGUILayout.LabelField("Rewritten per view", string.Join(", ", kinds));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(EditorGUI.indentLevel * 15);
                        var child = ro.transform.Find($"Draw{op.eid}");
                        using (new EditorGUI.DisabledScope(child == null))
                            if (GUILayout.Button(new GUIContent("Select Draw child", child == null ? "No on-screen child for this draw (e.g. shadow caster)" : ""))) Selection.activeObject = child.gameObject;
                        using (new EditorGUI.DisabledScope(player == null))
                            if (GUILayout.Button(new GUIContent("Replay up to this draw", "Offline replay stopped after this draw, previewing its first render target")))
                                RenderDocFrameWindow.ShowOp(player, k, FirstTarget(f, pass));
                        if (GUILayout.Button(new GUIContent("Copy eid", "Event ID to jump to in RenderDoc"))) EditorGUIUtility.systemCopyBuffer = op.eid.ToString();
                    }
                }
            }
        }

        static FrameOp PassOf(RenderDocFrame f, int k)
        {
            for (int i = k; i >= 0; i--) if (f.ops[i].type == FrameOpType.Pass) return f.ops[i];
            return null;
        }

        static int FirstTarget(RenderDocFrame f, FrameOp pass)
        {
            if (pass == null) return -1;
            foreach (int c in pass.colors) if (c >= 0) return c;
            return pass.depthTarget;
        }

        static string Describe(FrameResource r) => $"{r.id.Replace("ResourceId::", "#")} {r.format} {r.width}x{r.height}";

        static string Targets(RenderDocFrame f, FrameOp pass)
        {
            if (pass == null) return "-";
            var parts = pass.colors.Where(c => c >= 0 && c < f.resources.Length).Select(c => Describe(f.resources[c])).ToList();
            if (pass.depthTarget >= 0 && pass.depthTarget < f.resources.Length) parts.Add("depth " + Describe(f.resources[pass.depthTarget]));
            return parts.Count > 0 ? string.Join(", ", parts) : "-";
        }

        /// <summary>A short guess at the pass's purpose from its targets (the capture carries no pass names).</summary>
        static string Role(RenderDocFrame f, FrameOp pass)
        {
            if (pass == null) return "?";
            var colors = pass.colors.Where(c => c >= 0 && c < f.resources.Length).Select(c => f.resources[c]).ToArray();
            var depth = pass.depthTarget >= 0 && pass.depthTarget < f.resources.Length ? f.resources[pass.depthTarget] : null;
            bool screen(FrameResource r) => r.screenLevel == 0 || (r.width == f.captureWidth && r.height == f.captureHeight);
            if (colors.Length == 0 && depth != null) return screen(depth) ? "depth only" : $"shadow / depth map {depth.width}x{depth.height}";
            if (colors.Length >= 3 && screen(colors[0])) return "G-buffer";
            if (colors.Length > 0 && screen(colors[0])) return colors.Length > 1 ? $"screen ({colors.Length} targets)" : "screen colour";
            return colors.Length > 0 ? $"offscreen {colors[0].width}x{colors[0].height}" : "?";
        }
    }
}
