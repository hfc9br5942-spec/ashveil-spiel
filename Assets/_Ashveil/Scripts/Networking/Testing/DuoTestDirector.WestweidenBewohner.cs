using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Quests;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Westweiden, Lückenliste Punkt 3 (13.10.2026): Bewohner mit Tagesablauf – Teil von „spieltest weidepass“, solange die Zelle
    /// geladen ist. Prüft: Zahl der Bewohner, jeder Platz auf dem NavMesh, jeder Weg von Abschnitt zu Abschnitt vollständig, nachts
    /// schläft das Dorf (außer Wachen), mittags schläft niemand, Streife und Müllerburschen gehen wirklich zwischen den Orten.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private IEnumerator WwBewohnerSuite()
        {
            Line("");
            Line("[Westweiden: Bewohner mit Tagesablauf]");
            var alle = FindObjectsByType<NpcRoutine>(FindObjectsSortMode.None)
                .Where(r => r != null && r.transform.position.x < WwKante && r.GetComponent<NpcIdentity>() == null).ToList();
            Check(alle.Count >= 34, $"Bewohner in der Zelle ({alle.Count}, Soll ≥ 34)");
            if (alle.Count == 0) yield break;

            int plaetze = 0, abseits = 0, wege = 0, wegFehler = 0;
            float laengster = 0f;
            var fehler = new List<string>();
            var path = new NavMeshPath();
            foreach (NpcRoutine r in alle)
            {
                for (int i = 0; i < r.StepCount; i++)
                {
                    plaetze++;
                    if (!NavMesh.SamplePosition(r.Step(i).pos, out NavMeshHit _, 2f, NavMesh.AllAreas)) { abseits++; if (fehler.Count < 6) fehler.Add($"{r.name}: Platz „{r.Step(i).note}“ ohne NavMesh"); }
                }
                for (int i = 0; i < r.StepCount; i++)
                {
                    Vector3 a = r.Step(i).pos, b = r.Step((i + 1) % r.StepCount).pos;
                    if ((a - b).sqrMagnitude < 0.25f) continue;
                    wege++;
                    bool ok = NavMesh.SamplePosition(a, out NavMeshHit ha, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(b, out NavMeshHit hb, 2f, NavMesh.AllAreas) &&
                              NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    if (!ok) { wegFehler++; if (fehler.Count < 6) fehler.Add($"{r.name}: Weg „{r.Step(i).note}“ → „{r.Step((i + 1) % r.StepCount).note}“ {path.status}"); continue; }
                    float l = 0f;
                    for (int k = 1; k < path.corners.Length; k++) l += Vector3.Distance(path.corners[k - 1], path.corners[k]);
                    laengster = Mathf.Max(laengster, l);
                }
                yield return null;
            }
            Check(abseits == 0, $"Alle {plaetze} Plätze auf dem NavMesh" + (abseits > 0 ? $" – {abseits} abseits" : ""));
            Check(wegFehler == 0, $"Alle {wege} Wege zwischen den Abschnitten vollständig (längster {laengster:0} m)" + (wegFehler > 0 ? $" – {wegFehler} unvollständig" : ""));
            foreach (string f in fehler) Info(f);

            // Tagesrhythmus aus den Daten: um 2 Uhr schläft das Dorf (Wachen, Nachthirte ausgenommen), um 12 Uhr schläft niemand
            string Tun(NpcRoutine r, double h) { int i = r.StepAt(h, out _); return i >= 0 ? r.Step(i).activity : ""; }
            int nachtsWach = alle.Count(r => Tun(r, 24.0 * 400 + 2.0) != "Sleep");
            int mittagsSchlaf = alle.Count(r => Tun(r, 24.0 * 400 + 12.0) == "Sleep");
            Check(nachtsWach <= 6 && nachtsWach >= 2, $"Um 2 Uhr nachts schläft das Dorf ({alle.Count - nachtsWach} schlafen, {nachtsWach} wachen: Turm, Posten, Hirtenfeuer)");
            Check(mittagsSchlaf <= 3, $"Mittags schläft fast niemand ({mittagsSchlaf}, nur wer nachts Wache hatte)");
            int unterwegs = alle.Count(r => Enumerable.Range(0, r.StepCount).Any(i =>
                Vector3.Distance(r.Step(i).pos, r.Step((i + 1) % r.StepCount).pos) > 300f));
            Check(unterwegs >= 4, $"Weltleben zwischen den Orten: {unterwegs} gehen weiter als 300 m (Streife, Müllerburschen)");
        }
    }
}
