// ASHVEIL Pathtracer – Oberflaechen-Stellvertreter. Jedes URP-Material bekommt zur Laufzeit ein Gegenstueck mit
// diesem Shader (AshveilRTMaterialRegistry): gleiche Texturen und Werte, aber ein Raytracing-Pass mit
// Closest-Hit (Oberflaechendaten fuer den Integrator) und Any-Hit (Alpha-Ausschnitt, Deckung bei Schattenstrahlen).
// Kein Raster-Pass noetig – die Stellvertreter werden nie gezeichnet. Keine Keywords → keine Variantenflut.
Shader "Hidden/Ashveil/PTSurface"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Farbe", Color) = (1,1,1,1)
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normalstaerke", Float) = 1
        _MetallicGlossMap ("Metall/Glanz", 2D) = "white" {}
        _Metallic ("Metall", Float) = 0
        _Smoothness ("Glaette", Float) = 0.5
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emissionsfarbe", Color) = (0,0,0,0)
        _Cutoff ("Alpha-Schwelle", Float) = 0.5
        _AshAlphaClip ("Alpha-Ausschnitt", Float) = 0
        _AshTransparent ("Transparent (Alpha = Deckung)", Float) = 0
        _AshSmoothSource ("Glaette aus (0 Metall-A, 1 Albedo-A)", Float) = 0
        _AshHasMetalMap ("Metall-Textur", Float) = 0
        _AshHasNormalMap ("Normal-Textur", Float) = 0
        _AshHasEmissionMap ("Emissions-Textur", Float) = 0
        _AshSurfFlags ("Flags", Float) = 0
        _AshIor ("Brechzahl", Float) = 1.5
        _AshTransmission ("Durchlaessigkeit", Float) = 0
        _AshAbsorption ("Absorption je m", Vector) = (0,0,0,0)
        _AshTexSize ("Albedo-Groesse", Vector) = (1,1,0,0)
    }

    SubShader
    {
        Pass
        {
            Name "AshveilPT"
            Tags { "LightMode" = "AshveilPT" }

            HLSLPROGRAM
            #pragma raytracing surface

            #include "UnityRayTracingMeshUtils.cginc"
            #include "../ShaderLibrary/AshPTCommon.hlsl"

            Texture2D _BaseMap;          SamplerState sampler_BaseMap;
            Texture2D _BumpMap;          SamplerState sampler_BumpMap;
            Texture2D _MetallicGlossMap; SamplerState sampler_MetallicGlossMap;
            Texture2D _EmissionMap;      SamplerState sampler_EmissionMap;

            cbuffer UnityPerMaterial
            {
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _BumpScale;
                float _Metallic;
                float _Smoothness;
                float4 _EmissionColor;
                float _Cutoff;
                float _AshAlphaClip;
                float _AshTransparent;
                float _AshSmoothSource;
                float _AshHasMetalMap;
                float _AshHasNormalMap;
                float _AshHasEmissionMap;
                float _AshSurfFlags;
                float _AshIor;
                float _AshTransmission;
                float4 _AshAbsorption;
                float4 _AshTexSize;
            };

            struct AshVertex
            {
                float3 position;
                float3 normal;
                float4 tangent;
                float2 uv;
            };

            AshVertex AshFetch(uint i)
            {
                AshVertex v;
                v.position = UnityRayTracingFetchVertexAttribute3(i, kVertexAttributePosition);
                v.normal = UnityRayTracingFetchVertexAttribute3(i, kVertexAttributeNormal);
                v.tangent = UnityRayTracingFetchVertexAttribute4(i, kVertexAttributeTangent);
                v.uv = UnityRayTracingFetchVertexAttribute2(i, kVertexAttributeTexCoord0);
                return v;
            }

            float3 AshUnpackNormal(float4 packed, float scale)
            {
                // RGB- und AG-kodierte (DXT5nm/BC5) Normal-Maps
                packed.x *= packed.w;
                float3 n;
                n.xy = (packed.xy * 2.0 - 1.0) * scale;
                n.z = sqrt(max(1e-6, 1.0 - saturate(dot(n.xy, n.xy))));
                return n;
            }

            float AshHashFloat(uint a, uint b)
            {
                return (AshHashU(AshHashCombine(AshHashU(a), b)) >> 8) * (1.0 / 16777216.0);
            }

            float2 AshHitUV(BuiltInTriangleIntersectionAttributes attr, out float3 bary, out uint3 tri)
            {
                tri = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
                bary = float3(1.0 - attr.barycentrics.x - attr.barycentrics.y, attr.barycentrics.x, attr.barycentrics.y);
                float2 uv0 = UnityRayTracingFetchVertexAttribute2(tri.x, kVertexAttributeTexCoord0);
                float2 uv1 = UnityRayTracingFetchVertexAttribute2(tri.y, kVertexAttributeTexCoord0);
                float2 uv2 = UnityRayTracingFetchVertexAttribute2(tri.z, kVertexAttributeTexCoord0);
                float2 uv = uv0 * bary.x + uv1 * bary.y + uv2 * bary.z;
                return uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
            }

            [shader("anyhit")]
            void AshPTAnyHit(inout AshPTPayload p : SV_RayPayload, BuiltInTriangleIntersectionAttributes attr : SV_IntersectionAttributes)
            {
                float3 bary; uint3 tri;
                float2 uv = AshHitUV(attr, bary, tri);
                float a = _BaseMap.SampleLevel(sampler_BaseMap, uv, 0).a * _BaseColor.a;
                if (_AshAlphaClip > 0.5 && a < _Cutoff) { IgnoreHit(); return; }
                // Schattenstrahlen durch teiltransparente Flaechen: mit Wahrscheinlichkeit = Deckung blockieren
                if (_AshTransparent > 0.5 && (RayFlags() & RAY_FLAG_SKIP_CLOSEST_HIT_SHADER) != 0)
                {
                    uint2 px = DispatchRaysIndex().xy;
                    float u = AshHashFloat(px.x * 9781u + px.y * 6271u + PrimitiveIndex() * 26699u, asuint(RayTCurrent()));
                    if (u >= a) IgnoreHit();
                }
            }

            [shader("closesthit")]
            void AshPTClosestHit(inout AshPTPayload p : SV_RayPayload, BuiltInTriangleIntersectionAttributes attr : SV_IntersectionAttributes)
            {
                float3 bary; uint3 tri;
                float2 uv = AshHitUV(attr, bary, tri);
                AshVertex v0 = AshFetch(tri.x), v1 = AshFetch(tri.y), v2 = AshFetch(tri.z);

                float3x4 o2w = ObjectToWorld3x4();
                float3x3 w2oT = transpose((float3x3)WorldToObject3x4());
                float3 posO = v0.position * bary.x + v1.position * bary.y + v2.position * bary.z;
                float3 p0 = mul(o2w, float4(v0.position, 1.0));
                float3 p1 = mul(o2w, float4(v1.position, 1.0));
                float3 p2 = mul(o2w, float4(v2.position, 1.0));
                float3 posW = mul(o2w, float4(posO, 1.0));

                float3 dir = WorldRayDirection();
                float3 ngRaw = cross(p1 - p0, p2 - p0);
                float areaW = 0.5 * length(ngRaw);
                float3 ng = normalize(ngRaw);
                bool front = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;
                if (dot(ng, dir) > 0) ng = -ng;   // zur Einfallsseite

                float3 nO = v0.normal * bary.x + v1.normal * bary.y + v2.normal * bary.z;
                float3 ns = dot(nO, nO) > 1e-12 ? normalize(mul(w2oT, nO)) : ng;
                if (dot(ns, ng) < 0) ns = -ns;

                // Strahlkegel → Textur-LOD (Akenine-Moeller et al., Ray Tracing Gems Kap. 20)
                float t = RayTCurrent();
                float width = p.coneWidth + p.coneSpread * t;
                float2 duv1 = (v1.uv - v0.uv) * _BaseMap_ST.xy, duv2 = (v2.uv - v0.uv) * _BaseMap_ST.xy;
                float uvArea = 0.5 * abs(duv1.x * duv2.y - duv1.y * duv2.x) * _AshTexSize.x * _AshTexSize.y;
                float lod = 0.0;
                if (uvArea > 1e-12 && areaW > 1e-12 && width > 0)
                    lod = max(0.0, 0.5 * log2(uvArea / areaW) + log2(width / max(abs(dot(ng, dir)), 0.1)));

                float4 base = _BaseMap.SampleLevel(sampler_BaseMap, uv, lod) * _BaseColor;
                uint surf = (uint)_AshSurfFlags;

                float metallic = _Metallic;
                float smooth = _Smoothness;
                if (_AshHasMetalMap > 0.5)
                {
                    float4 mg = _MetallicGlossMap.SampleLevel(sampler_MetallicGlossMap, uv, lod);
                    metallic = mg.r;
                    smooth = mg.a * _Smoothness;
                }
                if (_AshSmoothSource > 0.5) smooth = base.a * _Smoothness;

                if (_AshHasNormalMap > 0.5)
                {
                    float3 tO = v0.tangent.xyz * bary.x + v1.tangent.xyz * bary.y + v2.tangent.xyz * bary.z;
                    float tw = v0.tangent.w < 0 ? -1.0 : 1.0;
                    if (dot(tO, tO) > 1e-12)
                    {
                        float3 T = normalize(mul((float3x3)o2w, tO));
                        T = normalize(T - ns * dot(ns, T));
                        float3 B = cross(ns, T) * tw;
                        float3 nt = AshUnpackNormal(_BumpMap.SampleLevel(sampler_BumpMap, uv, lod), _BumpScale);
                        float3 nm = normalize(T * nt.x + B * nt.y + ns * nt.z);
                        if (dot(nm, ng) > 0) ns = nm;
                    }
                }

                float3 emission = _EmissionColor.rgb;
                if (_AshHasEmissionMap > 0.5) emission *= _EmissionMap.SampleLevel(sampler_EmissionMap, uv, lod).rgb;
                bool doubleSided = (surf & ASH_SURF_DOUBLESIDED) != 0;
                if (!front && !doubleSided) emission = 0;

                p.hitT = t;
                p.position = posW;
                p.geoNormal = ng;
                p.shadingNormal = ns;
                p.albedo = base.rgb;
                p.metallic = metallic;
                p.roughness = 1.0 - saturate(smooth);
                p.opacity = _AshTransparent > 0.5 ? saturate(base.a) : 1.0;
                p.ior = _AshIor;
                p.transmission = _AshTransmission;
                p.absorption = _AshAbsorption.xyz;
                p.flags = ASH_SURF_HIT | (front ? ASH_SURF_FRONT : 0u) | surf;
                p.instanceId = InstanceID();
                p.primitiveIndex = PrimitiveIndex();
                p.coneWidth = width;
                if ((surf & ASH_SURF_UNLIT) != 0)
                {
                    p.emission = base.rgb;
                    p.albedo = 0;
                }
                else p.emission = emission;
            }
            ENDHLSL
        }
    }
}
