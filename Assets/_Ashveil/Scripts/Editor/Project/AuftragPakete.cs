using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftrag „pakete“ (17.10.2026, Max: „installiere bitte alle kostenlosen Addons, die wir für die Spieleentwicklung
    /// gebrauchen können“ – Auswahl per Rückfrage: ProBuilder, Terrain Tools, Cinemachine, Recorder, Splines).
    /// Fügt ausschließlich Unity-eigene, kostenlose Pakete aus der Unity-Registry hinzu (keine Fremdquellen, keine Git-URLs).
    /// Parameter: „liste“ (nur anzeigen) oder „hinzu|id1,id2,…“ – nur IDs aus <see cref="Erlaubt"/> werden angenommen.
    /// Das Hinzufügen löst eine Skript-Neuladung aus; das Ergebnis steht danach in Packages/manifest.json.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragPakete
    {
        /// <summary>Freigegebene, kostenlose Unity-Pakete (Max 17.10.).</summary>
        public static readonly string[] Erlaubt =
        {
            "com.unity.probuilder", "com.unity.terrain-tools", "com.unity.cinemachine", "com.unity.recorder", "com.unity.splines"
        };

        private static AddAndRemoveRequest _anfrage;
        private const string Merker = "Ashveil.Pakete.Laufend";

        static AuftragPakete()
        {
            Auftragsordner.Registrieren("pakete", Ausfuehren, true, 900);
            if (EditorPrefs.GetString(Merker, "") != "")
            {
                // Nach der Neuladung: Ergebnis ins Protokoll (das Auftragsergebnis wurde schon vor dem Hinzufügen geschrieben).
                string gewollt = EditorPrefs.GetString(Merker, "");
                EditorPrefs.DeleteKey(Merker);
                EditorApplication.delayCall += () => Debug.Log("[Ashveil] Pakete nach Neuladung: gewollt " + gewollt + " · im Manifest: " + ImManifest());
            }
        }

        private static string ImManifest()
        {
            try
            {
                string text = File.ReadAllText(Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, "Packages/manifest.json"));
                return string.Join(", ", Erlaubt.Where(id => text.Contains("\"" + id + "\"")));
            }
            catch (Exception e) { return "(Manifest nicht lesbar: " + e.Message + ")"; }
        }

        private static void Ausfuehren(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string p = (a.parameter ?? "liste").Trim();
            string drin = ImManifest();
            if (p == "liste") { fertig(true, "erlaubt: " + string.Join(", ", Erlaubt) + " · im Manifest: " + drin); return; }
            if (!p.StartsWith("hinzu|")) { fertig(false, "Parameter „liste“ oder „hinzu|id,id“ erwartet"); return; }
            var ids = p.Substring(6).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToArray();
            var abgelehnt = ids.Where(id => !Erlaubt.Contains(id)).ToArray();
            if (abgelehnt.Length > 0) { fertig(false, "nicht freigegeben: " + string.Join(", ", abgelehnt)); return; }
            var neu = ids.Where(id => !drin.Contains(id)).ToArray();
            if (neu.Length == 0) { fertig(true, "alle schon vorhanden: " + drin); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { fertig(false, "nicht im Play-Modus"); return; }

            // Ergebnis VOR dem Hinzufügen schreiben – die Neuladung würde den Auftrag sonst als „unterbrochen“ melden.
            EditorPrefs.SetString(Merker, string.Join(",", neu));
            fertig(true, "wird hinzugefügt (Neuladung folgt): " + string.Join(", ", neu) + " · vorher im Manifest: " + drin
                         + " · Kontrolle danach mit pakete|liste");
            _anfrage = Client.AddAndRemove(neu, null);
            EditorApplication.update += Warten;
        }

        private static void Warten()
        {
            if (_anfrage == null || !_anfrage.IsCompleted) return;
            EditorApplication.update -= Warten;
            if (_anfrage.Status == StatusCode.Success)
                Debug.Log("[Ashveil] Pakete hinzugefügt: " + string.Join(", ", _anfrage.Result.Select(x => x.name + " " + x.version)));
            else
                Debug.LogError("[Ashveil] Pakete: " + (_anfrage.Error != null ? _anfrage.Error.message : "Fehler"));
            _anfrage = null;
        }
    }
}
