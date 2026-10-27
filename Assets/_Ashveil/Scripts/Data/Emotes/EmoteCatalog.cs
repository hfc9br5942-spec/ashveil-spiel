using System;

namespace Ashveil.Data
{
    /// <summary>M8: die Standard-Emotes. Spieler und NPCs nutzen dieselben (Darstellung: <c>EmotePoser</c>).</summary>
    public enum EmoteId : byte { None = 0, Wave = 1, Fistbump = 2, Cheer = 3, Laugh = 4, Sit = 5, Dance = 6 }

    public readonly struct EmoteInfo
    {
        public readonly EmoteId Id;
        public readonly string Key;        // stabile ID fuer Actionbar/Speicher: "emote.wave"
        public readonly string Name;       // Anzeige
        public readonly string Command;    // Chat ohne "/": "winken"
        public readonly bool Loop;         // laeuft bis Bewegung/Kampf/anderes Emote/Abbruch
        public readonly float Duration;    // Einmal-Emotes: Laenge in Sekunden
        public readonly string Description;

        public EmoteInfo(EmoteId id, string key, string name, string command, bool loop, float duration, string description)
        {
            Id = id; Key = key; Name = name; Command = command; Loop = loop; Duration = duration; Description = description;
        }
    }

    /// <summary>
    /// Alle Emotes an einer Stelle: Chatbefehle (/winken …), Actionbar-IDs ("emote.wave"), Dauer, Schleife.
    /// Neue Emotes: Eintrag hier + Darstellung in <c>EmotePoser</c>.
    /// </summary>
    public static class EmoteCatalog
    {
        public const string KeyPrefix = "emote.";

        public static readonly EmoteInfo[] All =
        {
            new EmoteInfo(EmoteId.Wave, "emote.wave", "Winken", "winken", false, 2.6f, "Eine klare Begrüßung."),
            new EmoteInfo(EmoteId.Fistbump, "emote.fistbump", "Fistbump", "fistbump", false, 1.8f, "Faust nach vorn – wer steht gegenüber?"),
            new EmoteInfo(EmoteId.Cheer, "emote.cheer", "Jubeln", "jubeln", false, 2.4f, "Arme hoch, kurzer Freudensprung."),
            new EmoteInfo(EmoteId.Laugh, "emote.laugh", "Lachen", "lachen", false, 3.0f, "Lachen mit dem ganzen Körper."),
            new EmoteInfo(EmoteId.Sit, "emote.sit", "Sitzen", "sitzen", true, 0f, "Hinsetzen. Bewegung beendet es."),
            new EmoteInfo(EmoteId.Dance, "emote.dance", "Tanzen", "tanzen", true, 0f, "Retro-Pop-Tanz mit Rückwärtsgleiten – endlos, bis du dich bewegst.")
        };

        public static bool TryGet(EmoteId id, out EmoteInfo info)
        {
            foreach (EmoteInfo e in All) if (e.Id == id) { info = e; return true; }
            info = default;
            return false;
        }

        public static bool TryGetByKey(string key, out EmoteInfo info)
        {
            foreach (EmoteInfo e in All) if (string.Equals(e.Key, key, StringComparison.Ordinal)) { info = e; return true; }
            info = default;
            return false;
        }

        /// <summary>"/winken", "winken", "/wave" → Emote.</summary>
        public static bool TryGetByCommand(string command, out EmoteInfo info)
        {
            string c = (command ?? "").Trim().TrimStart('/').ToLowerInvariant();
            foreach (EmoteInfo e in All)
                if (c == e.Command || c == e.Key.Substring(KeyPrefix.Length)) { info = e; return true; }
            if (c == "tanz" || c == "dance") return TryGet(EmoteId.Dance, out info);
            if (c == "sit" || c == "setzen") return TryGet(EmoteId.Sit, out info);
            info = default;
            return false;
        }

        public static string CommandList()
        {
            var parts = new string[All.Length];
            for (int i = 0; i < All.Length; i++) parts[i] = "/" + All[i].Command;
            return string.Join(", ", parts);
        }
    }
}
