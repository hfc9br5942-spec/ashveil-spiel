// ASHVEIL Flora-Validation – Diagnose-Lit-Shader fuer F01/F04a/F19/F10/Totholz (PROTOTYPE / DIAGNOSTIC, ART-013 bleibt OPEN).
// Albedo + Normal + Smoothness, Alpha-Clipping, beidseitig, einfache Foliage-Transluzenz, Wind (_WIND, TEXCOORD2),
// Saison (_SEASON, TEXCOORD3), Schnee-Test, LOD-Crossfade (Dither). SRP-Batcher- und GPU-Instancing-kompatibel.
Shader "Ashveil/FloraValidation/FloraLit"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Farbe", Color) = (1,1,1,1)
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normalstaerke", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.15
        _Cutoff ("Alpha-Schwelle", Range(0,1)) = 0.5
        _Translucency ("Transluzenz", Range(0,1)) = 0
        _Foliage ("Foliage (gebogene Normalen, kein Flip)", Float) = 0
        _WindScale ("Windfaktor", Float) = 1
        _MipAlpha ("Alpha-Schaerfung je Mip (Coverage in der Ferne)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha-Clipping", Float) = 0
        [Toggle(_NORMALMAP)] _UseNormalMap ("Normalmap", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Smoothness;
            half _Cutoff;
            half _Translucency;
            half _Foliage;
            half _WindScale;
            half _Cull;
            half _AlphaClip;
            half _UseNormalMap;
            float4 _BaseMap_TexelSize;
            half _MipAlpha;
        CBUFFER_END

        #include "FloraCommon.hlsl"

        // Alpha-Schwelle je Mip (wie im externen Diagnose-Renderer): in der Ferne verlieren gefilterte Nadel-/Blattkarten sonst
        // Deckung (Alpha-Mittelung in den Mips). Alpha wird je Mip-Stufe verstaerkt: a * (1 + mip * _MipAlpha).
        float FV_MipLevel(float2 uv)
        {
            float2 px = uv * _BaseMap_TexelSize.zw;
            float2 dx = ddx(px), dy = ddy(px);
            return 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8));
        }
        half FV_AlphaMip(half a, float2 uv)
        {
            return a * (1.0 + max(FV_MipLevel(uv), 0.0) * _MipAlpha);
        }

        struct FVAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float4 tangentOS  : TANGENT;
            float2 uv         : TEXCOORD0;
            float4 wind       : TEXCOORD2;
            float4 season     : TEXCOORD3;
            half4  color      : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 FV_DisplacedOS(FVAttributes v)
        {
            return v.positionOS.xyz + FV_WindOffsetOS(v.wind * float4(1, _WindScale, 1, 1), v.positionOS.xyz, FV_ObjectPositionWS());
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
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                float4 season     : TEXCOORD4;
                float3 objPosWS   : TEXCOORD5;
                half4  color      : TEXCOORD6;
                half   fogFactor  : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(FVAttributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 pOS = FV_DisplacedOS(v);
                VertexPositionInputs pos = GetVertexPositionInputs(pOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = nrm.normalWS;
                o.tangentWS = float4(nrm.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.season = v.season;
                o.objPosWS = FV_ObjectPositionWS();
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                if (FV_LeafFallen(i.season, i.objPosWS)) discard;
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
            #if defined(_ALPHATEST_ON)
                clip(FV_AlphaMip(tex.a, i.uv) - _Cutoff);
            #endif
                half3 albedo = tex.rgb * _BaseColor.rgb * i.color.rgb;
                albedo = FV_SeasonColor(albedo, i.season, i.objPosWS);

                float3 nGeo = normalize(i.normalWS);
                if (_Foliage < 0.5 && !frontFace) nGeo = -nGeo;
                float3 nWS = nGeo;
            #if defined(_NORMALMAP)
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 t = normalize(i.tangentWS.xyz);
                float3 b = cross(nGeo, t) * i.tangentWS.w;
                nWS = normalize(mul(nTS, float3x3(t, b, nGeo)));
            #endif

                // Schnee-Test (nur technisch): Aufwaertsflaechen, globale Staerke
                if (_FV_SeasonParams.z > 0.0)
                {
                    float n = frac(sin(dot(floor(i.positionWS.xz * 4.0), float2(12.9898, 78.233))) * 43758.5453);
                    half sm = smoothstep(0.35, 0.8, nGeo.y + (n - 0.5) * 0.3) * _FV_SeasonParams.z;
                    albedo = lerp(albedo, half3(0.86, 0.88, 0.92), sm);
                }

                float3 viewWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                // Foliage (beidseitig, Normalen nicht gespiegelt): Normale auf die Betrachterseite spiegeln, sonst liefert der
                // Fresnel-Term bei N.V < 0 volle Himmelsreflexion (blaeulich-weisse "Reif"-Flecken auf Rueckseiten)
                if (_Foliage > 0.5)
                {
                    float nv = dot(nWS, viewWS);
                    if (nv < 0.0) nWS = normalize(nWS - 2.0 * nv * viewWS);
                }
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(nWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.smoothness = _Smoothness;
                s.occlusion = 1;
                s.normalTS = half3(0, 0, 1);
                half4 col = UniversalFragmentPBR(inputData, s);

                if (_Translucency > 0.0)
                {
                    Light ml = GetMainLight(inputData.shadowCoord);
                    half back = pow(saturate(dot(-inputData.viewDirectionWS, ml.direction)), 3.0) * 0.6 + 0.4 * saturate(dot(-nWS, ml.direction));
                    col.rgb += albedo * half3(1.0, 1.04, 0.9) * ml.color * _Translucency * back * ml.shadowAttenuation;
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
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 season : TEXCOORD1;
                float3 objPosWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(FVAttributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 ws = TransformObjectToWorld(FV_DisplacedOS(v));
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 ld = normalize(_LightPosition - ws);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.season = v.season;
                o.objPosWS = FV_ObjectPositionWS();
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                if (FV_LeafFallen(i.season, i.objPosWS)) discard;
            #if defined(_ALPHATEST_ON)
                clip(FV_AlphaMip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a, i.uv) - _Cutoff);
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
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 season : TEXCOORD1;
                float3 objPosWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(FVAttributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(FV_DisplacedOS(v));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.season = v.season;
                o.objPosWS = FV_ObjectPositionWS();
                return o;
            }

            half Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                if (FV_LeafFallen(i.season, i.objPosWS)) discard;
            #if defined(_ALPHATEST_ON)
                clip(FV_AlphaMip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a, i.uv) - _Cutoff);
            #endif
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 season : TEXCOORD2;
                float3 objPosWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(FVAttributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(FV_DisplacedOS(v));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.season = v.season;
                o.objPosWS = FV_ObjectPositionWS();
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                if (FV_LeafFallen(i.season, i.objPosWS)) discard;
            #if defined(_ALPHATEST_ON)
                clip(FV_AlphaMip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a, i.uv) - _Cutoff);
            #endif
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
