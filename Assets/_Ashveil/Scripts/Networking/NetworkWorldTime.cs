using System;
using System.Collections.Generic;
using Ashveil.Core;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// M8: Tageszeit im Netz. Die Uhr laeuft auf jedem Rechner selbst (<see cref="WorldClock"/> ueber den gemeinsamen
    /// FishNet-Takt); der Server schickt nur beim Anmelden und beim Verstellen Bezugsstunde, Bezugszeit und Tempo.
    /// Echtzeit (NET-020, Max 06.10.2026): Der Server stellt die Uhr beim Start aus seiner Ortszeit und dem Versatz aus
    /// <see cref="WorldTimeSettings"/>; danach laeuft sie 1:1 mit der echten Zeit.
    /// Verstellen ("/zeit 22"): der Host im Editor und im Development-Build sowie Test-Profile (Entwicklerwerkzeug).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkWorldTime : NetworkBehaviour
    {
        private static readonly List<NetworkWorldTime> All = new List<NetworkWorldTime>();
        private static bool _serverClockStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            All.Clear();
            _serverClockStarted = false;
        }

        /// <summary>Anzahl empfangener Uhr-Meldungen (Tests).</summary>
        public int ClientSyncs { get; private set; }

        private static double NetNow()
        {
            var tm = InstanceFinder.TimeManager;
            return tm != null ? tm.Tick * tm.TickDelta : Time.timeAsDouble;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            WorldClock.TimeSource = NetNow;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            All.Add(this);
            if (!_serverClockStarted)
            {
                _serverClockStarted = true;
                WorldTimeStart start = WorldTimeSettings.Requested(out string quelle);
                DateTime jetzt = DateTime.Now;
                WorldClock.Set(start.TotalHoursAt(jetzt), NetNow(), WorldClock.DefaultSecondsPerHour);
                Debug.Log($"[Ashveil] Weltzeit: {start} ({quelle}) – Serverzeit {jetzt:HH:mm}, Welt {WorldClock.Text} Uhr, Tag {WorldClock.Day} " +
                          $"(1 Stunde = {WorldClock.SecondsPerHour / 60.0:0.#} Minuten).");
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            All.Remove(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
            {
                WorldClock.SetRequest = RequestHour;
                if (!IsServerInitialized) ServerRequestClock();
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner && WorldClock.SetRequest == (System.Action<float>)RequestHour) WorldClock.SetRequest = null;
        }

        [ServerRpc]
        private void ServerRequestClock() => SendClock(Owner);

        private void SendClock(NetworkConnection conn)
        {
            if (conn == null || !conn.IsActive) return;
            TargetClock(conn, WorldClock.HourAtRef, WorldClock.RefTime, WorldClock.SecondsPerHour);
        }

        [TargetRpc]
        private void TargetClock(NetworkConnection conn, double hourAtRef, double refTime, double secondsPerHour)
        {
            ClientSyncs++;
            if (IsServerInitialized) return; // Host hat die Uhr schon
            WorldClock.Set(hourAtRef, refTime, secondsPerHour);
        }

        // ================================================================== Verstellen (Entwicklerwerkzeug)

        public void RequestHour(float hour)
        {
            if (IsClientInitialized && IsOwner) ServerSetHour(hour);
        }

        [ServerRpc]
        private void ServerSetHour(float hour)
        {
            var inv = GetComponent<NetworkInventory>();
            // Host im Editor oder Development-Build (Spieltest, NET-020) – in fertigen Builds nur Test-Profile
            bool allowed = (Owner != null && Owner.IsLocalClient && Debug.isDebugBuild) ||
                           (inv != null && inv.ProfileId != null && TestRechte.Erlaubt(inv.ProfileId));
            if (!allowed)
            {
                if (inv != null && inv.ProfileId != null) NetworkSocial.ServerSystemMessage(inv.ProfileId, "Die Zeit lässt sich nicht verstellen.");
                return;
            }
            ServerJumpTo(hour);
            if (inv != null && inv.ProfileId != null) NetworkSocial.ServerSystemMessage(inv.ProfileId, $"Die Zeit ist jetzt {WorldClock.Text} Uhr.");
        }

        /// <summary>
        /// Nur Spiel-Tests: Tempo der Uhr ändern (Zeitraffer), die Uhrzeit bleibt; allen Spielern melden. Für Suiten, deren
        /// Prüfungen in Spielminuten je echter Sekunde gedacht sind (Lagerleben, Himmel). Das Spiel selbst läuft in Echtzeit.
        /// </summary>
        public static void ServerSetTempo(double secondsPerHour)
        {
            WorldClock.Set(WorldClock.TotalHours, WorldClock.Now, secondsPerHour);
            foreach (NetworkWorldTime t in All) if (t != null) t.SendClock(t.Owner);
            Debug.Log($"[Ashveil] Tageszeit-Tempo: 1 Spielstunde = {WorldClock.SecondsPerHour / 60.0:0.#} Minuten");
        }

        /// <summary>Server: Uhr stellen und allen Spielern melden.</summary>
        public static void ServerJumpTo(float hour)
        {
            WorldClock.JumpTo(hour);
            foreach (NetworkWorldTime t in All) if (t != null) t.SendClock(t.Owner);
            Debug.Log("[Ashveil] Tageszeit gestellt: " + WorldClock.Text);
        }
    }

    /// <summary>
    /// Woher der Server die Startzeit der Weltuhr nimmt (NET-020): zuerst Startparameter „-ashveil-zeitversatz &lt;Wert&gt;“
    /// (Spieltest r2: „09:00“, Max 06.10.: Vormittag), im Editor sonst die Einstellung „Ashveil › Welt › Weltzeit im Editor“
    /// (Standard 15:30 = gestaltetes Licht, damit Bilder und Tests gleich bleiben), sonst Echtzeit.
    /// </summary>
    public static class WorldTimeSettings
    {
        public const string Arg = "-ashveil-zeitversatz";
        public const string EditorPrefKey = "Ashveil.Weltzeit.Editor";
        public const string EditorDefault = "15:30";

        public static WorldTimeStart Requested(out string quelle)
        {
            string arg = ProfileSelection.ArgValue(Arg);
            if (!string.IsNullOrEmpty(arg))
            {
                if (WorldTimeStart.TryParse(arg, out WorldTimeStart s))
                {
                    quelle = $"Startparameter {Arg} {arg}";
                    return s;
                }
                Debug.LogWarning($"[Ashveil] Weltzeit: „{Arg} {arg}“ nicht verstanden – Echtzeit. Erlaubt z. B. 09:00, echtzeit, +3, versatz=-2.5");
            }
#if UNITY_EDITOR
            string pref = UnityEditor.EditorPrefs.GetString(EditorPrefKey, EditorDefault);
            if (WorldTimeStart.TryParse(pref, out WorldTimeStart e))
            {
                quelle = "Editor-Einstellung „" + pref + "“";
                return e;
            }
#endif
            quelle = "Standard";
            return WorldTimeStart.Echtzeit;
        }
    }
}
