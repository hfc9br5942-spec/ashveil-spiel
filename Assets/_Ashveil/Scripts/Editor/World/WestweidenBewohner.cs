using System.Collections.Generic;
using Ashveil.Gameplay.Npcs;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using K = Ashveil.EditorTools.GreyboxKit;
using WA = Ashveil.EditorTools.WestweidenAuftraege;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden, Lückenliste Punkt 3 (13.10.2026, Max: „bis 10 Uhr ohne mich weiter“): Bewohner als Platzhalter mit Tagesablauf –
    /// Bäckerei, Schmiede, Gasthaus, Brunnen, Bürgerwehr mit Turmwache und Streife zum Posten, Müllerburschen zwischen Mühlenrain und
    /// Kornspeicher, Feldarbeit an der Hofstraße und am Dreispannhof, Knechte und Mägde auf den Höfen, Hirten auf dem Nordrücken,
    /// Wachen und Vorspannknecht am Posten. Nur Rollen, keine Eigennamen (Figuren mit Namen und Bogen: Max). VORSCHLAG Claude.
    /// Wie in Kareth: alles aus der Weltuhr (NpcRoutine, kein Netzverkehr), Wege über das NavMesh der Zelle, wer schläft, ist im Haus
    /// (ausgeblendet an der Tür). Bewusst NICHT: kein fahrender Kornwagen (der Wagen liegt in der Geschichte fest, K1/K2), keine
    /// Kinder (es gibt noch keine Kinderkörper). Plätze: Ortsmitte bzw. Haus aus den Bauplänen (<see cref="WestweidenOrte.HausLage"/>),
    /// dann der nächste freie Fleck; gemeinsame Plätze (Gasthaus, Brunnen, Feuer) vergeben Ringplätze, damit niemand ineinander steht.
    /// Aufruf aus <see cref="WestweidenZelle"/> nach den Auftragsfiguren (deren Helfer setzen Gelände und Versatz), vor dem NavMesh.
    /// </summary>
    public static class WestweidenBewohner
    {
        public const string RootName = "Bewohner (Westweiden, Tagesabläufe)";

        private static Dictionary<string, Vector3> _orte;
        private static Vector3 _v;
        private static readonly Dictionary<string, int> RingBelegt = new Dictionary<string, int>();
        private static readonly Dictionary<string, (Vector3 p, float yaw)> Cache = new Dictionary<string, (Vector3, float)>();
        private static readonly List<string> Warnungen = new List<string>();

        public static int Anzahl { get; private set; }

        // ------------------------------------------------------------------ Plätze

        private static Vector2 O(string ort, float dx = 0f, float dz = 0f)
        {
            if (!_orte.TryGetValue(ort, out Vector3 o)) { Warnungen.Add("Ort fehlt: " + ort); return new Vector2(_v.x, _v.z); }
            return new Vector2(o.x + _v.x + dx, o.z + _v.z + dz);
        }

        private static (Vector3 p, float yaw) Fleck(string key, Vector2 xz, Vector2 blick, float radius = 12f)
        {
            if (Cache.TryGetValue(key, out var c)) return c;
            if (!WA.FreierFleck(xz, radius, 0.45f, out Vector3 p)) Warnungen.Add("kein freier Fleck: " + key);
            Vector2 d = blick - new Vector2(p.x, p.z);
            var r = (p, d.sqrMagnitude > 0.01f ? Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg : 0f);
            Cache[key] = r;
            return r;
        }

        /// <summary>Vor einem Haus (Tür): nächster freier Fleck außerhalb der Wände, Blick zum Haus.</summary>
        private static (Vector3 p, float yaw) Haus(string name, float seite = 0f)
        {
            if (!WestweidenOrte.HausLage(name, out string ort, out Vector2 rel, out float halb)) { Warnungen.Add("Haus fehlt: " + name); return Fleck(name, O("L3 Langer Rain"), O("L3 Langer Rain")); }
            Vector2 m = O(ort, rel.x, rel.y);
            Vector2 zurMitte = O(ort) - m;
            Vector2 aussen = m + (zurMitte.sqrMagnitude > 1f ? zurMitte.normalized : Vector2.down) * (halb + 1.2f) + new Vector2(seite, 0f);
            return Fleck("Haus:" + name + ":" + seite, aussen, m, halb + 8f);
        }

        /// <summary>Ringplatz k um einen gemeinsamen Ort (Gasthaus, Brunnen, Feuer): jeder bekommt einen eigenen, alle schauen zur Mitte.</summary>
        private static (Vector3 p, float yaw) Ring(string key, Vector2 mitte, float r = 2.4f)
        {
            RingBelegt.TryGetValue(key, out int k);
            RingBelegt[key] = k + 1;
            float a = k * 2.399f;   // goldener Winkel: Plätze verteilen sich ohne Muster
            float rr = r + (k / 6) * 1.4f;
            Vector2 xz = mitte + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
            return Fleck($"Ring:{key}:{k}", xz, mitte, 6f);
        }

        // ------------------------------------------------------------------ nach dem NavMesh: Plätze auf erreichbare Stellen schieben

        /// <summary>
        /// Nach dem Backen (Lauf op_510: 30 von 167 Wegen unvollständig – manche freie Flecken lagen in Taschen zwischen Haus, Zaun und
        /// Hecke, die das NavMesh nicht mit dem Ort verbindet). Jeder Platz wird vom nächsten Ortskern aus erreicht; geht das nicht
        /// ganz, rückt er an den nächsten erreichbaren Punkt (Ende des Teilwegs). Rückgabe: Bericht.
        /// </summary>
        public static string NachDemNavMesh(Transform root)
        {
            Transform b = root != null ? root.Find(RootName) : null;
            if (b == null || _orte == null) return "Bewohner: nichts nachzubessern";
            var kerne = new List<Vector3>();
            foreach (Vector3 o in _orte.Values)
                if (NavMesh.SamplePosition(o + _v, out NavMeshHit h, 12f, NavMesh.AllAreas)) kerne.Add(h.position);
            var path = new NavMeshPath();
            int gerueckt = 0, weit = 0, plaetze = 0;
            foreach (NpcRoutine r in b.GetComponentsInChildren<NpcRoutine>(true))
            {
                var so = new SerializedObject(r);
                SerializedProperty steps = so.FindProperty("steps");
                for (int i = 0; i < steps.arraySize; i++)
                {
                    SerializedProperty pos = steps.GetArrayElementAtIndex(i).FindPropertyRelative("pos");
                    Vector3 p = pos.vector3Value;
                    plaetze++;
                    Vector3 kern = Vector3.zero; float bd = float.MaxValue;
                    foreach (Vector3 k in kerne) { float d = (k - p).sqrMagnitude; if (d < bd) { bd = d; kern = k; } }
                    if (bd == float.MaxValue) continue;
                    bool ziel = NavMesh.SamplePosition(p, out NavMeshHit hp, 2f, NavMesh.AllAreas);
                    if (ziel && NavMesh.CalculatePath(kern, hp.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) continue;
                    if (!NavMesh.CalculatePath(kern, ziel ? hp.position : p, NavMesh.AllAreas, path) || path.corners.Length == 0) continue;
                    Vector3 neu = path.corners[path.corners.Length - 1];
                    if (Vector3.Distance(neu, p) > 15f) { weit++; Warnungen.Add($"{r.name}: Platz „{steps.GetArrayElementAtIndex(i).FindPropertyRelative("note").stringValue}“ um {Vector3.Distance(neu, p):0} m gerückt"); }
                    pos.vector3Value = neu;
                    gerueckt++;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                if (r.StepCount > 0) r.transform.position = r.Step(0).pos;
            }
            foreach (string w in Warnungen) Debug.LogWarning("[Ashveil] Westweiden-Bewohner: " + w);
            return $"Bewohner-Plätze nach dem NavMesh: {gerueckt}/{plaetze} an erreichbare Stellen gerückt" + (weit > 0 ? $" ({weit} weiter als 15 m)" : "");
        }

        // ------------------------------------------------------------------ Bauen

        private static RoutineStep S(float ab, (Vector3 p, float yaw) wo, string tun, string notiz, bool traegt = false) =>
            new RoutineStep { from = ab, pos = wo.p, yaw = wo.yaw, activity = tun, note = notiz, carry = traegt };

        private static readonly string[] Wehr = { "chainmail_shirt", "travel_trousers", "iron_sabatons", "grey_wool_cloak", "rope_belt" };
        private static readonly string[] Bauer = { "frayed_gambeson", "travel_trousers", "foot_wraps", "rope_belt" };
        private static readonly string[] Handwerk = { "ashen_leather_jerkin", "travel_trousers", "stalker_boots", "stalker_gloves" };
        private static readonly string[] Alt = { "travel_mantle", "weaver_robe", "weaver_sandals", "rope_belt" };
        private static readonly string[] Hirte = { "travel_hood", "travel_mantle", "hunter_breeches", "stalker_boots" };

        private static string Look(bool frau, int saat)
        {
            var r = new System.Random(saat * 7919);
            return frau ? $"1|b1|s{r.Next(0, 5)}|h{r.Next(0, 7)}|c{r.Next(0, 6)}|a{r.Next(0, 6)}|e{r.Next(0, 4)}|m{r.Next(0, 4)}"
                        : $"1|b0|s{r.Next(0, 5)}|h{r.Next(0, 7)}|c{r.Next(0, 6)}|d{r.Next(0, 5)}|a{r.Next(0, 6)}|e{r.Next(0, 4)}|m{r.Next(0, 2)}";
        }

        public static string Build(Transform parent, Dictionary<string, Vector3> orte, Vector3 versatz)
        {
            _orte = orte;
            _v = versatz;
            RingBelegt.Clear();
            Cache.Clear();
            Warnungen.Clear();
            Physics.SyncTransforms();
            Transform root = K.Group(RootName, parent);
            root.position = Vector3.zero;
            var liste = new List<(string name, string titel, bool frau, string[] gear, RoutineStep[] steps)>();
            void B(string name, string titel, bool frau, string[] gear, params RoutineStep[] steps) => liste.Add((name, titel, frau, gear, steps));

            // ---------------------------------------------------------- Langer Rain (Mühlplatz, Hofstraße)
            Vector2 lr = O("L3 Langer Rain");
            Vector2 brunnen = lr, feuer = O("L3 Langer Rain", -8f, -10f), gasthausHof = O("L3 Langer Rain", 30f, 14f);
            List<Vector2> hofstrasse = WA.Weg("Hofstraße (Silberweg)");
            List<Vector2> bach = WA.Bach();
            var bachLr = Fleck("Bach bei Langer Rain", WA.AmWeg(bach, lr, 70f, 2.5f), WA.AmWeg(bach, lr, 70f, 0f), 8f);
            var bachLr2 = Fleck("Bach bei Langer Rain 2", WA.AmWeg(bach, lr, -60f, 2.5f), WA.AmWeg(bach, lr, -60f, 0f), 8f);
            var feldOst = Fleck("Feld an der Hofstraße Ost", WA.AmWeg(hofstrasse, lr, 190f, 28f), WA.AmWeg(hofstrasse, lr, 190f, 60f), 14f);
            var feldWest = Fleck("Feld an der Hofstraße West", WA.AmWeg(hofstrasse, lr, -190f, -28f), WA.AmWeg(hofstrasse, lr, -190f, -60f), 14f);
            var hecke = Fleck("Hecke an der Hofstraße", WA.AmWeg(hofstrasse, lr, 280f, 14f), WA.AmWeg(hofstrasse, lr, 280f, 30f), 14f);

            B("Bäckerin", "Langer Rain", true, Bauer,
                S(0f, Haus("Bäckerei"), "Sleep", "schläft"),
                S(4.75f, Haus("Bäckerei"), "Interact|Stand", "backt"),
                S(9.5f, Ring("Brunnen", brunnen), "Talk|Stand", "verkauft Brot am Brunnen"),
                S(13f, Haus("Bäckerei"), "Interact|Stand", "backt für den Abend"),
                S(19f, Ring("Gasthaus", gasthausHof), "SitTalk", "abends am Gasthaus"),
                S(21.75f, Haus("Bäckerei"), "Sleep", "schläft"));
            B("Bäckerbursche", "Langer Rain", false, Bauer,
                S(0f, Haus("Wohnhaus 5"), "Sleep", "schläft"),
                S(5.25f, Haus("Kornspeicher"), "Interact", "holt Mehl im Kornspeicher"),
                S(6.25f, Haus("Bäckerei", 2f), "Interact|Stand", "bringt Mehl, knetet", true),
                S(11f, Haus("Gasthaus Zum Mühlstein"), "Talk", "bringt Brot ins Gasthaus", true),
                S(11.5f, Haus("Bäckerei", 2f), "Interact|Stand", "in der Backstube"),
                S(17f, Ring("Brunnen", brunnen), "Talk|Stand", "am Brunnen"),
                S(20.25f, Haus("Wohnhaus 5"), "Sleep", "schläft"));
            B("Schmiedgeselle", "Langer Rain", false, Handwerk,
                S(0f, Haus("Wohnhaus 1"), "Sleep", "schläft"),
                S(6f, Haus("Schmiede", 3f), "Interact|Stand", "am Amboss"),
                S(12f, Ring("Gasthaus", gasthausHof), "SitTalk", "Mittag am Gasthaus"),
                S(13f, Haus("Schmiede", 3f), "Interact|Stand", "am Amboss"),
                S(18.5f, Ring("Feuer", feuer), "SitTalk", "abends am Wachtfeuer"),
                S(22f, Haus("Wohnhaus 1"), "Sleep", "schläft"));
            B("Wehrmann", "Bürgerwehr (Turmwache)", false, Wehr,
                S(0f, Haus("Wachturm Bürgerwehr"), "Torch|Stand", "Nachtwache am Turm"),
                S(7.5f, Haus("Bürgerwehr-Haus"), "Sleep", "schläft nach der Nachtwache"),
                S(15f, Ring("Feuer", feuer), "SitTalk", "am Wachtfeuer"),
                S(18f, Haus("Wachturm Bürgerwehr"), "Torch|FoldArms", "Nachtwache am Turm"));
            B("Wehrfrau", "Bürgerwehr", true, Wehr,
                S(0f, Haus("Bürgerwehr-Haus", 2f), "Sleep", "schläft"),
                S(6f, Haus("Wachturm Bürgerwehr", 2f), "FoldArms|Stand", "Tagwache am Turm"),
                S(12f, Ring("Gasthaus", gasthausHof), "SitTalk", "Mittag"),
                S(13f, Ring("Brunnen", brunnen, 4f), "FoldArms|Stand", "hält den Platz im Blick"),
                S(18f, Ring("Feuer", feuer), "SitTalk", "abends am Wachtfeuer"),
                S(22f, Haus("Bürgerwehr-Haus", 2f), "Sleep", "schläft"));
            // Streife der Bürgerwehr (Gebietsblatt v2, Weltleben): zweimal am Tag zum Posten über der Vorspannrampe und zurück
            var posten = Fleck("Posten (Meldung)", O("L10 Bürgerwehr-Posten", 2f, 2f), O("L10 Bürgerwehr-Posten", 0f, 18f), 10f);
            var posten2 = Fleck("Posten (Meldung 2)", O("L10 Bürgerwehr-Posten", 6f, 0f), O("L10 Bürgerwehr-Posten", 0f, 18f), 10f);
            for (int i = 0; i < 2; i++)
            {
                float t0 = i * 1.5f;
                B("Streifengänger", "Bürgerwehr (Streife)", i == 1, Wehr,
                    S(0f, Haus("Bürgerwehr-Haus", -2f - i * 2f), "Sleep", "schläft"),
                    S(7f + t0, Ring("Brunnen", brunnen, 4f), "FoldArms|Stand", "wartet auf die Streife"),
                    S(7.5f + t0, i == 0 ? posten : posten2, "Talk|FoldArms", "Streife: meldet am Posten"),
                    S(10.5f + t0, Haus("Bürgerwehr-Haus", -2f - i * 2f), "Talk|Stand", "Streife zurück, meldet in Langer Rain"),
                    S(14f + t0, i == 0 ? posten : posten2, "Talk|FoldArms", "zweite Streife am Posten"),
                    S(17f + t0, Ring("Gasthaus", gasthausHof), "SitTalk", "nach der Streife im Gasthaus"),
                    S(22f, Haus("Bürgerwehr-Haus", -2f - i * 2f), "Sleep", "schläft"));
            }
            B("Wirtsmagd", "Zum Mühlstein", true, Bauer,
                S(0f, Haus("Gasthaus Zum Mühlstein", 3f), "Sleep", "schläft"),
                S(8f, Haus("Gasthaus Zum Mühlstein", 3f), "Interact|Stand", "fegt vor dem Gasthaus"),
                S(10f, Ring("Brunnen", brunnen), "Interact", "holt Wasser am Brunnen"),
                S(10.5f, Haus("Gasthaus Zum Mühlstein", 3f), "Stand|Talk", "bringt Wasser", true),
                S(12f, Ring("Gasthaus", gasthausHof, 4f), "Talk|Stand", "bedient"),
                S(23f, Haus("Gasthaus Zum Mühlstein", 3f), "Sleep", "schläft"));
            B("Reisender", "Gast im Mühlstein", false, Alt,
                S(0f, Haus("Gasthaus Zum Mühlstein", -3f), "Sleep", "schläft im Gasthaus"),
                S(8.5f, Haus("Händler und Post"), "Talk|Stand", "beim Händler"),
                S(11f, Ring("Brunnen", brunnen), "Sit", "rastet am Brunnen"),
                S(14f, Haus("Stall und Karrenhalt"), "FoldArms|Stand", "sieht nach seinem Karren"),
                S(18f, Ring("Gasthaus", gasthausHof), "SitTalk", "im Gasthaus"),
                S(22.5f, Haus("Gasthaus Zum Mühlstein", -3f), "Sleep", "schläft im Gasthaus"));
            B("Fischer", "Mühlbach", false, Hirte,
                S(0f, Haus("Fischerhütte am Mühlbach"), "Sleep", "schläft"),
                S(5f, bachLr, "Interact|Kneel", "fischt im Mühlbach"),
                S(11f, Haus("Fischerhütte am Mühlbach"), "Sit", "flickt Netze"),
                S(13f, bachLr, "Interact|Kneel", "fischt im Mühlbach"),
                S(17f, Haus("Gasthaus Zum Mühlstein"), "Talk", "bringt Fisch ins Gasthaus", true),
                S(17.5f, Ring("Gasthaus", gasthausHof), "SitTalk", "im Gasthaus"),
                S(21f, Haus("Fischerhütte am Mühlbach"), "Sleep", "schläft"));
            B("Lehrer", "Langer Rain", false, Alt,
                S(0f, Haus("Lehrerhaus"), "Sleep", "schläft"),
                S(8f, Haus("Lehrerhaus"), "Talk|Stand", "unterrichtet vor dem Haus"),
                S(12f, Ring("Gasthaus", gasthausHof), "SitTalk", "Mittag"),
                S(13f, Haus("Lehrerhaus"), "Talk|Stand", "unterrichtet"),
                S(16f, Ring("Brunnen", brunnen), "Sit", "liest am Brunnen"),
                S(19f, Ring("Gasthaus", gasthausHof), "SitTalk", "im Gasthaus"),
                S(22f, Haus("Lehrerhaus"), "Sleep", "schläft"));
            B("Händlergehilfe", "Händler und Post", false, Handwerk,
                S(0f, Haus("Händler und Post", 2f), "Sleep", "schläft"),
                S(7f, Haus("Stall und Karrenhalt", 3f), "Interact", "holt Ware am Karrenhalt"),
                S(8f, Haus("Händler und Post", 2f), "Interact|Stand", "räumt Ware ein", true),
                S(12f, Ring("Brunnen", brunnen), "Talk", "am Brunnen"),
                S(13f, Haus("Händler und Post", 2f), "Interact|Stand", "im Laden"),
                S(18f, Ring("Feuer", feuer), "SitTalk", "abends am Wachtfeuer"),
                S(22f, Haus("Händler und Post", 2f), "Sleep", "schläft"));
            B("Alte vom Brunnen", "Langer Rain", true, Alt,
                S(0f, Haus("Wohnhaus 5", 2f), "Sleep", "schläft"),
                S(9f, Ring("Brunnen", brunnen), "Sit|SitTalk", "sitzt am Brunnen"),
                S(12f, Haus("Wohnhaus 5", 2f), "Sit", "sitzt vor dem Haus"),
                S(14f, Ring("Brunnen", brunnen), "SitTalk|Sit", "am Brunnen"),
                S(18f, Haus("Wohnhaus 5", 2f), "Sit", "vor dem Haus"),
                S(20f, Haus("Wohnhaus 5", 2f), "Sleep", "schläft"));
            B("Wäscherin", "Langer Rain", true, Bauer,
                S(0f, Haus("Wohnhaus 6"), "Sleep", "schläft"),
                S(7f, bachLr2, "Kneel|Interact", "wäscht am Mühlbach"),
                S(11f, Haus("Wohnhaus 6"), "Interact|Stand", "hängt Wäsche auf", true),
                S(15f, bachLr2, "Kneel|Interact", "wäscht am Mühlbach"),
                S(18f, Ring("Gasthaus", gasthausHof), "SitTalk", "im Gasthaus"),
                S(21f, Haus("Wohnhaus 6"), "Sleep", "schläft"));
            B("Stallbursche", "Karrenhalt", false, Bauer,
                S(0f, Haus("Stall und Karrenhalt", -3f), "Sleep", "schläft im Stall"),
                S(5.5f, Haus("Stall und Karrenhalt", -3f), "Interact|Stand", "versorgt die Zugtiere"),
                S(12f, Ring("Feuer", feuer), "SitTalk", "Mittag am Feuer"),
                S(13f, Haus("Stall und Karrenhalt", -3f), "Interact|Stand", "am Karrenhalt"),
                S(19f, Haus("Stall und Karrenhalt", -3f), "Sit", "sitzt am Stall"),
                S(21f, Haus("Stall und Karrenhalt", -3f), "Sleep", "schläft im Stall"));
            B("Kräutersammlerin", "Kräuterhaus", true, Alt,
                S(0f, Haus("Kräuterhaus"), "Sleep", "schläft"),
                S(7f, hecke, "Kneel|Interact", "sammelt an der Hecke"),
                S(11f, Haus("Kräuterhaus"), "Interact|Stand", "bündelt Kräuter", true),
                S(15f, bachLr, "Kneel", "sammelt am Bach"),
                S(18f, Haus("Kräuterhaus"), "Interact|Stand", "trocknet Kräuter"),
                S(21f, Haus("Kräuterhaus"), "Sleep", "schläft"));
            B("Feldarbeiter", "Hofstraße", false, Bauer,
                S(0f, Haus("Wohnhaus 3"), "Sleep", "schläft"),
                S(6f, feldOst, "Interact|Kneel", "arbeitet auf dem Feld"),
                S(12f, feldOst, "Sit", "Mittag am Feldrand"),
                S(12.75f, feldOst, "Interact|Kneel", "arbeitet auf dem Feld"),
                S(18f, Ring("Gasthaus", gasthausHof), "SitTalk", "im Gasthaus"),
                S(21.5f, Haus("Wohnhaus 3"), "Sleep", "schläft"));
            B("Feldarbeiterin", "Hofstraße", true, Bauer,
                S(0f, Haus("Wohnhaus 4"), "Sleep", "schläft"),
                S(6.25f, feldWest, "Kneel|Interact", "arbeitet auf dem Feld"),
                S(12f, feldWest, "Sit", "Mittag am Feldrand"),
                S(12.75f, feldWest, "Kneel|Interact", "arbeitet auf dem Feld"),
                S(18.25f, Ring("Feuer", feuer), "SitTalk", "abends am Wachtfeuer"),
                S(21.5f, Haus("Wohnhaus 4"), "Sleep", "schläft"));

            // ---------------------------------------------------------- Mühlenrain: Müllerburschen zwischen den Mühlen und Langer Rain
            var muehle1 = Fleck("Mühle 1", O("L1 Windmühlen", -60f, -9f), O("L1 Windmühlen", -60f, 0f), 10f);
            var muehle3 = Fleck("Mühle 3", O("L1 Windmühlen", 60f, -9f), O("L1 Windmühlen", 60f, 0f), 10f);
            var muehleRast = Fleck("Mühlenrain Rast", O("L1 Windmühlen", 0f, 8f), O("L1 Windmühlen", 0f, 25f), 10f);
            for (int i = 0; i < 2; i++)
            {
                float t0 = i * 0.75f;
                var meine = i == 0 ? muehle1 : muehle3;
                B("Müllerbursche", "Mühlenrain", false, Bauer,
                    S(0f, Haus(i == 0 ? "Wohnhaus 6" : "Wohnhaus 2", 2f), "Sleep", "schläft"),
                    S(5.5f + t0, meine, "Interact|Stand", "an der Mühle"),
                    S(10f + t0, Haus("Kornspeicher", 2f + i * 2f), "Interact", "bringt Mehl zum Kornspeicher", true),
                    S(10.75f + t0, meine, "Interact|Stand", "an der Mühle"),
                    S(13f, muehleRast, "SitTalk", "Mittag am Mühlenrain"),
                    S(13.75f, meine, "Interact|Stand", "an der Mühle"),
                    S(17f + t0, Haus("Bäckerei", -2f - i * 2f), "Interact", "bringt Mehl zur Bäckerei", true),
                    S(18f + t0, Ring("Gasthaus", gasthausHof), "SitTalk", "im Gasthaus"),
                    S(21.5f + t0, Haus(i == 0 ? "Wohnhaus 6" : "Wohnhaus 2", 2f), "Sleep", "schläft"));
            }

            // ---------------------------------------------------------- Hallgrund (Pferdehof)
            var stuten = Fleck("Koppel Stuten (Rand)", O("L4 Hallgrund", -42f, -20f), O("L4 Hallgrund", -70f, -40f), 14f);
            var fohlen = Fleck("Koppel Fohlen (Rand)", O("L4 Hallgrund", -10f, -60f), O("L4 Hallgrund", -20f, -80f), 14f);
            var traenke = Fleck("Tränke Hallgrund", O("L4 Hallgrund", -36f, -58f), O("L4 Hallgrund", -40f, -62f), 8f);
            var hgHof = O("L4 Hallgrund", -4f, 12f);
            B("Pferdeknecht", "Hallgrund", false, Bauer,
                S(0f, Haus("Hallgrund Wohnhaus"), "Sleep", "schläft"),
                S(5.5f, Haus("Hallgrund Pferdestall"), "Interact|Stand", "füttert im Stall"),
                S(8f, stuten, "FoldArms|Stand", "bei den Stuten"),
                S(12f, Ring("Hallgrund Hof", hgHof), "SitTalk", "Mittag auf dem Hof"),
                S(13f, traenke, "Interact", "füllt die Tränke"),
                S(14f, fohlen, "FoldArms|Interact", "bei den Fohlen"),
                S(17.5f, Haus("Hallgrund Pferdestall"), "Interact|Stand", "holt die Pferde herein"),
                S(21f, Haus("Hallgrund Wohnhaus"), "Sleep", "schläft"));
            B("Magd", "Hallgrund", true, Bauer,
                S(0f, Haus("Hallgrund Wohnhaus", 2f), "Sleep", "schläft"),
                S(6f, Haus("Hallgrund Wohnhaus", 2f), "Interact|Stand", "am Haus"),
                S(9f, Haus("Hallgrund Scheune"), "Interact", "in der Scheune", true),
                S(11f, Ring("Hallgrund Hof", hgHof), "Interact|Talk", "kocht für den Hof"),
                S(15f, Haus("Hallgrund Remise"), "Interact|Stand", "flickt Geschirr"),
                S(19f, Ring("Hallgrund Hof", hgHof), "SitTalk", "abends auf dem Hof"),
                S(21.5f, Haus("Hallgrund Wohnhaus", 2f), "Sleep", "schläft"));

            // ---------------------------------------------------------- Dreispannhof (Rinderhof, Winddach)
            var winddach = O("L6 Dreispannhof", 0f, 6f);
            var weide = Fleck("Rinderweide (Rand)", O("L6 Dreispannhof", -45f, -8f), O("L6 Dreispannhof", -70f, -10f), 14f);
            List<Vector2> kornspur = WA.Weg("Kornspur");
            var feldDh = Fleck("Feld an der Kornspur", WA.AmWeg(kornspur, O("L6 Dreispannhof"), 140f, 26f), WA.AmWeg(kornspur, O("L6 Dreispannhof"), 140f, 50f), 14f);
            B("Kuhhirte", "Dreispannhof", false, Hirte,
                S(0f, Haus("Dreispannhof Wohnhaus und Hofladen"), "Sleep", "schläft"),
                S(5f, Haus("Dreispannhof Stallflügel Nord (Altbau)"), "Interact|Kneel", "melkt"),
                S(7f, weide, "FoldArms|Stand", "hütet die Rinder"),
                S(12f, weide, "Sit", "Mittag auf der Weide"),
                S(13f, weide, "FoldArms|Stand", "hütet die Rinder"),
                S(17f, Haus("Dreispannhof Stallflügel West (Mittelbau)"), "Interact|Kneel", "melkt"),
                S(20f, Ring("Winddach", winddach), "SitTalk", "unter dem Winddach"),
                S(22f, Haus("Dreispannhof Wohnhaus und Hofladen"), "Sleep", "schläft"));
            B("Knecht", "Dreispannhof", false, Bauer,
                S(0f, Haus("Dreispannhof Wohnhaus und Hofladen", 2f), "Sleep", "schläft"),
                S(6f, Ring("Holzplatz Dreispannhof", O("L6 Dreispannhof", -14f, -40f), 1.5f), "Chop", "hackt Holz"),
                S(9f, Haus("Dreispannhof Stallflügel Ost (Jungbau)"), "Interact|Stand", "mistet aus", true),
                S(12f, Ring("Winddach", winddach), "SitTalk", "Mittag unter dem Winddach"),
                S(13f, feldDh, "Interact|Kneel", "auf dem Feld an der Kornspur"),
                S(19f, Ring("Winddach", winddach), "SitTalk", "abends unter dem Winddach"),
                S(21f, Haus("Dreispannhof Wohnhaus und Hofladen", 2f), "Sleep", "schläft"));
            B("Hofmagd", "Dreispannhof", true, Bauer,
                S(0f, Haus("Dreispannhof Wohnhaus und Hofladen", -2f), "Sleep", "schläft"),
                S(6.5f, Haus("Dreispannhof Wohnhaus und Hofladen", -2f), "Interact|Talk", "im Hofladen"),
                S(12f, Ring("Winddach", winddach), "SitTalk", "Mittag unter dem Winddach"),
                S(13f, Haus("Dreispannhof Wohnhaus und Hofladen", -2f), "Talk|Stand", "im Hofladen"),
                S(18f, Haus("Dreispannhof Stallflügel Nord (Altbau)", 2f), "Interact|Kneel", "melkt am Abend"),
                S(20.5f, Haus("Dreispannhof Wohnhaus und Hofladen", -2f), "Sleep", "schläft"));
            B("Feldarbeiter", "Dreispannhof", false, Bauer,
                S(0f, Haus("Dreispannhof Stallflügel West (Mittelbau)", 2f), "Sleep", "schläft über dem Stall"),
                S(6f, feldDh, "Interact|Kneel", "auf dem Feld an der Kornspur"),
                S(12f, feldDh, "Sit", "Mittag am Feldrand"),
                S(12.75f, feldDh, "Interact|Kneel", "auf dem Feld"),
                S(18.5f, Ring("Winddach", winddach), "SitTalk", "abends unter dem Winddach"),
                S(21.5f, Haus("Dreispannhof Stallflügel West (Mittelbau)", 2f), "Sleep", "schläft über dem Stall"));

            // ---------------------------------------------------------- Heckenhof (Schafhof im Rosenhag)
            var pferch = Fleck("Schafpferch (Rand)", O("L12 Heckenhof (Rosenhag)", -30f, -36f), O("L12 Heckenhof (Rosenhag)", -52f, -48f), 12f);
            var hhHof = O("L12 Heckenhof (Rosenhag)", -6f, -6f);
            B("Schäferknecht", "Heckenhof", false, Hirte,
                S(0f, Haus("Heckenhof Wohnhaus"), "Sleep", "schläft"),
                S(5f, Haus("Heckenhof Schafstall"), "Interact|Stand", "lässt die Schafe hinaus"),
                S(7f, pferch, "FoldArms|Stand", "am Pferch"),
                S(12f, pferch, "Sit", "Mittag am Pferch"),
                S(13f, pferch, "FoldArms|Stand", "am Pferch"),
                S(18f, Haus("Heckenhof Schafstall"), "Interact|Stand", "treibt die Schafe herein"),
                S(21f, Haus("Heckenhof Wohnhaus"), "Sleep", "schläft"));
            B("Scherer", "Heckenhof", false, Handwerk,
                S(0f, Haus("Heckenhof Wohnhaus", 2f), "Sleep", "schläft"),
                S(7f, Haus("Heckenhof Scherschuppen"), "Interact|Kneel", "schert im Schuppen"),
                S(12f, Ring("Heckenhof", hhHof), "SitTalk", "Mittag auf dem Hof"),
                S(13f, Haus("Heckenhof Scherschuppen"), "Interact|Kneel", "schert im Schuppen"),
                S(18f, Haus("Heckenhof Wohnhaus", 2f), "Stand", "trägt Wolle ins Haus", true),
                S(19f, Ring("Heckenhof", hhHof), "SitTalk", "abends auf dem Hof"),
                S(22f, Haus("Heckenhof Wohnhaus", 2f), "Sleep", "schläft"));

            // ---------------------------------------------------------- Hirtenlager am Nordrücken (Sommerweide)
            var hlFeuer = O("L7 Hirtenlager", 8f, 6f);
            var herde1 = Fleck("Herde am Nordrücken Ost", O("L7 Hirtenlager", 50f, -200f), O("L7 Hirtenlager", 50f, -210f), 16f);
            var herde2 = Fleck("Herde am Nordrücken West", O("L7 Hirtenlager", -390f, -10f), O("L7 Hirtenlager", -400f, -10f), 16f);
            var hlPferch = Fleck("Pferch auf dem Rücken (Rand)", O("L7 Hirtenlager", 26f, 30f), O("L7 Hirtenlager", 44f, 40f), 10f);
            B("Hirte", "Hirtenlager", false, Hirte,
                S(0f, Fleck("Zelt 1", O("L7 Hirtenlager", 18f, -8f), O("L7 Hirtenlager", 22f, -4f), 6f), "Sleep", "schläft im Zelt"),
                S(5f, hlPferch, "FoldArms|Stand", "öffnet den Pferch"),
                S(6.5f, herde1, "FoldArms|Stand", "hütet am Nordrücken"),
                S(12f, herde1, "Sit", "rastet bei der Herde"),
                S(13f, herde1, "FoldArms|Stand", "hütet am Nordrücken"),
                S(18.5f, Ring("Hirtenfeuer", hlFeuer), "SitTalk", "am Lagerfeuer"),
                S(22f, Fleck("Zelt 1", O("L7 Hirtenlager", 18f, -8f), O("L7 Hirtenlager", 22f, -4f), 6f), "Sleep", "schläft im Zelt"));
            B("Hirte", "Hirtenlager (Nachtwache)", false, Hirte,
                S(0f, Ring("Hirtenfeuer", hlFeuer), "SitTalk|Sit", "Nachtwache am Feuer"),
                S(6f, Fleck("Zelt 2", O("L7 Hirtenlager", 32f, 4f), O("L7 Hirtenlager", 28f, 8f), 6f), "Sleep", "schläft im Zelt"),
                S(13f, herde2, "FoldArms|Stand", "hütet am Westrücken"),
                S(19f, Ring("Hirtenfeuer", hlFeuer), "SitTalk|Sit", "Nachtwache am Feuer"));
            B("Hirtin", "Hirtenlager", true, Hirte,
                S(0f, Fleck("Zelt 3", O("L7 Hirtenlager", 6f, 36f), O("L7 Hirtenlager", 6f, 30f), 6f), "Sleep", "schläft im Zelt"),
                S(6f, Haus("Hirtenlager Käserei"), "Interact", "bringt Milch zur Käserei", true),
                S(8f, Haus("Hirtenlager Käserei"), "Interact|Stand", "in der Käserei"),
                S(12f, Ring("Hirtenfeuer", hlFeuer), "SitTalk", "Mittag am Feuer"),
                S(13f, hlPferch, "FoldArms|Stand", "am Pferch"),
                S(17f, Haus("Hirtenlager Käserei"), "Interact|Stand", "in der Käserei"),
                S(20f, Ring("Hirtenfeuer", hlFeuer), "SitTalk", "am Lagerfeuer"),
                S(22f, Fleck("Zelt 3", O("L7 Hirtenlager", 6f, 36f), O("L7 Hirtenlager", 6f, 30f), 6f), "Sleep", "schläft im Zelt"));

            // ---------------------------------------------------------- Bürgerwehr-Posten über der Vorspannrampe
            var feuerkorb = O("L10 Bürgerwehr-Posten", 6f, 12f);
            var schlagbaum = Fleck("Schlagbaum", O("L10 Bürgerwehr-Posten", 2f, 6f), O("Passfuß (Heckenkönig)"), 8f);
            var vorspann = Fleck("Koppel Vorspannpferde (Rand)", O("L10 Bürgerwehr-Posten", -4f, -30f), O("L10 Bürgerwehr-Posten", -20f, -38f), 10f);
            B("Postenwache", "Bürgerwehr-Posten", false, Wehr,
                S(0f, Haus("Posten Wachhaus"), "Sleep", "schläft"),
                S(6f, schlagbaum, "FoldArms|Stand", "am Schlagbaum"),
                S(12f, Ring("Feuerkorb", feuerkorb), "SitTalk", "Mittag am Feuerkorb"),
                S(13f, Haus("Posten Wachturm"), "FoldArms|Stand", "am Turm"),
                S(19f, Ring("Feuerkorb", feuerkorb), "SitTalk", "am Feuerkorb"),
                S(22f, Haus("Posten Wachhaus"), "Sleep", "schläft"));
            B("Postenwache", "Bürgerwehr-Posten (Nacht)", true, Wehr,
                S(0f, Haus("Posten Wachturm", 2f), "Torch|Stand", "Nachtwache am Turm"),
                S(7f, Haus("Posten Wachhaus", 2f), "Sleep", "schläft nach der Nachtwache"),
                S(15f, Ring("Feuerkorb", feuerkorb), "SitTalk", "am Feuerkorb"),
                S(18f, schlagbaum, "Torch|FoldArms", "Nachtwache am Schlagbaum"));
            B("Vorspannknecht", "Bürgerwehr-Posten", false, Bauer,
                S(0f, Haus("Posten Wachhaus", -2f), "Sleep", "schläft"),
                S(5.5f, Fleck("Unterstand Vorspannpferde", O("L10 Bürgerwehr-Posten", 10f, -14f), O("L10 Bürgerwehr-Posten", 10f, -22f), 8f), "Interact|Stand", "schirrt die Vorspannpferde an"),
                S(9f, vorspann, "FoldArms|Stand", "bei den Vorspannpferden"),
                S(12f, Ring("Feuerkorb", feuerkorb), "SitTalk", "Mittag am Feuerkorb"),
                S(13f, Fleck("Unterstand Vorspannpferde", O("L10 Bürgerwehr-Posten", 10f, -14f), O("L10 Bürgerwehr-Posten", 10f, -22f), 8f), "Interact|Stand", "am Unterstand"),
                S(18f, vorspann, "FoldArms|Stand", "bei den Vorspannpferden"),
                S(21f, Haus("Posten Wachhaus", -2f), "Sleep", "schläft"));

            // ---------------------------------------------------------- setzen
            int seed = 900;
            foreach (var b in liste)
            {
                SandboxSceneBuilder.WestweidenBewohnerFigur(root, b.name, b.titel, b.frau, Look(b.frau, seed), b.gear, b.steps, seed);
                seed++;
                Physics.SyncTransforms();
            }
            Anzahl = liste.Count;
            string r = $"Bewohner: {liste.Count} mit Tagesablauf ({Cache.Count} Plätze)";
            if (Warnungen.Count > 0)
            {
                r += $" ({Warnungen.Count} Hinweise, z. B. {Warnungen[0]})";
                foreach (string w in Warnungen) Debug.LogWarning("[Ashveil] Westweiden-Bewohner: " + w);
            }
            return r;
        }
    }
}
