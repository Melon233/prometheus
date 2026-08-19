using System;
using UnityEngine;

namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Stores every runtime-controlled rendering value for one project quality level.
    /// </summary>
    [Serializable]
    public sealed class PrometheusRenderQualityProfile
    {
        [SerializeField, Tooltip("Hardware family that owns this profile.")] private PrometheusRenderPlatform platform;
        [SerializeField, Tooltip("Project-owned quality identifier.")] private PrometheusRenderQualityLevel qualityLevel;
        [SerializeField, Tooltip("Whether realtime main and additional lights are evaluated by the selected pipeline.")] private bool realtimeLightingEnabled = true;
        [SerializeField, Tooltip("Whether cameras render realtime shadows and the world sun casts realtime shadows.")] private bool realtimeShadowsEnabled = true;
        [SerializeField, Tooltip("Whether gameplay cameras execute URP post-processing.")] private bool postProcessingEnabled = true;
        [SerializeField, Tooltip("Whether gameplay cameras apply color dithering after post-processing.")] private bool ditheringEnabled = true;
        [SerializeField, Range(0.5f, 2f), Tooltip("URP internal render resolution scale.")] private float renderScale = 1f;
        [SerializeField, Tooltip("URP MSAA sample count. Supported values are 1, 2, 4, and 8.")] private int msaaSampleCount = 1;
        [SerializeField, Tooltip("Whether the runtime pipeline uses an HDR color buffer.")] private bool supportsHdr = true;
        [SerializeField, Tooltip("Realtime shadow filtering selected for the world directional light.")] private LightShadows mainLightShadows = LightShadows.Soft;
        [SerializeField, Tooltip("Main-light shadow atlas resolution.")] private int mainLightShadowmapResolution = 2048;
        [SerializeField, Min(0f), Tooltip("Maximum camera distance that renders realtime shadows.")] private float shadowDistance = 50f;
        [SerializeField, Range(1, 4), Tooltip("Number of main-light shadow cascades.")] private int shadowCascadeCount = 2;
        [SerializeField, Range(0, 8), Tooltip("Maximum additional realtime lights evaluated per object.")] private int maxAdditionalLightsCount = 4;
        [SerializeField, Tooltip("Additional-light shadow atlas resolution.")] private int additionalLightsShadowmapResolution = 1024;
        [SerializeField, Range(0, 3), Tooltip("Number of highest-resolution texture mip levels skipped globally.")] private int globalTextureMipmapLimit;
        [SerializeField, Min(0.1f), Tooltip("Multiplier used when Unity chooses a mesh LOD.")] private float lodBias = 1f;
        [SerializeField, Min(0), Tooltip("Highest-detail mesh LOD index that Unity is allowed to use.")] private int maximumLodLevel;
        [SerializeField, Tooltip("Global anisotropic texture filtering policy.")] private AnisotropicFiltering anisotropicFiltering = AnisotropicFiltering.Enable;
        [SerializeField, Range(0, 4), Tooltip("Number of vertical blanks per presented frame. Zero disables vertical synchronization.")] private int vSyncCount = 1;
        [SerializeField, Tooltip("Requested application frame rate when vertical synchronization is disabled. Minus one uses the platform default.")] private int targetFrameRate = -1;

        /// <summary>
        /// Gets the hardware family represented by this profile.
        /// </summary>
        public PrometheusRenderPlatform Platform => platform;

        /// <summary>
        /// Gets the project quality identifier represented by this profile.
        /// </summary>
        public PrometheusRenderQualityLevel QualityLevel => qualityLevel;

        /// <summary>
        /// Gets whether the selected pipeline evaluates realtime lighting.
        /// </summary>
        public bool RealtimeLightingEnabled => realtimeLightingEnabled;

        /// <summary>
        /// Gets whether gameplay cameras and the world sun render realtime shadows.
        /// </summary>
        public bool RealtimeShadowsEnabled => realtimeShadowsEnabled;

        /// <summary>
        /// Gets whether gameplay cameras execute post-processing.
        /// </summary>
        public bool PostProcessingEnabled => postProcessingEnabled;

        /// <summary>
        /// Gets whether gameplay cameras apply color dithering.
        /// </summary>
        public bool DitheringEnabled => ditheringEnabled;

        /// <summary>
        /// Gets the internal render resolution scale.
        /// </summary>
        public float RenderScale => renderScale;

        /// <summary>
        /// Gets the URP MSAA sample count.
        /// </summary>
        public int MsaaSampleCount => msaaSampleCount;

        /// <summary>
        /// Gets whether HDR rendering is enabled.
        /// </summary>
        public bool SupportsHdr => supportsHdr;

        /// <summary>
        /// Gets the realtime shadow filtering selected for the world directional light.
        /// </summary>
        public LightShadows MainLightShadows => mainLightShadows;

        /// <summary>
        /// Gets the main-light shadow atlas resolution.
        /// </summary>
        public int MainLightShadowmapResolution => mainLightShadowmapResolution;

        /// <summary>
        /// Gets the maximum realtime shadow distance.
        /// </summary>
        public float ShadowDistance => shadowDistance;

        /// <summary>
        /// Gets the main-light shadow cascade count.
        /// </summary>
        public int ShadowCascadeCount => shadowCascadeCount;

        /// <summary>
        /// Gets the per-object additional-light limit.
        /// </summary>
        public int MaxAdditionalLightsCount => maxAdditionalLightsCount;

        /// <summary>
        /// Gets the additional-light shadow atlas resolution.
        /// </summary>
        public int AdditionalLightsShadowmapResolution => additionalLightsShadowmapResolution;

        /// <summary>
        /// Gets the global texture mipmap limit.
        /// </summary>
        public int GlobalTextureMipmapLimit => globalTextureMipmapLimit;

        /// <summary>
        /// Gets the mesh LOD selection bias.
        /// </summary>
        public float LodBias => lodBias;

        /// <summary>
        /// Gets the highest-detail mesh LOD index Unity may select.
        /// </summary>
        public int MaximumLodLevel => maximumLodLevel;

        /// <summary>
        /// Gets the global anisotropic filtering policy.
        /// </summary>
        public AnisotropicFiltering AnisotropicFiltering => anisotropicFiltering;

        /// <summary>
        /// Gets the vertical synchronization interval.
        /// </summary>
        public int VSyncCount => vSyncCount;

        /// <summary>
        /// Gets the requested application frame rate.
        /// </summary>
        public int TargetFrameRate => targetFrameRate;

        /// <summary>
        /// Creates a complete quality profile for the asset-generation workflow.
        /// </summary>
        internal PrometheusRenderQualityProfile(PrometheusRenderPlatform platform, PrometheusRenderQualityLevel qualityLevel, bool realtimeLightingEnabled, bool realtimeShadowsEnabled, bool postProcessingEnabled, bool ditheringEnabled, float renderScale, int msaaSampleCount, bool supportsHdr, LightShadows mainLightShadows, int mainLightShadowmapResolution, float shadowDistance, int shadowCascadeCount, int maxAdditionalLightsCount, int additionalLightsShadowmapResolution, int globalTextureMipmapLimit, float lodBias, int maximumLodLevel, AnisotropicFiltering anisotropicFiltering, int vSyncCount, int targetFrameRate)
        {
            this.platform = platform;
            this.qualityLevel = qualityLevel;
            this.realtimeLightingEnabled = realtimeLightingEnabled;
            this.realtimeShadowsEnabled = realtimeShadowsEnabled;
            this.postProcessingEnabled = postProcessingEnabled;
            this.ditheringEnabled = ditheringEnabled;
            this.renderScale = renderScale;
            this.msaaSampleCount = msaaSampleCount;
            this.supportsHdr = supportsHdr;
            this.mainLightShadows = mainLightShadows;
            this.mainLightShadowmapResolution = mainLightShadowmapResolution;
            this.shadowDistance = shadowDistance;
            this.shadowCascadeCount = shadowCascadeCount;
            this.maxAdditionalLightsCount = maxAdditionalLightsCount;
            this.additionalLightsShadowmapResolution = additionalLightsShadowmapResolution;
            this.globalTextureMipmapLimit = globalTextureMipmapLimit;
            this.lodBias = lodBias;
            this.maximumLodLevel = maximumLodLevel;
            this.anisotropicFiltering = anisotropicFiltering;
            this.vSyncCount = vSyncCount;
            this.targetFrameRate = targetFrameRate;
        }
    }
}
