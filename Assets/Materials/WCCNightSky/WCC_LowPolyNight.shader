Shader "WCC/Skybox/Low Poly Starry Night"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.025, 0.034, 0.085, 1)
        _HorizonColor ("Horizon", Color) = (0.085, 0.13, 0.22, 1)
        _GroundColor ("Lower Hemisphere", Color) = (0.018, 0.025, 0.055, 1)
        _StarColor ("Cool Stars", Color) = (0.75, 0.86, 1, 1)
        _WarmStarColor ("Warm Stars", Color) = (1, 0.88, 0.64, 1)
        _StarDensity ("Star Density", Range(0, 0.2)) = 0.065
        _StarSize ("Star Size", Range(0.01, 0.16)) = 0.065
        _StarIntensity ("Star Brightness", Range(0, 4)) = 1.25
        _FacetStrength ("Low Poly Facets", Range(0, 0.5)) = 0.16
        _Rotation ("Rotation", Range(0, 360)) = 18
        _Exposure ("Exposure", Range(0, 3)) = 1
        [Toggle] _MeteorsEnabled ("별똥별 켜기", Float) = 1
        _MeteorInterval ("별똥별 평균 출현 간격 (초)", Range(3,60)) = 12
        _MeteorSpeed ("별똥별 속도 (도/초)", Range(5,90)) = 24
        _MeteorTravel ("별똥별 이동 거리 (도)", Range(5,40)) = 18
        _MeteorElevation ("별똥별 출현 높이 (도)", Range(15,70)) = 40
        _MeteorTrailLength ("별똥별 꼬리 길이 (도)", Range(0.5,12)) = 4.5
        _MeteorWidth ("별똥별 두께", Range(0.0001,0.004)) = 0.0007
        _MeteorBrightness ("별똥별 밝기", Range(0,8)) = 2.5
        [HDR] _MeteorColor ("별똥별 꼬리 색", Color) = (0.58,0.76,1,1)
        [HideInInspector] _SkyPreviewTime ("Preview Time", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GroundColor;
                half4 _StarColor, _WarmStarColor;
                float _StarDensity, _StarSize, _StarIntensity;
                float _FacetStrength, _Rotation, _Exposure;
                half4 _MeteorColor;
                float _MeteorsEnabled, _MeteorInterval, _MeteorSpeed, _MeteorTravel, _MeteorElevation;
                float _MeteorTrailLength, _MeteorWidth, _MeteorBrightness, _SkyPreviewTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float Hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Continuous positions at the longitude wrap; latitude collapses at the poles.
            float3 SphereDirection(float2 uv)
            {
                float longitude = (uv.x - 0.5) * TWO_PI;
                float latitude = (uv.y - 0.5) * PI;
                float c = cos(latitude);
                return float3(sin(longitude) * c, sin(latitude), cos(longitude) * c);
            }

            half3 ShootingStars(float3 dir, float pixelFootprint)
            {
                if (_MeteorsEnabled < 0.5 || _MeteorBrightness <= 0.0 || dir.y < 0.02) return 0;
                float time = max(0.0,_SkyPreviewTime >= 0.0 ? _SkyPreviewTime : _Time.y);
                float baseDuration = max(_MeteorTravel,1.0)/max(_MeteorSpeed,1.0);
                float interval = max(_MeteorInterval*3.0,baseDuration*1.2+1.0);
                float aa = max(pixelFootprint*0.6,0.00002);
                half3 result = 0;
                // Three longitude sectors make occasional meteors visible from different camera directions.
                // Event times/positions vary each cycle; no objects or per-frame allocations are required.
                [unroll] for (int stream=0; stream<3; stream++)
                {
                    float clock = time/interval + stream*0.373;
                    float cycle = floor(clock);
                    float slotTime = frac(clock)*interval;
                    float2 seed = float2(cycle,stream*71.13+9.7);
                    float duration = baseDuration*lerp(0.8,1.2,Hash(seed+1.3));
                    float delay = 0.25 + Hash(seed+3.1)*max(0.0,interval-baseDuration*1.2-0.5);
                    float age = (slotTime-delay)/duration;
                    if (age < 0.0 || age > 1.0) continue;

                    float longitude = (stream+0.5)/3.0+(Hash(seed+13.7)-0.5)*0.27;
                    float elevation = radians(clamp(_MeteorElevation+(Hash(seed+24.9)-0.5)*28.0,15.0,75.0));
                    float3 start = SphereDirection(float2(longitude,0.5+elevation/PI));
                    float3 east = normalize(float3(start.z,0,-start.x));
                    float3 north = cross(start,east);
                    float side = Hash(seed+45.3) < 0.5 ? -1.0 : 1.0;
                    float3 direction = normalize(east*side*0.8-north*0.6);
                    float travel = radians(_MeteorTravel)*age;
                    float3 head = start*cos(travel)+direction*sin(travel);
                    float3 motion = -start*sin(travel)+direction*cos(travel);
                    float3 acrossAxis = normalize(cross(head,motion));
                    float along = atan2(dot(dir,motion),dot(dir,head));
                    float across = asin(clamp(dot(dir,acrossAxis),-1.0,1.0));

                    float tailLength = radians(_MeteorTrailLength)*lerp(0.75,1.2,Hash(seed+62.0));
                    float behind = -along;
                    float tail = saturate(1.0-behind/max(tailLength,0.0001));
                    float tailMask = step(0.0,behind)*step(behind,tailLength);
                    float width = _MeteorWidth*lerp(0.15,1.0,tail);
                    float tailCore = (1.0-smoothstep(width-aa,width+aa,abs(across)))*tail*tail;
                    float glow = exp2(-across*across/max(_MeteorWidth*_MeteorWidth*20.0,1e-8))*tail*0.18;
                    half3 trail = _MeteorColor.rgb*(tailCore+glow)*tailMask;

                    float diamond = abs(along)+abs(across);
                    float headCore = 1.0-smoothstep(_MeteorWidth*1.9-aa,_MeteorWidth*1.9+aa,diamond);
                    float headGlow = exp2(-(along*along+across*across)/max(_MeteorWidth*_MeteorWidth*14.0,1e-8))*0.35;
                    half3 headColor = lerp(_MeteorColor.rgb,half3(1.0,0.96,0.84),0.8);
                    float envelope = smoothstep(0.0,0.12,age)*(1.0-smoothstep(0.68,1.0,age));
                    result += (trail+headColor*(headCore+headGlow))*envelope;
                }
                return result*_MeteorBrightness*smoothstep(0.02,0.14,dir.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float angle = radians(_Rotation);
                float2 xz = float2(dir.x * cos(angle) - dir.z * sin(angle), dir.x * sin(angle) + dir.z * cos(angle));
                dir.xz = xz;
                float2 uv = float2(atan2(dir.x, dir.z) / TWO_PI + 0.5, asin(clamp(dir.y, -1.0, 1.0)) / PI + 0.5);

                float altitude = saturate(dir.y);
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(altitude, 0.48));
                sky = lerp(_GroundColor.rgb, sky, smoothstep(-0.25, 0.04, dir.y));

                // Restrained triangular color planes, kept broad enough to read as low poly.
                float2 facetUV = uv * float2(32.0, 16.0);
                float2 facetCell = floor(facetUV);
                float facetTriangle = step(frac(facetUV).x, frac(facetUV).y);
                float facet = Hash(float2(fmod(facetCell.x, 32.0), facetCell.y) + facetTriangle * float2(0.37, 0.73));
                sky *= 1.0 + (facet - 0.5) * _FacetStrength;

                const float2 grid = float2(192.0, 96.0);
                float2 cell = floor(uv * grid);
                half3 starLight = 0;
                float pixelFootprint = max(length(ddx(dir)), length(ddy(dir)));
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float2 candidate = cell + float2(x, y);
                        float2 key = float2(candidate.x - floor(candidate.x / grid.x) * grid.x, candidate.y);
                        float seed = Hash(key);
                        float2 jitter = float2(Hash(key + 17.31), Hash(key + 46.79));
                        float2 starUV = (candidate + 0.2 + jitter * 0.6) / grid;
                        float3 starDir = SphereDirection(starUV);
                        float density = _StarDensity * max(0.05, cos((starUV.y - 0.5) * PI));
                        float present = step(seed, density) * step(0.0, starUV.y) * step(starUV.y, 1.0);
                        float size = _StarSize * lerp(0.55, 1.5, Hash(key + 92.0)) * PI / grid.y;
                        float3 east = normalize(float3(starDir.z, 0, -starDir.x) + float3(0.00001, 0, 0));
                        float3 north = cross(starDir, east);
                        float2 delta = float2(dot(dir - starDir, east), dot(dir - starDir, north));
                        float diamond = abs(delta.x) + abs(delta.y);
                        float aa = max(pixelFootprint * 0.65, 0.00002);
                        float core = 1.0 - smoothstep(size - aa, size + aa, diamond);
                        float prominent = step(0.88, Hash(key + 137.0));
                        float fourPoint = sqrt(abs(delta.x * delta.y)) * 2.8 + max(abs(delta.x), abs(delta.y)) * 0.32;
                        float sparkle = 1.0 - smoothstep(size * 1.35 - aa, size * 1.35 + aa, fourPoint);
                        float glow = exp2(-dot(delta, delta) / max(size * size * 3.0, 1e-8)) * 0.08;
                        float energy = (max(core, sparkle * prominent) + glow) * present;
                        half3 starColor = lerp(_StarColor.rgb, _WarmStarColor.rgb, step(0.79, Hash(key + 73.0)));
                        starLight += starColor * energy * lerp(0.4, 1.0, Hash(key + 3.7));
                    }
                }
                float starVisibility = smoothstep(-0.02, 0.16, dir.y);
                return half4((sky + starLight * _StarIntensity * starVisibility + ShootingStars(dir,pixelFootprint)) * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}