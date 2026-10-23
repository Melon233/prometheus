// Scene view wireframe of the selected reconstructed objects, to inspect the captured topology. The replay pipeline does
// not draw MeshRenderers (so Unity's "Selection Wire" gizmo has nothing to overlay); this draws the meshes of the
// selected RenderDocObject's Draw* children (or of a selected Draw* child alone) as lines on top of the Scene view.
// Hidden edges: the pipeline leaves the Scene view depth cleared, so for "visible edges only" the reconstructed objects'
// meshes are first drawn depth-only as occluders (self- and mutual occlusion), then the lines are depth-tested.
// Toggle: Tools > RenderDoc To Unity > Selection Wireframe, or the checkboxes in the RenderDocObject inspector.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RenderDocToUnity
{
    [InitializeOnLoad]
    public static class RenderDocWireframe
    {
        const string MenuPath = "Tools/RenderDoc To Unity/Selection Wireframe";
        const string KeyOn = "RenderDocToUnity.Wireframe", KeyXRay = "RenderDocToUnity.WireframeXRay";
        static Material mat;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(KeyOn, true);
            set { EditorPrefs.SetBool(KeyOn, value); SceneView.RepaintAll(); }
        }

        /// <summary>Draw every edge, hidden ones included (x-ray). Off: only edges not occluded by the reconstructed scene.</summary>
        public static bool XRay
        {
            get => EditorPrefs.GetBool(KeyXRay, false);
            set { EditorPrefs.SetBool(KeyXRay, value); SceneView.RepaintAll(); }
        }

        public static Color Color = new Color(1f, 0.55f, 0.1f, 0.6f);

        static RenderDocWireframe() => SceneView.duringSceneGui += OnSceneGUI;

        [MenuItem(MenuPath)] static void Toggle() => Enabled = !Enabled;
        [MenuItem(MenuPath, true)] static bool ToggleValidate() { Menu.SetChecked(MenuPath, Enabled); return true; }

        static void OnSceneGUI(SceneView view)
        {
            if (!Enabled || Event.current.type != EventType.Repaint) return;
            var selected = new List<MeshFilter>();
            foreach (var go in Selection.gameObjects)
            {
                if (go.GetComponent<RenderDocObject>() != null) selected.AddRange(go.GetComponentsInChildren<MeshFilter>());
                else if (go.GetComponentInParent<RenderDocObject>() != null && go.TryGetComponent<MeshFilter>(out var mf)) selected.Add(mf);
            }
            if (selected.Count == 0) return;
            if (mat == null)
            {
                var shader = Shader.Find("Hidden/RenderDocToUnity/Wire");
                if (shader == null) return;
                mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            mat.SetColor("_Color", Color);
            mat.SetFloat("_ZTest", (float)(XRay ? CompareFunction.Always : CompareFunction.LessEqual));
            if (!XRay)
            {
                // occluders: every visible reconstructed object, depth only
                mat.SetPass(0);
                var scene = RenderDocScene.Active;
                if (scene != null)
                    foreach (var mf in scene.GetComponentsInChildren<MeshFilter>())
                        Draw(mf);
            }
            bool keep = GL.wireframe;
            GL.wireframe = true;
            mat.SetPass(1);
            foreach (var mf in selected) Draw(mf);
            GL.wireframe = keep;
            // leave the depth as the pipeline left it (cleared) for the handles drawn after this
            if (!XRay) GL.Clear(true, false, Color.clear);
        }

        static void Draw(MeshFilter mf)
        {
            var mesh = mf.sharedMesh;
            if (mesh == null || !mf.gameObject.activeInHierarchy) return;
            var m = mf.transform.localToWorldMatrix;
            for (int s = 0; s < mesh.subMeshCount; s++) Graphics.DrawMeshNow(mesh, m, s);
        }
    }
}
