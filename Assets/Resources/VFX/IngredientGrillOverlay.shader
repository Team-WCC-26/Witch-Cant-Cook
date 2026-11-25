Shader "WitchCantCook/IngredientGrillOverlay"
{
    Properties
    {
        _StripeColor ("Stripe Color", Color) = (0.21568628, 0.08627451, 0.02352941, 1)
        _StripeDirection ("Stripe Direction", Vector) = (1, 0, 0, 0)
        _BoundsCenter ("Bounds Center", Vector) = (0, 0, 0, 0)
        _StripeScale ("Stripe Scale", Float) = 5
        _StripeWidth ("Stripe Width", Range(0.01, 0.9)) = 0.34
        _StripeOffset ("Stripe Offset", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _StripeColor;
                float4 _StripeDirection;
                float4 _BoundsCenter;
                float _StripeScale;
                float _StripeWidth;
                float _StripeOffset;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float stripeCoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.stripeCoord = dot(input.positionOS.xyz - _BoundsCenter.xyz, _StripeDirection.xyz)
                    * _StripeScale + _StripeOffset;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float distanceToStripe = abs(frac(input.stripeCoord + 0.5) - 0.5);
                float edge = max(fwidth(input.stripeCoord), 0.0001);
                half coverage = 1.0 - smoothstep(_StripeWidth * 0.5 - edge,
                    _StripeWidth * 0.5 + edge, distanceToStripe);
                clip(coverage - 0.001);
                return half4(_StripeColor.rgb, _StripeColor.a * coverage);
            }
            ENDHLSL
        }
    }
}
