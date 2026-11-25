Shader "WCC/VFX/Magic Gradient Background"
{
    Properties
    {
        _GoldColor ("Golden Gradient", Color) = (1, 0.72, 0.31, 1)
        _VioletColor ("Violet Gradient", Color) = (0.65, 0.27, 0.83, 1)
        _Opacity ("Background Opacity", Range(0, 1)) = 0.2
        _Brightness ("Background Brightness", Range(0, 3)) = 1
        _ColorLength ("Gradient Length", Range(0.5, 12)) = 3.6
        _FlowSpeed ("Gradient Flow Speed", Range(0, 3)) = 0.55
        [HideInInspector] _MinDiameter ("Minimum Cross Section Diameter", Float) = 0.6
        [HideInInspector] _MaxDiameter ("Maximum Cross Section Diameter", Float) = 1.8
        [HideInInspector] _VariationLength ("Variation Length", Float) = 1.8
        [HideInInspector] _VariationSpeed ("Variation Speed", Float) = 0.45
        [HideInInspector] _RandomSeed ("Random Seed", Float) = 731
        [HideInInspector] _PathLength ("Path Length", Float) = 14
        [HideInInspector] _PreviewTime ("Preview Time", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-30" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "SoftVariableCrossSectionVolume"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _GoldColor, _VioletColor;
                float _Opacity, _Brightness, _ColorLength, _FlowSpeed;
                float _MinDiameter, _MaxDiameter, _VariationLength, _VariationSpeed, _RandomSeed;
                float _PathLength, _PreviewTime;
            CBUFFER_END

            float Hash(float knot, float seed)
            {
                return frac(sin(knot * 127.1 + seed * 17.73) * 43758.5453);
            }
            float SmoothNoise(float coordinate, float seed)
            {
                float knot = floor(coordinate);
                float t = frac(coordinate);
                t = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
                return lerp(Hash(knot, seed), Hash(knot + 1.0, seed), t);
            }
            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 sampleData : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 density : TEXCOORD1;
                float3 tint : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float time = _PreviewTime >= 0.0 ? _PreviewTime : _Time.y;
                float distance = input.sampleData.x;
                float coordinate = (distance - time * _VariationSpeed) / max(_VariationLength, 0.01);
                float warp = SmoothNoise(coordinate * 0.37, _RandomSeed + 31.0) * 0.7;
                float widthNoise = SmoothNoise(coordinate + warp, _RandomSeed);
                float heightNoise = SmoothNoise(coordinate * 1.27 + warp + 4.3, _RandomSeed + 113.0);
                float2 radius = 0.5 * lerp(_MinDiameter.xx, _MaxDiameter.xx, float2(widthNoise, heightNoise));
                float3 center = TransformObjectToWorld(input.positionOS.xyz);
                // Round density splats have no tube wall or top/bottom sheet.
                float3 cameraRight = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 cameraUp = UNITY_MATRIX_I_V._m01_m11_m21;
                float3 position = center + cameraRight * input.uv.x * radius.x
                    + cameraUp * input.uv.y * radius.y;
                output.positionCS = TransformWorldToHClip(position);
                output.uv = input.uv;
                float3 tangent = normalize(TransformObjectToWorldDir(input.tangentOS.xyz));
                float3 view = GetWorldSpaceNormalizeViewDir(center);
                float projected = length(tangent - view * dot(tangent, view));
                float sampleWeight = input.sampleData.y * max(projected, 0.2)
                    / max(sqrt(radius.x * radius.y) * 1.05, 0.01);
                float endFade = smoothstep(0.0, 0.6, distance)
                    * smoothstep(0.0, 0.6, max(0.0, _PathLength - distance));
                output.density = float3(sampleWeight, endFade, distance);
                float colorCoordinate = (distance - time * _FlowSpeed) / max(_ColorLength, 0.01);
                float phase = colorCoordinate * PI + SmoothNoise(coordinate * 0.6, _RandomSeed + 59.0) * 0.6;
                float blend = smoothstep(0.0, 1.0, 0.5 + 0.5 * sin(phase));
                output.tint = lerp(_GoldColor.rgb, _VioletColor.rgb, blend) * _Brightness;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float radiusSquared = dot(input.uv, input.uv);
                float profile = exp(-radiusSquared * 3.0)
                    * (1.0 - smoothstep(0.65, 1.0, radiusSquared));
                // Optical weights keep overlapping puffs translucent rather than building a solid tube.
                float weight = profile * input.density.x * input.density.y;
                half alpha = 1.0 - pow(max(1.0 - _Opacity, 0.001), weight);
                return half4(input.tint, saturate(alpha));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
