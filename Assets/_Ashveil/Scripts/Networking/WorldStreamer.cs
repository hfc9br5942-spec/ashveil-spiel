using System.Collections;
using System.Collections.Generic;
using System.Text;
using Ashveil.Core;
using Ashveil.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.Networking
{
    /// <summary>
    /// Large-World Phase E1: laedt und entlaedt Weltzellen (additive Szenen ohne NetworkObjects) nach Bedarf –
    /// auf dem Server fuer alle Spieler (Vereinigung), auf dem Client fuer die eigene Figur, im Host beides.
    /// Konventionen: Docs/World/WorldStreaming.md.
    ///
    ///   Bedarf   : Figur (oder ihre Position in <see cref="predictSeconds"/> s) naeher als loadMargin an der Zelle,
    ///              oder eine angekuendigte Reise (<see cref="RequestPreload"/>: Login, Teleport, Karren).
    ///   Halten   : solange eine Figur naeher als unloadMargin ist; danach noch holdSeconds.
    ///   Zustaende: Unloaded → Loading → Loaded → Ready (ein Frame spaeter; NavMesh registriert) → Unloading.
    ///
    /// Die Welt-Ebene bleibt die aktive Szene (nie SetActiveScene auf eine Zelle). Jeder Lade-/Entladevorgang wird mit
    /// Dauer, laengstem Frame, GC-Zaehler und Speicher protokolliert (Messen statt schaetzen).
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class WorldStreamer : MonoBehaviour
    {
        public enum CellState { Unloaded, Loading, Loaded, Ready, Unloading }

        [SerializeField] private WorldManifest manifest;
        [Tooltip("Vorausschau: Position in so vielen Sekunden (aktuelles Tempo) zaehlt als Bedarf")]
        [SerializeField] private float predictSeconds = 8f;
        [Tooltip("Pruefintervall (s)")]
        [SerializeField] private float interval = 0.25f;
        [Tooltip("Nach dem Entladen hoechstens so oft (s) ungenutzte Assets freigeben")]
        [SerializeField] private float unloadAssetsEvery = 30f;
        [Tooltip("Asset-Sammlung nach dem Entladen erst, wenn die Kamera so lange (s) still steht (Client) – sie haelt den Hauptfaden " +
                 "85–100 ms an (Messung 14.10.); im ruhigen Bild faellt das nicht auf")]
        [SerializeField] private float sammelnNachRuhe = 1.5f;
        [Tooltip("Spaetestens nach so vielen Sekunden trotzdem sammeln (Client)")]
        [SerializeField] private float sammelnSpaetestens = 300f;
        [Tooltip("Sofort sammeln, wenn Arbeits- oder Grafikspeicher ueber diesem Anteil liegt")]
        [SerializeField, Range(0.3f, 0.95f)] private float sammelnBeiSpeicher = 0.75f;

        private sealed class Cell
        {
            public WorldCellDefinition Def;
            public WorldRegionDefinition Region;
            public CellState State;
            public float LastKept = -999f;
            public Scene Scene;
            public string SceneName;
            public float Since;
            public int Loads;
        }

        private readonly Dictionary<string, Cell> _cells = new Dictionary<string, Cell>();
        private readonly List<(Vector3 pos, float until)> _preloads = new List<(Vector3, float)>();
        private readonly Dictionary<Object, (Vector3 pos, float time)> _last = new Dictionary<Object, (Vector3, float)>();
        private readonly List<Vector3> _points = new List<Vector3>();
        private float _next;
        private float _lastAssetUnload = -999f;
        private float _sammelnFaelligSeit = -1f;      // < 0: nichts vorgemerkt
        private bool _sammelt;
        private Vector3 _kamPos; private Quaternion _kamRot; private float _kamRuhig;

        [Tooltip("Angekündigte Reise: Ziel vor der Ankunft aus vier Richtungen in ein kleines Bild rendern, damit Geländedetails, " +
                 "Shader-Varianten und Grafikspeicher schon da sind, bevor der Spieler hinsieht (Messung 12.10.: sonst ein Bild von 0,5 s)")]
        [SerializeField] private bool warmUp = true;
        private static RenderTexture _warmRt;
        /// <summary>Letztes Aufwärmen: Bilder und längstes Bild (ms) – Tests.</summary>
        public static int WarmUpFrames;
        public static float WarmUpWorstMs;

        public static WorldStreamer Instance { get; private set; }
        public WorldManifest Manifest => manifest;

        /// <summary>Protokoll der letzten Lade-/Entladevorgaenge (Tests, Debug-Anzeige).</summary>
        public static readonly List<string> Log = new List<string>();
        /// <summary>Laengstes Bild beim Laden einer Zelle seit dem letzten Zuruecksetzen (ms) – fuer Tests im fertigen Spiel.</summary>
        public static float WorstLoadFrameMs;

        public void Configure(WorldManifest m) => manifest = m;

        private void Awake()
        {
            Instance = this;
            if (manifest == null) return;
            foreach ((WorldRegionDefinition region, WorldCellDefinition def) in manifest.AllCells())
            {
                var c = new Cell { Def = def, Region = region, SceneName = def.SceneName };
                if (def.alwaysLoaded || string.IsNullOrEmpty(def.scenePath)) c.State = CellState.Ready;
                _cells[def.cellId] = c;
            }
        }

        private void Start()
        {
            foreach (Cell c in _cells.Values)
            {
                if (c.State == CellState.Ready && (c.Def.alwaysLoaded || string.IsNullOrEmpty(c.Def.scenePath)))
                {
                    WorldCells.RaiseReady(c.Def.cellId); // Welt-Ebene (Kareth) ist ab Start bereit
                    continue;
                }
                // im Editor schon additiv geoeffnet?
                Scene s = SceneManager.GetSceneByPath(c.Def.scenePath);
                if (s.IsValid() && s.isLoaded)
                {
                    c.Scene = s;
                    c.State = CellState.Ready;
                    c.LastKept = Time.time;
                    WorldCells.RaiseReady(c.Def.cellId);
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            WorldCells.DebugInfo = null;
        }

        // ================================================================== Oeffentlich

        /// <summary>Zustand einer Zelle (Unloaded, wenn unbekannt).</summary>
        public CellState StateOf(string cellId) => _cells.TryGetValue(cellId, out Cell c) ? c.State : CellState.Unloaded;

        /// <summary>Szenenname einer Zelle (fuer Systeme, die Objekte einer Zelle zuordnen).</summary>
        public string SceneOf(string cellId) => _cells.TryGetValue(cellId, out Cell c) ? c.SceneName : null;

        /// <summary>Zelle, deren Grundflaeche den Punkt enthaelt (null ausserhalb aller Zellen, z. B. Dungeon-Instanzen).</summary>
        public string CellAt(Vector3 pos)
        {
            if (manifest == null) return null;
            WorldCellDefinition c = manifest.CellAt(pos, out _);
            return c?.cellId;
        }

        /// <summary>
        /// Darf eine Figur hier stehen? Ja, wenn der Punkt in keiner Zelle liegt oder seine Zelle mindestens geladen ist
        /// (Gelaende und Kollision da). Fallschutz: der Server haelt Figuren an der Grenze, statt sie fallen zu lassen.
        /// </summary>
        public bool IsWalkable(Vector3 pos)
        {
            string id = CellAt(pos);
            if (id == null || !_cells.TryGetValue(id, out Cell c)) return true;
            return c.State == CellState.Loaded || c.State == CellState.Ready;
        }

        /// <summary>Angekuendigte Reise (Login, Teleport, Karren): Zellen um das Ziel fuer einige Sekunden vorladen.</summary>
        public void RequestPreload(Vector3 target, float seconds = 20f)
        {
            _preloads.Add((target, Time.time + seconds));
            _next = 0f;
        }

        /// <summary>Ist das Ziel bereit (fuer Teleport/Login, Regel 5)?</summary>
        public bool IsReadyAt(Vector3 pos)
        {
            string id = CellAt(pos);
            return id == null || !_cells.TryGetValue(id, out Cell c) || c.State == CellState.Ready;
        }

        // ================================================================== Takt

        private void Update()
        {
            if (manifest == null) return;
            if (_sammelnFaelligSeit >= 0f && !_sammelt) SammelnPruefen();
            if (Time.time < _next) return;
            _next = Time.time + interval;
            CollectDemand();
            MeerHalten();   // 22.10. WELTZELLEN-01: vor der Zellschleife, damit LastKept das Entladen schon in diesem Takt verhindert
            foreach (Cell c in _cells.Values)
            {
                if (c.Def.alwaysLoaded || string.IsNullOrEmpty(c.Def.scenePath)) continue;
                bool need = false, keep = false;
                foreach (Vector3 p in _points)
                {
                    float d = c.Def.Distance(p);
                    if (d <= c.Def.loadMargin) need = true;
                    if (d <= c.Def.unloadMargin) keep = true;
                }
                if (need || keep) c.LastKept = Time.time;
                if (need && c.State == CellState.Unloaded) StartCoroutine(Load(c));
                else if (!keep && c.State == CellState.Ready && Time.time - c.LastKept > c.Def.holdSeconds) StartCoroutine(Unload(c));
            }
            WorldCells.DebugInfo = Summary();
        }

        /// <summary>
        /// 22.10. WELTZELLEN-01: Das Meer vor der Westweiden-Küste gehört zur Zellszene, reicht aber weit über das Zellrechteck
        /// hinaus (WestweidenWasser: bis 30 km). Wer mehr als unloadMargin hinausschwamm, verlor nach holdSeconds Zelle und Wasser,
        /// fiel unter die Welt und stand ohne Erklärung am Bindepunkt in Kareth. Jetzt hält ein Bedarfspunkt außerhalb aller Zellen,
        /// der in einer Wasserfläche liegt, die Zelle DIESER Wasserfläche geladen – auf Server und Client gleich, ohne Daten- oder
        /// Szenenänderung. Instanzplätze liegen im Meer-Rechteck, aber rund 175 m über dem Spiegel (Abstand 50 m wie der Motor) und
        /// sind zusätzlich über <see cref="DungeonSlot.At"/> ausgenommen; sonst hielte jede Instanz die Westweiden dauerhaft.
        /// </summary>
        private void MeerHalten()
        {
            const float ueberSpiegel = 50f;   // wie CharacterMotor / WaterVolume.TrySurface (Standard)
            foreach (Vector3 p in _points)
            {
                if (manifest.CellAt(p, out _) != null || DungeonSlot.At(p) != null) continue;
                foreach (WaterVolume w in WaterVolume.All)
                {
                    if (w == null || !w.AreaXZ.Contains(new Vector2(p.x, p.z)) || w.SurfaceY < p.y - ueberSpiegel) continue;
                    foreach (Cell c in _cells.Values)
                        if (!c.Def.alwaysLoaded && (c.State == CellState.Ready || c.State == CellState.Loaded) &&
                            c.Scene.IsValid() && w.gameObject.scene == c.Scene)
                            c.LastKept = Time.time;
                }
            }
        }

        /// <summary>Bedarfspunkte: Figuren (Server: alle; Client: die eigene) plus Vorausschau plus angekuendigte Reisen.</summary>
        private void CollectDemand()
        {
            _points.Clear();
            float now = Time.time;
            foreach (NetworkMotion m in NetworkMotion.Active)
            {
                if (m == null || !(m.IsServerInitialized || m.IsOwner)) continue;
                Vector3 pos = m.transform.position;
                _points.Add(pos);
                if (_last.TryGetValue(m, out (Vector3 pos, float time) prev) && now > prev.time)
                {
                    Vector3 v = (pos - prev.pos) / (now - prev.time);
                    v.y = 0f;
                    if (v.sqrMagnitude < 60f * 60f) _points.Add(pos + v * predictSeconds); // Teleports nicht als Tempo werten
                }
                _last[m] = (pos, now);
            }
            for (int i = _preloads.Count - 1; i >= 0; i--)
            {
                if (_preloads[i].until < now) _preloads.RemoveAt(i);
                else _points.Add(_preloads[i].pos);
            }
            // Ohne Netzwerk (Editor-Vorschau): Kamera
            if (NetworkMotion.Active.Count == 0 && Camera.main != null && Application.isEditor) _points.Add(Camera.main.transform.position);
        }

        // ================================================================== Laden / Entladen

        private IEnumerator Load(Cell c)
        {
            c.State = CellState.Loading;
            c.Since = Time.realtimeSinceStartup;
            int gc0 = System.GC.CollectionCount(0);
            long mem0 = System.GC.GetTotalMemory(false);
            float worst = 0f;
            AsyncOperation op = SceneManager.LoadSceneAsync(c.Def.scenePath, LoadSceneMode.Additive);
            if (op == null)
            {
                Note($"FEHLER: Zelle {c.Def.cellId} laesst sich nicht laden ({c.Def.scenePath} nicht in der Build-Liste?)");
                c.State = CellState.Unloaded;
                c.LastKept = Time.time + 30f; // nicht sofort erneut versuchen
                yield break;
            }
            while (!op.isDone)
            {
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                yield return null;
            }
            c.Scene = SceneManager.GetSceneByPath(c.Def.scenePath);
            c.State = CellState.Loaded;
            ConnectTerrains();
            yield return null; // NavMesh-Daten der Zelle sind mit OnEnable registriert
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            // Aufwärmen am Reiseziel (nur mit Kamera, also Client/Host; der Server rendert nichts)
            if (warmUp && Camera.main != null && PreloadZiel(c, out Vector3 ziel))
            {
                string wie = "";
                yield return WarmUp(ziel, m => wie = m);
                Note(wie);
            }
            c.State = CellState.Ready;
            c.Loads++;
            WorldCells.RaiseReady(c.Def.cellId);
            WorstLoadFrameMs = Mathf.Max(WorstLoadFrameMs, worst * 1000f);
            Note($"geladen {c.Def.cellId}: {(Time.realtimeSinceStartup - c.Since) * 1000f:0} ms, laengster Frame {worst * 1000f:0} ms, " +
                 $"GC {System.GC.CollectionCount(0) - gc0}, Speicher {(System.GC.GetTotalMemory(false) - mem0) / 1048576f:+0.0;-0.0} MB");
        }

        /// <summary>Angekündigtes Reiseziel in dieser Zelle (das jüngste), wenn es eines gibt.</summary>
        private bool PreloadZiel(Cell c, out Vector3 ziel)
        {
            for (int i = _preloads.Count - 1; i >= 0; i--)
                if (CellAt(_preloads[i].pos) == c.Def.cellId) { ziel = _preloads[i].pos; return true; }
            ziel = default;
            return false;
        }

        /// <summary>
        /// Vier Bilder aus Augenhöhe am Ziel in ein kleines Bild (256²), je eines pro Frame: erzeugt die Gelände-Detailfelder
        /// (DetailMesh), legt Shader-Varianten und Puffer an. Die Hauptkamera bleibt unberührt.
        /// </summary>
        private IEnumerator WarmUp(Vector3 ziel, System.Action<string> fertig)
        {
            Camera main = Camera.main;
            if (_warmRt == null) _warmRt = new RenderTexture(256, 256, 24) { name = "WorldStreamer Aufwärmen" };
            var go = new GameObject("WorldStreamer Aufwärmen") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.targetTexture = _warmRt;
            cam.enabled = false;
            cam.depth = main.depth - 10f;
            ziel.y = WorldGround.SampleHeight(ziel, ziel.y) + 1.7f;
            float t0 = Time.realtimeSinceStartup, worst = 0f;
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                cam.transform.SetPositionAndRotation(ziel, Quaternion.Euler(5f, i * 90f, 0f));
                float f0 = Time.realtimeSinceStartup;
                try { cam.Render(); n++; }
                catch (System.Exception e) { Debug.LogWarning("[Ashveil] Weltzellen: Aufwärmen – " + e.Message); break; }
                worst = Mathf.Max(worst, Time.realtimeSinceStartup - f0);
                yield return null;
            }
            Destroy(go);
            WarmUpFrames = n;
            WarmUpWorstMs = worst * 1000f;
            fertig($"aufgewaermt am Ziel ({ziel.x:0}/{ziel.z:0}): {n} Bilder, {(Time.realtimeSinceStartup - t0) * 1000f:0} ms, laengstes Rendern {worst * 1000f:0} ms");
        }

        private IEnumerator Unload(Cell c)
        {
            c.State = CellState.Unloading;
            c.Since = Time.realtimeSinceStartup;
            long mem0 = System.GC.GetTotalMemory(false);
            WorldCells.RaiseUnloading(c.Def.cellId); // Gegner entfernen, Referenzen loesen
            yield return null;
            if (c.Scene.IsValid() && c.Scene.isLoaded)
            {
                AsyncOperation op = SceneManager.UnloadSceneAsync(c.Scene);
                while (op != null && !op.isDone) yield return null;
            }
            c.State = CellState.Unloaded;
            ConnectTerrains();
            // 14.10.2026: nicht mehr im selben Bild sammeln (85–100 ms Hänger beim Weitergehen) – vormerken, siehe SammelnPruefen
            if (_sammelnFaelligSeit < 0f) _sammelnFaelligSeit = Time.time;
            Note($"entladen {c.Def.cellId}: {(Time.realtimeSinceStartup - c.Since) * 1000f:0} ms, Speicher {(System.GC.GetTotalMemory(false) - mem0) / 1048576f:+0.0;-0.0} MB");
        }

        /// <summary>
        /// Vorgemerkte Asset-Sammlung nach dem Entladen (Resources.UnloadUnusedAssets hält den Hauptfaden an): Client mit Grafik –
        /// sobald die Kamera <see cref="sammelnNachRuhe"/> s still steht und keine Zelle lädt, spätestens nach
        /// <see cref="sammelnSpaetestens"/> s; Server ohne Grafik – nur bei Speicherdruck (sonst stünde der Takt für alle Spieler).
        /// Beide: sofort bei Speicherdruck. Höchstens alle <see cref="unloadAssetsEvery"/> s.
        /// </summary>
        private void SammelnPruefen()
        {
            if (Time.time - _lastAssetUnload < unloadAssetsEvery) return;
            foreach (Cell c in _cells.Values) if (c.State == CellState.Loading || c.State == CellState.Unloading) return;
            bool druck = Speicherdruck(out string warum);
            bool grafik = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            bool ruhig = false;
            if (grafik)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Transform t = cam.transform;
                    if ((t.position - _kamPos).sqrMagnitude > 0.0025f || Quaternion.Angle(t.rotation, _kamRot) > 0.3f)
                    {
                        _kamPos = t.position; _kamRot = t.rotation; _kamRuhig = Time.unscaledTime;
                    }
                    ruhig = Time.unscaledTime - _kamRuhig >= sammelnNachRuhe;
                }
            }
            bool spaet = grafik && Time.time - _sammelnFaelligSeit > sammelnSpaetestens;
            if (druck || ruhig || spaet) StartCoroutine(Sammeln(druck ? "Speicher " + warum : ruhig ? "Kamera ruhig" : "spätestens"));
        }

        private IEnumerator Sammeln(string anlass)
        {
            _sammelt = true;
            float t0 = Time.realtimeSinceStartup, warten = Time.time - _sammelnFaelligSeit;
            long mem0 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            yield return Resources.UnloadUnusedAssets();
            _lastAssetUnload = Time.time;
            _sammelnFaelligSeit = -1f;
            _sammelt = false;
            Note($"Assets freigegeben ({anlass}, {warten:0} s nach dem Entladen): {(Time.realtimeSinceStartup - t0) * 1000f:0} ms, " +
                 $"Speicher {(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - mem0) / 1048576f:+0;-0} MB");
        }

        private bool Speicherdruck(out string warum)
        {
            warum = null;
            long sys = (long)SystemInfo.systemMemorySize * 1048576L, gfx = (long)SystemInfo.graphicsMemorySize * 1048576L;
            long belegt = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            long gfxBelegt = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
            if (sys > 0 && belegt > sys * sammelnBeiSpeicher) { warum = $"Arbeitsspeicher {belegt / 1048576} MB"; return true; }
            if (gfx > 0 && gfxBelegt > gfx * sammelnBeiSpeicher) { warum = $"Grafikspeicher {gfxBelegt / 1048576} MB"; return true; }
            return false;
        }

        /// <summary>Nachbarkacheln verbinden (LOD-Naehte schliessen) – nach jedem Laden/Entladen.</summary>
        private static void ConnectTerrains()
        {
            Terrain.SetConnectivityDirty();
        }

        private static void Note(string line)
        {
            Log.Add(line);
            if (Log.Count > 50) Log.RemoveAt(0);
            Debug.Log("[Ashveil] Weltzellen: " + line);
        }

        private string Summary()
        {
            var sb = new StringBuilder("<b>Welt</b> ");
            Vector3? me = null;
            foreach (NetworkMotion m in NetworkMotion.Active)
                if (m != null && m.IsOwner) me = m.transform.position;
            if (me.HasValue && manifest != null)
            {
                WorldCellDefinition cell = manifest.CellAt(me.Value, out WorldRegionDefinition region);
                sb.Append(region != null ? $"{region.displayName} · {cell.cellId}" : "ausserhalb (Instanz)");
            }
            sb.Append("\n");
            foreach (Cell c in _cells.Values)
            {
                if (c.Def.alwaysLoaded) continue;
                string st = c.State switch
                {
                    CellState.Ready => "<color=#8f8>bereit</color>",
                    CellState.Loading => "<color=#fd6>laedt</color>",
                    CellState.Loaded => "<color=#fd6>geladen</color>",
                    CellState.Unloading => "<color=#f96>entlaedt</color>",
                    _ => "<color=#999>aus</color>"
                };
                sb.Append($"{c.Def.cellId} {st}  ");
            }
            sb.Append($"\nSpeicher {System.GC.GetTotalMemory(false) / 1048576f:0} MB (verwaltet)");
            return sb.ToString();
        }
    }
}
