using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ShinySsrRendererFeature = ShinySSRR.ShinySSRR;
using ShinySsrVolumeComponent = ShinySSRR.ShinyScreenSpaceRaytracedReflections;

namespace Xuan.Prometheus.Rendering.Editor
{
    /// <summary>
    /// Creates and wires the project-owned desktop and mobile rendering assets without merging independent renderer paths or quality tiers.
    /// </summary>
    public static class PrometheusRenderingAssetGenerator
    {
        private const string RenderingRootPath = "Assets/Prometheus/Rendering";
        private const string PipelineFolderPath = RenderingRootPath + "/Pipeline";
        private const string SettingsFolderPath = RenderingRootPath + "/Settings";
        private const string ResourcesFolderPath = SettingsFolderPath + "/Resources";
        private const string ForwardRendererDataAssetPath = PipelineFolderPath + "/PrometheusPcForwardRenderer.asset";
        private const string SsgiRendererDataAssetPath = PipelineFolderPath + "/PrometheusPcDeferredMidRenderer.asset";
        private const string PipelineAssetPath = PipelineFolderPath + "/PrometheusPcDeferredMidPipeline.asset";
        private const string PcForwardLowPipelineAssetPath = PipelineFolderPath + "/PrometheusPcForwardLowPipeline.asset";
        private const string PcForwardMidPipelineAssetPath = PipelineFolderPath + "/PrometheusPcForwardMidPipeline.asset";
        private const string PcDeferredLowPipelineAssetPath = PipelineFolderPath + "/PrometheusPcDeferredLowPipeline.asset";
        private const string MobileForwardLowPipelineAssetPath = PipelineFolderPath + "/PrometheusMobileForwardLowPipeline.asset";
        private const string MobileForwardMidPipelineAssetPath = PipelineFolderPath + "/PrometheusMobileForwardMidPipeline.asset";
        private const string EnvironmentProfileAssetPath = SettingsFolderPath + "/PrometheusEnvironmentProfile.asset";
        private const string RenderingSettingsAssetPath = ResourcesFolderPath + "/PrometheusRenderingSettings.asset";
        private const string SsgiExampleRendererDataAssetPath = "Assets/Trd/MF.SSGI/ExampleScene/Renderer/MF.SSGI - Example URP Renderer - SSGI.asset";
        private const string ShinySsrExampleRendererDataAssetPath = "Assets/Trd/ShinySSRR/Pipelines/URP/ForwardRenderer.asset";
        private const string SsgiFeatureTypeFullName = "MF.SSGI.SSGIFeature";
        private const string SsgiVolumeComponentTypeFullName = "MF.SSGI.SSGIVolumeComponent";
        private const string SsgiShaderSetupMenuPath = "Tools/SSGI/Add SSGI to 'Always included shaders'";
        private const string UrpCompatibilityModeDefine = "URP_COMPATIBILITY_MODE";

        /// <summary>
        /// Character layers whose transparent Spine meshes must write Shiny's custom depth so reflections remain visible for either horizontal facing.
        /// </summary>
        private static readonly string[] ShinySsrTransparentDepthLayerNames =
        {
            "Character",
            "Enemy"
        };

