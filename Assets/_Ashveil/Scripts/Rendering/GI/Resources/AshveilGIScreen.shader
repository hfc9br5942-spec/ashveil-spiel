// ASHVEIL Dynamisches GI – Bildpass: fuer jedes sichtbare Pixel (Tiefe + Normale aus dem URP-Vorpass) das indirekte
// Licht aus dem Probengitter holen. Das Ergebnis geht als "Screen Space Irradiance" an URP; Lit, TerrainLit und das
// ASHVEIL-Gras nehmen es dann statt des flachen Umgebungslichts (bakedGI). SSAO wirkt wie bisher obendrauf.
// Ausserhalb des Gitters: das bisherige Umgebungslicht (Dreifarb-Verlauf der Atmosphaere) als Rueckfall.
Shader "Hidden/Ashveil/GIScreen"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "AshGIScreen"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "../ShaderLibrary/AshGICommon.hlsl"

            half4 _AshAmbientSky, _AshAmbientEquator, _AshAmbientGround;
            float4 _AshGIScreenParams;   // x Ansicht (0 normal, 1 nur GI weiss, 2 Gueltigkeit), y Rueckfall-Staerke

            half3 AmbientFallback(float3 n)
            {
                return n.y > 0 ? lerp(_AshAmbientEquator.rgb, _AshAmbientSky.rgb, n.y)
                               : lerp(_AshAmbientEquator.rgb, _AshAmbientGround.rgb, -n.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float depth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                if (depth <= 1e-7) return 0;
            #else
                if (depth >= 1.0 - 1e-7) return 0;
            #endif
                float3 p = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                // 10.10.2026: Normale ohne Laenge (Netze, denen der Build die Normalen nimmt, z. B. Terrain-Detail-Vorlagen) ergab
                // normalize(0) = NaN → Oktaeder-Kodierung NaN → Pixel schwarz (Wiese im Spieltest r2.5). Jetzt Rueckfall nach oben.
                float3 nRoh = SampleSceneNormals(uv);
                float3 n = dot(nRoh, nRoh) > 1e-6 ? normalize(nRoh) : float3(0, 1, 0);
                float3 toEye = normalize(_WorldSpaceCameraPos.xyz - p);
                float valid;
                float3 gi = AshGISample(p, n, toEye, valid);
                if (valid < 0.5) gi = AmbientFallback(n) * _AshGIScreenParams.y;
                if (_AshGIScreenParams.x > 1.5) gi = valid > 0.5 ? float3(0, 1, 0) : float3(1, 0, 0);
                // Nie NaN/Inf in die Irradiance-Textur (haette oben schon gegriffen; Schutz fuer alles Weitere)
                gi = max(gi, 0);
                if (any(isnan(gi)) || any(isinf(gi))) gi = AmbientFallback(n) * _AshGIScreenParams.y;
                return half4(gi, 1);
            }
            ENDHLSL
        }
    }
}
