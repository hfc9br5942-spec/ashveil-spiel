using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ashveil.Core.Diagnostics;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using FishNet;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ashveil.Networking
{
    /// <summary>
    /// CORE-005 / NET-012 (02.10.2026): misst den Server waehrend eines Lasttests. Nur aktiv mit
    /// "-ashveil-loadtest &lt;datei&gt;" (z. B. Server-Build mit -batchmode -nographics -ashveil-server).
    /// Alle 5 s eine Zeile: Verbindungen, Spielerfiguren, Gegner, Bildzeit (Mittel, 99 %, laengstes Bild), GC, Speicher.
    /// Nach "-ashveil-loadtest-seconds N" Zusammenfassung schreiben und beenden.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class LoadTestServerMonitor : MonoBehaviour
    {
        private string _file;
        private float _duration;
        private float _start, _windowStart;
        private readonly List<float> _frames = new List<float>(512);
        private readonly List<float> _all = new List<float>(65536);
        private int _gc0, _maxClients, _maxPlayers;
        private bool _warm; // erst ab der ersten Verbindung in die Zusammenfassung (Start/Laden zaehlt getrennt)
        private float _startupMax;
        private readonly StringBuilder _csv = new StringBuilder();
        private bool _done;
        // Arbeitszeit je Bild (ohne Wartezeit) und Rechenzeit des Prozesses: trennt echte Serverlast von Rechnerkonkurrenz
        private float _frameEnd;
        private readonly List<float> _work = new List<float>(512);
        private readonly List<float> _workAll = new List<float>(65536);
        private System.TimeSpan _cpu0;
        private float _cpuWall0;
        private string _cpuText = "0";
        // Aufschluesselung: welche Teile der Hauptschleife kosten wie viel (Entwicklungs-Build). Startet mit dem ersten Spieler,
        // damit alle Messpunkte schon existieren. Mittelwert je Bild ueber die ganze Messzeit + je 5-s-Fenster in eigener Datei.
        private static readonly string[] Parts =
        {
            "PlayerLoop", "Ashveil", "ScriptRun", ".Update()", ".LateUpdate()", ".FixedUpdate()", "Tick", "Physics", "Director", "Animat", "Skinned", "AIUpdate", "NavMesh", "Crowd",
            "FishNet", "Network", "Transport", "Tugboat", "GC.Collect", "UpdateAllRenderers", "UpdateTransform", "TransformChanged",
            "Instantiate", "Destroy", "Particle", "Audio", "Canvas", "UI.", "Render"
        };
        private static readonly ProfilerMarker WaitMarker = new ProfilerMarker("Ashveil.Lasttest.Warten"); // Takt-Wartezeit, keine Arbeit
        private readonly List<(string name, ProfilerRecorder rec)> _recs = new List<(string, ProfilerRecorder)>();
        private double[] _sumAll, _sumWin;
        // Speicher-Anforderungen je Bild: viel Muell → Speicherbereinigung und Heap-Vergroesserung (GC.Resize = Sekunden-Haenger)
        private ProfilerRecorder _alloc;
        private double _allocWin, _allocAll, _allocMaxFrame;
        private int _allocN;
        private int _nAll, _nWin;
        private readonly StringBuilder _breakdown = new StringBuilder();
        // 11.10.2026 (Lasttest r2.16: ein Bild von 3,1 s ohne Spur): langes Bild → Messpunkte DIESES Bildes ins Protokoll;
        // dazu Arbeitsspeicher des Rechners (51 Prozesse auf einem Rechner – voller Speicher macht Haenger, die kein Serverbefund sind)
        private float _nextLangesBild;
        private int _rechnerLastMax;
        // Laengstes Bild: wie viel davon war Spielschleife? (Rest = Prozess nicht am Zug: Rechner, Auslagerung, Pruefstand)
        private int _loopIdx = -1;
        private float _maxFrameMs, _maxFrameLoopMs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!TestBuild.Enabled) return;
            string file = ProfileSelection.ArgValue("-ashveil-loadtest");
            if (string.IsNullOrEmpty(file)) return;
            var go = new GameObject("LoadTestServerMonitor");
            DontDestroyOnLoad(go);
            var m = go.AddComponent<LoadTestServerMonitor>();
            m._file = file;
            string secs = ProfileSelection.ArgValue("-ashveil-loadtest-seconds");
            m._duration = float.TryParse(secs, NumberStyles.Float, CultureInfo.InvariantCulture, out float d) && d > 0f ? d : 300f;
            QualitySettings.vSyncCount = 0; // Takt: siehe LateUpdate (targetFrameRate gilt im batchmode nicht)
        }

        private void Start()
        {
            _start = _windowStart = Time.realtimeSinceStartup;
            _gc0 = System.GC.CollectionCount(0);
            _csv.AppendLine("zeit_s;verbindungen;spieler;gegner_lebend;bild_mittel_ms;bild_p99_ms;bild_max_ms;arbeit_mittel_ms;arbeit_p99_ms;cpu_kerne;gc;speicher_verwaltet_mb;speicher_gesamt_mb;rechner_last_pct;rechner_frei_mb");
            Debug.Log($"[Ashveil][Lasttest] Rechner: {SystemInfo.systemMemorySize} MB Arbeitsspeicher, {RechnerSpeicher.Text()} · {SystemInfo.processorCount} Kerne · " +
                      $"Physik: autoSyncTransforms {Physics.autoSyncTransforms}, Simulation {Physics.simulationMode}");
            _cpu0 = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            _cpuWall0 = Time.realtimeSinceStartup;
            _frameEnd = Time.realtimeSinceStartup;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file)));
            string abl = ProfileSelection.ArgValue("-ashveil-loadtest-ablation");
            if (!string.IsNullOrEmpty(abl) && float.TryParse(abl, NumberStyles.Float, CultureInfo.InvariantCulture, out float at))
                LoadTestAblation.Begin(Path.GetDirectoryName(Path.GetFullPath(_file)), at);
        }

        private void Update()
        {
            if (_done) return;
            float dt = Time.unscaledDeltaTime * 1000f;
            _frames.Add(dt);
            if (!_warm && InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Clients.Count > 0) { _warm = true; StartRecorders(); }
            SampleRecorders();
            if (_warm && dt > _maxFrameMs)
            {
                _maxFrameMs = dt;
                _maxFrameLoopMs = _loopIdx >= 0 && _recs[_loopIdx].rec.Valid && _recs[_loopIdx].rec.Count > 0 ? (float)(_recs[_loopIdx].rec.LastValue * 1e-6) : -1f;
            }
            if (_warm && dt > 250f && Time.realtimeSinceStartup >= _nextLangesBild)
            {
                _nextLangesBild = Time.realtimeSinceStartup + 2f;
                Debug.Log($"[Ashveil][Lasttest] Langes Bild: {dt:0} ms (Bild {Time.frameCount - 1}, {Time.realtimeSinceStartup - _start:0} s) – {TopLast(12)} · Rechner {RechnerSpeicher.Text()}");
            }
            if (_warm) _all.Add(dt);
            else _startupMax = Mathf.Max(_startupMax, dt);
            float now = Time.realtimeSinceStartup;
            if (now - _windowStart >= 5f) Flush(now);
            if (now - _start >= _duration) Finish();
        }

        // Ganz am Ende des Bildes: Arbeitszeit messen, dann auf 60 Bilder/s takten (Server-Takt wie im Betrieb)
        private void LateUpdate()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            float work = (now - _frameEnd) * 1000f;
            _work.Add(work);
            LoadTestAblation.LastWorkMs = work;
            if (_warm) _workAll.Add(work);
            int sleep = Mathf.FloorToInt(16.6f - work);
            if (sleep > 0) using (WaitMarker.Auto()) System.Threading.Thread.Sleep(sleep);
            _frameEnd = Time.realtimeSinceStartup;
        }

        private void StartRecorders()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (ProfilerRecorderHandle h in handles)
            {
                ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || _recs.Count >= 2000) continue;
                // alle Zeit-Messpunkte (Diagnose-Lauf); die Liste Parts kommt zuerst, falls die Grenze greift
                bool pick = _recs.Count < 1500;
                foreach (string part in Parts) if (d.Name.Contains(part)) { pick = true; break; }
                if (!pick) continue;
                var rec = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default);
                rec.Start();
                if (d.Name == "PlayerLoop") _loopIdx = _recs.Count;
                _recs.Add((d.Name, rec));
            }
            _alloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _sumAll = new double[_recs.Count];
            _sumWin = new double[_recs.Count];
            Debug.Log($"[Ashveil][Lasttest] Aufschluesselung: {_recs.Count} Messpunkte");
        }

        private void SampleRecorders()
        {
            if (_sumAll == null) return;
            for (int i = 0; i < _recs.Count; i++)
            {
                ProfilerRecorder r = _recs[i].rec;
                if (!r.Valid || r.Count == 0) continue;
                double ms = r.LastValue * 1e-6;
                _sumAll[i] += ms;
                _sumWin[i] += ms;
            }
            _nAll++;
            _nWin++;
            if (_alloc.Valid && _alloc.Count > 0)
            {
                double kb = _alloc.LastValue / 1024.0;
                _allocWin += kb;
                _allocAll += kb;
                _allocN++;
                if (kb > _allocMaxFrame) _allocMaxFrame = kb;
                // Grosse Einzel-Anforderung: ins Server-Log, damit die Nachbarzeilen zeigen, was gerade passierte
                if (kb > 4096) Debug.Log($"[Ashveil][Lasttest] Speicherspitze: {kb / 1024.0:0.0} MB in einem Bild (Bild {Time.frameCount - 1}, {Time.realtimeSinceStartup - _start:0} s) – {TopLast(8)}.");
            }
        }

        // Die teuersten Messpunkte (Mittel je Bild); Kinder und Eltern stehen beide drin (z. B. PlayerLoop enthaelt alles)
        private string Top(double[] sum, int n, int count, double minMs)
        {
            if (sum == null || n == 0) return "";
            var items = new List<(string, double)>();
            for (int i = 0; i < _recs.Count; i++)
            {
                double ms = sum[i] / n;
                if (ms < minMs || ms > 1000.0 || Skip(_recs[i].name)) continue; // Zaehler/Leerlauf, keine Arbeitszeit
                items.Add((_recs[i].name, ms));
            }
            items.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < count; i++)
                sb.Append(i == 0 ? "" : " | ").Append(items[i].Item1).Append(' ').Append(items[i].Item2.ToString("0.00", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Die teuersten Messpunkte des letzten Bildes (ms) – fuer lange Bilder und Speicherspitzen im Protokoll.</summary>
        private string TopLast(int count)
        {
            var items = new List<(string, double)>();
            for (int i = 0; i < _recs.Count; i++)
            {
                ProfilerRecorder r = _recs[i].rec;
                if (!r.Valid || r.Count == 0 || Skip(_recs[i].name)) continue;
                double ms = r.LastValue * 1e-6;
                if (ms >= 1.0 && ms <= 60000.0) items.Add((_recs[i].name, ms)); // Zaehler-Messpunkte (Object.Register u. a.) liefern Unsinn
            }
            items.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < count; i++)
                sb.Append(i == 0 ? "" : " | ").Append(items[i].Item1).Append(' ').Append(items[i].Item2.ToString("0.0", CultureInfo.InvariantCulture));
            return sb.Length > 0 ? sb.ToString() : "keine Messpunkte ueber 1 ms";
        }

        private static bool Skip(string name) =>
            name == "Idle" || name == "Main Thread" || name.EndsWith("Job", System.StringComparison.Ordinal) || name.StartsWith("WaitFor", System.StringComparison.Ordinal) ||
            name == "KickJobs" || name == "Component.SetGameObject" || name == "Ashveil.Lasttest.Warten";

        private void OnDestroy()
        {
            foreach (var r in _recs) r.rec.Dispose();
            _recs.Clear();
            if (_alloc.Valid) _alloc.Dispose();
        }

        private static float Percentile(List<float> v, float p)
        {
            if (v.Count == 0) return 0f;
            var s = new List<float>(v);
            s.Sort();
            return s[Mathf.Clamp(Mathf.CeilToInt(p * s.Count) - 1, 0, s.Count - 1)];
        }

        private void Flush(float now)
        {
            int clients = InstanceFinder.ServerManager != null ? InstanceFinder.ServerManager.Clients.Count : 0;
            int players = NetworkPlayer.ServerPlayers.Count;
            int enemies = 0;
            foreach (EnemyBrain b in EnemyBrain.All)
                if (b != null && b.GetComponent<Health>() is { IsAlive: true }) enemies++;
            _maxClients = Mathf.Max(_maxClients, clients);
            _maxPlayers = Mathf.Max(_maxPlayers, players);
            float avg = 0f, max = 0f;
            foreach (float f in _frames) { avg += f; max = Mathf.Max(max, f); }
            avg = _frames.Count > 0 ? avg / _frames.Count : 0f;
            float wAvg = 0f;
            foreach (float w in _work) wAvg += w;
            wAvg = _work.Count > 0 ? wAvg / _work.Count : 0f;
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            System.TimeSpan cpu = proc.TotalProcessorTime;
            float cores = (float)((cpu - _cpu0).TotalSeconds / Mathf.Max(0.001f, now - _cpuWall0));
            _cpu0 = cpu;
            _cpuWall0 = now;
            _cpuText = cores.ToString("0.00", CultureInfo.InvariantCulture);
            RechnerSpeicher.Lesen(out int rechnerLast, out _, out long rechnerFrei);
            _rechnerLastMax = Mathf.Max(_rechnerLastMax, rechnerLast);
            _csv.AppendLine(string.Join(";",
                (now - _start).ToString("0", CultureInfo.InvariantCulture), clients, players, enemies,
                avg.ToString("0.0", CultureInfo.InvariantCulture), Percentile(_frames, 0.99f).ToString("0.0", CultureInfo.InvariantCulture),
                max.ToString("0.0", CultureInfo.InvariantCulture),
                wAvg.ToString("0.0", CultureInfo.InvariantCulture), Percentile(_work, 0.99f).ToString("0.0", CultureInfo.InvariantCulture), _cpuText,
                System.GC.CollectionCount(0) - _gc0,
                (System.GC.GetTotalMemory(false) / 1048576f).ToString("0", CultureInfo.InvariantCulture),
                (Profiler.GetTotalAllocatedMemoryLong() / 1048576f).ToString("0", CultureInfo.InvariantCulture),
                rechnerLast, rechnerFrei));
            _frames.Clear();
            _work.Clear();
            _windowStart = now;
            if (_sumWin != null)
            {
                _breakdown.Append((now - _start).ToString("0", CultureInfo.InvariantCulture)).Append(" s, ").Append(players).Append(" Spieler, ")
                    .Append((_nWin > 0 ? _allocWin / _nWin : 0).ToString("0", CultureInfo.InvariantCulture)).Append(" KB Speicher je Bild: ")
                    .AppendLine(Top(_sumWin, _nWin, 14, 0.05));
                System.Array.Clear(_sumWin, 0, _sumWin.Length);
                _allocWin = 0;
                _nWin = 0;
            }
            try { File.WriteAllText(_file, _csv.ToString()); } catch (IOException) { }
            try { File.WriteAllText(BreakdownFile, _breakdown.ToString()); } catch (IOException) { }
        }

        private string BreakdownFile => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_file)), "server_aufschluesselung.txt");

        private float WAvg()
        {
            float a = 0f;
            foreach (float w in _workAll) a += w;
            return _workAll.Count > 0 ? a / _workAll.Count : 0f;
        }

        private void Finish()
        {
            _done = true;
            Flush(Time.realtimeSinceStartup);
            float avg = 0f, max = 0f;
            foreach (float f in _all) { avg += f; max = Mathf.Max(max, f); }
            avg = _all.Count > 0 ? avg / _all.Count : 0f;
            var sb = new StringBuilder(_csv.ToString());
            sb.AppendLine();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "ZUSAMMENFASSUNG;dauer_s={0:0};max_verbindungen={1};max_spieler={2};bild_mittel_ms={3:0.0};bild_p99_ms={4:0.0};bild_p999_ms={5:0.0};bild_max_ms={6:0.0};gc={7};start_max_ms={8:0};arbeit_mittel_ms={9:0.0};arbeit_p99_ms={10:0.0};rechner_last_max_pct={11};rechner_gesamt_mb={12};bild_max_schleife_ms={13:0}",
                Time.realtimeSinceStartup - _start, _maxClients, _maxPlayers, avg, Percentile(_all, 0.99f), Percentile(_all, 0.999f), max,
                System.GC.CollectionCount(0) - _gc0, _startupMax, WAvg(), Percentile(_workAll, 0.99f), _rechnerLastMax, SystemInfo.systemMemorySize, _maxFrameLoopMs));
            sb.AppendLine("AUFSCHLUESSELUNG;" + Top(_sumAll, _nAll, 25, 0.02));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "SPEICHER;alloc_kb_je_bild={0:0};alloc_kb_max_bild={1:0}",
                _allocN > 0 ? _allocAll / _allocN : 0, _allocMaxFrame));
            if (CharacterStore.Service is JsonFilePersistence jp) sb.AppendLine(jp.Stats());
            try { File.WriteAllText(_file, sb.ToString()); } catch (IOException) { }
            Debug.Log("[Ashveil][Lasttest] Messung beendet: " + _file);
            StartCoroutine(ShutDown());
        }

        // Sauber herunterfahren wie ein echter Server: erst alle Verbindungen schliessen, dann beenden.
        // (02.10.: mit 50 offenen Verbindungen hing Application.Quit direkt ueber zwei Minuten.)
        private System.Collections.IEnumerator ShutDown()
        {
            float t0 = Time.realtimeSinceStartup;
            string flag = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_file)), "server_beenden.txt");
            void Trace(string what)
            {
                try { File.AppendAllText(flag, $"{Time.realtimeSinceStartup - t0:0.00} s  {what}\n"); } catch (IOException) { }
            }
            try { File.WriteAllText(flag, ""); } catch (IOException) { }
            Trace("Beenden beginnt");
            if (InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Started)
                InstanceFinder.ServerManager.StopConnection(true);
            Trace("Server-Verbindungen gestoppt (StopConnection)");
            yield return new WaitForSecondsRealtime(1f);
            if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started)
                InstanceFinder.ClientManager.StopConnection();
            Trace("Client gestoppt");
            bool flushed = CharacterStore.FlushSaves(10000);
            Trace(flushed ? "Speicherstaende geschrieben" : "Speicherstaende NICHT vollstaendig geschrieben (10 s)");
            if (CharacterStore.Service is JsonFilePersistence jp) Trace(jp.Stats());
            Debug.Log($"[Ashveil][Lasttest] Verbindungen geschlossen nach {(Time.realtimeSinceStartup - t0):0.0} s – beende.");
            Application.quitting += () => Trace("Unity-Ereignis quitting");
            // Waechter: haengt das Beenden (02.10. zweimal mit 50 Verbindungen), nach 15 s hart beenden und das vermerken.
            // Der Runner wertet "Waechter" als eigenen Befund (Server faehrt nicht sauber herunter).
            var watchdog = new System.Threading.Thread(() =>
            {
                System.Threading.Thread.Sleep(15000);
                try { File.AppendAllText(flag, "Waechter: Beenden hing 15 s – hart beendet\n"); } catch (IOException) { }
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }) { IsBackground = true, Name = "Ashveil-Beenden-Waechter" };
            watchdog.Start();
            Trace("Quit aufgerufen");
            Application.Quit();
        }
    }
}
