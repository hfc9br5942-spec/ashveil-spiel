using System;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>Was der Charakter-Editor dem Server schicken darf (nur Bitten – der Server entscheidet).</summary>
    public interface ICharacterCreationService
    {
        void Submit(string name, CharacterAppearanceData look, string classId);
    }

    /// <summary>
    /// M7: Bruecke zwischen Netz und Charakter-Editor-Oberflaeche. Der Server meldet "dieses Profil hat noch keinen
    /// Charakter" (<see cref="Needed"/>), die Oberflaeche schickt Name + Aussehen und bekommt eine Antwort
    /// (<see cref="Result"/>: ok + vereinheitlichter Name, oder die Fehlermeldung).
    /// </summary>
    public static class CharacterCreationClient
    {
        public static bool Pending { get; private set; }
        public static bool Waiting { get; private set; }
        public static string SuggestedName { get; private set; } = "";
        public static string LastMessage { get; private set; } = "";
        public static bool LastOk { get; private set; }
        public static int ResultCount { get; private set; }
        public static ICharacterCreationService Service { get; private set; }

        public static event Action Needed;
        public static event Action<bool, string> Result;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pending = Waiting = LastOk = false;
            SuggestedName = LastMessage = "";
            ResultCount = 0;
            Service = null;
            Needed = null;
            Result = null;
        }

        public static void RaiseNeeded(ICharacterCreationService service, string suggestedName)
        {
            Service = service;
            SuggestedName = suggestedName ?? "";
            Pending = true;
            Waiting = false;
            Needed?.Invoke();
        }

        /// <summary>Charakter erstellen lassen. false = gerade nichts zu erstellen oder Antwort steht noch aus.</summary>
        public static bool Submit(string name, CharacterAppearanceData look, string classId = null)
        {
            if (!Pending || Waiting || Service == null || look == null) return false;
            Waiting = true;
            Service.Submit(name, look, classId ?? "");
            return true;
        }

        public static void RaiseResult(bool ok, string message)
        {
            Waiting = false;
            LastOk = ok;
            LastMessage = message ?? "";
            ResultCount++;
            if (ok) Pending = false;
            Result?.Invoke(ok, LastMessage);
        }
    }
}

namespace Ashveil.Gameplay.Characters
{
    /// <summary>M7: Name des eigenen Charakters (vom Server) fuer die Oberflaeche.</summary>
    public static class LocalPlayerInfo
    {
        public static string Name { get; set; } = "";

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Name = "";
    }
}
