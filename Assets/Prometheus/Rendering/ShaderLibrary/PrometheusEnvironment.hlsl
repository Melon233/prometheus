#ifndef PROMETHEUS_ENVIRONMENT_INCLUDED
#define PROMETHEUS_ENVIRONMENT_INCLUDED

// Project quality level encoded with the PrometheusRenderQualityLevel numeric values.
float _PrometheusQualityLevel;

// Normalized daily time where zero is midnight, 0.25 is sunrise, 0.5 is noon, and 0.75 is sunset.
float _PrometheusTimeOfDay;

// Current season encoded with the PrometheusSeason numeric values.
float _PrometheusSeason;

// Transition progress from the current season to the next ordered season.
float _PrometheusSeasonBlend;

// World-space direction from a shaded surface toward the sun.
float4 _PrometheusSunDirection;

// Directional-light RGB radiance after daily and seasonal intensity evaluation.
float4 _PrometheusSunColor;

// Evaluated Trilight ambient colors shared by custom world and character shaders.
float4 _PrometheusAmbientSkyColor;
float4 _PrometheusAmbientEquatorColor;
float4 _PrometheusAmbientGroundColor;

// Evaluated seasonal material colors used by project shader families.
float4 _PrometheusSeasonGlobalTint;
float4 _PrometheusVegetationTint;
float4 _PrometheusGroundTint;

// Evaluated atmospheric fog color shared with custom fog calculations.
float4 _PrometheusFogColor;

// Returns the project-wide seasonal multiplier for general world albedo.
half3 PrometheusApplyGlobalSeasonTint(half3 albedo)
{
    return albedo * (half3)_PrometheusSeasonGlobalTint.rgb;
}

// Returns the project-wide seasonal multiplier for vegetation albedo.
half3 PrometheusApplyVegetationSeasonTint(half3 albedo)
{
    return albedo * (half3)_PrometheusVegetationTint.rgb;
}

// Returns the project-wide seasonal multiplier for terrain and ground albedo.
half3 PrometheusApplyGroundSeasonTint(half3 albedo)
{
    return albedo * (half3)_PrometheusGroundTint.rgb;
}

#endif
