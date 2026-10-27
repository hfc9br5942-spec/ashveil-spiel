using Ashveil.Core;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Colosseum
{
    /// <summary>
    /// Kolosseum Pass 4: was der eigene Client vom laufenden Kampf weiss (Uhr, Block, Wiederbelebungen, Champion,
    /// Countdown der Glocke, am Boden, Wiederbeleben-Leiste, Truhe). Der Server schickt nur Signale bei Aenderungen
    /// (<see cref="EncounterCues"/>) – die Uhr rechnet jeder Rechner selbst weiter. Das HUD liest hier.
    /// </summary>
    public static class ColosseumClient
    {
        public const string CueClock = "arena.clock";         // Wert: vergangene Laufzeit (s), Lauf laeuft
        public const string CueScale = "arena.scale";         // Wert: Zeitraffer (Tests), 1 = Echtzeit
        public const string CuePhase = "arena.phase";         // Wert: Block-Index
        public const string CueRevives = "arena.revives";     // Wert: Vorrat
        public const string CueCountdown = "arena.countdown"; // Wert: Sekunden bis zum Start (0 = keiner)
        public const string CueBoss = "arena.boss";           // Wert: 1 = Champion im Ring, 0 = keiner
        public const string CueDown = "arena.down";           // Wert: 1 = eigene Figur am Boden
        public const string CueChannel = "arena.channel";     // Wert: Dauer der Wiederbelebung (s), 0 = abgebrochen/fertig
        public const string CueEnd = "arena.end";             // Wert: 1 = bestanden, 2 = besiegt, 0 = Anzeige aus
        public const string CueChest = "arena.chest";         // Wert: 1 = Truhe wartet auf mich, 0 = geleert
        public const string CueBell = "arena.bell";           // Wert: 1 = Glocke schlaegt

        /// <summary>Definition (vom Szenen-Direktor gesetzt) fuer Blocknamen.</summary>
        public static ColosseumDefinition Definition { get; set; }

        public static bool InRun { get; private set; }
        public static bool Visible { get; private set; }
        public static int Phase { get; private set; }
        public static int Revives { get; private set; }
        public static bool BossAlive { get; private set; }
        public static bool Downed { get; private set; }
        public static bool ChestWaiting { get; private set; }
        public static int Result { get; private set; }
        public static float TimeScale { get; private set; } = 1f;
        private static float _clockBase, _clockAt = -1f, _countdownEnd = -1f, _channelStart = -1f, _channelLen;
        private static float _stoppedAt = -1f;

        public static float Elapsed =>
            _clockAt < 0f ? 0f : InRun ? _clockBase + (Time.time - _clockAt) * TimeScale : _stoppedAt;

        public static float CountdownLeft => _countdownEnd < 0f ? 0f : Mathf.Max(0f, _countdownEnd - Time.time);
        /// <summary>0..1 Fortschritt der eigenen Wiederbelebung (kleiner 0 = keine).</summary>
        public static float ChannelProgress => _channelStart < 0f ? -1f : Mathf.Clamp01((Time.time - _channelStart) / Mathf.Max(0.1f, _channelLen));

        public static string PhaseName =>
            Definition != null && Phase >= 0 && Phase < Definition.phases.Count ? Definition.phases[Phase].displayName : "Prüfung";

        /// <summary>Zaehler fuer Anzeigen/Tests (jede Aenderung +1).</summary>
        public static int Revision { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            Clear();
            EncounterCues.Received -= OnCue;
            EncounterCues.Received += OnCue;
        }

        private static void Clear()
        {
            InRun = false;
            Visible = false;
            Phase = 0;
            Revives = 0;
            BossAlive = false;
            Downed = false;
            ChestWaiting = false;
            Result = 0;
            TimeScale = 1f;
            _clockAt = -1f;
            _countdownEnd = -1f;
            _channelStart = -1f;
            _stoppedAt = -1f;
            Revision++;
        }

        private static void OnCue(Vector3 where, string cue, float value)
        {
            if (cue == null) return;
            // 22.10. (INSTANZEN-04): „reset“ der Begegnungen nicht mehr auswerten. Auf dem Host loest EnemyEncounter.ServerReset es
            // lokal aus (gleicher Prozess) – jedes Belegen/Zuruecksetzen eines Salzgruben-Platzes loeschte so mitten im Lauf die
            // eigene Ring-Anzeige. Die Anzeige hat ihr eigenes Ende-Signal: CueEnd 0 beim Betreten (ColosseumInstanceHandler.OnEnter),
            // beim Verlassen und nach einem Wipe.
            if (cue == EncounterCues.Reset) return;
            if (!cue.StartsWith("arena.")) return;
            switch (cue)
            {
                case CueScale: TimeScale = Mathf.Max(0.01f, value); break;
                case CueClock:
                    Visible = true;
                    InRun = true;
                    Result = 0;
                    _countdownEnd = -1f;
                    _clockBase = value;
                    _clockAt = Time.time;
                    break;
                case CuePhase: Visible = true; Phase = Mathf.RoundToInt(value); break;
                case CueRevives: Visible = true; Revives = Mathf.RoundToInt(value); break;
                case CueCountdown:
                    Visible = true;
                    _countdownEnd = value > 0f ? Time.time + value : -1f;
                    break;
                case CueBoss: BossAlive = value > 0.5f; break;
                case CueDown: Downed = value > 0.5f; break;
                case CueChannel:
                    if (value > 0f) { _channelStart = Time.time; _channelLen = value; }
                    else _channelStart = -1f;
                    break;
                case CueChest: ChestWaiting = value > 0.5f; break;
                case CueEnd:
                    int r = Mathf.RoundToInt(value);
                    if (r == 0) { Clear(); return; }
                    _stoppedAt = Elapsed;
                    InRun = false;
                    Result = r;
                    BossAlive = false;
                    _channelStart = -1f;
                    break;
            }
            Revision++;
        }

        /// <summary>Kurzfassung fuer Selbsttests.</summary>
        public static string Describe() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "visible={0};run={1};clock={2:0.0};phase={3};revives={4};boss={5};down={6};channel={7:0.00};chest={8};result={9};countdown={10:0.0}",
            Visible ? 1 : 0, InRun ? 1 : 0, Elapsed, Phase, Revives, BossAlive ? 1 : 0, Downed ? 1 : 0, ChannelProgress, ChestWaiting ? 1 : 0, Result, CountdownLeft);
    }
}
