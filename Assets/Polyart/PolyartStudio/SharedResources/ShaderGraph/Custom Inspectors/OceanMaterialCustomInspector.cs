#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Polyart
{
    public class OceanMaterialCustomInspector : ShaderGUI
    {
        private bool showWaterColor = false;
        private bool showNormals = false;
        private bool showCaustics = false;
        private bool showFoam = false;
        private bool showEdgeFoam = false;

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

            GUILayout.Label("Ocean Material", largeLabelStyle);

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
                "This is the base Ocean Material. Here you will be able to customize the look of the water. Everything that has to do with the Waves can be found on the 'Ocean Tool'. This way you can have the same Ocean Material for many Oceans with different Wave Settings.",
                smallLabelStyle
            );
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showWaterColor = CenteredFoldout(showWaterColor, "Water Color", medLabelStyle);

            GUILayout.Space(5);
            if (showWaterColor)
            {
                GUILayout.Label(
                    "These settings control the Color of the Water. Keep in mind that other factors like the reflections can affect the final result.",
                    smallLabelStyle
                );

                GUILayout.Space(10);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                materialEditor.ShaderProperty(FindProperty("_WavePeakColor", properties), "Waves Peak Color");
                materialEditor.ShaderProperty(FindProperty("_WaterDeepColor", properties), "Deep Water Color");
                GUILayout.Space(5);
                materialEditor.ShaderProperty(FindProperty("_EdgeOpacityDistance", properties), "Shallow Water Distance");
                materialEditor.ShaderProperty(FindProperty("_WaterDepthOpacityGradience", properties), "Water Depth Opacity Gradience Length");
                GUILayout.Space(5);
                materialEditor.ShaderProperty(FindProperty("_BackgroundIntensityMultiplier", properties), "Underwater Brightness");
                GUILayout.Space(5);
                materialEditor.ShaderProperty(FindProperty("_ReflectionAmount", properties), "Reflection Probe Intensity");
                GUILayout.Space(5);
                materialEditor.ShaderProperty(FindProperty("_Smoothness", properties), "Smoothness");
                materialEditor.ShaderProperty(FindProperty("_Refraction", properties), "Refraction");
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);



            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showNormals = CenteredFoldout(showNormals, "Normals", medLabelStyle);

            GUILayout.Space(5);

            if (showNormals)
            {
                GUILayout.Label(
                    "This is a detail Normal Map blended with the Wave Normals",
                    smallLabelStyle
                );
                GUILayout.Space(10);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_WaterNormalMap", properties), "Normal Map");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NormalTiling", properties), "Normals Scale");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_NormalsSpeed", properties), "Normals Speed");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_TextureNormalsStrength", properties), "Normals Strength");
                GUILayout.Space(5);

                EditorGUILayout.EndVertical();

                GUILayout.Space(15);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showCaustics = CenteredFoldout(showCaustics, "Caustics", medLabelStyle);

            GUILayout.Space(5);

            if (showCaustics)
            {
                GUILayout.Label(
                    "Simple Caustics Settings.",
                    smallLabelStyle
                );
                GUILayout.Space(10);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CausticsScale", properties), "Caustics Scale");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CausticsDistance", properties), "Caustics Distance");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CausticsIntensity", properties), "Caustics Intensity");
                materialEditor.ShaderProperty(ShaderGUI.FindProperty("_CausticsNormalsDistortion", properties), "Caustics Distortion");
                GUILayout.Space(5);

                EditorGUILayout.EndVertical();
                GUILayout.Space(15);
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showEdgeFoam = CenteredFoldout(showEdgeFoam, "Edge Foam", medLabelStyle);

            GUILayout.Space(5);

            if (showEdgeFoam)
            {
                GUILayout.Label(
                    "Simple Foam around objects.",
                    smallLabelStyle
                );

                GUILayout.Space(15);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);

                materialEditor.ShaderProperty(FindProperty("_EdgeFoamDistance", properties), "Distance");
                materialEditor.ShaderProperty(FindProperty("_EdgeFoamSpeed", properties), "Speed");
                materialEditor.ShaderProperty(FindProperty("_EdgeFoamWavesNumber", properties), "Num Wave Lines");
                materialEditor.ShaderProperty(FindProperty("_EdgeFoamOpacity", properties), "Opacity");


                GUILayout.Space(5);
                EditorGUILayout.EndVertical();

                GUILayout.Space(15);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            showFoam = CenteredFoldout(showFoam, "Wave Foam", medLabelStyle);

            GUILayout.Space(5);

            if (showFoam)
            {
                GUILayout.Label(
                    "Simple Foam that is revealed mostly on the Wave Peaks.",
                    smallLabelStyle
                );

                GUILayout.Space(15);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Space(5);
                materialEditor.ShaderProperty(FindProperty("_FoamTexture", properties), "Foam Texture");
                materialEditor.ShaderProperty(FindProperty("_FoamTiling", properties), "Foam Tiling");
                GUILayout.Space(5);

                GUILayout.Label("Reveal And Intensity Values", smallBoldLabelStyle);
                GUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(
                    "The 'Pow' Values control the Intensity of the Foam Texture. Values Closer to 0 mean a fuller Foam while higher Values mean more faint Foam. These are blended by the 'Height' Values. A Height of 0 means flat Water Level and a value of 1 is the tallest Wave Peak.",
                    smallLabelStyle
                );
                EditorGUILayout.EndVertical();
                GUILayout.Space(5);

                materialEditor.ShaderProperty(FindProperty("_TrailFoamPowMin", properties), "Flat Water Level Pow");
                materialEditor.ShaderProperty(FindProperty("_TrailFoamPowMax", properties), "Wave Peak Level Pow");
                materialEditor.ShaderProperty(FindProperty("_TrailFoamAmountHeight", properties), "Start Blend Height");
                materialEditor.ShaderProperty(FindProperty("_TrailFoamGradience", properties), "Blend Gradience Length");


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