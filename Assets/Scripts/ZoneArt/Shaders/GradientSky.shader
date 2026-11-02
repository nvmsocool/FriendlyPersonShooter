// Starter skybox: three-color gradient (ground, horizon, zenith), a sun disk with halo following the
// main light, and soft noise bands. Use it as a material for RenderSettings.skybox, or write your own.
Shader "ZoneArt/GradientSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.2, 0.45, 0.95, 1)
        _Horizon ("Horizon", Color) = (0.95, 0.75, 0.6, 1)
        _Ground ("Below horizon", Color) = (0.35, 0.3, 0.35, 1)
        _HorizonSharpness ("Horizon sharpness", Range(0.5, 12)) = 3
        _SunColor ("Sun color", Color) = (1, 0.9, 0.7, 1)
        _SunSize ("Sun size", Range(0.0005, 0.1)) = 0.01
        _SunHalo ("Sun halo", Range(0, 2)) = 0.6
        _Bands ("Band strength", Range(0, 1)) = 0.15
        _BandColor ("Band color", Color) = (1, 0.6, 0.8, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Horizon, _Ground, _SunColor, _BandColor;
                half _HorizonSharpness, _SunSize, _SunHalo, _Bands;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half up = saturate(d.y);
                half3 sky = lerp(_Horizon.rgb, _Zenith.rgb, pow(up, 1.0 / _HorizonSharpness));
                half3 color = d.y >= 0 ? sky : lerp(_Horizon.rgb, _Ground.rgb, saturate(-d.y * 4));
                half band = sin(d.y * 40 + sin(atan2(d.z, d.x) * 3) * 1.5) * 0.5 + 0.5;
                color = lerp(color, _BandColor.rgb, band * _Bands * (1 - up) * step(0, d.y));
                float3 sunDir = _MainLightPosition.xyz;
                float sd = 1 - dot(d, sunDir);
                color += _SunColor.rgb * (smoothstep(_SunSize, _SunSize * 0.6, sd) * 6 + exp(-sd * 30) * _SunHalo);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
