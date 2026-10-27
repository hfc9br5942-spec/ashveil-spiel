// ASHVEIL Pathtracer – backt den Spielhimmel (DynamicSkyCore, dieselben globalen Werte vom SkyDirector) in eine
// Lat-Long-Karte. Die Sonnenscheibe bleibt weg: der Pathtracer behandelt sie als eigenes Licht mit Winkelgroesse,
// damit der winzige, sehr helle Bereich das Himmels-Sampling nicht verdirbt.
// Abbildung (muss zu AshPTLights.hlsl passen): u = atan2(z, x)/2π + 0,5, v = acos(y)/π, Zeile 0 = oben (Zenit).
Shader "Hidden/Ashveil/PTSkyBake"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "SkyBake"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #define ASHSKY_HIGH 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "../../Shaders/DynamicSkyCore.hlsl"

            float4 _AshPTBakeSize;    // x Breite, y Hoehe
            float4 _AshPTBakeGround;  // rgb Bodenfarbe unter dem Horizont (Himmelsshader liefert dort schon eine), w Modus 0 Himmel, 1 Verlauf
            float4 _AshPTGradSky, _AshPTGradEquator, _AshPTGradGround;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionCS.xy / _AshPTBakeSize.xy;
                float phi = (uv.x - 0.5) * 6.28318530718;
                float theta = uv.y * 3.14159265359;
                float st = sin(theta);
                float3 d = float3(st * cos(phi), cos(theta), st * sin(phi));
                float pixAng = 3.14159265359 / _AshPTBakeSize.y;
                float3 c;
                if (_AshPTBakeGround.w > 0.5)
                {
                    // Verlauf aus den URP-Umgebungsfarben (Szenen ohne Dynamic Sky, z. B. Labor)
                    float y = d.y;
                    c = y >= 0 ? lerp(_AshPTGradEquator.rgb, _AshPTGradSky.rgb, saturate(y)) : lerp(_AshPTGradEquator.rgb, _AshPTGradGround.rgb, saturate(-y));
                }
                else c = AshDynamicSky(d, pixAng, 0.0);
                return float4(max(c, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
}
