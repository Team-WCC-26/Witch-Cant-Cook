Shader "Hidden/WitchCantCook/PlayerPotGlow"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            Name "Silhouette"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(float4 position : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(position.xyz);
            }
            half4 Frag() : SV_Target { return 1; }
            ENDHLSL
        }
        Pass
        {
            Name "OutlineAndGlow"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_PlayerPotGlowMask);
            float4 _PlayerPotGlowMask_TexelSize;
            float4 _GlowColor;
            float _OutlineWidth;
            float _GlowWidth;
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings output;
                output.position = GetFullScreenTriangleVertexPosition(id);
                output.uv = GetFullScreenTriangleTexCoord(id);
                return output;
            }
            float Mask(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_PlayerPotGlowMask, sampler_LinearClamp, uv).r;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float center = Mask(input.uv);
                float edge = 0;
                float halo = 0;
                const float2 directions[8] = {
                    float2(1,0), float2(-1,0), float2(0,1), float2(0,-1),
                    float2(0.707,0.707), float2(-0.707,0.707),
                    float2(0.707,-0.707), float2(-0.707,-0.707)
                };
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float2 offset = directions[i] * _PlayerPotGlowMask_TexelSize.xy;
                    edge = max(edge, Mask(input.uv + offset * _OutlineWidth));
                    halo += Mask(input.uv + offset * _GlowWidth * 0.5) * 0.075;
                    halo += Mask(input.uv + offset * _GlowWidth) * 0.025;
                }
                return half4(_GlowColor.rgb, saturate(max(edge, halo) * (1 - center)) * _GlowColor.a);
            }
            ENDHLSL
        }
    }
}
