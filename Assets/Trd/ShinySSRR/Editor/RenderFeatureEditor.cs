using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

namespace ShinySSRR {

    [CustomEditor(typeof(ShinySSRR))]
    public class RenderFeatureEditor : Editor {

        SerializedProperty useDeferred, enableScreenSpaceNormalsPass, customSmoothnessMetallicPass, customSmoothnessMetallicPassSource;
        SerializedProperty renderPassEvent, ignorePostProcessingOption, cameraLayerMask, ignoreReflectionProbes;
        SerializedProperty screenSpaceNormalsOpaques, includeTransparentsInScreenSpaceNormals, transparentNormalsLayerMask;
        SerializedProperty enableTransparencyDepthPrepass, transparencyDepthPrepassLayerMask;
        Volume shinyVolume;

        private void OnEnable() {
            renderPassEvent = serializedObject.FindProperty("renderPassEvent");
            useDeferred = serializedObject.FindProperty("useDeferred");
            ignorePostProcessingOption = serializedObject.FindProperty("ignorePostProcessingOption");
            cameraLayerMask = serializedObject.FindProperty("cameraLayerMask");
            customSmoothnessMetallicPass = serializedObject.FindProperty("customSmoothnessMetallicPass");
            customSmoothnessMetallicPassSource = serializedObject.FindProperty("customSmoothnessMetallicPassSource");
            enableScreenSpaceNormalsPass = serializedObject.FindProperty("enableScreenSpaceNormalsPass");
            ignoreReflectionProbes = serializedObject.FindProperty("ignoreReflectionProbes");
            screenSpaceNormalsOpaques = serializedObject.FindProperty("screenSpaceNormalsOpaques");
            includeTransparentsInScreenSpaceNormals = serializedObject.FindProperty("includeTransparentsInScreenSpaceNormals");
            transparentNormalsLayerMask = serializedObject.FindProperty("transparentNormalsLayerMask");
            enableTransparencyDepthPrepass = serializedObject.FindProperty("enableTransparencyDepthPrepass");
            transparencyDepthPrepassLayerMask = serializedObject.FindProperty("transparencyDepthPrepassLayerMask");

            FindShinySSRRVolume();
        }


        void FindShinySSRRVolume() {
            Volume[] vols = Misc.FindObjectsOfType<Volume>(true);
            foreach (Volume volume in vols) {
                if (volume.sharedProfile != null && volume.sharedProfile.Has<ShinyScreenSpaceRaytracedReflections>()) {
                    shinyVolume = volume;
                    return;
                }
            }
        }


        public override void OnInspectorGUI() {

            if (shinyVolume != null) {
                EditorGUILayout.HelpBox("Select the Post Processing Volume to customize reflections settings.", MessageType.Info);
                if (GUILayout.Button("Show Volume Settings")) {
                    Selection.SetActiveObjectWithContext(shinyVolume, null);
                    GUIUtility.ExitGUI();
                }
            } else {
                EditorGUILayout.HelpBox("Create a Post Processing volume in the scene to customize Shiny SSR settings.", MessageType.Info);
            }

            EditorGUILayout.Separator();
            EditorGUILayout.PropertyField(renderPassEvent);
            EditorGUILayout.PropertyField(cameraLayerMask);
            EditorGUILayout.PropertyField(ignorePostProcessingOption);
            EditorGUILayout.PropertyField(useDeferred);
            EditorGUILayout.PropertyField(ignoreReflectionProbes);

            EditorGUILayout.Separator();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(enableTransparencyDepthPrepass, new GUIContent("Enable Depth Prepass"));
            if (GUILayout.Button("?", GUILayout.Width(24))) {
                const string dialogMessage = "Renders the depth of transparent objects in the selected layers so SSR rays can hit transparent surfaces during ray-marching.\n\nEnable when reflective surfaces need to reflect other transparents (e.g. a puddle reflecting a glass window, calm water reflecting a waterfall in front of it).\n\nSkip when reflective surfaces only reflect opaque scenery (sky, terrain, buildings). Saves GPU.\n\nWorks the same in forward and deferred rendering paths.";
                EditorUtility.DisplayDialog("Depth Prepass", dialogMessage, "OK");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            if (enableTransparencyDepthPrepass.boolValue) {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(transparencyDepthPrepassLayerMask, new GUIContent("Layer Mask"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(enableScreenSpaceNormalsPass, new GUIContent("Enable Screen Space Normals"));
            if (GUILayout.Button("?", GUILayout.Width(24))) {
                const string dialogMessage = "Master toggle for screen space normals. When OFF, the sub-options below are disabled.\n\nWhen ON, choose the sources:\n\n- Opaques: in forward, requests URP to generate the _CameraNormalsTexture (only opaque objects). In deferred, opaque normals come from the GBuffer for free, so this is a no-op.\n\n- Transparents: renders transparent objects in the layer mask using their DepthNormals pass into a custom buffer. Works in both forward and deferred. Required to see normals from water/glass surfaces.";
                EditorUtility.DisplayDialog("Screen Space Normals", dialogMessage, "OK");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(!enableScreenSpaceNormalsPass.boolValue)) {
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(useDeferred.boolValue)) {
                    EditorGUILayout.PropertyField(screenSpaceNormalsOpaques, new GUIContent("Opaques"));
                }
                EditorGUILayout.PropertyField(includeTransparentsInScreenSpaceNormals, new GUIContent("Transparents"));
                if (includeTransparentsInScreenSpaceNormals.boolValue) {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(transparentNormalsLayerMask, new GUIContent("Layer Mask"));
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Separator();

            if (!useDeferred.boolValue) {
                EditorGUILayout.PropertyField(customSmoothnessMetallicPass);
                if (customSmoothnessMetallicPass.boolValue) {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(customSmoothnessMetallicPassSource, new GUIContent("Pass Source", "Shiny: Shiny renders a prepass that gathers smoothness (R) and metallic (G) from your objects into the _SmoothnessMetallicRT texture. Your shaders must declare a pass named 'SmoothnessMetallic'.\n\nUser Custom Pass: you provide the _SmoothnessMetallicRT texture yourself from your own render pass (custom SRP feature, third party water, etc). Shiny reads it as-is."));
                    if (customSmoothnessMetallicPassSource.intValue == (int)SmoothnessMetallicPassSource.UserCustomPass) {
                        EditorGUILayout.HelpBox("Your custom pass must output a global render texture named _SmoothnessMetallicRT (R = smoothness, G = metallic).", MessageType.Info);
                    } else {
                        EditorGUILayout.HelpBox("Shiny gathers smoothness/metallic by rendering objects whose shader declares a 'SmoothnessMetallic' pass (R = smoothness, G = metallic). Materials without that pass produce no reflections in this mode.", MessageType.Info);
                    }
                    EditorGUI.indentLevel--;
                } else {
                    EditorGUILayout.HelpBox("In forward rendering, reflections can be added to the scene in two ways:\nA) adding a Reflections script to the objects you want to receive reflections, OR\nB) enabling the 'Custom Smoothness Metallic Pass' option (requires that the shaders support a specific pass named 'SmoothnessMetallic').", MessageType.Info);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

    }
}