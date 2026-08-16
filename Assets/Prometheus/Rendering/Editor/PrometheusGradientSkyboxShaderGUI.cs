using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Rendering.Editor
{
    /// <summary>
    /// Presents material-backed gradient keys as one HDR Gradient Bar for the Prometheus skybox shader.
    /// </summary>
    public sealed class PrometheusGradientSkyboxShaderGUI : ShaderGUI
    {
        private const int MaximumGradientKeyCount = 8;
        private static readonly GUIContent GradientLabel = new GUIContent("天空渐变", "左侧对应天底，中心对应地平线，右侧对应天顶。支持 HDR 颜色键、透明度键和 Unity Gradient 插值模式。");
        private static readonly GUIContent ExposureLabel = new GUIContent("曝光", "控制渐变天空与太阳最终输出的整体亮度。");
        private static readonly GUIContent SunGradientAxisInfluenceLabel = new GUIContent("太阳旋转影响渐变轴", "0 表示渐变固定沿世界空间上下方向，1 表示渐变轴完全跟随 Sun Source 旋转，中间值用于柔和偏移。");
        private static readonly GUIContent SunColorLabel = new GUIContent("太阳颜色", "与 Sun Source 灯光颜色相乘的 HDR 太阳盘颜色。");
        private static readonly GUIContent SunIntensityLabel = new GUIContent("太阳强度", "控制太阳盘与太阳光晕的额外亮度。");
        private static readonly GUIContent SunAngularDiameterLabel = new GUIContent("太阳角直径", "太阳盘在天空中的角直径，真实太阳约为 0.53 度。");
        private static readonly GUIContent SunHaloSizeLabel = new GUIContent("太阳光晕角度", "控制太阳周围渐变光晕覆盖的角度。");
        private static readonly GUIContent SunHaloIntensityLabel = new GUIContent("太阳光晕强度", "控制太阳周围柔和光晕的亮度，不改变太阳盘本身亮度。");

        /// <summary>
        /// Draws the HDR Gradient Bar and the exposed sky and sun controls for every selected material.
        /// </summary>
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material primaryMaterial = (Material)materialEditor.target;
            EditorGUILayout.LabelField("天空颜色", EditorStyles.boldLabel);
            Gradient currentGradient = ReadGradient(primaryMaterial);
            EditorGUI.BeginChangeCheck();
            Gradient editedGradient = EditorGUILayout.GradientField(GradientLabel, currentGradient, true);
            if (EditorGUI.EndChangeCheck())
            {
                materialEditor.RegisterPropertyChangeUndo("修改天空渐变");
                foreach (Object target in materialEditor.targets)
                {
                    Material material = (Material)target;
                    WriteGradient(material, editedGradient);
                    EditorUtility.SetDirty(material);
                }

                RefreshEnvironmentIfActiveSkybox(materialEditor.targets);
            }

            EditorGUI.BeginChangeCheck();
            materialEditor.ShaderProperty(FindProperty("_Exposure", properties), ExposureLabel);
            materialEditor.ShaderProperty(FindProperty("_SunGradientAxisInfluence", properties), SunGradientAxisInfluenceLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("太阳", EditorStyles.boldLabel);
            materialEditor.ShaderProperty(FindProperty("_SunColor", properties), SunColorLabel);
            materialEditor.ShaderProperty(FindProperty("_SunIntensity", properties), SunIntensityLabel);
            materialEditor.ShaderProperty(FindProperty("_SunAngularDiameter", properties), SunAngularDiameterLabel);
            materialEditor.ShaderProperty(FindProperty("_SunHaloSize", properties), SunHaloSizeLabel);
            materialEditor.ShaderProperty(FindProperty("_SunHaloIntensity", properties), SunHaloIntensityLabel);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshEnvironmentIfActiveSkybox(materialEditor.targets);
            }
        }

        /// <summary>
        /// Reconstructs a Unity Gradient from the material properties serialized alongside the shader.
        /// </summary>
        private static Gradient ReadGradient(Material material)
        {
            int colorKeyCount = Mathf.RoundToInt(material.GetFloat("_GradientColorCount"));
            GradientColorKey[] colorKeys = new GradientColorKey[colorKeyCount];
            for (int index = 0; index < colorKeyCount; index++)
            {
                colorKeys[index] = new GradientColorKey(material.GetColor($"_GradientColor{index}"), material.GetFloat($"_GradientColorTime{index}"));
            }

            int alphaKeyCount = Mathf.RoundToInt(material.GetFloat("_GradientAlphaCount"));
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[alphaKeyCount];
            for (int index = 0; index < alphaKeyCount; index++)
            {
                alphaKeys[index] = new GradientAlphaKey(material.GetFloat($"_GradientAlpha{index}"), material.GetFloat($"_GradientAlphaTime{index}"));
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            gradient.mode = (GradientMode)Mathf.RoundToInt(material.GetFloat("_GradientMode"));
            return gradient;
        }

        /// <summary>
        /// Stores every Gradient Bar key directly in the material so no generated texture or sidecar asset is required.
        /// </summary>
        private static void WriteGradient(Material material, Gradient gradient)
        {
            GradientColorKey[] colorKeys = gradient.colorKeys;
            material.SetFloat("_GradientColorCount", colorKeys.Length);
            for (int index = 0; index < MaximumGradientKeyCount; index++)
            {
                GradientColorKey key = colorKeys[Mathf.Min(index, colorKeys.Length - 1)];
                material.SetColor($"_GradientColor{index}", key.color);
                material.SetFloat($"_GradientColorTime{index}", key.time);
            }

            GradientAlphaKey[] alphaKeys = gradient.alphaKeys;
            material.SetFloat("_GradientAlphaCount", alphaKeys.Length);
            for (int index = 0; index < MaximumGradientKeyCount; index++)
            {
                GradientAlphaKey key = alphaKeys[Mathf.Min(index, alphaKeys.Length - 1)];
                material.SetFloat($"_GradientAlpha{index}", key.alpha);
                material.SetFloat($"_GradientAlphaTime{index}", key.time);
            }

            material.SetFloat("_GradientMode", (int)gradient.mode);
        }

        /// <summary>
        /// Rebuilds Unity's ambient probe when the edited material currently owns scene environment lighting.
        /// </summary>
        private static void RefreshEnvironmentIfActiveSkybox(Object[] editedTargets)
        {
            foreach (Object target in editedTargets)
            {
                if (target != RenderSettings.skybox)
                {
                    continue;
                }

                DynamicGI.UpdateEnvironment();
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                return;
            }
        }
    }
}
