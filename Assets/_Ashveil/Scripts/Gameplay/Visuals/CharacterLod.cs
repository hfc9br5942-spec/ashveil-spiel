using System.Collections.Generic;
using Ashveil.Core.Settings;
using Ashveil.Gameplay.Characters;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// Detailstufe einer Figur (Spieler, spaeter NPCs) – damit 50 Spieler in der Naehe fluessig bleiben.
    /// Ein gemeinsamer Takt (4x pro Sekunde) sortiert alle Figuren nach Abstand zur Kamera:
    ///   Stufe 0 – die naechsten N Figuren (Einstellung "Figurendetail"): volle Qualitaet.
    ///   Stufe 1 – weitere Figuren: keine eigenen Schatten, keine Kleinteile (Schnallen, Nieten …), einfachere
    ///             Hautverformung (2 Knochen) und Animation mit halber Rate (ab 40 m: Drittel-Rate), ausserhalb des
    ///             Bildes gar nicht.
    ///   Stufe 2 – jenseits der Figuren-Sichtweite: nicht gezeichnet (Namen zeichnet der Namensschild-Renderer selbst).
    /// Die eigene Spielfigur bleibt immer auf Stufe 0. Nur Darstellung – Spiellogik und Netz sind unberuehrt.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterLod : MonoBehaviour
    {
        private static readonly List<CharacterLod> All = new List<CharacterLod>();
        private static readonly List<Renderer> Scratch = new List<Renderer>();
        private static Ticker _ticker;

        /// <summary>Wie reduzierte Figuren (Stufe 1) animiert werden – Messgroesse fuer den Massenschlacht-Test.</summary>
        public enum ReducedAnimation
        {
            /// <summary>Animator aus, vom Takt selbst mit halber/drittel Rate weitergerechnet (Hauptthread).</summary>
            Stepped,
            /// <summary>Animator laeuft normal (Unity verteilt die Arbeit auf alle Kerne), nur Transform-Kultur ausserhalb des Bildes.</summary>
            Native,
            /// <summary>Keine Animation (Messung: was kosten Zeichnen und Haut allein?).</summary>
            Frozen
        }

        // 30.09. Massenschlacht-Test: 1000 Figuren 26 → 37 Bilder/s, weil Unity die Animatoren auf alle Kerne verteilt
        public static ReducedAnimation ReducedMode = ReducedAnimation.Native;

        /// <summary>Stufe 1: alle Teile einer Figur zu einem Haut-Mesh zusammenfassen (Massenschlacht-Schritt 2).</summary>
        public static bool MergeReduced = true;

        /// <summary>Immer volle Qualitaet (eigene Figur).</summary>
        public bool alwaysFull;

        public int Level { get; private set; }
        public static int Count => All.Count;

        /// <summary>Kleinteile der Ausruestung zeigen? (EquipmentVisuals fragt das zusaetzlich zum Abstand ab.)</summary>
        public bool DetailsAllowed => Level == 0;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<ShadowCastingMode> _shadowModes = new List<ShadowCastingMode>();
        private readonly List<SkinnedMeshRenderer> _skins = new List<SkinnedMeshRenderer>();
        private readonly List<SkinQuality> _skinQuality = new List<SkinQuality>();
        private Animator[] _animators = System.Array.Empty<Animator>();
        private AnimatorCullingMode[] _cullModes = System.Array.Empty<AnimatorCullingMode>();
        private bool[] _animEnabled = System.Array.Empty<bool>();
        private AppearanceRig _rig;
        private bool _hasEquipmentVisuals;
        private int _childCount = -1;
        private float _sqrDistance;
        private float _nextScan;
        private PlayerCharacter _player;

        // Animation mit reduzierter Rate (Stufe 1)
        private int _animStep = 2;
        private int _animPhase;
        private float _animAccum;

        private void OnEnable()
        {
            All.Add(this);
            _player = GetComponent<PlayerCharacter>();
            _hasEquipmentVisuals = GetComponent<EquipmentVisuals>() != null;
            if (_ticker == null)
            {
                var go = new GameObject("Figuren-Detail (Takt)") { hideFlags = HideFlags.HideAndDontSave };
                _ticker = go.AddComponent<Ticker>();
            }
            _animPhase = All.Count;
            Level = 0;
        }

        private void OnDisable()
        {
            All.Remove(this);
            SetLevel(0, true);
        }

        private CharacterMerge _merge;
        private float _nextMergeCheck;

        public bool IsMerged => _merge != null && _merge.Active;

        private void ReleaseMerge()
        {
            if (_merge != null) _merge.Release();
        }

        private void UpdateMerge()
        {
            if (Level != 1 || !MergeReduced || _animators.Length == 0 || _animators[0] == null)
            {
                ReleaseMerge();
                return;
            }
            if (_merge == null) _merge = gameObject.AddComponent<CharacterMerge>();
            // Nicht sofort bauen: Ein Takt kann Hunderte Figuren gleichzeitig umstufen. Der Takt baut mit Zeitbudget.
            if (!_merge.Active && !_mergeQueued)
            {
                _mergeQueued = true;
                MergeQueue.Enqueue(this);
            }
        }

        private bool _mergeQueued;
        private static readonly Queue<CharacterLod> MergeQueue = new Queue<CharacterLod>();
        private static readonly System.Diagnostics.Stopwatch MergeWatch = new System.Diagnostics.Stopwatch();
        /// <summary>Zeitbudget je Bild fuer das Zusammenfassen (ms).</summary>
        public const double MergeBudgetMs = 1.5;

        private static void ProcessMergeQueue()
        {
            if (MergeQueue.Count == 0) return;
            MergeWatch.Restart();
            while (MergeQueue.Count > 0 && MergeWatch.Elapsed.TotalMilliseconds < MergeBudgetMs)
            {
                CharacterLod c = MergeQueue.Dequeue();
                if (c == null) continue;
                c._mergeQueued = false;
                if (!c.isActiveAndEnabled || c.Level != 1 || !MergeReduced || c._merge == null || c._merge.Active) continue;
                if (c._animators.Length > 0 && c._animators[0] != null && !c._merge.Build(c._animators[0], c._renderers))
                    c._nextMergeCheck = Time.unscaledTime + 10f;   // lohnt nicht / geht nicht: selten neu versuchen
            }
        }

        /// <summary>Figuren, die noch auf ihr Zusammenfassen warten (Tests).</summary>
        public static int MergePending => MergeQueue.Count;

        /// <summary>Renderer neu einsammeln, wenn sich die Ausruestung geaendert hat (Kinder hinzugekommen/entfernt).</summary>
        private void Rescan()
        {
            int children = CountChildren(transform);
            if (children == _childCount) return;
            ReleaseMerge();
            RestoreAll(); // vorher alles auf volle Qualitaet, damit die gemerkten Ausgangswerte stimmen
            _childCount = children;
            Scratch.Clear();
            GetComponentsInChildren(true, Scratch);
            _renderers.Clear();
            _shadowModes.Clear();
            _skins.Clear();
            _skinQuality.Clear();
            foreach (Renderer r in Scratch)
            {
                if (r == null || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (r.GetComponent<CharacterMerge.MergedMarker>() != null) continue;
                _renderers.Add(r);
                _shadowModes.Add(r.shadowCastingMode);
                if (r is SkinnedMeshRenderer smr)
                {
                    _skins.Add(smr);
                    _skinQuality.Add(smr.quality);
                }
            }
            _animators = GetComponentsInChildren<Animator>(true);
            _cullModes = new AnimatorCullingMode[_animators.Length];
            _animEnabled = new bool[_animators.Length];
            for (int i = 0; i < _animators.Length; i++)
            {
                _cullModes[i] = _animators[i].cullingMode;
                _animEnabled[i] = _animators[i].enabled;
            }
            _rig = GetComponentInChildren<AppearanceRig>(true);
            int keep = Level;
            Level = 0;
            SetLevel(keep, true);
        }

        private static int CountChildren(Transform t)
        {
            int n = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c.GetComponent<CharacterMerge.MergedMarker>() != null) continue;   // zusammengefasstes Netz zaehlt nicht
                n += 1 + CountChildren(c);
            }
            return n;
        }

        private void RestoreAll()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                r.forceRenderingOff = false;
                r.shadowCastingMode = _shadowModes[i];
            }
            for (int i = 0; i < _skins.Count; i++)
                if (_skins[i] != null) _skins[i].quality = _skinQuality[i];
            for (int i = 0; i < _animators.Length; i++)
            {
                if (_animators[i] == null) continue;
                _animators[i].cullingMode = _cullModes[i];
                _animators[i].enabled = _animEnabled[i];
            }
        }

        private void SetLevel(int level, bool force = false)
        {
            if (level == Level && !force) return;
            ReleaseMerge();
            Level = level;
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                r.forceRenderingOff = level >= 2;
                r.shadowCastingMode = level == 0 ? _shadowModes[i] : ShadowCastingMode.Off;
            }
            for (int i = 0; i < _skins.Count; i++)
                if (_skins[i] != null) _skins[i].quality = level == 0 ? _skinQuality[i] : SkinQuality.Bone2;
            // Stufe 1: Animator selbst takten (reduzierte Rate), Stufe 2: ganz anhalten
            for (int i = 0; i < _animators.Length; i++)
            {
                Animator a = _animators[i];
                if (a == null) continue;
                bool native = level == 1 && ReducedMode == ReducedAnimation.Native;
                a.cullingMode = level == 0 ? _cullModes[i] : native ? AnimatorCullingMode.CullUpdateTransforms : AnimatorCullingMode.CullCompletely;
                a.enabled = (level == 0 || native) && _animEnabled[i];
            }
            _animAccum = 0f;
            if (!_hasEquipmentVisuals && _rig != null) _rig.SetDetailVisible(level == 0);
            UpdateMerge();
        }

        /// <summary>Stufe 1: Animation alle <see cref="_animStep"/> Bilder mit der gesammelten Zeit weiterrechnen.</summary>
        private void StepAnimation(float dt, int frame)
        {
            _animAccum += dt;
            if ((frame + _animPhase) % _animStep != 0) return;
            bool visible = false;
            for (int i = 0; i < _skins.Count && !visible; i++) visible = _skins[i] != null && _skins[i].isVisible;
            if (visible)
                for (int i = 0; i < _animators.Length; i++)
                {
                    Animator a = _animators[i];
                    if (a != null && _animEnabled[i] && a.runtimeAnimatorController != null && a.gameObject.activeInHierarchy) a.Update(_animAccum);
                }
            _animAccum = 0f;
        }

        private bool IsOwnFigure => alwaysFull || (_player != null && _player.isActiveAndEnabled);

        // ================================================================== Gemeinsamer Takt

        /// <summary>Einmal pro Welt: sortiert alle Figuren, verteilt die Stufen und taktet die reduzierten Animationen.</summary>
        private sealed class Ticker : MonoBehaviour
        {
            private float _timer;
            private static readonly List<CharacterLod> Sorted = new List<CharacterLod>();

            private void Update()
            {
                _timer -= Time.unscaledDeltaTime;
                if (_timer <= 0f)
                {
                    _timer = 0.25f;
                    Tick();
                }
                ProcessMergeQueue();
                float dt = Time.deltaTime;
                int frame = Time.frameCount;
                for (int i = 0; i < All.Count; i++)
                {
                    CharacterLod c = All[i];
                    if (c != null && c.Level == 1 && ReducedMode == ReducedAnimation.Stepped) c.StepAnimation(dt, frame);
                }
            }

            private static void Tick()
            {
                Camera cam = Camera.main;
                if (cam == null || All.Count == 0) return;
                GraphicsOptions o = GraphicsSettingsStore.Current;
                int full = GraphicsCatalog.FullDetailFigures[o.figureDetail];
                float view = GraphicsCatalog.FigureViewDistance[o.figureDetail];
                float viewSqr = view * view;
                Vector3 camPos = cam.transform.position;

                Sorted.Clear();
                foreach (CharacterLod c in All)
                {
                    if (c == null) continue;
                    c._sqrDistance = (c.transform.position - camPos).sqrMagnitude;
                    Sorted.Add(c);
                }
                Sorted.Sort((a, b) => a._sqrDistance.CompareTo(b._sqrDistance));
                int rank = 0;
                foreach (CharacterLod c in Sorted)
                {
                    if (Time.unscaledTime >= c._nextScan)
                    {
                        c._nextScan = Time.unscaledTime + 2f + Random.value * 0.5f;
                        c.Rescan();
                    }
                    if (c.IsOwnFigure)
                    {
                        c.SetLevel(0);
                        continue;
                    }
                    int level = c._sqrDistance > viewSqr ? 2 : rank < full ? 0 : 1;
                    rank++;
                    c._animStep = c._sqrDistance > 40f * 40f ? 3 : 2;
                    c.SetLevel(level);
                    // Zusammengefasstes Netz: ab und zu pruefen, ob Teile ein-/ausgeblendet wurden (Helm verdeckt Haare …)
                    if (c.Level == 1 && MergeReduced && Time.unscaledTime >= c._nextMergeCheck)
                    {
                        c._nextMergeCheck = Time.unscaledTime + 1.5f + Random.value;
                        if (c._merge == null || !c._merge.Active || !c._merge.StillMatches(c._renderers)) c.UpdateMergeRebuild();
                    }
                }
            }
        }

        private void UpdateMergeRebuild()
        {
            ReleaseMerge();
            UpdateMerge();
        }

        /// <summary>Anzahl zusammengefasster Figuren (Tests).</summary>
        public static int MergedCount
        {
            get
            {
                int n = 0;
                foreach (CharacterLod c in All) if (c != null && c.IsMerged) n++;
                return n;
            }
        }

        /// <summary>Alle Figuren neu einstufen (nach Aenderung von <see cref="ReducedMode"/>).</summary>
        public static void ReapplyAll()
        {
            foreach (CharacterLod c in All)
                if (c != null) c.SetLevel(c.Level, true);
        }

        /// <summary>Anzahl Figuren je Stufe (fuer Tests und Leistungsanzeige).</summary>
        public static void Tally(out int full, out int reduced, out int hidden)
        {
            full = reduced = hidden = 0;
            foreach (CharacterLod c in All)
            {
                if (c == null) continue;
                if (c.Level == 0) full++;
                else if (c.Level == 1) reduced++;
                else hidden++;
            }
        }
    }
}
