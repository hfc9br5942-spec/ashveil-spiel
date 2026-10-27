// Ashveil – Dynamic Sky V1 (Himmelskuppel). Alles prozedural auf der GPU, Werte kommen global vom SkyDirector:
// Tagesverlauf mit Horizont und Daemmerungsleuchten, Sonnenscheibe, zwei Monde mit Phase, drehender Sternenhimmel
// (Zellen-Sterne + eigene Sternbilder + Milchstrasse), zwei Wolkenschichten mit Wind, Entstehen/Zerfallen und
// einfacher Beleuchtung, Horizontdunst. Stufen: ASHSKY_LOW (eine Schicht), Standard, ASHSKY_HIGH (mehr Detail).
Shader "Ashveil/DynamicSky"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            Name "DynamicSky"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ ASHSKY_LOW ASHSKY_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "DynamicSkyCore.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float pixAng = length(fwidth(d));        // Winkelgroesse eines Pixels (einmal, ausserhalb von Verzweigungen)
                return half4(AshDynamicSky(d, pixAng, 1.0), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
