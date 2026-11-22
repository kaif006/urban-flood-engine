Shader "FloodTwin/FlowOverlay"
{
    Properties
    {
        _Overlay ("Flow and sector data", 2D) = "black" {}
        _GridSize ("Grid width and depth in metres", Vector) = (1, 1, 0, 0)
        _Sector ("Selected sector", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Overlay);
            SAMPLER(sampler_Overlay);

            CBUFFER_START(UnityPerMaterial)
                float4 _GridSize;
                float _Sector;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 gridUV : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.gridUV = float2(input.positionOS.x, -input.positionOS.z) / _GridSize.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 data = SAMPLE_TEXTURE2D(_Overlay, sampler_Overlay, input.gridUV);
                clip(data.a - 0.5);

                half3 low = half3(0.06, 0.18, 0.38);
                half3 mid = half3(0.02, 0.62, 0.69);
                half3 high = half3(1.0, 0.76, 0.14);
                half3 color = lerp(low, mid, saturate(data.r * 2.0));
                color = lerp(color, high, saturate(data.r * 2.0 - 1.0));

                half selected = _Sector < 0.5 ? 1.0 : (_Sector < 1.5 ? data.g : data.b);
                return half4(lerp(half3(0.10, 0.12, 0.14), color, selected), 1.0);
            }
            ENDHLSL
        }
    }
}
