#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

namespace Polyart
{
    public class RGB_RockCustomInspector : ShaderGUI
    {
        private bool showBakedTextures = false;
        private bool showEdgeParams = false;
        private bool showTilingLayers = false;
        private bool showCoveragelayer = false;

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        // Utility to sync keyword with float property
        void SetKeyword(Material mat, string keyword, bool state)
        {
            if (state)
                mat.EnableKeyword(keyword);
            else
                mat.DisableKeyword(keyword);
        }

        private bool CenteredFoldout(bool isExpanded, string label, GUIStyle labelStyle, float height = 24f)
        {
            Rect foldoutRect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            Rect arrowRect = new Rect(foldoutRect.x, foldoutRect.y, 20, foldoutRect.height);

            // Draw arrow (no label)
            isExpanded = EditorGUI.Foldout(arrowRect, isExpanded, GUIContent.none, true);

            // Handle full-width click
            Event e = Event.current;
            if (e.type == EventType.MouseDown && foldoutRect.Contains(e.mousePosition))
            {
                if (!arrowRect.Contains(e.mousePosition))
                {
                    isExpanded = !isExpanded;
                    e.Use();
                }
            }

            // Draw centered label
            GUI.Label(foldoutRect, label, labelStyle);

            return isExpanded;
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            // Get the actual material being edited
            Material material = materialEditor.target as Material;

            GUILayout.Space(15);

            GUIStyle largeLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 36,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUILayout.Label("Rock Material", largeLabelStyle);

            EditorGUILayout.Space(20, true);

            GUIStyle medLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Normal,
                normal = { textColor = UnityEngine.Color.white },
                alignment = TextAnchor.MiddleCenter
            };

            GUIStyle smallLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                wordWrap = true, // Enable text wrapping
                normal = { textColor = new UnityEngine.Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.UpperLeft // Better alignment for wrapped text
            };

            GUIStyle smallBoldLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true, // Enable text wrapping
                normal = { textColor = new UnityEngine.Color(0.95f, 0.95f, 0.95f) },
                alignment = TextAnchor.UpperLeft // Better alignment for wrapped text
            };

            // Custom styles for enabled and disabled buttons
            GUIStyle enabledStyle = new GUIStyle(GUI.skin.button);
            enabledStyle.normal.textColor = Color.white;
            enabledStyle.fontSize = 12;
            enabledStyle.normal.background = MakeTex(2, 2, new Color(0f, 0.4f, 0f));

            GUIStyle disabledStyle = new GUIStyle(GUI.skin.button);
            disabledStyle.normal.textColor = Color.white;
            disabledStyle.normal.background = MakeTex(2, 2, new Color(0.4f, 0f, 0f));

            // Create a box or area for the text to constrain width
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(
                "This Shader uses an advanced masking system to create Layered Effect. You can blend up to 3 Layers and a Coverage Layer on top of that.",
                smallLabelStyle
            );
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showBakedTextures = CenteredFoldout(showBakedTextures, "Baked Textures", medLabelStyle);

