// 暴击点 / 剩余时间进度条的透视着色器。
//
// 关键状态（都在这个唯一的 Pass 里）：
//   · ZTest Always：不管前面有没有垃圾、矿石，暴击点都画在最上面；
//   · Queue = Overlay：在所有不透明 / 普通透明物体之后绘制，保证能覆盖已经画好的垃圾；
//   · ZWrite Off：不写深度，避免影响后面正常物体的遮挡关系；
//   · Cull Off：球体与朝向相机的进度条面片都不依赖背面剔除。
//
// 注意：SRP 的 DrawRenderers 每个 Renderer 只会选一个匹配的 Pass，
// 所以透视不能做成「正常 Pass + 透视 Pass」两个同 LightMode 的 Pass，
// 必须在同一个 Pass 里用 ZTest Always 直接压在最上层。
Shader "Yujian/Crit Point Overlay"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CritPointOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
