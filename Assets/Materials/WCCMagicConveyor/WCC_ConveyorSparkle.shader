Shader "WCC/VFX/Conveyor Sparkle"
{
    Properties
    {
        [HDR] _Color ("Sparkle Color", Color) = (0.75, 0.92, 1, 1)
        _Intensity ("Brightness", Range(0, 4)) = 1.5
        _TwinkleSpeed ("Twinkle Speed", Range(0, 10)) = 4
        _RayStrength ("Star Rays", Range(0, 1)) = 1
        _CoreGlow ("Soft Core", Range(0, 1)) = 0.35
        _MinBrightness ("Minimum Sparkle Brightness", Range(0, 1)) = 0.12
        [HideInInspector] _PreviewTime ("Preview Time", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "Sparkle"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity, _TwinkleSpeed, _PreviewTime, _MinBrightness, _RayStrength, _CoreGlow;
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
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 p = input.uv * 2.0 - 1.0;
                float rayWidth = max(0.065, max(fwidth(p.x), fwidth(p.y)) * 0.65);
                float vertical = exp(-abs(p.x) / rayWidth) * pow(saturate(1.0 - abs(p.y)), 2.0);
                float horizontal = exp(-abs(p.y) / rayWidth) * pow(saturate(1.0 - abs(p.x)), 2.0);
                float core = exp(-dot(p,p) * 18.0) * _CoreGlow;
                float time = _PreviewTime >= 0.0 ? _PreviewTime : _Time.y;
                float phase = dot(input.color.rgb, float3(13.7, 37.1, 73.9));
                float twinkle = lerp(_MinBrightness, 1.0, pow(sin(time * _TwinkleSpeed + phase), 4.0));
                float alpha = saturate(max(vertical, horizontal) * _RayStrength + core) * input.color.a * _Color.a * twinkle;
                return half4(_Color.rgb * input.color.rgb * _Intensity, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
