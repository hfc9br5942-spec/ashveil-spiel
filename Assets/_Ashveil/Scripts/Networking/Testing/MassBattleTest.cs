using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Ashveil.Core;
using Ashveil.Core.Settings;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Visuals;
using Ashveil.World;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace Ashveil.Networking
{
    /// <summary>
    /// Massenschlacht-Stresstest (Max 30.09.: "500 gegen 500 im Radius sichtbar, ohne Absturz – Ziel ist das, was WoW Classic
    /// maximal kann"). Zwei Heere laufen aufeinander zu und kaempfen im Blickfeld der eigenen Figur; die Zahl der Figuren
    /// steigt stufenweise bis 3000 (eine volle Classic-Ebene). Gemessen je Stufe: Bildrate, 1 % tiefste, CPU, GPU,
    /// Speicher, Figuren voll/reduziert/aus, Kosten der Bewegung (entspricht der Netz-Interpolation echter Mitspieler).
    ///
    /// Die Figuren sind Darstellungs-Doubles wie im Leistungstest (echte Koerper, Ausruestung, Namensschilder, Animation,
    /// Figurendetail) – ohne Netzverbindung. Die Netzlast wird im Bericht gerechnet, nicht gemessen (steht so dabei).
    /// Schutz gegen Abstuerze: Wird der Speicher knapp oder ein Bild laenger als 5 s, bricht der Test die Steigerung ab
    /// und schreibt, bei welcher Zahl.
    /// </summary>
    public sealed class MassBattleTest : MonoBehaviour
    {
        public const string FlagFile = "Temp/ashveil_massbattle.flag";
        public const string Arg = "-ashveil-massbattle";

        private static readonly int[] Steps = { 100, 250, 500, 1000, 2000, 3000 };
        private static string ReportPath => ProjectPaths.Logs(Application.isEditor ? "Ashveil_Massenschlacht.txt" : "Ashveil_Massenschlacht_Spiel.txt");

        private readonly StringBuilder _report = new StringBuilder();
        private readonly List<Fighter> _fighters = new List<Fighter>();
        private readonly List<float> _frameTimes = new List<float>(4096);
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private double _cpuMs, _gpuMs, _skinned, _moveMs;
        private int _statFrames, _gpuFrames, _moveFrames;
        private int _passed, _failed, _logErrors;
        private Vector3 _center, _forward, _right;
        private Transform _root;
        private readonly Stopwatch _moveWatch = new Stopwatch();

        // Spawn-Material
        private AppearanceCatalog _cat;
        private RuntimeAnimatorController _controller;
        private readonly List<ItemDefinition>[] _armor = new List<ItemDefinition>[9];
        private readonly List<ItemDefinition> _weapons = new List<ItemDefinition>();
        private System.Random _rng = new System.Random(1000);

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MoveZId = Animator.StringToHash("MoveZ");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int WalkingId = Animator.StringToHash("Walking");

        private sealed class Fighter
        {
            public Transform T;
            public Animator Anim;
            public int Side;          // 0 = Heer A (Norden), 1 = Heer B (Sueden)
            public Vector2 Home;      // Platz im Kampfgetuemmel (Feld-Koordinaten u = quer, v = laengs)
            public Vector2 Start;     // Aufstellung vor dem Angriff
            public float Phase, Radius, Speed;
            public float Y;
            public float NextGround;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CheckFlag()
        {
            if (!TestBuild.Enabled) return;
            bool requested = Environment.GetCommandLineArgs() is string[] args && Array.IndexOf(args, Arg) >= 0;
            if (Application.isEditor && File.Exists(FlagFile))
            {
                requested = true;
                try { File.Delete(FlagFile); } catch (IOException) { }
            }
            if (!requested) return;
            ProfileSelection.Override = "perftest";
            var go = new GameObject("Massenschlacht-Test");
            DontDestroyOnLoad(go);
            go.AddComponent<MassBattleTest>();
        }

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            GraphicsOptions original = GraphicsSettingsStore.Current.Clone();
            GraphicsSettingsStore.PersistenceEnabled = false;
            Line($"ASHVEIL Massenschlacht-Stresstest · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Line($"  Grafikkarte: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB) · {SystemInfo.graphicsDeviceType}");
            Line($"  Prozessor:   {SystemInfo.processorType} ({SystemInfo.processorCount} Threads) · RAM {SystemInfo.systemMemorySize} MB");
            Line($"  Bild:        {Screen.width} x {Screen.height} · {(Application.isEditor ? "Unity-Editor (Editor kostet zusaetzlich)" : "fertiges Spiel")}");
            Line("  Ziel (Max): 500 gegen 500 im Radius sichtbar ohne Absturz; Obergrenze wie eine volle WoW-Classic-Ebene (≈ 3000).");

            PlayerCharacter player = null;
            float wait = 0f;
            while (player == null && wait < 40f)
            {
                player = FindLocalPlayer();
                wait += 0.5f;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Check(player != null, "Eigene Spielfigur ist da");
            if (player == null)
            {
                Finish(original);
                yield break;
            }
            yield return new WaitForSecondsRealtime(4f);
            PrepareSpawn(player);
            Check(_cat != null && _cat.bodies.Count > 0, "Figuren-Katalog geladen");
            if (_cat == null || _cat.bodies.Count == 0)
            {
                Finish(original);
                yield break;
            }

            // Schlachtfeld: 70 m vor der Kamera, Heere quer zur Blickrichtung
            Camera cam = Camera.main;
            _forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (_forward.sqrMagnitude < 0.01f) _forward = Vector3.forward;
            _right = Vector3.Cross(Vector3.up, _forward);
            _center = player.transform.position + _forward * 70f;
            _root = new GameObject("Massenschlacht-Figuren").transform;

            ApplyUncapped(GraphicsTier.Mittel);
            yield return new WaitForSecondsRealtime(2f);
            yield return Measure(4f);
            Section("Ohne Heere (Mittel)");
            Line($"  {Avg(),6:0} Bilder/s (1 % tiefste {Low1(),5:0}) · {Stats()}");

            // ---------------------------------------------------------- Steigerung (Mittel)
            Section("Steigerung der Figurenzahl (Mittel, VSync aus)");
            int reached = 0;
            string abortReason = null;
            var perStep = new Dictionary<int, float>();
            foreach (int target in Steps)
            {
                var spawnWatch = Stopwatch.StartNew();
                float longest = 0f;
                while (_fighters.Count < target)
                {
                    int batch = Mathf.Min(25, target - _fighters.Count);
                    float t0 = Time.realtimeSinceStartup;
                    for (int i = 0; i < batch; i++) SpawnFighter(_fighters.Count);
                    yield return null;
                    longest = Mathf.Max(longest, Time.realtimeSinceStartup - t0);
                    if (MemoryCritical(out string mem))
                    {
                        abortReason = $"Speicher knapp bei {_fighters.Count} Figuren ({mem})";
                        break;
                    }
                    if (Time.unscaledDeltaTime > 5f)
                    {
                        abortReason = $"Bild dauerte {Time.unscaledDeltaTime:0.0} s bei {_fighters.Count} Figuren";
                        break;
                    }
                }
                spawnWatch.Stop();
                if (abortReason != null) break;
                yield return new WaitForSecondsRealtime(1f);
                yield return WaitMerged();
                yield return new WaitForSecondsRealtime(2f);
                yield return Measure(6f);
                reached = target;
                perStep[target] = Avg();
                CharacterLod.Tally(out int full, out int reduced, out int hidden);
                Line($"  {target,5} Figuren: {Avg(),6:0} Bilder/s (1 % tiefste {Low1(),5:0}) · voll/reduziert/aus {full}/{reduced}/{hidden} · " +
                     $"Erzeugen {spawnWatch.Elapsed.TotalSeconds:0.0} s · Zusammenfassen {_lastMergeWait:0.0} s");
                Line($"               {Stats()} · Bewegung {MoveMs():0.00} ms · zusammengefasst {CharacterLod.MergedCount} · {MemoryText()}");
                Line($"               Zusammenfassen: {CharacterMerge.Diagnose()} · wartend {CharacterLod.MergePending}");
            }
            if (abortReason != null) Line($"  ABGEBROCHEN: {abortReason}");
            Check(reached >= 1000, "1000 Figuren (500 gegen 500) laufen ohne Absturz", $"erreicht {reached}");
            Check(reached >= 3000, "3000 Figuren (volle Classic-Ebene) laufen ohne Absturz", $"erreicht {reached}");

            // ---------------------------------------------------------- 1000 und Maximum je Stufe
            foreach (int n in new[] { 1000, reached })
            {
                if (n > _fighters.Count || n <= 0) continue;
                SetActiveCount(n);
                Section($"{n} Figuren je Grafikstufe");
                for (int t = 0; t < GraphicsCatalog.TierCount; t++)
                {
                    ApplyUncapped((GraphicsTier)t);
                    yield return new WaitForSecondsRealtime(1f);
                    yield return WaitMerged();
                    yield return new WaitForSecondsRealtime(2f);
                    yield return Measure(6f);
                    CharacterLod.Tally(out int full, out int reduced, out int hidden);
                    Line($"  {GraphicsCatalog.TierNames[t],-8}: {Avg(),6:0} Bilder/s (1 % tiefste {Low1(),5:0}) · voll/reduziert/aus {full}/{reduced}/{hidden}");
                    Line($"            {Stats()} · Bewegung {MoveMs():0.00} ms");
                    string est = Estimate();
                    if (est.Length > 0) Line("            " + est);
                    if (n == 1000 && t == 0) Check(Avg() >= 30f, "Niedrig, 1000 Figuren: mindestens 30 Bilder/s auf diesem Rechner", $"{Avg():0}");
                    if (n == 1000 && t == 1) Check(Avg() >= 30f, "Mittel, 1000 Figuren: mindestens 30 Bilder/s auf diesem Rechner", $"{Avg():0}");
                }
                if (n == reached) break;
            }

            yield return Breakdown();
            NetworkEstimate();
            Line("");
            Line("  Einordnung: WoW Classic lief bei der Oeffnung des AQ-Tors (weit ueber 1000 Spieler an einem Ort) selbst");
            Line("  mit einstelligen Bildraten und Serverabstuerzen. WoW Forever meldet in vollen Gebieten 25–30 Bilder/s (Beta).");
            Line("  Die Figuren hier sind Doubles ohne Netz; Server- und Netzlast gehoeren in einen eigenen Lasttest.");

            ClearAll();
            Finish(original);
        }

        // ================================================================== Figuren

        private void PrepareSpawn(PlayerCharacter player)
        {
            _cat = GameDataBootstrap.Catalog;
            Animator playerAnim = player.GetComponentInChildren<Animator>();
            _controller = playerAnim != null ? playerAnim.runtimeAnimatorController : _cat != null ? _cat.previewController : null;
            for (int s = 0; s < _armor.Length; s++) _armor[s] = new List<ItemDefinition>();
            if (GameData.Database == null) return;
            foreach (GameDefinition d in GameData.Database.All)
            {
                if (!(d is ItemDefinition item) || item.appearance == null) continue;
                for (int s = 0; s < _armor.Length; s++) if (item.FitsSlot((EquipmentSlot)s)) _armor[s].Add(item);
                if (item.FitsSlot(EquipmentSlot.MainHand)) _weapons.Add(item);
            }
        }

        private void SpawnFighter(int i)
        {
            int body = i % Mathf.Min(2, _cat.bodies.Count);
            GameObject prefab = _cat.bodies[body].model;
            if (prefab == null) return;
            var holder = new GameObject($"Kaempfer {i + 1:0000}").transform;
            holder.SetParent(_root, false);
            GameObject model = Instantiate(prefab, holder, false);
            Animator anim = model.GetComponentInChildren<Animator>(true) ?? model.AddComponent<Animator>();
            anim.applyRootMotion = false;
            if (_controller != null) anim.runtimeAnimatorController = _controller;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var look = new CharacterAppearanceData
            {
                body = body,
                skin = _rng.Next(0, _cat.SkinCount(body)),
                hair = _rng.Next(0, Mathf.Max(1, _cat.hairstyles.Count)),
                hairColor = _rng.Next(0, Mathf.Max(1, _cat.hairColors.Count)),
                beard = body == 0 ? _rng.Next(0, Mathf.Max(1, _cat.beards.Count)) : 0,
                face = _rng.Next(0, Mathf.Max(1, _cat.faces.Count)),
                eyeColor = _rng.Next(0, Mathf.Max(1, _cat.eyeColors.Count)),
                feature = _rng.Next(0, Mathf.Max(1, _cat.features.Count))
            };
            look.EnsureArrays();
            _cat.Sanitize(look);
            var equipment = new ItemStack[CharacterAppearanceData.SlotCount];
            for (int s = 0; s < _armor.Length; s++)
                if (_armor[s].Count > 0 && _rng.NextDouble() < 0.8) equipment[s] = new ItemStack(_armor[s][_rng.Next(_armor[s].Count)].Id, 1);
            if (_weapons.Count > 0) equipment[(int)EquipmentSlot.MainHand] = new ItemStack(_weapons[_rng.Next(_weapons.Count)].Id, 1);
            var rig = model.AddComponent<AppearanceRig>();
            rig.Init(holder);
            rig.Apply(EquipmentVisuals.Resolve(equipment, look, _cat), look, _cat);
            rig.PlaceWeapons(null, null);

            int side = i % 2;
            var label = holder.gameObject.AddComponent<WorldLabel>();
            label.Configure(side == 0 ? $"Wacht {i + 1}" : $"Nharim {i + 1}", "", side == 0 ? WorldLabelStyle.Friendly : WorldLabelStyle.Hostile, 40f);
            holder.gameObject.AddComponent<CharacterLod>();

            // Getuemmel: 160 m breit, 90 m tief, dichter in der Mitte; Aufstellung 45 m hinter der eigenen Linie
            float u = (float)(_rng.NextDouble() * 2 - 1);
            u = Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), 1.4f) * 80f;
            float v = (float)(_rng.NextDouble() * 2 - 1) * 45f * (0.4f + 0.6f * (float)_rng.NextDouble());
            var f = new Fighter
            {
                T = holder,
                Anim = anim,
                Side = side,
                Home = new Vector2(u, v),
                Start = new Vector2(u, (side == 0 ? 1f : -1f) * (50f + (float)_rng.NextDouble() * 25f)),
                Phase = (float)_rng.NextDouble() * 6.28f,
                Radius = 1.5f + (float)_rng.NextDouble() * 3f,
                Speed = 1.4f + (float)_rng.NextDouble() * 1.2f,
                NextGround = 0f
            };
            _fighters.Add(f);
        }

        /// <summary>Wohin geht die Zeit? 1000 Figuren auf Mittel, je ein Teil anders bzw. abgeschaltet.</summary>
        private IEnumerator Breakdown()
        {
            if (_fighters.Count < 1000) yield break;
            SetActiveCount(1000);
            ApplyUncapped(GraphicsTier.Mittel);
            Section("Aufschluesselung (1000 Figuren, Mittel)");
            var labels = new List<WorldLabel>();
            var renderers = new List<Renderer>();
            foreach (Fighter f in _fighters)
                if (f.T != null && f.T.gameObject.activeSelf)
                {
                    labels.AddRange(f.T.GetComponentsInChildren<WorldLabel>(true));
                    renderers.AddRange(f.T.GetComponentsInChildren<Renderer>(true));
                }

            IEnumerator Run(string name)
            {
                yield return WaitMerged();
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Measure(5f);
                Line($"  {name,-44} {Avg(),6:0} Bilder/s · {Stats()} · Bewegung {MoveMs():0.00} ms");
            }

            yield return Run("jetzt: zusammengefasste Figuren (Standard)");
            Line($"    zusammengefasst: {CharacterLod.MergedCount} Figuren · {CharacterMerge.CachedMeshes} geteilte Netze · " +
                 $"{CharacterMerge.CachedVertices / 1000} Tsd. Punkte auf der Grafikkarte");
            CharacterLod.MergeReduced = false;
            CharacterLod.ReapplyAll();
            yield return Run("ohne Zusammenfassen (Teile einzeln)");
            CharacterLod.ReducedAnimation keep = CharacterLod.ReducedMode;
            CharacterLod.ReducedMode = CharacterLod.ReducedAnimation.Stepped;
            CharacterLod.ReapplyAll();
            yield return Run("frueher: Animation im Hauptthread getaktet");
            CharacterLod.ReducedMode = CharacterLod.ReducedAnimation.Native;
            CharacterLod.ReapplyAll();
            yield return Run("jetzt: Animation ueber Unitys Mehrkern-System");
            CharacterLod.ReducedMode = CharacterLod.ReducedAnimation.Frozen;
            CharacterLod.ReapplyAll();
            yield return Run("ohne Animation (nur Zeichnen + Haut)");
            foreach (WorldLabel l in labels) if (l != null) l.enabled = false;
            yield return Run("ohne Animation, ohne Namensschilder");
            foreach (Renderer r in renderers) if (r != null) r.forceRenderingOff = true;
            yield return Run("ohne Animation, ohne Schilder, nichts gezeichnet");
            foreach (Renderer r in renderers) if (r != null) r.forceRenderingOff = false;
            foreach (WorldLabel l in labels) if (l != null) l.enabled = true;
            CharacterLod.ReducedMode = keep;
            CharacterLod.MergeReduced = true;
            CharacterLod.ReapplyAll();
            Line($"  Je Figur {renderers.Count / 1000f:0.0} Renderer (Koerperteile, Ruestung, Haare, Waffe).");
        }

        private void SetActiveCount(int n)
        {
            for (int i = 0; i < _fighters.Count; i++)
                if (_fighters[i].T != null) _fighters[i].T.gameObject.SetActive(i < n);
        }

        private void Update()
        {
            if (_fighters.Count == 0) return;
            _moveWatch.Restart();
            float time = Time.time;
            for (int i = 0; i < _fighters.Count; i++)
            {
                Fighter f = _fighters[i];
                if (f.T == null || !f.T.gameObject.activeSelf) continue;
                // Angriff: 12 s von der Aufstellung ins Getuemmel, danach kreisen/nachsetzen um den eigenen Platz
                float charge = Mathf.Clamp01((time - f.Phase * 0.3f) % 40f / 12f);
                Vector2 p = Vector2.Lerp(f.Start, f.Home, charge);
                float speed = charge < 1f ? 4.5f : f.Speed;
                Vector2 dirUv = f.Home - f.Start;
                if (charge >= 1f)
                {
                    float a = time * f.Speed / f.Radius + f.Phase;
                    p += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * f.Radius;
                    dirUv = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
                }
                Vector3 pos = _center + _right * p.x + _forward * p.y;
                if (time >= f.NextGround)
                {
                    // Bodenhoehe 5x pro Sekunde (wie Netz-Interpolation mit Bodenanpassung)
                    f.Y = WorldGround.SampleHeight(pos, _center.y);
                    f.NextGround = time + 0.2f;
                }
                pos.y = f.Y;
                Vector3 dir = _right * dirUv.x + _forward * dirUv.y;
                if (dir.sqrMagnitude < 0.0001f) dir = f.Side == 0 ? -_forward : _forward;
                f.T.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.normalized, Vector3.up));
                Animator an = f.Anim;
                if (an == null || an.runtimeAnimatorController == null || !an.isActiveAndEnabled) continue;
                an.SetFloat(SpeedId, speed);
                an.SetFloat(MoveZId, 1f);
                an.SetBool(GroundedId, true);
                an.SetBool(WalkingId, speed < 2f);
            }
            _moveWatch.Stop();
            _moveMs += _moveWatch.Elapsed.TotalMilliseconds;
            _moveFrames++;
        }

        private void ClearAll()
        {
            if (_root != null) Destroy(_root.gameObject);
            _fighters.Clear();
        }

        // ================================================================== Messung

        private IEnumerator Measure(float seconds)
        {
            _frameTimes.Clear();
            _cpuMs = _gpuMs = _skinned = _moveMs = 0;
            _statFrames = _gpuFrames = _moveFrames = 0;
            using (ProfilerRecorder main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1))
            using (ProfilerRecorder skinned = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Visible Skinned Meshes Count"))
            {
                float end = Time.realtimeSinceStartup + seconds;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    _frameTimes.Add(Time.unscaledDeltaTime);
                    _statFrames++;
                    if (skinned.Valid) _skinned += skinned.LastValue;
                    double mainMs = main.Valid ? main.LastValue * 1e-6 : 0.0;
                    FrameTimingManager.CaptureFrameTimings();
                    bool timed = FrameTimingManager.GetLatestTimings(1, _timing) > 0;
                    double cpu = timed && _timing[0].cpuMainThreadFrameTime > 0.0
                        ? _timing[0].cpuMainThreadFrameTime - _timing[0].cpuMainThreadPresentWaitTime
                        : mainMs;
                    _cpuMs += cpu;
                    if (timed && _timing[0].gpuFrameTime > 0.0 && _timing[0].gpuFrameTime < 1000.0)
                    {
                        _gpuMs += _timing[0].gpuFrameTime;
                        _gpuFrames++;
                    }
                }
            }
        }

        /// <summary>Warten, bis alle umgestuften Figuren zusammengefasst sind (Zeitbudget je Bild).</summary>
        private IEnumerator WaitMerged(float max = 30f)
        {
            float t0 = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(0.6f);
            while (CharacterLod.MergePending > 0 && Time.realtimeSinceStartup - t0 < max) yield return null;
            _lastMergeWait = Time.realtimeSinceStartup - t0;
        }

        private float _lastMergeWait;

        private double MoveMs() => _moveFrames > 0 ? _moveMs / _moveFrames : 0.0;

        private string Stats()
        {
            int n = Mathf.Max(1, _statFrames);
            string gpu = _gpuFrames > 0 ? $"{_gpuMs / _gpuFrames:0.0} ms" : "n. v.";
            string cpu = _cpuMs > 0 ? $"{_cpuMs / n:0.0} ms" : "n. v.";
            string skin = _skinned > 0 ? $" · {_skinned / n:0} Haut-Meshes sichtbar" : "";
            return $"CPU {cpu} · GPU {gpu}{skin}";
        }

        /// <summary>Hochrechnung wie im Leistungstest: RTX 5080 → RTX 3060 (GPU x3, 1080p), 285K → 6-Kern-Mittelklasse (CPU x1,6).</summary>
        private string Estimate()
        {
            if (_gpuFrames == 0 || _statFrames == 0) return "";
            if (!SystemInfo.graphicsDeviceName.Contains("5080")) return "";
            double cpu = _cpuMs / _statFrames, gpu = _gpuMs / _gpuFrames;
            double pixelFactor = (1920.0 * 1080.0) / Mathf.Max(1, Screen.width * Screen.height);
            double gpu3060 = gpu * 3.0 * pixelFactor, cpuMid = cpu * 1.6;
            return $"≈ Durchschnitts-PC (RTX 3060 + 6-Kerner, 1080p): GPU {gpu3060:0.0} ms, CPU {cpuMid:0.0} ms → ~{1000.0 / Math.Max(gpu3060, cpuMid):0} Bilder/s";
        }

        private float Avg()
        {
            if (_frameTimes.Count == 0) return 0f;
            float sum = 0f;
            foreach (float f in _frameTimes) sum += f;
            return _frameTimes.Count / Mathf.Max(0.0001f, sum);
        }

        private float Low1()
        {
            if (_frameTimes.Count == 0) return 0f;
            var sorted = new List<float>(_frameTimes);
            sorted.Sort();
            int n = Mathf.Max(1, sorted.Count / 100);
            float sum = 0f;
            for (int i = sorted.Count - n; i < sorted.Count; i++) sum += sorted[i];
            return n / Mathf.Max(0.0001f, sum);
        }

        private static string MemoryText()
        {
            long alloc = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
            long reserved = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
            long gfx = Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024);
            return $"Speicher {alloc} MB belegt / {reserved} MB reserviert, Grafiktreiber {gfx} MB";
        }

        private static bool MemoryCritical(out string text)
        {
            long reservedMb = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
            text = MemoryText();
            return reservedMb > SystemInfo.systemMemorySize * 0.6;
        }

        /// <summary>Netzlast gerechnet (nicht gemessen): was 1000 bzw. 3000 sichtbare Spieler an Daten erzeugen.</summary>
        private void NetworkEstimate()
        {
            Section("Netzlast (gerechnet, nicht gemessen)");
            // je Figur und Aktualisierung: Kopf 4 B + Position 6 B (quantisiert) + Blick 1 B + Bewegungszustand 2 B ≈ 13 B
            const double bytes = 13.0;
            foreach (int n in new[] { 1000, 3000 })
            {
                double flat = n * bytes * 20.0;                                     // alle mit 20 Hz
                double tiered = (50 * 20.0 + 200 * 10.0 + (n - 250) * 3.0) * bytes;  // nah 20 Hz, mittel 10 Hz, fern 3 Hz
                Line($"  {n} sichtbare Spieler: je Client {flat / 1024:0} KB/s bei 20 Hz fuer alle, gestaffelt {tiered / 1024:0} KB/s " +
                     $"(nah 20 / mittel 10 / fern 3 Hz). Server gesamt (alle {n} schauen zu): {flat * n / 1048576:0} MB/s bzw. {tiered * n / 1048576:0} MB/s.");
            }
            Line("  Folgerung: Pro Client ist das auch mit DSL machbar. Der Engpass ist der Server (Daten wachsen mit dem Quadrat der");
            Line("  Spielerzahl) – deshalb gestaffelte Raten nach Abstand, Buendelung und spaeter Ebenen wie in WoW.");
        }

        // ================================================================== Bericht

        private static void ApplyUncapped(GraphicsTier tier)
        {
            GraphicsSettingsStore.SetTier(tier);
            GraphicsOptions o = GraphicsSettingsStore.Current.Clone();
            o.vSync = false;
            o.frameLimit = GraphicsCatalog.FrameLimits.Length - 1;
            GraphicsSettingsStore.Set(o);
        }

        private void Finish(GraphicsOptions original)
        {
            Application.logMessageReceived -= OnLog;
            GraphicsSettingsStore.Set(original);
            GraphicsSettingsStore.PersistenceEnabled = true;
            Check(_logErrors == 0, "Keine Fehler/Ausnahmen im Log", $"{_logErrors}");
            Line("");
            Line($"ERGEBNIS: {_passed} bestanden, {_failed} fehlgeschlagen, {_logErrors} Fehler/Ausnahmen im Log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, _report.ToString());
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Ashveil][Massenschlacht] Bericht nicht geschrieben: " + e.Message);
            }
            Debug.Log($"[Ashveil][Massenschlacht] Fertig: {_passed} ok, {_failed} fehlgeschlagen → {ReportPath}");
            if (!Application.isEditor) Application.Quit();
        }

        private void OnLog(string msg, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception) && !msg.Contains("assetPath hash") && !TestLog.IsEditorTooling(msg, stack)) _logErrors++;
        }

        private void Section(string title)
        {
            Line("");
            Line($"[{title}]");
        }

        private void Line(string s)
        {
            _report.AppendLine(s);
            Debug.Log("[Ashveil][Massenschlacht] " + s);
        }

        private void Check(bool ok, string what, string detail = "")
        {
            if (ok) _passed++;
            else _failed++;
            Line($"  {(ok ? "OK  " : "FEHL")}  {what}{(ok || string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")")}");
        }

        private static PlayerCharacter FindLocalPlayer()
        {
            foreach (PlayerCharacter pc in FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Exclude))
                if (pc.isActiveAndEnabled) return pc;
            return null;
        }
    }
}
