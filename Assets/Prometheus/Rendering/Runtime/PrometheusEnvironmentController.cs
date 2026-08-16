using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Applies one continuous time of day and season transition to the sun, RenderSettings, and the project shader-global contract.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrometheusEnvironmentController : MonoBehaviour
    {
        private static readonly int TimeOfDayShaderPropertyId = Shader.PropertyToID("_PrometheusTimeOfDay");
        private static readonly int SeasonShaderPropertyId = Shader.PropertyToID("_PrometheusSeason");
        private static readonly int SeasonBlendShaderPropertyId = Shader.PropertyToID("_PrometheusSeasonBlend");
        private static readonly int SunDirectionShaderPropertyId = Shader.PropertyToID("_PrometheusSunDirection");
        private static readonly int SunColorShaderPropertyId = Shader.PropertyToID("_PrometheusSunColor");
        private static readonly int AmbientSkyColorShaderPropertyId = Shader.PropertyToID("_PrometheusAmbientSkyColor");
        private static readonly int AmbientEquatorColorShaderPropertyId = Shader.PropertyToID("_PrometheusAmbientEquatorColor");
        private static readonly int AmbientGroundColorShaderPropertyId = Shader.PropertyToID("_PrometheusAmbientGroundColor");
        private static readonly int SeasonGlobalTintShaderPropertyId = Shader.PropertyToID("_PrometheusSeasonGlobalTint");
        private static readonly int VegetationTintShaderPropertyId = Shader.PropertyToID("_PrometheusVegetationTint");
        private static readonly int GroundTintShaderPropertyId = Shader.PropertyToID("_PrometheusGroundTint");
        private static readonly int FogColorShaderPropertyId = Shader.PropertyToID("_PrometheusFogColor");

        [SerializeField, Tooltip("Authored daily curves and seasonal palettes.")] private PrometheusEnvironmentProfile profile;
        [SerializeField, Tooltip("Directional light controlled as the world sun.")] private Light sun;
        [SerializeField, Range(0f, 1f), Tooltip("Normalized daily time where zero is midnight, 0.25 is sunrise, 0.5 is noon, and 0.75 is sunset.")] private float normalizedTimeOfDay = 0.5f;
        [SerializeField, Tooltip("Season whose values form the start of the current transition.")] private PrometheusSeason currentSeason = PrometheusSeason.Summer;
        [SerializeField, Range(0f, 1f), Tooltip("Transition progress from the current season to the next ordered season.")] private float seasonTransition;
        [SerializeField, Tooltip("Advances normalized time automatically during Play Mode.")] private bool advanceTimeAutomatically;
        [SerializeField, Min(1f), Tooltip("Real seconds required for one complete in-game day when automatic time is enabled.")] private float secondsPerDay = 1200f;

        /// <summary>
        /// Gets the currently applied normalized time of day.
        /// </summary>
        public float NormalizedTimeOfDay => normalizedTimeOfDay;

        /// <summary>
        /// Gets the season at the start of the current seasonal transition.
        /// </summary>
        public PrometheusSeason CurrentSeason => currentSeason;

        /// <summary>
        /// Gets transition progress from the current season to the next ordered season.
        /// </summary>
        public float SeasonTransition => seasonTransition;

        /// <summary>
        /// Validates required references and applies the initial world environment before the first rendered frame.
        /// </summary>
        private void Awake()
        {
            if (profile == null)
            {
                throw new InvalidOperationException($"Environment controller '{name}' must reference a Prometheus environment profile.");
            }

            if (sun == null || sun.type != LightType.Directional)
            {
                throw new InvalidOperationException($"Environment controller '{name}' must reference the world directional light.");
            }

            PrometheusRenderQualityController.QualityChanged += HandleQualityChanged;
            ApplyQualityToSun(PrometheusRenderQualityController.CurrentProfile);
            ApplyEnvironment();
        }

        /// <summary>
        /// Removes the quality subscription when the environment controller leaves the active world.
        /// </summary>
        private void OnDestroy()
        {
            PrometheusRenderQualityController.QualityChanged -= HandleQualityChanged;
        }

        /// <summary>
        /// Advances the daily cycle and reapplies all environment outputs while automatic time is active.
        /// </summary>
        private void Update()
        {
            if (!advanceTimeAutomatically)
            {
                return;
            }

            normalizedTimeOfDay = Mathf.Repeat(normalizedTimeOfDay + Time.deltaTime / secondsPerDay, 1f);
            ApplyEnvironment();
        }

        /// <summary>
        /// Changes daily time and immediately updates the light, RenderSettings, and shader globals.
        /// </summary>
        public void SetTimeOfDay(float timeOfDay)
        {
            normalizedTimeOfDay = Mathf.Repeat(timeOfDay, 1f);
            ApplyEnvironment();
        }

        /// <summary>
        /// Changes the ordered seasonal transition and immediately updates every environment output.
        /// </summary>
        public void SetSeason(PrometheusSeason season, float transition)
        {
            currentSeason = season;
            seasonTransition = Mathf.Clamp01(transition);
            ApplyEnvironment();
        }

        /// <summary>
        /// Evaluates daily and seasonal data and writes one coherent environment state to Unity and project shaders.
        /// </summary>
        [ContextMenu("Apply Environment")]
        public void ApplyEnvironment()
        {
            PrometheusSeasonState seasonState = profile.EvaluateSeason(currentSeason, seasonTransition);
            float sunElevation = profile.SunElevation.Evaluate(normalizedTimeOfDay);
            float sunAzimuth = profile.SunAzimuthDegrees + normalizedTimeOfDay * 360f;
            Color sunColor = MultiplyColor(profile.SunColor.Evaluate(normalizedTimeOfDay), seasonState.GlobalTint);
            float sunIntensity = profile.SunIntensity.Evaluate(normalizedTimeOfDay) * seasonState.SunIntensityMultiplier;
            Color ambientSkyColor = MultiplyColor(profile.AmbientSkyColor.Evaluate(normalizedTimeOfDay), seasonState.GlobalTint);
            Color ambientEquatorColor = MultiplyColor(profile.AmbientEquatorColor.Evaluate(normalizedTimeOfDay), seasonState.GlobalTint);
            Color ambientGroundColor = MultiplyColor(profile.AmbientGroundColor.Evaluate(normalizedTimeOfDay), seasonState.GroundTint);
            Color fogColor = MultiplyColor(profile.FogColor.Evaluate(normalizedTimeOfDay), seasonState.FogTint);
            sun.transform.rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSkyColor;
            RenderSettings.ambientEquatorColor = ambientEquatorColor;
            RenderSettings.ambientGroundColor = ambientGroundColor;
            RenderSettings.ambientIntensity = profile.AmbientIntensity.Evaluate(normalizedTimeOfDay) * seasonState.AmbientIntensityMultiplier;
            RenderSettings.reflectionIntensity = profile.ReflectionIntensity.Evaluate(normalizedTimeOfDay);
            RenderSettings.fog = profile.FogEnabled;
            RenderSettings.fogMode = profile.FogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = profile.FogDensity.Evaluate(normalizedTimeOfDay);
            Shader.SetGlobalFloat(TimeOfDayShaderPropertyId, normalizedTimeOfDay);
            Shader.SetGlobalInteger(SeasonShaderPropertyId, (int)currentSeason);
            Shader.SetGlobalFloat(SeasonBlendShaderPropertyId, seasonTransition);
            Shader.SetGlobalVector(SunDirectionShaderPropertyId, -sun.transform.forward);
            Shader.SetGlobalColor(SunColorShaderPropertyId, new Color(sunColor.r * sunIntensity, sunColor.g * sunIntensity, sunColor.b * sunIntensity, 1f));
            Shader.SetGlobalColor(AmbientSkyColorShaderPropertyId, ambientSkyColor);
            Shader.SetGlobalColor(AmbientEquatorColorShaderPropertyId, ambientEquatorColor);
            Shader.SetGlobalColor(AmbientGroundColorShaderPropertyId, ambientGroundColor);
            Shader.SetGlobalColor(SeasonGlobalTintShaderPropertyId, seasonState.GlobalTint);
            Shader.SetGlobalColor(VegetationTintShaderPropertyId, seasonState.VegetationTint);
            Shader.SetGlobalColor(GroundTintShaderPropertyId, seasonState.GroundTint);
            Shader.SetGlobalColor(FogColorShaderPropertyId, fogColor);
        }

        /// <summary>
        /// Applies the new project quality profile to the world directional light.
        /// </summary>
        private void HandleQualityChanged(PrometheusRenderQualityLevel qualityLevel)
        {
            ApplyQualityToSun(PrometheusRenderQualityController.CurrentProfile);
        }

        /// <summary>
        /// Changes the world sun between disabled, hard, and soft realtime shadows without changing pipeline shader capabilities.
        /// </summary>
        private void ApplyQualityToSun(PrometheusRenderQualityProfile qualityProfile)
        {
            sun.shadows = qualityProfile.MainLightShadows;
        }

        /// <summary>
        /// Multiplies two HDR-capable RGB colors while preserving a fully opaque environment value.
        /// </summary>
        private static Color MultiplyColor(Color left, Color right)
        {
            return new Color(left.r * right.r, left.g * right.g, left.b * right.b, 1f);
        }
    }
}
