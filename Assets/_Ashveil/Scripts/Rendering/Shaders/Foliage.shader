// Ashveil – Vegetation (Gras, Farn, Kiefernkronen): Pixeltextur, Hauptlicht mit Schatten, Umgebungslicht,
// sanfter Wind nach Hoehe im Objekt, beidseitig, optional Alpha-Ausschnitt. Terrain-Details (Instancing) tauglich.
// 10.10.2026 (Wiesen-Pass, CORE-009): Hoehenverlauf (uv.y = Wurzel dunkel → Spitze hell, Bodenverschattung) und Farbvariation
// je Weltposition (Ton B in Flecken von _VarScale Metern) – eine Wiese in Flaechen statt gleichfoermiger Bueschel.
// Bewusst ueber uv.y statt Vertexfarbe, und Normale mit Rueckfall nach oben: Ein Netz, das nur ueber TerrainData (Detail-Vorlage)
// im Build haengt, verliert dort Normalen und Vertexfarben (Mesh-Optimierung); normalize(0) wurde NaN → die Wiese war im fertigen
// Spiel schwarz, im Editor-Review gruen (10.10.2026, Spieltest r2.5). _RootDark = 0 schaltet den Verlauf ab.
Shader "Ashveil/Foliage"
{
    Properties
    {
        _BaseMap ("Textur", 2D) = "white" {}
        _BaseColor ("Farbe", Color) = (1,1,1,1)
        _Cutoff ("Alpha-Schwelle", Range(0,1)) = 0.5
        _WindStrength ("Windstaerke (m)", Float) = 0.12
        _WindSpeed ("Windtempo", Float) = 1.3
        _SwayHeight ("Hoehe voller Ausschlag (m)", Float) = 0.6
        _Translucency ("Durchscheinen", Range(0,1)) = 0.35
        _NormalUp ("Normale nach oben (weiche Beleuchtung)", Range(0,1)) = 0.6
        _VarColor ("Farbvariation Ton B (Flecken)", Color) = (1,1,1,1)
        _VarScale ("Variation: Fleckgroesse (m)", Float) = 10
        _RootDark ("Wurzel dunkler (Vertexfarbe r)", Range(0,1)) = 0
        _NearFade ("Kameranaehe ausblenden bis (m)", Float) = 1.2
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha-Ausschnitt", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            float _WindStrength;
            float _WindSpeed;
            float _SwayHeight;
            half _Translucency;
            half _NormalUp;
            half4 _VarColor;
            float _VarScale;
            half _RootDark;
        CBUFFER_END

        // Kameranaehe (10.10.2026, Optik Durchgang 2): Vegetation dichter als _NearFade m an der Kamera wird per Dither ausgeblendet –
        // sonst streichen Kiefernkronen und Halme als schwarze Flaechen durchs Bild, wenn die Kamera am Hang zwischen Baeumen steht
        // (Muldenrand, Flacker-Rekorder). Gleiche Regel in ForwardLit, DepthOnly und DepthNormals; Schatten bleiben.
        float _NearFade;
        void AshNearFade(float3 positionWS, float2 pixel)
        {
            float d = distance(positionWS, _WorldSpaceCameraPos.xyz);
            float f = saturate((d - 0.35) / max(_NearFade, 0.05));
            if (f >= 1.0) return;
            // 4x4-Bayer
            uint2 q = uint2(pixel) & 3;
            const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            float schwelle = (bayer[q.y * 4 + q.x] + 0.5) / 16.0;
            clip(f - schwelle);
        }

        // Wertrauschen (bilinear aus Hashes) fuer Farbflecken je Weltposition – billig, ohne Textur
        float AshHash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float AshValueNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = AshHash2(i), b = AshHash2(i + float2(1, 0)), c = AshHash2(i + float2(0, 1)), d = AshHash2(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        half4 _AshAmbientSky, _AshAmbientEquator, _AshAmbientGround;

        float3 ApplyWind(float3 positionOS, float3 positionWS)
        {
            float h = saturate(positionOS.y / max(_SwayHeight, 0.01));
            float w = h * h;
            float t = _Time.y * _WindSpeed;
            float phase = positionWS.x * 0.21 + positionWS.z * 0.17;
            float gust = 0.6 + 0.4 * sin(t * 0.37 + positionWS.x * 0.013 + positionWS.z * 0.011);
            float2 sway = float2(sin(t + phase), cos(t * 0.83 + phase * 1.3) * 0.6) * _WindStrength * gust * w;
            return positionWS + float3(sway.x, 0, sway.y);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // Dynamisches GI (Screen Space Irradiance von URP): indirektes Licht je Pixel statt des Dreifarb-Verlaufs
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half hoehe : TEXCOORD3;   // uv.y des Netzes vor der Kachelung: 0 Wurzel … 1 Spitze
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                ws = ApplyWind(input.positionOS.xyz, ws);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.hoehe = saturate(input.uv.y);
                return o;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            #if defined(_ALPHATEST_ON)
                clip(tex.a - _Cutoff);
            #endif
                AshNearFade(input.positionWS, input.positionCS.xy);
                float3 nws = input.normalWS;
                float3 n = dot(nws, nws) > 1e-6 ? normalize(nws) : float3(0, 1, 0);   // ohne Normale (Build-Optimierung): nach oben
                n *= frontFace ? 1.0 : -1.0;
                // Gras/Nadeln wie eine Flaeche beleuchten: Normale Richtung Himmel ziehen (kein Schwarz auf Rueckseiten)
                n = normalize(lerp(n, float3(0, 1, 0), _NormalUp));
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                // Nur der Schatten daempft: Die Sonne hat keinen Abstandsabfall; distanceAttenuation traegt bei URP ohne Forward+-Variante
                // unity_LightData.z (Sichtbarkeitsmaske je Objekt), das fuer Terrain-Details nie gesetzt wird (0 → Gras ohne Sonne,
                // REND-026/REND-028). Fuer Vegetation ist 1 immer richtig.
                half atten = mainLight.shadowAttenuation;
                half ndl = saturate(dot(n, mainLight.direction));
                // Durchscheinen: Licht von hinten laesst Halme/Nadeln warm leuchten
                half back = saturate(dot(-n, mainLight.direction)) * _Translucency;
                half3 direct = mainLight.color * atten * (ndl * 0.85 + 0.15 + back);
            #if defined(_SCREEN_SPACE_IRRADIANCE)
                half3 ambient = SampleScreenSpaceGI(input.positionCS.xy);
            #else
                half3 ambient = n.y > 0 ? lerp(_AshAmbientEquator.rgb, _AshAmbientSky.rgb, n.y)
                                        : lerp(_AshAmbientEquator.rgb, _AshAmbientGround.rgb, -n.y);
            #endif
                // Wiesen-Pass: Wurzel dunkler, Boden verschattet, Farbflecken je Weltposition (Ton B)
                half fleck = (half)AshValueNoise(input.positionWS.xz / max(_VarScale, 0.5)) * 0.65 +
                             (half)AshValueNoise(input.positionWS.xz / max(_VarScale * 0.31, 0.5)) * 0.35;
                half3 tint = lerp(half3(1, 1, 1), _VarColor.rgb, saturate(fleck * 1.3 - 0.15));
                half h = input.hoehe;
                half3 albedo = tex.rgb * tint * lerp(1.0 - _RootDark, 1.0, h) * lerp(1.0 - 0.6 * _RootDark, 1.0, h);
                half3 rgb = albedo * (direct + ambient);
                return half4(rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 ws = ApplyWind(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                float3 nws = TransformObjectToWorldNormal(input.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            #endif
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
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 ws = ApplyWind(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                o.positionCS = TransformWorldToHClip(ws);
                o.positionWS = ws;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            #endif
                AshNearFade(input.positionWS, input.positionCS.xy);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 ws = ApplyWind(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                o.positionCS = TransformWorldToHClip(ws);
                o.positionWS = ws;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            #endif
                AshNearFade(input.positionWS, input.positionCS.xy);
                // Ohne Normale (Build nimmt sie Terrain-Detail-Vorlagen): nach oben, sonst bekommt das GI eine Null-Normale (NaN)
                float3 n = input.normalWS;
                return half4(dot(n, n) > 1e-6 ? NormalizeNormalPerPixel(n) : float3(0, 1, 0), 0.0);
            }
            ENDHLSL
        }
    }
}
