using System;
using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Maps;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// PROG-013 Entdeckungs-EP je Unterzone (Max 08.10.2026, Klick „Zusätzlich EP je Unterzone“; Spieltest r2):
    ///   · Der Server entscheidet und speichert, der Client zeigt nur an. Grundlage ist die Position der Figur auf dem Server und
    ///     das Unterzonen-Raster der Szene (<see cref="KartenBezug"/>); ohne Kartenbezug in der Szene passiert nichts.
    ///   · Jede neu betretene Unterzone gibt einmal 20 + 5 × (niedrigste Stufe der Unterzone) Erfahrung – dieselbe Formel wie die
    ///     Entdeckung der Quest-Orte (EP-Bilanz Kareth). Zusätzlich zu den Quest-Orten (P3), nicht statt ihrer.
    ///   · Gespeichert im Speicherstand wie die Quest-Orte (discoveredAreas, Schlüssel „uz.&lt;Gebiet&gt;.&lt;Nummer&gt;“) – kein neues Format.
    ///   · Ohne EP bekannt: die Startunterzone der Szene und die Stelle, an der man sich anmeldet (erster Blick nach dem Anmelden).
    ///   · An den eigenen Client: nach dem Anmelden die ganze Liste, danach jede neue Unterzone (ein RPC je Entdeckung).
    /// Prüfung alle 0,5 s je Spieler: ein Rasterzugriff, Arbeit nur beim Wechsel der Unterzone (CORE-005).
    /// </summary>
    public sealed partial class NetworkProgression
    {
        private const float KartenIntervall = 0.5f, KartenLoginVerzug = 2f;

        private float _kartenTakt;
        private bool _kartenPrimed;
        private int _kartenLetzte = -1;

        /// <summary>Server: letzte Unterzonen-Entdeckung mit EP („uz.kareth.2+40“) – Tests.</summary>
        public string ServerLastMapDiscovery { get; private set; } = "";

        /// <summary>Server: Anzahl entdeckter Unterzonen (Schlüssel „uz.*“).</summary>
        public int ServerMapDiscoveredCount
        {
            get
            {
                if (!Loaded || D.discoveredAreas == null) return 0;
                int n = 0;
                foreach (string k in D.discoveredAreas)
                    if (k != null && k.StartsWith(ZonenDienst.SchluesselPraefix, StringComparison.Ordinal)) n++;
                return n;
            }
        }

        private void KartenLogin()
        {
            _kartenPrimed = false;
            _kartenTakt = KartenLoginVerzug;   // Position nach dem Anmelden erst setzen lassen
            _kartenLetzte = -1;
            ServerLastMapDiscovery = "";
            KartenBezug kb = KartenBezug.Instanz;
            if (kb == null || kb.Dienst == null) return;
            if (D.discoveredAreas == null) D.discoveredAreas = new List<string>();
            // Startunterzone der Szene: bekannt, ohne EP (wie das Startlager bei den Quest-Orten)
            SzenenRasterDatei sz = kb.Dienst.Szene;
            if (sz != null && sz.start_unterzone > 0 && KartenMerken(kb.Dienst, kb.Dienst.UnterzoneNachId(sz.start_unterzone), null))
                _inv.ServerMarkDirty();
            KartenAnClient(kb.Dienst);
        }

        private void KartenUpdate()
        {
            KartenBezug kb = KartenBezug.Instanz;
            if (kb == null || kb.Dienst == null) return;
            _kartenTakt -= Time.deltaTime;
            if (_kartenTakt > 0f) return;
            _kartenTakt = KartenIntervall;
            UnterzoneDaten u = kb.UnterzoneBei(transform.position);
            bool ersterBlick = !_kartenPrimed;
            _kartenPrimed = true;
            if (u == null || u.id == _kartenLetzte) return;
            _kartenLetzte = u.id;
            ZonenDienst d = kb.Dienst;
            var neu = new List<int>(2);   // Allokation nur beim Wechsel der Unterzone
            if (!KartenMerken(d, u, neu)) return;
            // Erster Blick nach dem Anmelden: wo man steht, kennt man schon (kein EP fürs Einloggen) – wie bei den Quest-Orten
            int xp = ersterBlick ? 0 : Progression.DiscoveryXp(u.stufe_von);
            if (xp > 0)
            {
                ServerLastMapDiscovery = ZonenDienst.Schluessel(d.ZoneDerUnterzone(u.id), u) + "+" + xp;
                AddXp(xp);
                if (Owner != null && Owner.IsActive) TargetXp(Owner, xp, transform.position + Vector3.up * 2f);
                Debug.Log($"[Ashveil] '{_profile}' entdeckt die Unterzone {u.name} (+{xp} EP).");
            }
            _inv.ServerMarkDirty();
            if (xp > 0) SendState(false);
            if (Owner != null && Owner.IsActive) TargetKarten(Owner, neu.ToArray(), false, xp, xp > 0 ? u.name : "");
        }

        /// <summary>Unterzone (und ihre Ortsteile ohne eigene Fläche) im Speicherstand vermerken; true, wenn sie neu war.</summary>
        private bool KartenMerken(ZonenDienst d, UnterzoneDaten u, List<int> neu)
        {
            if (u == null) return false;
            string key = ZonenDienst.Schluessel(d.ZoneDerUnterzone(u.id), u);
            if (D.discoveredAreas.Contains(key)) return false;
            D.discoveredAreas.Add(key);
            neu?.Add(u.id);
            if (u.mit != null)
                foreach (int m in u.mit)
                {
                    UnterzoneDaten t = d.UnterzoneNachId(m);
                    string tk = ZonenDienst.Schluessel(d.ZoneDerUnterzone(m), t);
                    if (t == null || D.discoveredAreas.Contains(tk)) continue;
                    D.discoveredAreas.Add(tk);
                    neu?.Add(m);
                }
            return true;
        }

        /// <summary>Alle entdeckten Unterzonen an den eigenen Client (nach dem Anmelden; Wiederverbinden = neues Anmelden).</summary>
        private void KartenAnClient(ZonenDienst d)
        {
            if (Owner == null || !Owner.IsActive || D.discoveredAreas == null) return;
            var ids = new List<int>();
            foreach (string k in D.discoveredAreas)
            {
                UnterzoneDaten u = d.AusSchluessel(k);
                if (u != null) ids.Add(u.id);
            }
            TargetKarten(Owner, ids.ToArray(), true, 0, "");
        }

        /// <summary>Server, Tests: die ganze Liste erneut senden (wie nach einem Wiederverbinden).</summary>
        public void ServerKartenNeuSenden()
        {
            KartenBezug kb = KartenBezug.Instanz;
            if (Loaded && kb != null && kb.Dienst != null) KartenAnClient(kb.Dienst);
        }

        /// <summary>Server: Unterzone schon entdeckt?</summary>
        public bool ServerMapDiscovered(int unterzone)
        {
            KartenBezug kb = KartenBezug.Instanz;
            if (!Loaded || kb == null || kb.Dienst == null || D.discoveredAreas == null) return false;
            UnterzoneDaten u = kb.Dienst.UnterzoneNachId(unterzone);
            return u != null && D.discoveredAreas.Contains(ZonenDienst.Schluessel(kb.Dienst.ZoneDerUnterzone(unterzone), u));
        }

        private void KartenStopClient()
        {
            if (IsOwner) KartenEntdeckungen.Leeren();
        }

        [TargetRpc]
        private void TargetKarten(NetworkConnection conn, int[] ids, bool voll, int xp, string name)
        {
            KartenEntdeckungen.Melden(ids ?? Array.Empty<int>(), voll, xp, name);
        }
    }
}
