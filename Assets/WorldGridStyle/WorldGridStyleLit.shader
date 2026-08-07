Shader "Prototype 014/World Grid Style Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _WorldGridSize("World Grid Size", Range(0.001, 2)) = 0.02
        _WorldTextureScale("World Texture Scale", Range(0.001, 10)) = 0.25
        [Enum(World Triplanar, 0, Mesh UV, 1)] _TextureMapping("Texture Mapping", Float) = 0
        _TextureInfluence("Texture Influence", Range(0, 1)) = 1
        _ColorSteps("Color Quantization Steps", Range(1, 32)) = 8
        _ShadowSteps("Shadow Quantization Steps", Range(1, 16)) = 4
        _NoiseStrength("Noise Strength", Range(0, 0.25)) = 0.015
        _NoiseScale("Noise Scale", Range(0.01, 10)) = 1
        _ColorTint("Color Tint", Color) = (1, 1, 1, 1)
        _Brightness("Brightness", Range(-1, 1)) = 0
        _Contrast("Contrast", Range(0, 2)) = 1
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "SimpleLit" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "WorldGridStyleCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 gridPositionWS = GetWorldGridPosition(input.positionWS);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half4 textureColor = SampleStyleTexture(input.uv, gridPositionWS, normalWS);
                half3 albedo = lerp(_BaseColor.rgb, _BaseColor.rgb * textureColor.rgb, _TextureInfluence);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(gridPositionWS));
                half directLight = saturate(dot(normalWS, mainLight.direction));
                half shadow = QuantizeValue(mainLight.shadowAttenuation, _ShadowSteps);
                half3 lighting = mainLight.color * directLight * shadow;
                lighting += SampleSH(normalWS) * _AmbientStrength;

                InputData inputData = (InputData)0;
                inputData.positionWS = gridPositionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                #if defined(_ADDITIONAL_LIGHTS)
                uint additionalLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(additionalLightCount)
                    Light additionalLight = GetAdditionalLight(lightIndex, gridPositionWS);
                    half additionalLightAmount = saturate(dot(normalWS, additionalLight.direction));
                    lighting += additionalLight.color * additionalLight.distanceAttenuation * additionalLightAmount;
                LIGHT_LOOP_END
                #endif

                half3 finalColor = ApplyStyleColor(albedo * lighting, gridPositionWS);
                return half4(MixFog(finalColor, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
}
