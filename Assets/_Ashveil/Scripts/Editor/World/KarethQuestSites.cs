using System.Collections.Generic;
using System.IO;
using System.Text;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Verteilung aller questrelevanten Orte in Kareth (27.09.2026, Wunsch Max) – EINE Tabelle fuer Aufbau, Tests,
    /// Karte und Doku (<c>Docs/Design/ashveil-quest-orte-kareth.md</c>).
    ///
    /// Aufbau wie eine Zone in WoW Classic: Stufenbaender wachsen vom Wachtfeuer nach aussen, jedes Band hat einen
    /// Stuetzpunkt (Hub) mit Questgebern und darum Quest-Orte in 60–250 m Abstand. Bestehende Orte behalten Lage und
    /// Kennung; neu sind drei Stuetzpunkte, sieben kleine Orte und reservierte Anker fuer spaetere Inhalte.
    /// Kareth wird dabei nicht umgebaut (Kanon): nur neue Gruppen, das Gehoeft der Verblichenen rueckt vom Osttor weg.
    /// </summary>
    public static class KarethQuestSites
    {
        public const string RootName = "Quest-Orte (Kareth)";

        public enum Dressing { None, HubGlockenrast, HubOstwacht, HubPlateauwacht, Harzerlager, Speiernest, Schlachtfeld, Steinkreis, Wachturm, OediHuette }

        public struct Site
        {
            public string Id, Name, Zone, Hub, Notes;
            public float X, Z, Radius;
            public int Min, Max;
            public QuestSiteKind Kind;
            public QuestSiteStatus Status;
            public bool Reachable, OnGiant, InSea;
            public Dressing Dress;
            public string Label; // Schild in der Welt (leer = keins)
        }

        // Zonen (Stufenbaender) – Reihenfolge = Progression
        public const string Z1 = "Wachtfeuer (1–4)";
        public const string Z2 = "Glockenmoor und Kiefernwald (5–9)";
        public const string Z3 = "Westhang und Salzkueste (8–12)";
        public const string Z4 = "Aschenfeld (11–16)";
        public const string Z5 = "Riese und Granitplateau (13–18)";

        public const string HubWachtfeuer = "site.kareth.hub.wachtfeuer";
        public const string HubGlockenrast = "site.kareth.hub.glockenrast";
        public const string HubGrubenlager = "site.kareth.hub.grubenlager";
        public const string HubOstwacht = "site.kareth.hub.ostwacht";
        public const string HubPlateauwacht = "site.kareth.hub.plateauwacht";

        /// <summary>Neues Gehoeft der Verblichenen (Q4/Q5): 88 m vom Feuer statt 30 m vor dem Osttor, unter dem Unterarm des Riesen.</summary>
        public static readonly Vector3 AshCamp = new Vector3(88f, 0f, -2f);

        public static readonly Vector3 GlockenrastCenter = new Vector3(-20f, 0f, 158f);
        // Aschenfeld-Nord: > 115 m von der Route des Glockenhirten (Leine 80 m), nahe Schlachtfeld, Uhrwerk, Kolosseum
        public static readonly Vector3 OstwachtCenter = new Vector3(250f, 0f, 170f);
        public static readonly Vector3 PlateauwachtCenter = new Vector3(70f, 0f, 650f);

        private static Site S(string id, string name, string zone, string hub, float x, float z, float r, int min, int max,
            QuestSiteKind kind, QuestSiteStatus status, string notes, Dressing dress = Dressing.None, string label = "",
            bool reachable = true, bool onGiant = false, bool inSea = false) =>
            new Site
            {
                Id = "site.kareth." + id, Name = name, Zone = zone, Hub = hub, X = x, Z = z, Radius = r, Min = min, Max = max, Kind = kind,
                Status = status, Notes = notes, Dress = dress, Label = label, Reachable = reachable, OnGiant = onGiant, InSea = inSea
            };

        public static readonly Site[] All =
        {
            // ---------------------------------------------------------------- Zone 1: Wachtfeuer (1–4)
            S("hub.wachtfeuer", "Startlager Wachtfeuer", Z1, "", 0f, -4f, 32f, 1, 4, QuestSiteKind.Hub, QuestSiteStatus.Active,
                "Bralt, Ilse, Maelis, Gerrit, Kartograf, Dorn. Q1–Q5 und Q6 (Hinweis Salzgruben). Register/Nessa-Haken."),
            S("aussichtsfels", "Aussichtsfels", Z1, HubWachtfeuer, 30f, 12f, 4f, 1, 2, QuestSiteKind.Explore, QuestSiteStatus.Active,
                "Q3 Blick ueber die Senke (area.lookout)."),
            S("wolfsrudel.kiefernhang", "Rudel am Kiefernhang", Z1, HubWachtfeuer, -42f, 62f, 22f, 1, 3, QuestSiteKind.Collect, QuestSiteStatus.Active,
                "Q2 Zaehe Felle. Defensive Woelfe direkt vor dem Nordtor."),
            S("wolfsrudel.waechterruecken", "Rudel am Waechterruecken", Z1, HubWachtfeuer, -82f, 96f, 20f, 1, 3, QuestSiteKind.Collect, QuestSiteStatus.Active,
                "Q2 (zweites Rudel)."),
            S("gehoeft", "Verlassenes Gehoeft (Lager der Verblichenen)", Z1, HubWachtfeuer, AshCamp.x, AshCamp.z, 18f, 2, 4, QuestSiteKind.Kill, QuestSiteStatus.Active,
                "Q4 Die Verblichenen, Q5 Pulver. Verlegt vom Osttor weg (88 m vom Feuer), 4 Nahkaempfer + 2 Schuetzinnen."),
            S("halle", "Halle des Hauptmanns", Z1, HubWachtfeuer, 0f, -68f, 14f, 1, 20, QuestSiteKind.Story, QuestSiteStatus.Active,
                "Bralt. Spaeter Lagerrat, Siedlergeschichte, Meisselspuren (Menschen schlagen in ihren Hueter)."),
            S("waechterlichtung", "Waechterlichtung", Z1, HubWachtfeuer, -72f, 22f, 40f, 1, 3, QuestSiteKind.Story, QuestSiteStatus.Planned,
                "Frei. Vorgesehen fuer die Neuankoemmling-Kette (Register, erste Erinnerungen, Veit/Nessa)."),
            S("wolfsrudel.tieferwald", "Rudel im Tiefen Wald", Z1, HubWachtfeuer, -200f, 150f, 20f, 3, 5, QuestSiteKind.Kill, QuestSiteStatus.Ready,
                "Bruecke zu Zone 2: territoriale Woelfe, heulen."),

            // ---------------------------------------------------------------- Zone 2: Glockenmoor und Kiefernwald (5–9)
            S("hub.glockenrast", "Glockenrast", Z2, "", GlockenrastCenter.x, GlockenrastCenter.z, 22f, 5, 9, QuestSiteKind.Hub, QuestSiteStatus.Ready,
                "NEU: Rastplatz der Pilger am Nordweg, Posten eines Bruders der Stillen Glocke und einer Moorwaechterin. " +
                "Schliesst die Auftragsluecke Stufe 4–9 (ca. 10–14 Auftraege).", Dressing.HubGlockenrast, "Glockenrast"),
            S("wegschrein", "Wegschrein und Pilgerrast", Z2, HubGlockenrast, 10f, 172f, 14f, 3, 9, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "Pilger Ada und Rune; Gebete zum Beschuetzer (menschliche Deutung)."),
            S("schaedeldecke", "Die Schaedeldecke", Z2, HubGlockenrast, 80f, 92f, 34f, 5, 7, QuestSiteKind.Explore, QuestSiteStatus.Ready,
                "Fundstuecke der ersten Siedler, Blick in die hohle Innenseite, alte Symbole."),
            S("tamo.wagen", "Liegengebliebener Wagen", Z2, HubGlockenrast, 153f, -55f, 12f, 5, 6, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "Wagner Tamo: Ersatzrad aus dem Holzfaellerlager (Laufauftrag ueber zwei Zonen)."),
            S("holzfaellerlager", "Holzfaellerlager", Z2, HubGlockenrast, -250f, 240f, 32f, 5, 7, QuestSiteKind.Kill, QuestSiteStatus.Ready,
                "Von Verblichenen besetzt (aggressiv). Kette: Lager zurueckgewinnen, Werkzeug, verschwundene Holzfaeller."),
            S("harzerlager", "Verlassenes Harzerlager", Z2, HubGlockenrast, -150f, 330f, 20f, 6, 8, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "NEU: umgestuerzter Unterstand, Harzeimer. Spur der verschwundenen Holzfaeller.", Dressing.Harzerlager, "Verlassenes Harzerlager"),
            S("moorrand.west", "Moorrand West", Z2, HubGlockenrast, -90f, 330f, 20f, 5, 7, QuestSiteKind.Collect, QuestSiteStatus.Ready,
                "Nadelspeier: Giftdruesen fuer Maelis/die Moorwaechterin."),
            S("moorrand.ost", "Moorrand Ost", Z2, HubGlockenrast, 165f, 270f, 20f, 5, 7, QuestSiteKind.Collect, QuestSiteStatus.Ready,
                "Nadelspeier (zweites Revier)."),
            S("kuppeln", "Versunkene Kuppeln", Z2, HubGlockenrast, 20f, 300f, 30f, 6, 8, QuestSiteKind.Collect, QuestSiteStatus.Ready,
                "Torfschrate; Glockenscherben bergen fuer den Bruder der Stillen Glocke."),
            S("glockenturm", "Versunkener Glockenturm", Z2, HubGlockenrast, 115f, 290f, 20f, 7, 9, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "Warum die Glocken schweigen – Dogma der Stillen Glocke (\"die Alten nicht wecken\")."),
            S("aschenweide", "Aschenweide", Z2, HubGlockenrast, 110f, 380f, 20f, 7, 9, QuestSiteKind.Kill, QuestSiteStatus.Ready, "Torfschrat-Revier."),
            S("nordmoor", "Nordmoor", Z2, HubGlockenrast, -30f, 420f, 30f, 8, 9, QuestSiteKind.Kill, QuestSiteStatus.Ready,
                "Torfschrate; Abschluss der Moorkette vor dem Plateau."),
            S("morghul", "Morghuls Tiefe (Weltboss)", Z2, HubGlockenrast, 55f, 345f, 40f, 20, 20, QuestSiteKind.WorldBoss, QuestSiteStatus.Planned,
                "Reserviert: Arena im Moorzentrum fuer den Weltboss Morghul (eigene Platzierungsstudie)."),
            S("oedi", "Oedis Huette", Z2, "", -430f, 470f, 12f, 5, 20, QuestSiteKind.Hidden, QuestSiteStatus.Ready,
                "NEU, versteckt, ohne Schild und Kartenmarke: Oedi (Klassen-/Talentauftraege, kennt Giganten besser als er zugibt).",
                Dressing.OediHuette),

            // ---------------------------------------------------------------- Zone 3: Westhang und Salzkueste (8–12)
            S("hub.grubenlager", "Lager der Grubenleute", Z3, "", -296f, -172f, 20f, 8, 14, QuestSiteKind.Hub, QuestSiteStatus.Active,
                "Vela Salk (Q6–Q10), Tobbe (Handel), Fuhrmann Oswald."),
            S("bogen", "Bogen am Salzweg", Z3, HubGrubenlager, -130f, -106f, 16f, 6, 8, QuestSiteKind.Explore, QuestSiteStatus.Ready,
                "Ellenbogenrest ueber dem Weg; Aussichts-/Kletterziel."),
            S("armreste", "Pfeiler und Schraeger Stein", Z3, HubGrubenlager, -180f, 6f, 36f, 7, 9, QuestSiteKind.Collect, QuestSiteStatus.Ready,
                "Armreste des Riesen: \"Splitter des Beschuetzers\" sammeln – was die Menschen von ihm abschlagen."),
            S("speiernest", "Speiernest", Z3, HubGrubenlager, -330f, 80f, 18f, 8, 10, QuestSiteKind.Kill, QuestSiteStatus.Ready,
                "NEU: Brutstaette der Nadelspeier in der Westlichtung.", Dressing.Speiernest, "Speiernest"),
            S("klippen", "Westklippen", Z3, HubGrubenlager, -400f, -60f, 20f, 8, 10, QuestSiteKind.Kill, QuestSiteStatus.Ready, "Nadelspeier 8–10."),
            S("kueste", "Salzkueste", Z3, HubGrubenlager, -320f, -150f, 20f, 8, 10, QuestSiteKind.Collect, QuestSiteStatus.Ready,
                "Nadelspeier, Treibgut, Grabsalz am Strand."),
            S("wrack", "Wrack in der Brandung", Z3, HubGrubenlager, -410f, -270f, 20f, 9, 11, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "Vorgeschichte des Ertrunkenen Kapitaens (vor dem Dungeon). Liegt in der Brandung (vom Ufer aus).", reachable: false, inSea: true),
            S("salzgruben", "Die Salzgruben", Z3, HubGrubenlager, -290f, -195f, 12f, 10, 14, QuestSiteKind.Dungeon, QuestSiteStatus.Active,
                "Instanz 1 (Q7–Q10)."),

            // ---------------------------------------------------------------- Zone 4: Aschenfeld (11–16)
            S("hub.ostwacht", "Ostwacht", Z4, "", OstwachtCenter.x, OstwachtCenter.z, 20f, 11, 16, QuestSiteKind.Hub, QuestSiteStatus.Ready,
                "NEU: Posten der Jaeger und Spaeher am Rand des Aschenfelds (Graue Mutter, Glockenhirte beobachten, Krater). " +
                "Spaeter Zugang zu Kolosseum und Grenztunnel.", Dressing.HubOstwacht, "Ostwacht"),
            S("graue.mutter", "Revier der Grauen Mutter", Z4, HubOstwacht, 250f, -150f, 30f, 11, 13, QuestSiteKind.Elite, QuestSiteStatus.Ready,
                "Seltene Elite mit drei Verstecken (470/160, 480/−190)."),
            S("krater", "Einschlagkrater", Z4, HubOstwacht, 420f, -150f, 30f, 11, 13, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "Meteorit als Deutung (Volksmund, Kartograf) – nicht als letzte Wahrheit."),
            S("hirte.hueteplatz", "Hueteplatz des Glockenhirten", Z4, HubOstwacht, 340f, -122f, 14f, 12, 14, QuestSiteKind.Explore, QuestSiteStatus.Ready,
                "Beobachten, nicht kaempfen: Weltboss Stufe 34 zieht vorbei."),
            S("schlachtfeld", "Altes Schlachtfeld", Z4, HubOstwacht, 330f, 470f, 45f, 12, 14, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "NEU: Knochen, zerbrochene Banner mit fremden Zeichen, halb versunkener Stein – Foreshadowing Gebrochener Bund, " +
                "Richtung Grenze. Nie erklaert.", Dressing.Schlachtfeld, "Altes Schlachtfeld"),
            S("uhrwerk", "Das Uhrwerk (versiegelt)", Z4, HubOstwacht, 505f, 210f, 16f, 14, 16, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "Versiegeltes Tor. Kein Dungeon (Kanon)."),
            S("kolosseum", "Kolosseum-Vorplatz", Z4, HubOstwacht, 492f, 285f, 30f, 14, 16, QuestSiteKind.Dungeon, QuestSiteStatus.Ready,
                "Instanz 2 (K1), Greybox Pass 2: Fassade im Fels, Ringmeister Brannoc, Marketenderin Oda, Waffenwart Hesk, Eingang; " +
                "alter Weg vom Ostweg. Wellen ab Pass 3."),
            S("grenztunnel", "Grenztunnel", Z4, HubOstwacht, 500f, 60f, 16f, 16, 20, QuestSiteKind.Transition, QuestSiteStatus.Planned,
                "Uebergang ins Grenzgebirge (Tunnel, Entscheidung Max)."),

            // ---------------------------------------------------------------- Zone 5: Riese und Granitplateau (13–18)
            S("kratersee", "Kratersee auf dem Ruecken", Z5, HubPlateauwacht, -40f, -125f, 30f, 13, 16, QuestSiteKind.Story, QuestSiteStatus.Planned,
                "Aufstieg ueber den Ruecken geplant (Rastplatz, Angelplatz, Reliefs).", reachable: false, onGiant: true),
            S("hub.plateauwacht", "Plateauwacht", Z5, "", PlateauwachtCenter.x, PlateauwachtCenter.z, 20f, 15, 18, QuestSiteKind.Hub, QuestSiteStatus.Ready,
                "NEU: Wachposten oben an der Rampe, Nachschub fuer die Reise nach Norden (Hauptlager, M10).", Dressing.HubPlateauwacht, "Plateauwacht"),
            S("kloster", "Kloster der Stillen Glocke", Z5, HubPlateauwacht, 0f, 700f, 30f, 15, 18, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "Tore verschlossen; Aussenhof, Moenche, Dogma. Kein Dungeon (Kanon)."),
            S("steinkreis", "Steinkreis der Stillen", Z5, HubPlateauwacht, -300f, 680f, 22f, 15, 17, QuestSiteKind.Story, QuestSiteStatus.Ready,
                "NEU: alte Steine mit Giganten-Reliefs, widerspruechliche Mythen.", Dressing.Steinkreis, "Steinkreis der Stillen"),
            S("wachturm.ost", "Gebrochener Wachturm", Z5, HubPlateauwacht, 320f, 680f, 22f, 16, 18, QuestSiteKind.Kill, QuestSiteStatus.Ready,
                "NEU: Ruine am Ostrand des Plateaus; spaeter Plateau-Eliten.", Dressing.Wachturm, "Gebrochener Wachturm"),
            S("nordpass", "Nordpass", Z5, HubPlateauwacht, 0f, 770f, 20f, 18, 20, QuestSiteKind.Transition, QuestSiteStatus.Planned,
                "Weiterreise nach Norden (Karrenstrasse zum Hauptlager, M10).")
        };

        /// <summary>Waldlichtungen fuer neue Orte (vom Kiefernhang-Builder beachtet).</summary>
        public static IEnumerable<(Vector2 c, float r)> ForestClearings()
        {
            foreach (Site s in All)
                if (s.Dress == Dressing.OediHuette || s.Dress == Dressing.Harzerlager || s.Dress == Dressing.Speiernest)
                    yield return (new Vector2(s.X, s.Z), s.Radius + 4f);
        }

        // ================================================================== Aufbau

        private static Material _wood, _woodDark, _stone, _stoneDark, _cloth, _clothGrey, _fire, _bone, _soul;

        public static void Build(Transform env)
        {
            EnsureMaterials();

            Physics.SyncTransforms();
            Transform root = K.Group(RootName, env);
            var sb = new StringBuilder();
            int warnings = 0;
            foreach (Site s in All)
            {
                Vector3 p = Ground(s);
                Transform t = K.Group(s.Name, root, p, Vector3.zero);
                t.gameObject.AddComponent<WorldQuestSite>().Configure(s.Id, s.Name, s.Zone, s.Hub, s.Kind, s.Status, s.Min, s.Max, s.Radius, s.Reachable, s.Notes);
                if (s.Status != QuestSiteStatus.Planned) Dress(t, s);
                if (!string.IsNullOrEmpty(s.Label))
                    K.Label(t, new Vector3(0f, s.Kind == QuestSiteKind.Hub ? 5.5f : 4f, 0f), s.Label,
                        $"Stufe {s.Min}–{s.Max}", WorldLabelStyle.Place, s.Kind == QuestSiteKind.Hub ? 80f : 50f);
                string w = Validate(s, p);
                if (w.Length > 0)
                {
                    warnings++;
                    sb.AppendLine("  ! " + s.Id + ": " + w);
                }
            }
            if (warnings > 0) Debug.LogWarning("[Ashveil] Quest-Orte mit Hinweisen:\n" + sb);
            Debug.Log($"[Ashveil] Quest-Orte: {All.Length} Orte in 5 Zonen, {warnings} Hinweise.");
        }

        /// <summary>Greybox-Materialien der Orte (auch fuer Kareth v4, #27).</summary>
        internal static void EnsureMaterials()
        {
            _wood = K.Mat("Wood", new Color(0.36f, 0.25f, 0.16f));
            _woodDark = K.Mat("Wood_Dark", new Color(0.2f, 0.14f, 0.1f));
            _stone = K.Mat("Stone_Light", new Color(0.53f, 0.52f, 0.49f));
            _stoneDark = K.Mat("Stone_Dark", new Color(0.26f, 0.26f, 0.29f));
            _cloth = K.Mat("Cloth_Red", new Color(0.5f, 0.2f, 0.16f));
            _clothGrey = K.Mat("Cloth_Grey", new Color(0.42f, 0.4f, 0.38f));
            _fire = K.Mat("Glow_Fire", new Color(1f, 0.5f, 0.15f), 0f, new Color(4f, 1.6f, 0.4f));
            _bone = K.Mat("Bone", new Color(0.78f, 0.74f, 0.66f));
            // Seelenfarbe (Violett-Gold) nur als Hauch – Kanon: Seele, nicht Fraktion
            _soul = K.Mat("Soul_Gold_Faint", new Color(0.55f, 0.5f, 0.42f), 0.2f, new Color(0.35f, 0.25f, 0.45f));
        }

        /// <summary>Bodenpunkt (Terrain oder Riese); Kratersee auf dem Ruecken ueber den POI.</summary>
        private static Vector3 Ground(Site s)
        {
            if (s.OnGiant)
            {
                foreach (WorldPoi poi in Object.FindObjectsByType<WorldPoi>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (poi.Id == "poi.kareth.kratersee") return poi.transform.position;
            }
            Vector3 top = new Vector3(s.X, 400f, s.Z);
            if (!s.OnGiant && Physics.Raycast(top, Vector3.down, out RaycastHit hit, 600f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponent<Terrain>() != null)
                return hit.point;
            return KarethTerrainBuilder.OnGround(s.X, s.Z);
        }

        private static string Validate(Site s, Vector3 p)
        {
            var w = new List<string>();
            if (s.X < KarethTerrainBuilder.MinX || s.X > KarethTerrainBuilder.MaxX || s.Z < KarethTerrainBuilder.MinZ || s.Z > KarethTerrainBuilder.MaxZ)
                w.Add("ausserhalb von Kareth");
            if (!s.InSea && !s.OnGiant && p.y < KarethTerrainBuilder.SeaLevel + 1f) w.Add($"im Meer ({p.y:0.0} m)");
            if (!s.OnGiant && s.Kind != QuestSiteKind.Story && GiantBuilder.Blocks(s.X, s.Z, 0f, false) && s.Id != HubWachtfeuer)
                w.Add("auf der Grundflaeche des Riesen");
            if (s.Status != QuestSiteStatus.Planned)
            {
                float slope = Mathf.Abs(KarethTerrainBuilder.Height(s.X + 4f, s.Z) - KarethTerrainBuilder.Height(s.X - 4f, s.Z)) +
                              Mathf.Abs(KarethTerrainBuilder.Height(s.X, s.Z + 4f) - KarethTerrainBuilder.Height(s.X, s.Z - 4f));
                if (s.Dress != Dressing.None && slope > 6f) w.Add($"steiler Bauplatz ({slope:0.0} m Hoehenunterschied auf 8 m)");
            }
            return string.Join(", ", w);
        }

        // ------------------------------------------------------------------ Greybox der neuen Orte

        /// <summary>Greybox eines Ortstyps an t (Kareth v4, #27: nur auf ebenem Grund verwenden).</summary>
        internal static void DressAt(Transform t, Dressing d) => Dress(t, new Site { Dress = d });

        internal static Material ClothGrey => _clothGrey;

        private static void Dress(Transform t, Site s)
        {
            switch (s.Dress)
            {
                case Dressing.HubGlockenrast: Glockenrast(t); break;
                case Dressing.HubOstwacht: Ostwacht(t); break;
                case Dressing.HubPlateauwacht: Plateauwacht(t); break;
                case Dressing.Harzerlager: Harzerlager(t); break;
                case Dressing.Speiernest: Speiernest(t); break;
                case Dressing.Schlachtfeld: Schlachtfeld(t); break;
                case Dressing.Steinkreis: Steinkreis(t); break;
                case Dressing.Wachturm: Wachturm(t); break;
                case Dressing.OediHuette: OediHuette(t); break;
            }
        }

        internal static void Campfire(Transform t, Vector3 p, float light = 2.2f)
        {
            Transform f = K.Group("Feuerstelle", t, p, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                K.Block("Stein", f, new Vector3(Mathf.Sin(a) * 0.9f, -0.1f, Mathf.Cos(a) * 0.9f), new Vector3(0.4f, 0.3f, 0.35f), _stoneDark, i * 40f);
            }
            K.Prim(PrimitiveType.Sphere, "Glut", f, new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.2f, 0.9f), Vector3.zero, _fire, false);
            K.PointLight("Feuer Licht", f, new Vector3(0f, 1f, 0f), new Color(1f, 0.55f, 0.2f), 11f, light, true);
            K.Particles("Rauch", f, new Vector3(0f, 0.4f, 0f), new Color(0.35f, 0.33f, 0.31f, 0.45f), 0.8f, 4f, 0.9f, 5f, 10f, 0.3f);
        }

        internal static void Tent(Transform t, Vector3 p, float yaw, Material cloth)
        {
            Transform g = K.Group("Zelt", t, p, new Vector3(0f, yaw, 0f));
            K.Box("Plane L", g, new Vector3(-0.8f, 1f, 0f), new Vector3(0.06f, 2.3f, 3.2f), cloth, new Vector3(0f, 0f, -35f));
            K.Box("Plane R", g, new Vector3(0.8f, 1f, 0f), new Vector3(0.06f, 2.3f, 3.2f), cloth, new Vector3(0f, 0f, 35f));
            K.Block("Stange", g, new Vector3(0f, 0f, 1.6f), new Vector3(0.08f, 2f, 0.08f), _wood);
        }

        internal static void Crates(Transform t, Vector3 p, int n)
        {
            for (int i = 0; i < n; i++)
                K.Block("Kiste", t, p + new Vector3(i * 0.9f, 0f, (i % 2) * 0.6f), new Vector3(0.8f, 0.7f, 0.8f), _wood, i * 23f);
        }

        private static void Glockenrast(Transform t)
        {
            Campfire(t, Vector3.zero);
            Tent(t, new Vector3(-6f, 0f, 3f), 70f, _clothGrey);
            Tent(t, new Vector3(-4f, 0f, -5f), 120f, _clothGrey);
            Bench(t, new Vector3(2.5f, 0f, 1.5f), 30f);
            BellGallows(t, new Vector3(4f, 0f, -3f), 15f);
            Crates(t, new Vector3(-8f, 0f, -2f), 3);
        }

        internal static void Bench(Transform t, Vector3 p, float yaw) => K.Block("Bank", t, p, new Vector3(2.2f, 0.45f, 0.45f), _woodDark, yaw);

        /// <summary>Glockengalgen mit schweigender Glocke (Zeichen der Stillen Glocke), Kloeppel hochgebunden.</summary>
        internal static void BellGallows(Transform t, Vector3 p, float yaw)
        {
            Transform bell = K.Group("Glockengalgen", t, p, new Vector3(0f, yaw, 0f));
            K.Block("Pfosten L", bell, new Vector3(-1.1f, 0f, 0f), new Vector3(0.2f, 3.2f, 0.2f), _woodDark);
            K.Block("Pfosten R", bell, new Vector3(1.1f, 0f, 0f), new Vector3(0.2f, 3.2f, 0.2f), _woodDark);
            K.Box("Balken", bell, new Vector3(0f, 3.1f, 0f), new Vector3(2.6f, 0.2f, 0.2f), _woodDark);
            K.Prim(PrimitiveType.Cylinder, "Glocke", bell, new Vector3(0f, 2.45f, 0f), new Vector3(0.8f, 0.45f, 0.8f), Vector3.zero, _stoneDark);
        }

        private static void Ostwacht(Transform t)
        {
            // Hochstand mit Blick ueber das Aschenfeld
            Transform tower = K.Group("Hochstand", t, new Vector3(6f, 0f, 2f), new Vector3(0f, 20f, 0f));
            foreach (Vector2 c in new[] { new Vector2(-1.4f, -1.4f), new Vector2(1.4f, -1.4f), new Vector2(-1.4f, 1.4f), new Vector2(1.4f, 1.4f) })
                K.Block("Pfosten", tower, new Vector3(c.x, 0f, c.y), new Vector3(0.25f, 5.2f, 0.25f), _woodDark);
            K.Box("Plattform", tower, new Vector3(0f, 4.4f, 0f), new Vector3(3.6f, 0.2f, 3.6f), _wood);
            K.Box("Bruestung", tower, new Vector3(0f, 5f, 1.75f), new Vector3(3.6f, 0.9f, 0.1f), _wood);
            K.Box("Leiter", tower, new Vector3(0f, 2.2f, -2f), new Vector3(0.6f, 4.4f, 0.08f), _wood, new Vector3(-12f, 0f, 0f));
            Campfire(t, new Vector3(-3f, 0f, -1f));
            Tent(t, new Vector3(-7f, 0f, 3f), 100f, _cloth);
            // Fellgestell der Jaeger
            K.Block("Gestell L", t, new Vector3(-2f, 0f, 5f), new Vector3(0.12f, 2f, 0.12f), _wood);
            K.Block("Gestell R", t, new Vector3(0f, 0f, 5f), new Vector3(0.12f, 2f, 0.12f), _wood);
            K.Box("Fell", t, new Vector3(-1f, 1.3f, 5f), new Vector3(1.8f, 1.2f, 0.05f), _clothGrey);
            Crates(t, new Vector3(2f, 0f, -5f), 2);
        }

        private static void Plateauwacht(Transform t)
        {
            // Halb verfallene Steinhuette als Unterstand
            K.Block("Mauer N", t, new Vector3(0f, 0f, 3f), new Vector3(6f, 2.6f, 0.6f), _stone);
            K.Block("Mauer W", t, new Vector3(-3f, 0f, 0.5f), new Vector3(0.6f, 2.2f, 5f), _stone);
            K.Block("Mauer O Rest", t, new Vector3(3f, 0f, 1.5f), new Vector3(0.6f, 1.2f, 3f), _stone);
            K.Box("Dachplane", t, new Vector3(0f, 2.7f, 1.2f), new Vector3(6.4f, 0.06f, 4f), _cloth, new Vector3(-10f, 0f, 0f));
            Campfire(t, new Vector3(1f, 0f, -3f));
            Tent(t, new Vector3(-6f, 0f, -3f), 20f, _clothGrey);
            Crates(t, new Vector3(4f, 0f, -2f), 3);
            K.Block("Fahnenmast", t, new Vector3(-2f, 0f, -6f), new Vector3(0.14f, 6f, 0.14f), _woodDark);
            K.Box("Fahne", t, new Vector3(-1.3f, 5.3f, -6f), new Vector3(1.4f, 0.9f, 0.04f), _cloth);
        }

        private static void Harzerlager(Transform t)
        {
            K.Box("Unterstand (eingestuerzt)", t, new Vector3(0f, 0.9f, 0f), new Vector3(3.5f, 0.12f, 2.6f), _woodDark, new Vector3(22f, 30f, 8f));
            K.Block("Pfosten", t, new Vector3(1.4f, 0f, 1f), new Vector3(0.15f, 1.9f, 0.15f), _woodDark);
            for (int i = 0; i < 4; i++)
                K.Prim(PrimitiveType.Cylinder, "Harzeimer", t, new Vector3(-2f + i * 0.9f, 0.3f, -2f + (i % 2) * 0.5f),
                    new Vector3(0.5f, 0.3f, 0.5f), new Vector3(i == 2 ? 80f : 0f, 0f, 0f), _wood);
            K.Box("Axt im Stumpf", t, new Vector3(2.5f, 0.9f, -1.5f), new Vector3(0.08f, 0.7f, 0.25f), _stoneDark, new Vector3(0f, 0f, 20f));
            K.Prim(PrimitiveType.Cylinder, "Stumpf", t, new Vector3(2.5f, 0.35f, -1.5f), new Vector3(0.8f, 0.35f, 0.8f), Vector3.zero, _wood);
        }

        private static void Speiernest(Transform t)
        {
            var rng = new System.Random(77);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                K.Block("Fels", t, new Vector3(Mathf.Sin(a) * 7f, -0.4f, Mathf.Cos(a) * 7f), new Vector3(1.6f, 1.4f + (float)rng.NextDouble(), 1.4f), _stoneDark, i * 41f);
            }
            for (int i = 0; i < 6; i++)
                K.Prim(PrimitiveType.Sphere, "Ei", t, new Vector3((float)rng.NextDouble() * 4f - 2f, 0.3f, (float)rng.NextDouble() * 4f - 2f),
                    new Vector3(0.6f, 0.8f, 0.6f), Vector3.zero, _bone);
        }

        private static void Schlachtfeld(Transform t)
        {
            var rng = new System.Random(1911);
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector3((float)rng.NextDouble() * 60f - 30f, 0f, (float)rng.NextDouble() * 60f - 30f);
                Transform heap = K.Group("Knochen", t, p, new Vector3(0f, (float)rng.NextDouble() * 360f, 0f));
                for (int k = 0; k < 4; k++)
                    K.Prim(PrimitiveType.Cylinder, "Knochen", heap, new Vector3(k * 0.3f, 0.1f, (k % 2) * 0.4f), new Vector3(0.12f, 0.5f, 0.12f),
                        new Vector3(90f, k * 50f, 0f), _bone, false);
            }
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3((float)rng.NextDouble() * 50f - 25f, 0f, (float)rng.NextDouble() * 50f - 25f);
                Transform banner = K.Group("Zerbrochenes Banner", t, p, new Vector3((float)rng.NextDouble() * 30f - 15f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 30f - 15f));
                K.Block("Stange", banner, Vector3.zero, new Vector3(0.1f, 3.5f, 0.1f), _woodDark);
                K.Box("Tuch (fremde Zeichen)", banner, new Vector3(0.45f, 2.7f, 0f), new Vector3(0.8f, 1.2f, 0.03f), i % 2 == 0 ? _clothGrey : _cloth);
            }
            // Halb versunkener Stein mit einem Ring-Zeichen – kaum sichtbarer Seelen-Schimmer, nie erklaert
            Transform stone = K.Group("Versunkener Stein", t, new Vector3(0f, -1f, 0f), new Vector3(8f, 30f, -6f));
            K.Box("Stein", stone, new Vector3(0f, 1.6f, 0f), new Vector3(2.4f, 3.6f, 1f), _stoneDark);
            K.Prim(PrimitiveType.Cylinder, "Zeichen", stone, new Vector3(0f, 2f, -0.52f), new Vector3(1.2f, 0.02f, 1.2f), new Vector3(90f, 0f, 0f), _soul, false);
        }

        private static void Steinkreis(Transform t)
        {
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                float h = i == 4 ? 1.2f : 3.2f + (i % 3) * 0.6f; // einer umgestuerzt
                K.Block("Stein", t, new Vector3(Mathf.Sin(a) * 12f, -0.2f, Mathf.Cos(a) * 12f), new Vector3(1.3f, h, 0.8f), _stone, a * Mathf.Rad2Deg);
            }
            K.Block("Relief-Platte", t, new Vector3(0f, -0.1f, 0f), new Vector3(4f, 0.5f, 2.5f), _stoneDark, 12f);
        }

        private static void Wachturm(Transform t)
        {
            // Runder Turm aus acht Mauerstuecken, zwei fehlen (Bresche nach Osten)
            for (int i = 0; i < 8; i++)
            {
                if (i == 2 || i == 3) continue;
                float a = i * Mathf.PI / 4f;
                float h = 6f + (i % 3) * 1.5f;
                K.Block("Mauer", t, new Vector3(Mathf.Sin(a) * 4f, 0f, Mathf.Cos(a) * 4f), new Vector3(3.2f, h, 0.8f), _stone, a * Mathf.Rad2Deg);
            }
            var rng = new System.Random(5);
            for (int i = 0; i < 10; i++)
                K.Block("Schutt", t, new Vector3(6f + (float)rng.NextDouble() * 6f, -0.2f, (float)rng.NextDouble() * 8f - 4f),
                    new Vector3(1f, 0.6f, 0.9f), _stone, i * 33f);
        }

        private static void OediHuette(Transform t)
        {
            K.Block("Huette", t, Vector3.zero, new Vector3(4f, 2.4f, 3.2f), _woodDark, 25f);
            K.Box("Dach", t, new Vector3(0f, 2.7f, 0f), new Vector3(4.6f, 0.15f, 3.8f), _wood, new Vector3(0f, 25f, 12f));
            K.Block("Kamin", t, new Vector3(1.4f, 0f, 0.9f), new Vector3(0.6f, 3.6f, 0.6f), _stoneDark, 25f);
            K.Particles("Kaminrauch", t, new Vector3(1.4f, 3.8f, 0.9f), new Color(0.4f, 0.4f, 0.4f, 0.35f), 0.6f, 2f, 0.6f, 6f, 8f, 0.2f);
            K.PointLight("Fensterlicht", t, new Vector3(-0.8f, 1.3f, -1.8f), new Color(1f, 0.6f, 0.3f), 5f, 1.2f, false);
        }

        // ================================================================== Karte und Liste

        [MenuItem("Ashveil/Welt/Quest-Orte: Liste und Karte schreiben", priority = 520)]
        public static void ExportMenu()
        {
            string dir = Export();
            if (dir != null) Debug.Log("[Ashveil] Quest-Orte geschrieben: " + dir);
        }

        /// <summary>JSON aller Orte (mit gebauten Hoehen, falls in der Szene) und eine Draufsicht von Kareth (2048 px).</summary>
        public static string Export()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            string dir = Ashveil.Core.ProjectPaths.Logs("QuestOrte");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder("{\n  \"bounds\": [-600, -350, 600, 850],\n  \"sites\": [\n");
            for (int i = 0; i < All.Length; i++)
            {
                Site s = All[i];
                WorldQuestSite built = null;
                foreach (WorldQuestSite q in Object.FindObjectsByType<WorldQuestSite>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (q.Id == s.Id) built = q;
                float y = built != null ? built.transform.position.y : KarethTerrainBuilder.Height(s.X, s.Z);
                string F(float v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                sb.Append($"    {{\"id\":\"{s.Id}\",\"name\":\"{s.Name}\",\"zone\":\"{s.Zone}\",\"hub\":\"{s.Hub}\",\"x\":{F(s.X)},\"z\":{F(s.Z)},\"y\":{F(y)}," +
                          $"\"r\":{F(s.Radius)},\"min\":{s.Min},\"max\":{s.Max},\"kind\":\"{s.Kind}\",\"status\":\"{s.Status}\",\"reachable\":{(s.Reachable ? "true" : "false")}," +
                          $"\"built\":{(built != null ? "true" : "false")}}}");
                sb.Append(i < All.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(dir, "quest_orte.json"), sb.ToString(), new UTF8Encoding(false));
            RenderTopDown(Path.Combine(dir, "kareth_draufsicht.png"));
            return dir;
        }

        private static void RenderTopDown(string path)
        {
            const int size = 2048;
            GameObject camGo = null;
            RenderTexture rt = null;
            try
            {
                camGo = new GameObject("Karten-Kamera") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 600f; // z -350..850
                cam.transform.position = new Vector3(0f, 900f, 250f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 1400f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.12f, 0.14f);
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = true;
                int preview = LayerMask.NameToLayer("Preview");
                if (preview >= 0) cam.cullingMask &= ~(1 << preview);
                rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else cam.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            }
        }
    }
}
