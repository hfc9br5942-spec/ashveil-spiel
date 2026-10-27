// ASHVEIL Pathtracer – Gelaende-Stellvertreter: Splatmaps + bis zu 8 Terrain-Ebenen (wie URP Terrain Lit:
// gewichtete Albedo, Normal-Maps, Glaette, Metall). Textur-LOD je Ebene aus dem Strahlkegel.
Shader "Hidden/Ashveil/PTTerrain"
{
    Properties
    {
        _AshControl0 ("Splat 0-3", 2D) = "black" {}
        _AshControl1 ("Splat 4-7", 2D) = "black" {}
        _AshLayerCount ("Ebenen", Float) = 0
    }

    SubShader
    {
        Pass
        {
            Name "AshveilPT"
            Tags { "LightMode" = "AshveilPT" }

            HLSLPROGRAM
            #pragma raytracing terrain

            #include "UnityRayTracingMeshUtils.cginc"
            #include "../ShaderLibrary/AshPTCommon.hlsl"

            Texture2D _AshControl0; SamplerState sampler_AshControl0;
            Texture2D _AshControl1;
            Texture2D _AshSplat0, _AshSplat1, _AshSplat2, _AshSplat3, _AshSplat4, _AshSplat5, _AshSplat6, _AshSplat7;
            SamplerState sampler_AshSplat0, sampler_AshSplat1, sampler_AshSplat2, sampler_AshSplat3, sampler_AshSplat4, sampler_AshSplat5, sampler_AshSplat6, sampler_AshSplat7;
            Texture2D _AshNormal0, _AshNormal1, _AshNormal2, _AshNormal3, _AshNormal4, _AshNormal5, _AshNormal6, _AshNormal7;
            SamplerState sampler_AshNormal0;

            cbuffer UnityPerMaterial
            {
                float _AshLayerCount;
                float4 _AshSplatST0, _AshSplatST1, _AshSplatST2, _AshSplatST3, _AshSplatST4, _AshSplatST5, _AshSplatST6, _AshSplatST7;
                float4 _AshLayerParams0, _AshLayerParams1, _AshLayerParams2, _AshLayerParams3, _AshLayerParams4, _AshLayerParams5, _AshLayerParams6, _AshLayerParams7;
                float4 _AshLayerTint0, _AshLayerTint1, _AshLayerTint2, _AshLayerTint3, _AshLayerTint4, _AshLayerTint5, _AshLayerTint6, _AshLayerTint7;
            };

            float3 AshUnpackNormal(float4 packed, float scale)
            {
                packed.x *= packed.w;
                float3 n;
                n.xy = (packed.xy * 2.0 - 1.0) * scale;
                n.z = sqrt(max(1e-6, 1.0 - saturate(dot(n.xy, n.xy))));
                return n;
            }

            // Ein Layer: Albedo, Tangenten-Normale, Glaette, Metall gewichtet aufsummieren
            #define ASH_LAYER(i, WT) \
                if (WT > 1e-4) { \
                    float2 luv = uv * _AshSplatST##i.xy + _AshSplatST##i.zw; \
                    float lod = max(0.0, log2(footprint / max(_AshLayerParams##i.w, 1e-6))); \
                    float4 c = _AshSplat##i.SampleLevel(sampler_AshSplat##i, luv, lod) * _AshLayerTint##i; \
                    albedo += c.rgb * WT; \
                    smooth += _AshLayerParams##i.x * WT; \
                    metal += _AshLayerParams##i.y * WT; \
                    if (_AshLayerParams##i.z > 0) nt += AshUnpackNormal(_AshNormal##i.SampleLevel(sampler_AshNormal0, luv, lod), _AshLayerParams##i.z) * WT; \
                    else nt += float3(0, 0, 1) * WT; \
                    wsum += WT; \
                }

            [shader("closesthit")]
            void AshPTClosestHit(inout AshPTPayload p : SV_RayPayload, BuiltInTriangleIntersectionAttributes attr : SV_IntersectionAttributes)
            {
                uint3 tri = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
                float3 b = float3(1.0 - attr.barycentrics.x - attr.barycentrics.y, attr.barycentrics.x, attr.barycentrics.y);
                float3 v0 = UnityRayTracingFetchVertexAttribute3(tri.x, kVertexAttributePosition);
                float3 v1 = UnityRayTracingFetchVertexAttribute3(tri.y, kVertexAttributePosition);
                float3 v2 = UnityRayTracingFetchVertexAttribute3(tri.z, kVertexAttributePosition);
                float3 n0 = UnityRayTracingFetchVertexAttribute3(tri.x, kVertexAttributeNormal);
                float3 n1 = UnityRayTracingFetchVertexAttribute3(tri.y, kVertexAttributeNormal);
                float3 n2 = UnityRayTracingFetchVertexAttribute3(tri.z, kVertexAttributeNormal);
                float2 uv = UnityRayTracingFetchVertexAttribute2(tri.x, kVertexAttributeTexCoord0) * b.x
                          + UnityRayTracingFetchVertexAttribute2(tri.y, kVertexAttributeTexCoord0) * b.y
                          + UnityRayTracingFetchVertexAttribute2(tri.z, kVertexAttributeTexCoord0) * b.z;

                float3x4 o2w = ObjectToWorld3x4();
                float3 p0 = mul(o2w, float4(v0, 1)), p1 = mul(o2w, float4(v1, 1)), p2 = mul(o2w, float4(v2, 1));
                float3 posW = p0 * b.x + p1 * b.y + p2 * b.z;
                float3 dir = WorldRayDirection();
                float3 ng = normalize(cross(p1 - p0, p2 - p0));
                bool front = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;
                if (dot(ng, dir) > 0) ng = -ng;
                float3 ns = normalize(mul((float3x3)o2w, n0 * b.x + n1 * b.y + n2 * b.z));
                if (dot(ns, ng) < 0) ns = -ns;

                float t = RayTCurrent();
                float footprint = (p.coneWidth + p.coneSpread * t) / max(abs(dot(ng, dir)), 0.15);

                float4 c0 = _AshControl0.SampleLevel(sampler_AshControl0, uv, 0);
                float4 c1 = _AshControl1.SampleLevel(sampler_AshControl0, uv, 0);
                float3 albedo = 0, nt = 0;
                float smooth = 0, metal = 0, wsum = 0;
                int count = (int)_AshLayerCount;
                ASH_LAYER(0, c0.r)
                if (count > 1) ASH_LAYER(1, c0.g)
                if (count > 2) ASH_LAYER(2, c0.b)
                if (count > 3) ASH_LAYER(3, c0.a)
                if (count > 4) ASH_LAYER(4, c1.r)
                if (count > 5) ASH_LAYER(5, c1.g)
                if (count > 6) ASH_LAYER(6, c1.b)
                if (count > 7) ASH_LAYER(7, c1.a)
                if (wsum > 1e-4) { albedo /= wsum; smooth /= wsum; metal /= wsum; nt = normalize(nt); }
                else { albedo = 0.3; nt = float3(0, 0, 1); }

                // Tangentenraum wie im Netz: T entlang +x, B entlang +z
                float3 T = normalize(float3(1, 0, 0) - ns * ns.x);
                float3 B = cross(ns, T) * -1.0;
                float3 nm = normalize(T * nt.x + B * nt.y + ns * nt.z);
                if (dot(nm, ng) > 0) ns = nm;

                p.hitT = t;
                p.position = posW;
                p.geoNormal = ng;
                p.shadingNormal = ns;
                p.albedo = albedo;
                p.metallic = metal;
                p.roughness = 1.0 - saturate(smooth);
                p.emission = 0;
                p.opacity = 1;
                p.ior = 1.5;
                p.transmission = 0;
                p.absorption = 0;
                p.flags = ASH_SURF_HIT | (front ? ASH_SURF_FRONT : 0u);
                p.instanceId = InstanceID();
                p.primitiveIndex = PrimitiveIndex();
                p.coneWidth = p.coneWidth + p.coneSpread * t;
            }
            ENDHLSL
        }
    }
}
