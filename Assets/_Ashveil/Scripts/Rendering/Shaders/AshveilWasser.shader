// Ashveil – Wasser (Claude 12.10.2026, Wasser-Familie Westweiden; Art Direction §153 „gemaltes Klarwasser, Kalk klar“).
// Eine Oberfläche für Meer, Seen und Bäche: Farbe nach Wassertiefe (Szenentiefe), Klarheit (flach = durchsichtig), Fresnel-
// Spiegelung des Himmels, gemalte Sonnenglanzlichter, Uferschaum aus der Wellenkachel (B), Fließen entlang der Mesh-UV (Bach).
// Transparent: der Hoehennebel (AtmosphereFogPass, nach dem Himmel) erreicht Transparentes nicht – darum rechnet das Wasser
// denselben Nebel selbst (gleiche globale Parameter wie AtmosphereFog.shader).
// Fernes Meer: Ecken jenseits von 90 % der Sichtweite werden ans hintere Ende des Tiefenbereichs gelegt (wie DistantTerrain),
// so endet die Meeresfläche nie sichtbar am Horizont, auch bei kleiner Sichtweite.
// Hauptlicht: nur Richtung und Farbe (kein distanceAttenuation, TECH-119).
Shader "Ashveil/Wasser"
{
    Properties
    {
        _WellenTex ("Wellen (RG Normale, B Schaum, A Wolke; linear)", 2D) = "gray" {}
        _FlachFarbe ("Farbe flach", Color) = (0.42, 0.66, 0.62, 1)
        _TiefFarbe ("Farbe tief", Color) = (0.05, 0.20, 0.25, 1)
        _TiefeVoll ("Tiefe bis zur vollen Tiefenfarbe (m)", Float) = 6
        _Klarheit ("Sichttiefe (m)", Float) = 2.0
        _WellenMassstab ("Wellenkachel (m)", Float) = 14
        _WellenStaerke ("Wellenstärke", Range(0, 2)) = 0.7
        _Wind ("Wind (xy Richtung, z m/s)", Vector) = (1, 0.2, 0.6, 0)
        _Fliess ("Fließen entlang UV.v (m/s, Bach)", Float) = 0
        _UVWelt ("UV aus Welt-xz (1) oder Mesh (0)", Float) = 1
        _SchaumFarbe ("Schaumfarbe", Color) = (0.92, 0.94, 0.92, 1)
        _SchaumBreite ("Uferschaum (m)", Float) = 1.5
        _SchaumGrund ("Schaum überall (Bachschnellen)", Range(0, 1)) = 0
        _Glanz ("Glätte", Range(0, 1)) = 0.93
        _Glanzlicht ("Sonnenglanz", Range(0, 4)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_WellenTex); SAMPLER(sampler_WellenTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _WellenTex_ST;
                half4 _FlachFarbe, _TiefFarbe, _SchaumFarbe;
                float _TiefeVoll, _Klarheit, _WellenMassstab, _WellenStaerke, _Fliess, _UVWelt, _SchaumBreite, _SchaumGrund, _Glanz, _Glanzlicht;
                float4 _Wind;
            CBUFFER_END

            // globale Nebelparameter (AtmosphereController); nicht gesetzt = 0 = kein Nebel
            float4 _AshFogColor, _AshFogParams, _AshFogParams2, _AshSunColor, _AshFogDir;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float eye : TEXCOORD2;          // echte Augentiefe (auch wenn die Clip-Tiefe nach hinten gelegt ist)
                float4 screen : TEXCOORD3;
            };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float4 cs = TransformWorldToHClip(ws);
                o.eye = -TransformWorldToView(ws).z;
                o.screen = ComputeScreenPos(cs);
                float fern = _ProjectionParams.z * 0.9;
                if (length(ws - _WorldSpaceCameraPos) > fern)
                {
                #if UNITY_REVERSED_Z
                    cs.z = 1e-6 * cs.w;
                #else
                    cs.z = (1.0 - 2e-6) * cs.w;
                #endif
                }
                o.positionCS = cs;
                o.positionWS = ws;
                o.uv = i.uv;
                return o;
            }

            float Nebel(float3 ws, out float3 farbe)
            {
                float3 ray = ws - _WorldSpaceCameraPos;
                float dist = length(ray);
                float3 dir = ray / max(dist, 1e-4);
                float d = max(0.0, dist - _AshFogParams2.x);
                float a = _AshFogParams.x, b = max(_AshFogParams.y, 1e-5);
                float h0 = max(_WorldSpaceCameraPos.y - _AshFogParams.z, 0.0);
                float dy = dir.y * b * d;
                float integral = abs(dy) > 0.0001 ? a * exp(-b * h0) * (1.0 - exp(-dy)) / (b * dir.y) : a * exp(-b * h0) * d;
                float fog = 1.0 - exp(-max(integral, 0.0));
                fog = 1.0 - (1.0 - fog) * exp(-d * _AshFogParams2.z);
                fog = min(fog, _AshFogColor.a);
                fog *= lerp(1.0, 0.55, saturate((dist - 1200.0) / 3000.0));
                float sun = pow(saturate(dot(dir, _AshFogDir.xyz)), max(_AshSunColor.a, 1.0));
                farbe = lerp(_AshFogColor.rgb, _AshSunColor.rgb, sun);
                return saturate(fog);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 basis = _UVWelt > 0.5 ? i.positionWS.xz : i.uv;
                float2 uv = basis / max(_WellenMassstab, 0.01);
                float2 wind = normalize(_Wind.xy + 1e-4) * _Wind.z / max(_WellenMassstab, 0.01);
                float2 fliess = float2(0, -_Fliess / max(_WellenMassstab, 0.01));
                float2 uv1 = uv + wind * t + fliess * t;
                float2 uv2 = float2(uv.x * 0.83 - uv.y * 0.56, uv.x * 0.56 + uv.y * 0.83) * 0.41 - wind * 0.6 * t + fliess * 0.8 * t;
                half4 w1 = SAMPLE_TEXTURE2D(_WellenTex, sampler_WellenTex, uv1);
                half4 w2 = SAMPLE_TEXTURE2D(_WellenTex, sampler_WellenTex, uv2);
                // dritte, sehr große Lage (Dünung) gegen sichtbare Kachelung in der Ferne; feine Lagen blenden mit der
                // Entfernung aus (Entwurf 14: Moiré-Streifen zum Horizont)
                half4 w3 = SAMPLE_TEXTURE2D(_WellenTex, sampler_WellenTex, float2(uv.y * 0.6 + uv.x * 0.8, uv.x * 0.6 - uv.y * 0.8) * 0.083 + wind * 0.15 * t);
                // Entwurf 15: Strahlenmuster zum Fluchtpunkt (gerichtete Wellen der Kachel in der Ferne) → feine Lagen schon ab
                // 60 m ausblenden, in der Ferne trägt nur die Dünung, insgesamt ruhiger (Spiegel des Himmels übernimmt)
                float fern = saturate((i.eye - 60.0) / 700.0);
                float2 nxy = (((w1.rg * 2 - 1) + (w2.rg * 2 - 1) * 0.8) * (1.0 - 0.9 * fern) + (w3.rg * 2 - 1) * 0.6 * (1.0 - 0.5 * fern) * (1.0 - 0.7 * saturate((i.eye - 1500.0) / 2500.0))) * _WellenStaerke;
                float3 n = normalize(float3(nxy.x, 1.0, nxy.y));

                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float2 suv = i.screen.xy / i.screen.w;
                float szene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float dicke = max(0.0, szene - i.eye);                     // Wasser entlang des Sichtstrahls
                if (i.eye > _ProjectionParams.z * 0.85) dicke = 1000.0;      // fernes Meer (Tiefe nach hinten gelegt): tief
                float tiefe = dicke * saturate(v.y + 0.15);                // grob senkrechte Tiefe (flacher Blick ≠ tiefes Wasser)

                // Körperfarbe nach Tiefe, gemalt unruhig (Wolke)
                half3 koerper = lerp(_FlachFarbe.rgb, _TiefFarbe.rgb, 1.0 - exp(-tiefe / max(_TiefeVoll, 0.01)));
                koerper *= 0.94 + 0.12 * (w1.a * 0.6 + w2.a * 0.4);
                Light l = GetMainLight();
                half3 amb = SampleSH(float3(0, 1, 0));
                koerper *= amb * 0.9 + l.color * (0.35 * saturate(l.direction.y) + 0.1);

                // Spiegelung (Himmel/Reflexionsprobe) mit Fresnel
                float nv = saturate(dot(n, v));
                float fresnel = 0.02 + 0.98 * pow(1.0 - nv, 5.0);
                half3 spiegel = GlossyEnvironmentReflection(reflect(-v, n), 1.0 - _Glanz, 1.0);
                half3 farbe = lerp(koerper, spiegel, fresnel);

                // gemalte Sonnenglanzlichter: harte Kante statt weicher Glocke
                float3 hv = normalize(l.direction + v);
                float glanz = pow(saturate(dot(n, hv)), lerp(400.0, 120.0, fern));
                farbe += l.color * smoothstep(0.15, 0.6, glanz) * _Glanzlicht * saturate(l.direction.y * 4.0) * (1.0 - 0.7 * fern);

                // Uferschaum (+ Grundschaum im Bach)
                float ufer = 1.0 - saturate(tiefe / max(_SchaumBreite, 0.01));
                float menge = saturate(ufer * 0.85 + _SchaumGrund * 0.5);
                float muster = SAMPLE_TEXTURE2D(_WellenTex, sampler_WellenTex, uv * 1.7 + wind * 0.3 * t + fliess * 1.2 * t).b;
                float schaum = smoothstep(1.0 - menge, 1.0 - menge + 0.12, muster) * step(0.001, menge);
                half3 schaumLicht = _SchaumFarbe.rgb * (amb * 0.8 + l.color * 0.6);
                farbe = lerp(farbe, schaumLicht, schaum);

                // Durchsicht: flach klar, tief deckend; Spiegelung und Schaum decken immer
                float alpha = 1.0 - exp(-dicke / max(_Klarheit, 0.01));
                alpha = saturate(max(max(alpha, fresnel), schaum));
                alpha *= saturate(dicke * 4.0);                              // weicher Rand an der Uferlinie

                float3 nebelFarbe;
                float nebel = Nebel(i.positionWS, nebelFarbe);
                farbe = lerp(farbe, nebelFarbe, nebel);
                return half4(farbe, alpha);
            }
            ENDHLSL
        }
    }
}
