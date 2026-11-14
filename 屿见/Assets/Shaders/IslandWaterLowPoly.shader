Shader "Yujian/Island Water Low Poly"
{
    Properties
    {
        [Header(Water Colors)]
        _ShallowColor ("Shallow Color", Color) = (0.45, 0.85, 0.85, 1)
        _DeepColor ("Deep Color", Color) = (0.12, 0.42, 0.55, 1)
        _FoamColor ("Foam Color", Color) = (0.94, 0.98, 1, 1)

        [Header(Depth And Alpha)]
        _DepthRange ("Depth Range (world units)", Float) = 3.0
        _ShallowAlpha ("Shallow Alpha", Range(0, 1)) = 0.55
        _DeepAlpha ("Deep Alpha", Range(0, 1)) = 0.98

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

        [Header(Low Poly Waves)]
        _LowPolyWaveHeight ("Wave Height", Float) = 1.2
        _LowPolyWaveScale ("Wave Frequency", Float) = 0.035
        _LowPolyWaveSpeed ("Wave Speed", Float) = 0.5
        _LowPolyWaveDirection ("Wave Direction (XZ)", Vector) = (1, 0.6, 0, 0)

        [Header(Low Poly Facets)]
        _FacetTint ("Per-Face Tint", Range(0, 0.5)) = 0.22
        _FacetShade ("Facet Shading", Range(0, 1)) = 0.85
        _FacetLightDirection ("Facet Light Direction", Vector) = (0.35, 0.8, 0.25, 0)
        // 0 = 完全关闭原海面的色斑 / 流线 / 碎光 / 浪线，只保留低模分面。
        _NoiseStrength ("Residual Noise", Range(0, 1)) = 0

        [Header(Low Poly Triangle Edges)]
        _EdgeStrength ("Edge Strength", Range(0, 1)) = 0
        _EdgeWidth ("Edge Width (pixels)", Range(0.2, 4)) = 1.5
        _EdgeColor ("Edge Color", Color) = (0.90, 0.98, 1, 1)

        [Header(Shore Mask)]
        _ShoreMask ("Shore Mask (R = wave, G = land)", 2D) = "black" {}
        _ShoreMaskCenter ("Shore Mask Center (XZ)", Vector) = (0, 0, 0, 0)
        _ShoreMaskSize ("Shore Mask Size (world units)", Float) = 1
        _ShoreSmooth ("Shore Smooth", Range(0, 1)) = 0
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
            Name "IslandWaterLowPolyForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // 水面要盖住低模沙滩的锯齿边缘，让水线由平滑遮罩决定。
            ZTest Always
            // 和原海面一致：不剔除背面，避免 Plane 朝向变化导致整片水消失。
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_ShoreMask);
            SAMPLER(sampler_ShoreMask);

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
                float  _LowPolyWaveHeight;
                float  _LowPolyWaveScale;
                float  _LowPolyWaveSpeed;
                float4 _LowPolyWaveDirection;
                float  _FacetTint;
                float  _FacetShade;
                float4 _FacetLightDirection;
                float  _NoiseStrength;
                float  _EdgeStrength;
                float  _EdgeWidth;
                float4 _EdgeColor;
                float4 _ShoreMaskCenter;
                float  _ShoreMaskSize;
                float  _ShoreSmooth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                // mesh.uv2（TEXCOORD1）：xy = 三角形重心坐标，z = 面随机值。
                float4 uv2        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 worldXZ    : TEXCOORD0;
                float  viewDepth  : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float3 bary       : TEXCOORD4;
                float  faceRandom : TEXCOORD5;
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

            float Fbm2(float2 p)
            {
                float n = ValueNoise(p) * 0.5;
                n += ValueNoise(p * 2.03 + 17.13) * 0.25;
                return n / 0.75;
            }

            float2 Hash22(float2 p)
            {
                return float2(Hash21(p), Hash21(p + 19.19));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float2 wp = positionWS.xz;

                // 低模起伏：只依赖世界 XZ，拆点后的相邻三角形顶点位移一致，不会裂缝。
                float2 waveDir = _LowPolyWaveDirection.xy;
                float waveDirLength = max(length(waveDir), 1e-4);
                waveDir /= waveDirLength;
                float2 wavePerp = float2(-waveDir.y, waveDir.x);
                float phaseA = dot(wp, waveDir) * _LowPolyWaveScale * 6.2831853
                    + _Time.y * _LowPolyWaveSpeed;
                float phaseB = dot(wp, wavePerp) * _LowPolyWaveScale * 1.73 * 6.2831853
                    - _Time.y * _LowPolyWaveSpeed * 0.63;
                float lowPolyWave = sin(phaseA) * 0.65 + sin(phaseB) * 0.35;

                // 海岸遮罩：R = 浪高衰减（深水 1，水线 0，陆地 0）。
                // 贴图范围外按 1 处理，远处海面不受影响。
                float2 shoreUV = (wp - _ShoreMaskCenter.xy)
                    / max(_ShoreMaskSize, 0.001) + 0.5;
                float shoreInside = step(0.0, shoreUV.x) * step(shoreUV.x, 1.0)
                    * step(0.0, shoreUV.y) * step(shoreUV.y, 1.0);
                float shoreWaveMask = lerp(1.0,
                    SAMPLE_TEXTURE2D_LOD(_ShoreMask, sampler_ShoreMask, shoreUV, 0).r,
                    shoreInside);
                positionWS.y += lowPolyWave * _LowPolyWaveHeight * shoreWaveMask;

                // 潮汐：低频空间波 + 一个更慢的全局分量，让南北岸不同步。
                float tide = sin(_Time.y * _TideSpeed
                    + wp.x * _TideSpatialScale
                    + wp.y * _TideSpatialScale * 0.7) * _TideAmplitude;
                tide += sin(_Time.y * _TideSpeed * 0.37 + 1.7) * _TideAmplitude * 0.45;
                positionWS.y += tide;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.worldXZ = positionWS.xz;
                output.positionWS = positionWS;
                output.viewDepth = -TransformWorldToView(positionWS).z;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.bary = float3(input.uv2.x, input.uv2.y, 1.0 - input.uv2.x - input.uv2.y);
                output.faceRandom = input.color.r;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // 陆地遮罩：G > 0.5 的像素直接丢弃，水面不会盖到岛上。
                float2 shoreUV = (input.worldXZ - _ShoreMaskCenter.xy)
                    / max(_ShoreMaskSize, 0.001) + 0.5;
                float shoreInside = step(0.0, shoreUV.x) * step(shoreUV.x, 1.0)
                    * step(0.0, shoreUV.y) * step(shoreUV.y, 1.0);
                float4 shoreSample = SAMPLE_TEXTURE2D(
                    _ShoreMask, sampler_ShoreMask, shoreUV);
                float landMask = shoreSample.g * shoreInside;
                // 用遮罩的 R（水深）而不是屏幕深度来驱动岸边泡沫，
                // 这样泡沫边缘跟遮罩一样平滑，不会跟着低模沙滩的三角面出现台阶。
                float shoreWaveMask = lerp(1.0, shoreSample.r, shoreInside);
                // 允许水面往沙滩里多盖一点，把低模沙滩的锯齿藏到半透明水下面。
                clip(0.80 - landMask);
                float shoreSmooth = saturate(_ShoreSmooth);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterDepth = max(0.0, sceneEyeDepth - input.viewDepth);

                float depthT = saturate(waterDepth / max(_DepthRange, 0.001));
                // 近岸直接用遮罩 R 压回浅水色，避免大网格三角形在岸边
                // 形成一块块浅色/深色的硬边。
                // 整片水色都由遮罩 R 驱动：深水 0、岸边 1。
                // 不再混入屏幕深度，避免低模海床把浅水/深水边界切成大台阶。
                float shallowT = 1.0 - shoreWaveMask;
                float3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, shallowT);
                float alpha = lerp(_ShallowAlpha, _DeepAlpha, shallowT);

                float t = _Time.y;
                float2 wp = input.worldXZ;

                // 大尺度色斑：两个噪声相位交叉淡化，斑块缓慢“生灭”，不会整片平移。
                float patchTime = t * _PatchSpeed;
                float patchFrac = frac(patchTime);
                float patchBlend = smoothstep(0.0, 1.0, patchFrac);
                float2 patchSeed = floor(float2(patchTime, patchTime));
                float2 patchOffA = Hash22(patchSeed) * 113.0;
                float2 patchOffB = Hash22(patchSeed + 1.0) * 113.0;
                float2 patchDrift = float2(t * 0.02, -t * 0.013);
                float patchA = Fbm2(wp * _PatchScale + patchOffA + patchDrift);
                float patchB = Fbm2(wp * _PatchScale + patchOffB + patchDrift);
                float patchNoise = lerp(patchA, patchB, patchBlend);
                float patchMask = smoothstep(0.45, 0.75, patchNoise) * _PatchStrength
                    * _NoiseStrength;
                patchMask *= smoothstep(0.05, 0.45, depthT);
                color = lerp(color, _PatchColor.rgb, patchMask);

                // 分面：用屏幕导数得到当前三角形的几何法线，再叠固定方向光。
                // ddx/ddy 在同一三角形内恒定，所以每个三角面是一块完整的明暗。
                float3 faceNormal = normalize(cross(ddx(input.positionWS), ddy(input.positionWS)));
                faceNormal *= (faceNormal.y < 0.0) ? -1.0 : 1.0;
                float3 lightDir = normalize(_FacetLightDirection.xyz);
                float ndl = saturate(dot(faceNormal, lightDir));
                float facetShade = lerp(1.0, 0.55 + 0.60 * ndl, _FacetShade);
                float faceTint = 1.0 + (input.faceRandom - 0.5) * 2.0 * _FacetTint;

                // 岸边那块大三角形如果继续按面明暗，会显得很碎。
                // 用遮罩 R 在靠岸处淡出分面着色，深水仍保留低模分面。
                float3 baseColor = color;
                float3 facetedColor = baseColor * facetShade * faceTint;
                float shoreFacetFade = 1.0 - smoothstep(0.0, 0.55, shoreWaveMask);
                color = lerp(facetedColor, baseColor, shoreFacetFade);

                // 岸线泡沫：用遮罩 R（水深）驱动，边缘天然平滑；
                // 再用世界噪声打碎宽度、做缓慢脉动。
                float shoreNoise = Fbm3(wp * _FoamNoiseScale + float2(t * 0.10, -t * 0.07));
                float foamScale = clamp(_ShoreFoamDistance / 0.3, 0.05, 3.0);
                float foamEnd = lerp(0.18, 0.32, shoreSmooth)
                    * (0.80 + 0.40 * shoreNoise) * foamScale;
                float shoreBand = 1.0 - smoothstep(
                    0.0, max(foamEnd, 0.001), shoreWaveMask);
                float pulse = 0.75 + 0.25 * sin(t * _FoamPulseSpeed + shoreNoise * 6.28318);
                float foam = saturate(shoreBand * pulse * _FoamStrength);

                // 深水浪线。
                float waveNoise = Fbm3(wp * _WaveScale * 1.7 + float2(t * 0.04, t * 0.02));
                float wave = sin(dot(wp, float2(1.0, 0.65)) * _WaveScale * 6.28318
                    + t * _WaveSpeed + waveNoise * 5.0);
                float crest = smoothstep(0.90, 0.995, wave);
                crest *= smoothstep(_WaveStartDepth, _WaveStartDepth + 0.8, waterDepth)
                    * _WaveStrength * _NoiseStrength;

                // 整片水面的噪声流线。
                float streakPhase = frac(dot(wp, float2(0.9, 0.45)) * _StreakScale
                    + waveNoise * _StreakWarp
                    + t * _StreakSpeed);
                float streakLine = 1.0 - smoothstep(0.0, max(_StreakWidth, 0.0001),
                    abs(streakPhase - 0.5));
                streakLine *= smoothstep(_WaveStartDepth, _WaveStartDepth + 0.8, waterDepth);
                streakLine *= _StreakStrength * _NoiseStrength;

                // 冲浪区浪线。
                float surfZone = 1.0 - saturate(waterDepth / max(_SurfZoneDepth, 0.001));
                float lineNoise = Fbm3(wp * 0.35 + float2(t * 0.05, 0.0));
                float phase = frac(surfZone * _SurfWaveRepeat
                    - t * _SurfWaveSpeed + lineNoise * 0.3);
                float surfLine = 1.0 - smoothstep(0.0,
                    max(_SurfWaveWidth, 0.0001), abs(phase - 0.5));
                surfLine *= smoothstep(0.0, 0.12, surfZone)
                    * (1.0 - smoothstep(0.85, 1.0, surfZone));
                surfLine *= _SurfWaveStrength * _NoiseStrength;

                // 水面碎光。
                float2 sparkleFlow = float2(t * _SparkleSpeed, -t * _SparkleSpeed * 0.7);
                float glintA = ValueNoise(wp * _SparkleScale + sparkleFlow);
                float glintB = ValueNoise(wp * _SparkleScale * 2.3 - sparkleFlow * 1.6 + 13.1);
                float glint = glintA * glintB;
                float sparkle = smoothstep(_SparkleThreshold, _SparkleThreshold + 0.12, glint);
                sparkle *= 0.65 + 0.35 * sin(t * 2.0 + glint * 40.0);
                sparkle *= smoothstep(0.25, 0.9, depthT) * _SparkleStrength * _NoiseStrength;

                float totalFoam = saturate(foam + crest + streakLine + surfLine + sparkle);
                color = lerp(color, _FoamColor.rgb, totalFoam);
                alpha = saturate(alpha + foam * 0.65 + crest * 0.25 + streakLine * 0.15
                    + surfLine * 0.5 + sparkle * 0.45);

                // 方案 D / 碰撞体海面：浅水色 + 近裁剪线 alpha 渐隐。
                float shoreSoft = shoreBand * shoreSmooth;
                color = lerp(color, _FoamColor.rgb, shoreSoft * 0.10);
                alpha = saturate(alpha + shoreSoft * 0.08);
                float landFade = 1.0 - smoothstep(0.62, 0.80, landMask);
                alpha *= lerp(1.0, landFade, shoreSmooth);

                // 三角边线：重心坐标在三角形内部为 1，在边上为 0。
                // 用 fwidth 做像素级抗锯齿，远处不会糊成一片。
                float3 baryWidth = fwidth(input.bary);
                float3 edge = smoothstep(float3(0.0, 0.0, 0.0),
                    baryWidth * _EdgeWidth, input.bary);
                float edgeFactor = 1.0 - saturate(min(min(edge.x, edge.y), edge.z));
                float edgeMask = saturate(edgeFactor * _EdgeStrength);
                color = lerp(color, _EdgeColor.rgb, edgeMask);
                alpha = saturate(alpha + edgeMask * 0.35);

                color = MixFog(color, input.fogFactor);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
