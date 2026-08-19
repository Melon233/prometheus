using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Defines every platform, renderer-path, and quality-specific pipeline asset together with runtime quality profiles.
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Rendering/Rendering Settings", fileName = ResourceName)]
    public sealed class PrometheusRenderingSettings : ScriptableObject
    {
        /// <summary>
        /// Resource name used by the pre-scene runtime bootstrap.
        /// </summary>
        public const string ResourceName = "PrometheusRenderingSettings";

        [SerializeField, Tooltip("Desktop Forward pipeline used by the Low quality level.")] private UniversalRenderPipelineAsset pcForwardLowPipelineAsset;
        [SerializeField, Tooltip("Desktop Forward pipeline used by the Mid quality level.")] private UniversalRenderPipelineAsset pcForwardMidPipelineAsset;
        [SerializeField, Tooltip("Desktop Deferred pipeline used by the Low quality level.")] private UniversalRenderPipelineAsset pcDeferredLowPipelineAsset;
        [SerializeField, Tooltip("Desktop Deferred pipeline used by the Mid quality level.")] private UniversalRenderPipelineAsset pcDeferredMidPipelineAsset;
        [SerializeField, Tooltip("Mobile Forward pipeline used by the Low quality level.")] private UniversalRenderPipelineAsset mobileForwardLowPipelineAsset;
        [SerializeField, Tooltip("Mobile Forward pipeline used by the Mid quality level.")] private UniversalRenderPipelineAsset mobileForwardMidPipelineAsset;
        [SerializeField, Tooltip("Default environment profile used by day, night, and season controllers.")] private PrometheusEnvironmentProfile defaultEnvironmentProfile;
        [SerializeField, Tooltip("Desktop renderer path selected before a user preference applies another path.")] private PrometheusRenderPath startupPcRenderPath = PrometheusRenderPath.Deferred;
        [SerializeField, Tooltip("Desktop quality selected before a user preference applies another level.")] private PrometheusRenderQualityLevel startupPcQualityLevel = PrometheusRenderQualityLevel.Mid;
        [SerializeField, Tooltip("Mobile quality selected before a user preference applies another level.")] private PrometheusRenderQualityLevel startupMobileQualityLevel = PrometheusRenderQualityLevel.Mid;
        [SerializeField, Tooltip("Keeps the camera depth texture available for soft particles and depth-aware effects.")] private bool requiresCameraDepthTexture = true;
        [SerializeField, Tooltip("Keeps the camera opaque texture available for refraction effects.")] private bool requiresCameraOpaqueTexture = true;
        [SerializeField, Tooltip("Enables the Scriptable Render Pipeline batcher for compatible project shaders.")] private bool useSrpBatcher = true;
        [SerializeField, Tooltip("Enables Unity dynamic batching in addition to the SRP batcher.")] private bool supportsDynamicBatching;
        [SerializeField, Tooltip("Allows Adaptive Performance to change URP settings outside the project quality controller.")] private bool useAdaptivePerformance;
        [SerializeField, Tooltip("Enables Shiny screen-space reflections when the runtime rendering controller initializes.")] private bool screenSpaceReflectionsEnabledByDefault = true;
        [SerializeField, Tooltip("Keeps the main directional light on per-pixel Lit evaluation for every runtime quality profile.")] private LightRenderingMode mainLightRenderingMode = LightRenderingMode.PerPixel;
        [SerializeField, Tooltip("Keeps main-light shadow variants available while runtime profiles control their cost.")] private bool supportsMainLightShadows = true;
        [SerializeField, Tooltip("Keeps additional realtime lights on per-pixel Lit evaluation for every runtime quality profile.")] private LightRenderingMode additionalLightsRenderingMode = LightRenderingMode.PerPixel;
        [SerializeField, Tooltip("Keeps additional-light shadow variants available while project light systems decide which lights cast shadows.")] private bool supportsAdditionalLightShadows = true;
        [SerializeField, Tooltip("Keeps soft-shadow variants available so runtime profiles can choose hard or soft light shadows.")] private bool supportsSoftShadows = true;
        [SerializeField, Tooltip("Complete project-owned quality profiles.")] private PrometheusRenderQualityProfile[] qualityProfiles = Array.Empty<PrometheusRenderQualityProfile>();

        /// <summary>
        /// Gets the desktop Deferred Mid pipeline retained as the compatibility view for editor tooling that requires one representative pipeline.
        /// </summary>
        public UniversalRenderPipelineAsset PipelineAsset => pcDeferredMidPipelineAsset;

        /// <summary>
        /// Gets the desktop renderer path selected at startup.
        /// </summary>
        public PrometheusRenderPath StartupPcRenderPath => startupPcRenderPath;

        /// <summary>
        /// Gets the default day, night, and season environment profile.
        /// </summary>
        public PrometheusEnvironmentProfile DefaultEnvironmentProfile => defaultEnvironmentProfile;

        /// <summary>
        /// Gets the quality level applied during the runtime rendering bootstrap.
        /// </summary>
        public PrometheusRenderQualityLevel StartupQualityLevel => GetStartupQualityLevel(Application.isMobilePlatform ? PrometheusRenderPlatform.Mobile : PrometheusRenderPlatform.Pc);

        /// <summary>
        /// Gets whether every runtime quality profile requires the camera depth texture.
        /// </summary>
        public bool RequiresCameraDepthTexture => requiresCameraDepthTexture;

        /// <summary>
        /// Gets whether every runtime quality profile requires the camera opaque texture.
        /// </summary>
        public bool RequiresCameraOpaqueTexture => requiresCameraOpaqueTexture;

        /// <summary>
        /// Gets whether the runtime pipeline enables the SRP batcher.
        /// </summary>
        public bool UseSrpBatcher => useSrpBatcher;

        /// <summary>
        /// Gets whether the runtime pipeline enables dynamic batching.
        /// </summary>
        public bool SupportsDynamicBatching => supportsDynamicBatching;

        /// <summary>
        /// Gets whether Adaptive Performance may modify URP configuration independently.
        /// </summary>
        public bool UseAdaptivePerformance => useAdaptivePerformance;

        /// <summary>
        /// Gets whether the project-owned Shiny SSR master switch starts enabled for a new runtime session.
        /// </summary>
        public bool ScreenSpaceReflectionsEnabledByDefault => screenSpaceReflectionsEnabledByDefault;

        /// <summary>
        /// Gets the enabled-profile main-light evaluation mode serialized into Mid pipeline assets.
        /// </summary>
        public LightRenderingMode MainLightRenderingMode => mainLightRenderingMode;

        /// <summary>
        /// Gets whether profiles with realtime shadows include main-light shadow support.
        /// </summary>
        public bool SupportsMainLightShadows => supportsMainLightShadows;

        /// <summary>
        /// Gets the enabled-profile additional-light evaluation mode serialized into Mid pipeline assets.
        /// </summary>
        public LightRenderingMode AdditionalLightsRenderingMode => additionalLightsRenderingMode;

        /// <summary>
        /// Gets whether profiles with realtime shadows may include additional-light shadow support.
        /// </summary>
        public bool SupportsAdditionalLightShadows => supportsAdditionalLightShadows;

        /// <summary>
        /// Gets whether applicable Mid pipeline assets include soft-shadow shader variants.
        /// </summary>
        public bool SupportsSoftShadows => supportsSoftShadows;

        /// <summary>
        /// Gets all project-owned rendering quality profiles.
        /// </summary>
        public IReadOnlyList<PrometheusRenderQualityProfile> QualityProfiles => qualityProfiles;

        /// <summary>
        /// Returns the profile whose project-owned identifier matches the requested level.
        /// </summary>
        public PrometheusRenderQualityProfile GetQualityProfile(PrometheusRenderPlatform platform, PrometheusRenderQualityLevel qualityLevel)
        {
            foreach (PrometheusRenderQualityProfile profile in qualityProfiles)
            {
                if (profile.Platform == platform && profile.QualityLevel == qualityLevel)
                {
                    return profile;
                }
            }

            throw new InvalidOperationException($"Rendering settings '{name}' do not define platform '{platform}' quality level '{qualityLevel}'.");
        }

        /// <summary>
        /// Returns the profile for the current runtime platform.
        /// </summary>
        public PrometheusRenderQualityProfile GetQualityProfile(PrometheusRenderQualityLevel qualityLevel)
        {
            return GetQualityProfile(Application.isMobilePlatform ? PrometheusRenderPlatform.Mobile : PrometheusRenderPlatform.Pc, qualityLevel);
        }

        /// <summary>
        /// Returns the startup quality configured for one hardware family.
        /// </summary>
        public PrometheusRenderQualityLevel GetStartupQualityLevel(PrometheusRenderPlatform platform)
        {
            return platform switch { PrometheusRenderPlatform.Pc => startupPcQualityLevel, PrometheusRenderPlatform.Mobile => startupMobileQualityLevel, _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported rendering platform.") };
        }

        /// <summary>
        /// Resolves the exact immutable URP asset for one supported platform, renderer path, and quality combination.
        /// </summary>
        public UniversalRenderPipelineAsset GetPipelineAsset(PrometheusRenderPlatform platform, PrometheusRenderPath renderPath, PrometheusRenderQualityLevel qualityLevel)
        {
            if (platform == PrometheusRenderPlatform.Mobile)
            {
                if (renderPath != PrometheusRenderPath.Forward) throw new InvalidOperationException("Mobile rendering supports only the Forward path.");
                return qualityLevel switch { PrometheusRenderQualityLevel.Low => mobileForwardLowPipelineAsset, PrometheusRenderQualityLevel.Mid => mobileForwardMidPipelineAsset, _ => throw new ArgumentOutOfRangeException(nameof(qualityLevel), qualityLevel, "Unsupported rendering quality level.") };
            }

            if (platform != PrometheusRenderPlatform.Pc) throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported rendering platform.");
            if (renderPath == PrometheusRenderPath.Forward) return qualityLevel switch { PrometheusRenderQualityLevel.Low => pcForwardLowPipelineAsset, PrometheusRenderQualityLevel.Mid => pcForwardMidPipelineAsset, _ => throw new ArgumentOutOfRangeException(nameof(qualityLevel), qualityLevel, "Unsupported rendering quality level.") };
            if (renderPath == PrometheusRenderPath.Deferred) return qualityLevel switch { PrometheusRenderQualityLevel.Low => pcDeferredLowPipelineAsset, PrometheusRenderQualityLevel.Mid => pcDeferredMidPipelineAsset, _ => throw new ArgumentOutOfRangeException(nameof(qualityLevel), qualityLevel, "Unsupported rendering quality level.") };
            throw new ArgumentOutOfRangeException(nameof(renderPath), renderPath, "Unsupported rendering path.");
        }

        /// <summary>
        /// Assigns generated project assets and invariant pipeline requirements without replacing authored quality values.
        /// </summary>
        internal void ConfigureAssets(UniversalRenderPipelineAsset configuredPcForwardLowPipelineAsset, UniversalRenderPipelineAsset configuredPcForwardMidPipelineAsset, UniversalRenderPipelineAsset configuredPcDeferredLowPipelineAsset, UniversalRenderPipelineAsset configuredPcDeferredMidPipelineAsset, UniversalRenderPipelineAsset configuredMobileForwardLowPipelineAsset, UniversalRenderPipelineAsset configuredMobileForwardMidPipelineAsset, PrometheusEnvironmentProfile configuredEnvironmentProfile)
        {
            pcForwardLowPipelineAsset = configuredPcForwardLowPipelineAsset;
            pcForwardMidPipelineAsset = configuredPcForwardMidPipelineAsset;
            pcDeferredLowPipelineAsset = configuredPcDeferredLowPipelineAsset;
            pcDeferredMidPipelineAsset = configuredPcDeferredMidPipelineAsset;
            mobileForwardLowPipelineAsset = configuredMobileForwardLowPipelineAsset;
            mobileForwardMidPipelineAsset = configuredMobileForwardMidPipelineAsset;
            defaultEnvironmentProfile = configuredEnvironmentProfile;
        }

        /// <summary>
        /// Writes the initial complete quality profile set when the settings asset is first generated.
        /// </summary>
        internal void InitializeQualityProfiles(PrometheusRenderQualityProfile[] initialQualityProfiles)
        {
            qualityProfiles = initialQualityProfiles;
        }
    }
}
