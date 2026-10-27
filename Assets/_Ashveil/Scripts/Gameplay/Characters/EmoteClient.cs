using System;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>Was die Oberflaeche am eigenen Emote tun darf (nur Bitten – der Server entscheidet).</summary>
    public interface IEmoteService
    {
        EmoteId Current { get; }
        void Request(EmoteId id);
        void RequestStop();
    }

    /// <summary>M8: Bruecke fuer Chatbefehle (/winken …), Emote-Menue (J) und Actionbar zum eigenen Spieler.</summary>
    public static class EmoteClient
    {
        public static IEmoteService Current { get; private set; }
        public static string LastMessage { get; private set; } = "";
        public static event Action<string> Message;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            LastMessage = "";
            Message = null;
        }

        public static void Register(IEmoteService s) => Current = s;

        public static void Unregister(IEmoteService s)
        {
            if (Current == s) Current = null;
        }

        /// <summary>Emote abspielen; dasselbe Dauer-Emote nochmal = beenden.</summary>
        public static bool Play(EmoteId id)
        {
            if (Current == null || id == EmoteId.None) return false;
            if (Current.Current == id && EmoteCatalog.TryGet(id, out EmoteInfo info) && info.Loop) { Current.RequestStop(); return true; }
            Current.Request(id);
            return true;
        }

        public static void Stop() => Current?.RequestStop();

        /// <summary>Server lehnt ab o. ae. – Text fuer den Chat.</summary>
        public static void RaiseMessage(string text)
        {
            LastMessage = text ?? "";
            Message?.Invoke(LastMessage);
        }
    }
}
