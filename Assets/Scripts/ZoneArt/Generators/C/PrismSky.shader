// Prismatica skybox: painterly gradient, swirling stylized clouds with posterized shading, layered
// horizon cloud banks, daylight stars, up to three moons, a striped ringed planet, an aurora and sun rays.
Shader "ZoneArtC/PrismSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.2, 0.4, 0.9, 1)
        _Mid ("Mid sky", Color) = (0.5, 0.7, 1, 1)
        _Horizon ("Horizon", Color) = (1, 0.85, 0.9, 1)
        _Below ("Below horizon", Color) = (0.9, 0.8, 0.85, 1)
        _MidHeight ("Mid height", Range(0.02, 0.8)) = 0.25
        _SunColor ("Sun color", Color) = (1, 0.95, 0.8, 1)
        _SunSize ("Sun size", Range(0.0005, 0.05)) = 0.004
        _SunHalo ("Sun halo", Range(0, 3)) = 0.8
        _SunRays ("Sun rays", Range(0, 1)) = 0.3
        _CloudLit ("Cloud lit", Color) = (1, 1, 1, 1)
        _CloudShade ("Cloud shade", Color) = (0.9, 0.6, 0.8, 1)
        _CloudRim ("Cloud rim", Color) = (1, 0.95, 0.7, 1)
        _CloudCover ("Cloud cover threshold", Range(0, 1)) = 0.55
        _CloudScale ("Cloud scale", Float) = 1.2
        _CloudSeed ("Cloud seed", Vector) = (0, 0, 0, 0)
        _Swirl1 ("Swirl 1 (uv, radius, strength)", Vector) = (0.5, 0.3, 0.8, 3)
        _Swirl2 ("Swirl 2 (uv, radius, strength)", Vector) = (-0.8, -0.4, 0.6, -2.5)
        _Streak ("Brush streaks", Range(0, 1)) = 0.35
        _BankBack ("Bank back", Color) = (1, 0.9, 0.95, 1)
        _BankMid ("Bank mid", Color) = (1, 0.8, 0.9, 1)
        _BankFront ("Bank front", Color) = (0.9, 0.7, 0.9, 1)
        _BankHeight ("Bank height", Range(0, 0.4)) = 0.12
        _BankSeed ("Bank seed", Float) = 0
        _Stars ("Stars", Range(0, 2)) = 0.6
        _StarColor ("Star color", Color) = (1, 1, 1, 1)
        _Moon1 ("Moon 1 (dir, radius)", Vector) = (0.3, 0.5, 0.8, 0.04)
        _Moon1Col ("Moon 1 color", Color) = (1, 0.95, 0.9, 1)
        _Moon2 ("Moon 2 (dir, radius)", Vector) = (-0.6, 0.3, 0.7, 0.02)
        _Moon2Col ("Moon 2 color", Color) = (0.8, 0.9, 1, 1)
        _Moon3 ("Moon 3 (dir, radius)", Vector) = (0, 0, 0, 0)
        _Moon3Col ("Moon 3 color", Color) = (1, 0.8, 0.9, 1)
        _Planet ("Planet (dir, radius)", Vector) = (0.5, 0.25, -0.8, 0.18)
        _PlanetA ("Planet stripe A", Color) = (1, 0.7, 0.5, 1)
        _PlanetB ("Planet stripe B", Color) = (0.9, 0.5, 0.7, 1)
        _PlanetC ("Planet stripe C", Color) = (1, 0.95, 0.85, 1)
        _PlanetParams ("Planet (stripes, tilt, ring tilt, ring on)", Vector) = (9, 0.4, 0.28, 1)
        _RingColor ("Ring color", Color) = (1, 0.9, 0.8, 1)
        _SkyRing ("Sky ring (normal xyz, width)", Vector) = (0, 0, 0, 0)
        _SkyRingCol ("Sky ring color", Color) = (1, 0.95, 0.85, 1)
        _Aurora ("Aurora", Range(0, 2)) = 0
        _AuroraA ("Aurora A", Color) = (0.3, 1, 0.7, 1)
        _AuroraB ("Aurora B", Color) = (0.9, 0.3, 1, 1)
        _Exposure ("Exposure", Float) = 1
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
                half4 _Zenith, _Mid, _Horizon, _Below, _SunColor;
                half _MidHeight, _SunSize, _SunHalo, _SunRays;
                half4 _CloudLit, _CloudShade, _CloudRim;
                half _CloudCover; float _CloudScale; float4 _CloudSeed;
                float4 _Swirl1, _Swirl2; half _Streak;
                half4 _BankBack, _BankMid, _BankFront; half _BankHeight; float _BankSeed;
                half _Stars; half4 _StarColor;
                float4 _Moon1, _Moon2, _Moon3; half4 _Moon1Col, _Moon2Col, _Moon3Col;
                float4 _Planet; half4 _PlanetA, _PlanetB, _PlanetC; float4 _PlanetParams; half4 _RingColor;
                half _Aurora; half4 _AuroraA, _AuroraB;
                float4 _SkyRing; half4 _SkyRingCol;
                half _Exposure;
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

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            float fbm(float2 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 5; k++)
                {
                    s += a * vnoise(p);
                    p = p * 2.03 + float2(17.1, -3.7);
                    a *= 0.5;
                }
                return s;
            }

            float fbm3(float2 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 3; k++)
                {
                    s += a * vnoise(p);
                    p = p * 2.1 + float2(-7.3, 11.9);
                    a *= 0.5;
                }
                return s / 0.875;
            }

            float2 swirl(float2 uv, float4 s)
            {
                float2 v = uv - s.xy;
                float r2 = dot(v, v);
                float a = s.w * exp(-r2 / (s.z * s.z));
                float c = cos(a), sn = sin(a);
                return s.xy + float2(v.x * c - v.y * sn, v.x * sn + v.y * c);
            }

            // A body on the sky: returns disc coords (x, y in [-1, 1] when inside) and a basis.
            float2 discCoords(float3 d, float3 center, float radius, out float3 right, out float3 up)
            {
                right = normalize(cross(float3(0, 1, 0), center) + float3(1e-4, 0, 0));
                up = cross(center, right);
                float s = sin(radius);
                return float2(dot(d, right), dot(d, up)) / s * sign(dot(d, center));
            }

            half3 moon(half3 col, float3 d, float4 m, half3 mc, float3 sunDir, float seed)
            {
                if (m.w <= 0) return col;
                float3 c = normalize(m.xyz);
                float3 r, u;
                float2 q = discCoords(d, c, m.w, r, u);
                float rr = dot(q, q);
                float ca = dot(d, c);
                // halo
                col += mc * exp(-max(rr - 1, 0) * 3) * 0.12 * step(0, ca);
                if (rr < 1 && ca > 0)
                {
                    float3 nrm = normalize(r * q.x + u * q.y - c * sqrt(1 - rr));
                    float3 ls = normalize(sunDir + float3(0, 0.2, 0));
                    float lit = smoothstep(-0.05, 0.25, dot(-nrm, -ls) * -1 + 0.35);
                    float crater = smoothstep(0.55, 0.62, vnoise(q * 4 + seed)) * 0.25 + smoothstep(0.6, 0.7, vnoise(q * 9 + seed * 2)) * 0.15;
                    half3 body = mc * (1 - crater);
                    half3 dark = lerp(col, mc * 0.25, 0.6);
                    half edge = smoothstep(1, 0.92, rr);
                    col = lerp(col, lerp(dark, body * 1.15, lit), edge);
                }
                return col;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float elev = d.y;
                float az = atan2(d.z, d.x);
                float3 sunDir = _MainLightPosition.xyz;
                float sunDot = dot(d, sunDir);

                // Gradient.
                float t = saturate(elev);
                half3 sky = lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0, _MidHeight, t));
                sky = lerp(sky, _Zenith.rgb, smoothstep(_MidHeight * 0.6, 1, t));
                // Warm glow around the sun near the horizon.
                sky += _SunColor.rgb * pow(saturate(sunDot * 0.5 + 0.5), 6) * 0.35 * (1 - t);
                half3 col = sky;

                // Daylight stars.
                if (_Stars > 0)
                {
                    float3 p = d * 140;
                    float3 cell = floor(p);
                    float h = hash31(cell);
                    if (h > 0.9)
                    {
                        float3 ctr = cell + 0.5 + (float3(hash31(cell + 3.1), hash31(cell + 7.7), hash31(cell + 1.3)) - 0.5) * 0.6;
                        float dist = length(p - ctr);
                        float size = 0.06 + 0.12 * frac(h * 37.1);
                        float star = smoothstep(size, 0, dist);
                        float3 off = abs(p - ctr);
                        star += (smoothstep(0.03, 0, min(off.x, off.y)) + smoothstep(0.03, 0, min(off.y, off.z))) * smoothstep(size * 4, 0, dist) * 0.35;
                        half3 sc = lerp(_StarColor.rgb, half3(1, 0.85, 1), frac(h * 91.3));
                        col += sc * star * _Stars * smoothstep(0.02, 0.35, elev) * (1 - smoothstep(0.97, 1, sunDot));
                    }
                }

                // Aurora curtains.
                if (_Aurora > 0)
                {
                    float base = 0.22 + 0.08 * sin(az * 2 + _CloudSeed.z) + 0.05 * sin(az * 5.3 + 1.7);
                    float above = elev - base;
                    float curtain = smoothstep(0, 0.02, above) * exp(-max(above, 0) * 7);
                    float rays = 0.45 + 0.55 * pow(saturate(sin(az * 38 + fbm3(float2(az * 6, 3.1)) * 9)), 2);
                    float band = curtain * rays * (0.6 + 0.4 * fbm3(float2(az * 4 + 3, elev * 2)));
                    col += lerp(_AuroraA.rgb, _AuroraB.rgb, saturate(above * 5)) * band * _Aurora;
                }

                // Planet with stripes and rings.
                if (_Planet.w > 0)
                {
                    float3 c = normalize(_Planet.xyz);
                    float3 r, u;
                    float2 q = discCoords(d, c, _Planet.w, r, u);
                    float rr = dot(q, q);
                    bool front = dot(d, c) > 0;
                    float tilt = _PlanetParams.y;
                    float2 qt = float2(q.x * cos(tilt) - q.y * sin(tilt), q.x * sin(tilt) + q.y * cos(tilt));
                    if (front)
                    {
                        col += _PlanetA.rgb * exp(-max(sqrt(rr) - 1, 0) * 10) * 0.25 * step(1, rr);
                        if (rr < 1)
                        {
                            float z = sqrt(1 - rr);
                            float lat = qt.y;
                            float turb = fbm3(float2(qt.x * 3 / (z + 0.3), lat * 10)) - 0.5;
                            float s = sin((lat + turb * 0.08) * _PlanetParams.x * 3.14159);
                            half3 pc = lerp(_PlanetA.rgb, _PlanetB.rgb, smoothstep(-0.3, 0.3, s));
                            pc = lerp(pc, _PlanetC.rgb, smoothstep(0.75, 0.95, sin(lat * _PlanetParams.x * 1.7 + 1.3 + turb)));
                            float3 nrm = r * q.x + u * q.y - c * z;
                            float lit = saturate(dot(-nrm, -sunDir) * 0.6 + 0.65);
                            half rimLight = pow(1 - z, 3);
                            pc = pc * lit + _PlanetA.rgb * rimLight * 0.5;
                            col = lerp(col, pc, smoothstep(1, 0.985, rr));
                        }
                        if (_PlanetParams.w > 0)
                        {
                            float rt = _PlanetParams.z;
                            float ringR = length(float2(qt.x, qt.y / rt));
                            float inRing = smoothstep(1.35, 1.38, ringR) * smoothstep(2.3, 2.25, ringR);
                            float bands = 0.55 + 0.45 * sin(ringR * 34) * sin(ringR * 11 + 1);
                            bool behind = rr < 1 && qt.y > 0;
                            if (inRing > 0 && !behind)
                            {
                                col = lerp(col, _RingColor.rgb * (0.7 + 0.5 * bands), inRing * (0.55 + 0.35 * bands));
                            }
                        }
                    }
                }

                // A planetary ring seen from the surface: a striped band arcing across the whole sky.
                if (_SkyRing.w > 0 && elev > 0)
                {
                    float3 rn = normalize(_SkyRing.xyz);
                    float off = dot(d, rn);
                    float w = _SkyRing.w;
                    float band = smoothstep(w, w * 0.9, abs(off - w * 0.2));
                    float x = (off / w) * 0.5 + 0.5;
                    float stripes = 0.55 + 0.45 * sin(x * 60) * sin(x * 23 + 1.3);
                    float gaps = smoothstep(0.02, 0.035, abs(x - 0.42)) * smoothstep(0.012, 0.025, abs(x - 0.75)) * (0.75 + 0.25 * smoothstep(0.1, 0.3, x));
                    float a = band * gaps * (0.6 + 0.3 * stripes) * smoothstep(0, 0.12, elev);
                    col = lerp(col, _SkyRingCol.rgb * (0.85 + 0.3 * stripes), a);
                }
                col = moon(col, d, _Moon1, _Moon1Col.rgb, sunDir, 1.7);
                col = moon(col, d, _Moon2, _Moon2Col.rgb, sunDir, 5.3);
                col = moon(col, d, _Moon3, _Moon3Col.rgb, sunDir, 9.1);

                // Swirling stylized clouds on a dome.
                if (elev > 0)
                {
                    float2 uv = d.xz / (elev + 0.22) * _CloudScale + _CloudSeed.xy;
                    uv = swirl(uv, _Swirl1 + float4(_CloudSeed.xy, 0, 0));
                    uv = swirl(uv, _Swirl2 + float4(_CloudSeed.xy, 0, 0));
                    float n = fbm(uv * 0.8);
                    float2 dirS = normalize(uv - _Swirl1.xy - _CloudSeed.xy + 1e-3);
                    float2 perp = float2(-dirS.y, dirS.x);
                    float streak = vnoise(float2(dot(uv, dirS) * 3.5, dot(uv, perp) * 0.5) + 5.1);
                    n += (streak - 0.5) * 0.06 * _Streak;
                    float fadeH = smoothstep(0.0, 0.12, elev);
                    float cover = _CloudCover + (1 - fadeH) * 0.25;
                    float m = smoothstep(cover, cover + 0.025, n);
                    float2 sunUV = normalize(sunDir.xz + 1e-4);
                    float shade = n - fbm((uv + sunUV * 0.2) * 0.8);
                    half3 cc = lerp(_CloudShade.rgb, _CloudLit.rgb, smoothstep(-0.03, 0.03, shade));
                    float inner = smoothstep(cover + 0.12, cover + 0.2, n);
                    cc = lerp(cc, lerp(_CloudShade.rgb, _CloudLit.rgb, 0.4), inner * 0.5);
                    float edge = m * (1 - smoothstep(cover + 0.02, cover + 0.06, n));
                    cc += _CloudRim.rgb * edge * 0.5 * (0.5 + 0.5 * pow(saturate(sunDot), 4));
                    cc += _SunColor.rgb * pow(saturate(sunDot), 24) * 0.6;
                    col = lerp(col, cc, m * 0.95);
                }

                // Sun disk, halo, rays.
                float sd = 1 - sunDot;
                col += _SunColor.rgb * (smoothstep(_SunSize, _SunSize * 0.7, sd) * 4 + exp(-sd * 40) * _SunHalo * 0.6 + exp(-sd * 6) * _SunHalo * 0.12);
                if (_SunRays > 0)
                {
                    float3 sr = normalize(cross(float3(0, 1, 0), sunDir) + float3(1e-4, 0, 0));
                    float3 su = cross(sunDir, sr);
                    float ang = atan2(dot(d, su), dot(d, sr));
                    float rays = pow(saturate(sin(ang * 13 + _BankSeed) * 0.5 + 0.5), 6) + pow(saturate(sin(ang * 7 - 1.3 + _BankSeed) * 0.5 + 0.5), 10) * 0.6;
                    col += _SunColor.rgb * rays * exp(-sd * 5) * _SunRays * 0.4 * step(0, sunDot);
                }

                // Layered horizon cloud banks (painted silhouettes).
                float2 ring = float2(cos(az), sin(az));
                half3 bankCols[3] = { _BankBack.rgb, _BankMid.rgb, _BankFront.rgb };
                [unroll]
                for (int k = 0; k < 3; k++)
                {
                    float kk = k;
                    float scale = 2.2 + kk * 1.6;
                    float prof = _BankHeight * (1 - kk * 0.28) * (0.35 + 0.9 * fbm3(ring * scale + _BankSeed + kk * 13.7));
                    prof += _BankHeight * 0.25 * pow(abs(sin(az * (5 + kk * 3) + _BankSeed * 2 + kk)), 3);
                    prof -= 0.015;
                    float bank = smoothstep(prof + 0.002, prof - 0.002, elev);
                    float topLight = smoothstep(prof - 0.04, prof, elev);
                    half3 bc = bankCols[k] * (0.92 + 0.18 * topLight) + _SunColor.rgb * topLight * pow(saturate(sunDot * 0.5 + 0.5), 4) * 0.25;
                    col = lerp(col, bc, bank * step(-0.2, elev));
                }

                // Below the horizon: fade to the fog color.
                col = lerp(col, _Below.rgb, smoothstep(0.0, -0.04, elev));
                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
