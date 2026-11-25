Shader "WCC/VFX/Magic Conveyor Flow"
{
    Properties
    {
        [HDR] _FlowColor ("Flow Color", Color) = (0.10, 0.72, 0.78, 1)
        [HDR] _AccentColor ("Accent Color", Color) = (0.46, 0.24, 0.78, 1)
        _AccentAmount ("Violet Amount", Range(0, 0.6)) = 0.18
        _Intensity ("Brightness", Range(0, 5)) = 1.4
        _Opacity ("Opacity", Range(0, 1)) = 0.82
        _FlowSpeed ("Flow Speed", Range(-5, 5)) = 1.25
        _StreakDensity ("Streak Density", Range(0.1, 4)) = 0.55
        _StreakLength ("Faint Tail Length", Range(0.1, 0.85)) = 0.74
        _HeadLength ("Bright Head Length", Range(0.02, 0.2)) = 0.055
        _HeadBlend ("Head To Tail Blend", Range(0.05, 0.5)) = 0.22
        _TailStrength ("Faint Tail Brightness", Range(0.02, 0.4)) = 0.18
        _CoreWidth ("Core Width", Range(0.02, 0.5)) = 0.14
        _HaloStrength ("Soft Halo", Range(0, 1)) = 0.20
        _IdleStrength ("Continuous Thread", Range(0, 0.5)) = 0.10
        [HideInInspector] _PreviewTime ("Preview Time", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "MagicFlow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FlowColor;
                half4 _AccentColor;
                float _AccentAmount, _Intensity, _Opacity, _FlowSpeed;
                float _StreakDensity, _StreakLength, _CoreWidth;
                float _HaloStrength, _IdleStrength, _PreviewTime;
                float _HeadLength, _TailStrength, _HeadBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float time = _PreviewTime >= 0.0 ? _PreviewTime : _Time.y;
                // Distance-based U coordinates let each streak travel at a stable world speed.
                float phase = dot(input.color.rgb, float3(1.17, 2.71, 4.13));
                float q = frac((input.uv.x - time * _FlowSpeed) * _StreakDensity + phase);
                // One continuous brightness profile joins the faint tail to the short tip.
                float headEnd = 0.90;
                float peak = headEnd - _HeadLength * 0.5;
                float tailStart = headEnd - _HeadLength - _StreakLength;
                float tailProgress = saturate((q - tailStart) / (peak - tailStart));
                float softTail = smoothstep(0.0, 1.0, tailProgress) * _TailStrength;
                float shoulder = max(_HeadLength, _StreakLength * _HeadBlend);
                float behindPeak = max(peak - q, 0.0) / shoulder;
                float highlight = exp(-2.0 * behindPeak * behindPeak);
                float frontFade = 1.0 - smoothstep(peak, headEnd, q);
                float tailFade = smoothstep(tailStart, tailStart + _StreakLength * 0.12, q);
                float profile = (softTail + highlight * (0.85 - _TailStrength)) * frontFade * tailFade;
                float crossSection = abs(input.uv.y * 2.0 - 1.0);
                float aa = max(fwidth(crossSection), 0.008);
                float core = 1.0 - smoothstep(_CoreWidth, _CoreWidth + aa * 2.0, crossSection);
                float halo = pow(saturate(1.0 - crossSection), 3.5) * _HaloStrength;
                float brightness = _IdleStrength + profile;
                float violet = _AccentAmount * (0.45 + 0.55 * sin(input.uv.x * 0.75 - time * 0.35 + phase));
                half3 tint = lerp(_FlowColor.rgb, _AccentColor.rgb, saturate(violet)) * input.color.rgb;
                half alpha = saturate((core + halo) * brightness * _Opacity * input.color.a);
                return half4(tint * _Intensity, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
