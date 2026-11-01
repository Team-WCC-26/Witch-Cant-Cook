Shader "WitchCantCook/VFX/InteractionFocusSparkle"
{
    Properties
    {
        _StarAmount ("Star rays", Range(0,1)) = 1
        _Intensity ("Brightness", Range(0,4)) = 1.35
    }
    HLSLINCLUDE
    struct Attributes { float4 position : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
    struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
    float _StarAmount;
    float _Intensity;
    float4 SparkleFragment(Varyings input) : SV_Target
    {
        float2 p = input.uv * 2 - 1;
        float r2 = dot(p,p);
        float core = exp2(-r2 * 48);
        float glow = exp2(-r2 * 7) * .26;
        float rays = pow(saturate(1-abs(p.x)),24) * pow(saturate(1-abs(p.y)),1.3)
                   + pow(saturate(1-abs(p.y)),24) * pow(saturate(1-abs(p.x)),1.3);
        float alpha = saturate(core + glow + rays * _StarAmount * .78) * input.color.a;
        float3 color = lerp(input.color.rgb, float3(.94,.97,1), saturate(core * .8)) * _Intensity;
        return float4(color,alpha);
    }
    ENDHLSL
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Universal Sparkle"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SparkleFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Builtin Preview Sparkle"
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SparkleFragment
            #include "UnityCG.cginc"
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            ENDHLSL
        }
    }
}
