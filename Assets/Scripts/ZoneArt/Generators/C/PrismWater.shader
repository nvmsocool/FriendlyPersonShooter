// Prismatica water: translucent tinted sheet with a fresnel sky reflection, painted ripple bands,
// sun glints and soft shading from the main light. No depth texture needed.
Shader "ZoneArtC/PrismWater"
{
    Properties
    {
        _Shallow ("Shallow color", Color) = (0.3, 0.9, 0.9, 0.55)
        _Deep ("Deep color", Color) = (0.1, 0.4, 0.7, 0.85)
        _Sky ("Sky reflection", Color) = (0.9, 0.9, 1, 1)
        _Glint ("Glint color", Color) = (1, 1, 1, 1)
        _Ripple ("Ripple scale", Float) = 0.35
        _SkyH ("Sky horizon", Color) = (1, 0.9, 0.9, 1)
        _SkyZ ("Sky zenith", Color) = (0.3, 0.5, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Deep, _Sky, _Glint, _SkyH, _SkyZ;
                float _Ripple;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half4 color : COLOR; half fog : TEXCOORD1; };

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.color = v.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.positionWS.xz * _Ripple;
                float n1 = vnoise(uv * float2(0.7, 2.2));
                float n2 = vnoise(uv * 1.9 + 7.1);
                float e = 0.12;
                float2 grad = float2(vnoise(uv * float2(0.7, 2.2) + float2(e, 0)) - n1, vnoise(uv * 1.9 + 7.1 + float2(0, e)) - n2);
                float3 nrm = normalize(float3(grad.x * 0.6, 1, grad.y * 0.6));
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float dist = length(GetCameraPositionWS() - i.positionWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float fres = 0.08 + 0.92 * pow(1 - saturate(dot(nrm, viewDir)), 5);
                half depth = i.color.r;
                half4 baseCol = lerp(_Shallow, _Deep, depth);
                half3 col = baseCol.rgb * (0.6 + 0.4 * light.shadowAttenuation);
                float3 r = reflect(-viewDir, nrm);
                half3 skyRefl = lerp(_SkyH.rgb, _SkyZ.rgb, smoothstep(0.0, 0.6, r.y));
                col = lerp(col, lerp(skyRefl, _Sky.rgb, 0.35), saturate(fres * 0.85));
                // Painted ripple lines: thin wavy bands, fading with distance.
                float wave = sin(i.positionWS.x * 0.35 + i.positionWS.z * 0.21 + n2 * 7.0);
                float lines = smoothstep(0.93, 0.985, wave) * (1 - smoothstep(25, 90, dist));
                col = lerp(col, _SkyH.rgb * 1.1, lines * 0.45);
                float3 h = normalize(light.direction + viewDir);
                float spec = pow(saturate(dot(nrm, h)), 160) * light.shadowAttenuation;
                col += _Glint.rgb * spec * 2.5;
                half shore = i.color.g;
                float foam = shore * smoothstep(0.45, 0.6, n2 + shore * 0.3);
                col = lerp(col, half3(1, 1, 1), foam * 0.6);
                half alpha = saturate(baseCol.a + fres * 0.35 + foam * 0.3);
                col = MixFog(col, ComputeFogFactor(TransformWorldToHClip(i.positionWS).z));
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
