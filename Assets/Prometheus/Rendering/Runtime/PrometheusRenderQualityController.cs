using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using ShinySsrRendererFeature = ShinySSRR.ShinySSRR;

namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Selects one immutable platform, renderer-path, and quality-specific URP asset and applies the matching camera and Unity quality policy.
    /// </summary>
    public static class PrometheusRenderQualityController
    {
        private static readonly int QualityLevelShaderPropertyId = Shader.PropertyToID("_PrometheusQualityLevel");
        private static PrometheusRenderingSettings settings;
        private static UniversalRenderPipelineAsset activePipelineAsset;
        private static PrometheusRenderPlatform currentPlatform;
        private static PrometheusRenderPath currentRenderPath;
        private static PrometheusRenderQualityLevel currentQualityLevel;
        private static PrometheusRenderQualityProfile currentProfile;
        private static bool screenSpaceReflectionsRequested;
        private static bool screenSpaceReflectionsEnabled;
        private static bool initialized;

        /// <summary>
        /// Raised after a complete platform-specific quality profile has been applied.
        /// </summary>
        public static event Action<PrometheusRenderQualityLevel> QualityChanged;

        /// <summary>
        /// Raised after the active desktop renderer path changes.
        /// </summary>
        public static event Action<PrometheusRenderPath> RenderPathChanged;

        /// <summary>
        /// Raised after the effective Shiny SSR pass state changes.
        /// </summary>
        public static event Action<bool> ScreenSpaceReflectionsChanged;

        /// <summary>
        /// Gets whether the pre-scene rendering bootstrap selected an active pipeline asset.
        /// </summary>
        public static bool IsInitialized => initialized;

        /// <summary>
        /// Gets the hardware family selected for the current player.
        /// </summary>
        public static PrometheusRenderPlatform CurrentPlatform => currentPlatform;

        /// <summary>
        /// Gets the active renderer path.
        /// </summary>
        public static PrometheusRenderPath CurrentRenderPath => currentRenderPath;

        /// <summary>
        /// Gets the currently applied user-facing quality level.
        /// </summary>
        public static PrometheusRenderQualityLevel CurrentQualityLevel => currentQualityLevel;

        /// <summary>
        /// Gets the complete currently applied platform quality profile.
        /// </summary>
        public static PrometheusRenderQualityProfile CurrentProfile => currentProfile;

        /// <summary>
        /// Gets the immutable URP asset currently assigned to Unity QualitySettings.
        /// </summary>
        public static UniversalRenderPipelineAsset ActivePipelineAsset => activePipelineAsset;

        /// <summary>
        /// Gets the active pipeline through the former property name for compatible callers.
        /// </summary>
        public static UniversalRenderPipelineAsset RuntimePipelineAsset => activePipelineAsset;

        /// <summary>
        /// Gets whether Shiny screen-space reflections are effectively allowed to enqueue passes.
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
            activePipelineAsset = null;
            currentPlatform = default;
            currentRenderPath = default;
            currentQualityLevel = default;
            currentProfile = null;
            screenSpaceReflectionsRequested = false;
            ShinySsrRendererFeature.isEnabled = false;
            screenSpaceReflectionsEnabled = false;
            initialized = false;
            QualityChanged = null;
            RenderPathChanged = null;
            ScreenSpaceReflectionsChanged = null;
        }

        /// <summary>
        /// Loads external rendering settings and selects the platform startup configuration before the first scene starts.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            settings = Resources.Load<PrometheusRenderingSettings>(PrometheusRenderingSettings.ResourceName);
            if (settings == null) throw new InvalidOperationException($"Resources must contain '{PrometheusRenderingSettings.ResourceName}'. Run Prometheus/Rendering/Create Or Update Rendering Assets.");
            currentPlatform = Application.isMobilePlatform ? PrometheusRenderPlatform.Mobile : PrometheusRenderPlatform.Pc;
            currentRenderPath = currentPlatform == PrometheusRenderPlatform.Mobile ? PrometheusRenderPath.Forward : settings.StartupPcRenderPath;
            currentQualityLevel = settings.GetStartupQualityLevel(currentPlatform);
            screenSpaceReflectionsRequested = settings.ScreenSpaceReflectionsEnabledByDefault;
            QualitySettings.activeQualityLevelChanged += HandleUnityQualityLevelChanged;
            initialized = true;
            ApplyConfiguration(false, false);
        }

        /// <summary>
        /// Applies the requested Low or Mid level within the current platform and renderer path.
        /// </summary>
        public static void ApplyQuality(PrometheusRenderQualityLevel qualityLevel)
        {
            if (!initialized) throw new InvalidOperationException("Prometheus rendering must initialize before applying a runtime quality profile.");
            currentQualityLevel = qualityLevel;
            ApplyConfiguration(true, false);
        }

        /// <summary>
        /// Changes the desktop renderer path while preserving the current quality level.
        /// </summary>
        public static void ApplyRenderPath(PrometheusRenderPath renderPath)
        {
            if (!initialized) throw new InvalidOperationException("Prometheus rendering must initialize before changing the renderer path.");
            if (currentPlatform == PrometheusRenderPlatform.Mobile && renderPath != PrometheusRenderPath.Forward) throw new InvalidOperationException("Mobile rendering supports only the Forward path.");
            bool renderPathChanged = currentRenderPath != renderPath;
            currentRenderPath = renderPath;
            ApplyConfiguration(false, renderPathChanged);
        }

        /// <summary>
        /// Records the user-facing SSR preference while Low quality and non-Deferred paths continue to force the effective pass off.
        /// </summary>
        public static void SetScreenSpaceReflectionsEnabled(bool enabled)
        {
            if (!initialized) throw new InvalidOperationException("Prometheus rendering must initialize before changing the screen-space reflection state.");
            screenSpaceReflectionsRequested = enabled;
            ApplyScreenSpaceReflectionsState(IsDeferredMidConfiguration && screenSpaceReflectionsRequested);
        }

        /// <summary>
        /// Applies the active rendering policy to one gameplay camera without changing its composition or culling configuration.
        /// </summary>
        public static void ApplyCurrentCameraQuality(Camera camera, UniversalAdditionalCameraData cameraData)
        {
            if (!initialized) throw new InvalidOperationException("Prometheus rendering must initialize before configuring a gameplay camera.");
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (cameraData == null) throw new ArgumentNullException(nameof(cameraData));
            camera.allowHDR = activePipelineAsset.supportsHDR;
            camera.allowMSAA = currentRenderPath == PrometheusRenderPath.Forward && activePipelineAsset.msaaSampleCount > 1;
            cameraData.renderShadows = currentProfile.RealtimeShadowsEnabled;
            cameraData.renderPostProcessing = currentProfile.PostProcessingEnabled;
            cameraData.antialiasing = IsDeferredMidConfiguration ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.dithering = currentProfile.DitheringEnabled;
        }

        /// <summary>
        /// Maps one effective SSR state to Shiny's renderer-pass gate so editor tooling can inspect the integration without entering Play Mode.
        /// </summary>
        internal static void ApplyScreenSpaceReflectionsState(bool enabled)
        {
            bool stateChanged = screenSpaceReflectionsEnabled != enabled;
            ShinySsrRendererFeature.isEnabled = enabled;
            screenSpaceReflectionsEnabled = enabled;
            if (stateChanged) ScreenSpaceReflectionsChanged?.Invoke(enabled);
        }

        /// <summary>
        /// Reapplies project ownership when external code changes Unity's two compatibility quality indices.
        /// </summary>
        private static void HandleUnityQualityLevelChanged(int previousQualityLevel, int currentUnityQualityLevel)
        {
            currentQualityLevel = currentUnityQualityLevel switch { 0 => PrometheusRenderQualityLevel.Low, 1 => PrometheusRenderQualityLevel.Mid, _ => throw new InvalidOperationException($"Unity quality index '{currentUnityQualityLevel}' is outside the project-owned Low/Mid range.") };
            ApplyConfiguration(true, false);
        }

        /// <summary>
        /// Selects the exact immutable pipeline asset and applies all non-URP quality values as one configuration transaction.
        /// </summary>
        private static void ApplyConfiguration(bool notifyQualityChanged, bool notifyRenderPathChanged)
        {
            activePipelineAsset = settings.GetPipelineAsset(currentPlatform, currentRenderPath, currentQualityLevel);
            if (activePipelineAsset == null) throw new InvalidOperationException($"Rendering settings '{settings.name}' do not reference pipeline '{currentPlatform}/{currentRenderPath}/{currentQualityLevel}'.");
            currentProfile = settings.GetQualityProfile(currentPlatform, currentQualityLevel);
            QualitySettings.renderPipeline = activePipelineAsset;
            ApplyProfileToUnityQuality(currentProfile);
            ApplyScreenSpaceReflectionsState(IsDeferredMidConfiguration && screenSpaceReflectionsRequested);
            Shader.SetGlobalInteger(QualityLevelShaderPropertyId, (int)currentQualityLevel);
            if (notifyRenderPathChanged) RenderPathChanged?.Invoke(currentRenderPath);
            if (notifyQualityChanged || notifyRenderPathChanged) QualityChanged?.Invoke(currentQualityLevel);
        }

        /// <summary>
        /// Reports whether the expensive desktop Deferred Mid feature chain is active.
        /// </summary>
        private static bool IsDeferredMidConfiguration => currentPlatform == PrometheusRenderPlatform.Pc && currentRenderPath == PrometheusRenderPath.Deferred && currentQualityLevel == PrometheusRenderQualityLevel.Mid;

        /// <summary>
        /// Applies public URP quality properties for editor generation and compatibility tooling; immutable runtime assets are selected instead of mutated.
        /// </summary>
        internal static void ApplyProfileToPipeline(PrometheusRenderingSettings renderingSettings, PrometheusRenderQualityProfile profile, UniversalRenderPipelineAsset pipelineAsset)
        {
            pipelineAsset.supportsCameraDepthTexture = profile.PostProcessingEnabled && renderingSettings.RequiresCameraDepthTexture;
            pipelineAsset.supportsCameraOpaqueTexture = profile.PostProcessingEnabled && renderingSettings.RequiresCameraOpaqueTexture;
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
