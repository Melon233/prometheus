#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

namespace Polyart
{

    public class TilingMaterialCustomInspector : ShaderGUI
    {
        private bool showLayers = false;
        private bool showParallax = false;
        private bool showCoverage = false;

        // Utility to sync keyword with float property
        void SetKeyword(Material mat, string keyword, bool state)
        {
            if (state)
                mat.EnableKeyword(keyword);
            else
                mat.DisableKeyword(keyword);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            // Get the actual material being edited
            Material material = materialEditor.target as Material;

            GUILayout.Space(15);

            GUILayout.Label("Tiling Material", CustomInspectorsHelper.LargeLabelStyle);

            EditorGUILayout.Space(20, true);

            // Create a box or area for the text to constrain width
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(
                "This Shader uses an advanced layering system to create a Layered Effect. You can blend up to 3 Layers and a Coverage Layer on top of that.",
                CustomInspectorsHelper.SmallLabelStyle
            );
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showLayers = CustomInspectorsHelper.CenteredFoldout(showLayers, "Tiling Layers", CustomInspectorsHelper.MediumLabelStyle);

            GUILayout.Space(5);
            if (showLayers)
            {
                GUILayout.Label(
                    "You can click on the Layer Buttons to Enable/Disable them. \n* In order for Layer 3 to be available you must have Layer 2 Enabled.\n",
                    CustomInspectorsHelper.SmallLabelStyle
                );
                GUILayout.Space(10);

                // Get the float property that backs the keyword
                MaterialProperty useEmissiveProp = FindProperty("_USE_EMISSIVE", properties);
                materialEditor.ShaderProperty(useEmissiveProp, "Use Emissive");

                // Convert float to bool for UI
                bool useEmissive = useEmissiveProp.floatValue == 1;

                GUILayout.Space(10);

                // Draw the toggle button
                GUI.enabled = false;
                if (GUILayout.Button("Layer 1 Enabled", CustomInspectorsHelper.EnabledButtonStyle))
                {

                }
                GUI.enabled = true;

                GUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                GUILayout.Label("Color", CustomInspectorsHelper.SmallLabelStyleCenter );
                materialEditor.ShaderProperty(FindProperty("_Layer_01_Color", properties), "Color Map");
                materialEditor.ShaderProperty(FindProperty("_Layer_01_Tint", properties), "Tint");
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                GUILayout.Label("Normals", CustomInspectorsHelper.SmallLabelStyleCenter);
                materialEditor.ShaderProperty(FindProperty("_Layer_01_Normal", properties), "Normal Map");
                materialEditor.ShaderProperty(FindProperty("_Layer_01_Normal_Strength", properties), "Normal Strength");
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                GUILayout.Label("ORMH", CustomInspectorsHelper.SmallLabelStyleCenter);
                materialEditor.ShaderProperty(FindProperty("_Layer_01_ORM", properties), "ORMH Map");
                materialEditor.ShaderProperty(FindProperty("_Layer_01_Metallic", properties), "Metallic Multiplier");
                materialEditor.ShaderProperty(FindProperty("_Layer_01_Roughness", properties), "Smoothness Multiplier");
                materialEditor.ShaderProperty(FindProperty("_Layer_01_AO", properties), "AO Intensity");
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();

                if (useEmissive)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Space(5);
                    GUILayout.Label("Emissive", CustomInspectorsHelper.SmallLabelStyleCenter);
                    materialEditor.ShaderProperty(FindProperty("_Layer_01_Emissive", properties), "Emissive Map");
                    materialEditor.ShaderProperty(FindProperty("_Layer_01_Emissive_Color", properties), "Emissive HDR Tint");
                    GUILayout.Space(5);
                    EditorGUILayout.EndVertical();
                }

                GUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                GUILayout.Label("Layer 1 Tiling Variation", CustomInspectorsHelper.SmallLabelStyleCenter);
                GUILayout.Space(5);

                // Get the float property that backs the keyword
                MaterialProperty useVariationProp = FindProperty("_USE_TILING_VARIATION", properties);
                materialEditor.ShaderProperty(useVariationProp, "Use Tiling Variation");

                // Convert float to bool for UI
                bool useVariation = useVariationProp.floatValue == 1;

                if (useVariation)
                {
                    GUILayout.Space(5);
                    materialEditor.ShaderProperty(FindProperty("_Variation_Tiling", properties), "Tiling Multiplier");
                    materialEditor.ShaderProperty(FindProperty("_Layer_01_Variation_Tint", properties), "Tint");
                    GUILayout.Space(5);
                    materialEditor.ShaderProperty(FindProperty("_VariationNoiseTiling", properties), "Noise Mask Tiling");
                    materialEditor.ShaderProperty(FindProperty("_Use_Vertex_Paint_for_Tiling_Variation", properties), "Use Vertex Paint insteaf of Noise");
                    materialEditor.ShaderProperty(FindProperty("_Variation_Mask_Contrast", properties), "Mask Contrast");
                }
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();

                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
                
                GUILayout.Space(10);

                // Get the float property that backs the keyword
                MaterialProperty useLayer2Prop = FindProperty("_USE_LAYER_2", properties);

                // Convert float to bool for UI
                bool layer2 = useLayer2Prop.floatValue == 1;

                // Draw the toggle button
                EditorGUI.BeginChangeCheck();
                if (GUILayout.Button(layer2 ? "Layer 2 Enabled" : "Layer 2 Disabled", layer2 ? CustomInspectorsHelper.EnabledButtonStyle : CustomInspectorsHelper.DisabledButtonStyle))
                {
                    layer2 = !layer2; // toggle value
                }

                if (EditorGUI.EndChangeCheck())
                {
                    // Register undo and apply new value
                    Undo.RecordObject(material, "Toggle _USE_LAYER_2");

                    useLayer2Prop.floatValue = layer2 ? 1 : 0;

                    // Update the keyword based on property
                    SetKeyword(material, "_USE_LAYER_2", layer2);

                    // Mark material dirty and refresh UI
                    EditorUtility.SetDirty(material);
                    materialEditor.PropertiesChanged();
                }

                // Ensure keyword stays in sync on domain reload / recompile
                SetKeyword(material, "_USE_LAYER_2", useLayer2Prop.floatValue == 1);

                // Show Layer 2 properties if keyword is enabled
                if (layer2)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Space(5);

                    materialEditor.ShaderProperty(FindProperty("_Layer_1_2_Contrast", properties), "Layer 1 -> 2 Contrast");
                    GUILayout.Space(5);

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Space(5);
                    GUILayout.Label("Color", CustomInspectorsHelper.SmallLabelStyleCenter);
                    materialEditor.ShaderProperty(FindProperty("_Layer_02_Color", properties), "Color Map");
                    materialEditor.ShaderProperty(FindProperty("_Layer_02_Tint", properties), "Tint");
                    GUILayout.Space(5);
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(5);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Space(5);
                    GUILayout.Label("Normals", CustomInspectorsHelper.SmallLabelStyleCenter);
                    materialEditor.ShaderProperty(FindProperty("_Layer_02_Normal", properties), "Normal Map");
                    materialEditor.ShaderProperty(FindProperty("_Layer_02_Normal_Strength", properties), "Normal Strength");
                    GUILayout.Space(5);
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(5);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Space(5);
                    GUILayout.Label("ORMH", CustomInspectorsHelper.SmallLabelStyleCenter);
                    materialEditor.ShaderProperty(FindProperty("_Layer_02_ORM", properties), "ORMH Map");
                    materialEditor.ShaderProperty(FindProperty("_Layer_02_Metallic", properties), "Metallic Multiplier");
                    materialEditor.ShaderProperty(FindProperty("_Layer_02_Roughness", properties), "Smoothness Multiplier");
                    materialEditor.ShaderProperty(FindProperty("_Layer_02_AO", properties), "AO Intensity");
                    GUILayout.Space(5);
                    EditorGUILayout.EndVertical();

                    if (useEmissive)
                    {
                        GUILayout.Space(5);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.Space(5);
                        GUILayout.Label("Emissive", CustomInspectorsHelper.SmallLabelStyleCenter);
                        materialEditor.ShaderProperty(FindProperty("_Layer_02_Emissive", properties), "Emissive Map");
                        materialEditor.ShaderProperty(FindProperty("_Layer_02_Emissive_Color", properties), "Emissive HDR Tint");
                        GUILayout.Space(5);
                        EditorGUILayout.EndVertical();
                    }

                    GUILayout.Space(5);
                    EditorGUILayout.EndVertical();
                }

