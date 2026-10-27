// ASHVEIL Flora-Validation – Oktaeder-Impostor (EXPERIMENTELL, nicht kanonisiert).
// 8x8 Ansichten auf der oberen Hemisphaere, Albedo- und Objektraum-Normal-Atlas aus dem eigenen Offline-Bake (glTF-Konvention:
// x gespiegelt, Atlas-v von oben). Billboard zur Kamera (Schatten: zur Lichtrichtung), bilineare Ueberblendung der 4 naechsten
// Ansichten, dynamisches Licht ueber den Normal-Atlas, LOD-Crossfade.
// v3: seitenverhaeltnis-gerechte Rahmen (_OctExt: horizontaler Radius r, halbe Hoehe h – je Ansicht h*cos(e) + r*sin(e)),
//     Mip-Abtastung mit Gradienten statt LOD 0, Alpha-Schaerfung je Mip (_MipAlpha), Helligkeitsabgleich (_OctGain) in Unity kalibriert.
//     _OctExt.z = 0 -> alter quadratischer Rahmen (Radius _OctBox.w) fuer Vergleiche.
Shader "Ashveil/FloraValidation/FloraImpostor"
{
    Properties
    {
        _BaseMap ("Albedo-Atlas", 2D) = "white" {}
        _NormalAtlas ("Normal-Atlas (Objektraum)", 2D) = "bump" {}
        _OctBox ("Mitte (xyz, Unity-Objektraum) + Radius (w)", Vector) = (0,10,0,10)
        _OctExt ("Rahmen: r (x), halbe Hoehe (y), aktiv (z)", Vector) = (0,0,0,0)
        _OctGrid ("Raster", Float) = 8
        _OctGain ("Helligkeitsabgleich", Float) = 1
        _Cutoff ("Alpha-Schwelle", Range(0,1)) = 0.42
        _MipAlpha ("Alpha-Schaerfung je Mip", Float) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.1
        _Translucency ("Transluzenz", Range(0,1)) = 0.18
        _NormalSign ("Diagnose: Achsvorzeichen Normal-Atlas (glTF->Unity)", Vector) = (-1,1,1,0)
        _NormalBlend ("Normal-Atlas -> Blickrichtung (Kalibrierung)", Range(0,1)) = 0
        _OctAO ("v4: AO im Alpha des Normal-Atlas vorhanden", Float) = 0
        _AOStrength ("v4: AO auf Umgebungslicht", Range(0,1)) = 1
        _AODirect ("v4: AO auf direktes Licht (richtungsneutral)", Range(0,1)) = 0
        _OctBlendSharp ("v4: Schaerfe der Ansichtsueberblendung (1 = linear)", Range(1,4)) = 1
        _CrownC ("Micro-Pass: Kronenmitte (Objektraum, w = vorhanden)", Vector) = (0,0,0,0)
        _CrownR ("Micro-Pass: Kronenradien (Objektraum)", Vector) = (1,1,1,0)
        _MacroBlend ("Micro-Pass: Anteil Kronen-Grossnormale", Range(0,1)) = 0
        _ThickK ("Micro-Pass: Dicke-Daempfung der Transluzenz (Lichtweg)", Range(0,8)) = 0
        _SelfK ("Micro-Pass: Kronen-Eigenschatten (Lichtweg durch das Kronenvolumen)", Range(0,8)) = 0
        _Sheen ("Micro-Pass: Himmelsglanz streifender Blattkarten (aus dem Normal-Atlas je Ansicht)", Range(0,4)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" "DisableBatching" = "False" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalAtlas); SAMPLER(sampler_NormalAtlas);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseMap_TexelSize;
            float4 _OctBox;
            float4 _OctExt;
            float _OctGrid;
            float _OctGain;
            half _Cutoff;
            half _MipAlpha;
            half _Smoothness;
            half _Translucency;
            half _NormalBlend;
            half _OctAO;
            half _AOStrength;
            half _AODirect;
            half _OctBlendSharp;
            float4 _NormalSign;
            float4 _CrownC;
            float4 _CrownR;
            half _MacroBlend;
            half _ThickK;
            half _SelfK;
            half _Sheen;
        CBUFFER_END

        struct ImpAttributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float FV_Scale() { return length(TransformObjectToWorldDir(float3(1, 0, 0), false)); }

        // Ansichtsrichtung einer Atlaszelle (identisch zum Bake: lod2.oct_dir)
        float3 FV_OctDecode(float2 cell, float G)
        {
            float2 u = cell / (G - 1.0) * 2.0 - 1.0;
            float x = (u.x - u.y) * 0.5, z = (u.x + u.y) * 0.5, y = 1.0 - abs(x) - abs(z);
            if (y < 0.0) { float s = abs(x) + abs(z); x /= s; z /= s; y = 0.0; }
            return normalize(float3(x, max(y, 0.02), z));
        }

        float2 FV_OctEncode(float3 d)
        {
            d.y = max(d.y, 0.0);
            d /= (abs(d.x) + abs(d.y) + abs(d.z) + 1e-6);
            return float2(d.x + d.z, d.z - d.x);
        }

        float FV_HalfW() { return _OctExt.z > 0.5 ? _OctExt.x : _OctBox.w; }
        float FV_HalfH(float dy) { return _OctExt.z > 0.5 ? _OctExt.y * sqrt(saturate(1.0 - dy * dy)) + _OctExt.x * abs(dy) : _OctBox.w; }

        void FV_Cells(float3 dirOS_gltf, out float2 i0, out float2 w)
        {
            float G = _OctGrid;
            float2 f = (FV_OctEncode(dirOS_gltf) * 0.5 + 0.5) * (G - 1.0);
            i0 = clamp(floor(f), 0.0, G - 2.0);
            w = saturate(f - i0);
        }

        // groesste Rahmenhoehe der 4 beteiligten Ansichten -> Billboard-Hoehe (kein Abschneiden)
        float FV_BillboardHalfH(float3 dirOS_gltf)
        {
            if (_OctExt.z < 0.5) return _OctBox.w;
            float2 i0, w; FV_Cells(dirOS_gltf, i0, w);
            float hb = 0;
            [unroll] for (int k = 0; k < 4; k++)
            {
                float3 dk = FV_OctDecode(i0 + float2(k & 1, k >> 1), _OctGrid);
                hb = max(hb, FV_HalfH(dk.y));
            }
            return hb;
        }

        // Billboard um die Oktaeder-Mitte; toward = Richtung (Welt) zum Betrachter bzw. zum Licht
        void FV_Billboard(float2 uv, float3 toward, out float3 positionWS, out float3 dirOS_gltf, out float halfH)
        {
            float3 c = TransformObjectToWorld(_OctBox.xyz);
            float s = FV_Scale();
            float3 z = normalize(toward);
            float3 x = cross(float3(0, 1, 0), z);
            if (dot(x, x) < 1e-8) x = cross(float3(0, 0, 1), z);
            x = normalize(x);
            float3 y = cross(z, x);
            // Step E (01.10.): Spiegelungsfehler behoben. Der Bake legt die Bild-u-Achse auf die Kamera-RECHTE (glTF-lookAt),
            // cross(up, z) zeigt in Unity aber zur Kamera-LINKEN -> jedes Fernbild war horizontal gespiegelt (Silhouette und
            // Links/Rechts-Beleuchtung vertauscht). y bleibt aus der alten Basis (zeigt nach oben).
            x = -x;
            float3 d = normalize(TransformWorldToObjectDir(z, false));
            d.x = -d.x;   // Unity -> glTF-Bake-Raum
            dirOS_gltf = d;
            halfH = FV_BillboardHalfH(d);
            float2 q = uv * 2.0 - 1.0;
            positionWS = c + (x * q.x * FV_HalfW() + y * q.y * halfH) * s;
        }

        // Atlas-Abtastung mit Mip-Gradienten. Rueckgabe: Albedo + Alpha, Normal (glTF)
        void FV_SampleAtlasAO2(float2 uv, float3 dirOS_gltf, float halfH, out half4 albedo, out half3 nGltf, out half ao, out half graz)
        {
            half gacc = 0;
            float G = _OctGrid;
            float2 i0, w; FV_Cells(dirOS_gltf, i0, w);
            w = saturate((w - 0.5) * _OctBlendSharp + 0.5);   // v4: schaerfere Ansichtswahl (weniger Doppelkonturen)
            half aoacc = 0;
            float framePx = _BaseMap_TexelSize.z / G;
            half4 acc = 0; half3 nacc = 0; float wsum = 0;
            [unroll] for (int k = 0; k < 4; k++)
            {
                float2 off = float2(k & 1, k >> 1);
                float ww = (off.x > 0.5 ? w.x : 1.0 - w.x) * (off.y > 0.5 ? w.y : 1.0 - w.y);
                float3 dk = FV_OctDecode(i0 + off, G);
                float2 uk = uv;
                uk.y = 0.5 + (uv.y - 0.5) * halfH / max(FV_HalfH(dk.y), 1e-3);
                float inside = (uk.y >= 0.0 && uk.y <= 1.0) ? 1.0 : 0.0;
                uk = saturate(uk);
                float2 gx = ddx(uk) / G, gy = ddy(uk) / G;
                float2 a = (i0 + off + uk) / G;
                a.y = 1.0 - a.y; gx.y = -gx.y; gy.y = -gy.y;
                half4 t = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, a, gx, gy);
                half4 n4 = SAMPLE_TEXTURE2D_GRAD(_NormalAtlas, sampler_NormalAtlas, a, gx, gy);
                half3 n = n4.rgb * 2.0 - 1.0;
                float2 px = uk * framePx;
                float mip = 0.5 * log2(max(max(dot(ddx(px), ddx(px)), dot(ddy(px), ddy(px))), 1e-8));
                t.a = saturate(t.a * (1.0 + max(mip, 0.0) * _MipAlpha)) * inside;
                acc += half4(t.rgb * t.a, t.a) * ww; nacc += n * t.a * ww; aoacc += n4.a * t.a * ww; wsum += ww;
                // Micro-Pass: wie streifend sieht diese Ansicht die Blattkarte? Je Ansicht aus dem eigenen Normal berechnet und erst
                // danach gemittelt – die Normalenmittelung der 4 Ansichten loescht diese Information sonst aus.
                half ndv = abs(dot(normalize(n + 1e-4), dk));
                half f5 = 1.0 - ndv; f5 = f5 * f5; f5 = f5 * f5 * (1.0 - ndv);
                gacc += f5 * t.a * ww;
            }
            albedo = half4(acc.rgb / max(acc.a, 1e-4), acc.a / max(wsum, 1e-4));
            nGltf = normalize(nacc + 1e-4);
            ao = _OctAO > 0.5 ? aoacc / max(acc.a, 1e-4) : 1.0;
            graz = gacc / max(acc.a, 1e-4);
        }

        void FV_SampleAtlasAO(float2 uv, float3 dirOS_gltf, float halfH, out half4 albedo, out half3 nGltf, out half ao)
        {
            half g; FV_SampleAtlasAO2(uv, dirOS_gltf, halfH, albedo, nGltf, ao, g);
        }

        void FV_SampleAtlas(float2 uv, float3 dirOS_gltf, float halfH, out half4 albedo, out half3 nGltf)
        {
            half ao; FV_SampleAtlasAO(uv, dirOS_gltf, halfH, albedo, nGltf, ao);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 dirOS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                float halfH : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(ImpAttributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 c = TransformObjectToWorld(_OctBox.xyz);
                float3 wp; float3 d; float hb;
                FV_Billboard(v.uv, GetCameraPositionWS() - c, wp, d, hb);
                o.positionWS = wp; o.dirOS = d; o.uv = v.uv; o.halfH = hb;
                o.positionCS = TransformWorldToHClip(wp);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                half4 alb; half3 ng;
                half aoS, grazS; FV_SampleAtlasAO2(i.uv, normalize(i.dirOS), i.halfH, alb, ng, aoS, grazS);
                clip(alb.a - _Cutoff);
                ng *= _NormalSign.xyz;   // Standard (-1,1,1): glTF -> Unity (x gespiegelt wie die Meshes)
                float3 nWS = normalize(TransformObjectToWorldDir(ng, false));
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                // Kalibrierbare Daempfung der blickabhaengigen Normal-Atlas-Wirkung (bleibt lichtabhaengig, keine gebackene Richtung)
                // Micro-Pass: Kronen-Grossnormale. Der Sichtstrahl wird mit dem Kronenellipsoid geschnitten; dessen Normale traegt die
                // Makrobeleuchtung (Sonnenseite hell, abgewandte Seite dunkel), der Atlas nur noch die Feinstruktur. Keine gebackene Richtung.
                // Dazu der Lichtweg vom sichtbaren Kronenpunkt zur aktuellen Sonne durch dasselbe Volumen: daraus Eigenschatten der Krone
                // und Daempfung der Transluzenz. Die Sonnenrichtung kommt zur Laufzeit, nichts ist gebacken.
                half thickT = 0, selfLen = 0, rimW = 0;
                if (_CrownC.w > 0.5 && (_MacroBlend > 0.001 || _ThickK > 0.001 || _SelfK > 0.001))
                {
                    float sc0 = FV_Scale();
                    float3 cc = TransformObjectToWorld(_CrownC.xyz);
                    float3 rr = max(_CrownR.xyz * sc0, 0.05);
                    float3 ro = GetCameraPositionWS();
                    float3 rd = normalize(i.positionWS - ro);
                    float3 o1 = (ro - cc) / rr, d1 = rd / rr;
                    float qa = dot(d1, d1), qb = dot(o1, d1), qc = dot(o1, o1) - 1.0;
                    float disc = qb * qb - qa * qc;
                    if (disc > 0.0)
                    {
                        float sq = sqrt(disc);
                        float t0 = (-qb - sq) / qa;
                        float3 pE = o1 + d1 * t0;                       // Eintrittspunkt im Einheitsraum
                        float3 nMacro = normalize(pE / rr);              // Ellipsoid-Normale (Welt)
                        float rim = saturate(sq / qa / (0.25 * min(rr.x, min(rr.y, rr.z))));   // weich zum Rand
                        nWS = normalize(lerp(nWS, nMacro, _MacroBlend * rim));
                        float rmax = max(rr.x, max(rr.y, rr.z));
                        thickT = saturate(sq / qa / rmax);          // halbe Sehne / groesster Radius: 0 Rand … 1 Mitte
                        rimW = rim;
                        // sichtbarer Kronenpunkt leicht innerhalb der Huelle (20 % der Sehne), dann Strahl zur Sonne bis zum Austritt
                        float t1 = (-qb + sq) / qa;
                        float3 pI = o1 + d1 * lerp(t0, t1, 0.05);
                        float3 d2 = _MainLightPosition.xyz / rr;
                        float a2 = dot(d2, d2), b2 = dot(pI, d2), c2 = dot(pI, pI) - 1.0;
                        float tex = (-b2 + sqrt(max(b2 * b2 - a2 * c2, 0.0))) / a2;
                        selfLen = saturate(max(tex, 0.0) / (2.0 * rmax));   // Lichtweg / Kronendurchmesser
                    }
                }
                nWS = normalize(lerp(nWS, inputData.viewDirectionWS, _NormalBlend));
                inputData.normalWS = nWS;
                // Eigenschatten-Korrektur: der Schattenwerfer-Billboard steht zur Sonne gedreht durch dieselbe Mitte und wuerde die
                // untere Haelfte des Kamera-Billboards abschatten. Abtastpunkt daher um den Rahmenradius zur Sonne verschieben.
                float sc = FV_Scale();
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS + _MainLightPosition.xyz * max(FV_HalfW(), i.halfH) * sc);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(nWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                half3 baseCol = alb.rgb * _OctGain;
                // v4: richtungsneutrales AO – voll auf Umgebungslicht, anteilig auf direktes Licht; nie auf die Transluzenz (kein falsches Abdunkeln im Gegenlicht)
                s.albedo = baseCol * lerp(1.0, aoS, _AODirect); s.alpha = 1; s.smoothness = _Smoothness;
                s.occlusion = lerp(1.0, aoS, _AOStrength); s.normalTS = half3(0, 0, 1);
                half4 col = UniversalFragmentPBR(inputData, s);
                Light ml = GetMainLight(inputData.shadowCoord);
                // Micro-Pass: Kronen-Eigenschatten – der verdeckte Anteil des direkten Diffuslichts wird abgezogen (Lambert-Naeherung)
                half selfSh = lerp(1.0, exp(-_SelfK * selfLen * 4.0), rimW);
                col.rgb -= s.albedo * 0.96 * ml.color * saturate(dot(nWS, ml.direction)) * ml.shadowAttenuation * (1.0 - selfSh);
                half back = 0.4 * saturate(dot(-nWS, ml.direction)) + 0.6 * pow(saturate(dot(-inputData.viewDirectionWS, ml.direction)), 3.0);
                // Micro-Pass: dicke Kronenbereiche lassen weniger Gegenlicht durch (guenstiger Dicke-Term statt Durchstrahlung)
                col.rgb += baseCol * ml.color * _Translucency * back * ml.shadowAttenuation * exp(-_ThickK * selfLen * 4.0 * rimW);
                // Micro-Pass: Himmelsglanz – streifende Karten spiegeln den Himmel (Fresnel); Himmelsfarbe aus den Umgebungs-SH
                if (_Sheen > 0.001)
                {
                    half3 rv = reflect(-inputData.viewDirectionWS, nWS);
                    col.rgb += max(SampleSH(rv), 0) * grazS * _Sheen * s.occlusion;
                }
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            float3 _LightDirection;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 dirOS : TEXCOORD1;
                float halfH : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(ImpAttributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 wp; float3 d; float hb;
                FV_Billboard(v.uv, _LightDirection, wp, d, hb);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, normalize(_LightDirection), _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs; o.uv = v.uv; o.dirOS = d; o.halfH = hb;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                half4 alb; half3 ng;
                FV_SampleAtlas(i.uv, normalize(i.dirOS), i.halfH, alb, ng);
                clip(alb.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 dirOS : TEXCOORD1;
                float halfH : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(ImpAttributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 c = TransformObjectToWorld(_OctBox.xyz);
                float3 wp; float3 d; float hb;
                FV_Billboard(v.uv, GetCameraPositionWS() - c, wp, d, hb);
                o.positionCS = TransformWorldToHClip(wp); o.uv = v.uv; o.dirOS = d; o.halfH = hb;
                return o;
            }

            half Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                half4 alb; half3 ng;
                FV_SampleAtlas(i.uv, normalize(i.dirOS), i.halfH, alb, ng);
                clip(alb.a - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
}
