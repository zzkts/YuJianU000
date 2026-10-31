// 建筑迁移预览用的绿 / 红外表着色器（策划案 3.3 + 3.2 面积反馈）。
//
// 只在迁移过程中使用：BuildingMigration 把建筑每个 Renderer 的材质换成由本着色器
// 克隆出来的透明材质，再把 _GhostColor 设成绿或红、按闪烁曲线改 alpha。
// 迁移结束换回原材质，建筑本身的外观不受影响。
//
// 为什么不用 URP/Lit 直接改颜色：
//   · 建筑分层颜色走 MaterialPropertyBlock 的 _BaseColor，直接染色会覆盖掉楼层颜色，
//     而且不同楼层的 MPB 会互相打架；
//   · 迁移时要的是「整体变成一个半透明色块」，本着色器用独立属性 _GhostColor，
//     不会被 MPB 干扰，也保证绿色 / 红色在任何贴图上都看得清。
//
// 贴图只用来保留一点表面细节（_TextureInfluence），不参与光照——迁移中的建筑
// 本来就不该被当正常物体打光，否则绿 / 红会被环境光冲淡。
Shader "Yujian/Building Preview Tint"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [HDR] _GhostColor ("Ghost Color (RGB = 颜色，A = 透明度)", Color) = (0.24, 0.85, 0.35, 0.5)
        _TextureInfluence ("Texture Influence（0 = 纯色块，1 = 原贴图）", Range(0, 1)) = 0.35
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
            Name "BuildingPreviewTint"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            // 只画正面：建筑多数是封闭体块，双面画会让半透明层叠得发黑
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _GhostColor;
                float  _TextureInfluence;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float  fogCoord : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // 纯色块 → 原贴图之间插值：绿 / 红始终是主色，贴图只负责一点明暗起伏
                half3 detail = lerp(half3(1, 1, 1), tex.rgb, _TextureInfluence);
                half3 rgb = detail * _GhostColor.rgb;
                half alpha = saturate(_GhostColor.a * tex.a);

                half4 color = half4(rgb, alpha);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
