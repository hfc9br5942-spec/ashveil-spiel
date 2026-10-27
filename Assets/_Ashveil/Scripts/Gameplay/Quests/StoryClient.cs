using System;
using System.Collections.Generic;
using Ashveil.Data;

namespace Ashveil.Gameplay.Quests
{
    /// <summary>
    /// Wie der Pferdehof fuer DIESEN Spieler aussieht (der Server leitet es aus Volk, Klasse, Stufe und Geschichtsstand ab).
    /// Andere Spieler sehen ihren eigenen Hof – so steht nie ein „toter Farmer“ fuer jemanden herum (Phasing).
    /// </summary>
    public enum FarmPhase : byte
    {
        /// <summary>Alltag: Herde auf der Weide, Zaun heil, der Zuechter arbeitet (fuer alle, die die Geschichte nicht betrifft).</summary>
        Normal = 0,
        /// <summary>„Wo die Herde floh“ laeuft oder steht bevor: Koppel leer, Westzaun eingedrueckt, Farmer in Panik.</summary>
        Crisis = 1,
        /// <summary>Die Herde ist heimgekehrt, Abschlussszene/Abgabe steht an.</summary>
        Return = 2,
        /// <summary>Nach Q1: Fohlen bei der Stute auf der Weide, Zaun geflickt.</summary>
        Growing = 3,
        /// <summary>„Der letzte Weidegang“: der Hof ist gealtert, das Pferd erwachsen, der Zuechter muede.</summary>
        Aged = 4,
        /// <summary>Nach dem Weidegang: Tor offen, der Zuechter ist im Haus.</summary>
        After = 5,
        /// <summary>Epilog: stiller Hof, Grab unter dem Baum.</summary>
        Epilogue = 6
    }

    /// <summary>
    /// Eigener Geschichtsstand, wie der Server ihn schickt (Client, nur der Besitzer): Volk, Geschichtsmarken, welcher
    /// Geschichtsauftrag gerade angeboten wird und der Stand der Pferde-Geschichte. Die Welt-Darstellung (Farmer, Fohlen auf
    /// der Weide, gealterter Hof, Hinweise im Wald) liest nur von hier – sie ist lokal und fuer jeden Spieler eigen (Phasing).
    /// Aktionen sind Bitten an den Server (<see cref="Request"/>).
    /// </summary>
    public static class StoryClient
    {
        private static readonly HashSet<string> Flags = new HashSet<string>();

        public static bool Ready { get; private set; }
        public static string Race { get; private set; } = RaceCatalog.HumanId;
        /// <summary>Geschichtsauftrag, den eine Szene gerade anbietet (leer = keiner).</summary>
        public static string StoryOffer { get; private set; } = "";
        public static HorseStoryState HorseState { get; private set; }
        public static int HorseStep { get; private set; }
        public static float HerdProgress { get; private set; }
        public static bool Epilogue { get; private set; }
        public static FarmPhase Farm { get; private set; }
        public static int Revision { get; private set; }

        public static event Action Changed;
        /// <summary>Server spielt eine Szene ab (Szenen-Kennung).</summary>
        public static event Action<string> SceneRequested;

        /// <summary>Bitte an den Server: (Aktion, Argument) – z. B. ("approach", ""), ("free_foal", ""), ("open_gate", ""), ("hand", "").</summary>
        public static Action<string, string> Request;

        public static bool HasFlag(string f) => !string.IsNullOrEmpty(f) && Flags.Contains(f);

        public static void Apply(string race, string flags, string offer, int horseState, int step, float herd, bool epilogue, byte farmPhase)
        {
            Farm = (FarmPhase)farmPhase;
            Race = RaceCatalog.Normalize(race);
            Flags.Clear();
            if (!string.IsNullOrEmpty(flags))
                foreach (string f in flags.Split(','))
                    if (f.Length > 0) Flags.Add(f);
            StoryOffer = offer ?? "";
            HorseState = (HorseStoryState)horseState;
            HorseStep = step;
            HerdProgress = herd;
            Epilogue = epilogue;
            Ready = true;
            Revision++;
            Changed?.Invoke();
        }

        public static void RaiseScene(string sceneId) => SceneRequested?.Invoke(sceneId);

        public static void Send(string action, string arg = "") => Request?.Invoke(action, arg ?? "");

        public static void Reset()
        {
            Flags.Clear();
            Race = RaceCatalog.HumanId;
            StoryOffer = "";
            HorseState = HorseStoryState.Unknown;
            HorseStep = 0;
            HerdProgress = 0f;
            Epilogue = false;
            Farm = FarmPhase.Normal;
            Ready = false;
            Revision++;
            Changed?.Invoke();
        }
    }
}