                if (layer2)
                {
                    GUILayout.Space(10);

                    // Get the float property that backs the keyword
                    MaterialProperty useLayer3Prop = FindProperty("_USE_LAYER_3", properties);

                    // Convert float to bool for UI
                    bool layer3 = useLayer3Prop.floatValue == 1;

                    // Draw the toggle button
                    EditorGUI.BeginChangeCheck();
                    if (GUILayout.Button(layer3 ? "Layer 3 Enabled" : "Layer 3 Disabled", layer3 ? CustomInspectorsHelper.EnabledButtonStyle : CustomInspectorsHelper.DisabledButtonStyle))
                    {
                        layer3 = !layer3; // toggle value
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        // Register undo and apply new value
                        Undo.RecordObject(material, "Toggle _USE_LAYER_2");

                        useLayer3Prop.floatValue = layer3 ? 1 : 0;

                        // Update the keyword based on property
                        SetKeyword(material, "_USE_LAYER_3", layer3);

                        // Mark material dirty and refresh UI
                        EditorUtility.SetDirty(material);
                        materialEditor.PropertiesChanged();
                    }

                    // Ensure keyword stays in sync on domain reload / recompile
                    SetKeyword(material, "_USE_LAYER_3", useLayer3Prop.floatValue == 1);

                    // Show Layer 2 properties if keyword is enabled
                    if (layer3)
                    {
                        GUILayout.Space(5);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.Space(5);

                        materialEditor.ShaderProperty(FindProperty("_Layer_2_3_Contrast", properties), "Layer 2 -> 3 Contrast");
                        GUILayout.Space(5);

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.Space(5);
                        GUILayout.Label("Color", CustomInspectorsHelper.SmallLabelStyleCenter);
                        materialEditor.ShaderProperty(FindProperty("_Layer_03_Color", properties), "Color Map");
                        materialEditor.ShaderProperty(FindProperty("_Layer_03_Tint", properties), "Tint");
                        GUILayout.Space(5);
                        EditorGUILayout.EndVertical();
                        GUILayout.Space(5);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.Space(5);
                        GUILayout.Label("Normals", CustomInspectorsHelper.SmallLabelStyleCenter);
                        materialEditor.ShaderProperty(FindProperty("_Layer_03_Normal", properties), "Normal Map");
                        materialEditor.ShaderProperty(FindProperty("_Layer_03_Normal_Strength", properties), "Normal Strength");
                        GUILayout.Space(5);
                        EditorGUILayout.EndVertical();
                        GUILayout.Space(5);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.Space(5);
                        GUILayout.Label("ORMH", CustomInspectorsHelper.SmallLabelStyleCenter);
                        materialEditor.ShaderProperty(FindProperty("_Layer_03_ORM", properties), "ORMH Map");
                        materialEditor.ShaderProperty(FindProperty("_Layer_03_Metallic", properties), "Metallic Multiplier");
                        materialEditor.ShaderProperty(FindProperty("_Layer_03_Roughness", properties), "Smoothness Multiplier");
                        materialEditor.ShaderProperty(FindProperty("_Layer_03_AO", properties), "AO Intensity");
                        GUILayout.Space(5);
                        EditorGUILayout.EndVertical();

                        if (useEmissive)
                        {
                            GUILayout.Space(5);
                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            GUILayout.Space(5);
                            GUILayout.Label("Emissive", CustomInspectorsHelper.SmallLabelStyleCenter);
                            materialEditor.ShaderProperty(FindProperty("_Layer_03_Emissive", properties), "Emissive Map");
                            materialEditor.ShaderProperty(FindProperty("_Layer_03_Emissive_Color", properties), "Emissive HDR Tint");
                            GUILayout.Space(5);
                            EditorGUILayout.EndVertical();
                        }

                        GUILayout.Space(5);
                        EditorGUILayout.EndVertical();
                    }
                }
                