        /// <summary>
        /// Repairs all six platform, renderer-path, and quality pipeline assets while preserving authored environment and screen-space effect values.
        /// </summary>
        [MenuItem("Prometheus/Rendering/Create Or Update Rendering Assets")]
        public static void CreateOrUpdateRenderingAssets()
        {
            EnsureCurrentBuildTargetSupportsUrpCompatibilityMode();
            EnsureFolder(PipelineFolderPath);
            EnsureFolder(SettingsFolderPath);
            EnsureFolder(ResourcesFolderPath);
            UniversalRenderPipelineAsset sourcePipelineAsset = GetSourcePipelineAsset();
            UniversalRendererData forwardRendererData = GetOrCreateForwardRendererData(sourcePipelineAsset);
            UniversalRendererData ssgiRendererData = GetOrCreateSsgiRendererData(forwardRendererData);
            UniversalRenderPipelineAsset pipelineAsset = GetOrCreatePipelineAsset(sourcePipelineAsset, forwardRendererData, ssgiRendererData);
            UniversalRenderPipelineAsset pcForwardLowPipelineAsset = LoadRequiredAsset<UniversalRenderPipelineAsset>(PcForwardLowPipelineAssetPath);
            UniversalRenderPipelineAsset pcForwardMidPipelineAsset = LoadRequiredAsset<UniversalRenderPipelineAsset>(PcForwardMidPipelineAssetPath);
            UniversalRenderPipelineAsset pcDeferredLowPipelineAsset = LoadRequiredAsset<UniversalRenderPipelineAsset>(PcDeferredLowPipelineAssetPath);
            UniversalRenderPipelineAsset mobileForwardLowPipelineAsset = LoadRequiredAsset<UniversalRenderPipelineAsset>(MobileForwardLowPipelineAssetPath);
            UniversalRenderPipelineAsset mobileForwardMidPipelineAsset = LoadRequiredAsset<UniversalRenderPipelineAsset>(MobileForwardMidPipelineAssetPath);
            PrometheusEnvironmentProfile environmentProfile = GetOrCreateEnvironmentProfile();
            PrometheusRenderingSettings renderingSettings = GetOrCreateRenderingSettings(pcForwardLowPipelineAsset, pcForwardMidPipelineAsset, pcDeferredLowPipelineAsset, pipelineAsset, mobileForwardLowPipelineAsset, mobileForwardMidPipelineAsset, environmentProfile);
            ConfigurePipelineCapabilities(pcForwardLowPipelineAsset, renderingSettings, renderingSettings.GetQualityProfile(PrometheusRenderPlatform.Pc, PrometheusRenderQualityLevel.Low), PrometheusRenderPath.Forward);
            ConfigurePipelineCapabilities(pcForwardMidPipelineAsset, renderingSettings, renderingSettings.GetQualityProfile(PrometheusRenderPlatform.Pc, PrometheusRenderQualityLevel.Mid), PrometheusRenderPath.Forward);
            ConfigurePipelineCapabilities(pcDeferredLowPipelineAsset, renderingSettings, renderingSettings.GetQualityProfile(PrometheusRenderPlatform.Pc, PrometheusRenderQualityLevel.Low), PrometheusRenderPath.Deferred);
            ConfigurePipelineCapabilities(pipelineAsset, renderingSettings, renderingSettings.GetQualityProfile(PrometheusRenderPlatform.Pc, PrometheusRenderQualityLevel.Mid), PrometheusRenderPath.Deferred);
            ConfigurePipelineCapabilities(mobileForwardLowPipelineAsset, renderingSettings, renderingSettings.GetQualityProfile(PrometheusRenderPlatform.Mobile, PrometheusRenderQualityLevel.Low), PrometheusRenderPath.Forward);
            ConfigurePipelineCapabilities(mobileForwardMidPipelineAsset, renderingSettings, renderingSettings.GetQualityProfile(PrometheusRenderPlatform.Mobile, PrometheusRenderQualityLevel.Mid), PrometheusRenderPath.Forward);
            AssignPipelineToUnitySettings(pcForwardLowPipelineAsset, pcForwardMidPipelineAsset);
            EnsureSsgiShadersAreIncludedInBuild();
            EnsureShinySsrVolumeProfiles();
            EditorUtility.SetDirty(forwardRendererData);
            EditorUtility.SetDirty(ssgiRendererData);
            EditorUtility.SetDirty(pipelineAsset);
            EditorUtility.SetDirty(environmentProfile);
            EditorUtility.SetDirty(renderingSettings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = renderingSettings;
            EditorGUIUtility.PingObject(renderingSettings);
            Debug.Log($"Prometheus rendering now owns PC Forward, PC Deferred, and Mobile Forward pipeline families with Low and Mid assets, plus platform quality profiles in '{RenderingSettingsAssetPath}'.");
        }

        /// <summary>
        /// Loads one generated rendering asset and reports the exact missing path instead of silently rebuilding it from an unrelated platform preset.
        /// </summary>
        private static T LoadRequiredAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null) throw new InvalidOperationException($"Required generated rendering asset is missing at '{assetPath}'. Restore the project-owned asset before updating rendering settings.");
            return asset;
        }

