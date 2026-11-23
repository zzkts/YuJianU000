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
        _ShallowAlpha ("Shallow Alpha", Range(0, 1)) = 0.65
        _DeepAlpha ("Deep Alpha", Range(0, 1)) = 0.98

        [Header(Shore Foam)]
        // 这圈泡沫按「垂直水深」计算，单位是世界单位（米）。
        // 岛宽上百米时 0.3 米基本看不见，默认给到 2 米，可在材质里调宽窄。
        _ShoreFoamDistance ("Shore Foam Distance (world units)", Float) = 2.5
        _ShoreFoamCore ("Shore Foam Core Width (world units)", Float) = 0.6
        _FoamStrength ("Foam Strength", Range(0, 1)) = 1.0
        _FoamNoiseScale ("Foam Noise Scale", Float) = 0.25
        _FoamPulseSpeed ("Foam Pulse Speed", Float) = 0.5

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
        _SurfZoneDepth ("Surf Zone Depth (world units)", Float) = 2.5
        _SurfWaveRepeat ("Surf Wave Repeat", Float) = 2.0
        _SurfWaveSpeed ("Surf Wave Speed", Float) = 0.30
        _SurfWaveWidth ("Surf Wave Width", Range(0.01, 0.5)) = 0.12
        _SurfWaveStrength ("Surf Wave Strength", Range(0, 1)) = 0.8

        [Header(Surface Volume)]
        _SwellHeight ("Swell Height (world units)", Float) = 0.40
        _SwellScale ("Swell Scale", Float) = 0.009
        _SwellSpeed ("Swell Speed", Float) = 0.35
        _SwellDirection ("Swell Direction (XZ)", Vector) = (1, 0.55, 0, 0)
        _SwellNormalStrength ("Swell Normal Strength", Range(0, 4)) = 2.2
        _RippleScale ("Ripple Scale", Float) = 0.28
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.14
        _VolumeLightDirection ("Volume Light Direction", Vector) = (0.32, 0.77, -0.56, 0)
        _FresnelColor ("Fresnel Color", Color) = (0.60, 0.88, 0.96, 1)
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.30
        _SpecularStrength ("Sun Glint Strength", Range(0, 2)) = 0.35
        _SpecularGloss ("Sun Glint Gloss", Float) = 60
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
                float  _ShoreFoamCore;
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
                float  _SwellHeight;
                float  _SwellScale;
                float  _SwellSpeed;
                float4 _SwellDirection;
                float  _SwellNormalStrength;
                float  _RippleScale;
                float  _RippleStrength;
                float4 _VolumeLightDirection;
                float4 _FresnelColor;
                float  _FresnelStrength;
                float  _SpecularStrength;
                float  _SpecularGloss;
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
                float3 positionWS : TEXCOORD3;
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

            float2 SwellDir()
            {
                float2 d = _SwellDirection.xy;
                return d / max(length(d), 1e-4);
            }

            // 低频涌浪：世界坐标驱动的 FBM，沿传播方向拉伸。用于顶点起伏
            // 和整体明暗；用噪声而不是方向正弦，避免出现规则的行波。
            float SwellHeight(float2 wp, float t)
            {
                float2 d = SwellDir();
                float2 p = float2(-d.y, d.x);
                float2 uv = float2(dot(wp, p) * 0.55, dot(wp, d)) * _SwellScale;
                float2 flow = float2(t * _SwellSpeed * 0.085, t * _SwellSpeed * 0.063);
                return (Fbm2(uv + flow) - 0.5) * 2.0;
            }

            // 细碎涟漪：只参与法线，不移动顶点，避免低模网格把它采样成台阶。
            float RippleHeight(float2 wp, float t)
            {
                float2 flow = float2(t * 0.075, -t * 0.052);
                return (Fbm2(wp * _RippleScale + flow + 31.7) - 0.5) * 2.0;
            }

            // 用屏幕导数反推世界空间斜率，一次高度采样就能得到法线，
            // 比有限差分省很多噪声采样。涟漪随距离淡出，避免远处闪烁。
            float3 SurfaceNormal(float2 wp, float3 posWS, float t, float rippleFade)
            {
                float3 dpdx = ddx(posWS);
                float3 dpdy = ddy(posWS);
                float det = dpdx.x * dpdy.z - dpdx.z * dpdy.x;
                det = (abs(det) < 1e-5) ? 1e-5 : det;

                float hSwell = SwellHeight(wp, t);
                float2 dhS = float2(ddx(hSwell), ddy(hSwell));
                float2 g = float2(
                    (dpdy.z * dhS.x - dpdx.z * dhS.y) / det,
                    (-dpdy.x * dhS.x + dpdx.x * dhS.y) / det);
                g *= _SwellNormalStrength;

                float hRipple = RippleHeight(wp, t) * rippleFade;
                float2 dhR = float2(ddx(hRipple), ddy(hRipple));
                g += float2(
                    (dpdy.z * dhR.x - dpdx.z * dhR.y) / det,
                    (-dpdy.x * dhR.x + dpdx.x * dhR.y) / det) * _RippleStrength;

                return normalize(float3(-g.x, 1.0, -g.y));
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

                // 低频涌浪起伏：平面网格顶点较疏，只做长波，避免采样成锯齿。
                positionWS.y += SwellHeight(wp, _Time.y) * _SwellHeight;

                // 位移后必须重算裁剪空间位置与视深度，否则水线不会真的动。
                output.positionCS = TransformWorldToHClip(positionWS);
                output.worldXZ = positionWS.xz;
                output.positionWS = positionWS;
                output.viewDepth = -TransformWorldToView(positionWS).z;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawSceneDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                float waterDepth = max(0.0, sceneEyeDepth - input.viewDepth);

                // 岸线用的“垂直水深”：水线两侧的地形高度差。
                // 视空间深度差会随相机俯角被拉长，泡沫圈会变宽变窄、还会漂，
                // 所以贴岛的那一圈单独用世界 Y 计算，和机位无关。
                // 采样到天空（没有不透明几何）时把水深设成极大，避免整片海被当成浅水。
                float3 scenePosWS = ComputeWorldSpacePosition(
                    screenUV, rawSceneDepth, UNITY_MATRIX_I_VP);
                bool hasSceneGeometry = sceneEyeDepth < _ProjectionParams.z * 0.999;
                float shoreDepth = hasSceneGeometry
                    ? max(0.0, input.positionWS.y - scenePosWS.y)
                    : 1e4;

                float depthT = saturate(waterDepth / max(_DepthRange, 0.001));
                float3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
                float alpha = lerp(_ShallowAlpha, _DeepAlpha, depthT);

                float t = _Time.y;
                float2 wp = input.worldXZ;

                // 体积感：涌浪与涟漪的斜率合成法线，配一点方向光和菲涅尔，
                // 让平坦的海面在坡面朝向变化时有明暗层次。
                float rippleFade = saturate(1.0 - input.viewDepth / 90.0);
                float3 waveNormal = SurfaceNormal(wp, input.positionWS, t, rippleFade);
                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 lightDir = normalize(_VolumeLightDirection.xyz);
                float ndl = saturate(dot(waveNormal, lightDir));
                color *= lerp(0.88, 1.10, ndl);
                float fresnel = pow(1.0 - saturate(dot(waveNormal, viewDir)), 3.0)
                    * _FresnelStrength;
                color = lerp(color, _FresnelColor.rgb, fresnel);

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

                // 岸线泡沫：岛与海的交界整圈都铺白色浪花。
                // 内圈（foamCore）是贴着水线的实心白沫；外圈（foamOuter）在
                // 泡沫带的前 45% 保持满强度，只在最外侧淡出，噪声只负责把
                // 外缘打得参差不齐——这样整圈是实的白沫，而不是一层淡水色。
                float shoreNoise = Fbm3(wp * _FoamNoiseScale + float2(t * 0.10, -t * 0.07));
                float band = max(_ShoreFoamDistance * (0.70 + 0.60 * shoreNoise), 0.0001);
                float foamOuter = 1.0 - smoothstep(band * 0.45, band, shoreDepth);
                float foamCore = 1.0 - smoothstep(
                    0.0, max(_ShoreFoamCore, 0.0001), shoreDepth);
                float pulse = 0.92 + 0.08 * sin(t * _FoamPulseSpeed + shoreNoise * 6.28318);
                float foam = saturate(
                    max(foamCore, foamOuter * pulse) * _FoamStrength);

                // 深水浪线：完全不使用正弦。把采样坐标沿浪脊方向拉长、
                // 沿传播方向压缩（各向异性），再对噪声做域扭曲（domain
                // warping），取 FBM 的高值区作为浪脊。方向感来自拉伸而
                // 不是周期项，因此浪线会自然弯曲、分叉、成片断开，
                // 不会出现平行或同步平移的条纹。
                float waveNoise = Fbm3(wp * _WaveScale * 1.7 + float2(t * 0.04, t * 0.02));
                float2 waveDir = normalize(float2(1.0, 0.65));
                float2 wavePerp = float2(-waveDir.y, waveDir.x);
                float2 waveUV = float2(dot(wp, wavePerp) * 0.55,
                    dot(wp, waveDir) * 1.45) * _WaveScale;
                // 两组互不相关的噪声偏移，让浪脊弯曲、分叉。
                float2 waveWarp = float2(
                    Fbm2(waveUV * 0.6 + float2(t * 0.015, -t * 0.011)),
                    Fbm2(waveUV * 0.6 + float2(-t * 0.012, t * 0.017) + 19.7)
                ) - 0.5;
                // 沿传播方向滚动，浪脊缓慢推进。
                float fieldNoise = Fbm3(waveUV + waveWarp * 1.1
                    + float2(0.0, t * _WaveSpeed));
                // FBM 分布较窄，先拉开对比，浪线覆盖率才可控。
                float field = saturate((fieldNoise - 0.5) * 2.2 + 0.5);
                // fwidth 自适应阈值：远处浪线不会闪烁、忽粗忽细。
                float waveAA = fwidth(field) * 1.5;
                float crest = smoothstep(0.58 - waveAA, 0.74 + waveAA, field);
                // 大尺度低频斑块决定浪线在哪里成片出现，避免满屏均匀。
                float waveSet = Fbm2(wp * _WaveScale * 0.35
                    + float2(t * 0.008, -t * 0.006));
                crest *= smoothstep(0.45, 0.62, waveSet);
                crest *= smoothstep(_WaveStartDepth, _WaveStartDepth + 1.2, waterDepth)
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
                // 同样用垂直水深，让浪线跟泡沫圈一起贴住岛，而不是随相机漂移。
                float surfZone = 1.0 - saturate(shoreDepth / max(_SurfZoneDepth, 0.001));
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

                // 浪脊高光：把法线起伏变成少量太阳反光，进一步强化体积感。
                float3 halfDir = normalize(lightDir + viewDir);
                float spec = pow(saturate(dot(waveNormal, halfDir)),
                    max(_SpecularGloss, 1.0)) * _SpecularStrength;
                spec *= smoothstep(0.05, 0.45, depthT);
                color += _FoamColor.rgb * spec;

                color = MixFog(color, input.fogFactor);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