                GUILayout.Space(15);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showParallax = CustomInspectorsHelper.CenteredFoldout(showParallax, "Parallax Occlusion", CustomInspectorsHelper.MediumLabelStyle);

            GUILayout.Space(5);

            if (showParallax)
            {
                GUILayout.Label(
                    "These parameters apply to all three Layers.",
                    CustomInspectorsHelper.SmallLabelStyle
                );
                GUILayout.Space(10);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);

                // Get the float property that backs the keyword
                MaterialProperty useParallaxProp = FindProperty("_USE_PARALLAX_OCCLUSION", properties);
                materialEditor.ShaderProperty(useParallaxProp, "Use Parallax Occlusion");

                // Convert float to bool for UI
                bool useParallax = useParallaxProp.floatValue == 1;

                if (useParallax )
                {
                    GUILayout.Space(5);
                    materialEditor.ShaderProperty(FindProperty("_POM_Height_Ratio", properties), "POM Depth");
                    materialEditor.ShaderProperty(FindProperty("_POM_Max_Steps", properties), "POM Max Steps");
                }

                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
                GUILayout.Space(15);
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showCoverage = CustomInspectorsHelper.CenteredFoldout(showCoverage, "Dirt Coverage", CustomInspectorsHelper.MediumLabelStyle);

            GUILayout.Space(5);

            if (showCoverage)
            {
                GUILayout.Label(
                    "The Coverage Layer Blends on top of the others based on the final Heightmap. First it fills the cracks and crevices and slowly moves on top.",
                    CustomInspectorsHelper.SmallLabelStyle
                );
                GUILayout.Space(10);

                GUILayout.Space(10);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);

                // Get the float property that backs the keyword
                MaterialProperty useCoverageProp = FindProperty("_USE_DIRT_MASK", properties);
                materialEditor.ShaderProperty(useCoverageProp, "Use Dirt Coverage");

                // Convert float to bool for UI
                bool useCoverage = useCoverageProp.floatValue == 1;

                if (useCoverage)
                {
                    GUILayout.Space(5);
                    materialEditor.ShaderProperty(FindProperty("_Dirt_Mask_Growth", properties), "Mask Growth on Heightmap");
                    materialEditor.ShaderProperty(FindProperty("_Dirt_Mask_Feather", properties), "Mask Feather");
                    GUILayout.Space(5);
                    materialEditor.ShaderProperty(FindProperty("_Dirt_Color", properties), "Color");
                    materialEditor.ShaderProperty(FindProperty("_Dirt_Smoothness", properties), "Smoothness");
                }

                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
                GUILayout.Space(15);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);           
        }
    }
}
#endif