        /// <summary>
        /// Adds Unity 6000.3's compile-time compatibility switch to the active build target because the serialized Compatibility Mode checkbox is ignored when this symbol is absent.
        /// </summary>
        private static void EnsureCurrentBuildTargetSupportsUrpCompatibilityMode()
        {
            BuildTargetGroup activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            NamedBuildTarget activeNamedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(activeBuildTargetGroup);
            string[] currentDefines = PlayerSettings.GetScriptingDefineSymbols(activeNamedBuildTarget).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (currentDefines.Contains(UrpCompatibilityModeDefine, StringComparer.Ordinal))
            {
                return;
            }

            PlayerSettings.SetScriptingDefineSymbols(activeNamedBuildTarget, string.Join(";", currentDefines.Append(UrpCompatibilityModeDefine)));
        }

        /// <summary>
        /// Executes MF.SSGI's documented build setup so every runtime shader resolved through Shader.Find survives player shader stripping.
        /// </summary>
        private static void EnsureSsgiShadersAreIncludedInBuild()
        {
            if (!EditorApplication.ExecuteMenuItem(SsgiShaderSetupMenuPath))
            {
                throw new InvalidOperationException($"MF.SSGI must expose the documented menu item '{SsgiShaderSetupMenuPath}' before Prometheus rendering assets can be generated.");
            }
        }

        /// <summary>
        /// Returns the currently assigned URP asset used as the serialization source for the first project-owned pipeline asset.
        /// </summary>
        private static UniversalRenderPipelineAsset GetSourcePipelineAsset()
        {
            UniversalRenderPipelineAsset sourcePipelineAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (sourcePipelineAsset == null)
            {
                throw new InvalidOperationException("GraphicsSettings.defaultRenderPipeline must reference a URP asset before generating Prometheus rendering assets.");
            }

            return sourcePipelineAsset;
        }

        /// <summary>
        /// Returns the existing project renderer or clones the current active forward renderer during first-time generation.
        /// </summary>
        private static UniversalRendererData GetOrCreateForwardRendererData(UniversalRenderPipelineAsset sourcePipelineAsset)
        {
            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(ForwardRendererDataAssetPath);
            if (rendererData != null)
            {
                return rendererData;
            }

            if (sourcePipelineAsset.rendererDataList.Length == 0 || sourcePipelineAsset.rendererDataList[0] is not UniversalRendererData sourceRendererData)
            {
                throw new InvalidOperationException($"Source pipeline '{sourcePipelineAsset.name}' must contain a UniversalRendererData at index zero.");
            }

            rendererData = UnityEngine.Object.Instantiate(sourceRendererData);
            rendererData.name = "PrometheusPcForwardRenderer";
            rendererData.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(rendererData, ForwardRendererDataAssetPath);
            return rendererData;
        }

        /// <summary>
        /// Returns the dedicated deferred renderer and guarantees that it owns one MF.SSGI feature followed by one optional Shiny SSR feature.
        /// </summary>
        private static UniversalRendererData GetOrCreateSsgiRendererData(UniversalRendererData forwardRendererData)
        {
            UniversalRendererData ssgiRendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(SsgiRendererDataAssetPath);
            if (ssgiRendererData == null)
            {
                ssgiRendererData = UnityEngine.Object.Instantiate(forwardRendererData);
                ssgiRendererData.name = "PrometheusPcDeferredMidRenderer";
                ssgiRendererData.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(ssgiRendererData, SsgiRendererDataAssetPath);
            }

            SerializedObject serializedRendererData = new SerializedObject(ssgiRendererData);
            SetIntegerProperty(serializedRendererData, "m_RenderingMode", (int)RenderingMode.Deferred);
            serializedRendererData.ApplyModifiedPropertiesWithoutUndo();
            ScriptableRendererFeature ssgiFeature = EnsureSsgiFeature(ssgiRendererData);
            EnsureShinySsrFeature(ssgiRendererData, ssgiFeature);
            return ssgiRendererData;
        }

