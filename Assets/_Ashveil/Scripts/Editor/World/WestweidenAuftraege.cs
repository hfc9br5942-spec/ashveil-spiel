using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Story;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;
using WC = Ashveil.Data.WestweidenCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden (Lückenliste Punkt 2, 12.10.2026): was die Aufträge W01–W28 in der Welt brauchen – 18 Auftragsfiguren an ihren
    /// Orten und die Fundstellen (Zaunpfähle an der Hallgrunder Koppel, Kornsäcke an der Kornspur, Schlammgrundler im Mühlbach unter
    /// der Brücke von Langer Rain, Zulauf der Tränke bei den Kopfweiden am Dreispannhof, Zollbuch in der Ruine der Alten Zollhütte).
    /// Aufruf aus <see cref="WestweidenZelle"/> nach dem Verschieben an die Lage in Kareth (Weltkoordinaten), vor dem NavMesh.
    /// Plätze: Ortsmitte + Versatz (Ortsbaupläne in <see cref="WestweidenOrte"/>), dann der nächste freie Fleck (kein Bauteil, nicht
    /// auf einem Weg, nicht steil) – damit keine Figur in einer Wand steht und die Weg-Prüfung sauber bleibt.
    /// Keine Netzobjekte (Zellen-Regel): Figuren tragen NpcIdentity, Fundstellen StoryAnchor + QuestNodeCondition.
    /// </summary>
    public static class WestweidenAuftraege
    {
        public const string RootName = "Aufträge (Westweiden: Figuren und Fundstellen)";
        private const string DataDir = ProjectSetup.Root + "/Data/Westweiden/Entwurf";

        private static Terrain _t;
        private static Vector3 _v;
        private static readonly List<string> Warnungen = new List<string>();

        public static string Build(Transform parent, Terrain terrain, Vector3 versatz, Dictionary<string, Vector3> orte)
        {
            _t = terrain;
            _v = versatz;
            Warnungen.Clear();
            Physics.SyncTransforms();
            Transform root = K.Group(RootName, parent);
            root.position = Vector3.zero;   // Kinder in Weltkoordinaten (Tagesabläufe rechnen in Welt)
            Transform figuren = K.Group("Figuren", root);
            Transform fund = K.Group("Fundstellen", root);
            Material holz = K.Mat("WW_Fund_Holz", new Color(0.52f, 0.40f, 0.26f));
            Material leinen = K.Mat("WW_Fund_Leinen", new Color(0.78f, 0.70f, 0.52f));
            Material rot = K.Mat("WW_Fund_Hofzeichen", new Color(0.62f, 0.18f, 0.12f));
            Material fisch = K.Mat("WW_Fund_Grundler", new Color(0.30f, 0.30f, 0.26f), 0.5f);
            Material leder = K.Mat("WW_Fund_Leder", new Color(0.38f, 0.26f, 0.16f), 0.2f);
            Material messing = K.Mat("WW_Fund_Messing", new Color(0.72f, 0.58f, 0.30f), 0.6f);
            Material reisig = K.Mat("WW_Fund_Reisig", new Color(0.33f, 0.28f, 0.18f));

            Vector2 O(string ort, float dx = 0f, float dz = 0f)
            {
                if (!orte.TryGetValue(ort, out Vector3 o)) { Warnungen.Add("Ort fehlt: " + ort); return new Vector2(_v.x, _v.z); }
                return new Vector2(o.x + _v.x + dx, o.z + _v.z + dz);
            }

            // ---------------------------------------------------------- Figuren (Look/Ausrüstung: Platzhalter wie Kareth, bis Punkt 6)
            var liste = new List<(SandboxSceneBuilder.WwFigur f, Vector2 xz, Vector2 blick)>();
            void F(string name, string titel, string id, bool frau, string look, string[] gear, Vector2 xz, Vector2 blick, string tun = null, string notiz = null) =>
                liste.Add((new SandboxSceneBuilder.WwFigur { Name = name, Title = titel, NpcId = id, Female = frau, Look = look, Gear = gear, Activity = tun, Note = notiz ?? titel }, xz, blick));

            string[] wehr = { "chainmail_shirt", "travel_trousers", "iron_sabatons", "grey_wool_cloak", "rope_belt" };
            string[] bauer = { "frayed_gambeson", "travel_trousers", "foot_wraps", "rope_belt" };
            string[] handwerk = { "ashen_leather_jerkin", "travel_trousers", "stalker_boots", "stalker_gloves" };
            string[] alt = { "travel_mantle", "weaver_robe", "weaver_sandals", "rope_belt" };
            string[] hirte = { "travel_hood", "travel_mantle", "hunter_breeches", "stalker_boots" };

            Vector2 lr = O("L3 Langer Rain");
            F("Jost", "Wegweiser der Bürgerwehr", WC.Jost, false, "1|b0|s2|h1|c3|d2|a2|e1|m0", wehr, O("L2 Kammsattel", -6f, 5f), O("L4 Hallgrund"));
            F("Brun Haller", "Pferdezüchter", WC.Brun, false, "1|b0|s3|h6|c1|d3|a3|e2|m0", handwerk, O("L4 Hallgrund", 6f, 8f), O("L4 Hallgrund", 40f, 0f));
            F("Stallknecht", "Hallgrund", WC.Stallknecht, false, "1|b0|s1|h2|c2|d0|a0|e0|m1", bauer, O("L4 Hallgrund", 12f, 25f), O("L4 Hallgrund", 1f, 35f), "Interact|Stand");
            F("Harm", "Schmied", WC.Harm, false, "1|b0|s4|h0|c4|d4|a3|e3|m0", handwerk, O("L3 Langer Rain", -39f, -1f), lr, "Interact|Stand", "schmiedet");
            F("Lute", "Sattler", WC.Lute, false, "1|b0|s1|h5|c2|d1|a2|e1|m0", handwerk, O("L3 Langer Rain", -45f, -17f), lr);
            F("Alwine Reder", "Hauptfrau der Bürgerwehr", WC.Alwine, true, "1|b1|s2|h3|c1|a2|e1|m1", wehr, O("L3 Langer Rain", 7f, 4f), O("L3 Langer Rain", 0f, -40f), "FoldArms|Stand", "wartet am Brunnen");
            F("Gerlach", "Händler", WC.Gerlach, false, "1|b0|s0|h4|c3|d2|a4|e2|m0", alt, O("L3 Langer Rain", -12f, 18f), lr);
            F("Tade", "Wirt „Zum Mühlstein“", WC.Tade, false, "1|b0|s3|h2|c4|d3|a3|e1|m0", bauer, O("L3 Langer Rain", 21f, 31f), lr);
            F("Mutter Sanne", "Kräuterfrau", WC.Sanne, true, "1|b1|s4|h5|c2|a5|e3|m0", alt, O("L3 Langer Rain", 14f, 44f), lr);
            F("Stromflüsterer", "Reittiere über die Westweiden", WC.Stromfluesterer, false, "1|b0|s2|h3|c0|d1|a3|e2|m1", hirte, O("L3 Langer Rain", 17f, -30f), lr);
            Vector2 muehle = O("L1 Windmühlen", 0f, 25f);
            F("Meister Hollerbach", "Windmüller", WC.Hollerbach, false, "1|b0|s3|h0|c2|d4|a4|e3|m0", bauer, muehle + new Vector2(8f, -5f), lr);
            F("Marthe", "Bäuerin", WC.Marthe, true, "1|b1|s3|h2|c3|a3|e2|m0", bauer, O("L6 Dreispannhof", 2f, -8f), O("L6 Dreispannhof"));
            F("Wilke", "Altbauer", WC.Wilke, false, "1|b0|s4|h6|c5|d4|a5|e3|m0", alt, O("L6 Dreispannhof", -6f, -13f), O("L6 Dreispannhof"), "Sit|Stand", "sitzt vor dem Wohnhaus");
            F("Imke", "Schäferin", WC.Imke, true, "1|b1|s1|h4|c2|a1|e0|m1", hirte, O("L12 Heckenhof (Rosenhag)", 2f, 2f), O("L12 Heckenhof (Rosenhag)", -8f, -32f));
            F("Fenna Gaul", "Wagenführerin", WC.Fenna, true, "1|b1|s2|h1|c3|a2|e1|m0", handwerk, O("Kornwagen", 5f, 4f), O("Kornwagen"), "FoldArms|Stand", "wacht am Kornwagen");
            F("Ruland", "Postenführer", WC.Ruland, false, "1|b0|s3|h2|c1|d3|a4|e2|m0", wehr, O("L10 Bürgerwehr-Posten", 4f, 7f), O("Passfuß (Heckenkönig)"));
            F("Oberhirte", "Hirtenlager", WC.Oberhirte, false, "1|b0|s4|h6|c4|d4|a4|e2|m0", hirte, O("L7 Hirtenlager", 13f, 8f), O("L8 Steinriegel"));
            F("Brigge", "Käserin", WC.Brigge, true, "1|b1|s3|h3|c2|a3|e2|m0", bauer, O("L7 Hirtenlager", -6f, 13f), O("L7 Hirtenlager"));

            int seed = 700, figurenOk = 0;
            foreach ((SandboxSceneBuilder.WwFigur f0, Vector2 xz, Vector2 blick) in liste)
            {
                SandboxSceneBuilder.WwFigur f = f0;
                if (!FreierFleck(xz, 14f, 0.6f, out Vector3 p)) { Warnungen.Add("kein freier Platz für " + f.Name); p = Boden(xz); }
                Vector2 d = blick - new Vector2(p.x, p.z);
                f.Pos = p;
                f.Yaw = d.sqrMagnitude > 0.01f ? Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg : 0f;
                SandboxSceneBuilder.WestweidenFigur(figuren, f, seed++);
                Physics.SyncTransforms();   // nächste Figur sieht diese als belegt
                figurenOk++;
            }

            // ---------------------------------------------------------- Fundstellen
            int knoten = 0;
            var rng = new System.Random(4211);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // W02: Zaunpfähle an der eingerissenen Koppel östlich von Hallgrund, bis hinunter zum Bach (Westen/Südwesten)
            Vector2 hg = O("L4 Hallgrund");
            Vector2 bach = new Vector2(3300f + _v.x, 2290f + _v.z);
            for (int i = 0; i < WC.PostCount; i++)
            {
                Vector2 xz = i < 7 ? hg + new Vector2(R(30f, 70f), R(-25f, 25f)) : Vector2.Lerp(hg, bach, R(0.55f, 0.85f)) + new Vector2(R(-6f, 6f), R(-6f, 6f));
                if (Knoten(fund, WC.NodePost(i), xz, rng, v =>
                    {
                        K.Prim(PrimitiveType.Cylinder, "Zaunpfahl", v, new Vector3(0f, 0.12f, 0f), new Vector3(0.16f, 0.9f, 0.16f), new Vector3(88f, 0f, R(-20f, 20f)), holz, false);
                    })) knoten++;
            }

            // W09: Kornsäcke entlang der Kornspur (westlich und östlich vom Kornwagen, in den Furchen neben dem Weg)
            List<Vector2> kornspur = Weg("Kornspur");
            Vector2 wagen = O("Kornwagen");
            for (int i = 0; i < WC.GrainCount; i++)
            {
                Vector2 xz = AmWeg(kornspur, wagen, R(25f, 260f) * (i % 2 == 0 ? -1f : 1f), R(4f, 9f) * (rng.Next(2) == 0 ? -1f : 1f));
                if (Knoten(fund, WC.NodeGrain(i), xz, rng, v =>
                    {
                        K.Prim(PrimitiveType.Capsule, "Kornsack", v, new Vector3(0f, 0.22f, 0f), new Vector3(0.55f, 0.35f, 0.4f), new Vector3(90f, R(0f, 180f), 0f), leinen, false);
                        if (i % 3 == 0) K.Prim(PrimitiveType.Cube, "Hofzeichen", v, new Vector3(0f, 0.42f, 0f), new Vector3(0.18f, 0.02f, 0.18f), Vector3.zero, rot, false);
                    })) knoten++;
            }

            // W12: Schlammgrundler im Mühlbach unter der Brücke von Langer Rain (watbar, kein WaterVolume)
            List<Vector2> muehlbach = Bach();
            for (int i = 0; i < WC.FishCount; i++)
            {
                Vector2 xz = AmWeg(muehlbach, lr, R(-160f, 160f), R(-1.2f, 1.2f));
                if (Knoten(fund, WC.NodeFish(i), xz, rng, v =>
                    {
                        K.Prim(PrimitiveType.Sphere, "Grundler-Schatten", v, new Vector3(0f, 0.05f, 0f), new Vector3(0.45f, 0.06f, 0.18f), new Vector3(0f, R(0f, 180f), 0f), fisch, false);
                    }, wasser: true)) knoten++;
            }

            // W14: Zulauf der Tränke – an der Kopfweide, die dem Dreispannhof am nächsten steht (der Graben läuft dort entlang)
            Vector2 dsh = O("L6 Dreispannhof");
            Vector2 weide = NaechsteKopfweide(dsh) ?? dsh + new Vector2(60f, 0f);
            if (Knoten(fund, WC.NodeDrain, weide + new Vector2(3f, 2f), rng, v =>
                {
                    K.Prim(PrimitiveType.Sphere, "Nest im Zulauf", v, new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.45f, 0.9f), Vector3.zero, reisig, false);
                }, permanent: true)) knoten++;

            // W19: Zollbuch in der Ruine der Alten Zollhütte (Hauptbau, mitten in Habbos Lager)
            Vector2 zoll = O("L9 Alte Zollhütte", -11f, 16f);
            if (Knoten(fund, WC.NodeTollBook, zoll, rng, v =>
                {
                    K.Prim(PrimitiveType.Cube, "Zollbuch", v, new Vector3(0f, 0.08f, 0f), new Vector3(0.42f, 0.12f, 0.3f), new Vector3(0f, 18f, 0f), leder, false);
                    K.Prim(PrimitiveType.Cube, "Beschlag", v, new Vector3(0.18f, 0.15f, 0.12f), new Vector3(0.06f, 0.02f, 0.06f), new Vector3(0f, 18f, 0f), messing, false);
                }, suchRadius: 9f)) knoten++;

            int soll = WC.PostCount + WC.GrainCount + WC.FishCount + 2;
            string r = $"Aufträge: {figurenOk}/{WC.Npcs.Length} Figuren, {knoten}/{soll} Fundstellen";
            if (Warnungen.Count > 0)
            {
                r += $" ({Warnungen.Count} Hinweise, z. B. {Warnungen[0]})";
                foreach (string w in Warnungen) Debug.LogWarning("[Ashveil] Westweiden-Aufträge: " + w);
            }
            return r;
        }

        // ================================================================== Fundstellen

        /// <summary>Anker + Klickfläche + Bedingung; Darstellung verschwindet für den, der das Fundstück genommen hat (wie Glockenrast).</summary>
        private static bool Knoten(Transform parent, string action, Vector2 xz, System.Random rng, System.Action<Transform> darstellung,
            bool permanent = false, bool wasser = false, float suchRadius = 6f)
        {
            if (!GlockenrastCatalog.TryNode(action, out GlockenrastCatalog.Node n)) { Warnungen.Add("unbekannte Fundstelle " + action); return false; }
            Vector3 p;
            if (wasser) p = Boden(xz);
            else if (!FreierFleck(xz, suchRadius, 0.5f, out p)) { Warnungen.Add("kein freier Platz für " + action); p = Boden(xz); }
            Transform t = K.Group("Fundstelle " + action, parent, p, new Vector3(0f, (float)rng.NextDouble() * 360f, 0f));
            t.gameObject.AddComponent<StoryAnchor>().Configure(action, 2f);
            GameObject vis = null;
            Transform v = K.Group("Darstellung", t);
            darstellung(v);
            if (!permanent) vis = v.gameObject;
            StoryInteractable.Add(t.gameObject, action, n.LabelKey, n.Range, new Vector3(1.6f, 1.6f, 1.6f), new Vector3(0f, 0.7f, 0f));
            bool item = !string.IsNullOrEmpty(n.Item);
            t.gameObject.AddComponent<QuestNodeCondition>().Configure(n.Quest, n.Story ?? "", item ? GlockenrastCatalog.TakenFlag(action) : "", item ? vis : null);
            K.MakeDynamic(t.gameObject);
            return true;
        }

        /// <summary>Fundstelle an einer festen Weltposition (Kreidekeller: in der Höhle, ohne Geländesuche).</summary>
        internal static bool KnotenAn(Transform parent, string action, Vector3 p, System.Action<Transform> darstellung)
        {
            if (!GlockenrastCatalog.TryNode(action, out GlockenrastCatalog.Node n)) { Debug.LogWarning("[Ashveil] unbekannte Fundstelle " + action); return false; }
            Transform t = K.Group("Fundstelle " + action, parent, p, Vector3.zero);
            t.gameObject.AddComponent<StoryAnchor>().Configure(action, 2f);
            Transform v = K.Group("Darstellung", t);
            darstellung(v);
            StoryInteractable.Add(t.gameObject, action, n.LabelKey, n.Range, new Vector3(1.6f, 1.6f, 1.6f), new Vector3(0f, 0.7f, 0f));
            bool item = !string.IsNullOrEmpty(n.Item);
            t.gameObject.AddComponent<QuestNodeCondition>().Configure(n.Quest, n.Story ?? "", item ? GlockenrastCatalog.TakenFlag(action) : "", item ? v.gameObject : null);
            K.MakeDynamic(t.gameObject);
            return true;
        }

        // ================================================================== Plätze

        internal static Vector3 Boden(Vector2 xz) => new Vector3(xz.x, _t.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + _t.transform.position.y, xz.y);

        /// <summary>Nächster freier Fleck um xz (Spirale bis radius): kein Collider außer Gelände, nicht auf einem Weg, Neigung ≤ 25°.</summary>
        internal static bool FreierFleck(Vector2 xz, float radius, float halbBreite, out Vector3 p)
        {
            const float Schritt = 1.5f;
            for (float r = 0f; r <= radius; r += Schritt)
            {
                int n = r < 0.1f ? 1 : Mathf.Max(6, Mathf.RoundToInt(2f * Mathf.PI * r / Schritt));
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    Vector2 q = xz + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (WestweidenOrte.WegAbstand(q.x - _v.x, q.y - _v.z) < 2.5f) continue;
                    Vector3 nrm = _t.terrainData.GetInterpolatedNormal((q.x - _t.transform.position.x) / _t.terrainData.size.x, (q.y - _t.transform.position.z) / _t.terrainData.size.z);
                    if (Vector3.Angle(nrm, Vector3.up) > 25f) continue;
                    Vector3 b = Boden(q);
                    bool frei = true;
                    foreach (Collider c in Physics.OverlapCapsule(b + Vector3.up * (halbBreite + 0.05f), b + Vector3.up * 1.9f, halbBreite, ~0, QueryTriggerInteraction.Ignore))
                        if (!(c is TerrainCollider)) { frei = false; break; }
                    if (!frei) continue;
                    p = b;
                    return true;
                }
            }
            p = Boden(xz);
            return false;
        }

        /// <summary>Punkt an einer Linie: Bogenlänge s vom Fußpunkt von „bei“ aus (± = Richtung), seitlich versetzt.</summary>
        internal static Vector2 AmWeg(List<Vector2> linie, Vector2 bei, float s, float seite)
        {
            if (linie.Count < 2) return bei + new Vector2(s, seite);
            // Fußpunkt
            int best = 0; float bt = 0f, bd = float.MaxValue;
            for (int i = 0; i + 1 < linie.Count; i++)
            {
                Vector2 a = linie[i], b = linie[i + 1];
                float t = Mathf.Clamp01(Vector2.Dot(bei - a, b - a) / Mathf.Max(1e-3f, (b - a).sqrMagnitude));
                float d = (a + (b - a) * t - bei).sqrMagnitude;
                if (d < bd) { bd = d; best = i; bt = t; }
            }
            int seg = best; float tt = bt; float rest = s;
            while (true)
            {
                Vector2 a = linie[seg], b = linie[seg + 1];
                float len = (b - a).magnitude;
                float pos = tt * len + rest;
                if (pos >= 0f && pos <= len || (rest > 0 && seg + 2 >= linie.Count) || (rest < 0 && seg == 0))
                {
                    pos = Mathf.Clamp(pos, 0f, len);
                    Vector2 dir = (b - a) / Mathf.Max(1e-3f, len);
                    return a + dir * pos + new Vector2(-dir.y, dir.x) * seite;
                }
                if (pos > len) { rest = pos - len; seg++; tt = 0f; }
                else { rest = pos; seg--; tt = 1f; }
            }
        }

        internal static List<Vector2> Weg(string name)
        {
            var l = new List<Vector2>();
            foreach (string line in File.ReadAllLines(DataDir + "/WW_Wege.csv", Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3 || f[0] != name) continue;
                l.Add(new Vector2(float.Parse(f[1], CultureInfo.InvariantCulture) + _v.x, float.Parse(f[2], CultureInfo.InvariantCulture) + _v.z));
            }
            return l;
        }

        internal static List<Vector2> Bach()
        {
            var l = new List<Vector2>();
            foreach (string line in File.ReadAllLines(DataDir + "/WW_Bach.csv", Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3) continue;
                l.Add(new Vector2(float.Parse(f[1], CultureInfo.InvariantCulture) + _v.x, float.Parse(f[2], CultureInfo.InvariantCulture) + _v.z));
            }
            return l;
        }

        private static Vector2? NaechsteKopfweide(Vector2 bei)
        {
            Vector2? best = null; float bd = 400f * 400f;
            foreach (string line in File.ReadAllLines(DataDir + "/WW_Fuelle.csv", Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 4 || f[0] != "kopfweide") continue;
                var p = new Vector2(float.Parse(f[2], CultureInfo.InvariantCulture) + _v.x, float.Parse(f[3], CultureInfo.InvariantCulture) + _v.z);
                float d = (p - bei).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }
    }
}
