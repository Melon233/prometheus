#ifndef POLYART_GERSTNER_WAVES
#define POLYART_GERSTNER_WAVES

#ifndef TWOPI
#define TWOPI 6.28318530718
#endif

#define MAX_WAVES 32

uniform float2 _PlanePivots;

uniform int _WaveCount;
uniform float4 _WaveData[MAX_WAVES]; // wavelength, height, offset
uniform float4 _WindDirection;
uniform float4 _FlowPivot;

float2 Rotate2D(float2 uv, float rotation01)
{
    float angle = rotation01 * TWOPI;
    float s = sin(angle);
    float c = cos(angle);
    float2x2 rotMatrix = float2x2(c, -s, s, c);
    return mul(rotMatrix, uv);
}

float Hash01(int x)
{
    x ^= x >> 13;
    x *= 1103515245;
    return frac(x * (1.0 / 4294967296.0)); // 1 / 2^32
}

inline void CalculateGerstnerWaves_float(in float2 worldPos, in float time, out float OutOffset, out float3 OutWaveNormals)
{
    OutOffset = 0;
    OutWaveNormals = float3(0, 0, 1);
    OutWaveNormals = float3(0, 0, 1);
    
    float3 tangent = float3(1, 0, 0);
    float3 binormal = float3(0, 0, 1);

    const float gravity = 9.8;
    
    float2 directionalGradients = Rotate2D(worldPos, atan2(_WindDirection.x, _WindDirection.y) / TWOPI);

    for (int i = 0; i < _WaveCount; ++i)
    {       
        float randomFloat = Hash01(i);        

        float3 direction;        
        if (_FlowPivot.z > 0)
        {
            direction = float3(_FlowPivot.xy - worldPos.xy, 0);
        }
        else
        {
            direction = float3(_WindDirection.xy, 0);
        }
        
        float wavelength = _WaveData[i].x;
        float amplitude = _WaveData[i].y;
        float offset = _WaveData[i].z;
        
        float dividedWavelength = TWOPI / wavelength;

        float waveSpeed = sqrt(dividedWavelength * gravity) * time;
        
        float dott; // This is the gradience for the cos
        
        float timeOffset;
        
        if (_FlowPivot.z > 0)
        {
            float distance = length(direction);
            float2 radialUVs = direction.xy;
            float angle = randomFloat * 6.283185;
            float s = sin(angle);
            float c = cos(angle);
            float2x2 rotMatrix = float2x2(c, -s, s, c);
            radialUVs = mul(rotMatrix, radialUVs);
            float radialGradient = frac(atan2(radialUVs.x, radialUVs.y) / 6.283185);
            radialGradient = cos(radialGradient * floor(randomFloat * 5 + 11) * 6.283185);
            timeOffset = radialGradient * wavelength / 3;
            dott = (distance + timeOffset) * -dividedWavelength;

        }
        else
        {
            // Apply the same angle deviation to the timeOffset calculations
            float2 rotatedGradients;
            float deviationAngle = 10 * 0.0174533;
            float randomDeviation = (randomFloat * 2.0 - 1.0) * deviationAngle;
        
            float s = sin(randomDeviation);
            float c = cos(randomDeviation);
            float2x2 rotMatrix = float2x2(c, -s, s, c);
            rotatedGradients = mul(rotMatrix, directionalGradients.xy);

            timeOffset = sin(rotatedGradients.x / offset / (randomFloat + 1) + (sin(randomFloat) * 100)) *
                    sin(rotatedGradients.x / offset / (frac(randomFloat * 3.756) * 5.17 + 1) + (sin(randomFloat * 3.456) * 297)) * wavelength / 3;
        
            timeOffset *= 1 + (cos(rotatedGradients.y / 5000 / (randomFloat + 1) + (cos(randomFloat) * 200) + (time * 0.1)) *
                          cos(rotatedGradients.y / 17100 / (frac(randomFloat * 3.456) + 1) + (cos(randomFloat * 3.456) * 327)) * 0.25);
        
            //dott = (rotatedGradients.x) * dividedWavelength;
            dott = (rotatedGradients.y + timeOffset) * dividedWavelength;   
            
            waveSpeed *= -1;
        }
        
        dott -= waveSpeed;
        
        float cosine = cos(dott) * amplitude;;
        float sinee = sin(dott);
        
        OutOffset += cosine;
        
        float steepnessMul = dividedWavelength * amplitude;        
        
        float cosdd = cosine * steepnessMul;
        float sindd = sinee * steepnessMul;
        
        float3 sindd3 = sindd * normalize(direction);
        
        OutWaveNormals.xy += sindd3.xy;
    }
    
    //OutWaveNormals.y = 1 - OutWaveNormals.y;
    
    OutWaveNormals = normalize(float3(OutWaveNormals.x, -OutWaveNormals.y, 1));
    
    if (!_FlowPivot.z > 0)
        OutWaveNormals = float3(OutWaveNormals.x, OutWaveNormals.y, OutWaveNormals.z);
}

#endif // POLYART_GERSTNER_WAVES


