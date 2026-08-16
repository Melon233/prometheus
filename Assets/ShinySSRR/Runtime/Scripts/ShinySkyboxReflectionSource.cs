using UnityEngine;

namespace ShinySSRR {

    // Optional scene override for the Automatic Cubemap skybox reflection mode.
    [ExecuteAlways]
    [AddComponentMenu("Effects/Shiny SSR Skybox Reflection Source")]
    public class ShinySkyboxReflectionSource : MonoBehaviour {

        [Tooltip("Reflection probe whose cubemap is reused for skybox reflections in 'Automatic Cubemap' mode, instead of Shiny baking its own. If empty, a Reflection Probe on this same GameObject is used (if any). If no probe is found, this transform's position is used as the bake anchor.")]
        public ReflectionProbe probe;

        public static ShinySkyboxReflectionSource current;

        void OnEnable() { current = this; }

        void OnDisable() { if (current == this) current = null; }

        public ReflectionProbe ResolveProbe() {
            return probe != null ? probe : GetComponent<ReflectionProbe>();
        }
    }
}
