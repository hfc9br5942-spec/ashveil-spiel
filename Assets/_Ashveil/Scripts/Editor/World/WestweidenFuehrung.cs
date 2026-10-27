using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using Ashveil.Maps;
using UnityEditor;
using UnityEngine;
using WC = Ashveil.Data.WestweidenCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftragsführung der Westweiden (Lückenliste Punkt 4, 13.10.2026): Kreise auf Karte und Minimap für alle Aufträge mit dem
    /// Kennzeichen „westweiden“ (W01–W28 und R6 aus Akt I). Anders als Kareth (<see cref="KarethV4Fuehrung"/>, Tabelle von Hand)
    /// werden die Lagen aus der gebauten Zelle gelesen – Figuren (NpcIdentity), Fundstellen (StoryAnchor) und Gegnergebiete
    /// (EnemySpawnArea samt Verstecken) –, damit Kreis und Welt nie auseinanderlaufen, wenn sich die Zelle ändert.
    ///
    /// Regeln wie Kareth (Quest Design Bible §4, Papier A/B/C §3):
    ///   A  je Ziel ein Kreis um das Zielgebiet (+40 m, mindestens 60 m, Mitte um r/4 versetzt); weit auseinanderliegende Gebiete
    ///      und Verstecke seltener Tiere je ein eigener Kreis. Abgabekreis (60 m), wenn die Abgabefigur weiter als 250 m vom
    ///      letzten Ziel steht.
    ///   C  unmarkierte Aufträge (QuestDefinition.unmarked): nichts.
    /// Stufe je Auftrag: VORSCHLAG Claude – alle A (wie die Kareth-Nebenaufträge und das Classic-Paket); Max kann einzelne auf B/C
    /// setzen (11 §AS). Geschrieben wird StreamingAssets/Ashveil/Karten/szenen/westweiden_v2_fuehrung.json, gelesen vom Client
    /// über <see cref="QuestFuehrung"/> (weiteres Szenenraster). Aufruf am Ende von <see cref="WestweidenZelle"/>, oder über das Menü
    /// bei geöffneter Zelle.
    /// </summary>
    public static class WestweidenFuehrung
    {
        public const string DateiName = KarethV4Spiel.KartenWestweidenRaster + "_fuehrung.json";
        public const string Stand = "13.10.2026 (Westweiden-Zelle: alle A als Vorschlag, Lagen aus der gebauten Zelle)";
        private const float AbgabeAbstand = 250f, AbgabeRadius = 60f, Haufen = 200f;

        /// <summary>Stufe abweichend von A (VORSCHLAG/Max). Leer = alle A; unmarkierte Aufträge sind immer C.</summary>
        private static readonly Dictionary<string, string> Stufen = new Dictionary<string, string>();

        /// <summary>
        /// Gegner der Weltfülle leben überall – der Kreis gehört dahin, wo der Auftragstext sie sucht (Unterzonen des Szenenrasters
        /// westweiden_v2): W15 „an den Hofrändern, bis hinüber zum Rosenhag“, W27 „am Passfuß“. Ohne Eintrag und mit mehr als
        /// <see cref="MaxKreise"/> Gebieten gelten die nächsten beim Auftraggeber (mit Hinweis).
        /// </summary>
        private static readonly Dictionary<string, int[]> Gebiet = new Dictionary<string, int[]>
        {
            { WC.W15, new[] { 23, 24 } },   // Dreispann-Weiden, Rosenhag
            { WC.W27, new[] { 26 } },       // Passfuß
        };
        private const int MaxKreise = 4;

        public static string Pfad => Path.Combine(Application.streamingAssetsPath, "Ashveil", "Karten", "szenen", DateiName);

        [MenuItem("Ashveil/Welt/Westweiden: Auftragsführung schreiben (Zelle geöffnet)", priority = 569)]
        private static void Menu()
        {
            Debug.Log("[Ashveil] Westweiden: " + Schreiben());
            AssetDatabase.Refresh();
        }

        /// <summary>Aus den geladenen Szenen rechnen und schreiben; gibt eine Zusammenfassung zurück.</summary>
        public static string Schreiben()
        {
            var warn = new List<string>();
            FuehrungDatei f = Bauen(warn);
            Directory.CreateDirectory(Path.GetDirectoryName(Pfad));
            File.WriteAllText(Pfad, JsonUtility.ToJson(f, true).Replace("\r\n", "\n"));
            int a = f.auftraege.Count(e => e.stufe == "A"), c = f.auftraege.Count(e => e.stufe == "C");
            int kreise = f.auftraege.Sum(e => e.kreise.Length);
            return $"Auftragsführung {DateiName}: {a} × A ({kreise} Kreise), {c} × C" +
                   (warn.Count > 0 ? $" – {warn.Count} Hinweise: " + string.Join("; ", warn.Take(8)) : "");
        }

        public static FuehrungDatei Bauen(List<string> warn)
        {
            Vector2 v = KarethV4Spiel.KartenWeltversatz;
            ZonenDienst dienst = ZonenDienst.Laden(Path.Combine(Application.streamingAssetsPath, "Ashveil", "Karten"), out string fehler);
            if (dienst == null || !dienst.LadeSzene(KarethV4Spiel.KartenSzenenRaster, out fehler) || !dienst.LadeWeitereSzene(KarethV4Spiel.KartenWestweidenRaster, out fehler))
            { warn.Add("Szenenraster: " + fehler); dienst = null; }

            // ---- was die Zelle hergibt
            var figuren = new Dictionary<string, Vector2>();
            foreach (NpcIdentity n in Object.FindObjectsByType<NpcIdentity>(FindObjectsInactive.Include))
                if (!string.IsNullOrEmpty(n.NpcId) && !figuren.ContainsKey(n.NpcId)) figuren[n.NpcId] = XZ(n.transform.position);
            var fundItem = new Dictionary<string, List<Vector2>>();
            var fundStory = new Dictionary<string, List<Vector2>>();
            foreach (StoryAnchor s in Object.FindObjectsByType<StoryAnchor>(FindObjectsInactive.Include))
            {
                if (string.IsNullOrEmpty(s.AnchorId)) continue;
                if (!WC.TryNode(s.AnchorId, out GlockenrastCatalog.Node node) && !GlockenrastCatalog.TryNode(s.AnchorId, out node)) continue;
                if (!string.IsNullOrEmpty(node.Item)) Add(fundItem, node.Item, XZ(s.Position));
                if (!string.IsNullOrEmpty(node.Story)) Add(fundStory, node.Story, XZ(s.Position));
            }
            var gebiete = new Dictionary<string, List<(Vector2 p, float r, Vector2[] verstecke)>>();
            foreach (EnemySpawnArea s in Object.FindObjectsByType<EnemySpawnArea>(FindObjectsInactive.Include))
            {
                if (s.Definition == null || s.SummonOnly) continue;
                var so = new SerializedObject(s);
                SerializedProperty alt = so.FindProperty("alternatePoints");
                var verstecke = new List<Vector2> { XZ(s.transform.position) };
                if (alt != null) for (int i = 0; i < alt.arraySize; i++) verstecke.Add(XZ(alt.GetArrayElementAtIndex(i).vector3Value));
                if (!gebiete.TryGetValue(s.Definition.Id, out var l)) gebiete[s.Definition.Id] = l = new List<(Vector2, float, Vector2[])>();
                l.Add((XZ(s.transform.position), s.Radius, verstecke.ToArray()));
            }
            // Beute: welches Tier trägt welchen Auftragsgegenstand
            var beute = new Dictionary<string, List<string>>();
            foreach (EnemySpawnArea s in Object.FindObjectsByType<EnemySpawnArea>(FindObjectsInactive.Include))
            {
                LootTableDefinition t = s.Definition != null ? s.Definition.lootTable : null;
                if (t == null) continue;
                foreach (LootTableDefinition.Entry e in t.entries)
                    if (e.item != null && e.chance > 0f)
                    {
                        if (!beute.TryGetValue(e.item.Id, out var l)) beute[e.item.Id] = l = new List<string>();
                        if (!l.Contains(s.Definition.Id)) l.Add(s.Definition.Id);
                    }
            }

            // ---- Aufträge mit dem Kennzeichen „westweiden“
            var quests = AssetDatabase.FindAssets("t:QuestDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(q => q != null && q.requiredSceneTag == WC.SceneTag)
                .OrderBy(q => q.Id, System.StringComparer.Ordinal).ToList();
            if (quests.Count == 0) warn.Add("keine Aufträge mit Kennzeichen westweiden gefunden");
            if (figuren.Count == 0) warn.Add("keine Figuren in den geladenen Szenen – ist die Zelle geöffnet?");

            // Gegenstände, die ein Auftrag mitgibt oder als Belohnung gibt (kein Ort draußen)
            var ausHand = new HashSet<string>();
            foreach (QuestDefinition q in quests)
                foreach (QuestItemReward r in q.startItems.Concat(q.rewardItems))
                    if (r.item != null) ausHand.Add(r.item.Id);
            var auftraege = new List<FuehrungAuftrag>();
            foreach (QuestDefinition q in quests)
            {
                string stufe = q.unmarked ? "C" : Stufen.TryGetValue(q.Id, out string s0) ? s0 : "A";
                var a = new FuehrungAuftrag { quest = q.Id, stufe = stufe, kreise = new FuehrungKreis[0] };
                if (stufe == "A")
                {
                    var kreise = new List<FuehrungKreis>();
                    Vector2? letztes = null;
                    for (int i = 0; i < q.objectives.Count; i++)
                    {
                        QuestObjective o = q.objectives[i];
                        List<(Vector2 m, float r)> ziel = Zielgebiete(o, figuren, fundItem, fundStory, gebiete, beute);
                        if (Gebiet.TryGetValue(q.Id, out int[] uz) && dienst != null)
                        {
                            var drin = ziel.FindAll(z => System.Array.IndexOf(uz, dienst.UnterzoneBei(z.m.x + v.x, z.m.y + v.y)?.id ?? -1) >= 0);
                            if (drin.Count == 0) warn.Add($"{q.Id}: kein Zielgebiet in Unterzone {string.Join("/", uz)}");
                            else ziel = drin;
                        }
                        if (ziel.Count > MaxKreise && figuren.TryGetValue(q.giverNpc ?? "", out Vector2 geber))
                        {
                            ziel.Sort((p1, p2) => Vector2.Distance(p1.m, geber).CompareTo(Vector2.Distance(p2.m, geber)));
                            warn.Add($"{q.Id}: {ziel.Count} Zielgebiete – die {MaxKreise} nächsten beim Auftraggeber");
                            ziel = ziel.GetRange(0, MaxKreise);
                        }
                        if (ziel.Count == 0)
                        {
                            // Gegenstand aus der Hand eines anderen Auftrags (Hufeisen in W05): kein Ziel draußen, der Abgabekreis führt
                            if (o.type != QuestObjectiveType.Collect || !ausHand.Contains(o.targetId ?? ""))
                                warn.Add($"{q.Id} Ziel {i} ({o.type} {o.targetId}) ohne Lage in der Zelle");
                            continue;
                        }
                        int nr = q.objectives.Count > 1 ? i : -1;
                        foreach ((Vector2 m, float r) in ziel)
                        {
                            // Richtung des Versatzes aus Kennung + Ziel + laufender Nummer (fest je Kreis), angezeigt wird das Ziel
                            FuehrungKreis k = KarethV4Fuehrung.Kreis(q.Id, i * 100 + kreise.Count, m.x, m.y, r, v);
                            k.ziel = nr;
                            kreise.Add(k);
                        }
                        letztes = ziel[ziel.Count - 1].m;
                    }
                    a.kreise = kreise.ToArray();
                    string abgabe = q.TurnInNpc;
                    if (!string.IsNullOrEmpty(abgabe))
                    {
                        if (!figuren.TryGetValue(abgabe, out Vector2 fig))
                        {
                            if (!abgabe.StartsWith("npc.westweiden.", System.StringComparison.Ordinal)) { }   // Abgabe außerhalb der Zelle (Kareth): kein Kreis
                            else warn.Add($"{q.Id}: Abgabefigur {abgabe} nicht in der Zelle");
                        }
                        else if (letztes == null || Vector2.Distance(fig, letztes.Value) > AbgabeAbstand)
                            a.abgabe = new FuehrungKreis { ziel = -1, x = Mathf.Round(fig.x + v.x), z = Mathf.Round(fig.y + v.y), radius = AbgabeRadius };
                    }
                }
                auftraege.Add(a);
            }
            return new FuehrungDatei { version = 1, stand = Stand, szene = KarethV4Spiel.KartenWestweidenRaster, auftraege = auftraege.ToArray() };
        }

        /// <summary>Zielgebiete eines Ziels (Szenen-Meter, Mitte und Radius); weit auseinanderliegende Lagen getrennt.</summary>
        private static List<(Vector2 m, float r)> Zielgebiete(QuestObjective o, Dictionary<string, Vector2> figuren,
            Dictionary<string, List<Vector2>> fundItem, Dictionary<string, List<Vector2>> fundStory,
            Dictionary<string, List<(Vector2 p, float r, Vector2[] verstecke)>> gebiete, Dictionary<string, List<string>> beute)
        {
            var punkte = new List<(Vector2 p, float r)>();
            void Tiere(string enemy)
            {
                if (!gebiete.TryGetValue(enemy, out var l)) return;
                foreach (var g in l)
                    if (g.verstecke.Length > 1) foreach (Vector2 h in g.verstecke) punkte.Add((h, 0f));   // seltenes Tier: ein Kreis je Versteck
                    else punkte.Add((g.p, g.r));
            }
            switch (o.type)
            {
                case QuestObjectiveType.TalkTo:
                    if (figuren.TryGetValue(o.targetId ?? "", out Vector2 f)) punkte.Add((f, 20f));
                    break;
                case QuestObjectiveType.Kill:
                    Tiere(o.targetId ?? "");
                    break;
                case QuestObjectiveType.Collect:
                    if (fundItem.TryGetValue(o.targetId ?? "", out var fl)) foreach (Vector2 p in fl) punkte.Add((p, 5f));
                    else if (beute.TryGetValue(o.targetId ?? "", out var tiere)) foreach (string t in tiere) Tiere(t);
                    break;
                case QuestObjectiveType.Story:
                    if (fundStory.TryGetValue(o.targetId ?? "", out var sl)) foreach (Vector2 p in sl) punkte.Add((p, 5f));
                    break;
            }
            // zu Haufen zusammenfassen (Abstand der Mitten ≤ 300 m); Versteck-Punkte (r 0) bleiben einzeln
            var haufen = new List<List<(Vector2 p, float r)>>();
            foreach (var pt in punkte)
            {
                List<(Vector2 p, float r)> h = pt.r <= 0f ? null : haufen.FirstOrDefault(x => x[0].r > 0f && Vector2.Distance(Mitte(x), pt.p) <= Haufen);
                if (h == null) haufen.Add(new List<(Vector2, float)> { pt });
                else h.Add(pt);
            }
            var res = new List<(Vector2 m, float r)>();
            foreach (var h in haufen)
            {
                Vector2 m = Mitte(h);
                float r = h.Max(x => Vector2.Distance(m, x.p) + x.r);
                res.Add((m, Mathf.Max(r, 20f)));
            }
            return res;
        }

        private static Vector2 Mitte(List<(Vector2 p, float r)> h)
        {
            Vector2 s = Vector2.zero;
            foreach (var x in h) s += x.p;
            return s / h.Count;
        }

        private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

        private static void Add(Dictionary<string, List<Vector2>> d, string k, Vector2 p)
        {
            if (!d.TryGetValue(k, out var l)) d[k] = l = new List<Vector2>();
            l.Add(p);
        }
    }
}
