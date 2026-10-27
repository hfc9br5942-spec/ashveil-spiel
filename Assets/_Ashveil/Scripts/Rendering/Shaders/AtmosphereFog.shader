// Ashveil – Hoehennebel mit Sonnenstreuung (Vollbild, nach Opakem + Himmel).
// Dichte: a * exp(-b * (y - baseHeight)); entlang des Sichtstrahls analytisch integriert.
Shader "Hidden/Ashveil/AtmosphereFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "AtmosphereFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _AshFogColor;   // rgb Farbe, a maximale Deckkraft
            float4 _AshFogParams;  // x Dichte, y Hoehenabnahme, z Basishoehe, w Horizontschaerfe
            float4 _AshFogParams2; // x Startentfernung, y Himmelsentfernung, z Dunst
            float4 _AshSunColor;   // rgb Streulicht, a Exponent
            float4 _AshFogDir;     // xyz Richtung zur Sonne

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float rawDepth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool isSky = rawDepth <= 0.000001;
                float depth = rawDepth;
            #else
                bool isSky = rawDepth >= 0.999999;
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
            #endif
                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = worldPos - cam;
                float dist = length(ray);
                float3 dir = ray / max(dist, 1e-4);
                if (isSky) dist = _AshFogParams2.y;

                float d = max(0.0, dist - _AshFogParams2.x);
                float a = _AshFogParams.x;
                float b = _AshFogParams.y;
                float h0 = max(cam.y - _AshFogParams.z, 0.0);   // unter der Nebelbasis (Kerbe, Senken) nicht unbegrenzt dichter
                float dy = dir.y * b * d;
                float fogIntegral;
                if (abs(dy) > 0.0001)
                    fogIntegral = a * exp(-b * h0) * (1.0 - exp(-dy)) / (b * dir.y);
                else
                    fogIntegral = a * exp(-b * h0) * d;
                float fog = 1.0 - exp(-max(fogIntegral, 0.0));
                fog = 1.0 - (1.0 - fog) * exp(-d * _AshFogParams2.z);
                fog = min(fog, _AshFogColor.a);
                // Fernsicht: Gelaende in vielen Kilometern bleibt als Silhouette lesbar (Berge am Horizont
                // sollen locken, nicht im Weiss verschwinden) – Obergrenze ab 1,5 km sanft auf 55 %
                if (!isSky) fog *= lerp(1.0, 0.55, saturate((dist - 1200.0) / 3000.0));
                if (isSky)
                {
                    float horizon = saturate(1.0 - max(dir.y, 0.0) * _AshFogParams.w);
                    fog *= horizon * horizon;
                }

                float sunAmount = pow(saturate(dot(dir, _AshFogDir.xyz)), max(_AshSunColor.a, 1.0));
                float3 fogColor = lerp(_AshFogColor.rgb, _AshSunColor.rgb, sunAmount);
                color.rgb = lerp(color.rgb, fogColor, fog);
                return color;
            }
            ENDHLSL
        }
    }
}
