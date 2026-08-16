using System;
using UnityEngine;

namespace Xuan.Prometheus.Rendering
{
    /// <summary>
    /// Stores the environment tint and intensity multipliers authored for one season.
    /// </summary>
    [Serializable]
    public sealed class PrometheusSeasonLighting
    {
        [SerializeField, ColorUsage(false, true), Tooltip("Subtle color multiplier shared by world materials for this season.")] private Color globalTint = Color.white;
        [SerializeField, ColorUsage(false, true), Tooltip("Season color exposed to shaders for vegetation materials.")] private Color vegetationTint = Color.white;
        [SerializeField, ColorUsage(false, true), Tooltip("Season color exposed to shaders for soil, rock, and terrain materials.")] private Color groundTint = Color.white;
        [SerializeField, ColorUsage(false, true), Tooltip("Season multiplier applied to the evaluated fog color.")] private Color fogTint = Color.white;
        [SerializeField, Min(0f), Tooltip("Season multiplier applied to directional-light intensity.")] private float sunIntensityMultiplier = 1f;
        [SerializeField, Min(0f), Tooltip("Season multiplier applied to ambient-light intensity.")] private float ambientIntensityMultiplier = 1f;

        /// <summary>
        /// Gets the world material color multiplier.
        /// </summary>
        public Color GlobalTint => globalTint;

        /// <summary>
        /// Gets the vegetation material color multiplier.
        /// </summary>
        public Color VegetationTint => vegetationTint;

        /// <summary>
        /// Gets the terrain and ground material color multiplier.
        /// </summary>
        public Color GroundTint => groundTint;

        /// <summary>
        /// Gets the fog color multiplier.
        /// </summary>
        public Color FogTint => fogTint;

        /// <summary>
        /// Gets the directional-light intensity multiplier.
        /// </summary>
        public float SunIntensityMultiplier => sunIntensityMultiplier;

        /// <summary>
        /// Gets the ambient-light intensity multiplier.
        /// </summary>
        public float AmbientIntensityMultiplier => ambientIntensityMultiplier;

        /// <summary>
        /// Creates one season definition with explicit material and lighting values.
        /// </summary>
        internal PrometheusSeasonLighting(Color globalTint, Color vegetationTint, Color groundTint, Color fogTint, float sunIntensityMultiplier, float ambientIntensityMultiplier)
        {
            this.globalTint = globalTint;
            this.vegetationTint = vegetationTint;
            this.groundTint = groundTint;
            this.fogTint = fogTint;
            this.sunIntensityMultiplier = sunIntensityMultiplier;
            this.ambientIntensityMultiplier = ambientIntensityMultiplier;
        }
    }

    /// <summary>
    /// Contains the fully interpolated season values applied to RenderSettings, lights, and project shaders.
    /// </summary>
    public readonly struct PrometheusSeasonState
    {
        /// <summary>
        /// Gets the interpolated world material color multiplier.
        /// </summary>
        public Color GlobalTint { get; }

        /// <summary>
        /// Gets the interpolated vegetation color multiplier.
        /// </summary>
        public Color VegetationTint { get; }

        /// <summary>
        /// Gets the interpolated ground color multiplier.
        /// </summary>
        public Color GroundTint { get; }

        /// <summary>
        /// Gets the interpolated fog color multiplier.
        /// </summary>
        public Color FogTint { get; }

        /// <summary>
        /// Gets the interpolated directional-light intensity multiplier.
        /// </summary>
        public float SunIntensityMultiplier { get; }

        /// <summary>
        /// Gets the interpolated ambient-light intensity multiplier.
        /// </summary>
        public float AmbientIntensityMultiplier { get; }

        /// <summary>
        /// Creates one immutable evaluated environment state.
        /// </summary>
        private PrometheusSeasonState(Color globalTint, Color vegetationTint, Color groundTint, Color fogTint, float sunIntensityMultiplier, float ambientIntensityMultiplier)
        {
            GlobalTint = globalTint;
            VegetationTint = vegetationTint;
            GroundTint = groundTint;
            FogTint = fogTint;
            SunIntensityMultiplier = sunIntensityMultiplier;
            AmbientIntensityMultiplier = ambientIntensityMultiplier;
        }

        /// <summary>
        /// Interpolates two authored season definitions without allocating runtime state.
        /// </summary>
        internal static PrometheusSeasonState Lerp(PrometheusSeasonLighting from, PrometheusSeasonLighting to, float transition)
        {
            return new PrometheusSeasonState(Color.Lerp(from.GlobalTint, to.GlobalTint, transition), Color.Lerp(from.VegetationTint, to.VegetationTint, transition), Color.Lerp(from.GroundTint, to.GroundTint, transition), Color.Lerp(from.FogTint, to.FogTint, transition), Mathf.Lerp(from.SunIntensityMultiplier, to.SunIntensityMultiplier, transition), Mathf.Lerp(from.AmbientIntensityMultiplier, to.AmbientIntensityMultiplier, transition));
        }
    }
}
