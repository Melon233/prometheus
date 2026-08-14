using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Xuan.Prometheus.Rendering.Tests
{
    /// <summary>
    /// Guards the project-wide URP assignment and every asset migration that must remain true when rendering content is added later.
    /// </summary>
    public sealed class UrpProjectConfigurationTests
    {
        /// <summary>
        /// Built-in shader names that must never return after the project has been migrated to URP.
        /// </summary>
        private static readonly HashSet<string> ForbiddenMaterialShaders = new HashSet<string>
        {
            "Standard",
            "Standard (Specular setup)",
            "Legacy Shaders/Particles/Additive",
            "Legacy Shaders/Particles/Additive (Soft)",
            "Legacy Shaders/Particles/Alpha Blended",
            "Mobile/Particles/Additive",
            "Spine/Skeleton",
            "Spine/Skeleton Lit",
            "Spine/Sprite/Unlit",
            "Hovl/Particles/Blend_TwoSides",
            "Hovl/Particles/BlendDistort",
            "Hovl/Particles/Distortion",
            "Hovl/Particles/Ice"
        };

        /// <summary>
        /// Verifies that the Graphics default is a URP asset so new or platform-specific quality levels cannot silently fall back to Built-in rendering.
        /// </summary>
        [Test]
        public void GraphicsDefaultRenderPipelineUsesUrp()
        {
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.TypeOf<UniversalRenderPipelineAsset>());
        }

        /// <summary>
        /// Reads every quality level at runtime and verifies its own URP asset, renderer reference, depth texture, and opaque texture configuration.
        /// </summary>
        [Test]
        public void EveryQualityLevelUsesACompleteUrpConfiguration()
        {
            int originalQualityLevel = QualitySettings.GetQualityLevel();
            try
            {
                for (int qualityIndex = 0; qualityIndex < QualitySettings.names.Length; qualityIndex++)
                {
                    QualitySettings.SetQualityLevel(qualityIndex, false);
                    UniversalRenderPipelineAsset pipelineAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
                    Assert.That(pipelineAsset, Is.Not.Null, $"Quality level '{QualitySettings.names[qualityIndex]}' must reference a UniversalRenderPipelineAsset.");
                    Assert.That(pipelineAsset.supportsCameraDepthTexture, Is.True, $"Quality level '{QualitySettings.names[qualityIndex]}' must provide the depth texture required by soft particles.");
                    Assert.That(pipelineAsset.supportsCameraOpaqueTexture, Is.True, $"Quality level '{QualitySettings.names[qualityIndex]}' must provide the opaque texture required by refraction effects.");
                    SerializedProperty rendererList = new SerializedObject(pipelineAsset).FindProperty("m_RendererDataList");
                    Assert.That(rendererList.arraySize, Is.GreaterThan(0), $"Quality level '{QualitySettings.names[qualityIndex]}' must contain a renderer data reference.");
                    Assert.That(rendererList.GetArrayElementAtIndex(0).objectReferenceValue, Is.Not.Null, $"Quality level '{QualitySettings.names[qualityIndex]}' must contain a valid default renderer data asset.");
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(originalQualityLevel, false);
            }
        }

        /// <summary>
        /// Dynamically scans project materials so newly imported assets cannot reintroduce the Built-in shaders already removed by this migration.
        /// </summary>
        [Test]
        public void ProjectMaterialsDoNotUseMigratedBuiltInShaders()
        {
            foreach (string materialGuid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string materialPath = AssetDatabase.GUIDToAssetPath(materialGuid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                Assert.That(material, Is.Not.Null, $"Material asset '{materialPath}' must load successfully.");
                Assert.That(material.shader, Is.Not.Null, $"Material asset '{materialPath}' must reference a shader.");
                Assert.That(ForbiddenMaterialShaders.Contains(material.shader.name), Is.False, $"Material asset '{materialPath}' still uses migrated Built-in shader '{material.shader.name}'.");
            }
        }

        /// <summary>
        /// Dynamically verifies every shader currently referenced by a project material so unsupported third-party or newly imported shaders fail the same regression suite.
        /// </summary>
        [Test]
        public void EveryProjectMaterialShaderCompilesForTheActivePipeline()
        {
            IEnumerable<Shader> materialShaders = AssetDatabase.FindAssets("t:Material", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).Select(material => material.shader).Distinct();
            foreach (Shader shader in materialShaders)
            {
                Assert.That(shader, Is.Not.Null, "Every project material must reference a shader.");
                Assert.That(shader.isSupported, Is.True, $"Shader '{shader.name}' must support the active editor graphics API and URP configuration.");
                string[] compilerErrors = ShaderUtil.GetShaderMessages(shader).Where(message => message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Select(message => $"{message.message} at {message.file}:{message.line}").ToArray();
                Assert.That(compilerErrors, Is.Empty, $"Shader '{shader.name}' contains compiler errors:\n{string.Join("\n", compilerErrors)}");
            }
        }

        /// <summary>
        /// Reads each migrated particle material's serialized legacy tint and compares it with the URP base color instead of relying on fixed color constants.
        /// </summary>
        [Test]
        public void MigratedParticleMaterialsPreserveTheirSerializedTint()
        {
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            foreach (string materialGuid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(materialGuid));
                if (material.shader != particleShader || !TryReadSavedColor(material, "_TintColor", out Color savedTint))
                {
                    continue;
                }

                Color baseColor = material.GetColor("_BaseColor");
                Assert.That(Vector4.Distance(baseColor, savedTint), Is.LessThan(0.0001f), $"Material '{AssetDatabase.GetAssetPath(material)}' must copy its own serialized _TintColor into _BaseColor.");
            }
        }

        /// <summary>
        /// Verifies that each custom replacement shader exists and is supported by the active editor graphics API.
        /// </summary>
        [TestCase("Prometheus/URP/Hovl/Blend_TwoSides")]
        [TestCase("Prometheus/URP/Hovl/BlendDistort")]
        [TestCase("Prometheus/URP/Hovl/Distortion")]
        [TestCase("Prometheus/URP/Hovl/Ice")]
        [TestCase("Universal Render Pipeline/Spine/Skeleton")]
        [TestCase("Universal Render Pipeline/Spine/Skeleton Lit")]
        [TestCase("Universal Render Pipeline/Spine/Sprite")]
        [TestCase("Universal Render Pipeline/2D/Spine/Skeleton Lit")]
        [TestCase("Universal Render Pipeline/2D/Spine/Sprite")]
        public void RequiredUrpShadersAreSupported(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, $"Shader '{shaderName}' must be discoverable after import.");
            Assert.That(shader.isSupported, Is.True, $"Shader '{shaderName}' must compile for the active editor graphics API.");
        }

        /// <summary>
        /// Reads a named color from Unity's serialized material property map even when the newly assigned shader no longer exposes the legacy property.
        /// </summary>
        private static bool TryReadSavedColor(Material material, string propertyName, out Color color)
        {
            SerializedProperty colors = new SerializedObject(material).FindProperty("m_SavedProperties.m_Colors");
            for (int colorIndex = 0; colorIndex < colors.arraySize; colorIndex++)
            {
                SerializedProperty entry = colors.GetArrayElementAtIndex(colorIndex);
                if (entry.FindPropertyRelative("first").stringValue != propertyName)
                {
                    continue;
                }

                color = entry.FindPropertyRelative("second").colorValue;
                return true;
            }

            color = default;
            return false;
        }
    }
}
