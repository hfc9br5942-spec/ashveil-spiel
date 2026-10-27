using System.Collections;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.World;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Server: baut beim Start das NavMesh (aus den Kollidern der Szene), erzeugt die Gegner aller
    /// <see cref="EnemySpawnArea"/>s und laesst Gefallene nach Leichen- und Wiederkehrzeit neu erscheinen.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class EnemyDirector : MonoBehaviour
    {
        [Tooltip("So lange (s) bleibt eine Leiche zusaetzlich liegen, solange noch Beute darin ist")]
        [SerializeField] private float lootCorpseTime = 150f;

        [Header("Fairness und Dichte (CORE-008, 02.10.2026)")]
        [Tooltip("Gegner erscheinen nicht naeher als so viele Meter an einem lebenden Spieler")]
        [SerializeField] private float spawnClearance = 12f;
        [Tooltip("Wartet eine Wiederkehr laenger als so viele Sekunden auf einen freien Punkt, reicht der weiteste Punkt")]
        [SerializeField] private float maxClearanceWait = 30f;
        [Tooltip("...aber nie naeher als so viele Meter")]
        [SerializeField] private float minClearance = 5f;
        [Tooltip("Spieler bis Gebietsradius + so viele Meter zaehlen als Jaeger dieses Gebiets")]
        [SerializeField] private float hunterRange = 60f;
        [Tooltip("Zusaetzliche Gegner je weiterem Jaeger, als Anteil der Grundanzahl")]
        [SerializeField] private float extraPerHunter = 0.5f;
        [Tooltip("Hoechstens so viel mal die Grundanzahl")]
        [SerializeField] private float maxDensityFactor = 3f;   // 11.10.2026: hunderte Spieler wie WoW (vorher 2)
        [Tooltip("Wiederkehr wird je weiterem Jaeger um diesen Anteil kuerzer ...")]
        [SerializeField] private float respawnSpeedupPerHunter = 0.35f;   // 11.10.2026: dynamische Wiederkehr wie Classic (vorher 0,25, linear)
        [Tooltip("... aber nie kuerzer als dieser Anteil der normalen Zeit")]
        [SerializeField] private float minRespawnFactor = 0.12f;   // 11.10.2026: bei vielen Jaegern bis ≈ 1/8 der Zeit (vorher 0,5)
        [Tooltip("Zusaetzliche Gegner verschwinden, wenn so lange niemand mehr im Gebiet jagt (nur ruhige, unverletzte)")]
        [SerializeField] private float idleExtraTimeout = 90f;

        // ---- Aktivierungsraster wie MMO-Server (Classic „Grids“, 11.10.2026): Gegner gibt es als Figur nur nahe bei Spielern
        [Tooltip("Ein Gebiet wird geweckt, wenn ein Spieler naeher als so viele Meter ist (groesser als die Gegner-Sicht 180 m)")]
        [SerializeField] private float activateRange = 260f;
        [Tooltip("Ein waches Gebiet schlaeft ein, wenn kein Spieler naeher als so viele Meter ist ...")]
        [SerializeField] private float deactivateRange = 340f;
        [Tooltip("... und das so viele Sekunden lang")]
        [SerializeField] private float dormantDelay = 45f;

        /// <summary>Test/Diagnose: Aktivierungsraster ein/aus (aus = alle Gebiete immer wach, wie vor dem 11.10.2026).</summary>
        public static bool ActivationEnabled = true;
        private readonly System.Collections.Generic.HashSet<EnemySpawnArea> _active = new System.Collections.Generic.HashSet<EnemySpawnArea>();
        private readonly System.Collections.Generic.Dictionary<EnemySpawnArea, float> _lastNear = new System.Collections.Generic.Dictionary<EnemySpawnArea, float>();
        /// <summary>Diagnose: Gebiete wach / gesamt, lebende Gegner-Figuren.</summary>
        public int ActiveAreas => _active.Count;
        /// <summary>Weckabstand (m) – Tests rechnen damit aus, welche Gebiete wach sein müssen.</summary>
        public float ActivateRange => activateRange;
        public int ManagedAreas => _booted.Count;

        /// <summary>Test: Dichte nach Spielerzahl ein/aus.</summary>
        public static bool DensityEnabled = true;
        /// <summary>Test: Wartezeit bis zum Abbau ueberzaehliger Gegner ueberschreiben (&lt; 0 = Einstellung).</summary>
        public static float TestIdleExtraTimeout = -1f;

        private NetworkManager _manager;
        private bool _running;

        /// <summary>Server laeuft und Gegner sind erschienen (Dungeon-Instanzen warten darauf).</summary>
        public static EnemyDirector Instance { get; private set; }
        public bool Ready { get; private set; }
        private sealed class PendingRespawn
        {
            public EnemySpawnArea Area;
            public float Due;
        }

        private static readonly System.Collections.Generic.List<PendingRespawn> Pending = new System.Collections.Generic.List<PendingRespawn>();

        /// <summary>Test: Wiederkehr aller wartenden Gegner eines Gebiets sofort ausloesen (seltene Gegner).</summary>
        public static int HurryRespawns(EnemySpawnArea area)
        {
            int n = 0;
            foreach (PendingRespawn p in Pending)
                if (area == null || p.Area == area) { p.Due = 0f; n++; }
            return n;
        }

        /// <summary>Test: Sekunden bis zur naechsten Wiederkehr in diesem Gebiet (-1 = keine geplant).</summary>
        public static float SecondsUntilRespawn(EnemySpawnArea area)
        {
            float best = -1f;
            foreach (PendingRespawn p in Pending)
                if (p.Area == area) best = best < 0f ? Mathf.Max(0f, p.Due - Time.time) : Mathf.Min(best, Mathf.Max(0f, p.Due - Time.time));
            return best;
        }

        private void Awake() => Instance = this;

        private void Start()
        {
            _manager = GetComponentInParent<NetworkManager>();
            if (_manager == null) _manager = InstanceFinder.NetworkManager;
            if (_manager != null) _manager.ServerManager.OnServerConnectionState += OnServerState;
        }

        private void OnDestroy()
        {
            if (_manager != null && _manager.ServerManager != null)
                _manager.ServerManager.OnServerConnectionState -= OnServerState;
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started && !_running)
            {
                _running = true;
                EnemyEncounter.Summoner = SpawnOnce;
                EnemyEncounter.Remover = Remove;
                EnemyEncounter.OffeneWeltAnsage += OnOffeneWeltAnsage;
                Ashveil.Core.WorldCells.CellReady += OnCellReady;
                Ashveil.Core.WorldCells.CellUnloading += OnCellUnloading;
                StartCoroutine(Boot());
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped && _running)
            {
                _running = false;
                Ready = false;
                EnemyEncounter.Summoner = null;
                EnemyEncounter.Remover = null;
                EnemyEncounter.OffeneWeltAnsage -= OnOffeneWeltAnsage;
                Ashveil.Core.WorldCells.CellReady -= OnCellReady;
                Ashveil.Core.WorldCells.CellUnloading -= OnCellUnloading;
                Pending.Clear();
                _booted.Clear();
                _owner.Clear();
                _byArea.Clear();
                _lastHunted.Clear();
                _active.Clear();
                _lastNear.Clear();
                _wakeQueue.Clear();
                _wakeQueued.Clear();
                _wakeRunning = false;
                StopAllCoroutines();
            }
        }

        private IEnumerator Boot()
        {
            yield return null; // Szenenobjekte zuerst fertig werden lassen
            float t0 = Time.realtimeSinceStartup;
            int surfaces = 0;
            int baked = 0;
            foreach (NavMeshSurface s in FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
            {
                // Im Editor vorgebacken (Kareth ist 1,2 km gross) – nur fehlende Flaechen zur Laufzeit bauen
                if (s.navMeshData != null) { baked++; continue; }
                s.BuildNavMesh();
                surfaces++;
            }
            Debug.Log($"[Ashveil] Server: NavMesh bereit ({baked} vorgebacken, {surfaces} gebaut, {(Time.realtimeSinceStartup - t0) * 1000f:0} ms).");

            int total = SpawnAreas(null);
            int planned = 0;
            foreach (EnemySpawnArea a in _booted) if (a != null) planned += a.Count;
            Debug.Log(ActivationEnabled
                ? $"[Ashveil] Server: {planned} Gegner in {_booted.Count} Gebieten geplant – Aktivierungsraster: nur nahe bei Spielern als Figur ({total} sofort)."
                : $"[Ashveil] Server: {total} Gegner erschienen.");
            Ready = true;
            StartCoroutine(DensityLoop());
            StartCoroutine(ActivationLoop());
        }

        // ================================================================== Weltzellen (Large-World E1)

        // Gebiete, deren Gegner schon erzeugt sind, und wem ein Gegner gehoert (zum Entfernen beim Entladen einer Zelle)
        private readonly System.Collections.Generic.HashSet<EnemySpawnArea> _booted = new System.Collections.Generic.HashSet<EnemySpawnArea>();
        private readonly System.Collections.Generic.Dictionary<EnemyBrain, EnemySpawnArea> _owner = new System.Collections.Generic.Dictionary<EnemyBrain, EnemySpawnArea>();
        // Gegner (lebend oder Leiche) je Gebiet, fuer Dichte und Abbau
        private readonly System.Collections.Generic.Dictionary<EnemySpawnArea, System.Collections.Generic.List<EnemyBrain>> _byArea =
            new System.Collections.Generic.Dictionary<EnemySpawnArea, System.Collections.Generic.List<EnemyBrain>>();
        private readonly System.Collections.Generic.Dictionary<EnemySpawnArea, float> _lastHunted = new System.Collections.Generic.Dictionary<EnemySpawnArea, float>();

        private void Own(EnemyBrain b, EnemySpawnArea area)
        {
            _owner[b] = area;
            if (!_byArea.TryGetValue(area, out var list)) _byArea[area] = list = new System.Collections.Generic.List<EnemyBrain>();
            list.Add(b);
        }

        private void Disown(EnemyBrain b)
        {
            if (b == null) return;
            if (_owner.TryGetValue(b, out EnemySpawnArea area) && area != null && _byArea.TryGetValue(area, out var list)) list.Remove(b);
            _owner.Remove(b);
        }

        /// <summary>Gegner aller Gebiete (sceneName = null) oder nur der Gebiete einer Zellen-Szene erzeugen.</summary>
        private int SpawnAreas(string sceneName)
        {
            int total = 0;
            foreach (EnemySpawnArea area in FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None))
            {
                if (area.Prefab == null || area.Definition == null) continue;
                if (sceneName != null && area.gameObject.scene.name != sceneName) continue;
                if (area.GetComponentInParent<DungeonSlot>() != null) continue; // Instanzen erzeugen ihre Gegner selbst
                if (area.SummonOnly) continue; // ruft ein Boss selbst
                if (!_booted.Add(area)) continue;
                if (ActivationEnabled && Managed(area)) continue; // erst wenn ein Spieler naht (ActivationLoop)
                _active.Add(area);
                for (int i = 0; i < area.Count; i++)
                    if (Spawn(area)) total++;
            }
            return total;
        }

        /// <summary>Zelle bereit (NavMesh da): ihre Gegner erzeugen. Vor dem ersten Start erledigt das Boot().</summary>
        private void OnCellReady(string cellId)
        {
            if (!_running || !Ready || WorldStreamer.Instance == null) return;
            string scene = WorldStreamer.Instance.SceneOf(cellId);
            if (string.IsNullOrEmpty(scene)) return;
            int n = SpawnAreas(scene);
            if (n > 0) Debug.Log($"[Ashveil] Server: Zelle {cellId} bereit – {n} Gegner erschienen.");
        }

        /// <summary>Zelle wird entladen: ihre Gegner (lebend oder Leiche) entfernen, Wiederkehr endet mit dem Gebiet.</summary>
        private void OnCellUnloading(string cellId)
        {
            if (!_running || WorldStreamer.Instance == null) return;
            string scene = WorldStreamer.Instance.SceneOf(cellId);
            if (string.IsNullOrEmpty(scene)) return;
            var gone = new System.Collections.Generic.List<EnemyBrain>();
            foreach (var kv in _owner)
                if (kv.Value == null || kv.Value.gameObject.scene.name == scene) gone.Add(kv.Key);
            // 22.10. WELTZELLEN-02: Gerufene (SpawnOnce – Alarmglocke im Kreidekeller, Helfer offener Begegnungen) gehören keinem
            // Gebiet und stehen nicht in _owner. Starb der Spieler gegen die Verstärkung, wurde die Zelle entladen, bevor die Glocke sie
            // nach 30 s wegschicken konnte – sie blieben dauerhaft (ohne Wegnetz) in der Welt und wurden mit jeder Runde mehr. Jetzt gehen
            // sie mit ihrer Zelle. Erst sammeln, dann entfernen (EnemyBrain.All ändert sich beim Despawn). Instanz- und Kolosseumgegner
            // liegen außerhalb aller Zellen (CellAt = null) und bleiben unberührt.
            foreach (EnemyBrain b in EnemyBrain.All)
                if (b != null && !_owner.ContainsKey(b) && WorldStreamer.Instance.CellAt(b.transform.position) == cellId) gone.Add(b);
            foreach (EnemyBrain b in gone)
            {
                Disown(b);
                Remove(b);
            }
            foreach (var key in new System.Collections.Generic.List<EnemySpawnArea>(_byArea.Keys))
                if (key == null || key.gameObject.scene.name == scene) { _byArea.Remove(key); _lastHunted.Remove(key); }
            _booted.RemoveWhere(a => a == null || a.gameObject.scene.name == scene);
            _active.RemoveWhere(a => a == null || a.gameObject.scene.name == scene);
            Pending.RemoveAll(p => p.Area == null || p.Area.gameObject.scene.name == scene);
            if (gone.Count > 0) Debug.Log($"[Ashveil] Server: Zelle {cellId} entladen – {gone.Count} Gegner entfernt.");
        }

        private bool Spawn(EnemySpawnArea area, SpawnMode mode = SpawnMode.Initial)
        {
            EnemyBrain brain = SpawnBrain(area, mode);
            if (brain == null) return false;
            Own(brain, area);
            brain.Killed += b => StartCoroutine(RespawnLater(area, b));
            return true;
        }

        /// <summary>
        /// Dungeon-Instanz: einen Gegner des Gebiets erzeugen, OHNE Wiederkehr. Die Leiche verschwindet nach Leichen-
        /// und Beutezeit; das Zuruecksetzen der Instanz raeumt den Rest ab. Gibt den Gegner zurueck (oder null).
        /// </summary>
        /// <summary>Kreidekeller (12.10.2026): Ansagen offener Begegnungen (Kellermeister, Alarmglocke) an Spieler im Umkreis von 60 m.</summary>
        private static void OnOffeneWeltAnsage(Vector3 where, string text)
        {
            foreach (NetworkInventory inv in NetworkInventory.Online.Values)
                if (inv != null && inv.IsReady && (inv.transform.position - where).sqrMagnitude <= 60f * 60f)
                    NetworkSocial.ServerSystemMessage(inv.ProfileId, text);
        }

        public EnemyBrain SpawnOnce(EnemySpawnArea area)
        {
            if (!_running || area == null || area.Prefab == null || area.Definition == null) return null;
            EnemyBrain brain = SpawnBrain(area);
            if (brain != null) brain.Killed += b => StartCoroutine(RemoveCorpseLater(area, b));
            return brain;
        }

        /// <summary>
        /// Kolosseum: einen Gegner an einem Punkt erzeugen, ohne Gebiet und ohne Wiederkehr. <paramref name="prepare"/>
        /// laeuft nach Configure und vor dem Spawn (z. B. Arena-Modus). Die Leiche verschwindet nach
        /// <paramref name="corpseTime"/> Sekunden (laenger, solange Beute darin liegt).
        /// </summary>
        public EnemyBrain SpawnAt(GameObject prefab, Vector3 pos, float yaw, int level, string group, System.Action<EnemyBrain> prepare, float corpseTime = 8f)
        {
            if (!_running || prefab == null) return null;
            if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas)) return null;
            GameObject go = Instantiate(prefab, hit.position, Quaternion.Euler(0f, yaw, 0f));
            var brain = go.GetComponent<EnemyBrain>();
            if (brain == null)
            {
                Destroy(go);
                return null;
            }
            brain.Configure(level, hit.position, group);
            prepare?.Invoke(brain);
            EnemyInterestCondition.Ensure(go);
            _manager.ServerManager.Spawn(go);
            brain.Killed += b => StartCoroutine(RemoveAfter(b, corpseTime));
            return brain;
        }

        private IEnumerator RemoveAfter(EnemyBrain dead, float corpse)
        {
            yield return new WaitForSeconds(corpse);
            float extra = 0f;
            CorpseLoot loot = dead != null ? dead.GetComponent<CorpseLoot>() : null;
            while (loot != null && loot.HasAnyLoot && extra < lootCorpseTime)
            {
                extra += 1f;
                yield return new WaitForSeconds(1f);
            }
            Remove(dead);
        }

        /// <summary>Server: Gegner (lebend oder Leiche) sofort entfernen – z. B. beim Zuruecksetzen einer Instanz.</summary>
        public void Remove(EnemyBrain brain)
        {
            if (brain == null || _manager == null) return;
            var nob = brain.GetComponent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned) _manager.ServerManager.Despawn(brain.gameObject);
        }

        /// <summary>
        /// Dominiert (Klassen-Identitaet §59): das wilde Wesen verlaesst die Welt, ohne zu sterben (keine Beute, keine
        /// Erfahrung). Sein Gebiet belebt es nach der ueblichen Zeit wieder – sonst wuerden Rudel ausgedunnt.
        /// </summary>
        public void ServerRemoveTamed(EnemyBrain brain)
        {
            if (brain == null) return;
            _owner.TryGetValue(brain, out EnemySpawnArea area);
            Disown(brain);
            Remove(brain);
            if (area != null && _running) StartCoroutine(RespawnTamed(area));
        }

        private IEnumerator RespawnTamed(EnemySpawnArea area)
        {
            var entry = new PendingRespawn { Area = area, Due = Time.time + Mathf.Max(1f, area.Definition.RollRespawnTime()) };
            Pending.Add(entry);
            while (_running && area != null && Time.time < entry.Due)
                yield return new WaitForSeconds(0.5f);
            Pending.Remove(entry);
            if (_running && area != null) yield return SpawnWhenFair(area);
        }

        private IEnumerator RemoveCorpseLater(EnemySpawnArea area, EnemyBrain dead)
        {
            float corpse = area != null && area.Definition != null ? area.Definition.corpseTime : 25f;
            yield return new WaitForSeconds(corpse);
            float extra = 0f;
            CorpseLoot loot = dead != null ? dead.GetComponent<CorpseLoot>() : null;
            while (loot != null && loot.HasAnyLoot && extra < lootCorpseTime)
            {
                extra += 1f;
                yield return new WaitForSeconds(1f);
            }
            Remove(dead);
        }

        private enum SpawnMode : byte
        {
            /// <summary>Start, Zelle geladen, Instanz: kein Spielerabstand noetig.</summary>
            Initial,
            /// <summary>Wiederkehr/Dichte: nur Punkte ab <see cref="spawnClearance"/> zum naechsten Spieler.</summary>
            Strict,
            /// <summary>Wiederkehr wartet schon lange: weitester Punkt, aber mindestens <see cref="minClearance"/>.</summary>
            Relaxed
        }

        /// <summary>Waagerechter Abstand zum naechsten lebenden Spieler (unendlich ohne Spieler).</summary>
        private static float NearestPlayer(Vector3 p)
        {
            float best = float.MaxValue;
            var players = NetworkPlayer.ServerPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                NetworkPlayer pl = players[i];
                if (pl == null || !pl.ServerAlive) continue;
                Vector3 d = pl.transform.position - p;
                d.y = 0f;
                float m = d.sqrMagnitude;
                if (m < best) best = m;
            }
            return best == float.MaxValue ? best : Mathf.Sqrt(best);
        }

        private bool TryPickPoint(EnemySpawnArea area, SpawnMode mode, out Vector3 pos)
        {
            float bestDist = -1f;
            pos = default;
            int tries = mode == SpawnMode.Initial ? 1 : 10;
            for (int t = 0; t < tries; t++)
            {
                Vector3 center = area.PickCenter();
                Vector2 r = Random.insideUnitCircle * area.Radius;
                Vector3 p = center + new Vector3(r.x, 0f, r.y);
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 4f, NavMesh.AllAreas)) p = hit.position;
                else if (NavMesh.SamplePosition(center, out hit, 8f, NavMesh.AllAreas)) p = hit.position;
                else continue;
                if (mode == SpawnMode.Initial) { pos = p; return true; }
                float d = NearestPlayer(p);
                if (d >= spawnClearance) { pos = p; return true; }
                if (d > bestDist) { bestDist = d; pos = p; }
            }
            return mode == SpawnMode.Relaxed && bestDist >= minClearance;
        }

        /// <summary>Wiederkehr: warten, bis ein fairer Punkt frei ist (kein Erscheinen direkt am Spieler).</summary>
        private IEnumerator SpawnWhenFair(EnemySpawnArea area)
        {
            float waited = 0f;
            while (_running && area != null)
            {
                SpawnMode mode = waited >= maxClearanceWait ? SpawnMode.Relaxed : SpawnMode.Strict;
                if (Spawn(area, mode)) yield break;
                if (!_booted.Contains(area)) yield break; // Zelle entladen
                if (ActivationEnabled && Managed(area) && !_active.Contains(area)) yield break; // Gebiet schlaeft wieder
                waited += 2f;
                yield return new WaitForSeconds(2f);
            }
        }

        private EnemyBrain SpawnBrain(EnemySpawnArea area, SpawnMode mode = SpawnMode.Initial)
        {
            if (!TryPickPoint(area, mode, out Vector3 pos))
            {
                if (mode == SpawnMode.Initial)
                    Debug.LogWarning($"[Ashveil] Server: Kein NavMesh bei '{area.name}' – Gegner wird nicht erzeugt.", area);
                return null;
            }

            long alloc0 = System.GC.GetAllocatedBytesForCurrentThread(); // CORE-005: Speicher je Erscheinen (Lasttest 02.10.)
            GameObject go = Instantiate(area.Prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var brain = go.GetComponent<EnemyBrain>();
            if (brain == null)
            {
                Destroy(go);
                return null;
            }
            int level = Random.Range(area.MinLevel, area.MaxLevel + 1);
            brain.Configure(level, pos, area.Group);
            brain.SetAggroProfile(area.Aggro);
            if (area.Route != null) brain.SetRoute(area.Route);
            EnemyInterestCondition.Ensure(go); // Sichtbarkeit je Instanz-Platz (N3)
            _manager.ServerManager.Spawn(go);
            long allocMb = (System.GC.GetAllocatedBytesForCurrentThread() - alloc0) / (1024 * 1024);
            if (allocMb >= 2 && ServerVisualStripper.Active) Debug.Log($"[Ashveil] Server: '{area.Prefab.name}' erzeugt – {allocMb} MB Speicher in einem Schritt (CORE-005 beobachten).");
            return brain;
        }

        private IEnumerator RespawnLater(EnemySpawnArea area, EnemyBrain dead)
        {
            float corpse = area.Definition.corpseTime;
            yield return new WaitForSeconds(corpse);
            // Nicht aufgenommene Beute: Leiche bleibt laenger liegen (hoechstens lootCorpseTime)
            float extra = 0f;
            CorpseLoot loot = dead != null ? dead.GetComponent<CorpseLoot>() : null;
            while (loot != null && loot.HasAnyLoot && extra < lootCorpseTime)
            {
                extra += 1f;
                yield return new WaitForSeconds(1f);
            }
            corpse += extra;
            if (dead != null) Disown(dead);
            if (dead != null && dead.GetComponent<FishNet.Object.NetworkObject>() is { IsSpawned: true }) _manager.ServerManager.Despawn(dead.gameObject);
            if (area == null) yield break; // Zelle entladen
            // Mehrere Jaeger im Gebiet: kuerzere Wiederkehr (nie unter minRespawnFactor)
            float wait = Mathf.Max(1f, area.Definition.RollRespawnTime() * area.RespawnMultiplier * RespawnFactor(area) - corpse);
            var entry = new PendingRespawn { Area = area, Due = Time.time + wait };
            Pending.Add(entry);
            while (_running && area != null && Time.time < entry.Due)
                yield return new WaitForSeconds(0.5f);
            Pending.Remove(entry);
            if (!_running || area == null) yield break;
            // Gebiet schlaeft (kein Spieler nahe): nicht erzeugen – beim Wecken fuellt es sich auf (Wiederkehrzeit ist dann abgelaufen)
            if (!_active.Contains(area)) yield break;
            // Dichte gesunken (Zusatzplaetze nicht mehr gebraucht): nicht wiederkehren
            if (Current(area) >= TargetCount(area)) yield break;
            yield return SpawnWhenFair(area);
        }

        // ================================================================== Dichte nach Spielerzahl (CORE-008)

        private static bool Scales(EnemySpawnArea area) =>
            area != null && area.Definition != null && area.Route == null && area.HideoutCount == 1 && !area.Definition.elite && !area.SummonOnly;

        /// <summary>
        /// Zusatzgegner und kuerzere Wiederkehr nur, wo sie keine ungewollten Mehrfachkaempfe erzeugen (CORE-008):
        /// friedliche/defensive Gebiete immer, angreifende Lager erst ab 10 m Radius. Enge Lager (z. B. Gehoeft der
        /// Verblichenen) bleiben unveraendert – dort wuerde schnelleres Nachruecken Gruppen ueberrollen (Gruppentest 02.10.).
        /// </summary>
        private static bool AddsExtras(EnemySpawnArea area) =>
            Scales(area) && (area.Aggro == Ashveil.Data.AggroProfile.Defensive || area.Aggro == Ashveil.Data.AggroProfile.Passive || area.Radius >= 10f);

        /// <summary>Lebende Spieler, die in diesem Gebiet jagen (Gebietsradius + hunterRange).</summary>
        public int Hunters(EnemySpawnArea area)
        {
            if (area == null) return 0;
            float r = area.Radius + hunterRange;
            float r2 = r * r;
            Vector3 c = area.transform.position;
            int n = 0;
            var players = NetworkPlayer.ServerPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                NetworkPlayer pl = players[i];
                if (pl == null || !pl.ServerAlive) continue;
                Vector3 d = pl.transform.position - c;
                d.y = 0f;
                if (d.sqrMagnitude <= r2) n++;
            }
            return n;
        }

        /// <summary>Soll-Anzahl: Grundanzahl, je weiterem Jaeger mehr, hoechstens maxDensityFactor × Grundanzahl.</summary>
        public int TargetCount(EnemySpawnArea area)
        {
            if (area == null) return 0;
            int baseCount = area.Count;
            if (!DensityEnabled || !AddsExtras(area)) return baseCount;
            int h = Hunters(area);
            if (h <= 1) return baseCount;
            int extra = Mathf.CeilToInt(baseCount * extraPerHunter * (h - 1));
            return Mathf.Min(baseCount + extra, Mathf.CeilToInt(baseCount * maxDensityFactor));
        }

        private float RespawnFactor(EnemySpawnArea area)
        {
            // Nur offene/friedliche Gebiete (wie Zusatzgegner): enge Lager sollen fuer Gruppen nicht schneller nachruecken
            if (!DensityEnabled || !AddsExtras(area)) return 1f;
            int h = Hunters(area);
            // Classic-artig: je mehr Jaeger, desto schneller – hyperbolisch statt linear (2 Jaeger ≈ 0,74, 5 ≈ 0,42, 10 ≈ 0,24, 25+ = Untergrenze)
            return h <= 1 ? 1f : Mathf.Max(minRespawnFactor, 1f / (1f + respawnSpeedupPerHunter * (h - 1)));
        }

        /// <summary>Gegner des Gebiets in der Welt (auch Leichen) plus geplante Wiederkehr.</summary>
        public int Current(EnemySpawnArea area)
        {
            int n = _byArea.TryGetValue(area, out var list) ? list.Count : 0;
            foreach (PendingRespawn p in Pending)
                if (p.Area == area) n++;
            return n;
        }

        /// <summary>Test: lebende Gegner des Gebiets.</summary>
        public System.Collections.Generic.List<EnemyBrain> AliveOf(EnemySpawnArea area)
        {
            var result = new System.Collections.Generic.List<EnemyBrain>();
            if (area != null && _byArea.TryGetValue(area, out var list))
                foreach (EnemyBrain b in list)
                    if (b != null && b.GetComponent<Ashveil.Gameplay.Combat.Health>() is { IsAlive: true }) result.Add(b);
            return result;
        }

        /// <summary>
        /// Wird vom Aktivierungsraster verwaltet: offene Gebiete der Oberwelt. Immer wach bleiben Wanderrouten (Glockenhirte) und
        /// seltene Gegner mit mehreren Verstecken – ihre Zeiten und Wege sollen nicht vom Spielerabstand abhaengen.
        /// </summary>
        private static bool Managed(EnemySpawnArea area) =>
            area != null && area.Definition != null && area.Route == null && area.HideoutCount == 1 && !area.SummonOnly;

        // Raster der Spielerpositionen (128 m), je Runde neu – damit bleibt die Pruefung auch bei hunderten Spielern billig
        private const float PlayerCell = 128f;
        private readonly System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Vector3>> _playerGrid =
            new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<Vector3>>();
        private readonly System.Collections.Generic.Stack<System.Collections.Generic.List<Vector3>> _gridPool =
            new System.Collections.Generic.Stack<System.Collections.Generic.List<Vector3>>();

        private static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        private void BuildPlayerGrid()
        {
            foreach (var l in _playerGrid.Values) { l.Clear(); _gridPool.Push(l); }
            _playerGrid.Clear();
            var players = NetworkPlayer.ServerPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                NetworkPlayer pl = players[i];
                if (pl == null) continue; // auch Gefallene: sie kommen als Geist zurueck
                Vector3 p = pl.transform.position;
                long k = CellKey(Mathf.FloorToInt(p.x / PlayerCell), Mathf.FloorToInt(p.z / PlayerCell));
                if (!_playerGrid.TryGetValue(k, out var list)) _playerGrid[k] = list = _gridPool.Count > 0 ? _gridPool.Pop() : new System.Collections.Generic.List<Vector3>(4);
                list.Add(p);
            }
        }

        /// <summary>Ist ein Spieler naeher als <paramref name="range"/>? (nur die Rasterzellen im Umkreis)</summary>
        private bool PlayerWithin(Vector3 c, float range)
        {
            float r2 = range * range;
            int n = Mathf.CeilToInt(range / PlayerCell);
            int cx = Mathf.FloorToInt(c.x / PlayerCell), cz = Mathf.FloorToInt(c.z / PlayerCell);
            for (int dx = -n; dx <= n; dx++)
                for (int dz = -n; dz <= n; dz++)
                {
                    if (!_playerGrid.TryGetValue(CellKey(cx + dx, cz + dz), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        Vector3 d = list[i] - c;
                        d.y = 0f;
                        if (d.sqrMagnitude <= r2) return true;
                    }
                }
            return false;
        }

        /// <summary>
        /// Aktivierungsraster (MMO-Server, Classic „Grids“): zweimal je Sekunde. Naht ein Spieler, wird das Gebiet geweckt und auf
        /// seine Grundanzahl aufgefuellt (abzueglich noch laufender Wiederkehrzeiten) – ausserhalb der Gegner-Sicht. Ist lange kein
        /// Spieler nahe, verschwinden ruhige, unverletzte Gegner; Leichen laufen ihre Zeit ab. So rechnet der Server nur dort, wo
        /// gespielt wird, egal wie gross die Welt ist.
        /// </summary>
        private IEnumerator ActivationLoop()
        {
            var wait = new WaitForSeconds(0.5f);
            var areas = new System.Collections.Generic.List<EnemySpawnArea>();
            while (_running)
            {
                yield return wait;
                areas.Clear();
                foreach (EnemySpawnArea a in _booted)
                    if (a != null && Managed(a)) areas.Add(a);
                if (!ActivationEnabled)
                {
                    foreach (EnemySpawnArea a in areas)
                        if (_active.Add(a)) Wake(a);
                    continue;
                }
                BuildPlayerGrid();
                float now = Time.time;
                foreach (EnemySpawnArea area in areas)
                {
                    Vector3 c = area.transform.position;
                    bool awake = _active.Contains(area);
                    if (!awake)
                    {
                        if (!PlayerWithin(c, activateRange + area.Radius)) continue;
                        _active.Add(area);
                        _lastNear[area] = now;
                        Wake(area);
                        continue;
                    }
                    if (PlayerWithin(c, deactivateRange + area.Radius)) { _lastNear[area] = now; continue; }
                    if (_lastNear.TryGetValue(area, out float last) && now - last < dormantDelay) continue;
                    if (Sleep(area)) { _active.Remove(area); _lastNear.Remove(area); }
                }
            }
        }

        /// <summary>Gebiet wecken: in die Warteschlange (verteilt auf mehrere Bilder, damit Wecken keine Bildspitze macht).</summary>
        private void Wake(EnemySpawnArea area)
        {
            if (!_wakeQueued.Add(area)) return;
            _wakeQueue.Enqueue(area);
            if (!_wakeRunning) StartCoroutine(WakeWorker());
        }

        [Tooltip("Zeitbudget je Bild fuers Wecken (ms; mindestens ein Gegner je Bild). Ruckler-Messung r2.14: ein Gegner erzeugen ≈ 6–7 ms")]
        [SerializeField] private float wakeBudgetMs = 3f;
        private readonly System.Collections.Generic.Queue<EnemySpawnArea> _wakeQueue = new System.Collections.Generic.Queue<EnemySpawnArea>();
        private readonly System.Collections.Generic.HashSet<EnemySpawnArea> _wakeQueued = new System.Collections.Generic.HashSet<EnemySpawnArea>();
        private bool _wakeRunning;

        private IEnumerator WakeWorker()
        {
            _wakeRunning = true;
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            int spawnedThisFrame = 0;
            while (_running && _wakeQueue.Count > 0)
            {
                EnemySpawnArea area = _wakeQueue.Peek();
                if (area == null || !_active.Contains(area)) { _wakeQueue.Dequeue(); _wakeQueued.Remove(area); continue; }
                if (area.Count - Current(area) <= 0) { _wakeQueue.Dequeue(); _wakeQueued.Remove(area); continue; }
                if (spawnedThisFrame > 0 && watch.Elapsed.TotalMilliseconds >= wakeBudgetMs)
                {
                    yield return null;
                    watch.Restart();
                    spawnedThisFrame = 0;
                    continue;
                }
                spawnedThisFrame++;
                if (!Spawn(area, SpawnMode.Strict))
                {
                    // Spieler steht mitten im Gebiet (Anmelden, Wiederbeleben): Rest wartet auf einen fairen Punkt
                    int rest = area.Count - Current(area);
                    for (int i = 0; i < rest; i++) StartCoroutine(SpawnWhenFair(area));
                    _wakeQueue.Dequeue();
                    _wakeQueued.Remove(area);
                }
            }
            _wakeRunning = false;
        }

        /// <summary>Gebiet schlafen legen: ruhige, unverletzte Gegner entfernen. false, solange einer kaempft oder verletzt ist.</summary>
        private bool Sleep(EnemySpawnArea area)
        {
            if (!_byArea.TryGetValue(area, out var list) || list.Count == 0) return true;
            for (int i = 0; i < list.Count; i++)
            {
                EnemyBrain b = list[i];
                if (b == null) continue;
                var hp = b.GetComponent<Ashveil.Gameplay.Combat.Health>();
                if (hp != null && hp.IsAlive && (b.InCombat || hp.Current < hp.Max)) return false;
            }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                EnemyBrain b = list[i];
                var hp = b != null ? b.GetComponent<Ashveil.Gameplay.Combat.Health>() : null;
                if (b == null || hp == null || !hp.IsAlive) continue; // Leichen laufen ihre Zeit ab (Beute)
                Disown(b);
                Remove(b);
            }
            return true;
        }

        private IEnumerator DensityLoop()
        {
            var wait = new WaitForSeconds(3f);
            var areas = new System.Collections.Generic.List<EnemySpawnArea>();
            while (_running)
            {
                yield return wait;
                if (!DensityEnabled) continue;
                areas.Clear();
                foreach (EnemySpawnArea a in _booted)
                    if (AddsExtras(a) && _active.Contains(a)) areas.Add(a);
                foreach (EnemySpawnArea area in areas)
                {
                    if (area == null) continue;
                    int hunters = Hunters(area);
                    if (hunters > 0) _lastHunted[area] = Time.time;
                    int target = TargetCount(area);
                    int current = Current(area);
                    if (current < target)
                    {
                        Spawn(area, SpawnMode.Strict); // je Runde hoechstens einer pro Gebiet, nie direkt am Spieler
                        continue;
                    }
                    // Niemand jagt mehr: ueberzaehlige, ruhige und unverletzte Gegner leise abbauen
                    float timeout = TestIdleExtraTimeout >= 0f ? TestIdleExtraTimeout : idleExtraTimeout;
                    if (hunters == 0 && current > area.Count && _byArea.TryGetValue(area, out var list) &&
                        (!_lastHunted.TryGetValue(area, out float last) || Time.time - last >= timeout))
                    {
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            EnemyBrain b = list[i];
                            var hp = b != null ? b.GetComponent<Ashveil.Gameplay.Combat.Health>() : null;
                            if (b == null || hp == null || !hp.IsAlive || hp.Current < hp.Max || b.InCombat) continue;
                            Disown(b);
                            Remove(b);
                            break; // einer pro Runde
                        }
                    }
                }
            }
        }
    }
}
