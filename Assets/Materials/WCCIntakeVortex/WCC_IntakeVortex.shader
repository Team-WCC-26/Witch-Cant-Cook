Shader "WCC/VFX/Intake Vortex"
{
    Properties
    {
        [MainTexture][NoScaleOffset] _BaseMap ("원본 소용돌이 이미지", 2D) = "white" {}
        [NoScaleOffset] _StarTimingMap ("별 영역과 개별 표시 정보", 2D) = "black" {}
        [MainColor] _Color ("전체 색상 보정", Color) = (1,1,1,1)
        _Brightness ("밝기", Range(0,3)) = 1.1
        _Glow ("빛 강조", Range(0,2)) = 0.3
        _AnimationSpeed ("전체 흡입 속도 배율", Range(0,10)) = 1
        [HideInInspector] _StarRigidShape ("별 원본 비율 유지", Float) = 0
        [HideInInspector] _StarSurfaceHeight ("별 표면 높이 보정", Float) = 0
        _FlowSpeed ("움직임 속도", Range(0,1)) = 0.22
        _FlowStrength ("흡입 흐름 세기", Range(0,0.12)) = 0.035
        _SpinSpeed ("회전 흐름 비율", Range(-2,2)) = 0.65
        _InwardSpeed ("방사 흐름 세기", Range(0,2)) = 1
        _FlowDirection ("흐름 방향 (-1 방출 / 1 흡입)", Range(-1,1)) = 1
        _Sparkle ("별빛 강조", Range(0,1)) = 0.3
        _StarLifetime ("별 표시 시간 (초)", Range(0.2,6)) = 1.8
        _StarPause ("완전히 사라진 뒤 대기 (초)", Range(0.1,6)) = 1.3
        _StarStagger ("별마다 시작 시차 (초)", Range(0,8)) = 3.1
        _StarPull ("별이 중앙으로 끌리는 비율", Range(0,0.8)) = 0.32
        [NoScaleOffset] _StarSurfaceMap ("별 이동용 표면 깊이", 2D) = "black" {}
        [HideInInspector] _StarTargetOS ("별이 끌려가는 중심", Vector) = (0,0,0,0)
        [HideInInspector] _StarGeometry ("개별 별 메시 사용", Float) = 0
        _SurfaceInset ("입구에서 내려가는 거리", Range(0,0.3)) = 0
        _RecessDepth ("표면이 들어가는 깊이", Range(0,1)) = 0
        _DepthShading ("안쪽 명암", Range(0,0.6)) = 0.15
        _Center ("흡입 중심 XY", Vector) = (0.5,0.5,0,0)
        [HideInInspector] _RecessAxis ("안쪽 방향", Vector) = (0,-1,0,0)
        [HideInInspector] _UVTransform ("표면 UV 보정", Vector) = (1,1,0,0)
        [HideInInspector] _PreviewTime ("Preview Time", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" "IgnoreProjector"="True" }
        ZWrite On
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "PaintedMagicFlow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_StarTimingMap);
            SAMPLER(sampler_StarTimingMap);
            TEXTURE2D(_StarSurfaceMap);
            SAMPLER(sampler_StarSurfaceMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _UVTransform, _Center, _RecessAxis, _StarTargetOS;
                float _Brightness, _Glow, _FlowSpeed, _FlowStrength;
                float _SpinSpeed, _InwardSpeed, _FlowDirection, _Sparkle, _PreviewTime;
                float _StarLifetime, _StarPause, _StarStagger, _RecessDepth, _DepthShading;
                float _StarPull, _StarGeometry, _AnimationSpeed, _StarSurfaceHeight, _StarRigidShape, _SurfaceInset;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 depth : TEXCOORD1;
                float4 starCenter : TEXCOORD2;
                float4 starBasis : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float depth : TEXCOORD1;
                float2 sourceUV : TEXCOORD2;
                float starID : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            float StarPhase(float id)
            {
                return frac(round(id) * 0.61803398875 + 0.137);
            }
            float StarAge(float id, float time)
            {
                float elapsed = time - StarPhase(id) * _StarStagger;
                if (elapsed < 0.0) return -1.0;
                return fmod(elapsed, max(_StarLifetime,0.2) + max(_StarPause,0.1));
            }
            float StarOpacity(float id, float time)
            {
                float age = StarAge(id,time);
                if (age < 0.0) return 0.0;
                float lifetime = max(_StarLifetime,0.2);
                return smoothstep(0.0,lifetime*0.22,age)
                    * (1.0-smoothstep(lifetime*0.55,lifetime,age));
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float time = (_PreviewTime >= 0.0 ? _PreviewTime : _Time.y) * _AnimationSpeed;
                float3 position = input.positionOS.xyz;
                float2 uv = input.uv;
                float depth = input.depth.x;
                float id = input.depth.y;
                if (id > 0.5)
                {
                    float age = StarAge(id,time);
                    float travel = _StarPull * pow(saturate(age/max(_StarLifetime,0.2)),1.25);
                    float2 targetUV = (_Center.xy-_UVTransform.zw)/_UVTransform.xy;
                    uv = lerp(input.uv,targetUV,travel);
                    position = lerp(position,_StarTargetOS.xyz,travel);
                    float4 surface = SAMPLE_TEXTURE2D_LOD(_StarSurfaceMap,sampler_StarSurfaceMap,uv,0);
                    depth = surface.r;
                    if (_StarSurfaceHeight > 0.5) position.y = surface.b - 0.5;
                    if (_StarRigidShape > 0.5)
                    {
                        // Move the center along the bowl, then rotate the whole glyph as one rigid plane.
                        float2 centerUV = lerp(input.starCenter.xy,targetUV,travel);
                        float4 centerSurface = SAMPLE_TEXTURE2D_LOD(_StarSurfaceMap,sampler_StarSurfaceMap,centerUV,0);
                        float height = centerSurface.b-0.5-_RecessDepth*centerSurface.r;
                        const float stepUV = 0.004;
                        float4 surfaceU = SAMPLE_TEXTURE2D_LOD(_StarSurfaceMap,sampler_StarSurfaceMap,centerUV+float2(stepUV,0),0);
                        float4 surfaceV = SAMPLE_TEXTURE2D_LOD(_StarSurfaceMap,sampler_StarSurfaceMap,centerUV+float2(0,stepUV),0);
                        float gradientU = (surfaceU.b-0.5-_RecessDepth*surfaceU.r-height)/stepUV;
                        float gradientV = (surfaceV.b-0.5-_RecessDepth*surfaceV.r-height)/stepUV;
                        // Orthonormal axes and one common scale prevent shear and aspect stretching.
                        float3 alongU = float3(input.starBasis.x,gradientU,input.starBasis.y);
                        float3 alongV = float3(input.starBasis.z,gradientV,input.starBasis.w);
                        float3 right = normalize(alongU);
                        float3 normal = normalize(cross(alongU,alongV));
                        if (normal.y < 0.0) normal = -normal;
                        float3 up = normalize(cross(normal,right));
                        if (dot(up,alongV) < 0.0) up = -up;
                        float scale = sqrt(max(length(input.starBasis.xy)*length(input.starBasis.zw),0.00001));
                        float2 offset = (input.uv-input.starCenter.xy)*scale*(1.0-travel);
                        float2 centerXZ = lerp(input.starCenter.zw,_StarTargetOS.xz,travel);
                        position = float3(centerXZ.x,height,centerXZ.y)+right*offset.x+up*offset.y+normal*0.02;
                        depth = centerSurface.r;
                        // Depth has already been applied to the center height.
                        position -= _RecessAxis.xyz*_RecessDepth*depth;
                    }
                    // The same mesh draws background and moving glyphs in a single pass.
                    position -= _RecessAxis.xyz * 0.007;
                }
                output.positionCS = TransformObjectToHClip(position + _RecessAxis.xyz*(_RecessDepth*depth+_SurfaceInset));
                output.uv = uv;
                output.sourceUV = input.uv;
                output.depth = depth;
                output.starID = id;
                return output;
            }
            half3 BackgroundWithoutStar(float2 uv)
            {
                half3 source = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                float4 data = SAMPLE_TEXTURE2D(_StarTimingMap, sampler_StarTimingMap, uv);
                if (data.g > 0.001)
                {
                    // Fill the painted glyph from the neighboring texture on both sides.
                    // Only the background is advected twice, never the star itself.
                    float halfWidth = max(data.a * 0.25 * 1.5, 0.003);
                    float left = data.b - halfWidth;
                    float right = data.b + halfWidth;
                    half3 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, float2(left, uv.y)).rgb;
                    half3 b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, float2(right, uv.y)).rgb;
                    source = lerp(source, lerp(a,b,saturate((uv.x-left)/(right-left))), data.g);
                }
                return source;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float time = (_PreviewTime >= 0.0 ? _PreviewTime : _Time.y) * _AnimationSpeed;
                float opacity = 0.0;
                float4 star = 0.0;
                if (input.starID > 0.5)
                {
                    clip(_StarGeometry-0.5);
                    opacity = StarOpacity(round(input.starID),time);
                    clip(opacity-0.0001);
                    star = SAMPLE_TEXTURE2D(_StarTimingMap,sampler_StarTimingMap,input.sourceUV);
                    clip(0.25-abs(round(star.r*255.0)-input.starID));
                    clip(star.g-0.001);
                    clip(SAMPLE_TEXTURE2D(_StarSurfaceMap,sampler_StarSurfaceMap,input.uv).g-0.5);
                }
                float2 localUV = input.uv * _UVTransform.xy + _UVTransform.zw;
                float2 delta = localUV - _Center.xy;
                float radius = length(delta);
                float2 radial = delta / max(radius, 0.001);
                float2 tangent = float2(-radial.y, radial.x);
                float edge = min(min(localUV.x,localUV.y),min(1.0-localUV.x,1.0-localUV.y));
                float edgeFade = smoothstep(0.01,0.14,edge);
                float centerFade = smoothstep(0.025,0.12,radius);
                float variation = 0.82 + 0.18 * sin(localUV.x*15.0+localUV.y*11.0);
                float2 flow = (radial*_InwardSpeed*_FlowDirection+tangent*_SpinSpeed)
                    *_FlowStrength*edgeFade*centerFade*variation;
                float phaseA = frac(time*_FlowSpeed);
                float phaseB = frac(time*_FlowSpeed+0.5);
                float blend = abs(phaseA*2.0-1.0);
                float2 uvA = (localUV+flow*(phaseA-0.5)-_UVTransform.zw)/_UVTransform.xy;
                float2 uvB = (localUV+flow*(phaseB-0.5)-_UVTransform.zw)/_UVTransform.xy;
                half3 background = lerp(BackgroundWithoutStar(uvA),BackgroundWithoutStar(uvB),blend);
                half3 contribution = 0.0;
                if (input.starID > 0.5)
                {
                    half3 original = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.sourceUV).rgb;
                    contribution = max(original-BackgroundWithoutStar(input.sourceUV),0.0) * star.g * opacity;
                }
                else if (_StarGeometry < 0.5)
                {
                    // Retain compatibility with a plain surface mesh.
                    star = SAMPLE_TEXTURE2D(_StarTimingMap,sampler_StarTimingMap,input.uv);
                    opacity = star.r > 0.001 ? StarOpacity(round(star.r*255.0),time) : 0.0;
                    half3 original = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb;
                    contribution = max(original-BackgroundWithoutStar(input.uv),0.0) * star.g * opacity;
                }
                half3 painted = (background+contribution)*_Color.rgb;
                half peak = max(painted.r,max(painted.g,painted.b));
                half3 color = painted*_Brightness + painted*peak*peak*_Glow;
                color += contribution*_Color.rgb*_Sparkle;
                color *= 1.0-input.depth*_DepthShading;
                return half4(color,1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}