using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Social
{
    public enum ChatChannel : byte
    {
        /// <summary>Umgebung: alle Spieler in Rufweite</summary>
        Say = 0,
        /// <summary>Gruppe</summary>
        Party = 1,
        /// <summary>Fluestern an einen Spieler</summary>
        Whisper = 2,
        /// <summary>Eigenes Gefluestertes (Kopie an den Absender)</summary>
        WhisperSent = 3,
        /// <summary>Hinweise des Spiels (Gruppe beigetreten, Fehler ...)</summary>
        System = 4
    }

    public struct ChatLine
    {
        public ChatChannel Channel;
        public string From;
        public string Text;
        public float Time;
    }

    /// <summary>Gruppenmitglied, wie der Client es sieht (Stand vom Server, 4 mal pro Sekunde).</summary>
    public sealed class PartyMemberView
    {
        public string Name;
        public int Level;
        public string ClassName;
        public float Health;   // 0..1
        public bool Alive;
        public bool Online;
        public bool Leader;
        public bool Self;
    }

    /// <summary>Was die Oberflaeche ueber Gruppe und Chat wissen und ausloesen darf.</summary>
    public interface ISocialService
    {
        bool Ready { get; }
        string MyName { get; }
        IReadOnlyList<PartyMemberView> Party { get; }
        IReadOnlyList<ChatLine> Chat { get; }
        /// <summary>Offene Einladung (Name des Einladenden) oder null.</summary>
        string PendingInvite { get; }

        void Say(ChatChannel channel, string target, string text);
        void Invite(string name);
        void RespondInvite(bool accept);
        void LeaveParty();
        void Kick(string name);
    }

    public static class SocialClient
    {
        public static ISocialService Current { get; private set; }

        /// <summary>Gruppe, Chat oder Einladung haben sich geaendert.</summary>
        public static event Action Changed;
        /// <summary>Rechtsklick auf einen anderen Spieler (Name, Bildschirmposition).</summary>
        public static event Action<string, Vector2> PlayerClicked;

        public static void Register(ISocialService s)
        {
            Current = s;
            Changed?.Invoke();
        }

        public static void Unregister(ISocialService s)
        {
            if (Current != s) return;
            Current = null;
            Changed?.Invoke();
        }

        public static void RaiseChanged() => Changed?.Invoke();
        public static void RaisePlayerClicked(string name, Vector2 screen) => PlayerClicked?.Invoke(name, screen);

        /// <summary>Chattext entschaerfen: keine Formatierungs-Tags, keine Steuerzeichen, begrenzte Laenge.</summary>
        public static string Clean(string text, int max = 200)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder(Math.Min(text.Length, max));
            foreach (char c in text)
            {
                if (sb.Length >= max) break;
                if (char.IsControl(c)) continue;
                sb.Append(c == '<' ? '(' : c == '>' ? ')' : c); // keine Richtext-Tags
            }
            return sb.ToString().Trim();
        }
    }
}
