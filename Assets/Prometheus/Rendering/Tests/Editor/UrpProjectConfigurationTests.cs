using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Xuan.Prometheus.Rendering;

namespace Xuan.Prometheus.Rendering.Tests
{
    /// <summary>
    /// Guards the project-wide URP assignment and every asset migration that must remain true when rendering content is added later.
    /// </summary>
    public sealed class UrpProjectConfigurationTests
    {
        /// <summary>
        /// Character layers that must participate in Shiny's transparent depth prepass so reflected Spine meshes survive either horizontal winding.
        /// </summary>
        private static readonly string[] ShinySsrTransparentDepthLayerNames =
        {
            "Character",
            "Enemy"
        };

        /// <summary>
        /// Fully qualified third-party type names keep the project-owned test assembly independent from plugin assemblies while still validating imported rendering contracts.
        /// </summary>
        private const string SsgiFeatureTypeFullName = "MF.SSGI.SSGIFeature";
        private const string ShinySsrFeatureTypeFullName = "ShinySSRR.ShinySSRR";

        /// <summary>
        /// Verifies that the Graphics default is Mobile Forward Mid so editor rendering matches the Android Mid configuration.
        /// </summary>
        [Test]
        public void GraphicsDefaultRenderPipelineUsesMobileForwardMid()
        {
            PrometheusRenderingSettings renderingSettings = LoadRenderingSettings();
            UniversalRenderPipelineAsset expectedPipelineAsset = renderingSettings.GetPipelineAsset(PrometheusRenderPlatform.Mobile, PrometheusRenderPath.Forward, PrometheusRenderQualityLevel.Mid);
            Assert.That(expectedPipelineAsset, Is.Not.Null, "Prometheus rendering settings must reference the Mobile Forward Mid pipeline.");
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.SameAs(expectedPipelineAsset), "GraphicsSettings must use Mobile Forward Mid for editor rendering.");
        }

