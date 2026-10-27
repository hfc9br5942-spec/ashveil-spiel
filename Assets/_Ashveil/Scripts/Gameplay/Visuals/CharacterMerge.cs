using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// Massenschlacht-Schritt 2 (Grafikziel 30.09.): Fasst alle sichtbaren Teile einer Figur – Koerper, Kleidungsschalen,
    /// Frisur, starre Ruestungsteile, Waffe – zu EINEM Haut-Mesh zusammen. Bei 1000 Figuren sinkt die Zahl der Renderer
    /// von ≈ 7 500 auf ≈ 1 000; gleiche Materialien werden zu einem Teilnetz vereint.
    ///
    /// Starre Teile werden zu Knochen ihrer selbst (Gewicht 1 auf das eigene Transform), folgen also jeder Bewegung –
    /// auch der Waffe zwischen Hand und Ruecken. Gleich ausgestattete Figuren teilen sich das Netz (Cache).
    /// Nur Darstellung. Die Originale bleiben bestehen und werden nur nicht gezeichnet (CharacterLod schaltet um).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterMerge : MonoBehaviour
    {
        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();
        private static readonly StringBuilder Key = new StringBuilder(1024);
        private const int MaxCached = 1500;

        private sealed class Entry
        {
            public Mesh Mesh;
            public Material[] Materials;
            public int Users;
        }

        public static int CachedMeshes => Cache.Count;
        /// <summary>Diagnose: Bauversuche, erfolgreiche Bauten, zu wenige Teile, nicht lesbare Netze, Bauzeit gesamt.</summary>
        public static int Attempts, Built, TooFewParts, Unreadable, CacheHits;
        public static double BuildMs;

        public static string Diagnose() =>
            $"Versuche {Attempts} · gebaut {Built} (Cache-Treffer {CacheHits}) · zu wenig Teile {TooFewParts} · nicht lesbare Teile {Unreadable} · " +
            $"Bauzeit {(Built > 0 ? BuildMs / Built : 0):0.00} ms je Figur";
        public static long CachedVertices
        {
            get
            {
                long n = 0;
                foreach (Entry e in Cache.Values) if (e.Mesh != null) n += e.Mesh.vertexCount;
                return n;
            }
        }

        private SkinnedMeshRenderer _merged;
        private string _key;
        private readonly List<Renderer> _sources = new List<Renderer>();
        private readonly HashSet<Renderer> _sourceSet = new HashSet<Renderer>();

        /// <summary>
        /// 21.10.2026 (Tripo-Kleidung): Je LODGroup nur EINE Detailstufe zusammenfassen (Stufe 1, falls vorhanden). Die LODGroup
        /// schaltet ihre Renderer nicht aus (enabled bleibt an) – bisher landeten darum Koerperstufe 0, 1 und 2 gleichzeitig im Netz
        /// (dreifache Kosten, Z-Flackern), mit Kleidstufen noch mehr. Die uebrigen Stufen sind ausgeblendet, solange zusammengefasst ist.
        /// Aus = altes Verhalten (nur zum Vergleich im Test).
        /// </summary>
        public static bool NurEineStufe = true;
        private readonly List<Renderer> _andereStufen = new List<Renderer>();

        public bool Active => _merged != null && _merged.enabled;
        public SkinnedMeshRenderer Merged => _merged;

        /// <summary>Zusammenfassen und die Quellen ausblenden. Liefert false, wenn nichts Sinnvolles zu bauen ist.</summary>
        public bool Build(Animator animator, List<Renderer> candidates)
        {
            Release();
            Attempts++;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var parts = new List<Renderer>();
            HashSet<Renderer> andere = NurEineStufe ? AndereStufen(candidates) : null;
            foreach (Renderer r in candidates)
            {
                if (andere != null && andere.Contains(r)) continue;   // andere Detailstufe derselben LODGroup
                if (Eligible(r) && !r.forceRenderingOff) parts.Add(r);
                else if (Visible(r) && !r.forceRenderingOff) Unreadable++;
            }
            if (parts.Count < 2 || animator == null)
            {
                TooFewParts++;
                return false;
            }

            // Knochenliste (Transform + Bindpose, doppelte zusammengelegt) und Schluessel fuer den Netz-Cache
            var bones = new List<Transform>();
            var binds = new List<Matrix4x4>();
            var boneIndex = new Dictionary<(Transform, int), int>();
            var remaps = new List<int[]>();
            Key.Clear();
            foreach (Renderer r in parts)
            {
                if (r is SkinnedMeshRenderer s)
                {
                    Transform[] b = s.bones;
                    Matrix4x4[] bp = s.sharedMesh.bindposes;
                    var map = new int[b.Length];
                    for (int i = 0; i < b.Length; i++)
                    {
                        Matrix4x4 m = i < bp.Length ? bp[i] : Matrix4x4.identity;
                        var k = (b[i], MatrixHash(m));
                        if (b[i] == null) { map[i] = 0; continue; }
                        if (!boneIndex.TryGetValue(k, out int idx))
                        {
                            idx = bones.Count;
                            boneIndex[k] = idx;
                            bones.Add(b[i]);
                            binds.Add(m);
                        }
                        map[i] = idx;
                    }
                    remaps.Add(map);
                    Key.Append('S').Append(Id(s.sharedMesh));
                    foreach (int i in map) Key.Append(',').Append(i);
                }
                else
                {
                    int idx = bones.Count;
                    bones.Add(r.transform);
                    binds.Add(Matrix4x4.identity);
                    remaps.Add(new[] { idx });
                    Key.Append('R').Append(Id(r.GetComponent<MeshFilter>().sharedMesh)).Append('@').Append(idx);
                }
                foreach (Material mat in r.sharedMaterials) Key.Append('m').Append(Id(mat));
                Key.Append('|');
            }
            _key = Key.ToString();

            if (Cache.TryGetValue(_key, out Entry entry)) CacheHits++;
            else
            {
                entry = BuildMesh(parts, remaps, binds);
                if (entry == null) return false;
                Cache[_key] = entry;
            }
            entry.Users++;

            var go = new GameObject("Figur (zusammengefasst)") { layer = animator.gameObject.layer };
            go.transform.SetParent(animator.transform, false);   // unter dem Animator: zaehlt fuer dessen Sichtbarkeit
            go.AddComponent<MergedMarker>();
            _merged = go.AddComponent<SkinnedMeshRenderer>();
            _merged.sharedMesh = entry.Mesh;
            _merged.sharedMaterials = entry.Materials;
            _merged.bones = bones.ToArray();
            _merged.quality = SkinQuality.Bone2;
            _merged.shadowCastingMode = ShadowCastingMode.Off;
            _merged.updateWhenOffscreen = false;
            SkinnedMeshRenderer body = FindBody(parts);
            _merged.rootBone = body != null && body.rootBone != null ? body.rootBone : animator.transform;
            Bounds lb = body != null ? body.localBounds : new Bounds(Vector3.up, Vector3.one * 2f);
            lb.Expand(1.2f);   // Waffen, Schultern, Helm
            _merged.localBounds = lb;

            _sources.AddRange(parts);
            _sourceSet.UnionWith(parts);
            foreach (Renderer r in _sources) r.forceRenderingOff = true;
            // Die anderen Stufen wuerde die LODGroup je nach Abstand weiter zeichnen – solange zusammengefasst ist, aus
            if (andere != null)
                foreach (Renderer r in andere)
                    if (r != null && !r.forceRenderingOff) { r.forceRenderingOff = true; _andereStufen.Add(r); }
            Built++;
            BuildMs += watch.Elapsed.TotalMilliseconds;
            return true;
        }

        /// <summary>Zusammengefasstes Netz entfernen und die Quellen wieder zeichnen.</summary>
        public void Release()
        {
            foreach (Renderer r in _sources) if (r != null) r.forceRenderingOff = false;
            foreach (Renderer r in _andereStufen) if (r != null) r.forceRenderingOff = false;
            _sources.Clear();
            _sourceSet.Clear();
            _andereStufen.Clear();
            if (_merged != null)
            {
                Zerstoeren(_merged.gameObject);
                _merged = null;
            }
            if (_key != null && Cache.TryGetValue(_key, out Entry e))
            {
                e.Users--;
                // Unbenutzte Netze eine Weile behalten (Figuren wechseln oft zwischen den Stufen hin und her)
                if (e.Users <= 0 && Cache.Count > MaxCached)
                {
                    if (e.Mesh != null) Zerstoeren(e.Mesh);
                    Cache.Remove(_key);
                }
            }
            _key = null;
        }

        /// <summary>21.10.: im Edit-Modus (EditMode-Tests) DestroyImmediate – Destroy ist dort verboten.</summary>
        private static void Zerstoeren(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        /// <summary>Stimmt die Quelle noch? (Ausruestung/Frisur ausgeblendet oder dazugekommen.)</summary>
        public bool StillMatches(List<Renderer> candidates)
        {
            foreach (Renderer r in _sources) if (!Eligible(r)) return false;   // Teil verschwunden/ausgeblendet
            foreach (Renderer r in candidates)                                    // neues sichtbares Teil dazugekommen
                if (Eligible(r) && !r.forceRenderingOff && !_sourceSet.Contains(r)) return false;
            return true;
        }

        /// <summary>21.10.: Renderer der nicht gewaehlten Detailstufen aller LODGroups der Kandidaten (gewaehlt: Stufe 1, sonst keine
        /// Einschraenkung). Ein Renderer, der auch in der gewaehlten Stufe steht (Kleidstufe fuer mehrere Stufen), bleibt. null = keine.</summary>
        private static HashSet<Renderer> AndereStufen(List<Renderer> candidates)
        {
            HashSet<Renderer> weg = null;
            HashSet<LODGroup> gesehen = null;
            foreach (Renderer r in candidates)
            {
                if (r == null) continue;
                LODGroup g = r.GetComponentInParent<LODGroup>(true);
                if (g == null) continue;
                gesehen ??= new HashSet<LODGroup>();
                if (!gesehen.Add(g)) continue;
                LOD[] lods = g.GetLODs();
                if (lods.Length < 2) continue;
                const int behalten = 1;
                weg ??= new HashSet<Renderer>();
                for (int i = 0; i < lods.Length; i++)
                    if (i != behalten)
                        foreach (Renderer x in lods[i].renderers) if (x != null) weg.Add(x);
                foreach (Renderer x in lods[behalten].renderers) if (x != null) weg.Remove(x);
            }
            return weg;
        }

        private static bool Visible(Renderer r) =>
            r != null && r.enabled && r.gameObject.activeInHierarchy && (r is SkinnedMeshRenderer || r is MeshRenderer);

        /// <summary>Sichtbar und zusammenfassbar (lesbares Netz; Haut-Meshes mit Knochen).</summary>
        private static bool Eligible(Renderer r)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
            if (r is SkinnedMeshRenderer s) return s.sharedMesh != null && s.sharedMesh.isReadable && s.bones != null && s.bones.Length > 0;
            return r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null && mf.sharedMesh.isReadable;
        }

        private void OnDestroy() => Release();

        // ================================================================== Netz bauen

        private static Entry BuildMesh(List<Renderer> parts, List<int[]> remaps, List<Matrix4x4> binds)
        {
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color32>();
            var weights = new List<BoneWeight>();
            var byMaterial = new Dictionary<Material, List<int>>();
            var order = new List<Material>();
            bool anyColor = false;
            var tv = new List<Vector3>();
            var tn = new List<Vector3>();
            var tu = new List<Vector2>();
            var tc = new List<Color32>();
            var tri = new List<int>();

            for (int p = 0; p < parts.Count; p++)
            {
                Renderer r = parts[p];
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>().sharedMesh;
                if (!m.isReadable) continue;
                int baseV = pos.Count;
                m.GetVertices(tv);
                m.GetNormals(tn);
                m.GetUVs(0, tu);
                m.GetColors(tc);
                pos.AddRange(tv);
                for (int i = 0; i < tv.Count; i++) nrm.Add(i < tn.Count ? tn[i] : Vector3.up);
                for (int i = 0; i < tv.Count; i++) uv.Add(i < tu.Count ? tu[i] : Vector2.zero);
                if (tc.Count == tv.Count) anyColor = true;
                for (int i = 0; i < tv.Count; i++) col.Add(i < tc.Count ? tc[i] : new Color32(255, 255, 255, 255));

                int[] map = remaps[p];
                if (r is SkinnedMeshRenderer)
                {
                    BoneWeight[] bw = m.boneWeights;
                    for (int i = 0; i < tv.Count; i++)
                    {
                        BoneWeight w = i < bw.Length ? bw[i] : new BoneWeight { weight0 = 1f };
                        w.boneIndex0 = Map(map, w.boneIndex0);
                        w.boneIndex1 = Map(map, w.boneIndex1);
                        w.boneIndex2 = Map(map, w.boneIndex2);
                        w.boneIndex3 = Map(map, w.boneIndex3);
                        weights.Add(w);
                    }
                }
                else
                {
                    var w = new BoneWeight { boneIndex0 = map[0], weight0 = 1f };
                    for (int i = 0; i < tv.Count; i++) weights.Add(w);
                }

                Material[] mats = r.sharedMaterials;
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    Material mat = mats.Length > 0 ? mats[Mathf.Min(sm, mats.Length - 1)] : null;
                    if (mat == null) continue;
                    if (!byMaterial.TryGetValue(mat, out List<int> list))
                    {
                        list = new List<int>();
                        byMaterial[mat] = list;
                        order.Add(mat);
                    }
                    m.GetTriangles(tri, sm);
                    for (int i = 0; i < tri.Count; i++) list.Add(tri[i] + baseV);
                }
            }
            if (pos.Count == 0 || order.Count == 0) return null;

            var mesh = new Mesh { name = "Figur (zusammengefasst)", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            if (anyColor) mesh.SetColors(col);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = binds.ToArray();
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++) mesh.SetTriangles(byMaterial[order[i]], i, false);
            mesh.RecalculateBounds();
            // lesbar lassen: der Pathtracer (Fotomodus) backt Haut-Meshes auf der CPU
            return new Entry { Mesh = mesh, Materials = order.ToArray() };
        }

        private static int Map(int[] map, int i) => i >= 0 && i < map.Length ? map[i] : 0;

        private static SkinnedMeshRenderer FindBody(List<Renderer> parts)
        {
            SkinnedMeshRenderer best = null;
            foreach (Renderer r in parts)
                if (r is SkinnedMeshRenderer s && (best == null || s.sharedMesh.vertexCount > best.sharedMesh.vertexCount)) best = s;
            return best;
        }

        private static int Id(Object o) => o == null ? 0 : RuntimeHelpers.GetHashCode(o);

        private static int MatrixHash(Matrix4x4 m)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < 16; i++) h = h * 31 + Mathf.RoundToInt(m[i] * 10000f);
                return h;
            }
        }

        /// <summary>Markiert das zusammengefasste Objekt (CharacterLod zaehlt es nicht als Ausruestungsaenderung).</summary>
        public sealed class MergedMarker : MonoBehaviour { }
    }
}
