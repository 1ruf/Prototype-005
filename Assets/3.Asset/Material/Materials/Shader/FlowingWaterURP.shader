Shader "Prototype/URP/Flowing Water"
{
    Properties
    {
        [MainTexture] _BaseMap("Water Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Water Color", Color) = (0.05, 0.42, 0.62, 0.62)
        [Normal] _NormalMap("Flow Normal Map", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0, 2)) = 0.65
        _NormalTiling("Normal Tiling", Range(0.1, 20)) = 3
        _FlowDirection("Flow Direction (X, Y)", Vector) = (0, -1, 0, 0)
        _FlowSpeed("Flow Speed", Range(0, 5)) = 0.8
        _FlowDistortion("Flow Distortion", Range(0, 0.2)) = 0.035
        _FresnelColor("Edge Highlight", Color) = (0.45, 0.9, 1, 1)
        _FresnelPower("Edge Highlight Width", Range(0.5, 8)) = 4
        _Smoothness("Smoothness", Range(0, 1)) = 0.82
        _SpecularIntensity("Sun Glint", Range(0, 3)) = 1.2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _FresnelColor;
                float4 _FlowDirection;
                half _NormalStrength;
                half _NormalTiling;
                half _FlowSpeed;
                half _FlowDistortion;
                half _FresnelPower;
                half _Smoothness;
                half _SpecularIntensity;
            CBUFFER_END

            TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionWS = positionInputs.positionWS;
                output.positionCS = TransformWorldToHClip(positionInputs.positionWS);
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 tangentWS = input.tangentWS - normalWS * dot(input.tangentWS, normalWS);
                tangentWS = dot(tangentWS, tangentWS) > 0.0001 ? normalize(tangentWS) : normalize(cross(float3(0, 1, 0), normalWS) + float3(0.001, 0, 0));
                half3 bitangentWS = normalize(cross(normalWS, tangentWS));

                float2 flowDir = normalize(_FlowDirection.xy + 0.0001);
                float time = _Time.y * _FlowSpeed;
                float2 normalUV = input.uv * _NormalTiling;
                half3 distortionNormal = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUV * 0.55 + flowDir * time * 0.35));
                float2 distortion = distortionNormal.xy * _FlowDistortion;
                half3 normalA = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUV + flowDir * time + distortion), _NormalStrength);
                half3 normalB = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUV * 1.63 + flowDir * time * 1.42 - distortion), _NormalStrength * 0.7);
                half3 normalTS = normalize(half3(normalA.xy + normalB.xy, normalA.z * normalB.z));
                normalWS = normalize(tangentWS * normalTS.x + bitangentWS * normalTS.y + normalWS * normalTS.z);

                float2 baseUV = input.uv + flowDir * time + distortion;
                half4 water = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV) * _BaseColor;
                half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 diffuse = water.rgb * (ambient + mainLight.color * NdotL * mainLight.distanceAttenuation);

                half3 halfDir = SafeNormalize(mainLight.direction + viewDirWS);
                half specular = pow(saturate(dot(normalWS, halfDir)), exp2(10.0h * _Smoothness + 1.0h)) * _SpecularIntensity;
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
                half3 color = diffuse + mainLight.color * specular + _FresnelColor.rgb * fresnel;
                return half4(color, water.a);
            }
            ENDHLSL
        }
    }
}
