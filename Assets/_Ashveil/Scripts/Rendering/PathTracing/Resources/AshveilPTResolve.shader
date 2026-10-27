// ASHVEIL Pathtracer – Aufloesung der Akkumulation (Summe / Probenzahl) in lineares HDR fuer die normale
// Nachbearbeitung, oder eine Diagnose-Ansicht (Albedo, Normale, Tiefe, Probenzahl, Varianz, NaN rot).
Shader "Hidden/Ashveil/PTResolve"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Resolve"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            Texture2D<float4> _AshPTAccumTex;
            Texture2D<float4> _AshPTMomentsTex;
            Texture2D<float4> _AshPTAlbedoTex;
            Texture2D<float4> _AshPTNormalDepthTex;
            Texture2D<float4> _AshPTDenoisedTex;
            float4 _AshPTResolveParams;   // x Ansicht, y Probenziel (Heatmap), z Tiefen-Skala, w entrauscht (0/1)
            float4 _AshPTSizeTex;         // x Breite, y Hoehe
            float4 _AshPTResolveParams2;  // x Belichtung (Faktor, Kamera-Einstellung – nicht im Integrator)

            float3 Heat(float t)
            {
                t = saturate(t);
                return saturate(float3(1.5 - abs(4 * t - 3), 1.5 - abs(4 * t - 2), 1.5 - abs(4 * t - 1)));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                int2 px = int2(input.texcoord * _AshPTSizeTex.xy);
                px = clamp(px, int2(0, 0), int2(_AshPTSizeTex.xy) - 1);
                float4 acc = _AshPTAccumTex.Load(int3(px, 0));
                float n = max(acc.a, 1.0);
                int view = (int)_AshPTResolveParams.x;
                float3 c = acc.rgb / n;
                if (_AshPTResolveParams.w > 0.5 && (view == 0 || view >= 4 && view <= 8))
                    c = _AshPTDenoisedTex.Load(int3(px, 0)).rgb;
                if (view == 0 || (view >= 4 && view <= 8)) c *= _AshPTResolveParams2.x;
                if (view == 1) c = _AshPTAlbedoTex.Load(int3(px, 0)).rgb / n;
                else if (view == 2) c = normalize(_AshPTNormalDepthTex.Load(int3(px, 0)).xyz + 1e-6) * 0.5 + 0.5;
                else if (view == 3) c = (_AshPTNormalDepthTex.Load(int3(px, 0)).w / n * _AshPTResolveParams.z).xxx;
                else if (view == 9) c = Heat(acc.a / max(_AshPTResolveParams.y, 1.0));
                else if (view == 10)
                {
                    float4 m = _AshPTMomentsTex.Load(int3(px, 0));
                    float mean = m.x / n;
                    float var = max(0.0, m.y / n - mean * mean);
                    float relErr = sqrt(var / n) / max(mean, 1e-3);   // relativer Standardfehler des Mittelwerts
                    c = Heat(relErr * 10.0);
                }
                float4 mm = _AshPTMomentsTex.Load(int3(px, 0));
                if (mm.z > 0.5 && view == 11) c = float3(1, 0, 0);
                if (view == 11 && mm.z <= 0.5) c = acc.rgb / n * 0.25;
                return float4(max(c, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
}
