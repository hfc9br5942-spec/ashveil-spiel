using System.Collections.Generic;
using System.IO;
using Ashveil.Gameplay.Quests;
using UnityEngine;

namespace Ashveil.Maps
{
    /// <summary>
    /// Führung der Aufträge auf Karte und Minimap (Quest Design Bible §4, UI-202; Max 10.10.2026: A/B/C-Liste freigegeben, Kreise
    /// auch auf der Minimap). Liest szenen/&lt;Szene&gt;_fuehrung.json (<see cref="FuehrungDatei"/>) und hält die Anzeige des
    /// <see cref="KartenSystem"/> auf dem Stand der eigenen Aufträge (<see cref="ProgressionClient"/>):
    ///   A – je offenem Ziel ein weicher Kreis; sind alle Ziele erfüllt, ein Kreis um die Abgabefigur (wenn eingetragen).
    ///   B – nur die Unterzone (Name und schwacher Ring auf der Gebietskarte; nicht auf der Minimap).
    ///   C – nichts.
    /// Reine Anzeige am Client, kein Netzverkehr: Der Client kennt seine Aufträge schon. Ein Auftrag ohne Eintrag in der Datei
    /// (andere Szene, ältere Aufträge) bekommt nichts – nie ein Punkt, nie ein Pfeil.
    /// </summary>
    public sealed class QuestFuehrung : MonoBehaviour
    {
        /// <summary>Farbe der Zielkreise (warmes Gelb, wie das „!“ der Geber).</summary>
        public static readonly Color Farbe = new Color(1f, 0.84f, 0.36f);

        private KartenSystem _system;
        private readonly Dictionary<string, FuehrungAuftrag> _auftraege = new Dictionary<string, FuehrungAuftrag>();
        private readonly HashSet<string> _kreise = new HashSet<string>();        // gezeigte Kreis-Kennungen
        private readonly HashSet<string> _regionen = new HashSet<string>();      // gezeigte Unterzonen-Kennungen (Auftrag)
        private readonly HashSet<string> _soll = new HashSet<string>();
        private readonly HashSet<long> _lagen = new HashSet<long>();
        private readonly List<string> _weg = new List<string>();
        private bool _dirty;

        public int Auftraege => _auftraege.Count;
        public string Stand { get; private set; } = "";
        public bool Geladen => _auftraege.Count > 0;

        /// <summary>Datei lesen (null, wenn sie fehlt oder kaputt ist).</summary>
        public static FuehrungDatei Laden(string ordner, string szene, out string fehler)
        {
            fehler = null;
            if (string.IsNullOrEmpty(szene)) return null;
            string pfad = Path.Combine(ordner, "szenen", szene + "_fuehrung.json");
            if (!File.Exists(pfad)) { fehler = "Keine Auftragsführung: " + pfad; return null; }
            try
            {
                var f = JsonUtility.FromJson<FuehrungDatei>(File.ReadAllText(pfad));
                if (f == null || f.auftraege == null) { fehler = "Auftragsführung unvollständig: " + pfad; return null; }
                return f;
            }
            catch (System.Exception e)
            {
                fehler = "Auftragsführung nicht lesbar: " + e.Message;
                return null;
            }
        }

        public void Init(KartenSystem system)
        {
            _system = system;
            ZonenDienst d = system.Dienst;
            if (d == null || d.Szene == null) return;
            FuehrungDatei f = Laden(d.Ordner, d.Szene.name, out string fehler);
            if (f == null) { if (fehler != null) Debug.Log("[Karten] " + fehler); return; }
            foreach (FuehrungAuftrag a in f.auftraege)
                if (a != null && !string.IsNullOrEmpty(a.quest)) _auftraege[a.quest] = a;
            Stand = f.stand ?? "";
            // Weitere Szenenraster (Weltzellen, 13.10.2026): deren Führung kommt dazu (gleiche Kennungen: die erste gewinnt)
            foreach (SzenenRasterDatei w in d.WeitereSzenen)
            {
                FuehrungDatei fw = Laden(d.Ordner, w.name, out string fehlerW);
                if (fw == null) { if (fehlerW != null) Debug.Log("[Karten] " + fehlerW); continue; }
                foreach (FuehrungAuftrag a in fw.auftraege)
                    if (a != null && !string.IsNullOrEmpty(a.quest) && !_auftraege.ContainsKey(a.quest)) _auftraege[a.quest] = a;
            }
            Debug.Log($"[Karten] Auftragsführung {d.Szene.name}: {_auftraege.Count} Aufträge (Stand {Stand})");
        }

