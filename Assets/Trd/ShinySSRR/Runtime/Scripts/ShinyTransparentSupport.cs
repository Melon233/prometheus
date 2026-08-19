using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace ShinySSRR {

[ExecuteAlways]
    public class ShinyTransparentSupport : MonoBehaviour {

        [Tooltip("Include all child renderers in addition to this GameObject's renderer.")]
        public bool includeChildren;

        [Tooltip("Draw depth using the renderer's own shader pass instead of Shiny's depth-only material. Required for shaders with vertex displacement (e.g. Crest water).")]
        public bool useOwnDepthPass;

        [Tooltip("Name of the shader pass used for the depth-only render when 'Use Own Depth Pass' is enabled.")]
        public string depthPassName = "DepthOnly";

        [NonSerialized]
        public Renderer[] renderers;

        [NonSerialized]
        public int[] depthPassIndices;

        public void OnEnable () {
            RefreshRenderers();
            ShinySSRR.RegisterTransparentSupport(this);
        }

        public void OnDisable () {
            ShinySSRR.UnregisterTransparentSupport(this);
        }

        public void RefreshRenderers () {
            if (includeChildren) {
                renderers = GetComponentsInChildren<Renderer>(true);
            } else {
                Renderer self = GetComponent<Renderer>();
                renderers = self != null ? new Renderer[] { self } : new Renderer[0];
            }

            int count = renderers.Length;
            if (depthPassIndices == null || depthPassIndices.Length != count) {
                depthPassIndices = new int[count];
            }
            for (int i = 0; i < count; i++) {
                int pass = -1;
                if (useOwnDepthPass) {
                    Renderer r = renderers[i];
                    if (r != null) {
                        Material mat = r.sharedMaterial;
                        if (mat != null && mat.shader != null && !string.IsNullOrEmpty(depthPassName)) {
                            pass = mat.FindPass(depthPassName);
                        }
                    }
                }
                depthPassIndices[i] = pass;
            }
        }

#if UNITY_EDITOR
        void OnValidate () {
            RefreshRenderers();
        }
#endif

    }

}