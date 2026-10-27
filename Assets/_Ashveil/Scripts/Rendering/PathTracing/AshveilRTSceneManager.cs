using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Ashveil.Rendering.PathTracing
{
    /// <summary>Instanz-Kategorien (Master-Plan §10). Figur/Kreatur/Boss teilen sich ein Maskenbit.</summary>
    public enum RTCategory { StaticWorld, Terrain, Vegetation, Character, Props, Transparent, Emissive, VfxProxy }

    /// <summary>
    /// Verwaltet die Beschleunigungsstruktur von Hand (ManagementMode.Manual, §9/§136): nur geladene Zellen,
    /// eigene RT-LOD-Wahl, Figuren als eingefrorene Posen (BakeMesh), Gelaende als Stellvertreter-Netz, Transparenz/
    /// Emission als Kategorie. Gebaut wird relativ zu einem Ursprung nahe der Kamera (Floating-Origin-fest, §13).
    /// </summary>
    public sealed class AshveilRTSceneManager : System.IDisposable
    {
        public const uint MaskStatic = 0x01, MaskTerrain = 0x02, MaskVegetation = 0x04, MaskCharacter = 0x08,
                          MaskProps = 0x10, MaskTransparent = 0x20, MaskEmissive = 0x40, MaskVfx = 0x80;

        private sealed class Instance
        {
            public Renderer Renderer;
            public int SubMesh;
            public int Handle;
            public RTCategory Category;
            public Mesh Mesh;
            public bool Skinned;
            public bool Static;
            public Matrix4x4 Matrix;
            public int EmissiveOffset = -1;
            public int Moves;
            public bool Removed;
            public Object Owner;   // Terrain bei Gelaende/Gras (ohne Renderer)
            public Material Proxy; // Stellvertreter-Material (zum Aufraeumen des Materialverzeichnisses)
        }

        private readonly AshveilRTMaterialRegistry _materials;
        private readonly AshveilRTTerrainProxies _terrain;
        private RayTracingAccelerationStructure _rtas;
        private readonly List<Instance> _instances = new List<Instance>();
        private readonly Dictionary<SkinnedMeshRenderer, Mesh> _baked = new Dictionary<SkinnedMeshRenderer, Mesh>();
        private readonly List<AshPTEmissiveTriCpu> _emissive = new List<AshPTEmissiveTriCpu>();
        private GraphicsBuffer _instanceInfo;
        private bool _sceneDirty = true;

        public RayTracingAccelerationStructure Structure => _rtas;
        public GraphicsBuffer InstanceInfo => _instanceInfo;
        public IReadOnlyList<AshPTEmissiveTriCpu> EmissiveTriangles => _emissive;
        public Vector3 Origin { get; private set; }
        public int GeometryVersion { get; private set; }
        public bool NeedsBuild { get; set; }
        public int InstanceCount => _instances.Count;
        public int SkinnedCount { get; private set; }
        public int TerrainCount { get; private set; }
        public long TriangleCount { get; private set; }
        public int SkippedMaterials => _materials.Skipped;
        public ulong MemoryBytes { get; private set; }
        public readonly int[] CategoryCounts = new int[8];
        /// <summary>Instanzen nach Abstand zur Aufnahme-Position: unter 60 m, 120 m, 250 m, darueber.</summary>
        public readonly int[] DistanceCounts = new int[4];
        private Vector3 _collectPos;

        // Optionen fuer andere Nutzer derselben Struktur (dynamisches GI): Figuren, Gras und Leuchtdreiecke kann man
        // weglassen, und statt LOD0 in der Naehe darf die guenstigste Stufe genommen werden (diffuses Licht braucht keine Details).
        public bool IncludeSkinned = true;
        public bool IncludeDetails = true;
        public bool CollectEmissiveTriangles = true;
        public bool PreferCoarseLod;
        public string DebugName = "PT";
        /// <summary>Kleinere Objekte (halbe Diagonale in m) weglassen – fuer diffuses Licht unerheblich, spart Instanzen.</summary>
        public float MinExtent;
        /// <summary>Bewegliche Objekte erst nachfuehren, wenn sie sich so weit (m) bewegt haben (0 = jede Aenderung).</summary>
        public float MoveThreshold;
        public int SkippedSmall { get; private set; }
        /// <summary>Objekte an animierten Figuren (Waffen, Werkzeug, Fackeln in der Hand) weglassen.</summary>
        public bool ExcludeAnimatedAttachments;
        /// <summary>Diagnose: zuletzt bewegtes Objekt und Anzahl Bewegungen.</summary>
        public string LastMoved = "-";
        public int MovedCount;
        /// <summary>Objekte, die sich so oft bewegt haben (Tiere, Wagen im Dauerbetrieb), ganz herausnehmen (0 = nie).</summary>
        public int DropMoversAfter;
        public int DroppedMovers { get; private set; }

        /// <summary>
        /// Streaming um die Kamera (dynamisches GI): Objekte weiter als StreamRadius (und kleiner als StreamKeepSize) kommen
        /// erst hinein, wenn man sich naehert, und fallen wieder heraus, wenn man sich entfernt. Haelt die Instanzzahl klein –
        /// Unity baut je Strahlenaufruf eine Tabelle ueber alle Instanzen (CPU-Kosten je Instanz). 0 = aus.
        /// </summary>
        public float StreamRadius;
        public float StreamKeepSize = 20f;
        /// <summary>
        /// Grosse Objekte (ab StreamKeepSize) streamen ebenfalls, aber mit diesem weiten Radius (0 = immer drin). Nach einem
        /// Sprung in eine ferne Instanz faellt so das 4 km entfernte Kareth heraus, statt jeden Strahlenaufruf zu verteuern.
        /// </summary>
        public float StreamKeepRadius;
        /// <summary>
        /// Hoechstens so viele neue Instanzen zwischen zwei Neubauten (0 = unbegrenzt). Jede neue Instanz eines statisch
        /// gebatchten Objekts braucht beim Bau eine eigene Struktur (BLAS), und der Grafikfaden legt dafuer Speicher an.
        /// Viele auf einmal liessen nach einem Sprung ein Bild 50–64 ms warten (Ruckler-Messung 09.10.2026, CORE-005).
        /// </summary>
        public int MaxAddsPerBuild;
        private int _addsSinceBuild;
        /// <summary>Das Streaming hat Objekte wegen <see cref="MaxAddsPerBuild"/> zurueckgestellt: bald neu bauen.</summary>
        public bool AddsCapped { get; private set; }
        private float StreamRadiusFor(float size) => size >= StreamKeepSize ? StreamKeepRadius : StreamRadius;
        private bool IsStreamed(float size) => StreamRadius > 0f && (size < StreamKeepSize || StreamKeepRadius > 0f);
        private readonly List<(MeshRenderer r, PathTraceSurface a)> _deferred = new List<(MeshRenderer, PathTraceSurface)>();
        private readonly List<MeshRenderer> _streamable = new List<MeshRenderer>();
        private readonly Dictionary<Renderer, List<int>> _byRenderer = new Dictionary<Renderer, List<int>>();
        private int _cursorDeferred, _cursorActive, _removedCount;
        public int ActiveInstances => _instances.Count - _removedCount;
        public int DeferredCount => _deferred.Count;
        /// <summary>Diagnose: alle Netze mit EINEM Material (misst die Kosten der Shader-Tabelle je Material).</summary>
        public Material OverrideMaterial;

        public struct AshPTEmissiveTriCpu
        {
            public Vector3 P0, P1, P2;
            public float Area;
            public float Weight;
            public uint InstanceId;
            public uint Primitive;
            public float Luminance;
        }

        public AshveilRTSceneManager(AshveilRTMaterialRegistry materials, Shader terrainShader)
        {
            _materials = materials;
            _terrain = new AshveilRTTerrainProxies(terrainShader);
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (IncrementalScenes && _rtas != null) _pendingScenes.Enqueue(s);
            else _sceneDirty = true;
        }

        private void OnSceneUnloaded(Scene s)
        {
            if (IncrementalScenes && _rtas != null) _purgeDead = true;
            else _sceneDirty = true;
        }

        /// <summary>
        /// Zellen-Streaming ohne Neuaufnahme (dynamisches GI): neue Szenen werden ueber mehrere Bilder hinzugefuegt, Objekte
        /// entladener Szenen herausgenommen – kein Ruckler beim Betreten einer Zelle.
        /// </summary>
        public bool IncrementalScenes;
        private readonly Queue<Scene> _pendingScenes = new Queue<Scene>();
        private readonly List<Renderer> _incoming = new List<Renderer>();
        private readonly List<Terrain> _incomingTerrains = new List<Terrain>();
        private readonly Dictionary<Renderer, bool> _incomingLod = new Dictionary<Renderer, bool>();
        private bool _purgeDead;
        private float _captureRadius = 4000f;
        public int IncrementalAdds { get; private set; }
        /// <summary>Nach dem Entladen einer Zelle sofort neu bauen (die Struktur zeigt sonst auf freigegebene Netze).</summary>
        public bool RequiresImmediateBuild { get; set; }
        public int TerrainMaxVertsPerSide { get => _terrain.MaxVertsPerSide; set => _terrain.MaxVertsPerSide = value; }

        public bool SceneDirty => _sceneDirty;
        public void MarkDirty() => _sceneDirty = true;

        /// <summary>Alle geladenen, sichtbaren Renderer neu aufnehmen (Zellen-/Szenenwechsel oder Start).</summary>
        public void Collect(Vector3 cameraPos, float captureRadius)
        {
            _sceneDirty = false;
            Origin = new Vector3(Mathf.Round(cameraPos.x), Mathf.Round(cameraPos.y), Mathf.Round(cameraPos.z));
            _rtas?.Dispose();
            var settings = new RayTracingAccelerationStructure.Settings(
                RayTracingAccelerationStructure.ManagementMode.Manual,
                RayTracingAccelerationStructure.RayTracingModeMask.Everything, ~0);
            _rtas = new RayTracingAccelerationStructure(settings);
            _instances.Clear();
            _emissive.Clear();
            System.Array.Clear(CategoryCounts, 0, CategoryCounts.Length);
            System.Array.Clear(DistanceCounts, 0, DistanceCounts.Length);
            _collectPos = cameraPos;
            SkinnedCount = 0;
            SkippedSmall = 0;
            DroppedMovers = 0;
            _deferred.Clear(); _streamable.Clear(); _byRenderer.Clear();
            _cursorDeferred = _cursorActive = _removedCount = 0;
            _addsSinceBuild = 0;
            AddsCapped = false;
            TriangleCount = 0;
            DetailCount = 0;

            _captureRadius = captureRadius;
            _pendingScenes.Clear(); _incoming.Clear(); _incomingTerrains.Clear(); _incomingLod.Clear(); _purgeDead = false;

            // RT-LOD: je LOD-Gruppe genau eine Stufe nach Entfernung (nah LOD0, mittel LOD1, weit letzte Stufe).
            var lodChoice = new Dictionary<Renderer, bool>();
            ChooseLods(Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None), cameraPos, lodChoice);

            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                ConsiderRenderer(r, cameraPos, captureRadius, lodChoice);

            TerrainCount = 0;
            foreach (Terrain t in Terrain.activeTerrains) AddTerrain(t, cameraPos);

            UploadInstanceInfo();
            GeometryVersion++;
            NeedsBuild = true;
        }

        private void ChooseLods(IEnumerable<LODGroup> groups, Vector3 cameraPos, Dictionary<Renderer, bool> lodChoice)
        {
            foreach (LODGroup g in groups)
            {
                if (g == null) continue;
                LOD[] lods = g.GetLODs();
                if (lods.Length == 0) continue;
                float d = Vector3.Distance(cameraPos, g.transform.TransformPoint(g.localReferencePoint));
                int pick = PreferCoarseLod
                    ? (d < 25f ? Mathf.Min(1, lods.Length - 1) : lods.Length - 1)
                    : d < 60f ? 0 : d < 220f ? Mathf.Min(1, lods.Length - 1) : lods.Length - 1;
                for (int i = 0; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null) lodChoice[r] = i == pick;
            }
        }

        /// <summary>Einen Renderer pruefen und (sofort oder spaeter per Streaming) aufnehmen. true, wenn sofort aufgenommen.</summary>
        private static int _previewLayer = -2;

        private bool ConsiderRenderer(Renderer r, Vector3 cameraPos, float captureRadius, Dictionary<Renderer, bool> lodChoice)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) return false;
            if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            if (lodChoice.TryGetValue(r, out bool keep) && !keep) return false;
            if (r is not MeshRenderer && r is not SkinnedMeshRenderer) return false;
            int layer = r.gameObject.layer;
            if (_previewLayer == -2) _previewLayer = LayerMask.NameToLayer("Preview");
            if (layer == 5 || layer == _previewLayer) return false;   // UI, Charaktervorschau
            PathTraceSurface authoring = r.GetComponent<PathTraceSurface>();
            if (authoring != null && authoring.kind == PathTraceSurface.Kind.Ausschliessen) return false;

            Bounds b = r.bounds;
            float dist = Mathf.Sqrt(b.SqrDistance(cameraPos));
            float size = b.extents.magnitude;
            if (dist > captureRadius && size < dist * 0.01f) return false;   // klein und weit weg: unter einem Pixel
            if (size < MinExtent) { SkippedSmall++; return false; }
            if (ExcludeAnimatedAttachments && !r.isPartOfStaticBatch && IsOnMovingEntity(r)) { SkippedSmall++; return false; }

            if (r is SkinnedMeshRenderer smr) { if (IncludeSkinned) AddSkinned(smr, authoring); return IncludeSkinned; }
            if (IsStreamed(size))
            {
                float sr = StreamRadiusFor(size);
                if (dist > sr) { _deferred.Add(((MeshRenderer)r, authoring)); return false; }
                AddMesh((MeshRenderer)r, authoring);
                _streamable.Add((MeshRenderer)r);
                return true;
            }
            AddMesh((MeshRenderer)r, authoring);
            return true;
        }

        private bool AddTerrain(Terrain t, Vector3 cameraPos)
        {
            if (t == null || !t.isActiveAndEnabled || t.terrainData == null) return false;
            if (!_terrain.TryGet(t, out Mesh mesh, out Material mat, out Matrix4x4 m)) return false;
            var cfg = new RayTracingMeshInstanceConfig(mesh, 0, mat)
            {
                subMeshFlags = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly,
                enableTriangleCulling = true,
                frontTriangleCounterClockwise = false,
                mask = MaskTerrain,
                layer = t.gameObject.layer
            };
            AddInstance(cfg, m, null, RTCategory.Terrain, mesh, false, true, 0);
            _instances[_instances.Count - 1].Owner = t;
            TerrainCount++;
            if (IncludeDetails) CollectDetails(t, cameraPos);
            return true;
        }

        /// <summary>Neue Zellen schrittweise aufnehmen, entladene herausnehmen. true bei Aenderung.</summary>
        private bool TickScenes(Vector3 cameraPos, int budget)
        {
            bool changed = false;
            if (_purgeDead)
            {
                _purgeDead = false;
                foreach (Instance inst in _instances)
                {
                    if (inst.Removed) continue;
                    bool dead = inst.Owner != null || inst.Category == RTCategory.Terrain ? inst.Owner == null : inst.Renderer == null;
                    if (!dead) continue;
                    _rtas.RemoveInstance(inst.Handle);
                    Forget(inst);
                    _removedCount++;
                    changed = true;
                }
                var liveTerrains = new HashSet<TerrainData>();
                foreach (Instance inst in _instances)
                    if (!inst.Removed && inst.Category == RTCategory.Terrain && inst.Owner is Terrain lt && lt != null && lt.terrainData != null)
                        liveTerrains.Add(lt.terrainData);
                _terrain.ReleaseDead(liveTerrains);
                var usedProxies = new HashSet<Material>();
                foreach (Instance inst in _instances)
                    if (!inst.Removed && inst.Proxy != null) usedProxies.Add(inst.Proxy);
                _materials.RetainOnlyProxies(usedProxies);
                RequiresImmediateBuild = true;   // entladene Netze nicht mehr in der Struktur lassen
            }
            if (_incoming.Count == 0 && _incomingTerrains.Count == 0 && _pendingScenes.Count > 0)
            {
                Scene sc = _pendingScenes.Dequeue();
                if (sc.IsValid() && sc.isLoaded)
                {
                    var groups = new List<LODGroup>();
                    foreach (GameObject root in sc.GetRootGameObjects())
                    {
                        _incoming.AddRange(root.GetComponentsInChildren<Renderer>());
                        _incomingTerrains.AddRange(root.GetComponentsInChildren<Terrain>());
                        groups.AddRange(root.GetComponentsInChildren<LODGroup>());
                    }
                    _incomingLod.Clear();
                    ChooseLods(groups, cameraPos, _incomingLod);
                }
            }
            for (int k = 0; k < budget && _incoming.Count > 0; k++)
            {
                Renderer r = _incoming[_incoming.Count - 1];
                _incoming.RemoveAt(_incoming.Count - 1);
                if (ConsiderRenderer(r, cameraPos, _captureRadius, _incomingLod)) { changed = true; IncrementalAdds++; }
            }
            if (_incoming.Count == 0 && _incomingTerrains.Count > 0)
            {
                Terrain t = _incomingTerrains[_incomingTerrains.Count - 1];
                _incomingTerrains.RemoveAt(_incomingTerrains.Count - 1);
                if (AddTerrain(t, cameraPos)) { changed = true; IncrementalAdds++; }
            }
            return changed;
        }

        public int DetailCount { get; private set; }

        /// <summary>Teil einer Figur, eines Tieres oder eines Gegners (animiert, laeuft per NavMesh/Controller, Physik)?</summary>
        private static bool IsOnMovingEntity(Renderer r) =>
            r.GetComponentInParent<Animator>() != null ||
            r.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null ||
            r.GetComponentInParent<CharacterController>() != null ||
            r.GetComponentInParent<Rigidbody>() != null;

        /// <summary>
        /// Gras/Farn (Terrain-Details mit eigenem Netz) nur im Nahbereich (§89): nicht Millionen Halme blind in die
        /// Struktur, sondern die tatsaechlich gezeichneten Instanzen bis zur Detail-Sichtweite (hoechstens 60 m).
        /// </summary>
        private void CollectDetails(Terrain t, Vector3 cameraPos)
        {
            TerrainData d = t.terrainData;
            DetailPrototype[] protos = d.detailPrototypes;
            if (protos == null || protos.Length == 0 || t.detailObjectDensity <= 0f) return;
            float radius = Mathf.Min(t.detailObjectDistance, 60f);
            Vector3 tp = t.GetPosition();
            int perPatch = Mathf.Max(1, d.detailResolutionPerPatch);
            int patches = Mathf.Max(1, d.detailResolution / perPatch);
            float patchSizeX = d.size.x / patches, patchSizeZ = d.size.z / patches;
            for (int layer = 0; layer < protos.Length; layer++)
            {
                DetailPrototype proto = protos[layer];
                if (!proto.usePrototypeMesh || proto.prototype == null) continue;
                MeshFilter mf = proto.prototype.GetComponentInChildren<MeshFilter>();
                MeshRenderer mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
                if (mf == null || mr == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                Matrix4x4 local = proto.prototype.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Material[] mats = mr.sharedMaterials;
                var entries = new AshveilRTMaterialRegistry.Entry[mesh.subMeshCount];
                var ok = new bool[mesh.subMeshCount];
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                    ok[sm] = _materials.TryGet(sm < mats.Length ? mats[sm] : mats[mats.Length - 1], null, out entries[sm]);
                for (int pz = 0; pz < patches; pz++)
                for (int px = 0; px < patches; px++)
                {
                    Vector3 c = tp + new Vector3((px + 0.5f) * patchSizeX, 0f, (pz + 0.5f) * patchSizeZ);
                    float dx = c.x - cameraPos.x, dz = c.z - cameraPos.z;
                    float reach = radius + 0.75f * Mathf.Max(patchSizeX, patchSizeZ);
                    if (dx * dx + dz * dz > reach * reach) continue;
                    DetailInstanceTransform[] inst = d.ComputeDetailInstanceTransforms(px, pz, layer, t.detailObjectDensity, out Bounds _);
                    if (inst == null) continue;
                    foreach (DetailInstanceTransform di in inst)
                    {
                        Vector3 pos = tp + new Vector3(di.posX, di.posY, di.posZ);
                        if ((pos - cameraPos).sqrMagnitude > radius * radius) continue;
                        Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.Euler(0f, di.rotationY * Mathf.Rad2Deg, 0f), new Vector3(di.scaleXZ, di.scaleY, di.scaleXZ)) * local;
                        for (int sm = 0; sm < mesh.subMeshCount; sm++)
                        {
                            if (!ok[sm]) continue;
                            var e = entries[sm];
                            var cfg = new RayTracingMeshInstanceConfig(mesh, (uint)sm, e.Proxy)
                            {
                                subMeshFlags = e.AlphaTest ? RayTracingSubMeshFlags.Enabled : RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly,
                                enableTriangleCulling = !e.DoubleSided,
                                frontTriangleCounterClockwise = false,
                                mask = MaskVegetation,
                                layer = t.gameObject.layer
                            };
                            AddInstance(cfg, m, null, RTCategory.Vegetation, mesh, false, true, sm);
                            _instances[_instances.Count - 1].Owner = t;
                            DetailCount++;
                        }
                    }
                }
            }
        }

        private void AddMesh(MeshRenderer r, PathTraceSurface authoring)
        {
            var mf = r.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0) return;
            Material[] mats = r.sharedMaterials;
            Matrix4x4 m = r.localToWorldMatrix;
            bool isStatic = r.gameObject.isStatic;
            int first = 0, count = mesh.subMeshCount;
            if (r.isPartOfStaticBatch)
            {
                // Statisches Batching: gemeinsames Netz in Weltkoordinaten, dieses Objekt belegt einen Teilbereich
                first = r.subMeshStartIndex;
                count = mats.Length;
                m = Matrix4x4.identity;
                isStatic = true;
            }
            for (int i = 0; i < count; i++)
            {
                int s = first + i;
                if (s >= mesh.subMeshCount) break;
                Material src = i < mats.Length ? mats[i] : (mats.Length > 0 ? mats[mats.Length - 1] : null);
                if (!_materials.TryGet(src, authoring, out var e)) continue;
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                RTCategory cat = Categorize(e, isStatic, false);
                AddSubmesh(r, mesh, s, e, m, cat, false, isStatic);
            }
        }

        private void AddSkinned(SkinnedMeshRenderer smr, PathTraceSurface authoring)
        {
            if (smr.sharedMesh == null) return;
            if (!_baked.TryGetValue(smr, out Mesh baked) || baked == null)
            {
                baked = new Mesh { name = smr.sharedMesh.name + " (" + DebugName + "-Pose)", hideFlags = HideFlags.DontSave };
                baked.indexFormat = smr.sharedMesh.indexFormat;
                _baked[smr] = baked;
            }
            smr.BakeMesh(baked, true);
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            Material[] mats = smr.sharedMaterials;
            for (int s = 0; s < baked.subMeshCount; s++)
            {
                Material src = s < mats.Length ? mats[s] : (mats.Length > 0 ? mats[mats.Length - 1] : null);
                if (!_materials.TryGet(src, authoring, out var e)) continue;
                AddSubmesh(smr, baked, s, e, m, e.Emissive ? RTCategory.Emissive : RTCategory.Character, true, false);
            }
            SkinnedCount++;
        }

        private static RTCategory Categorize(AshveilRTMaterialRegistry.Entry e, bool isStatic, bool skinned)
        {
            if (e.Emissive) return RTCategory.Emissive;
            if (e.Transparent || e.Dielectric) return RTCategory.Transparent;
            if (e.Vegetation) return RTCategory.Vegetation;
            if (skinned) return RTCategory.Character;
            return isStatic ? RTCategory.StaticWorld : RTCategory.Props;
        }

        private static uint MaskFor(RTCategory c) => c switch
        {
            RTCategory.StaticWorld => MaskStatic,
            RTCategory.Terrain => MaskTerrain,
            RTCategory.Vegetation => MaskVegetation,
            RTCategory.Character => MaskCharacter,
            RTCategory.Transparent => MaskTransparent,
            RTCategory.Emissive => MaskEmissive,
            RTCategory.VfxProxy => MaskVfx,
            _ => MaskProps
        };

        private void AddSubmesh(Renderer r, Mesh mesh, int sub, AshveilRTMaterialRegistry.Entry e, Matrix4x4 m, RTCategory cat, bool skinned, bool isStatic)
        {
            bool mirrored = m.determinant < 0f;
            var cfg = new RayTracingMeshInstanceConfig(mesh, (uint)sub, OverrideMaterial != null ? OverrideMaterial : e.Proxy)
            {
                subMeshFlags = (e.AlphaTest || e.Transparent)
                    ? RayTracingSubMeshFlags.Enabled
                    : RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly,
                dynamicGeometry = skinned,
                enableTriangleCulling = !e.DoubleSided,
                frontTriangleCounterClockwise = mirrored,
                mask = MaskFor(cat),
                layer = r.gameObject.layer
            };
            AddInstance(cfg, m, r, cat, mesh, skinned, isStatic, sub);
            _instances[_instances.Count - 1].Proxy = e.Proxy;
            if (e.Emissive && CollectEmissiveTriangles) CollectEmissive(mesh, sub, m, e, (uint)(_instances.Count - 1));
        }

        private void AddInstance(RayTracingMeshInstanceConfig cfg, Matrix4x4 m, Renderer r, RTCategory cat, Mesh mesh, bool skinned, bool isStatic, int sub)
        {
            uint id = (uint)_instances.Count;
            int handle = _rtas.AddInstance(cfg, m, null, id);
            _instances.Add(new Instance { Renderer = r, SubMesh = sub, Handle = handle, Category = cat, Mesh = mesh, Skinned = skinned, Static = isStatic, Matrix = m });
            if (r != null && StreamRadius > 0f)
            {
                if (!_byRenderer.TryGetValue(r, out List<int> ids)) _byRenderer[r] = ids = new List<int>();
                ids.Add((int)id);
            }
            CategoryCounts[(int)cat]++;
            float dd = r != null ? Mathf.Sqrt(r.bounds.SqrDistance(_collectPos)) : 0f;
            DistanceCounts[dd < 60f ? 0 : dd < 120f ? 1 : dd < 250f ? 2 : 3]++;
            TriangleCount += (long)(mesh.GetIndexCount(sub) / 3);
        }

        private void CollectEmissive(Mesh mesh, int sub, Matrix4x4 m, AshveilRTMaterialRegistry.Entry e, uint instanceId)
        {
            if (!mesh.isReadable || _emissive.Count > 65536) return;
            var verts = new List<Vector3>();
            mesh.GetVertices(verts);
            int[] tris = mesh.GetTriangles(sub);
            int offset = _emissive.Count;
            float lum = e.EmissionLuminance * (e.HasEmissionMap ? 0.5f : 1f);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 p0 = m.MultiplyPoint3x4(verts[tris[t]]) - Origin;
                Vector3 p1 = m.MultiplyPoint3x4(verts[tris[t + 1]]) - Origin;
                Vector3 p2 = m.MultiplyPoint3x4(verts[tris[t + 2]]) - Origin;
                float area = 0.5f * Vector3.Cross(p1 - p0, p2 - p0).magnitude;
                _emissive.Add(new AshPTEmissiveTriCpu
                {
                    P0 = p0, P1 = p1, P2 = p2, Area = area, Weight = area * lum * (e.DoubleSided ? 2f : 1f),
                    InstanceId = instanceId, Primitive = (uint)(t / 3), Luminance = lum
                });
            }
            _instances[(int)instanceId].EmissiveOffset = offset;
        }

        private void UploadInstanceInfo()
        {
            int n = Mathf.Max(1, _instances.Count);
            if (_instanceInfo == null || _instanceInfo.count < n)
            {
                _instanceInfo?.Release();
                _instanceInfo = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(n), 16);
            }
            var data = new uint[n * 4];
            for (int i = 0; i < _instances.Count; i++)
            {
                data[i * 4] = _instances[i].EmissiveOffset >= 0 ? (uint)_instances[i].EmissiveOffset : 0xFFFFFFFFu;
                data[i * 4 + 1] = (uint)_instances[i].Category;
            }
            if (_instances.Count == 0) data[0] = 0xFFFFFFFFu;
            _instanceInfo.SetData(data);
        }

        /// <summary>
        /// Bewegliche Objekte nachfuehren (nur wenn die Welt nicht eingefroren ist): Transformationen und Posen.
        /// Rueckgabe: true, wenn sich etwas geaendert hat (→ Akkumulation neu).
        /// </summary>
        public bool UpdateDynamic() => UpdateDynamic(0, 1);

        /// <summary>Nur jede sliceCount-te Instanz ab sliceIndex pruefen (Kosten ueber mehrere Bilder verteilen).</summary>
        public bool UpdateDynamic(int sliceIndex, int sliceCount)
        {
            bool changed = false;
            var rebaked = new HashSet<SkinnedMeshRenderer>();
            sliceCount = Mathf.Max(1, sliceCount);
            for (int ii = ((sliceIndex % sliceCount) + sliceCount) % sliceCount; ii < _instances.Count; ii += sliceCount)
            {
                Instance inst = _instances[ii];
                if (inst.Removed || inst.Static || inst.Renderer == null) continue;
                if (inst.Skinned)
                {
                    var smr = (SkinnedMeshRenderer)inst.Renderer;
                    if (rebaked.Add(smr)) smr.BakeMesh(inst.Mesh, true);
                    Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    _rtas.UpdateInstanceTransform(inst.Handle, m);
                    inst.Matrix = m;
                    changed = true;
                    continue;
                }
                Matrix4x4 now = inst.Renderer.localToWorldMatrix;
                if (MoveThreshold > 0f)
                {
                    // Nur spuerbare Bewegung zaehlt (Werkzeug in der Hand, wehende Fahne: nein; Karren, Tor: ja)
                    Vector3 dp = now.GetColumn(3) - inst.Matrix.GetColumn(3);
                    float turn = Vector3.Dot(((Vector3)now.GetColumn(2)).normalized, ((Vector3)inst.Matrix.GetColumn(2)).normalized);
                    if (dp.sqrMagnitude < MoveThreshold * MoveThreshold && turn > 0.995f) continue;
                }
                if (now != inst.Matrix)
                {
                    MovedCount++;
                    LastMoved = inst.Renderer.name + " (" + (inst.Renderer.transform.parent != null ? inst.Renderer.transform.parent.name : "-") + ")";
                    inst.Moves++;
                    if (DropMoversAfter > 0 && inst.Moves >= DropMoversAfter)
                    {
                        _rtas.RemoveInstance(inst.Handle);
                        Forget(inst);
                        _removedCount++;
                        DroppedMovers++;
                        changed = true;
                        continue;
                    }
                    _rtas.UpdateInstanceTransform(inst.Handle, now);
                    inst.Matrix = now;
                    changed = true;
                }
            }
            if (changed) { NeedsBuild = true; GeometryVersion++; }
            return changed;
        }

        /// <summary>Streaming-Schritt: hoechstens budget Objekte pruefen (hinein und heraus). true bei Aenderung.</summary>
        public bool Stream(Vector3 cameraPos, int budget)
        {
            if (_rtas == null) return false;
            bool changed = IncrementalScenes && TickScenes(cameraPos, budget);
            if (StreamRadius <= 0f) { if (changed) { NeedsBuild = true; GeometryVersion++; } return changed; }
            // hinein (hoechstens MaxAddsPerBuild neue Instanzen je Neubau)
            AddsCapped = false;
            for (int k = 0; k < budget && _deferred.Count > 0; k++)
            {
                if (MaxAddsPerBuild > 0 && _addsSinceBuild >= MaxAddsPerBuild) { AddsCapped = true; break; }
                if (_cursorDeferred >= _deferred.Count) _cursorDeferred = 0;
                (MeshRenderer r, PathTraceSurface a) = _deferred[_cursorDeferred];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) { SwapRemove(_deferred, _cursorDeferred); continue; }
                Bounds bIn = r.bounds;
                float rIn = StreamRadiusFor(bIn.extents.magnitude);
                if (bIn.SqrDistance(cameraPos) < rIn * rIn)
                {
                    int vorher = _instances.Count;
                    AddMesh(r, a);
                    _addsSinceBuild += _instances.Count - vorher;
                    _streamable.Add(r);
                    SwapRemove(_deferred, _cursorDeferred);
                    changed = true;
                }
                else _cursorDeferred++;
            }
            // heraus
            for (int k = 0; k < budget && _streamable.Count > 0; k++)
            {
                if (_cursorActive >= _streamable.Count) _cursorActive = 0;
                MeshRenderer r = _streamable[_cursorActive];
                bool gone = r == null;
                if (!gone)
                {
                    Bounds bOut = r.bounds;
                    float outR = StreamRadiusFor(bOut.extents.magnitude) * 1.25f;
                    if (bOut.SqrDistance(cameraPos) < outR * outR) { _cursorActive++; continue; }
                }
                if (_byRenderer.TryGetValue(r, out List<int> ids))
                {
                    foreach (int id in ids)
                    {
                        Instance inst = _instances[id];
                        if (inst.Removed) continue;
                        _rtas.RemoveInstance(inst.Handle);
                        Forget(inst);
                        _removedCount++;
                    }
                    _byRenderer.Remove(r);
                }
                if (!gone) _deferred.Add((r, r.GetComponent<PathTraceSurface>()));
                SwapRemove(_streamable, _cursorActive);
                changed = true;
            }
            if (changed) { NeedsBuild = true; GeometryVersion++; }
            return changed;
        }

        /// <summary>Instanz gilt als entfernt; Verweise loesen, damit Netze/Materialien entladener Zellen freigegeben werden koennen.</summary>
        private static void Forget(Instance inst)
        {
            inst.Removed = true;
            inst.Mesh = null;
            inst.Renderer = null;
            inst.Owner = null;
            inst.Proxy = null;
        }

        private static void SwapRemove<T>(List<T> list, int i)
        {
            int last = list.Count - 1;
            list[i] = list[last];
            list.RemoveAt(last);
        }

        public void Build(CommandBuffer cmd)
        {
            if (_rtas == null) return;
            cmd.BuildRayTracingAccelerationStructure(_rtas, Origin);
            NeedsBuild = false;
            _addsSinceBuild = 0;
        }

        public void UpdateMemoryStat()
        {
            if (_rtas != null) MemoryBytes = _rtas.GetSize();
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _rtas?.Dispose();
            _rtas = null;
            _instanceInfo?.Release();
            _instanceInfo = null;
            foreach (Mesh m in _baked.Values) if (m != null) Object.Destroy(m);
            _baked.Clear();
            _terrain.Dispose();
        }
    }
}