            GUILayout.Space(5);
            if (showBakedTextures)
            {
                GUILayout.Label(
                    "These Textures are unique to each Mesh and control how the Tiling Layers will blend together.",
                    smallLabelStyle
                );

                GUILayout.Space(10);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                materialEditor.ShaderProperty(FindProperty("_Normal_Strength", properties), "Normal Strength");
                materialEditor.ShaderProperty(FindProperty("_Normal_Map", properties), "Normal Map");
                GUILayout.Space(5);
                materialEditor.ShaderProperty(FindProperty("_Edge_Map", properties), "Edge Map");
                materialEditor.ShaderProperty(FindProperty("_RGB_Map", properties), "RGB Map");
                GUILayout.Space(5);
                materialEditor.ShaderProperty(FindProperty("_Scale_UVs_with_Object", properties), "Scale Tiling UVs with Object");
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showEdgeParams = CenteredFoldout(showEdgeParams, "Edge Params", medLabelStyle);

            GUILayout.Space(5);

            if (showEdgeParams)
            {
                GUILayout.Label(
                    "These parameters apply some effects on top of the Blended Layers and Below the Coverage Layer.",
                    smallLabelStyle
                );
                GUILayout.Space(10);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);

                EditorGUILayout.LabelField("Stylization Color", smallBoldLabelStyle);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Stylization_Mask_Contrast", properties), "Contrast");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Stylization_Color", properties), "Color");
                GUILayout.Space(5);

                EditorGUILayout.EndVertical();
                GUILayout.Space(15);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Ambient Occlusion", smallBoldLabelStyle);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_AO_Mask_Contrast", properties), "Contrast");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_AO_Color", properties), "Color");
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
                GUILayout.Space(15);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Dirt Mask", smallBoldLabelStyle);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Dirt_Mask_Contrast", properties), "Contrast");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Dirt_Color", properties), "Color");
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
                GUILayout.Space(15);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Edgewear", smallBoldLabelStyle);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Edgewear_Mask_Contrast", properties), "Contrast");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_Edgewear_Color", properties), "Color");
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
                GUILayout.Space(15);
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);
            
            showTilingLayers = CenteredFoldout(showTilingLayers, "Tiling Layers", medLabelStyle);

            GUILayout.Space(5);

            if (showTilingLayers)
            {
                GUILayout.Label(
                    "You can click on the Layer Buttons to Enable/Disable them. \n* In order for Layer 2 to be available you must have Layer 1 Enabled.\n* In order for Layer 3 to be available you must have Layer 2 Enabled.",
                    smallLabelStyle
                );
                GUILayout.Space(10);

                // Get the float property that backs the keyword
                MaterialProperty useLayer1Prop = FindProperty("_USE_LAYER_1", properties);

                // Convert float to bool for UI
                bool layer1 = useLayer1Prop.floatValue == 1;

                // Draw the toggle button
                EditorGUI.BeginChangeCheck();
                if (GUILayout.Button(layer1 ? "Layer 1 Enabled" : "Layer 1 Disabled", layer1 ? enabledStyle : disabledStyle))
                {
                    layer1 = !layer1; // toggle value
                }

                if (EditorGUI.EndChangeCheck())
                {
                    // Register undo and apply new value
                    Undo.RecordObject(material, "Toggle _USE_LAYER_1");

                    useLayer1Prop.floatValue = layer1 ? 1 : 0;

                    // Update the keyword based on property
                    SetKeyword(material, "_USE_LAYER_1", layer1);

                    // Mark material dirty and refresh UI
                    EditorUtility.SetDirty(material);
                    materialEditor.PropertiesChanged();
                }

                // Ensure keyword stays in sync on domain reload / recompile
                SetKeyword(material, "_USE_LAYER_1", useLayer1Prop.floatValue == 1);

                // Show Layer 1 properties if keyword is enabled
                if (layer1)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Space(5);

                    materialEditor.ShaderProperty(FindProperty("_Layer_1_Tiling", properties), "Tiling");
                    materialEditor.ShaderProperty(FindProperty("_Layer_1_Color_Map", properties), "Color Map");
                    materialEditor.ShaderProperty(FindProperty("_Layer_1_Hue", properties), "Hue");
                    materialEditor.ShaderProperty(FindProperty("_Layer_1_Brightness", properties), "Brightness");
                    materialEditor.ShaderProperty(FindProperty("_Layer_1_Smoothness_Strength", properties), "Smoothness Strength");
                    materialEditor.ShaderProperty(FindProperty("_Layer_1_Normal_Map", properties), "Normal Map");
                    materialEditor.ShaderProperty(FindProperty("_Layer_1_Normal_Strength", properties), "Normal Strength");

                    GUILayout.Space(5);
                    EditorGUILayout.EndVertical();
                }

                if (layer1)
                {
                    GUILayout.Space(10);

                    // Get the float property that backs the keyword
                    MaterialProperty useLayer2Prop = FindProperty("_USE_LAYER_2", properties);

                    // Convert float to bool for UI
                    bool layer2 = useLayer2Prop.floatValue == 1;

                    // Draw the toggle button
                    EditorGUI.BeginChangeCheck();
                    if (GUILayout.Button(layer2 ? "Layer 2 Enabled" : "Layer 2 Disabled", layer2 ? enabledStyle : disabledStyle))
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

                        materialEditor.ShaderProperty(FindProperty("_Layer_2_Tiling", properties), "Tiling");
                        materialEditor.ShaderProperty(FindProperty("_Layer_2_Color_Map", properties), "Color Map");
                        materialEditor.ShaderProperty(FindProperty("_Layer_2_Hue", properties), "Hue");
                        materialEditor.ShaderProperty(FindProperty("_Layer_2_Brightness", properties), "Brightness");
                        materialEditor.ShaderProperty(FindProperty("_Layer_2_Smoothness_Strength", properties), "Smoothness Strength");
                        materialEditor.ShaderProperty(FindProperty("_Layer_2_Normal_Map", properties), "Normal Map");
                        materialEditor.ShaderProperty(FindProperty("_Layer_2_Normal_Strength", properties), "Normal Strength");

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
                        if (GUILayout.Button(layer3 ? "Layer 3 Enabled" : "Layer 3 Disabled", layer3 ? enabledStyle : disabledStyle))
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

                            materialEditor.ShaderProperty(FindProperty("_Layer_3_Tiling", properties), "Tiling");
                            materialEditor.ShaderProperty(FindProperty("_Layer_3_Color_Map", properties), "Color Map");
                            materialEditor.ShaderProperty(FindProperty("_Layer_3_Hue", properties), "Hue");
                            materialEditor.ShaderProperty(FindProperty("_Layer_3_Brightness", properties), "Brightness");
                            materialEditor.ShaderProperty(FindProperty("_Layer_3_Smoothness_Strength", properties), "Smoothness Strength");
                            materialEditor.ShaderProperty(FindProperty("_Layer_3_Normal_Map", properties), "Normal Map");
                            materialEditor.ShaderProperty(FindProperty("_Layer_3_Normal_Strength", properties), "Normal Strength");

                            GUILayout.Space(5);
                            EditorGUILayout.EndVertical();
                        }
                    }
                }

                GUILayout.Space(15);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showCoveragelayer = CenteredFoldout(showCoveragelayer, "Coverage Layer", medLabelStyle);

            GUILayout.Space(5);

            if (showCoveragelayer)
            {
                GUILayout.Label(
                    "This Layer blends on top of every other using the Normals of the Rock.",
                    smallLabelStyle
                );

                GUILayout.Space(15);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);

                GUILayout.Label("Mask", smallBoldLabelStyle );
                materialEditor.ShaderProperty(FindProperty("_Coverage_Normal_Mask_Slope", properties), "Slope Mask Angle");
                materialEditor.ShaderProperty(FindProperty("_Coverage_Normal_Mask_Gradience", properties), "Slope Mask Gradience");
                materialEditor.ShaderProperty(FindProperty("_Coverage_Height_Mask_Pivot_Offset", properties), "Pivot Height Mask Offset");
                materialEditor.ShaderProperty(FindProperty("_Coverage_Height_Mask_Length", properties), "Pivot Height Mask Gradience");

                GUILayout.Space(5);
                EditorGUILayout.EndVertical();

                GUILayout.Space(15);

                // Get the float property that backs the keyword
                MaterialProperty useCoverageLayer = FindProperty("_USE_COVERAGE_LAYER", properties);

                // Convert float to bool for UI
                bool coverage = useCoverageLayer.floatValue == 1;

                // Draw the toggle button
                EditorGUI.BeginChangeCheck();
                if (GUILayout.Button(coverage ? "Coverage Enabled" : "Coverage Disabled", coverage ? enabledStyle : disabledStyle))
                {
                    coverage = !coverage; // toggle value
                }

                if (EditorGUI.EndChangeCheck())
                {
                    // Register undo and apply new value
                    Undo.RecordObject(material, "Toggle _USE_COVERAGE_LAYER");

                    useCoverageLayer.floatValue = coverage ? 1 : 0;

                    // Update the keyword based on property
                    SetKeyword(material, "_USE_COVERAGE_LAYER", coverage);

                    // Mark material dirty and refresh UI
                    EditorUtility.SetDirty(material);
                    materialEditor.PropertiesChanged();
                    GUILayout.Space(15);
                }

                // Ensure keyword stays in sync on domain reload / recompile
                SetKeyword(material, "_USE_COVERAGE_LAYER", useCoverageLayer.floatValue == 1);

                // Show Layer 1 properties if keyword is enabled
                if (coverage)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Space(5);

                    materialEditor.ShaderProperty(FindProperty("_Coverage_Tiling", properties), "Tiling");
                    materialEditor.ShaderProperty(FindProperty("_Coverage_Color_Map", properties), "Color Map");
                    materialEditor.ShaderProperty(FindProperty("_Coverage_Hue", properties), "Hue");
                    materialEditor.ShaderProperty(FindProperty("_Coverage_Brightness", properties), "Brightness");
                    materialEditor.ShaderProperty(FindProperty("_Coverage_Smoothness_Strength", properties), "Smoothness Strength");
                    materialEditor.ShaderProperty(FindProperty("_Coverage_Normal_Map", properties), "Normal Map");
                    materialEditor.ShaderProperty(FindProperty("_Coverage_Normal_Strength", properties), "Normal Strength");

                    GUILayout.Space(15);
                    EditorGUILayout.EndVertical();
                }
                GUILayout.Space(15);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

        }
    }

}

#endif