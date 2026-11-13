Shader "WitchCantCook/VFX/InteractionFocusSparkle"
{
    Properties
    {
        _StarAmount ("Particle shape: disc / diamond", Range(0,1)) = 1
        _Intensity ("Brightness", Range(0,4)) = 1.1
        _EffectOpacity ("Effect opacity", Range(0,1)) = 1
        _PickupBlend ("Pickup color blend", Range(0,1)) = 0
        _PickupColorMin ("Pickup color min", Color) = (1,.62,.05,.95)
        _PickupColorMax ("Pickup color max", Color) = (1,.94,.35,1)
        _SourceAlphaRange ("Source alpha range", Vector) = (.95,1,0,0)
    }
    HLSLINCLUDE
    // UV.xy, normalized age.z and stable per-particle random.w share TEXCOORD0.
    struct Attributes { float4 position : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
    struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
    float _StarAmount;
    float _Intensity;
    float _EffectOpacity;
    float _PickupBlend;
    float4 _PickupColorMin;
    float4 _PickupColorMax;
    float4 _SourceAlphaRange;
    float4 SparkleFragment(Varyings input) : SV_Target
    {
        float2 p = input.uv.xy * 2 - 1;
        float shapeDistance = lerp(length(p), abs(p.x) + abs(p.y), _StarAmount);
        float edge = max(fwidth(shapeDistance) * 1.2, .015);
        float age = saturate(input.uv.z);
        float random = saturate(input.uv.w);
        float4 pickupStart = lerp(_PickupColorMin, _PickupColorMax, random);
        float3 pickupLifetime = age < .65
            ? lerp(_PickupColorMax.rgb, _PickupColorMin.rgb, age / .65)
            : lerp(_PickupColorMin.rgb, _PickupColorMax.rgb, (age - .65) / .35);
        float blend = saturate(_PickupBlend);
        // Blend complete endpoint colors; never multiply a blue start by a yellow lifetime.
        float3 particleColor = lerp(input.color.rgb, pickupStart.rgb * pickupLifetime, blend);
        float sourceAlpha = max(lerp(_SourceAlphaRange.x, _SourceAlphaRange.y, random), .001);
        float particleAlpha = input.color.a * lerp(1, pickupStart.a / sourceAlpha, blend);
        float alpha = (1 - smoothstep(.74 - edge, .74 + edge, shapeDistance)) * particleAlpha * _EffectOpacity;
        float face = 1 - smoothstep(.16, .72, shapeDistance);
        float3 color = particleColor * lerp(.62, 1, face) * _Intensity;
        color = lerp(color, float3(1,1,1), face * .12);
        return float4(color, alpha);
    }
    ENDHLSL
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Universal Particle"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
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
            Name "Builtin Preview Particle"
            Blend SrcAlpha OneMinusSrcAlpha
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