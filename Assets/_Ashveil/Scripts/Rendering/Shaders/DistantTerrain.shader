// Fernansicht der Makrowelt (Terrain-Vorgabe §64–66, §121): grobe Gelaende-Proxies bis 40 km. Der Knoten wird je
// Kamera um die Kamera herum verkleinert (DistantTerrainProxyRoot) und liegt so immer innerhalb der Sichtweite;
// die Tiefe wird ans hintere Ende des Tiefenbereichs gelegt – nach echter Entfernung sortiert und stets HINTER
// jeder echten Geometrie. Dadurch keine doppelten Berge: wo echtes Gelaende steht, deckt es den Proxy.
Shader "Ashveil/DistantTerrain"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4 _AshProxyParams;   // x Verkleinerung, y Tiefen-Epsilon, z maximale Entfernung (m), w Dunst-Entfernung (m)

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
        CBUFFER_END

        // Tiefe hinter alle echte Geometrie legen, nach echter Entfernung sortiert
        float4 ProxyClip(float3 ws, out float dist)
        {
            float s = max(_AshProxyParams.x, 1e-6);
            dist = length(ws - _WorldSpaceCameraPos) / s;
            float4 cs = TransformWorldToHClip(ws);
            float k = _AshProxyParams.y * (1.0 - saturate(dist / max(_AshProxyParams.z, 1.0)));
        #if UNITY_REVERSED_Z
            cs.z = k * cs.w;
        #else
            cs.z = (1.0 - 2.0 * k) * cs.w;
        #endif
            return cs;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _AshSkyHaze;
            float4 _AshSkyHorizon;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; half4 color : COLOR; float dist : TEXCOORD1; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = ProxyClip(ws, o.dist);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light l = GetMainLight();
                float ndl = saturate(dot(n, l.direction));
                half3 albedo = i.color.rgb * _Tint.rgb;
                half3 col = albedo * (l.color * ndl * 0.85 + SampleSH(n) * 0.9);
                // Luftperspektive: in die Ferne blau-grau (Nachts dunkel, weil der Horizont dunkel ist)
                float haze = 1.0 - exp(-i.dist / max(_AshProxyParams.w, 1.0));
                half3 hazeCol = lerp(_AshSkyHorizon.rgb, _AshSkyHaze.rgb, 0.5);
                // die Nebel-Nachbearbeitung (AtmosphereFog) legt ohnehin Dunst darueber – hier nur ein Hauch Blau
                col = lerp(col, hazeCol, saturate(haze) * 0.35);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                float d;
                o.positionCS = ProxyClip(TransformObjectToWorld(i.positionOS.xyz), d);
                return o;
            }
            half Frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                float d;
                o.positionCS = ProxyClip(TransformObjectToWorld(i.positionOS.xyz), d);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
