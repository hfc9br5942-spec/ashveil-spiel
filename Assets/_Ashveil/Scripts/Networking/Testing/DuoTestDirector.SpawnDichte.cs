using System.Collections;
using System.IO;
using System.Linq;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// CORE-008 / CORE-005 (02.10.2026): Spawn-Fairness und Gegnerdichte nach Spielerzahl, mit zwei echten Figuren.
    ///   · Grundanzahl, solange niemand jagt
    ///   · zwei Jaeger im Gebiet → mehr Gegner (Grundanzahl + Haelfte je weiterem Jaeger, hoechstens doppelt)
    ///   · Wiederkehr nie direkt am Spieler: steht jemand mitten im Gebiet, wartet der Gegner
    ///   · geht man ein Stueck weg, kommt er – mit Abstand
    ///   · niemand jagt mehr → ueberzaehlige, ruhige Gegner werden abgebaut
    /// Start: "Ashveil › Test › Spawn-Fairness und Dichte (2 Spieler)". Bericht: Logs/…/Ashveil_SpawnDichteTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string SpawnDensityFlag = "spawn-dichte";
        private static bool _spawnDensityMode;
        private const string DensityA = "duo-dichte-a", DensityB = "duo-dichte-b";

        private IEnumerator SpawnDensitySuite()
        {
            NetworkInventory a = null, b = null;
            yield return WaitUntil(() => (a = OnlineInv(DensityA)) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, DensityB);
            yield return WaitUntil(() => (b = OnlineInv(DensityB)) != null, 120f);
            Check(b != null, "Spieler B (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);
            a.GetComponent<Health>()?.SetInvulnerableFor(3600f);
            b.GetComponent<Health>()?.SetInvulnerableFor(3600f);
            EnemyDirector dir = EnemyDirector.Instance;
            Check(dir != null && dir.Ready, "Gegner-Regie bereit");
            if (dir == null) yield break;
            EnemyDirector.TestIdleExtraTimeout = 6f;

            // Testgebiet: das erste Wolfsrudel am Lager (Kiefernhang, defensiv, Grundanzahl 3)
            EnemySpawnArea area = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .Where(x => x.Definition != null && x.Definition.Id == "enemy.ash_wolf" && x.Route == null && x.HideoutCount == 1)
                .OrderBy(x => Flat(x.transform.position, Vector3.zero)).FirstOrDefault();
            Check(area != null, $"Testgebiet gefunden ({(area != null ? area.name : "-")}, Grundanzahl {(area != null ? area.Count : 0)})");
            if (area == null) yield break;
            Vector3 c = area.transform.position;
            Vector3 far = Gnd(0f, -40f); // Lager, weit genug weg (> Radius + 60 m)
            Vector3 near = Gnd(c.x, c.z);
            Vector3 aside = Gnd(c.x + 30f, c.z); // im Jagdbereich, aber ausserhalb des Spawn-Abstands

            // ---------------------------------------------------------- Grundanzahl
            Line("");
            Line("[Grundanzahl ohne Jaeger]");
            yield return Ask(a, "goto", V(far, 0f));
            yield return Ask(b, "goto", V(far + new Vector3(2f, 0f, 0f), 0f));
            yield return new WaitForSecondsRealtime(2f);
            Check(dir.Hunters(area) == 0 && dir.TargetCount(area) == area.Count,
                $"Niemand jagt: Soll = Grundanzahl ({dir.TargetCount(area)} von {area.Count})");

            // ---------------------------------------------------------- Dichte mit zwei Jaegern
            Line("");
            Line("[Zwei Jaeger im Gebiet]");
            yield return Ask(a, "goto", V(aside, 0f));
            yield return Ask(b, "goto", V(aside + new Vector3(0f, 0f, 3f), 0f));
            yield return new WaitForSecondsRealtime(1f);
            int target2 = dir.TargetCount(area);
            int expected = Mathf.Min(area.Count + Mathf.CeilToInt(area.Count * 0.5f), area.Count * 2);
            Check(dir.Hunters(area) == 2 && target2 == expected, $"Zwei Jaeger: Soll {target2} (erwartet {expected})");
            float t0 = Time.realtimeSinceStartup;
            yield return WaitUntil(() => dir.AliveOf(area).Count >= target2, 3f * (target2 - area.Count) + 10f);
            int alive2 = dir.AliveOf(area).Count;
            Check(alive2 >= target2, $"Zusaetzliche Gegner erscheinen ({alive2} lebend nach {Time.realtimeSinceStartup - t0:0} s)");
            float minDist = dir.AliveOf(area).Select(w => Mathf.Min(Flat(w.transform.position, a.transform.position), Flat(w.transform.position, b.transform.position))).DefaultIfEmpty(99f).Min();
            Info($"Abstand der Gegner zu den Spielern: mindestens {minDist:0} m (Grundgegner standen schon vorher dort)");

            // ---------------------------------------------------------- Fairness: kein Erscheinen am Spieler
            Line("");
            Line("[Kein Erscheinen direkt am Spieler]");
            yield return Ask(a, "goto", V(near, 0f));
            yield return Ask(b, "goto", V(near + new Vector3(1.5f, 0f, 0f), 0f));
            yield return new WaitForSecondsRealtime(1f);
            foreach (EnemyBrain w in dir.AliveOf(area))
                w.GetComponent<Health>().ApplyDamage(new DamageInfo { Amount = 999999f, Source = a.gameObject, Unblockable = true });
            yield return new WaitForSecondsRealtime(1f);
            Check(dir.AliveOf(area).Count == 0, "Alle Gegner des Gebiets besiegt");
            // Leichen ohne Wartezeit abraeumen lassen: Beute ist persoenlich, die Leichen bleiben sonst liegen
            yield return WaitUntil(() => EnemyDirector.SecondsUntilRespawn(area) >= 0f, area.Definition.corpseTime + 160f);
            yield return new WaitForSecondsRealtime(1.5f);
            EnemyDirector.HurryRespawns(area);
            yield return new WaitForSecondsRealtime(6f);
            int spawnedWhileStanding = dir.AliveOf(area).Count(w => Mathf.Min(Flat(w.transform.position, a.transform.position), Flat(w.transform.position, b.transform.position)) < 12f);
            int aliveStanding = dir.AliveOf(area).Count;
            Check(spawnedWhileStanding == 0, $"Spieler steht mitten im Gebiet: kein Gegner erscheint naeher als 12 m ({aliveStanding} erschienen, {spawnedWhileStanding} zu nah)");

            yield return Ask(a, "goto", V(aside, 0f));
            yield return Ask(b, "goto", V(aside + new Vector3(0f, 0f, 3f), 0f));
            yield return WaitUntil(() => dir.AliveOf(area).Count >= area.Count, 15f);
            int back = dir.AliveOf(area).Count;
            float minAfter = dir.AliveOf(area).Select(w => Mathf.Min(Flat(w.transform.position, a.transform.position), Flat(w.transform.position, b.transform.position))).DefaultIfEmpty(99f).Min();
            Check(back >= area.Count && minAfter >= 12f, $"Spieler treten zur Seite: Gegner kehren zurueck ({back}), naechster {minAfter:0} m entfernt");

            // ---------------------------------------------------------- Abbau
            Line("");
            Line("[Niemand jagt mehr]");
            yield return WaitUntil(() => dir.AliveOf(area).Count >= dir.TargetCount(area), 20f);
            int before = dir.AliveOf(area).Count;
            yield return Ask(a, "goto", V(far, 0f));
            yield return Ask(b, "goto", V(far + new Vector3(2f, 0f, 0f), 0f));
            yield return WaitUntil(() => dir.Current(area) <= area.Count, 6f + 3f * (before - area.Count) + 12f);
            Check(dir.Current(area) == area.Count, $"Ueberzaehlige Gegner abgebaut ({before} → {dir.Current(area)}, Grundanzahl {area.Count})");

            EnemyDirector.TestIdleExtraTimeout = -1f;
        }
    }
}
