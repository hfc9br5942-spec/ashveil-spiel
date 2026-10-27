using System.Collections.Generic;
using Ashveil.Core;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Ashveil.Gameplay.Audio
{
    /// <summary>
    /// Klanglandschaft am Client (Claude 14.10.2026, TECH-125, Max: „gerne mit valve“): Wind überall, je <see cref="KlangGebiet"/>
    /// Vögel am Tag und Grillen in der Nacht (Weltuhr), dazu die nächsten <see cref="KlangQuelle"/>n der Welt.
    /// MMO-Regeln (CORE-005): nur am Client mit Grafik (Server ohne Klang, kein Netzverkehr); höchstens <see cref="maxQuellen"/>
    /// hörbare Weltquellen (die nächsten), Auswahl 4× je Sekunde, weiches Ein-/Ausblenden; 3D-Quellen räumlich über Steam Audio
    /// (spatialize, HRTF). Unter Dach/Fels (Strahl nach oben trifft) wird der Wind leiser und es singen keine Vögel.
    /// Nachhall (14.10.2026, Schritt 3): in einem <see cref="HoehlenVolumen"/> laufen die Weltquellen über die Mischpult-Gruppe
    /// „Hoehle“ (Send → Steam-Audio-Nachhall, vorberechnet), draußen über „Welt“ (trocken). Die gebackenen Sonden der Szenen
    /// (<see cref="NachhallWurzel"/>, im Editor AUS gespeichert) schaltet nur diese Komponente ein – also nur am Client.
    /// </summary>
    public sealed class KlangLandschaft : MonoBehaviour
    {
        public AudioClip windBett;
        [Range(0f, 1f)] public float windLaut = 0.45f;
        [Tooltip("Höchstzahl gleichzeitig hörbarer Weltquellen")] public int maxQuellen = 16;
        [Tooltip("Lautstärke der Umgebung (später Einstellungen)")] public static float Umgebung = 1f;
        [Header("Mischpult (Audio/Ashveil_Klang.mixer)")]
        public AudioMixerGroup weltGruppe;
        public AudioMixerGroup hoehleGruppe;
        public AudioMixerGroup bettGruppe;
        [Header("Schritte (10 Varianten je Boden)")]
        public AudioClip[] schrittGras, schrittKies, schrittFels, schrittMatsch, schrittHolz, schrittWasser;
        /// <summary>Diagnose/Tests: Schritte nach Boden.</summary>
        public SchrittKlang Schritte { get; private set; }

        public static KlangLandschaft Instanz { get; private set; }
        /// <summary>Diagnose/Tests: wie viele Weltquellen gerade spielen, und ob der Hörer gerade „draußen“ ist.</summary>
        public int Aktiv { get; private set; }
        public bool Draussen { get; private set; } = true;
        public string Gebiet { get; private set; } = "";
        /// <summary>Diagnose/Tests: steht der Hörer in einer Höhle (Nachhall an)?</summary>
        public bool InHoehle { get; private set; }
        /// <summary>Diagnose/Tests: eingeschaltete Nachhall-Wurzeln (gebackene Sonden).</summary>
        public static int NachhallWurzeln { get; private set; }

        private AudioSource _wind, _nacht, _vogel;
        private AudioSource[] _pool;
        private KlangQuelle[] _belegt;
        private float[] _ziel;
        private float _naechsteWahl, _naechsterVogel, _windZiel = 1f, _nachtZiel;
        private KlangGebiet _gebiet;
        private readonly List<KlangQuelle> _kandidaten = new List<KlangQuelle>();

        // Messpunkte für die Ruckler-Probe (CORE-005)
        private static readonly ProfilerMarker MKlang = new ProfilerMarker("Ashveil Klang");
        private static readonly ProfilerMarker MSteam = new ProfilerMarker("Ashveil Klang Steam-Hoerer");
        private static readonly ProfilerMarker MSchritt = new ProfilerMarker("Ashveil Klang Schritt");

        private static bool OhneKlang => Application.isBatchMode ||
                                        SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        private void Awake()
        {
            if (OhneKlang) { enabled = false; return; }
            Instanz = this;
            _wind = Bett("Wind", windBett, true);
            _nacht = Bett("Nacht", null, true);
            _vogel = Quelle("Vogel", true);
            _vogel.maxDistance = 120f;
            int n = Mathf.Clamp(maxQuellen, 4, 32);
            _pool = new AudioSource[n];
            _belegt = new KlangQuelle[n];
            _ziel = new float[n];
            for (int i = 0; i < n; i++) _pool[i] = Quelle("Welt " + i, true);
            _naechsterVogel = Time.time + Random.Range(2f, 6f);
            for (int i = 0; i < SceneManager.sceneCount; i++) NachhallEin(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
            SceneManager.sceneLoaded += NachhallEin;
            Schritte = new SchrittKlang(transform,
                new[] { schrittGras, schrittKies, schrittFels, schrittMatsch, schrittHolz, schrittWasser }, weltGruppe);
            Ashveil.Gameplay.Motion.Procedural.CharacterMotionPoser.Fussaufsatz += Fuss;
            foreach (AudioClip[] g in new[] { schrittGras, schrittKies, schrittFels, schrittMatsch, schrittHolz, schrittWasser })
                if (g != null) foreach (AudioClip c in g) Bereit(c);
        }

        private void Fuss(Ashveil.Gameplay.Motion.Procedural.CharacterMotionPoser wer, Vector3 fuss, float tempo)
        {
            AudioListener h = Hoerer();
            if (h == null || !enabled) return;
            using (MSchritt.Auto()) Schritte.Aufsatz(wer, fuss, tempo, h.transform.position, Umgebung);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= NachhallEin;
            Ashveil.Gameplay.Motion.Procedural.CharacterMotionPoser.Fussaufsatz -= Fuss;
            if (Instanz == this) Instanz = null;
        }

        /// <summary>Gebackene Nachhall-Sonden einer (Zellen-)Szene einschalten – nur hier, also nur am Client mit Klang.</summary>
        private static void NachhallEin(Scene s, LoadSceneMode m)
        {
            if (!s.isLoaded) return;
            foreach (GameObject r in s.GetRootGameObjects())
                foreach (NachhallWurzel w in r.GetComponentsInChildren<NachhallWurzel>(true))
                    if (!w.gameObject.activeSelf) { w.gameObject.SetActive(true); NachhallWurzeln++; }
        }

        /// <summary>Steam-Audio-Hörer am AudioListener: Nachhall aus den gebackenen Sonden (listener-centric).</summary>
        private void SteamHoerer(AudioListener h)
        {
            using var _ = MSteam.Auto();
            if (SteamAudio.SteamAudioManager.Singleton == null) return;
            var l = h.GetComponent<SteamAudio.SteamAudioListener>();
            if (l == null) l = h.gameObject.AddComponent<SteamAudio.SteamAudioListener>();
            l.reverbType = SteamAudio.ReverbType.Baked;
            if (!l.applyReverb) { l.applyReverb = true; l.enabled = false; l.enabled = true; }   // OnEnable meldet ihn erst mit applyReverb an
            SteamAudio.SteamAudioManager.NotifyAudioListenerChangedTo(h.transform);
            _steamHoerer = h;
        }
        private AudioListener _steamHoerer;

        private AudioSource Bett(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject("Klang " + name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f; s.volume = 0f; s.clip = clip; s.dopplerLevel = 0f;
            s.priority = 64;
            s.outputAudioMixerGroup = bettGruppe;
            if (clip != null) Bereit(clip);                 // gespielt wird erst, wenn geladen (Update)
            return s;
        }

        private AudioSource Quelle(string name, bool spatial)
        {
            var go = new GameObject("Klang " + name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 1f; s.dopplerLevel = 0f; s.volume = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.spatialize = spatial;                       // Steam Audio Spatializer (HRTF), siehe Projekteinstellungen › Audio
            s.priority = 128;
            s.outputAudioMixerGroup = weltGruppe;
            return s;
        }

        /// <summary>Quelle wird abgeschaltet/zerstört: ihren Platz im Pool freigeben.</summary>
        internal static void Freigeben(KlangQuelle q)
        {
            KlangLandschaft k = Instanz;
            if (k == null || q.Platz < 0 || k._belegt == null) return;
            int i = q.Platz;
            if (i < k._belegt.Length && k._belegt[i] == q) { k._belegt[i] = null; k._ziel[i] = 0f; k._pool[i].Stop(); }
            q.Platz = -1;
        }

        /// <summary>
        /// Ist der Klang geladen? Sonst im Hintergrund laden lassen und diesmal nicht spielen. (Messung 14.10. nachmittags: das
        /// erste Play eines noch nicht geladenen Klangs lud ihn im Hauptfaden – 136 ms Hänger nach dem Laden der Westweiden.)
        /// </summary>
        public static bool Bereit(AudioClip c)
        {
            if (c == null) return false;
            if (c.loadType == AudioClipLoadType.Streaming) return true;   // Strom: öffnet beim Abspielen, dekodiert im Audio-Faden
            AudioDataLoadState st = c.loadState;
            if (st == AudioDataLoadState.Loaded) return true;
            if (st == AudioDataLoadState.Unloaded) c.LoadAudioData();
            return false;
        }

        private static float Tag(float h)
        {
            // 0 nachts, 1 am Tag; Dämmerung 4:30–6:00 und 20:00–21:30
            float morgen = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4.5f, 6f, h));
            float abend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 21.5f, h));
            return Mathf.Min(morgen, abend);
        }

        private void Update()
        {
            using var _ = MKlang.Auto();
            AudioListener hoerer = Hoerer();
            if (hoerer == null) return;
            Vector3 hp = hoerer.transform.position;
            float tag = Tag(WorldClock.Hours);
            if (_steamHoerer != hoerer) SteamHoerer(hoerer);

            if (Time.time >= _naechsteWahl)
            {
                _naechsteWahl = Time.time + 0.25f;
                // draußen? Strahl 40 m nach oben (Dach, Fels, Höhle) – billig, 4× je Sekunde
                Draussen = !Physics.Raycast(hp + Vector3.up * 0.5f, Vector3.up, 40f, ~0, QueryTriggerInteraction.Ignore);
                _gebiet = null;
                foreach (KlangGebiet g in KlangGebiet.Alle) if (g != null && g.Enthaelt(hp)) { _gebiet = g; break; }
                Gebiet = _gebiet != null ? _gebiet.name : "";
                bool hoehle = HoehlenVolumen.Enthaelt(hp);
                if (hoehle != InHoehle)
                {
                    InHoehle = hoehle;
                    AudioMixerGroup g = hoehle && hoehleGruppe != null ? hoehleGruppe : weltGruppe;
                    for (int i = 0; i < _pool.Length; i++) _pool[i].outputAudioMixerGroup = g;
                    _vogel.outputAudioMixerGroup = weltGruppe;
                    Schritte?.Gruppe(g);
                }
                Waehlen(hp, tag);
            }

            // Betten
            _windZiel = (Draussen ? 1f : 0.3f) * windLaut;
            _wind.volume = Mathf.MoveTowards(_wind.volume, _windZiel * Umgebung, Time.deltaTime * 0.5f);
            if (!_wind.isPlaying && _wind.clip != null && Bereit(_wind.clip)) { _wind.time = Random.Range(0f, _wind.clip.length); _wind.Play(); }
            AudioClip nachtClip = _gebiet != null ? _gebiet.nachtBett : null;
            if (_nacht.clip != nachtClip)
            {
                if (_nacht.volume <= 0.01f && (nachtClip == null || Bereit(nachtClip)))
                {
                    _nacht.clip = nachtClip;
                    if (nachtClip != null) { _nacht.time = Random.Range(0f, nachtClip.length); _nacht.Play(); }
                }
                _nachtZiel = 0f;
            }
            else _nachtZiel = nachtClip != null ? (1f - tag) * _gebiet.nachtLaut * (Draussen ? 1f : 0.35f) : 0f;
            _nacht.volume = Mathf.MoveTowards(_nacht.volume, _nachtZiel * Umgebung, Time.deltaTime * 0.3f);

            // Vögel: am Tag, draußen, im Gebiet – irgendwo hoch über der Weide rings um den Hörer
            if (Time.time >= _naechsterVogel)
            {
                _naechsterVogel = Time.time + Random.Range(4f, 13f);
                AudioClip[] v = _gebiet != null ? _gebiet.tagEinzeln : null;
                AudioClip vc = v != null && v.Length > 0 ? v[Random.Range(0, v.Length)] : null;
                if (vc != null && Draussen && tag > 0.3f && !_vogel.isPlaying && Bereit(vc))
                {
                    Vector2 r = Random.insideUnitCircle.normalized * Random.Range(18f, 55f);
                    _vogel.transform.position = hp + new Vector3(r.x, Random.Range(14f, 35f), r.y);
                    _vogel.minDistance = 10f; _vogel.maxDistance = 120f;
                    _vogel.pitch = Random.Range(0.94f, 1.06f);
                    _vogel.clip = vc;
                    _vogel.volume = _gebiet.tagLaut * tag * Umgebung;
                    _vogel.Play();
                }
            }

            // Weltquellen: Einblenden/Ausblenden, Einzelklänge auslösen, bewegten Quellen folgen
            int aktiv = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                AudioSource s = _pool[i];
                KlangQuelle q = _belegt[i];
                if (q != null)
                {
                    s.transform.position = q.transform.position;
                    if (!q.schleife && Time.time >= q.Naechster && !s.isPlaying)
                    {
                        AudioClip ec = q.clips[Random.Range(0, q.clips.Length)];
                        if (Bereit(ec))
                        {
                            q.Naechster = Time.time + Random.Range(q.pause.x, q.pause.y);
                            s.pitch = q.grundTon * (1f + Random.Range(-q.tonhoehe, q.tonhoehe));
                            s.clip = ec;
                            s.Play();
                        }
                        else q.Naechster = Time.time + 0.5f;
                    }
                }
                float ziel = _ziel[i] * Umgebung;
                s.volume = Mathf.MoveTowards(s.volume, ziel, Time.deltaTime * 0.8f);
                if (q == null && s.volume <= 0.001f && s.isPlaying) s.Stop();
                if (s.isPlaying) aktiv++;
            }
            Aktiv = aktiv;
        }

        /// <summary>Die nächsten hörbaren Quellen bekommen einen Platz; ferne geben ihren Platz ab (ausblenden).</summary>
        private void Waehlen(Vector3 hp, float tag)
        {
            _kandidaten.Clear();
            foreach (KlangQuelle q in KlangQuelle.Alle)
            {
                if (q == null || q.clips == null || q.clips.Length == 0) continue;
                if (q.tageszeit == 1 && tag < 0.2f) continue;
                if (q.tageszeit == 2 && tag > 0.8f) continue;
                q.Abstand2 = (q.transform.position - hp).sqrMagnitude;
                if (q.Abstand2 > q.maxAbstand * q.maxAbstand) continue;
                _kandidaten.Add(q);
            }
            _kandidaten.Sort((a, b) => a.Abstand2.CompareTo(b.Abstand2));
            if (_kandidaten.Count > _pool.Length) _kandidaten.RemoveRange(_pool.Length, _kandidaten.Count - _pool.Length);

            // nicht mehr gewählte freigeben (ausblenden)
            for (int i = 0; i < _belegt.Length; i++)
            {
                KlangQuelle q = _belegt[i];
                if (q != null && !_kandidaten.Contains(q)) { q.Platz = -1; _belegt[i] = null; _ziel[i] = 0f; }
            }
            // neu gewählte belegen
            foreach (KlangQuelle q in _kandidaten)
            {
                if (q.Platz >= 0) { _ziel[q.Platz] = q.lautstaerke; continue; }
                if (q.schleife && !Bereit(q.clips[0])) continue;              // lädt im Hintergrund – nächste Auswahl
                int frei = -1;
                for (int i = 0; i < _belegt.Length; i++)
                    if (_belegt[i] == null && _pool[i].volume <= 0.001f) { frei = i; break; }
                if (frei < 0) break;                                          // alle Plätze noch im Ausblenden – nächstes Mal
                AudioSource s = _pool[frei];
                _belegt[frei] = q; q.Platz = frei; _ziel[frei] = q.lautstaerke;
                s.transform.position = q.transform.position;
                s.minDistance = q.minAbstand; s.maxDistance = q.maxAbstand;
                s.loop = q.schleife; s.pitch = q.grundTon;
                if (q.schleife)
                {
                    s.clip = q.clips[0];
                    s.volume = 0f;
                    s.time = Random.Range(0f, s.clip.length * 0.95f);       // nicht alle Quellen im Gleichschritt
                    s.Play();
                }
                else
                {
                    s.volume = q.lautstaerke * Umgebung;
                    q.Naechster = Time.time + Random.Range(0f, q.pause.y);
                }
            }
        }

        private static AudioListener _hoerer;
        private static AudioListener Hoerer()
        {
            if (_hoerer != null && _hoerer.isActiveAndEnabled) return _hoerer;
            _hoerer = FindAnyObjectByType<AudioListener>();
            return _hoerer;
        }
    }
}
