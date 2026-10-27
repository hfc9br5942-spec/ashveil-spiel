using System;
using System.Collections.Generic;

namespace Ashveil.Data
{
    /// <summary>
    /// M7: Regeln fuer Charakternamen – Client (sofortige Rueckmeldung) und Server (entscheidet) nutzen dieselben.
    /// 3–16 Buchstaben (auch Umlaute), keine Ziffern/Leerzeichen, nicht dreimal derselbe Buchstabe hintereinander,
    /// keine reservierten Woerter. Schreibweise wird vereinheitlicht: erster Buchstabe gross, Rest klein.
    /// Ob der Name schon vergeben ist, prueft nur der Server.
    /// </summary>
    public static class CharacterNameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        private static readonly string[] Reserved =
        {
            "admin", "administrator", "gm", "gamemaster", "moderator", "mod", "support", "system", "server", "ashveil",
            "anthropic", "claude", "niemand", "spieler", "wanderer", "test"
        };

        /// <summary>"  aRWEN " → "Arwen".</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string t = raw.Trim();
            if (t.Length == 0) return "";
            return char.ToUpperInvariant(t[0]) + t.Substring(1).ToLowerInvariant();
        }

        /// <summary>null = gueltig, sonst die Meldung fuer den Spieler.</summary>
        public static string Validate(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Bitte gib einen Namen ein.";
            if (name.Length < MinLength) return $"Der Name braucht mindestens {MinLength} Buchstaben.";
            if (name.Length > MaxLength) return $"Der Name darf höchstens {MaxLength} Buchstaben haben.";
            int run = 1;
            for (int i = 0; i < name.Length; i++)
            {
                if (!char.IsLetter(name[i])) return "Nur Buchstaben – keine Ziffern, Leer- oder Sonderzeichen.";
                if (i > 0) run = char.ToLowerInvariant(name[i]) == char.ToLowerInvariant(name[i - 1]) ? run + 1 : 1;
                if (run >= 3) return "Nicht dreimal derselbe Buchstabe hintereinander.";
            }
            string low = name.ToLowerInvariant();
            foreach (string r in Reserved)
                if (low == r || (r.Length >= 5 && low.Contains(r))) return "Dieser Name ist reserviert.";
            return null;
        }

        // ------------------------------------------------------------------ Vorschlaege (Wuerfel)

        private static readonly string[] MaleStart = { "Al", "Bran", "Cor", "Dar", "Ed", "Fen", "Gar", "Hal", "Ing", "Jor", "Kel", "Lor", "Mar", "Nor", "Os", "Rag", "Sev", "Tor", "Ulf", "Var", "Wen" };
        private static readonly string[] MaleEnd = { "dric", "ric", "mar", "dor", "gar", "rin", "mund", "wald", "bert", "vin", "helm", "rek", "son", "an" };
        private static readonly string[] FemaleStart = { "A", "Bri", "Ce", "Da", "El", "Fre", "Gun", "Hel", "Il", "Ja", "Ka", "Li", "Ma", "Ne", "Ro", "Se", "Tha", "Ul", "Ve", "Wy" };
        private static readonly string[] FemaleEnd = { "wen", "la", "ra", "lind", "na", "dis", "run", "ja", "wyn", "gard", "ne", "ria", "the", "va" };

        /// <summary>Zufaelliger, gueltiger Name passend zum Koerper.</summary>
        public static string Random(bool female, Random rng)
        {
            string[] a = female ? FemaleStart : MaleStart;
            string[] b = female ? FemaleEnd : MaleEnd;
            for (int tries = 0; tries < 20; tries++)
            {
                string n = Normalize(a[rng.Next(a.Length)] + b[rng.Next(b.Length)]);
                if (Validate(n) == null) return n;
            }
            return female ? "Elwen" : "Bralgar";
        }

        public static IReadOnlyList<string> ReservedWords => Reserved;
    }
}
