// Prismatica lit shader: vertex color albedo, soft wrapped sun with hue-shifted (colored) shadows,
// a gentle toon shelf, sky ambient, colored rim light, painterly world-space color breakup, fog,
// and glow from vertex alpha below 1 (emission = albedo * (1 - alpha) * _Glow).
Shader "ZoneArtC/PrismLit"
{
    Properties
    {
        _Wrap ("Diffuse wrap", Range(0,1)) = 0.4
        _Glow ("Glow strength for vertex alpha < 1", Float) = 4
        _Rim ("Rim strength", Range(0,2)) = 0.35
        _RimColor ("Rim color", Color) = (1,0.9,0.8,1)
        _ShadowColor ("Shadow tint (added light in shade)", Color) = (0.25,0.12,0.4,1)
        _AmbientBoost ("Ambient multiplier", Range(0,3)) = 1
        _Toon ("Toon shelf amount", Range(0,1)) = 0.35
        _Breakup ("Painterly breakup", Range(0,1)) = 0.12
        _BreakupScale ("Breakup scale", Float) = 0.25
        _Translucency ("Back-lit translucency", Range(0,1)) = 0
        _StrataOn ("Strata on cliffs", Float) = 0
        _BandH ("Band height", Float) = 2.5
        _BandOffset ("Band offset", Float) = 0
        _StrataCount ("Strata count", Float) = 6
        _StrataWarp ("Strata warp", Float) = 1.2
        _S0 ("Strata 0", Color) = (1,0.5,0.4,1)
        _S1 ("Strata 1", Color) = (1,0.7,0.4,1)
        _S2 ("Strata 2", Color) = (0.8,0.5,1,1)
        _S3 ("Strata 3", Color) = (1,0.85,0.65,1)
        _S4 ("Strata 4", Color) = (1,0.65,0.75,1)
        _S5 ("Strata 5", Color) = (0.6,0.5,0.95,1)
        _FogTint ("Distance desaturation toward fog", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half _Wrap;
            half _Glow;
            half _Rim;
            half4 _RimColor;
            half4 _ShadowColor;
            half _AmbientBoost;
            half _Toon;
            half _Breakup;
            float _BreakupScale;
            half _Translucency;
            half _StrataOn, _BandH, _BandOffset, _StrataCount, _StrataWarp, _FogTint;
            half4 _S0, _S1, _S2, _S3, _S4, _S5;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fog : TEXCOORD2;
            };

            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3 - 2 * f);
                float a = lerp(lerp(hash31(i), hash31(i + float3(1,0,0)), f.x), lerp(hash31(i + float3(0,1,0)), hash31(i + float3(1,1,0)), f.x), f.y);
                float b = lerp(lerp(hash31(i + float3(0,0,1)), hash31(i + float3(1,0,1)), f.x), lerp(hash31(i + float3(0,1,1)), hash31(i + float3(1,1,1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half3 n = normalize(i.normalWS) * (front ? 1 : -1);
                half3 albedo = pow(max(i.color.rgb, 0), 2.2);
                float3 wp = i.positionWS * _BreakupScale;
                float nz = vnoise(wp) * 0.6 + vnoise(wp * 2.7 + 13.1) * 0.4;
                albedo *= 1 + (nz - 0.5) * 2 * _Breakup;
                if (_StrataOn > 0.5)
                {
                    half3 nn = normalize(i.normalWS);
                    half cliff = smoothstep(0.8, 0.6, abs(nn.y));
                    float y = i.positionWS.y + (vnoise(float3(i.positionWS.xz * 0.06, 3.7)) - 0.5) * _StrataWarp * 2;
                    float bf = (y + _BandOffset) / _BandH;
                    float b = floor(bf);
                    float k = b - _StrataCount * floor(b / _StrataCount);
                    half3 sc = k < 0.5 ? _S0.rgb : k < 1.5 ? _S1.rgb : k < 2.5 ? _S2.rgb : k < 3.5 ? _S3.rgb : k < 4.5 ? _S4.rgb : _S5.rgb;
                    float fb = frac(bf);
                    sc *= 0.8 + 0.2 * smoothstep(0.0, 0.07, fb) + 0.08 * smoothstep(0.6, 1.0, fb);
                    sc *= 1 + (nz - 0.5) * 0.3;
                    albedo = lerp(albedo, sc, cliff);
                }

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, light.direction);
                half diffuse = saturate((ndl + _Wrap) / (1 + _Wrap));
                half shelf = smoothstep(0.18, 0.32, diffuse) * 0.75 + diffuse * 0.25;
                diffuse = lerp(diffuse, shelf, _Toon);
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half trans = saturate(dot(-viewDir, light.direction)) * _Translucency * saturate(-ndl + 0.3);
                half lit = saturate(diffuse * light.shadowAttenuation + trans * light.shadowAttenuation) * light.distanceAttenuation;
                half3 ambient = SampleSH(n) * _AmbientBoost;
                half3 lighting = light.color * lit + ambient + _ShadowColor.rgb * (1 - lit);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    lighting *= ao.indirectAmbientOcclusion;
                #endif
                half rim = pow(1 - saturate(dot(n, viewDir)), 3) * _Rim;
                half3 color = albedo * lighting + rim * _RimColor.rgb * (0.4 + lit * 0.6) * albedo * 1.5;
                color += albedo * (1 - i.color.a) * _Glow;
                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half3 n = normalize(i.normalWS) * (front ? 1 : -1);
                return half4(NormalizeNormalPerPixel(n), 0);
            }
            ENDHLSL
        }
    }
}
