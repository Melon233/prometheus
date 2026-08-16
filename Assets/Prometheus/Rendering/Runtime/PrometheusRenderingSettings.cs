using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Defines the single project pipeline asset, project-wide pipeline requirements, startup quality, and runtime quality profiles.
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Rendering/Rendering Settings", fileName = ResourceName)]
    public sealed class PrometheusRenderingSettings : ScriptableObject
    {
        /// <summary>
        /// Resource name used by the pre-scene runtime bootstrap.
        /// </summary>
        public const string ResourceName = "PrometheusRenderingSettings";

        [SerializeField, Tooltip("The only serialized URP asset owned by Prometheus rendering.")] private UniversalRenderPipelineAsset pipelineAsset;
        [SerializeField, Tooltip("Default environment profile used by day, night, and season controllers.")] private PrometheusEnvironmentProfile defaultEnvironmentProfile;
        [SerializeField, Tooltip("Project quality selected before a user preference or hardware policy applies another profile.")] private PrometheusRenderQualityLevel startupQualityLevel = PrometheusRenderQualityLevel.High;
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
        /// Gets the single serialized URP asset used as the immutable source for the runtime pipeline copy.
        /// </summary>
        public UniversalRenderPipelineAsset PipelineAsset => pipelineAsset;

        /// <summary>
        /// Gets the default day, night, and season environment profile.
        /// </summary>
        public PrometheusEnvironmentProfile DefaultEnvironmentProfile => defaultEnvironmentProfile;

        /// <summary>
        /// Gets the quality level applied during the runtime rendering bootstrap.
        /// </summary>
        public PrometheusRenderQualityLevel StartupQualityLevel => startupQualityLevel;

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
        /// Gets the invariant main-light evaluation mode serialized into the single pipeline asset.
        /// </summary>
        public LightRenderingMode MainLightRenderingMode => mainLightRenderingMode;

        /// <summary>
        /// Gets whether the single pipeline asset includes main-light shadow support.
        /// </summary>
        public bool SupportsMainLightShadows => supportsMainLightShadows;

        /// <summary>
        /// Gets the invariant additional-light evaluation mode serialized into the single pipeline asset.
        /// </summary>
        public LightRenderingMode AdditionalLightsRenderingMode => additionalLightsRenderingMode;

        /// <summary>
        /// Gets whether the single pipeline asset includes additional-light shadow support.
        /// </summary>
        public bool SupportsAdditionalLightShadows => supportsAdditionalLightShadows;

        /// <summary>
        /// Gets whether the single pipeline asset includes soft-shadow shader variants.
        /// </summary>
        public bool SupportsSoftShadows => supportsSoftShadows;

        /// <summary>
        /// Gets all project-owned rendering quality profiles.
        /// </summary>
        public IReadOnlyList<PrometheusRenderQualityProfile> QualityProfiles => qualityProfiles;

        /// <summary>
        /// Returns the profile whose project-owned identifier matches the requested level.
        /// </summary>
        public PrometheusRenderQualityProfile GetQualityProfile(PrometheusRenderQualityLevel qualityLevel)
        {
            foreach (PrometheusRenderQualityProfile profile in qualityProfiles)
            {
                if (profile.QualityLevel == qualityLevel)
                {
                    return profile;
                }
            }

            throw new InvalidOperationException($"Rendering settings '{name}' do not define quality level '{qualityLevel}'.");
        }

        /// <summary>
        /// Assigns generated project assets and invariant pipeline requirements without replacing authored quality values.
        /// </summary>
        internal void ConfigureAssets(UniversalRenderPipelineAsset configuredPipelineAsset, PrometheusEnvironmentProfile configuredEnvironmentProfile)
        {
            pipelineAsset = configuredPipelineAsset;
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
