#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Ashveil.Core;
using Ashveil.Core.Events;
using Ashveil.Data;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using Ashveil.Maps;
using Ashveil.UI;
using Ashveil.UI.Diagnostics;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Ruckler-Probe (CORE-005, 09.10.2026): spielt im fertigen Spiel einen festen Ablauf ab, damit die Bild-Spitzen-Messung
    /// (<see cref="FrameSpikeProfiler"/>) die Hänger aus dem Spieltest r2 nachstellen kann – ohne Mensch und ohne Fensterklicks.
    /// Nur Entwicklungs-Build, nur mit „-ashveil-ruckler-probe“ (Auftrag „ruckler_messung“); im Editor nie.
    ///
    /// Ablauf (jeder Teil ist ein Abschnitt in der Messung):
    /// 1. Charakter-Editor (nur wenn er aufgeht, Profil „creator-…“): jede Auswahl jeder Kategorie für beide Körper, sechsmal
    ///    würfeln, Namen tippen, erstellen.
    /// 2. Welt: Ankunft abwarten, Rundblick (eine volle Drehung), ein Stück laufen (Schritte, Geräusche), die Fenster Fähigkeiten,
    ///    Rucksack, Talente, Karte (Gebiet und Welt) und Einstellungen öffnen und mit Esc schließen.
    /// 3. Blickpunkte aus dem Argument („B;x:z:Blickrichtung[:Höhe];…“ – Höhe für Höhlen; liegt ein Ziel in einer ungeladenen Weltzelle,
    ///    wird sie als angekündigte Reise geladen und das Laden mitgemessen – das „B“ vorn, weil ein Wert mit Minus sonst als neues Argument
    ///    gälte; vom Auftrag aus den Blickpunkten des Kareth-Plans und fernen Orten ohne Gegner): hinversetzen (nur Host,
    ///    Test-Profil), kurz warten, Rundblick, ein paar Schritte. Danach zurück ans Wachtfeuer.
    /// 4. „Speichern und beenden“ wie im Spielmenü.
    /// Meldungen im Unity-Log: „[Ashveil] Ruckler-Probe: …“.
    /// </summary>
    public sealed class RucklerProbe : MonoBehaviour
    {
        public const string Argument = "-ashveil-ruckler-probe";

        private GameObject _spieler;
        private int _fehler;

        private struct Blick
        {
            public float X, Z, Yaw;
            /// <summary>Feste Höhe (Westweiden 12.10.: Höhlen – sonst Gelände + 0,5 m); NaN = Gelände.</summary>
            public float Y;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isEditor || ArgWert(Argument) == null) return;
            var go = new GameObject("Ruckler-Probe");
            DontDestroyOnLoad(go);
            go.AddComponent<RucklerProbe>();
        }

        private void OnEnable() => EventBus<LocalPlayerSpawnedEvent>.Subscribe(OnSpieler);
        private void OnDisable() => EventBus<LocalPlayerSpawnedEvent>.Unsubscribe(OnSpieler);

        private void OnSpieler(LocalPlayerSpawnedEvent e)
        {
            if (e.Player != null) _spieler = e.Player;
        }

        private IEnumerator Start()
        {
            List<Blick> blicke = BlickeLesen(ArgWert(Argument));
            Log($"beginnt ({blicke.Count} Blickpunkte)");
            FrameSpikeProfiler.Abschnitt("Start und Laden");

            // 1. Charakter-Editor oder gleich die Welt
            float bis = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < bis && !EditorOffen() && !SpielerBereit()) yield return null;
            if (EditorOffen()) yield return Editor(CharacterCreatorUi.Instance);

            // 2. Welt
            bis = Time.realtimeSinceStartup + 120f;
            while (Time.realtimeSinceStartup < bis && !SpielerBereit()) yield return null;
            if (!SpielerBereit())
            {
                Log("FEHLER – keine Spielfigur nach 120 s, Probe bricht ab");
                yield return Beenden();
                yield break;
            }
            FrameSpikeProfiler.Abschnitt("Welt: Ankunft");
            yield return Warten(8f);
            var input = _spieler.GetComponent<PlayerInputReader>();
            var motion = _spieler.GetComponent<NetworkMotion>();
            if (input == null) Fehler("keine Eingabe an der Spielfigur");

            FrameSpikeProfiler.Abschnitt("Welt: Rundblick an der Ankunft");
            yield return Rundblick(input);
            FrameSpikeProfiler.Abschnitt("Welt: Laufen an der Ankunft");
            yield return Laufen(input, 6f);

            yield return Fenster();

            // 3. Blickpunkte
            if (motion == null && blicke.Count > 0) Fehler("keine Netz-Bewegung an der Spielfigur – keine Blickpunkte");
            for (int i = 0; motion != null && i < blicke.Count; i++)
            {
                FrameSpikeProfiler.Abschnitt($"Blickpunkt {i + 1}");
                yield return Versetzen(motion, blicke[i]);
                yield return Warten(4f);
                yield return Rundblick(input);
                yield return Laufen(input, 3f);
                yield return Warten(1f);
            }
            if (motion != null)
            {
                FrameSpikeProfiler.Abschnitt("Zurück ans Wachtfeuer");
                yield return Versetzen(motion, new Blick { X = 0f, Z = 8f, Yaw = 180f, Y = float.NaN });
                yield return Warten(4f);
                yield return Rundblick(input);
                // Weltzellen (Westweiden 12.10.): nach der Rückkehr wird die Zelle entladen (Haltezeit 20 s) – das gehört in die Messung
                if (WorldStreamer.Instance != null && WorldStreamer.Log.Count > 0)
                {
                    FrameSpikeProfiler.Abschnitt("Wachtfeuer: Zelle entladen");
                    yield return Warten(26f);
                    foreach (string z in WorldStreamer.Log) Log("Weltzellen: " + z);
                    Log($"Weltzellen: längstes Bild beim Laden {WorldStreamer.WorstLoadFrameMs:0} ms");
                }
            }

            // 4. Ende
            Log(_fehler == 0 ? "fertig" : $"fertig mit {_fehler} Fehler(n)");
            yield return Beenden();
        }

        // ================================================================== Teile

        private IEnumerator Editor(CharacterCreatorUi ui)
        {
            FrameSpikeProfiler.Abschnitt("Charakter-Editor: offen");
            yield return Warten(3f);
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            var kategorien = new[]
            {
                CharacterCreatorUi.Category.Face, CharacterCreatorUi.Category.Skin, CharacterCreatorUi.Category.Hair,
                CharacterCreatorUi.Category.HairColor, CharacterCreatorUi.Category.Eyes, CharacterCreatorUi.Category.Beard,
                CharacterCreatorUi.Category.Feature, CharacterCreatorUi.Category.Class
            };
            int koerper = Mathf.Max(1, ui.OptionCount(CharacterCreatorUi.Category.Body));
            int auswahl = 0;
            for (int b = 0; b < koerper; b++)
            {
                FrameSpikeProfiler.Abschnitt($"Charakter-Editor: Körper {b + 1}, alle Auswahlen");
                ui.SelectCategory(CharacterCreatorUi.Category.Body);
                ui.SetValue(CharacterCreatorUi.Category.Body, b);
                yield return Bilder(6);
                foreach (CharacterCreatorUi.Category c in kategorien)
                {
                    ui.SelectCategory(c);
                    yield return Bilder(3);
                    foreach (var o in ui.Options(c, cat))
                    {
                        if (o.value < 0) continue;
                        ui.SetValue(c, o.value);
                        auswahl++;
                        yield return Bilder(4);
                    }
                }
            }
            FrameSpikeProfiler.Abschnitt("Charakter-Editor: würfeln");
            for (int i = 0; i < 6; i++)
            {
                ui.Randomize(100 + i);
                yield return Warten(0.4f);
            }
            FrameSpikeProfiler.Abschnitt("Charakter-Editor: Name und Erstellen");
            ui.SelectCategory(CharacterCreatorUi.Category.Name);
            yield return Bilder(3);
            ui.TypeText("Messprobe");
            yield return Warten(0.6f);
            Log($"Charakter-Editor: {auswahl} Auswahlen, Name „{ui.NameText}“, erstelle …");
            if (!ui.TryCreate()) Fehler("Erstellen abgelehnt: " + ui.Message);
        }

        private IEnumerator Fenster()
        {
            FrameSpikeProfiler.Abschnitt("Fenster: Fähigkeiten");
            if (SpellbookUi.Instance != null) SpellbookUi.Instance.SetOpen(true);
            yield return Warten(1.2f);
            yield return AllesZu();

            FrameSpikeProfiler.Abschnitt("Fenster: Rucksack");
            InventoryUi.RequestBag = true;
            yield return Warten(1.2f);
            yield return AllesZu();

            FrameSpikeProfiler.Abschnitt("Fenster: Talente");
            if (TalentUi.Instance != null) TalentUi.Instance.SetOpen(true);
            yield return Warten(1.2f);
            yield return AllesZu();

            FrameSpikeProfiler.Abschnitt("Fenster: Karte");
            KartenSystem k = KartenSystem.Instanz;
            if (k != null && k.Fenster != null)
            {
                k.Fenster.Umschalten();
                yield return Warten(1.2f);
                if (k.Fenster.Offen) k.Fenster.ZeigeWelt();
                yield return Warten(1.2f);
            }
            else Fehler("keine Karte");
            yield return AllesZu();

            FrameSpikeProfiler.Abschnitt("Fenster: Einstellungen");
            if (SettingsUi.Instance != null) SettingsUi.Instance.SetOpen(true);
            yield return Warten(1.2f);
            yield return AllesZu();
        }

        /// <summary>Esc, bis kein Fenster mehr offen ist (nie mehr als fünfmal; ohne offenes Fenster kein Esc, sonst käme das Spielmenü).</summary>
        private IEnumerator AllesZu()
        {
            for (int i = 0; i < 5 && UiFensterstapel.OffeneFenster > 0; i++)
            {
                UiFensterstapel.EscAusloesen();
                yield return Bilder(3);
            }
            if (UiFensterstapel.OffeneFenster > 0) Fehler($"Fenster bleibt offen: {UiFensterstapel.Oberstes}");
            yield return Warten(0.6f);
        }

        /// <summary>Eine volle Drehung der Figur (die Kamera folgt), höchstens 12 s.</summary>
        private IEnumerator Rundblick(PlayerInputReader input)
        {
            if (input == null || _spieler == null) yield break;
            Transform t = _spieler.transform;
            float gedreht = 0f, alt = t.eulerAngles.y, bis = Time.realtimeSinceStartup + 12f;
            while (gedreht < 360f && Time.realtimeSinceStartup < bis && _spieler != null)
            {
                input.InjectTurn(0.6f);
                yield return null;
                float y = t.eulerAngles.y;
                gedreht += Mathf.Abs(Mathf.DeltaAngle(alt, y));
                alt = y;
            }
            input.InjectTurn(0f);
            if (gedreht < 300f) Fehler($"Rundblick nur {gedreht:0}°");
        }

        /// <summary>Geradeaus laufen (wie W), dann stehen bleiben.</summary>
        private IEnumerator Laufen(PlayerInputReader input, float sekunden)
        {
            if (input == null || _spieler == null) yield break;
            float bis = Time.realtimeSinceStartup + sekunden;
            while (Time.realtimeSinceStartup < bis && _spieler != null)
            {
                input.InjectMovement(1f, false);
                yield return null;
            }
            input.InjectMovement(0f, false);
        }

        private IEnumerator Versetzen(NetworkMotion motion, Blick b)
        {
            var p = new Vector3(b.X, 0f, b.Z);
            // Weltzellen (Westweiden 12.10.): Ziel in einer noch nicht geladenen Zelle – angekündigte Reise, Laden abwarten
            WorldStreamer ws = WorldStreamer.Instance;
            if (ws != null && !ws.IsReadyAt(p))
            {
                FrameSpikeProfiler.Abschnitt("Zelle laden (angekündigte Reise)");
                int vorher = WorldStreamer.Log.Count;
                ws.RequestPreload(p, 30f);
                float t = 0f;
                while (!ws.IsReadyAt(p) && t < 30f) { t += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
                if (!ws.IsReadyAt(p)) Fehler($"Zelle bei ({b.X:0}/{b.Z:0}) nach 30 s nicht geladen");
                for (int i = vorher; i < WorldStreamer.Log.Count; i++) Log("Weltzellen: " + WorldStreamer.Log[i]);
                FrameSpikeProfiler.Abschnitt("Ankunft nach dem Laden");
            }
            p.y = !float.IsNaN(b.Y) ? b.Y : WorldGround.TrySample(p, out float h) ? h + 0.5f : 60f;
            motion.RequestTeleport(p, b.Yaw);
        }

        private IEnumerator Beenden()
        {
            FrameSpikeProfiler.Abschnitt("Speichern und beenden");
            SpielmenueUi menue = SpielmenueUi.Instance;
            if (menue != null)
            {
                menue.Oeffnen();
                yield return null;
                menue.SpeichernUndBeenden();
            }
            // Sicherheit: Endet das Spiel nicht über das Menü, dann nach 45 s direkt (außerhalb sicherer Orte wartet das Menü 20 s, COMBAT-094)
            yield return Warten(45f);
            Log("Spiel endet ohne Menü (Sicherheit)");
            Application.Quit();
        }

        // ================================================================== Hilfen

        private bool SpielerBereit() => _spieler != null && InventoryClient.Current != null && InventoryClient.Current.Ready;

        private static bool EditorOffen() => CharacterCreatorUi.Instance != null && CharacterCreatorUi.Instance.IsOpen;

        private static IEnumerator Warten(float sekunden)
        {
            float bis = Time.realtimeSinceStartup + sekunden;
            while (Time.realtimeSinceStartup < bis) yield return null;
        }

        private static IEnumerator Bilder(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        private void Fehler(string text)
        {
            _fehler++;
            Log("FEHLER – " + text);
        }

        private static void Log(string text)
        {
            Debug.Log("[Ashveil] Ruckler-Probe: " + text);
            FrameSpikeProfiler.Vermerk("Probe: " + text);
        }

        /// <summary>„B;x:z:Blickrichtung;…“ (Punkt als Dezimaltrenner; Teile ohne Doppelpunkt wie das „B“ werden übersprungen).</summary>
        private static List<Blick> BlickeLesen(string arg)
        {
            var l = new List<Blick>();
            if (string.IsNullOrEmpty(arg)) return l;
            foreach (string teil in arg.Split(';'))
            {
                string[] w = teil.Split(':');
                if (w.Length < 2) continue;
                if (!float.TryParse(w[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                float yaw = 0f, y = float.NaN;
                if (w.Length > 2) float.TryParse(w[2], NumberStyles.Float, CultureInfo.InvariantCulture, out yaw);
                if (w.Length > 3) float.TryParse(w[3], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                l.Add(new Blick { X = x, Z = z, Yaw = yaw, Y = y });
            }
            return l;
        }

        /// <summary>Wert nach dem Argument; "" ohne Wert; null, wenn das Argument fehlt.</summary>
        private static string ArgWert(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                return i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[i + 1] : string.Empty;
            }
            return null;
        }
    }
}
#endif
