using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Server: Gruppen (bis 5 Spieler) nach Profil-ID. Reine Logik ohne Netz – NetworkSocial ruft sie auf.
    /// Gruppen leben nur waehrend der Serversitzung (wie in klassischen MMOs nach einem Neustart).
    /// Wer die Verbindung verliert, bleibt Mitglied (offline) und ist nach dem Wiederverbinden wieder dabei.
    /// </summary>
    public static class PartyService
    {
        public const int MaxSize = 5;
        public const float InviteSeconds = 60f;
        /// <summary>Gruppenmitglieder in dieser Entfernung teilen Beuteberechtigung, Erfahrung und Toetungsziele.</summary>
        public const float ShareRange = 60f;

        public sealed class Party
        {
            public int Id;
            public string Leader;
            public readonly List<string> Members = new List<string>();
        }

        private struct PendingInvite
        {
            public string From;
            public float Expires;
        }

        private static readonly Dictionary<string, Party> ByProfile = new Dictionary<string, Party>();
        private static readonly Dictionary<string, PendingInvite> Invites = new Dictionary<string, PendingInvite>();
        private static int _nextId = 1;

        public static Party Of(string profile) =>
            profile != null && ByProfile.TryGetValue(profile, out Party p) ? p : null;

        public static IEnumerable<string> MembersOf(string profile)
        {
            Party p = Of(profile);
            if (p == null) yield break;
            foreach (string m in p.Members) if (m != profile) yield return m;
        }

        public static string PendingInviteFrom(string profile)
        {
            if (profile == null || !Invites.TryGetValue(profile, out PendingInvite i)) return null;
            if (Time.unscaledTime > i.Expires)
            {
                Invites.Remove(profile);
                return null;
            }
            return i.From;
        }

        /// <summary>null = Einladung verschickt, sonst Grund.</summary>
        public static string Invite(string from, string to)
        {
            if (from == to) return "Du kannst dich nicht selbst einladen.";
            Party mine = Of(from);
            if (mine != null && mine.Leader != from) return "Nur die Gruppenleitung kann einladen.";
            if (mine != null && mine.Members.Count >= MaxSize) return "Die Gruppe ist voll.";
            if (Of(to) != null) return "Der Spieler ist bereits in einer Gruppe.";
            if (PendingInviteFrom(to) != null) return "Der Spieler hat schon eine Einladung.";
            Invites[to] = new PendingInvite { From = from, Expires = Time.unscaledTime + InviteSeconds };
            return null;
        }

        /// <summary>Einladung annehmen. Gibt die (neue) Gruppe zurueck oder null mit Grund.</summary>
        public static Party Accept(string profile, out string error)
        {
            error = null;
            string from = PendingInviteFrom(profile);
            Invites.Remove(profile);
            if (from == null) { error = "Keine Einladung (mehr)."; return null; }
            if (Of(profile) != null) { error = "Du bist bereits in einer Gruppe."; return null; }
            Party p = Of(from);
            if (p == null)
            {
                p = new Party { Id = _nextId++, Leader = from };
                p.Members.Add(from);
                ByProfile[from] = p;
            }
            if (p.Members.Count >= MaxSize) { error = "Die Gruppe ist voll."; return null; }
            p.Members.Add(profile);
            ByProfile[profile] = p;
            return p;
        }

        public static void Decline(string profile) => Invites.Remove(profile);

        /// <summary>Verlassen (oder entfernt werden). Gibt die verbleibende Gruppe zurueck (null = aufgeloest).</summary>
        /// <param name="online">
        /// 21.10. (MEHRSPIELER-04): Optional – ist dieses Mitglied gerade online? Geht die Leitung, uebernimmt das aelteste
        /// Online-Mitglied (sonst bekam ein Offline-Mitglied die Leitung, und die uebrigen konnten weder einladen noch
        /// entfernen). Ohne Online-Mitglied oder ohne Angabe wie bisher das aelteste Mitglied.
        /// </param>
        public static Party Leave(string profile, System.Predicate<string> online = null)
        {
            Party p = Of(profile);
            if (p == null) return null;
            p.Members.Remove(profile);
            ByProfile.Remove(profile);
            if (p.Members.Count <= 1)
            {
                foreach (string m in p.Members) ByProfile.Remove(m);
                p.Members.Clear();
                return null;
            }
            if (p.Leader == profile)
            {
                // 21.10. (MEHRSPIELER-04): Leitung an ein Online-Mitglied. Geht die Leitung selbst offline, wechselt sie
                // weiterhin nicht (bisheriges Verhalten; ein Wechsel dort waere neue Mechanik und eine Entscheidung von Max).
                string next = online != null ? p.Members.Find(online) : null;
                p.Leader = next ?? p.Members[0];
            }
            return p;
        }

        /// <summary>Server-Neustart im Editor ohne Domain-Reload: alles vergessen.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            ByProfile.Clear();
            Invites.Clear();
            _nextId = 1;
        }
    }
}
