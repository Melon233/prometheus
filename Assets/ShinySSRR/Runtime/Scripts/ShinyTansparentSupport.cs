using UnityEngine;

namespace ShinySSRR {

/// <summary>
/// Replaced by ShinyTransparentSupport (fixes typo)
/// </summary>
[AddComponentMenu("")]
[ExecuteAlways]
    public class ShinyTansparentSupport : MonoBehaviour {

        void OnEnable () {
            ShinyTransparentSupport t = GetComponent<ShinyTransparentSupport>();
            if ( t== null) {
                gameObject.AddComponent<ShinyTransparentSupport>();
            }
            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(gameObject);
            #endif
            DestroyImmediate(this);

        }
    }

}