        /// <summary>
        /// Adds the imported MF.SSGI feature as a renderer-owned sub-asset and keeps its GBuffer sampling switch aligned with the dedicated deferred renderer.
        /// </summary>
        private static ScriptableRendererFeature EnsureSsgiFeature(UniversalRendererData ssgiRendererData)
        {
            ScriptableRendererFeature ssgiFeature = ssgiRendererData.rendererFeatures.FirstOrDefault(feature => feature != null && feature.GetType().FullName == SsgiFeatureTypeFullName);
            if (ssgiFeature == null)
            {
                UniversalRendererData exampleRendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(SsgiExampleRendererDataAssetPath);
                if (exampleRendererData == null)
                {
                    throw new InvalidOperationException($"The imported MF.SSGI example renderer is required at '{SsgiExampleRendererDataAssetPath}' before generating the dedicated project renderer.");
                }

                ScriptableRendererFeature exampleSsgiFeature = exampleRendererData.rendererFeatures.SingleOrDefault(feature => feature != null && feature.GetType().FullName == SsgiFeatureTypeFullName);
                if (exampleSsgiFeature == null)
                {
                    throw new InvalidOperationException($"Renderer '{SsgiExampleRendererDataAssetPath}' must contain exactly one '{SsgiFeatureTypeFullName}' feature to seed the project-owned renderer.");
                }

                ssgiFeature = UnityEngine.Object.Instantiate(exampleSsgiFeature);
                ssgiFeature.name = exampleSsgiFeature.name;
                ssgiFeature.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(ssgiFeature, ssgiRendererData);
                ssgiRendererData.rendererFeatures.Add(ssgiFeature);
            }

            SerializedObject serializedFeature = new SerializedObject(ssgiFeature);
            SerializedProperty showInSceneViewProperty = serializedFeature.FindProperty("showInSceneView");
            SerializedProperty settingsProperty = serializedFeature.FindProperty("settings");
            SerializedProperty useDeferredRenderingProperty = settingsProperty?.FindPropertyRelative("UseDeferredRendering");
            SerializedProperty debugScreenCoverageProperty = settingsProperty?.FindPropertyRelative("DebugScreenCoverage");
            if (showInSceneViewProperty == null || useDeferredRenderingProperty == null || debugScreenCoverageProperty == null)
            {
                throw new InvalidOperationException($"Feature '{ssgiFeature.name}' does not expose the expected Scene View, deferred-rendering, and debug-coverage serialization contract.");
            }

            showInSceneViewProperty.boolValue = false;
            useDeferredRenderingProperty.boolValue = true;
            debugScreenCoverageProperty.floatValue = 1f;
            serializedFeature.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ssgiFeature);
            return ssgiFeature;
        }

        /// <summary>
        /// Adds Shiny SSR as a renderer-owned sub-asset, enables its deferred GBuffer path, and schedules it after the current SSGI composition event.
        /// </summary>
        private static void EnsureShinySsrFeature(UniversalRendererData ssgiRendererData, ScriptableRendererFeature ssgiFeature)
        {
            ShinySsrRendererFeature[] existingShinySsrFeatures = ssgiRendererData.rendererFeatures.OfType<ShinySsrRendererFeature>().ToArray();
            if (existingShinySsrFeatures.Length > 1)
            {
                throw new InvalidOperationException($"Renderer '{ssgiRendererData.name}' contains {existingShinySsrFeatures.Length} Shiny SSR features, but the project rendering chain owns exactly one optional SSR pass.");
            }

            ShinySsrRendererFeature shinySsrFeature = existingShinySsrFeatures.SingleOrDefault();
            if (shinySsrFeature == null)
            {
                UniversalRendererData exampleRendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(ShinySsrExampleRendererDataAssetPath);
                if (exampleRendererData == null)
                {
                    throw new InvalidOperationException($"The imported Shiny SSR example renderer is required at '{ShinySsrExampleRendererDataAssetPath}' before generating the project renderer.");
                }

                ShinySsrRendererFeature exampleShinySsrFeature = exampleRendererData.rendererFeatures.OfType<ShinySsrRendererFeature>().SingleOrDefault();
                if (exampleShinySsrFeature == null)
                {
                    throw new InvalidOperationException($"Renderer '{ShinySsrExampleRendererDataAssetPath}' must contain exactly one Shiny SSR feature to seed the project-owned renderer.");
                }

                shinySsrFeature = UnityEngine.Object.Instantiate(exampleShinySsrFeature);
                shinySsrFeature.name = exampleShinySsrFeature.name;
                shinySsrFeature.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(shinySsrFeature, ssgiRendererData);
                ssgiRendererData.rendererFeatures.Add(shinySsrFeature);
            }

            SerializedProperty ssgiSettingsProperty = new SerializedObject(ssgiFeature).FindProperty("settings");
            SerializedProperty ssgiRenderPassEventProperty = ssgiSettingsProperty?.FindPropertyRelative("RenderPassEvent");
            if (ssgiRenderPassEventProperty == null)
            {
                throw new InvalidOperationException($"Feature '{ssgiFeature.name}' does not expose the expected render-pass event serialization contract.");
            }

            SerializedObject serializedShinySsrFeature = new SerializedObject(shinySsrFeature);
            SerializedProperty useDeferredProperty = serializedShinySsrFeature.FindProperty("useDeferred");
            SerializedProperty renderPassEventProperty = serializedShinySsrFeature.FindProperty("renderPassEvent");
            SerializedProperty enableTransparencyDepthPrepassProperty = serializedShinySsrFeature.FindProperty("enableTransparencyDepthPrepass");
            SerializedProperty transparencyDepthPrepassLayerMaskProperty = serializedShinySsrFeature.FindProperty("transparencyDepthPrepassLayerMask");
            if (useDeferredProperty == null || renderPassEventProperty == null || enableTransparencyDepthPrepassProperty == null || transparencyDepthPrepassLayerMaskProperty == null)
            {
                throw new InvalidOperationException($"Feature '{shinySsrFeature.name}' does not expose the expected Shiny SSR serialization contract.");
            }

            useDeferredProperty.boolValue = true;
            enableTransparencyDepthPrepassProperty.boolValue = true;
            transparencyDepthPrepassLayerMaskProperty.intValue |= GetRequiredShinySsrTransparentDepthLayerMask();
            if (renderPassEventProperty.intValue <= ssgiRenderPassEventProperty.intValue)
            {
                renderPassEventProperty.intValue = ssgiRenderPassEventProperty.intValue + 1;
            }

            serializedShinySsrFeature.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(shinySsrFeature);
        }

        /// <summary>
        /// Resolves the authored character layer names at generation time so the serialized mask follows project layer assignments instead of fixed bit positions.
        /// </summary>
        private static int GetRequiredShinySsrTransparentDepthLayerMask()
        {
            int requiredLayerMask = 0;
            foreach (string layerName in ShinySsrTransparentDepthLayerNames)
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer < 0)
                {
                    throw new InvalidOperationException($"Required Shiny SSR transparent-depth layer '{layerName}' is missing from the project layer configuration.");
                }

                requiredLayerMask |= 1 << layer;
            }

            return requiredLayerMask;
        }

        /// <summary>
        /// Adds one active Shiny SSR Volume component to every project rendering profile that already owns MF.SSGI settings while preserving all existing authored values.
        /// </summary>
        private static void EnsureShinySsrVolumeProfiles()
        {
            VolumeProfile[] ssgiVolumeProfiles = AssetDatabase.FindAssets("t:VolumeProfile", new[] { SettingsFolderPath }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<VolumeProfile>).Where(profile => profile != null && profile.components.Any(component => component != null && component.GetType().FullName == SsgiVolumeComponentTypeFullName)).ToArray();
            if (ssgiVolumeProfiles.Length == 0)
            {
                throw new InvalidOperationException($"Folder '{SettingsFolderPath}' must contain at least one Volume profile with '{SsgiVolumeComponentTypeFullName}' before Shiny SSR can join the SSGI rendering chain.");
            }

            foreach (VolumeProfile volumeProfile in ssgiVolumeProfiles)
            {
                if (volumeProfile.TryGet(out ShinySsrVolumeComponent shinySsrVolumeComponent))
                {
                    continue;
                }

                shinySsrVolumeComponent = volumeProfile.Add<ShinySsrVolumeComponent>(true);
                shinySsrVolumeComponent.ApplyRaytracingPreset(ShinySSRR.RaytracingPreset.Medium);
                shinySsrVolumeComponent.reflectionsMultiplier.Override(1f);
                shinySsrVolumeComponent.temporalFilter.Override(false);
                AssetDatabase.AddObjectToAsset(shinySsrVolumeComponent, volumeProfile);
                EditorUtility.SetDirty(shinySsrVolumeComponent);
                EditorUtility.SetDirty(volumeProfile);
            }
        }

        /// <summary>
        /// Returns the existing single Prometheus pipeline asset or clones the active URP asset during first-time generation.
        /// </summary>
        private static UniversalRenderPipelineAsset GetOrCreatePipelineAsset(UniversalRenderPipelineAsset sourcePipelineAsset, UniversalRendererData forwardRendererData, UniversalRendererData ssgiRendererData)
        {
            UniversalRenderPipelineAsset pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipelineAsset != null)
            {
                return pipelineAsset;
            }

            pipelineAsset = UnityEngine.Object.Instantiate(sourcePipelineAsset);
            pipelineAsset.name = "PrometheusPcDeferredMidPipeline";
            pipelineAsset.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);
            ConfigurePipelineRenderers(pipelineAsset, forwardRendererData, ssgiRendererData);
            return pipelineAsset;
        }

        /// <summary>
        /// Returns the existing environment profile or creates the default daily curves and seasonal palettes through ScriptableObject field initialization.
        /// </summary>
        private static PrometheusEnvironmentProfile GetOrCreateEnvironmentProfile()
        {
            PrometheusEnvironmentProfile environmentProfile = AssetDatabase.LoadAssetAtPath<PrometheusEnvironmentProfile>(EnvironmentProfileAssetPath);
            if (environmentProfile != null)
            {
                return environmentProfile;
            }

            environmentProfile = ScriptableObject.CreateInstance<PrometheusEnvironmentProfile>();
            environmentProfile.name = "PrometheusEnvironmentProfile";
            AssetDatabase.CreateAsset(environmentProfile, EnvironmentProfileAssetPath);
            return environmentProfile;
        }

        /// <summary>
        /// Returns the Resources settings asset, assigns generated assets, and creates a complete profile set only when no authored profiles exist.
        /// </summary>
        private static PrometheusRenderingSettings GetOrCreateRenderingSettings(UniversalRenderPipelineAsset pcForwardLowPipelineAsset, UniversalRenderPipelineAsset pcForwardMidPipelineAsset, UniversalRenderPipelineAsset pcDeferredLowPipelineAsset, UniversalRenderPipelineAsset pcDeferredMidPipelineAsset, UniversalRenderPipelineAsset mobileForwardLowPipelineAsset, UniversalRenderPipelineAsset mobileForwardMidPipelineAsset, PrometheusEnvironmentProfile environmentProfile)
        {
            PrometheusRenderingSettings renderingSettings = AssetDatabase.LoadAssetAtPath<PrometheusRenderingSettings>(RenderingSettingsAssetPath);
            if (renderingSettings == null)
            {
                renderingSettings = ScriptableObject.CreateInstance<PrometheusRenderingSettings>();
                renderingSettings.name = PrometheusRenderingSettings.ResourceName;
                AssetDatabase.CreateAsset(renderingSettings, RenderingSettingsAssetPath);
            }

            renderingSettings.ConfigureAssets(pcForwardLowPipelineAsset, pcForwardMidPipelineAsset, pcDeferredLowPipelineAsset, pcDeferredMidPipelineAsset, mobileForwardLowPipelineAsset, mobileForwardMidPipelineAsset, environmentProfile);
            if (renderingSettings.QualityProfiles.Count == 0)
            {
                renderingSettings.InitializeQualityProfiles(CreateDefaultQualityProfiles());
            }

            return renderingSettings;
        }

        /// <summary>
        /// Creates the initial project-owned quality values; later edits remain serialized in the settings asset and are not overwritten by this generator.
        /// </summary>
        private static PrometheusRenderQualityProfile[] CreateDefaultQualityProfiles()
        {
            PrometheusRenderQualityProfile pcLow = new PrometheusRenderQualityProfile(PrometheusRenderPlatform.Pc, PrometheusRenderQualityLevel.Low, false, false, false, false, 0.75f, 1, false, LightShadows.None, 256, 0f, 1, 0, 256, 1, 0.7f, 1, AnisotropicFiltering.Disable, 0, 60);
            PrometheusRenderQualityProfile pcMid = new PrometheusRenderQualityProfile(PrometheusRenderPlatform.Pc, PrometheusRenderQualityLevel.Mid, true, true, true, true, 1f, 2, true, LightShadows.Soft, 2048, 60f, 2, 4, 512, 0, 1.2f, 0, AnisotropicFiltering.ForceEnable, 1, -1);
            PrometheusRenderQualityProfile mobileLow = new PrometheusRenderQualityProfile(PrometheusRenderPlatform.Mobile, PrometheusRenderQualityLevel.Low, false, false, false, false, 0.65f, 1, false, LightShadows.None, 256, 0f, 1, 0, 256, 2, 0.5f, 1, AnisotropicFiltering.Disable, 0, 30);
            PrometheusRenderQualityProfile mobileMid = new PrometheusRenderQualityProfile(PrometheusRenderPlatform.Mobile, PrometheusRenderQualityLevel.Mid, true, true, true, false, 0.85f, 2, false, LightShadows.Hard, 1024, 25f, 1, 2, 512, 1, 0.8f, 0, AnisotropicFiltering.Enable, 0, 60);
            return new[] { pcLow, pcMid, mobileLow, mobileMid };
        }

        /// <summary>
        /// Replaces the pipeline renderer list with the default forward renderer followed by the dedicated deferred SSGI renderer through URP's serialized asset contract.
        /// </summary>
        private static void ConfigurePipelineRenderers(UniversalRenderPipelineAsset pipelineAsset, UniversalRendererData forwardRendererData, UniversalRendererData ssgiRendererData)
        {
            SerializedObject serializedPipeline = new SerializedObject(pipelineAsset);
            SerializedProperty rendererDataList = serializedPipeline.FindProperty("m_RendererDataList");
            SerializedProperty defaultRendererIndex = serializedPipeline.FindProperty("m_DefaultRendererIndex");
            if (rendererDataList == null || defaultRendererIndex == null)
            {
                throw new InvalidOperationException($"URP asset '{pipelineAsset.name}' does not expose the expected renderer serialization contract.");
            }

            rendererDataList.arraySize = 2;
            rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue = forwardRendererData;
            rendererDataList.GetArrayElementAtIndex(1).objectReferenceValue = ssgiRendererData;
            defaultRendererIndex.intValue = 0;
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Serializes the light and shadow capabilities required by one platform quality profile into its dedicated pipeline asset.
        /// </summary>
        private static void ConfigurePipelineCapabilities(UniversalRenderPipelineAsset pipelineAsset, PrometheusRenderingSettings renderingSettings, PrometheusRenderQualityProfile profile, PrometheusRenderPath renderPath)
        {
            PrometheusRenderQualityController.ApplyProfileToPipeline(renderingSettings, profile, pipelineAsset);
            pipelineAsset.msaaSampleCount = renderPath == PrometheusRenderPath.Deferred ? 1 : profile.MsaaSampleCount;
            pipelineAsset.supportsDynamicBatching = profile.Platform == PrometheusRenderPlatform.Mobile;
            SerializedObject serializedPipeline = new SerializedObject(pipelineAsset);
            SetIntegerProperty(serializedPipeline, "m_MainLightRenderingMode", profile.RealtimeLightingEnabled ? (int)renderingSettings.MainLightRenderingMode : (int)LightRenderingMode.Disabled);
            SetBooleanProperty(serializedPipeline, "m_MainLightShadowsSupported", profile.RealtimeShadowsEnabled && renderingSettings.SupportsMainLightShadows);
            SetIntegerProperty(serializedPipeline, "m_AdditionalLightsRenderingMode", profile.RealtimeLightingEnabled ? (int)renderingSettings.AdditionalLightsRenderingMode : (int)LightRenderingMode.Disabled);
            SetBooleanProperty(serializedPipeline, "m_AdditionalLightShadowsSupported", profile.RealtimeShadowsEnabled && renderingSettings.SupportsAdditionalLightShadows);
            SetBooleanProperty(serializedPipeline, "m_SoftShadowsSupported", profile.RealtimeShadowsEnabled && profile.MainLightShadows == LightShadows.Soft && renderingSettings.SupportsSoftShadows);
            SetBooleanProperty(serializedPipeline, "m_AnyShadowsSupported", profile.RealtimeShadowsEnabled && (renderingSettings.SupportsMainLightShadows || renderingSettings.SupportsAdditionalLightShadows));
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Writes one required integer URP serialization property and fails immediately if the installed URP version changes that contract.
        /// </summary>
        private static void SetIntegerProperty(SerializedObject serializedObject, string propertyName, int value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Serialized object '{serializedObject.targetObject.name}' does not expose required integer property '{propertyName}'.");
            }

            property.intValue = value;
        }

        /// <summary>
        /// Writes one required Boolean URP serialization property and fails immediately if the installed URP version changes that contract.
        /// </summary>
        private static void SetBooleanProperty(SerializedObject serializedObject, string propertyName, bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Serialized object '{serializedObject.targetObject.name}' does not expose required Boolean property '{propertyName}'.");
            }

            property.boolValue = value;
        }

        /// <summary>
        /// Assigns the desktop Mid asset as the Graphics default and maps Unity's Low and Mid compatibility entries to desktop Forward assets.
        /// </summary>
        private static void AssignPipelineToUnitySettings(UniversalRenderPipelineAsset lowPipelineAsset, UniversalRenderPipelineAsset midPipelineAsset)
        {
            GraphicsSettings.defaultRenderPipeline = midPipelineAsset;
            UnityEngine.Object qualitySettingsObject = QualitySettings.GetQualitySettings();
            SerializedObject serializedQualitySettings = new SerializedObject(qualitySettingsObject);
            SerializedProperty qualityLevels = serializedQualitySettings.FindProperty("m_QualitySettings");
            if (qualityLevels == null)
            {
                throw new InvalidOperationException("Unity QualitySettings do not expose the expected quality-level serialization contract.");
            }

            for (int qualityIndex = 0; qualityIndex < qualityLevels.arraySize; qualityIndex++)
            {
                SerializedProperty pipelineOverride = qualityLevels.GetArrayElementAtIndex(qualityIndex).FindPropertyRelative("customRenderPipeline");
                if (pipelineOverride == null)
                {
                    throw new InvalidOperationException($"Unity quality level {qualityIndex} does not expose the expected customRenderPipeline property.");
                }

                pipelineOverride.objectReferenceValue = qualityIndex == 0 ? lowPipelineAsset : midPipelineAsset;
            }

            serializedQualitySettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(qualitySettingsObject);
        }

        /// <summary>
        /// Creates every folder segment required by an asset path while preserving existing project folders.
        /// </summary>
        private static void EnsureFolder(string folderPath)
        {
            string[] pathSegments = folderPath.Split('/');
            string currentPath = pathSegments[0];
            for (int segmentIndex = 1; segmentIndex < pathSegments.Length; segmentIndex++)
            {
                string nextPath = currentPath + "/" + pathSegments[segmentIndex];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, pathSegments[segmentIndex]);
                }

                currentPath = nextPath;
            }
        }
    }
}