        public FuehrungAuftrag Eintrag(string questId) => questId != null && _auftraege.TryGetValue(questId, out FuehrungAuftrag a) ? a : null;

        private void OnEnable()
        {
            ProgressionClient.Changed += Markieren;
            _dirty = true;
        }

        private void OnDisable()
        {
            ProgressionClient.Changed -= Markieren;
        }

        private void Markieren() => _dirty = true;

        private void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            Anwenden();
        }

        /// <summary>Anzeige auf den Stand der eigenen Aufträge bringen (nur Unterschiede werden gemeldet).</summary>
        public void Anwenden()
        {
            if (_system == null) return;
            _soll.Clear();
            IProgressionService p = ProgressionClient.Current;
            IReadOnlyList<QuestView> quests = p != null && p.Ready ? p.Quests : null;
            if (quests != null && _auftraege.Count > 0)
            {
                for (int i = 0; i < quests.Count; i++)
                {
                    QuestView v = quests[i];
                    if (v?.Def == null || !_auftraege.TryGetValue(v.Def.Id, out FuehrungAuftrag a)) continue;
                    if (a.stufe == "B")
                    {
                        if (a.unterzone > 0) Region(v.Def.Id, a.unterzone);
                        continue;
                    }
                    if (a.stufe != "A") continue;
                    if (v.Complete)
                    {
                        if (a.abgabe != null && a.abgabe.radius > 0f) Kreis(v.Def.Id + "#abgabe", a.abgabe, v.Def.Title);
                        continue;
                    }
                    if (a.kreise == null) continue;
                    _lagen.Clear();
                    foreach (FuehrungKreis k in a.kreise)
                    {
                        if (k == null || k.radius <= 0f) continue;
                        if (k.ziel >= 0 && Erfuellt(v, k.ziel)) continue;
                        if (!_lagen.Add(((long)k.x << 42) ^ ((long)k.z << 21) ^ (long)k.radius)) continue;   // zwei Ziele am selben Ort: ein Kreis
                        Kreis(v.Def.Id + "#" + k.ziel, k, v.Def.Title);
                    }
                }
            }
            // Was nicht mehr gebraucht wird, verschwindet (Abgabe, Abbruch, erledigtes Ziel)
            _weg.Clear();
            foreach (string id in _kreise) if (!_soll.Contains("k:" + id)) _weg.Add(id);
            foreach (string id in _weg) { _kreise.Remove(id); _system.EntferneQuestgebiet(id); }
            _weg.Clear();
            foreach (string id in _regionen) if (!_soll.Contains("r:" + id)) _weg.Add(id);
            foreach (string id in _weg) { _regionen.Remove(id); _system.EntferneQuestUnterzone(id); }
        }

        private static bool Erfuellt(QuestView v, int ziel)
        {
            if (v.Def.objectives == null || ziel >= v.Def.objectives.Count) return true;
            return ziel < v.Progress.Length && v.Progress[ziel] >= v.Def.objectives[ziel].count;
        }

        private void Kreis(string id, FuehrungKreis k, string text)
        {
            _soll.Add("k:" + id);
            if (_kreise.Add(id)) _system.ZeigeQuestgebiet(id, k.x, k.z, k.radius, Farbe, text);
        }

        private void Region(string questId, int unterzone)
        {
            _soll.Add("r:" + questId);
            if (_regionen.Add(questId)) _system.ZeigeQuestUnterzone(questId, unterzone, Farbe);
        }
    }
}
