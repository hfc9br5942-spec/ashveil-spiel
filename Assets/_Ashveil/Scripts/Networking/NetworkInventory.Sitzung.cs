using System.Collections;
using Ashveil.Gameplay.Characters;
using Ashveil.World;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// „Speichern und beenden“ (UI-204; Spieltest r2, Max 09.10.2026). Der Besitzer bittet, der Server speichert sofort,
    /// wartet ohne zu blockieren, bis die Datei geschrieben ist (Schreibfaden, höchstens 5 s), und meldet das Ergebnis
    /// zurück. Beendet wird beim Spieler; der Server trennt niemanden und ändert nichts am Spiel.
    /// Schutz gegen Fluten: eine Bitte je Verbindung zur Zeit und höchstens eine je 2 s.
    ///
    /// Abmelde-Countdown (COMBAT-094/UI-209, Max 10.10.2026: „Countdown außerhalb, sofort im Lager“): Außerhalb sicherer Orte
    /// wartet der Server <see cref="AbmeldenSekunden"/> Sekunden, bevor er speichert und bestätigt; wird die Figur in der
    /// Zeit verletzt oder bewegt sie sich, bricht er ab und meldet es. Sichere Orte: Stützpunkte (WorldQuestSite, Art Hub,
    /// Lager und Siedlungen) und der Ankunftsplatz (PlayerSpawn). Der Server entscheidet; ein Client kann den Countdown nicht
    /// abkürzen. Offen (11 §AM): Wer das Fenster einfach schließt, trennt sofort – dass die Figur dann noch eine Weile in der
    /// Welt bleibt (wie in Classic), ist ein eigener Schritt.
    /// </summary>
    public sealed partial class NetworkInventory : ISitzungService
    {
        public const float AbmeldenSekunden = 20f;
        private const float SpeichernAbstand = 2f;
        private const float SpeichernWartezeit = 5f;
        private const float SpawnSicher = 30f, HubRand = 10f, BewegungAbbruch = 0.75f;
        private float _speichernErlaubtAb;
        private bool _speichernLaeuft;
        private Coroutine _abmelden;
        private bool _verletzt;

        /// <summary>Tests: läuft gerade ein Abmelde-Countdown?</summary>
        public bool ServerAbmeldenLaeuft => _abmelden != null;
        /// <summary>Tests: letzter Grund eines Abbruchs ("" = keiner).</summary>
        public string ServerAbmeldenAbbruch { get; private set; } = "";

        void ISitzungService.SpeichernAnfragen() => ServerSpeichernAnfragen();
        void ISitzungService.AbmeldenAbbrechen() => ServerAbmeldenAbbrechen();

        [ServerRpc]
        private void ServerSpeichernAnfragen()
        {
            if (!_loaded || _data == null)
            {
                TargetGespeichert(Owner, false, "");
                return;
            }
            if (_speichernLaeuft || _abmelden != null) return;   // die laufende Bitte antwortet
            // 22.10.2026 (SPEICHERN-02): Innerhalb der 2-s-Sperre läuft keine Bitte mehr (z. B. Countdown gerade abgebrochen und
            // sofort erneut geklickt) – vorher blieb das still, das Menü wartete auf eine Antwort, die nie kam („Keine Antwort vom
            // Server“). Jetzt antwortet der Server mit dem vorhandenen Abbruch und dem Grund „zu schnell“ (keine neue RPC).
            if (Time.unscaledTime < _speichernErlaubtAb)
            {
                if (Owner != null && Owner.IsActive) TargetAbmeldenAbgebrochen(Owner, SitzungClient.ZuSchnell);
                return;
            }
            _speichernErlaubtAb = Time.unscaledTime + SpeichernAbstand;
            if (ServerSichererOrt(transform.position))
            {
                SpeichernJetzt();
                return;
            }
            ServerAbmeldenAbbruch = "";
            _verletzt = false;
            _abmelden = StartCoroutine(AbmeldenCountdown());
            if (Owner != null && Owner.IsActive) TargetAbmeldenWartet(Owner, AbmeldenSekunden);
        }

        [ServerRpc]
        private void ServerAbmeldenAbbrechen()
        {
            if (_abmelden == null) return;
            AbmeldenAbbrechen("abgebrochen");
        }

        /// <summary>Lager, Siedlungen (Stützpunkte) und der Ankunftsplatz zählen als sicher.</summary>
        public static bool ServerSichererOrt(Vector3 p)
        {
            foreach (WorldQuestSite s in WorldQuestSite.All)
            {
                if (s == null || s.Kind != QuestSiteKind.Hub) continue;
                Vector3 c = s.transform.position;
                float r = s.Radius + HubRand, dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            GameObject spawn = GameObject.Find("PlayerSpawn");
            if (spawn != null)
            {
                Vector3 c = spawn.transform.position;
                float dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz <= SpawnSicher * SpawnSicher) return true;
            }
            // 22.10. WELTZELLEN-03: Die Westweiden-Zelle hat keine Stützpunkte (WorldQuestSite) – Langer Rain, Hallgrund und der
            // Dreispannhof liefen deshalb immer über den 20-s-Countdown. Ihre sicheren Anker (SafeSpawnAnchor aus WestweidenZelle.Anker,
            // 23–30 m neben der Ortsmitte) zählen jetzt mit einem Umkreis, der Ortskern, Gasthaus, Händler und Hof abdeckt. Nur im Code:
            // kein Neubau der Zelle, keine Entdeckungs-EP. Kammsattel und Kreidekeller bleiben unsicher (kein Sofort-Abmelden als Flucht).
            foreach (SafeSpawnAnchor a in SafeSpawnAnchor.All)
            {
                float r = a != null ? SiedlungsAnkerRadius(a.AnchorId) : 0f;
                if (r <= 0f) continue;
                Vector3 c = a.transform.position;
                float dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }

        /// <summary>22.10. WELTZELLEN-03: Umkreis (m) um den sicheren Anker einer Siedlung ohne Stützpunkt; 0 = kein sicherer Ort.</summary>
        private static float SiedlungsAnkerRadius(string anchorId)
        {
            switch (anchorId)
            {
                case "ww_langer_rain": return 65f;    // Ort ±70 m um den Brunnen, Anker 23 m südöstlich
                case "ww_hallgrund": return 50f;      // Hof ±40 m, Anker 30 m nordwestlich
                case "ww_dreispannhof": return 55f;   // Hof ±35 m, Anker 27 m nordöstlich
                default: return 0f;                   // ww_kammsattel, ww_kreidekeller: draußen
            }
        }

        private IEnumerator AbmeldenCountdown()
        {
            Vector3 start = transform.position;
            float bis = Time.unscaledTime + AbmeldenSekunden;
            if (_health != null) _health.Damaged += AbmeldenVerletzt;
            try
            {
                // 22.10.2026 (SPEICHERN-02): erst ein Bild warten. Brach der Countdown sofort ab (tot außerhalb sicherer Orte), lief der
                // Abbruch noch innerhalb von StartCoroutine – danach wurde _abmelden auf die beendete Coroutine gesetzt (der Server
                // ignorierte jede weitere Bitte der Sitzung) und TargetAbmeldenWartet kam NACH dem Abbruch an (Menü zeigte den Countdown).
                yield return null;
                while (Time.unscaledTime < bis)
                {
                    if (_verletzt) { AbmeldenAbbrechen("Schaden"); yield break; }
                    if (_health != null && !_health.IsAlive) { AbmeldenAbbrechen("gestorben"); yield break; }
                    Vector3 p = transform.position;
                    float dx = p.x - start.x, dz = p.z - start.z;
                    if (dx * dx + dz * dz > BewegungAbbruch * BewegungAbbruch) { AbmeldenAbbrechen("Bewegung"); yield break; }
                    yield return null;
                }
            }
            finally
            {
                if (_health != null) _health.Damaged -= AbmeldenVerletzt;
            }
            _abmelden = null;
            SpeichernJetzt();
        }

        private void AbmeldenVerletzt(Ashveil.Gameplay.Combat.DamageInfo info)
        {
            if (info.Amount > 0f) _verletzt = true;
        }

        private void AbmeldenAbbrechen(string grund)
        {
            if (_abmelden != null) StopCoroutine(_abmelden);
            _abmelden = null;
            if (_health != null) _health.Damaged -= AbmeldenVerletzt;
            ServerAbmeldenAbbruch = grund;
            Debug.Log($"[Ashveil] Abmelden: '{_profile}' abgebrochen ({grund}).");
            if (Owner != null && Owner.IsActive) TargetAbmeldenAbgebrochen(Owner, grund);
        }

        private void SpeichernJetzt()
        {
            _speichernLaeuft = true;
            string vorher = _data.savedAtUtc;
            SaveNow();
            StartCoroutine(WarteAufSpeicherung(vorher));
        }

        private IEnumerator WarteAufSpeicherung(string vorher)
        {
            float bis = Time.unscaledTime + SpeichernWartezeit;
            while (CharacterStore.IsSaving(_profile) && Time.unscaledTime < bis) yield return null;
            string zeit = _data != null ? _data.savedAtUtc : "";
            bool ok = !CharacterStore.IsSaving(_profile) && CharacterStore.LastSaveOk(_profile) && zeit != vorher;
            _speichernLaeuft = false;
            Debug.Log($"[Ashveil] Speichern und beenden: '{_profile}' {(ok ? "gespeichert" : "nicht bestätigt")} ({zeit}).");
            if (Owner != null && Owner.IsActive) TargetGespeichert(Owner, ok, zeit);
        }

        [TargetRpc]
        private void TargetGespeichert(NetworkConnection conn, bool ok, string zeitUtc) => SitzungClient.MeldeGespeichert(ok, zeitUtc);

        [TargetRpc]
        private void TargetAbmeldenWartet(NetworkConnection conn, float sekunden) => SitzungClient.MeldeAbmeldenWartet(sekunden);

        [TargetRpc]
        private void TargetAbmeldenAbgebrochen(NetworkConnection conn, string grund) => SitzungClient.MeldeAbmeldenAbgebrochen(grund);
    }
}
