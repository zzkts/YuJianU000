Shader "Yujian/Island Terrain"
{
    Properties
    {
        [Header(Colors)]
        _GrassColor ("Grass Color", Color) = (0.553, 0.733, 0.388, 1)
        _SandColor ("Sand Color", Color) = (0.902, 0.827, 0.639, 1)
        _WetSandColor ("Wet Sand Color", Color) = (0.58, 0.50, 0.38, 1)
        _SeabedColor ("Seabed Color", Color) = (0.74, 0.68, 0.52, 1)
        _RockColor ("Rock Color", Color) = (0.639, 0.612, 0.549, 1)

        [Header(Height Blend)]
        // 以海平面为 0 点：水线附近是湿沙，向上平滑过渡到干沙、再到草地。
        _WaterLevel ("Water Level (world Y)", Float) = -1.4
        _GrassStart ("Grass Start (above water)", Float) = 0.6
        _GrassBlend ("Grass Blend Width", Float) = 0.7
        _WetBand ("Wet Sand Half Band", Float) = 0.3
        _WetStrength ("Wet Sand Strength", Range(0, 1)) = 0.35

        [Header(Slope Rock)]
        _RockSlopeStart ("Rock Slope Start", Range(0, 1)) = 0.35
        _RockSlopeEnd ("Rock Slope End", Range(0, 1)) = 0.7

        [Header(Breakup)]
        _NoiseScale ("Noise Scale", Float) = 0.3
        _NoiseStrength ("Noise Strength (world units)", Float) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassColor;
                float4 _SandColor;
                float4 _WetSandColor;
                float4 _SeabedColor;
                float4 _RockColor;
                float  _WaterLevel;
                float  _GrassStart;
                float  _GrassBlend;
                float  _WetBand;
                float  _WetStrength;
                float  _RockSlopeStart;
                float  _RockSlopeEnd;
                float  _NoiseScale;
                float  _NoiseStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm2(float2 p)
            {
                float n = ValueNoise(p) * 0.5;
                n += ValueNoise(p * 2.03 + 17.13) * 0.25;
                return n / 0.75;
            }

            // 世界高度驱动的连续配色：湿沙 → 干沙 → 草地，陡坡再混入岩石色。
            // 噪声只扰动高度阈值，让色带边缘呈自然的不规则曲线。
            float3 TerrainColor(float3 positionWS, float3 normalWS)
            {
                float2 wp = positionWS.xz;
                float noise = (Fbm2(wp * _NoiseScale + 37.1) - 0.5) * 2.0;
                float h = positionWS.y - _WaterLevel + noise * _NoiseStrength;

                float3 color = lerp(_SeabedColor.rgb, _SandColor.rgb,
                    smoothstep(-1.2, 0.0, h));

                float wet = 1.0 - smoothstep(0.0, max(_WetBand, 0.001), abs(h));
                color = lerp(color, _WetSandColor.rgb, saturate(wet * _WetStrength));

                float grass = smoothstep(_GrassStart,
                    _GrassStart + max(_GrassBlend, 0.001), h);
                color = lerp(color, _GrassColor.rgb, grass);

                float slope = 1.0 - saturate(normalWS.y);
                float rock = smoothstep(_RockSlopeStart,
                    max(_RockSlopeEnd, 0.001), slope);
                color = lerp(color, _RockColor.rgb, rock);

                return color;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs position = GetVertexPositionInputs(
                    input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);

                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 albedo = TerrainColor(input.positionWS, normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = SampleSH(normalWS)
                    + mainLight.color * (ndl * mainLight.shadowAttenuation
                        * mainLight.distanceAttenuation);

                half3 color = albedo * lighting;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // 水面用深度纹理判断岸线泡沫，必须有 DepthOnly；SSAO 需要 DepthNormals。
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                return half4(packedNormalWS, 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
