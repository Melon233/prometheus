using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Defines the continuous daily lighting curves and ordered seasonal palettes used by the world environment.
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Rendering/Environment Profile", fileName = "PrometheusEnvironmentProfile")]
    public sealed class PrometheusEnvironmentProfile : ScriptableObject
    {
        [SerializeField, Tooltip("Sun elevation in degrees over normalized day time where zero is midnight and one wraps to the next midnight.")] private AnimationCurve sunElevation = CreateSunElevationCurve();
        [SerializeField, Tooltip("Base sun azimuth in degrees before the normalized day rotation is added.")] private float sunAzimuthDegrees = -30f;
        [SerializeField, Tooltip("Directional-light color over normalized day time.")] private Gradient sunColor = CreateSunColorGradient();
        [SerializeField, Tooltip("Directional-light intensity over normalized day time.")] private AnimationCurve sunIntensity = CreateSunIntensityCurve();
        [SerializeField, Tooltip("Trilight sky ambient color over normalized day time.")] private Gradient ambientSkyColor = CreateAmbientSkyGradient();
        [SerializeField, Tooltip("Trilight horizon ambient color over normalized day time.")] private Gradient ambientEquatorColor = CreateAmbientEquatorGradient();
        [SerializeField, Tooltip("Trilight ground ambient color over normalized day time.")] private Gradient ambientGroundColor = CreateAmbientGroundGradient();
        [SerializeField, Tooltip("RenderSettings ambient intensity over normalized day time.")] private AnimationCurve ambientIntensity = CreateAmbientIntensityCurve();
        [SerializeField, Tooltip("Reflection-probe and skybox reflection intensity over normalized day time.")] private AnimationCurve reflectionIntensity = CreateReflectionIntensityCurve();
        [SerializeField, Tooltip("Whether the environment controller enables Unity fog.")] private bool fogEnabled = true;
        [SerializeField, Tooltip("Unity fog equation controlled by this environment profile.")] private FogMode fogMode = FogMode.ExponentialSquared;
        [SerializeField, Tooltip("Fog color over normalized day time before seasonal tinting.")] private Gradient fogColor = CreateFogColorGradient();
        [SerializeField, Tooltip("Fog density over normalized day time.")] private AnimationCurve fogDensity = CreateFogDensityCurve();
        [SerializeField, Tooltip("Spring palette and light multipliers.")] private PrometheusSeasonLighting spring = CreateSpringLighting();
        [SerializeField, Tooltip("Summer palette and light multipliers.")] private PrometheusSeasonLighting summer = CreateSummerLighting();
        [SerializeField, Tooltip("Autumn palette and light multipliers.")] private PrometheusSeasonLighting autumn = CreateAutumnLighting();
        [SerializeField, Tooltip("Winter palette and light multipliers.")] private PrometheusSeasonLighting winter = CreateWinterLighting();

        /// <summary>
        /// Gets the daily sun elevation curve.
        /// </summary>
        public AnimationCurve SunElevation => sunElevation;

        /// <summary>
        /// Gets the base directional-light azimuth.
        /// </summary>
        public float SunAzimuthDegrees => sunAzimuthDegrees;

        /// <summary>
        /// Gets the daily directional-light color gradient.
        /// </summary>
        public Gradient SunColor => sunColor;

        /// <summary>
        /// Gets the daily directional-light intensity curve.
        /// </summary>
        public AnimationCurve SunIntensity => sunIntensity;

        /// <summary>
        /// Gets the daily sky ambient color gradient.
        /// </summary>
        public Gradient AmbientSkyColor => ambientSkyColor;

        /// <summary>
        /// Gets the daily horizon ambient color gradient.
        /// </summary>
        public Gradient AmbientEquatorColor => ambientEquatorColor;

        /// <summary>
        /// Gets the daily ground ambient color gradient.
        /// </summary>
        public Gradient AmbientGroundColor => ambientGroundColor;

        /// <summary>
        /// Gets the daily ambient intensity curve.
        /// </summary>
        public AnimationCurve AmbientIntensity => ambientIntensity;

        /// <summary>
        /// Gets the daily reflection intensity curve.
        /// </summary>
        public AnimationCurve ReflectionIntensity => reflectionIntensity;

        /// <summary>
        /// Gets whether Unity fog is enabled by this profile.
        /// </summary>
        public bool FogEnabled => fogEnabled;

        /// <summary>
        /// Gets the Unity fog equation selected by this profile.
        /// </summary>
        public FogMode FogMode => fogMode;

        /// <summary>
        /// Gets the daily fog color gradient.
        /// </summary>
        public Gradient FogColor => fogColor;

        /// <summary>
        /// Gets the daily fog density curve.
        /// </summary>
        public AnimationCurve FogDensity => fogDensity;

        /// <summary>
        /// Evaluates the current season and its ordered transition to the next season.
        /// </summary>
        public PrometheusSeasonState EvaluateSeason(PrometheusSeason season, float transition)
        {
            return PrometheusSeasonState.Lerp(GetSeasonLighting(season), GetSeasonLighting(GetNextSeason(season)), transition);
        }

        /// <summary>
        /// Returns the authored lighting definition for one season.
        /// </summary>
        private PrometheusSeasonLighting GetSeasonLighting(PrometheusSeason season)
        {
            return season switch
            {
                PrometheusSeason.Spring => spring,
                PrometheusSeason.Summer => summer,
                PrometheusSeason.Autumn => autumn,
                PrometheusSeason.Winter => winter,
                _ => throw new ArgumentOutOfRangeException(nameof(season), season, "Unknown Prometheus season.")
            };
        }

        /// <summary>
        /// Returns the next season in the permanent spring, summer, autumn, winter cycle.
        /// </summary>
        private static PrometheusSeason GetNextSeason(PrometheusSeason season)
        {
            return season switch
            {
                PrometheusSeason.Spring => PrometheusSeason.Summer,
                PrometheusSeason.Summer => PrometheusSeason.Autumn,
                PrometheusSeason.Autumn => PrometheusSeason.Winter,
                PrometheusSeason.Winter => PrometheusSeason.Spring,
                _ => throw new ArgumentOutOfRangeException(nameof(season), season, "Unknown Prometheus season.")
            };
        }

        /// <summary>
        /// Creates the default daily solar elevation from midnight through sunrise, noon, sunset, and the next midnight.
        /// </summary>
        private static AnimationCurve CreateSunElevationCurve()
        {
            return new AnimationCurve(new Keyframe(0f, -90f), new Keyframe(0.25f, 0f), new Keyframe(0.5f, 75f), new Keyframe(0.75f, 0f), new Keyframe(1f, -90f));
        }

        /// <summary>
        /// Creates the default directional-light intensity curve with smooth daylight shoulders and a dark night interval.
        /// </summary>
        private static AnimationCurve CreateSunIntensityCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.23f, 0f), new Keyframe(0.3f, 0.65f), new Keyframe(0.5f, 1.2f), new Keyframe(0.7f, 0.65f), new Keyframe(0.77f, 0f), new Keyframe(1f, 0f));
        }

        /// <summary>
        /// Creates the default ambient intensity curve that preserves night readability without flattening daytime light direction.
        /// </summary>
        private static AnimationCurve CreateAmbientIntensityCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.25f, 0.55f), new Keyframe(0.5f, 1f), new Keyframe(0.75f, 0.55f), new Keyframe(1f, 0.3f));
        }

        /// <summary>
        /// Creates the default reflection intensity curve for day and night environment response.
        /// </summary>
        private static AnimationCurve CreateReflectionIntensityCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.25f, 0.45f), new Keyframe(0.5f, 1f), new Keyframe(0.75f, 0.45f), new Keyframe(1f, 0.2f));
        }

        /// <summary>
        /// Creates the default atmospheric fog density curve.
        /// </summary>
        private static AnimationCurve CreateFogDensityCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0.008f), new Keyframe(0.25f, 0.006f), new Keyframe(0.5f, 0.003f), new Keyframe(0.75f, 0.006f), new Keyframe(1f, 0.008f));
        }

        /// <summary>
        /// Creates the default sun color cycle.
        /// </summary>
        private static Gradient CreateSunColorGradient()
        {
            return CreateGradient(new GradientColorKey(new Color(0.18f, 0.24f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.48f, 0.24f), 0.25f), new GradientColorKey(new Color(1f, 0.96f, 0.86f), 0.5f), new GradientColorKey(new Color(1f, 0.38f, 0.2f), 0.75f), new GradientColorKey(new Color(0.18f, 0.24f, 0.45f), 1f));
        }

        /// <summary>
        /// Creates the default sky ambient color cycle.
        /// </summary>
        private static Gradient CreateAmbientSkyGradient()
        {
            return CreateGradient(new GradientColorKey(new Color(0.025f, 0.04f, 0.11f), 0f), new GradientColorKey(new Color(0.35f, 0.18f, 0.2f), 0.25f), new GradientColorKey(new Color(0.58f, 0.7f, 0.9f), 0.5f), new GradientColorKey(new Color(0.32f, 0.15f, 0.2f), 0.75f), new GradientColorKey(new Color(0.025f, 0.04f, 0.11f), 1f));
        }

        /// <summary>
        /// Creates the default horizon ambient color cycle.
        /// </summary>
        private static Gradient CreateAmbientEquatorGradient()
        {
            return CreateGradient(new GradientColorKey(new Color(0.045f, 0.05f, 0.09f), 0f), new GradientColorKey(new Color(0.52f, 0.24f, 0.18f), 0.25f), new GradientColorKey(new Color(0.48f, 0.5f, 0.52f), 0.5f), new GradientColorKey(new Color(0.48f, 0.2f, 0.16f), 0.75f), new GradientColorKey(new Color(0.045f, 0.05f, 0.09f), 1f));
        }

        /// <summary>
        /// Creates the default ground ambient color cycle.
        /// </summary>
        private static Gradient CreateAmbientGroundGradient()
        {
            return CreateGradient(new GradientColorKey(new Color(0.018f, 0.022f, 0.04f), 0f), new GradientColorKey(new Color(0.16f, 0.09f, 0.08f), 0.25f), new GradientColorKey(new Color(0.22f, 0.23f, 0.2f), 0.5f), new GradientColorKey(new Color(0.14f, 0.07f, 0.08f), 0.75f), new GradientColorKey(new Color(0.018f, 0.022f, 0.04f), 1f));
        }

        /// <summary>
        /// Creates the default atmospheric fog color cycle.
        /// </summary>
        private static Gradient CreateFogColorGradient()
        {
            return CreateGradient(new GradientColorKey(new Color(0.025f, 0.035f, 0.075f), 0f), new GradientColorKey(new Color(0.52f, 0.25f, 0.2f), 0.25f), new GradientColorKey(new Color(0.63f, 0.72f, 0.78f), 0.5f), new GradientColorKey(new Color(0.48f, 0.2f, 0.19f), 0.75f), new GradientColorKey(new Color(0.025f, 0.035f, 0.075f), 1f));
        }

        /// <summary>
        /// Builds a gradient with opaque alpha across the entire daily cycle.
        /// </summary>
        private static Gradient CreateGradient(params GradientColorKey[] colorKeys)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        /// <summary>
        /// Creates the default spring palette.
        /// </summary>
        private static PrometheusSeasonLighting CreateSpringLighting()
        {
            return new PrometheusSeasonLighting(new Color(1.02f, 1f, 0.98f), new Color(0.76f, 1f, 0.72f), new Color(0.96f, 0.91f, 0.8f), new Color(1f, 0.95f, 0.93f), 1.02f, 1f);
        }

        /// <summary>
        /// Creates the default summer palette.
        /// </summary>
        private static PrometheusSeasonLighting CreateSummerLighting()
        {
            return new PrometheusSeasonLighting(Color.white, new Color(0.7f, 0.95f, 0.62f), new Color(1f, 0.95f, 0.78f), new Color(0.93f, 0.98f, 1f), 1.05f, 1.05f);
        }

        /// <summary>
        /// Creates the default autumn palette.
        /// </summary>
        private static PrometheusSeasonLighting CreateAutumnLighting()
        {
            return new PrometheusSeasonLighting(new Color(1.05f, 0.93f, 0.8f), new Color(1f, 0.62f, 0.22f), new Color(0.8f, 0.52f, 0.28f), new Color(1f, 0.8f, 0.62f), 0.95f, 0.9f);
        }

        /// <summary>
        /// Creates the default winter palette.
        /// </summary>
        private static PrometheusSeasonLighting CreateWinterLighting()
        {
            return new PrometheusSeasonLighting(new Color(0.82f, 0.9f, 1.08f), new Color(0.62f, 0.7f, 0.66f), new Color(0.9f, 0.95f, 1f), new Color(0.8f, 0.9f, 1f), 0.85f, 0.8f);
        }
    }
}
