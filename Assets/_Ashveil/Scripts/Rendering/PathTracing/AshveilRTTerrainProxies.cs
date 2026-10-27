using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>
    /// Gelaende im Pathtracer (§91–92): Unity-Terrains stehen nicht als Renderer in einer manuellen Beschleunigungs-
    /// struktur, deshalb je Terrain ein Stellvertreter-Netz in voller Hoehenfeld-Aufloesung (Normalen aus den Hoehen,
    /// UV = Terrain-Koordinate) und ein Material mit Splatmaps und bis zu 8 Ebenen (Textur, Normal-Map, Kachelung,
    /// Glaette, Metall). Einmal je Terrain gebaut; aendert sich das Hoehenfeld, wird neu gebaut.
    /// </summary>
    public sealed class AshveilRTTerrainProxies : System.IDisposable
    {
        private sealed class Proxy
        {
            public Mesh Mesh;
            public Material Material;
            public int Hash;
        }

        private readonly Shader _shader;
        private readonly Dictionary<TerrainData, Proxy> _proxies = new Dictionary<TerrainData, Proxy>();

        public AshveilRTTerrainProxies(Shader shader) => _shader = shader;

        /// <summary>Hoechstens so viele Punkte je Seite (0 = volle Aufloesung). Dynamisches GI: 257 reicht fuer diffuses Licht.</summary>
        public int MaxVertsPerSide;

        public bool TryGet(Terrain t, out Mesh mesh, out Material mat, out Matrix4x4 matrix)
        {
            mesh = null; mat = null;
            matrix = Matrix4x4.Translate(t.GetPosition());
            if (_shader == null) return false;
            TerrainData d = t.terrainData;
            int hash = Hash(d);
            if (!_proxies.TryGetValue(d, out Proxy p) || p.Hash != hash)
            {
                if (p != null) Release(p);
                p = new Proxy { Mesh = BuildMesh(d, MaxVertsPerSide), Material = new Material(_shader) { name = d.name + " (PT-Gelaende)", hideFlags = HideFlags.DontSave }, Hash = hash };
                _proxies[d] = p;
            }
            FillMaterial(d, p.Material);
            mesh = p.Mesh;
            mat = p.Material;
            return mesh != null;
        }

        private static int Hash(TerrainData d)
        {
            unchecked
            {
                int h = d.heightmapResolution * 31 + d.size.GetHashCode();
                // Stichprobe der Hoehen (billig): Aenderungen am Gelaende fuehren zu neuem Netz
                int r = d.heightmapResolution;
                for (int i = 0; i < 64; i++)
                {
                    int x = (i * 7919) % r, y = (i * 104729) % r;
                    h = h * 31 + d.GetHeight(x, y).GetHashCode();
                }
                return h;
            }
        }

        private static Mesh BuildMesh(TerrainData d, int maxSide)
        {
            int res = d.heightmapResolution;
            int step = res > 1025 ? 2 : 1;           // bis 1025² volle Aufloesung
            if (maxSide > 1) step = Mathf.Max(step, Mathf.CeilToInt((res - 1) / (float)(maxSide - 1)));
            int n = (res - 1) / step + 1;
            float[,] h = d.GetHeights(0, 0, res, res);
            Vector3 size = d.size;
            var verts = new Vector3[n * n];
            var normals = new Vector3[n * n];
            var tangents = new Vector4[n * n];
            var uvs = new Vector2[n * n];
            float dx = size.x / (n - 1), dz = size.z / (n - 1);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int hx = x * step, hz = z * step;
                float y = h[hz, hx] * size.y;
                int i = z * n + x;
                verts[i] = new Vector3(x * dx, y, z * dz);
                uvs[i] = new Vector2(x / (float)(n - 1), z / (float)(n - 1));
                float hl = h[hz, Mathf.Max(hx - step, 0)] * size.y, hr = h[hz, Mathf.Min(hx + step, res - 1)] * size.y;
                float hd = h[Mathf.Max(hz - step, 0), hx] * size.y, hu = h[Mathf.Min(hz + step, res - 1), hx] * size.y;
                Vector3 nrm = new Vector3((hl - hr) / (2f * dx), 1f, (hd - hu) / (2f * dz)).normalized;
                normals[i] = nrm;
                Vector3 tan = (Vector3.right - nrm * nrm.x).normalized;
                tangents[i] = new Vector4(tan.x, tan.y, tan.z, -1f);
            }
            var tris = new int[(n - 1) * (n - 1) * 6];
            int k = 0;
            for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                int a = z * n + x, b = a + 1, c = a + n, e = c + 1;
                // Uhrzeigersinn von oben = Vorderseite (Unity)
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = e;
            }
            var mesh = new Mesh { name = d.name + " (PT-Gelaende)", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static readonly int[] SplatIds = Ids("_AshSplat");
        private static readonly int[] NormalIds = Ids("_AshNormal");
        private static readonly int[] StIds = Ids("_AshSplatST");
        private static readonly int[] ParamIds = Ids("_AshLayerParams");

        private static int[] Ids(string prefix)
        {
            var ids = new int[8];
            for (int i = 0; i < 8; i++) ids[i] = Shader.PropertyToID(prefix + i);
            return ids;
        }

        private static void FillMaterial(TerrainData d, Material m)
        {
            Texture2D[] ctrl = d.alphamapTextures;
            m.SetTexture("_AshControl0", ctrl.Length > 0 ? ctrl[0] : Texture2D.blackTexture);
            m.SetTexture("_AshControl1", ctrl.Length > 1 ? ctrl[1] : Texture2D.blackTexture);
            TerrainLayer[] layers = d.terrainLayers;
            int count = Mathf.Min(layers.Length, 8);
            m.SetFloat("_AshLayerCount", count);
            for (int i = 0; i < 8; i++)
            {
                TerrainLayer l = i < count ? layers[i] : null;
                Texture2D diff = l != null ? l.diffuseTexture : null;
                Texture2D nrm = l != null ? l.normalMapTexture : null;
                m.SetTexture(SplatIds[i], diff != null ? diff : Texture2D.grayTexture);
                m.SetTexture(NormalIds[i], nrm != null ? nrm : Texture2D.normalTexture);
                Vector2 tile = l != null ? l.tileSize : Vector2.one;
                Vector2 off = l != null ? l.tileOffset : Vector2.zero;
                // Kachelung ueber das ganze Terrain: uv * Groesse / Kachel
                m.SetVector(StIds[i], new Vector4(d.size.x / Mathf.Max(tile.x, 1e-3f), d.size.z / Mathf.Max(tile.y, 1e-3f),
                                                  off.x / Mathf.Max(tile.x, 1e-3f), off.y / Mathf.Max(tile.y, 1e-3f)));
                float texel = diff != null ? tile.x / Mathf.Max(diff.width, 1) : 1f;
                m.SetVector(ParamIds[i], new Vector4(l != null ? l.smoothness : 0f, l != null ? l.metallic : 0f,
                                                     nrm != null ? l.normalScale : 0f, texel));
                m.SetColor("_AshLayerTint" + i, l != null ? (Color)l.diffuseRemapMax : Color.white);
            }
        }

        private static void Release(Proxy p)
        {
            if (p.Mesh != null) Object.Destroy(p.Mesh);
            if (p.Material != null) Object.Destroy(p.Material);
        }

        /// <summary>Stellvertreter entladener Gelaende freigeben (Zellen-Streaming: sonst waechst der Speicher je Besuch).</summary>
        public int ReleaseDead(HashSet<TerrainData> live = null)
        {
            var dead = new List<TerrainData>();
            foreach (var kv in _proxies) if (kv.Key == null || (live != null && !live.Contains(kv.Key))) dead.Add(kv.Key);
            foreach (TerrainData d in dead) { Release(_proxies[d]); _proxies.Remove(d); }
            return dead.Count;
        }

        public void Dispose()
        {
            foreach (Proxy p in _proxies.Values) Release(p);
            _proxies.Clear();
        }
    }
}
