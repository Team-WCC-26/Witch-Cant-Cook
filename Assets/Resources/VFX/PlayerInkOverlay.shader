Shader "WitchCantCook/PlayerInkOverlay"
{
    Properties
    {
        _InkTex ("Ink", 2D) = "white" {}
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" }
        Pass
        {
            Name "InkOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_InkTex);
            SAMPLER(sampler_InkTex);
            CBUFFER_START(UnityPerMaterial)
                float4x4 _WorldToHead;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 pos : POSITION;
                float3 normal : NORMAL;
            };
            struct Varyings
            {
                float4 pos : SV_POSITION;
                float3 headPos : TEXCOORD0;
                float3 headNormal : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 worldPos = TransformObjectToWorld(input.pos.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(input.normal);
                output.pos = TransformWorldToHClip(worldPos + worldNormal * 0.0005);
                output.headPos = mul(_WorldToHead, float4(worldPos, 1)).xyz;
                output.headNormal = mul((float3x3)_WorldToHead, worldNormal);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Head-local projection follows skinning without character atlas UVs.
                clip(input.headPos.z - 0.015);
                clip(input.headPos.y + 0.025);
                clip(0.43 - input.headPos.y);
                clip(input.headNormal.z - 0.05);
                float2 uv = float2(input.headPos.x, input.headPos.y - 0.19) / 0.55 + 0.5;
                clip(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)));
                half4 ink = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, uv);
                ink.a *= _Opacity;
                clip(ink.a - 0.005);
                return ink;
            }
            ENDHLSL
        }
    }
}
