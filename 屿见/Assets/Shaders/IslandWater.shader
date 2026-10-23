Shader "Yujian/Island Water"
{
    Properties
    {
        [Header(Water Colors)]
        _ShallowColor ("Shallow Color", Color) = (0.45, 0.85, 0.85, 1)
        _DeepColor ("Deep Color", Color) = (0.12, 0.42, 0.55, 1)
        _FoamColor ("Foam Color", Color) = (0.94, 0.98, 1, 1)

        [Header(Depth And Alpha)]
        _DepthRange ("Depth Range (world units)", Float) = 3.0
        _ShallowAlpha ("Shallow Alpha", Range(0, 1)) = 0.35
        _DeepAlpha ("Deep Alpha", Range(0, 1)) = 0.92

        [Header(Shore Foam)]
        _ShoreFoamDistance ("Shore Foam Distance (world units)", Float) = 0.30
        _FoamStrength ("Foam Strength", Range(0, 1)) = 0.85
        _FoamNoiseScale ("Foam Noise Scale", Float) = 1.2
        _FoamPulseSpeed ("Foam Pulse Speed", Float) = 1.1

        [Header(Open Water Waves)]
        _WaveScale ("Wave Scale", Float) = 0.08
        _WaveSpeed ("Wave Speed", Float) = 0.15
        _WaveStrength ("Wave Strength", Range(0, 1)) = 0.20
        _WaveStartDepth ("Wave Start Depth (world units)", Float) = 0.4

        [Header(Noise Patches)]
        _PatchColor ("Patch Color", Color) = (0.28, 0.70, 0.72, 1)
        _PatchScale ("Patch Scale", Float) = 0.035
        _PatchSpeed ("Patch Speed", Float) = 0.06
        _PatchStrength ("Patch Strength", Range(0, 1)) = 0.35

        [Header(Noise Streaks)]
        _StreakScale ("Streak Scale", Float) = 0.015
        _StreakSpeed ("Streak Speed", Float) = 0.06
        _StreakWarp ("Streak Warp", Float) = 0.8
        _StreakWidth ("Streak Width", Range(0.01, 0.5)) = 0.06
        _StreakStrength ("Streak Strength", Range(0, 1)) = 0.15

        [Header(Surface Sparkles)]
        _SparkleScale ("Sparkle Scale", Float) = 1.4
        _SparkleSpeed ("Sparkle Speed", Float) = 0.08
        _SparkleThreshold ("Sparkle Threshold", Range(0.3, 0.95)) = 0.58
        _SparkleStrength ("Sparkle Strength", Range(0, 1)) = 0.35

        [Header(Dynamic Shoreline)]
        _TideAmplitude ("Tide Amplitude (world units)", Float) = 0.18
        _TideSpeed ("Tide Speed", Float) = 0.5
        _TideSpatialScale ("Tide Spatial Scale", Float) = 0.02
        _SurfZoneDepth ("Surf Zone Depth (world units)", Float) = 1.2
        _SurfWaveRepeat ("Surf Wave Repeat", Float) = 3.0
        _SurfWaveSpeed ("Surf Wave Speed", Float) = 0.35
        _SurfWaveWidth ("Surf Wave Width", Range(0.01, 0.5)) = 0.08
        _SurfWaveStrength ("Surf Wave Strength", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "IslandWaterForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            // 只渲染一次三角形；关掉背面剔除，避免 Plane 朝向变化导致整片水消失。
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float  _DepthRange;
                float  _ShallowAlpha;
                float  _DeepAlpha;
                float  _ShoreFoamDistance;
                float  _FoamStrength;
                float  _FoamNoiseScale;
                float  _FoamPulseSpeed;
                float  _WaveScale;
                float  _WaveSpeed;
                float  _WaveStrength;
                float  _WaveStartDepth;
                float4 _PatchColor;
                float  _PatchScale;
                float  _PatchSpeed;
                float  _PatchStrength;
                float  _StreakScale;
                float  _StreakSpeed;
                float  _StreakWarp;
                float  _StreakWidth;
                float  _StreakStrength;
                float  _SparkleScale;
                float  _SparkleSpeed;
                float  _SparkleThreshold;
                float  _SparkleStrength;
                float  _TideAmplitude;
                float  _TideSpeed;
                float  _TideSpatialScale;
                float  _SurfZoneDepth;
                float  _SurfWaveRepeat;
                float  _SurfWaveSpeed;
                float  _SurfWaveWidth;
                float  _SurfWaveStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldXZ    : TEXCOORD0;
                float  viewDepth  : TEXCOORD1;
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

            float Fbm3(float2 p)
            {
                float n = ValueNoise(p) * 0.5;
                n += ValueNoise(p * 2.03 + 17.13) * 0.25;
                n += ValueNoise(p * 4.01 + 41.70) * 0.125;
                return n / 0.875;
            }

            // 大尺度色斑只需要两层，省一次噪声采样。
            float Fbm2(float2 p)
            {
                float n = ValueNoise(p) * 0.5;
                n += ValueNoise(p * 2.03 + 17.13) * 0.25;
                return n / 0.75;
            }

            // 二维哈希，给双相位色斑生成互不相关的随机偏移。
            float2 Hash22(float2 p)
            {
                return float2(Hash21(p), Hash21(p + 19.19));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // 注意：wp 只有 x/y 两个分量，wp.x = 世界 X，wp.y = 世界 Z。
                float2 wp = positionWS.xz;

                // 潮汐：低频空间波 + 一个更慢的全局分量，让南北岸不同步。
                float tide = sin(_Time.y * _TideSpeed
                    + wp.x * _TideSpatialScale
                    + wp.y * _TideSpatialScale * 0.7) * _TideAmplitude;
                tide += sin(_Time.y * _TideSpeed * 0.37 + 1.7) * _TideAmplitude * 0.45;
                positionWS.y += tide;

                // 位移后必须重算裁剪空间位置与视深度，否则水线不会真的动。
                output.positionCS = TransformWorldToHClip(positionWS);
                output.worldXZ = positionWS.xz;
                output.viewDepth = -TransformWorldToView(positionWS).z;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterDepth = max(0.0, sceneEyeDepth - input.viewDepth);

                float depthT = saturate(waterDepth / max(_DepthRange, 0.001));
                float3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
                float alpha = lerp(_ShallowAlpha, _DeepAlpha, depthT);

                float t = _Time.y;
                float2 wp = input.worldXZ;

                // 大尺度色斑：两个噪声相位交叉淡化，斑块缓慢“生灭”，不会整片平移。
                float patchTime = t * _PatchSpeed;
                float patchFrac = frac(patchTime);
                // A = 当前周期，B = 下一周期；f 从 0→1 时 A 淡出、B 淡入，
                // 下个周期 B 变成新的 A，因此交界处连续。
                float patchBlend = smoothstep(0.0, 1.0, patchFrac);
                float2 patchSeed = floor(float2(patchTime, patchTime));
                float2 patchOffA = Hash22(patchSeed) * 113.0;
                float2 patchOffB = Hash22(patchSeed + 1.0) * 113.0;
                float2 patchDrift = float2(t * 0.02, -t * 0.013); // 周期内也缓慢漂移
                float patchA = Fbm2(wp * _PatchScale + patchOffA + patchDrift);
                float patchB = Fbm2(wp * _PatchScale + patchOffB + patchDrift);
                float patchNoise = lerp(patchA, patchB, patchBlend);
                float patchMask = smoothstep(0.45, 0.75, patchNoise) * _PatchStrength;
                patchMask *= smoothstep(0.05, 0.45, depthT); // 浅水区保持干净
                color = lerp(color, _PatchColor.rgb, patchMask);

                // 岸线泡沫：水深很浅的一圈，边缘用世界噪声打碎并缓慢脉动。
                float shoreNoise = Fbm3(wp * _FoamNoiseScale + float2(t * 0.10, -t * 0.07));
                float band = _ShoreFoamDistance * (0.55 + 0.90 * shoreNoise);
                float shoreFoam = 1.0 - smoothstep(0.0, max(band, 0.0001), waterDepth);
                float pulse = 0.75 + 0.25 * sin(t * _FoamPulseSpeed + shoreNoise * 6.28318);
                float foam = saturate(shoreFoam * pulse * _FoamStrength);

                // 深水浪线：低频正弦 + 噪声扰动，只在水深足够处出现。
                float waveNoise = Fbm3(wp * _WaveScale * 1.7 + float2(t * 0.04, t * 0.02));
                float wave = sin(dot(wp, float2(1.0, 0.65)) * _WaveScale * 6.28318
                    + t * _WaveSpeed + waveNoise * 5.0);
                float crest = smoothstep(0.90, 0.995, wave);
                crest *= smoothstep(_WaveStartDepth, _WaveStartDepth + 0.8, waterDepth)
                    * _WaveStrength;

                // 整片水面的噪声流线：用低频噪声扭曲 phase，形成不规则的白色水纹。
                float streakPhase = frac(dot(wp, float2(0.9, 0.45)) * _StreakScale
                    + waveNoise * _StreakWarp
                    + t * _StreakSpeed);
                float streakLine = 1.0 - smoothstep(0.0, max(_StreakWidth, 0.0001),
                    abs(streakPhase - 0.5));
                streakLine *= smoothstep(_WaveStartDepth, _WaveStartDepth + 0.8, waterDepth);
                streakLine *= _StreakStrength;

                // 冲浪区浪线：在“离岸程度”坐标上滚动 frac，形成向岸推进的白色浪线。
                float surfZone = 1.0 - saturate(waterDepth / max(_SurfZoneDepth, 0.001));
                float lineNoise = Fbm3(wp * 0.35 + float2(t * 0.05, 0.0));
                float phase = frac(surfZone * _SurfWaveRepeat
                    - t * _SurfWaveSpeed + lineNoise * 0.3);
                float surfLine = 1.0 - smoothstep(0.0,
                    max(_SurfWaveWidth, 0.0001), abs(phase - 0.5));
                surfLine *= smoothstep(0.0, 0.12, surfZone)
                    * (1.0 - smoothstep(0.85, 1.0, surfZone));
                surfLine *= _SurfWaveStrength;

                // 水面碎光：两层高频噪声相乘取峰值，只在较深水域出现。
                float2 sparkleFlow = float2(t * _SparkleSpeed, -t * _SparkleSpeed * 0.7);
                float glintA = ValueNoise(wp * _SparkleScale + sparkleFlow);
                float glintB = ValueNoise(wp * _SparkleScale * 2.3 - sparkleFlow * 1.6 + 13.1);
                float glint = glintA * glintB;
                float sparkle = smoothstep(_SparkleThreshold, _SparkleThreshold + 0.12, glint);
                // 每个亮点的相位不同，形成闪烁而不是整体平移。
                sparkle *= 0.65 + 0.35 * sin(t * 2.0 + glint * 40.0);
                sparkle *= smoothstep(0.25, 0.9, depthT) * _SparkleStrength;

                float totalFoam = saturate(foam + crest + streakLine + surfLine + sparkle);
                color = lerp(color, _FoamColor.rgb, totalFoam);
                alpha = saturate(alpha + foam * 0.65 + crest * 0.25 + streakLine * 0.15
                    + surfLine * 0.5 + sparkle * 0.45);

                color = MixFog(color, input.fogFactor);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
