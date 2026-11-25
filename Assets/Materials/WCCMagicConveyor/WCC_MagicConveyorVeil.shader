Shader "WCC/VFX/Magic Filament Flow"
{
    Properties
    {
        [HDR] _VioletColor ("Violet Light", Color) = (0.52, 0.12, 0.85, 1)
        [HDR] _GoldColor ("Golden Light", Color) = (1, 0.64, 0.22, 1)
        _ColorSpacing ("Next Color Start Distance", Range(0.25, 5)) = 3.6
        _FlowSpeed ("Color Flow Speed", Range(0, 4)) = 1
        _Opacity ("Light Strength", Range(0, 1)) = 1
        _Intensity ("Light Brightness", Range(0, 6)) = 2.8
        _Softness ("Filament Softness", Range(2, 14)) = 6
        [HideInInspector] _PreviewTime ("Preview Time", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "LuminousFilaments"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _VioletColor, _GoldColor;
                float _ColorSpacing, _FlowSpeed, _Opacity, _Intensity, _Softness, _PreviewTime;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 filament : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 filament : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 center = TransformObjectToWorld(input.positionOS.xyz);
                float3 tangent = normalize(TransformObjectToWorldDir(input.tangentOS.xyz));
                float3 view = GetWorldSpaceNormalizeViewDir(center);
                float3 side = cross(tangent, view);
                float sideLength = length(side);
                side = sideLength > 0.001 ? side / sideLength : float3(0,1,0);
                float3 position = center + side * input.tangentOS.w;
                output.positionCS = TransformWorldToHClip(position);
                output.uv = input.uv;
                output.filament = input.filament;
                output.color = input.color;
                return output;
            }
            float ColorPulse(float phase)
            {
                float p = phase - floor(phase / 2.0) * 2.0;
                float s = sin(p * PI * 0.5);
                return s * s;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float time = _PreviewTime >= 0.0 ? _PreviewTime : _Time.y;
                float group = floor(input.filament.x + 0.5);
                half3 tint = group < 0.5 ? _GoldColor.rgb : _VioletColor.rgb;
                float phase = (time * _FlowSpeed - input.uv.x) / max(_ColorSpacing, 0.01);
                float pulse = lerp(0.055, 1.0, ColorPulse(phase - group + input.filament.y));
                float crossSection = abs(input.uv.y * 2.0 - 1.0);
                float profile = exp(-crossSection * crossSection * _Softness)
                    * (1.0 - smoothstep(0.85, 1.0, crossSection));
                float shimmer = 0.88 + 0.12 * sin(input.uv.x * 2.5 - time * 2.1 + input.filament.y * 12.0);
                half alpha = saturate(profile * pulse * shimmer * input.color.a * _Opacity);
                return half4(tint * _Intensity, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
