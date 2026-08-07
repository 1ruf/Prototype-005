#ifndef WORLD_GRID_STYLE_COMMON_INCLUDED
#define WORLD_GRID_STYLE_COMMON_INCLUDED

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half4 _ColorTint;
half _WorldGridSize;
half _WorldTextureScale;
half _TextureMapping;
half _TextureInfluence;
half _ColorSteps;
half _ShadowSteps;
half _NoiseStrength;
half _NoiseScale;
half _Brightness;
half _Contrast;
half _AmbientStrength;
CBUFFER_END

float3 GetWorldGridPosition(float3 positionWS)
{
    float gridSize = max((float)_WorldGridSize, 0.0001);
    return (floor(positionWS / gridSize) + 0.5) * gridSize;
}

half QuantizeValue(half value, half steps)
{
    half safeSteps = max(steps, 1.0h);
    return floor(saturate(value) * safeSteps + 0.5h) / safeSteps;
}

half3 QuantizeColor(half3 color, half steps)
{
    half safeSteps = max(steps, 1.0h);
    return floor(saturate(color) * safeSteps + 0.5h) / safeSteps;
}

half GetWorldHash(float3 positionWS)
{
    return frac(sin(dot(positionWS, float3(12.9898, 78.233, 37.719))) * 43758.5453);
}

half4 SampleTriplanar(float3 positionWS, half3 normalWS)
{
    float3 projection = positionWS / max((float)_WorldTextureScale, 0.0001);
    half3 weights = abs(normalWS);
    weights *= weights;
    weights *= weights;
    weights /= max(weights.x + weights.y + weights.z, 0.0001h);
    half4 xSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, projection.zy * _BaseMap_ST.xy + _BaseMap_ST.zw);
    half4 ySample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, projection.xz * _BaseMap_ST.xy + _BaseMap_ST.zw);
    half4 zSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, projection.xy * _BaseMap_ST.xy + _BaseMap_ST.zw);
    return xSample * weights.x + ySample * weights.y + zSample * weights.z;
}

half4 SampleStyleTexture(float2 uv, float3 gridPositionWS, half3 normalWS)
{
    half4 uvSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    half4 triplanarSample = SampleTriplanar(gridPositionWS, normalWS);
    return lerp(triplanarSample, uvSample, saturate(_TextureMapping));
}

half3 ApplyStyleColor(half3 color, float3 gridPositionWS)
{
    half3 corrected = color * _ColorTint.rgb;
    corrected = (corrected - 0.5h) * _Contrast + 0.5h;
    corrected += _Brightness;
    half noise = (GetWorldHash(gridPositionWS * max(_NoiseScale, 0.0001h)) - 0.5h) * _NoiseStrength;
    corrected += noise;
    return QuantizeColor(corrected, _ColorSteps);
}

#endif