        /// <summary>
        /// Verifies that Unity exposes only Low and Mid compatibility entries while runtime platform and renderer-path selection remain project-owned.
        /// </summary>
        [Test]
        public void UnityQualityLevelsExposeLowAndMidCompatibilityPipelines()
        {
            PrometheusRenderingSettings renderingSettings = LoadRenderingSettings();
            int originalQualityLevel = QualitySettings.GetQualityLevel();
            try
            {
                Assert.That(QualitySettings.names, Is.EqualTo(new[] { "Low", "Mid" }), "Unity QualitySettings must expose exactly the two user-facing project levels.");
                for (int qualityIndex = 0; qualityIndex < QualitySettings.names.Length; qualityIndex++)
                {
                    QualitySettings.SetQualityLevel(qualityIndex, false);
                    UniversalRenderPipelineAsset pipelineAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
                    Assert.That(pipelineAsset, Is.Not.Null, $"Quality level '{QualitySettings.names[qualityIndex]}' must reference a UniversalRenderPipelineAsset.");
                    UniversalRenderPipelineAsset expectedPipelineAsset = renderingSettings.GetPipelineAsset(PrometheusRenderPlatform.Mobile, PrometheusRenderPath.Forward, qualityIndex == 0 ? PrometheusRenderQualityLevel.Low : PrometheusRenderQualityLevel.Mid);
                    Assert.That(pipelineAsset, Is.SameAs(expectedPipelineAsset), $"Quality level '{QualitySettings.names[qualityIndex]}' must reference its configured Mobile Forward editor pipeline.");
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
        /// Dynamically reads each SSGI renderer's own pass events and rendering mode so Shiny SSR remains deferred-compatible and executes after the current SSGI composition without fixed expected values.
        /// </summary>
        [Test]
        public void EverySsgiRendererOwnsOneCompatibleShinySsrFeature()
        {
            PrometheusRenderingSettings renderingSettings = LoadRenderingSettings();
            SerializedProperty rendererList = new SerializedObject(renderingSettings.PipelineAsset).FindProperty("m_RendererDataList");
            UniversalRendererData[] rendererData = Enumerable.Range(0, rendererList.arraySize).Select(rendererIndex => rendererList.GetArrayElementAtIndex(rendererIndex).objectReferenceValue as UniversalRendererData).ToArray();
            UniversalRendererData[] ssgiRendererData = rendererData.Where(data => data != null && data.rendererFeatures.Any(IsSsgiFeature)).ToArray();
            Assert.That(ssgiRendererData, Is.Not.Empty, "The active pipeline must expose at least one renderer containing MF.SSGI before its optional Shiny SSR chain can be validated.");
            int requiredTransparentDepthLayerMask = ShinySsrTransparentDepthLayerNames.Aggregate(0, (layerMask, layerName) =>
            {
                int layer = LayerMask.NameToLayer(layerName);
                Assert.That(layer, Is.GreaterThanOrEqualTo(0), $"Required Shiny SSR transparent-depth layer '{layerName}' must exist before renderer configuration can be validated.");
                return layerMask | 1 << layer;
            });
            foreach (UniversalRendererData renderer in ssgiRendererData)
            {
                ScriptableRendererFeature[] ssgiFeatures = renderer.rendererFeatures.Where(IsSsgiFeature).ToArray();
                ScriptableRendererFeature[] shinySsrFeatures = renderer.rendererFeatures.Where(IsShinySsrFeature).ToArray();
                Assert.That(ssgiFeatures, Has.Length.EqualTo(1), $"Renderer '{renderer.name}' must own exactly one MF.SSGI composition feature.");
                Assert.That(shinySsrFeatures, Has.Length.EqualTo(1), $"Renderer '{renderer.name}' must own exactly one optional Shiny SSR feature.");
                ScriptableRendererFeature ssgiFeature = ssgiFeatures.Single();
                ScriptableRendererFeature shinySsrFeature = shinySsrFeatures.Single();
                bool rendererUsesDeferred = new SerializedObject(renderer).FindProperty("m_RenderingMode").intValue == (int)RenderingMode.Deferred;
                SerializedProperty ssgiRenderPassEventProperty = new SerializedObject(ssgiFeature).FindProperty("settings").FindPropertyRelative("RenderPassEvent");
                SerializedObject serializedShinySsrFeature = new SerializedObject(shinySsrFeature);
                SerializedProperty shinySsrUseDeferredProperty = serializedShinySsrFeature.FindProperty("useDeferred");
                SerializedProperty shinySsrRenderPassEventProperty = serializedShinySsrFeature.FindProperty("renderPassEvent");
                SerializedProperty enableTransparencyDepthPrepassProperty = serializedShinySsrFeature.FindProperty("enableTransparencyDepthPrepass");
                SerializedProperty transparencyDepthPrepassLayerMaskProperty = serializedShinySsrFeature.FindProperty("transparencyDepthPrepassLayerMask");
                Assert.That(shinySsrUseDeferredProperty.boolValue, Is.EqualTo(rendererUsesDeferred), $"Shiny SSR feature '{shinySsrFeature.name}' must derive its GBuffer path from renderer '{renderer.name}'.");
                Assert.That(shinySsrRenderPassEventProperty.intValue, Is.GreaterThan(ssgiRenderPassEventProperty.intValue), $"Shiny SSR feature '{shinySsrFeature.name}' must execute after the render-pass event read from SSGI feature '{ssgiFeature.name}' so reflections sample the composed indirect-light result.");
                Assert.That(shinySsrFeature.isActive, Is.True, $"Shiny SSR feature '{shinySsrFeature.name}' must keep its renderer capability active so the project-owned runtime master switch can selectively enqueue or skip it.");
                Assert.That(enableTransparencyDepthPrepassProperty.boolValue, Is.True, $"Shiny SSR feature '{shinySsrFeature.name}' must render transparent character depth before ray marching.");
                Assert.That(transparencyDepthPrepassLayerMaskProperty.intValue & requiredTransparentDepthLayerMask, Is.EqualTo(requiredTransparentDepthLayerMask), $"Shiny SSR feature '{shinySsrFeature.name}' must include every currently assigned character layer in its transparent depth prepass.");
            }
        }

        /// <summary>
        /// Invokes Shiny's actual shared transparent-depth material factory and verifies that mirrored Spine triangles are not removed by either rendering backend.
        /// </summary>
        [Test]
        public void ShinyTransparentDepthOverrideIsDoubleSidedForMirroredSpineMeshes()
        {
            ScriptableRendererFeature shinySsrFeature = AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Prometheus/Rendering/Pipeline" }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<UniversalRendererData>).Where(renderer => renderer != null).SelectMany(renderer => renderer.rendererFeatures).Single(IsShinySsrFeature);
            Type depthRenderPassType = shinySsrFeature.GetType().GetNestedType("DepthRenderPass", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.That(depthRenderPassType, Is.Not.Null, "The imported Shiny SSR feature must expose its transparent depth render pass.");
            System.Reflection.MethodInfo depthMaterialFactory = depthRenderPassType.GetMethod("GetOrCreateDepthOnlyMaterial", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(depthMaterialFactory, Is.Not.Null, "Shiny's Legacy and RenderGraph transparent-depth paths must share one material factory.");
            Material depthMaterial = depthMaterialFactory.Invoke(null, Array.Empty<object>()) as Material;
            Assert.That(depthMaterial, Is.Not.Null, "Shiny's transparent-depth material factory must resolve its required override shader.");
            Assert.That(depthMaterial.GetInt("_Cull"), Is.EqualTo((int)CullMode.Off), "Shiny's transparent-depth override must remain double-sided because Spine ScaleX facing changes reverse triangle winding.");
        }

        /// <summary>
        /// Toggles the project-owned SSR state away from and back to the plugin's current value, then verifies both exposed state holders instead of assuming a fixed startup value.
        /// </summary>
        [Test]
        public void ScreenSpaceReflectionSwitchMapsDirectlyToShinyMasterGate()
        {
            ScriptableRendererFeature shinySsrFeature = AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Prometheus/Rendering/Pipeline" }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<UniversalRendererData>).Where(renderer => renderer != null).SelectMany(renderer => renderer.rendererFeatures).Single(IsShinySsrFeature);
            System.Reflection.FieldInfo shinySsrMasterGate = shinySsrFeature.GetType().GetField("isEnabled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.That(shinySsrMasterGate, Is.Not.Null, $"Shiny SSR feature type '{shinySsrFeature.GetType().FullName}' must expose its documented public static master gate.");
            bool originalState = (bool)shinySsrMasterGate.GetValue(null);
            try
            {
                foreach (bool expectedState in new[] { !originalState, originalState })
                {
                    PrometheusRenderQualityController.ApplyScreenSpaceReflectionsState(expectedState);
                    Assert.That(PrometheusRenderQualityController.ScreenSpaceReflectionsEnabled, Is.EqualTo(expectedState), "The Prometheus runtime state must match the value supplied by the caller.");
                    Assert.That((bool)shinySsrMasterGate.GetValue(null), Is.EqualTo(expectedState), "The Shiny SSR renderer-pass gate must derive its value from the Prometheus runtime switch.");
                }
            }
            finally
            {
                PrometheusRenderQualityController.ApplyScreenSpaceReflectionsState(originalState);
            }
        }

        /// <summary>
        /// Verifies that every platform owns exactly one Low and one Mid runtime profile.
        /// </summary>
        [Test]
        public void RenderingSettingsDefineEveryProjectQualityLevelExactlyOnce()
        {
            PrometheusRenderingSettings renderingSettings = LoadRenderingSettings();
            PrometheusRenderQualityLevel[] expectedQualityLevels = Enum.GetValues(typeof(PrometheusRenderQualityLevel)).Cast<PrometheusRenderQualityLevel>().ToArray();
            PrometheusRenderPlatform[] expectedPlatforms = Enum.GetValues(typeof(PrometheusRenderPlatform)).Cast<PrometheusRenderPlatform>().ToArray();
            foreach (PrometheusRenderPlatform platform in expectedPlatforms)
            {
                PrometheusRenderQualityLevel[] configuredQualityLevels = renderingSettings.QualityProfiles.Where(profile => profile.Platform == platform).Select(profile => profile.QualityLevel).ToArray();
                Assert.That(configuredQualityLevels, Is.EquivalentTo(expectedQualityLevels), $"Platform '{platform}' must define exactly the Low and Mid quality profiles.");
                Assert.That(configuredQualityLevels.Distinct().Count(), Is.EqualTo(configuredQualityLevels.Length), $"Platform '{platform}' must not contain duplicate quality profiles.");
                Assert.That(renderingSettings.GetQualityProfile(platform, renderingSettings.GetStartupQualityLevel(platform)), Is.Not.Null, $"Platform '{platform}' startup quality must resolve through its own profile collection.");
            }
        }

        /// <summary>
        /// Applies every profile to a temporary pipeline copy and compares all results with values read from that profile instead of fixed test constants.
        /// </summary>
        [Test]
        public void EveryQualityProfileMapsItsOwnValuesToTheRuntimePipeline()
        {
            PrometheusRenderingSettings renderingSettings = LoadRenderingSettings();
            UniversalRenderPipelineAsset temporaryPipelineAsset = UnityEngine.Object.Instantiate(renderingSettings.PipelineAsset);
            try
            {
                foreach (PrometheusRenderQualityProfile profile in renderingSettings.QualityProfiles)
                {
                    PrometheusRenderQualityController.ApplyProfileToPipeline(renderingSettings, profile, temporaryPipelineAsset);
                    Assert.That(temporaryPipelineAsset.supportsCameraDepthTexture, Is.EqualTo(profile.PostProcessingEnabled && renderingSettings.RequiresCameraDepthTexture), $"Profile '{profile.Platform}/{profile.QualityLevel}' must apply its depth texture policy.");
                    Assert.That(temporaryPipelineAsset.supportsCameraOpaqueTexture, Is.EqualTo(profile.PostProcessingEnabled && renderingSettings.RequiresCameraOpaqueTexture), $"Profile '{profile.Platform}/{profile.QualityLevel}' must apply its opaque texture policy.");
                    Assert.That(temporaryPipelineAsset.useSRPBatcher, Is.EqualTo(renderingSettings.UseSrpBatcher), $"Profile '{profile.QualityLevel}' must apply the configured SRP batcher policy.");
                    Assert.That(temporaryPipelineAsset.supportsDynamicBatching, Is.EqualTo(renderingSettings.SupportsDynamicBatching), $"Profile '{profile.QualityLevel}' must apply the configured dynamic batching policy.");
                    Assert.That(temporaryPipelineAsset.useAdaptivePerformance, Is.EqualTo(renderingSettings.UseAdaptivePerformance), $"Profile '{profile.QualityLevel}' must apply the configured Adaptive Performance policy.");
                    Assert.That(temporaryPipelineAsset.renderScale, Is.EqualTo(profile.RenderScale).Within(0.0001f), $"Profile '{profile.QualityLevel}' must apply its own render scale.");
                    Assert.That(temporaryPipelineAsset.msaaSampleCount, Is.EqualTo(profile.MsaaSampleCount), $"Profile '{profile.QualityLevel}' must apply its own MSAA sample count.");
                    Assert.That(temporaryPipelineAsset.supportsHDR, Is.EqualTo(profile.SupportsHdr), $"Profile '{profile.QualityLevel}' must apply its own HDR policy.");
                    Assert.That(temporaryPipelineAsset.mainLightShadowmapResolution, Is.EqualTo(profile.MainLightShadowmapResolution), $"Profile '{profile.QualityLevel}' must apply its own main-light shadow atlas resolution.");
                    Assert.That(temporaryPipelineAsset.shadowDistance, Is.EqualTo(profile.ShadowDistance).Within(0.0001f), $"Profile '{profile.QualityLevel}' must apply its own shadow distance.");
                    Assert.That(temporaryPipelineAsset.shadowCascadeCount, Is.EqualTo(profile.ShadowCascadeCount), $"Profile '{profile.QualityLevel}' must apply its own shadow cascade count.");
                    Assert.That(temporaryPipelineAsset.maxAdditionalLightsCount, Is.EqualTo(profile.MaxAdditionalLightsCount), $"Profile '{profile.QualityLevel}' must apply its own additional-light count.");
                    Assert.That(temporaryPipelineAsset.additionalLightsShadowmapResolution, Is.EqualTo(profile.AdditionalLightsShadowmapResolution), $"Profile '{profile.QualityLevel}' must apply its own additional-light shadow atlas resolution.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporaryPipelineAsset);
            }
        }

        /// <summary>
        /// Verifies that invariant Lit and shadow capabilities stored in rendering settings match the single serialized pipeline asset.
        /// </summary>
        [Test]
        public void PipelineCapabilitiesMatchRenderingSettings()
        {
            PrometheusRenderingSettings renderingSettings = LoadRenderingSettings();
            UniversalRenderPipelineAsset pipelineAsset = renderingSettings.PipelineAsset;
            Assert.That(pipelineAsset.mainLightRenderingMode, Is.EqualTo(renderingSettings.MainLightRenderingMode));
            Assert.That(pipelineAsset.supportsMainLightShadows, Is.EqualTo(renderingSettings.SupportsMainLightShadows));
            Assert.That(pipelineAsset.additionalLightsRenderingMode, Is.EqualTo(renderingSettings.AdditionalLightsRenderingMode));
            Assert.That(pipelineAsset.supportsAdditionalLightShadows, Is.EqualTo(renderingSettings.SupportsAdditionalLightShadows));
            Assert.That(pipelineAsset.supportsSoftShadows, Is.EqualTo(renderingSettings.SupportsSoftShadows));
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
        [TestCase("Prometheus/Rendering/World/Grid Lit")]
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
        /// Dynamically enumerates the Grid Lit shader's compiled passes and requires paired Forward and GBuffer implementations so every renderer sees the same procedural surface data.
        /// </summary>
        [Test]
        public void GridShaderExposesForwardAndGBufferPassesForBothRenderers()
        {
            Shader gridShader = Shader.Find("Prometheus/Rendering/World/Grid Lit");
            Assert.That(gridShader, Is.Not.Null, "The project Grid Lit shader must be discoverable before its deferred-renderer contract can be validated.");
            Material inspectionMaterial = new Material(gridShader);
            try
            {
                ShaderTagId lightModeTagName = new ShaderTagId("LightMode");
                string[] passLightModes = Enumerable.Range(0, inspectionMaterial.passCount).Select(passIndex => gridShader.FindPassTagValue(passIndex, lightModeTagName).name).ToArray();
                string passReport = string.Join(", ", Enumerable.Range(0, inspectionMaterial.passCount).Select(passIndex => $"{inspectionMaterial.GetPassName(passIndex)}:{passLightModes[passIndex]}"));
                Assert.That(passLightModes, Does.Contain("UniversalForward"), $"Grid Lit must expose a UniversalForward pass for the project's default Forward renderer. Compiled passes: {passReport}.");
                Assert.That(passLightModes, Does.Contain("UniversalGBuffer"), $"Grid Lit must expose a UniversalGBuffer pass because Deferred MF.SSGI reads its albedo and occlusion from the GBuffer. Compiled passes: {passReport}.");
                Assert.That(passLightModes, Does.Not.Contain("UniversalForwardOnly"), $"Grid Lit must not bypass its GBuffer implementation through UniversalForwardOnly. Compiled passes: {passReport}.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inspectionMaterial);
            }
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

        /// <summary>
        /// Dynamically locates the only project rendering settings asset so tests do not depend on a duplicated hard-coded value table.
        /// </summary>
        private static PrometheusRenderingSettings LoadRenderingSettings()
        {
            string[] settingsGuids = AssetDatabase.FindAssets($"t:{nameof(PrometheusRenderingSettings)}", new[] { "Assets" });
            Assert.That(settingsGuids, Has.Length.EqualTo(1), "The project must contain exactly one Prometheus rendering settings asset.");
            PrometheusRenderingSettings renderingSettings = AssetDatabase.LoadAssetAtPath<PrometheusRenderingSettings>(AssetDatabase.GUIDToAssetPath(settingsGuids[0]));
            Assert.That(renderingSettings, Is.Not.Null, "The discovered Prometheus rendering settings asset must load successfully.");
            return renderingSettings;
        }

        /// <summary>
        /// Identifies the imported MF.SSGI renderer feature without adding a compile-time reference from the project-owned test assembly to Assembly-CSharp.
        /// </summary>
        private static bool IsSsgiFeature(ScriptableRendererFeature feature)
        {
            return feature != null && feature.GetType().FullName == SsgiFeatureTypeFullName;
        }

        /// <summary>
        /// Identifies the imported Shiny SSR renderer feature through its stable plugin type name.
        /// </summary>
        private static bool IsShinySsrFeature(ScriptableRendererFeature feature)
        {
            return feature != null && feature.GetType().FullName == ShinySsrFeatureTypeFullName;
        }
    }
}
