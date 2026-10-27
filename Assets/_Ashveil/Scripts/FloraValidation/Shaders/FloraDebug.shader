// ASHVEIL Flora-Validation – Diagnose-Ansichten (nur Testszene):
//   Overdraw: additiv, ZTest Always, je Fragment +_Increment im Rotkanal (Schichten pro Pixel, obere Schranke)
//   LOD-Farben: unbeleuchtete Einfaerbung je LOD-Stufe
// Alpha-Clipping wie im Originalmaterial; Impostor-Billboard wie FloraImpostor (vereinfacht: naechste Ansicht).
Shader "Ashveil/FloraValidation/FloraDebug"
{
    Properties
    {
        _BaseMap ("Albedo / Atlas", 2D) = "white" {}
        _Cutoff ("Alpha-Schwelle", Range(0,1)) = 0.5
        _DebugColor ("Farbe", Color) = (1,0,0,1)
        _Increment ("Overdraw-Schritt", Float) = 1
        _AlphaClip ("Alpha-Clipping", Float) = 0
        _Impostor ("Impostor", Float) = 0
        _OctBox ("Oktaeder-Box", Vector) = (0,10,0,10)
        _OctGrid ("Raster", Float) = 8
        _OctExt ("Rahmen r/h/aktiv", Vector) = (0,0,0,0)
        _MipAlpha ("Alpha-Schaerfung je Mip", Float) = 0
        _Mode ("Modus 0 Overdraw / 1 Farbe", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
        _ZWrite ("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Debug"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZTest [_ZTest]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Cutoff;
                half4 _DebugColor;
                float _Increment;
                half _AlphaClip;
                half _Impostor;
                float4 _OctBox;
                float _OctGrid;
                float4 _OctExt;
                float4 _BaseMap_TexelSize;
                half _MipAlpha;
                half _Mode;
                half _Cull; half _SrcBlend; half _DstBlend; half _ZTest; half _ZWrite;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 dirOS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float2 OctEnc(float3 d) { d.y = max(d.y, 0.0); d /= (abs(d.x) + abs(d.y) + abs(d.z) + 1e-6); return float2(d.x + d.z, d.z - d.x); }
            float3 OctDec(float2 cell, float G)
            {
                float2 u = cell / (G - 1.0) * 2.0 - 1.0;
                float x = (u.x - u.y) * 0.5, z = (u.x + u.y) * 0.5, y = 1.0 - abs(x) - abs(z);
                if (y < 0.0) { float s2 = abs(x) + abs(z); x /= s2; z /= s2; y = 0.0; }
                return normalize(float3(x, max(y, 0.02), z));
            }
            float HalfH(float dy) { return _OctExt.z > 0.5 ? _OctExt.y * sqrt(saturate(1.0 - dy * dy)) + _OctExt.x * abs(dy) : _OctBox.w; }
            float HalfW() { return _OctExt.z > 0.5 ? _OctExt.x : _OctBox.w; }

            V Vert(A v)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.uv = v.uv;
                if (_Impostor > 0.5)
                {
                    float3 c = TransformObjectToWorld(_OctBox.xyz);
                    float s = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                    float3 z = normalize(GetCameraPositionWS() - c);
                    float3 x = normalize(cross(float3(0, 1, 0), z) + float3(1e-5, 0, 0));
                    float3 y = cross(z, x);
                    float2 q = v.uv * 2.0 - 1.0;
                    float3 d = normalize(TransformWorldToObjectDir(z, false)); d.x = -d.x;
                    float2 cell = round((OctEnc(d) * 0.5 + 0.5) * (_OctGrid - 1.0));
                    float3 wp = c + (x * q.x * HalfW() + y * q.y * HalfH(OctDec(cell, _OctGrid).y)) * s;
                    o.dirOS = d;
                    o.positionCS = TransformWorldToHClip(wp);
                }
                else
                {
                    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                    o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                }
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half a = 1;
                if (_Impostor > 0.5)
                {
                    float2 f = (OctEnc(normalize(i.dirOS)) * 0.5 + 0.5) * (_OctGrid - 1.0);
                    float2 a2 = (round(f) + i.uv) / _OctGrid; a2.y = 1.0 - a2.y;
                    a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, a2).a;
                    float2 px = i.uv * _BaseMap_TexelSize.z / _OctGrid;
                    float mip = 0.5 * log2(max(max(dot(ddx(px), ddx(px)), dot(ddy(px), ddy(px))), 1e-8));
                    a *= 1.0 + max(mip, 0.0) * _MipAlpha;
                    clip(a - _Cutoff);
                }
                else if (_AlphaClip > 0.5)
                {
                    a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a;
                    float2 px = i.uv * _BaseMap_TexelSize.zw;
                    float mip = 0.5 * log2(max(max(dot(ddx(px), ddx(px)), dot(ddy(px), ddy(px))), 1e-8));
                    a *= 1.0 + max(mip, 0.0) * _MipAlpha;
                    clip(a - _Cutoff);
                }
                if (_Mode < 0.5) return half4(_Increment, 0, 0, 1);
                return half4(_DebugColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
