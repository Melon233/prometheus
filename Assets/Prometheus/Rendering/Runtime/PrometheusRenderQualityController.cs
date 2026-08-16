using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using ShinySsrRendererFeature = ShinySSRR.ShinySSRR;

namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Owns runtime quality changes by modifying one non-persistent copy of the single serialized Prometheus URP asset.
    /// </summary>
    public static class PrometheusRenderQualityController
    {
        private static readonly int QualityLevelShaderPropertyId = Shader.PropertyToID("_PrometheusQualityLevel");
        private static PrometheusRenderingSettings settings;
        private static UniversalRenderPipelineAsset runtimePipelineAsset;
        private static PrometheusRenderQualityLevel currentQualityLevel;
        private static PrometheusRenderQualityProfile currentProfile;
        private static bool screenSpaceReflectionsEnabled;
        private static bool initialized;

        /// <summary>
        /// Raised after every complete project quality profile has been applied.
        /// </summary>
        public static event Action<PrometheusRenderQualityLevel> QualityChanged;

        /// <summary>
        /// Raised after the project-owned Shiny SSR runtime master switch changes.
        /// </summary>
        public static event Action<bool> ScreenSpaceReflectionsChanged;

        /// <summary>
        /// Gets whether the pre-scene rendering bootstrap created the runtime pipeline copy.
        /// </summary>
        public static bool IsInitialized => initialized;

        /// <summary>
        /// Gets the currently applied project quality level.
        /// </summary>
        public static PrometheusRenderQualityLevel CurrentQualityLevel => currentQualityLevel;

        /// <summary>
        /// Gets the complete currently applied project quality profile.
        /// </summary>
        public static PrometheusRenderQualityProfile CurrentProfile => currentProfile;

        /// <summary>
        /// Gets the non-persistent URP asset modified by runtime quality changes.
        /// </summary>
        public static UniversalRenderPipelineAsset RuntimePipelineAsset => runtimePipelineAsset;

        /// <summary>
        /// Gets whether Shiny screen-space reflections are currently allowed to enqueue their renderer passes.
        /// </summary>
        public static bool ScreenSpaceReflectionsEnabled => screenSpaceReflectionsEnabled;

        /// <summary>
        /// Clears static state before entering a new runtime session, including sessions with domain reload disabled.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            QualitySettings.activeQualityLevelChanged -= HandleUnityQualityLevelChanged;
            settings = null;
            runtimePipelineAsset = null;
            currentQualityLevel = default;
            currentProfile = null;
            ShinySsrRendererFeature.isEnabled = false;
            screenSpaceReflectionsEnabled = false;
            initialized = false;
            QualityChanged = null;
            ScreenSpaceReflectionsChanged = null;
        }

        /// <summary>
        /// Loads project settings, clones the only serialized pipeline asset, and applies the startup profile before the first scene starts.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            settings = Resources.Load<PrometheusRenderingSettings>(PrometheusRenderingSettings.ResourceName);
            if (settings == null)
            {
                throw new InvalidOperationException($"Resources must contain '{PrometheusRenderingSettings.ResourceName}'. Run Prometheus/Rendering/Create Or Update Rendering Assets.");
            }

            if (settings.PipelineAsset == null)
            {
                throw new InvalidOperationException($"Rendering settings '{settings.name}' must reference the single Prometheus URP asset.");
            }

            runtimePipelineAsset = UnityEngine.Object.Instantiate(settings.PipelineAsset);
            runtimePipelineAsset.name = $"{settings.PipelineAsset.name} (Runtime)";
            runtimePipelineAsset.hideFlags = HideFlags.DontSave;
            QualitySettings.renderPipeline = runtimePipelineAsset;
            QualitySettings.activeQualityLevelChanged += HandleUnityQualityLevelChanged;
            initialized = true;
            ApplyScreenSpaceReflectionsState(settings.ScreenSpaceReflectionsEnabledByDefault);
            ApplyQuality(settings.StartupQualityLevel);
        }

        /// <summary>
        /// Applies a complete project quality profile to the runtime pipeline and Unity global quality values.
        /// </summary>
        public static void ApplyQuality(PrometheusRenderQualityLevel qualityLevel)
        {
            if (!initialized)
            {
                throw new InvalidOperationException("Prometheus rendering must initialize before applying a runtime quality profile.");
            }

            PrometheusRenderQualityProfile profile = settings.GetQualityProfile(qualityLevel);
            ApplyProfileToPipeline(settings, profile, runtimePipelineAsset);
            ApplyProfileToUnityQuality(profile);
            currentQualityLevel = qualityLevel;
            currentProfile = profile;
            Shader.SetGlobalInteger(QualityLevelShaderPropertyId, (int)qualityLevel);
            QualityChanged?.Invoke(qualityLevel);
        }

        /// <summary>
        /// Changes the user-facing Shiny SSR master switch without mutating the shared renderer data asset or its authored Volume parameters.
        /// </summary>
        public static void SetScreenSpaceReflectionsEnabled(bool enabled)
        {
            if (!initialized)
            {
                throw new InvalidOperationException("Prometheus rendering must initialize before changing the screen-space reflection state.");
            }

            ApplyScreenSpaceReflectionsState(enabled);
        }

        /// <summary>
        /// Maps one project-owned SSR state to Shiny's renderer-pass gate so editor tests can validate the integration without entering Play Mode.
        /// </summary>
        internal static void ApplyScreenSpaceReflectionsState(bool enabled)
        {
            bool stateChanged = screenSpaceReflectionsEnabled != enabled;
            ShinySsrRendererFeature.isEnabled = enabled;
            screenSpaceReflectionsEnabled = enabled;
            if (stateChanged)
            {
                ScreenSpaceReflectionsChanged?.Invoke(enabled);
            }
        }

        /// <summary>
        /// Reasserts the runtime pipeline copy and current Prometheus profile when external code changes Unity's legacy quality index.
        /// </summary>
        private static void HandleUnityQualityLevelChanged(int previousQualityLevel, int currentUnityQualityLevel)
        {
            QualitySettings.renderPipeline = runtimePipelineAsset;
            ApplyQuality(currentQualityLevel);
        }

        /// <summary>
        /// Maps project-owned settings and one quality profile onto a URP asset without relying on Unity quality-level assets.
        /// </summary>
        internal static void ApplyProfileToPipeline(PrometheusRenderingSettings renderingSettings, PrometheusRenderQualityProfile profile, UniversalRenderPipelineAsset pipelineAsset)
        {
            pipelineAsset.supportsCameraDepthTexture = renderingSettings.RequiresCameraDepthTexture;
            pipelineAsset.supportsCameraOpaqueTexture = renderingSettings.RequiresCameraOpaqueTexture;
            pipelineAsset.useSRPBatcher = renderingSettings.UseSrpBatcher;
            pipelineAsset.supportsDynamicBatching = renderingSettings.SupportsDynamicBatching;
            pipelineAsset.useAdaptivePerformance = renderingSettings.UseAdaptivePerformance;
            pipelineAsset.renderScale = profile.RenderScale;
            pipelineAsset.msaaSampleCount = profile.MsaaSampleCount;
            pipelineAsset.supportsHDR = profile.SupportsHdr;
            pipelineAsset.mainLightShadowmapResolution = profile.MainLightShadowmapResolution;
            pipelineAsset.shadowDistance = profile.ShadowDistance;
            pipelineAsset.shadowCascadeCount = profile.ShadowCascadeCount;
            pipelineAsset.maxAdditionalLightsCount = profile.MaxAdditionalLightsCount;
            pipelineAsset.additionalLightsShadowmapResolution = profile.AdditionalLightsShadowmapResolution;
        }

        /// <summary>
        /// Maps project-owned non-URP values onto Unity's global runtime quality controls.
        /// </summary>
        internal static void ApplyProfileToUnityQuality(PrometheusRenderQualityProfile profile)
        {
            QualitySettings.globalTextureMipmapLimit = profile.GlobalTextureMipmapLimit;
            QualitySettings.lodBias = profile.LodBias;
            QualitySettings.maximumLODLevel = profile.MaximumLodLevel;
            QualitySettings.anisotropicFiltering = profile.AnisotropicFiltering;
            QualitySettings.vSyncCount = profile.VSyncCount;
            Application.targetFrameRate = profile.TargetFrameRate;
        }
    }
}
