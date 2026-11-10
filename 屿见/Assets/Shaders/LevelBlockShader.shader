// 关卡未解锁的灰化着色器（策划案 2.3.1，「关卡未解锁 Shader / Level_Block_Shader」）。
//
// 解锁前，LevelEntrance 会把关卡父物体以下所有 Renderer 的材质换成由本着色器
// 克隆出来的灰度材质，让整棵子树一起变灰；解锁后换回原材质，原外观不受影响。
//
// 为什么不直接用 URP/Lit 把颜色改灰：
//   · 关卡模型材质各不相同，贴图 / 基础色都写在各自的材质里；
//   · 这里按原材质逐个克隆，保留贴图和基础色的明暗，只把颜色压成低饱和度灰，
//     外形细节还在，玩家仍能看出这是哪一块地图；
//   · 不参与光照（Unlit），避免场景灯光把灰度冲淡或染上别的颜色。
Shader "Yujian/Level Block"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _GrayTint ("Gray Tint（灰色调）", Color) = (0.82, 0.84, 0.86, 1)
        _GrayStrength ("Gray Strength（0 = 原色，1 = 全灰）", Range(0, 1)) = 1
        _Brightness ("Brightness（灰度亮度）", Range(0, 2)) = 1.15
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LevelBlock"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

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
                float4 _BaseColor;
                float4 _GrayTint;
                float  _GrayStrength;
                float  _Brightness;
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
                half3 albedo = tex.rgb * _BaseColor.rgb;

                // 亮度加权求灰，再乘一层冷灰色调：饱和度归零，整体偏灰蓝
                half luminance = dot(albedo, half3(0.299, 0.587, 0.114));
                half3 grayColor = luminance * _GrayTint.rgb * _Brightness;
                half3 rgb = lerp(albedo, grayColor, saturate(_GrayStrength));

                half4 color = half4(rgb, 1);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
