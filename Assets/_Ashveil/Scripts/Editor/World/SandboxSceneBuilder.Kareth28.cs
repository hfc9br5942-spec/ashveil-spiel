using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using Ashveil.World;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;
using V4 = Ashveil.EditorTools.KarethV4Inhalte;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// #28 Kareth v4, Welle 0 (Bauplan #28 v2, Max 07.10. abends): Greybox-Requisiten der elf Ergänzungsaufträge und die
    /// Figur, die es nur in Kareth v4 gibt (Ebba Salweg, Wirtin der „Schale“). Lagen aus der Beidatei (inhalte_w0.py, Requisiten
    /// „k28.*“); gebaut aus dem Requisiten-Teil von <see cref="BuildKarethV4Inhalte"/>. Erzählt wird mit Gegenständen in der Welt
    /// (Environmental Storytelling), nicht mit Schildern – außer dem Wirtshausschild und zwei kleinen Hinweisen am Rastplatz.
    ///   · Vorratsgruben (K-A), verlassener Holzplatz (K-D), Flüsterbalg-Nest mit Planenfetzen (U-1), Wrack im Salz mit Mast (U-3),
    ///     fünf Zeichensteine (U-5, Gestalt wie die Zeichensteine am ältesten Herd), zwei Wüstungshöfe und die Spur nach Süden (U-6),
    ///     alter Rastplatz mit leeren Schalen und Stiefelabdrücken, Versteck im Schilf (U-7), Tresen der „Schale“ und Hocker (U-2).
    /// Kollision nur an festen Teilen (Wände, Stämme, Rumpf, Tresen, Fässer) – Flaches (Gruben, Abdrücke, Schilf) ohne, damit das
    /// NavMesh nicht zerfällt. Marken der Orte stehen neben den festen Teilen (Weltprüfung: zu Fuß erreichbar).
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        // ================================================================== Figuren nur für Kareth v4

        /// <summary>Körper, Titel und Kennung der Figuren, die nur Kareth v4 hat (Tagesablauf aus der Beidatei).</summary>
        private static List<NpcSpec> KarethNpcs() => new List<NpcSpec>
        {
            new NpcSpec
            {
                // Character Sheet freigegeben (Max 07.10.): um die 50, kräftig, Schürze über rostrotem Kleid, Ärmel hochgekrempelt, Tuch im
                // Haar – weiblicher Basiskörper, keine neue Rüstung: Gambeson und Hose rostrot gefärbt, Kapuze knochenweiß als Tuch
                Name = "Ebba Salweg", Title = "Zur Schale", NpcId = KarethCatalog.Ebba, Female = true, Quest = true,
                Look = "1|b1|s3|h2|c5|a2|e1|m1|y0:1.1.1,2:4.1.11,4:4.4.11",
                Gear = new[] { "travel_hood", "frayed_gambeson", "travel_trousers", "weaver_sandals" },
                Steps = new[] { At(0f, Vector3.zero, 0f, "Stand") }
            },
            // Untersteg beleben (Max 10.10. abends: Character Sheets v1 freigegeben – `claude/ashveil-untersteg-figuren-sheets-v1-2026-10-10.md`).
            // Tagesabläufe aus der Beidatei (inhalte_w0.py); Basiskörper, keine neue Rüstung. Liv (Kind) erst mit der Figurenprobe.
            new NpcSpec
            {
                // Müllerin, um die 40, hochgewachsen, Mehlstaub, Lederschürze (Gambeson), Haar straff zurück
                Name = "Grete Bachmann", Title = "Müllerin", NpcId = KarethCatalog.Grete, Female = true, Quest = true,   // seit 10.10. spät Auftragsfigur (N01, N11)
                Look = "1|b1|s2|h3|c4|a3|e2|m1", Gear = new[] { "frayed_gambeson", "travel_trousers", "weaver_sandals", "rope_belt" },
                Steps = new[] { At(0f, Vector3.zero, 0f, "Stand") }
            },
            new NpcSpec
            {
                // Brückenwächter, Ende 20, schmal, zu großer Wachtmantel (geerbt), Helm mit Delle
                Name = "Hennek Doll", Title = "Brückenwache", NpcId = KarethCatalog.Hennek, Quest = true,   // seit 10.10. spät Auftragsfigur (N03, N08)
                Look = "1|b0|s1|h1|c2|d0|a1|e1|m0", Gear = new[] { "dented_helm", "frayed_gambeson", "travel_trousers", "foot_wraps", "grey_wool_cloak" },
                Steps = new[] { At(0f, Vector3.zero, 0f, "Stand") }
            },
            new NpcSpec
            {
                // Krämerin, um die 60, klein, rund, Schlüsselbund; Händlerin (NpcDefinition in QuestContentBuilder.Kareth)
                Name = "Idun Vesper", Title = "Krämerin", NpcId = KarethCatalog.Idun, Female = true,
                Look = "1|b1|s0|h6|c6|a5|e3|m2", Gear = new[] { "weaver_robe", "travel_mantle", "weaver_sandals" },
                Steps = new[] { At(0f, Vector3.zero, 0f, "Stand") }
            },
            new NpcSpec
            {
                // die Alte am Herd der Schale, über 70, dicke Wollschichten, Stock (kein Gegenstand), ein Auge trüb
                Name = "Mutter Ansa", Title = "", Female = true, NpcId = Ashveil.Data.RegisterCatalog.Ansa, Quest = true,   // Akt I „Das Register“ (R3)
                Look = "1|b1|s3|h5|c6|a5|e0|m3", Gear = new[] { "weaver_robe", "travel_hood", "foot_wraps", "grey_wool_cloak" },
                Steps = new[] { At(0f, Vector3.zero, 0f, "Stand") }
            }
        };

        // ================================================================== Requisiten (k28.*)

        private static Material _k28Turnip, _k28Crock, _k28Cloak, _k28Reed, _k28Silt;

        private static void Kareth28Materials()
        {
            _k28Turnip = K.Mat("K28_Ruebe", new Color(0.78f, 0.62f, 0.56f));
            _k28Crock = K.Mat("K28_Steingut", new Color(0.5f, 0.33f, 0.22f), 0.35f);
            _k28Cloak = K.Mat("K28_Wachmantel", new Color(0.33f, 0.3f, 0.26f));   // nicht farbgleich mit „KV4 Schwelle Stein dunkel“ (Weltprüfung)
            _k28Reed = K.Mat("K28_Schilf", new Color(0.52f, 0.5f, 0.3f));
            _k28Silt = K.Mat("K28_Schlick", new Color(0.24f, 0.21f, 0.17f), 0.4f);
        }

        /// <summary>Ein Requisit „k28.*“ bauen. false = unbekannt (Warnung beim Aufrufer).</summary>
        private static bool BuildKareth28Prop(Transform parent, V4.Requisit r, Vector3 pos)
        {
            if (_k28Turnip == null) Kareth28Materials();
            Transform t = K.Group(r.name, parent, pos, new Vector3(0f, r.yaw, 0f));
            switch (r.art)
            {
                case "k28.gruben": K28StoragePits(t); break;
                case "k28.holzplatz": K28WoodYard(t); break;
                case "k28.nest": K28Nest(t); break;
                case "k28.wrack": K28Wreck(t); break;
                case "k28.zeichenstein": K28SignStone(t); break;
                case "k28.wuestungshof": K28Farmstead(t, r.name.Contains("West")); break;
                case "k28.spur": K28Trail(t); break;
                case "k28.rastplatz": K28RestingPlace(t); break;
                case "k28.versteck": K28Hideout(t); break;
                // Tresen und Hocker stehen am Gasthaus bzw. an der Kochstelle auf ebenem Boden – ohne Anpassung an den Hang
                case "k28.tresen": K28Counter(t); return true;
                case "k28.hocker": K28Stool(t, r.name.Contains("Ebba") ? 0.5f : 0.45f); return true;
                default:
                    Object.DestroyImmediate(t.gameObject);
                    return false;
            }
            K28Drape(t);
            return true;
        }

        /// <summary>
        /// Teile eines Requisits auf den Hang legen (Weltprüfung 07.10., erster Bau: Stiefelabdrücke der Spur und verrollte Stämme
        /// schwebten bis 1 m, ein Nesthügel 0,3 m, Steine und Asche am Feuer des Westhofs lagen unter dem Gelände). Gebaut wird
        /// jedes Requisit wie auf ebenem Boden; danach rückt jedes direkte Teil um den Höhenunterschied des Geländes zwischen
        /// seiner Mitte und der Mitte des Requisits. Baugruppen (Stapel, Rumpf, Mast, Bank, Zaun) rücken als Ganzes. Flache Teile
        /// am Boden (Gruben, Planen, Schlafrollen, Spurstücke, Salzkruste) werden zusätzlich nach dem Hang geneigt.
        /// </summary>
        private static void K28Drape(Transform root)
        {
            if (_v4Terrain == null) return;
            float g0 = V4Y(root.position.x, root.position.z);
            foreach (Transform c in root)
            {
                Vector3 p = c.position;
                c.position = p + Vector3.up * (V4Y(p.x, p.z) - g0);
                bool flat = c.childCount == 0 && c.localPosition.y < 0.25f && c.localScale.y <= 0.15f &&
                            Mathf.Max(c.localScale.x, c.localScale.z) >= 0.9f;
                if (!flat) continue;
                Vector3 fz = c.forward, fx = c.right;
                fz.y = 0f;
                fx.y = 0f;
                if (fz.sqrMagnitude < 0.01f || fx.sqrMagnitude < 0.01f) continue;
                float hz = c.lossyScale.z * 0.5f, hx = c.lossyScale.x * 0.5f;
                Vector3 az = fz.normalized * hz, ax = fx.normalized * hx;
                float pitch = Mathf.Atan2(V4Y(p.x + az.x, p.z + az.z) - V4Y(p.x - az.x, p.z - az.z), 2f * hz) * Mathf.Rad2Deg;
                float roll = Mathf.Atan2(V4Y(p.x + ax.x, p.z + ax.z) - V4Y(p.x - ax.x, p.z - ax.z), 2f * hx) * Mathf.Rad2Deg;
                c.localRotation *= Quaternion.Euler(-pitch, 0f, roll);
            }
        }

        /// <summary>Lange Bodenspur (Fahr- oder Schleifspur) in Stücken, damit sie dem Hang folgen kann (siehe <see cref="K28Drape"/>).</summary>
        private static void K28GroundTrack(Transform t, string name, float x, float z0, float z1, float width, int pieces, Material m, float yaw = 0f)
        {
            float len = (z1 - z0) / pieces;
            for (int i = 0; i < pieces; i++)
                K.Box(name, t, new Vector3(x, 0.012f, z0 + len * (i + 0.5f)), new Vector3(width, 0.012f, len * 1.04f), m, new Vector3(0f, yaw, 0f), false);
        }

        /// <summary>K-A: Vorratsgruben – abgedeckte Gruben, zwei aufgewühlt, Erdhaufen und angefressene Rüben.</summary>
        private static void K28StoragePits(Transform t)
        {
            Vector3[] pits = { new Vector3(-2.4f, 0f, 1.2f), new Vector3(0.6f, 0f, 2.2f), new Vector3(2.8f, 0f, -0.6f), new Vector3(-0.8f, 0f, -2.0f), new Vector3(1.6f, 0f, -3.4f) };
            for (int i = 0; i < pits.Length; i++)
            {
                Vector3 p = pits[i];
                bool open = i == 1 || i == 3;
                K.Prim(PrimitiveType.Cylinder, open ? "Grube (aufgewühlt)" : "Grube", t, p + Vector3.up * 0.015f, new Vector3(1.3f, 0.015f, 1.1f), new Vector3(0f, i * 37f, 0f), _ash, false);
                if (open)
                {
                    K.Prim(PrimitiveType.Sphere, "Erdhaufen", t, p + new Vector3(0.9f, 0f, 0.4f), new Vector3(1.1f, 0.4f, 0.9f), new Vector3(0f, i * 50f, 0f), _dirt, false);
                    K.Box("Abdeckung (weggeschoben)", t, p + new Vector3(-1.1f, 0.06f, -0.3f), new Vector3(1.5f, 0.06f, 1.1f), _wood, new Vector3(4f, i * 23f + 30f, 6f), false);
                    for (int k = 0; k < 3; k++)
                        K.Prim(PrimitiveType.Sphere, "Rübe (angefressen)", t, p + new Vector3(0.4f + k * 0.35f, 0.08f, -0.7f + k * 0.2f), new Vector3(0.18f, 0.14f, 0.16f),
                            new Vector3(0f, k * 70f, 20f), _k28Turnip, false);
                }
                else
                    K.Box("Abdeckung", t, p + new Vector3(0f, 0.05f, 0f), new Vector3(1.5f, 0.06f, 1.2f), _wood, new Vector3(0f, i * 23f, 0f), false);
            }
            K.Block("Merkpfahl", t, new Vector3(0.2f, -0.3f, 0.2f), new Vector3(0.1f, 1.5f, 0.1f), _woodDark);
            K.Box("Lumpen am Pfahl", t, new Vector3(0.28f, 1.05f, 0.2f), new Vector3(0.05f, 0.35f, 0.18f), _canvas, new Vector3(0f, 0f, 8f), false);
        }

        /// <summary>K-D: verlassener Holzplatz – ein Stapel Stämme, ein umgestürzter, ein Stumpf mit steckender Axt, Fahrspur ins Leere.</summary>
        private static void K28WoodYard(Transform t)
        {
            // Stapel (Stämme quer, 6 m lang), 4–3–2 – als Baugruppe, damit die Lagen beim Anpassen an den Hang aufeinander liegen bleiben
            Transform stack = K.Group("Stapel", t, Vector3.zero, Vector3.zero);
            float[][] layers = { new[] { -0.84f, -0.28f, 0.28f, 0.84f }, new[] { -0.56f, 0f, 0.56f }, new[] { -0.28f, 0.28f } };
            for (int l = 0; l < layers.Length; l++)
                foreach (float z in layers[l])
                    K.Prim(PrimitiveType.Cylinder, "Stamm", stack, new Vector3(0f, 0.27f + l * 0.48f, z), new Vector3(0.55f, 3f, 0.55f), new Vector3(0f, 0f, 90f), _bark);
            // umgestürzter Stapel: Stämme liegen kreuz und quer
            for (int i = 0; i < 4; i++)
                K.Prim(PrimitiveType.Cylinder, "Stamm (verrollt)", t, new Vector3(-1.4f + i * 0.9f, 0.26f, 4.6f + (i % 2) * 0.8f), new Vector3(0.5f, 2.6f, 0.5f),
                    new Vector3(0f, 18f + i * 23f, 90f), _bark);
            // Stumpf mit Axt
            Transform stump = K.Group("Stumpf mit Axt", t, new Vector3(3.6f, 0f, -2.6f), Vector3.zero);
            K.Prim(PrimitiveType.Cylinder, "Stumpf", stump, new Vector3(0f, 0.3f, 0f), new Vector3(0.8f, 0.35f, 0.8f), Vector3.zero, _wood);
            K.Box("Axtstiel", stump, new Vector3(0f, 0.95f, 0.15f), new Vector3(0.05f, 0.85f, 0.05f), _woodDark, new Vector3(28f, 0f, 0f), false);
            K.Box("Axtblatt", stump, new Vector3(0f, 0.66f, -0.02f), new Vector3(0.05f, 0.18f, 0.24f), _metal, new Vector3(28f, 0f, 0f), false);
            // Sägebock mit halb gesägtem Stamm
            Transform horse = K.Group("Sägebock", t, new Vector3(-4f, 0f, -2.2f), Vector3.zero);
            for (int s = -1; s <= 1; s += 2)
            {
                K.Box("Bock", horse, new Vector3(s * 0.9f, 0.45f, 0f), new Vector3(0.08f, 1.0f, 0.08f), _woodDark, new Vector3(0f, 0f, 20f), false);
                K.Box("Bock", horse, new Vector3(s * 0.9f, 0.45f, 0f), new Vector3(0.08f, 1.0f, 0.08f), _woodDark, new Vector3(0f, 0f, -20f), false);
            }
            K.Prim(PrimitiveType.Cylinder, "Stamm (halb gesägt)", horse, new Vector3(0f, 0.85f, 0f), new Vector3(0.4f, 1.4f, 0.4f), new Vector3(0f, 0f, 90f), _bark);
            // Fahrspur, die am Platz endet (in Stücken, sie steigt zum Weg hin an)
            for (int s = -1; s <= 1; s += 2)
                K28GroundTrack(t, "Fahrspur", s * 0.8f, -13.5f, -4.5f, 0.35f, 4, _dirt);
        }

        /// <summary>U-1: Nest der Flüsterbälge – drei Nesthügel aus Zweigen und Laub, ein Fetzen von Tamos Wagenplane.</summary>
        private static void K28Nest(Transform t)
        {
            Vector3[] nests = { new Vector3(0f, 0f, 0f), new Vector3(-3.2f, 0f, 1.6f), new Vector3(2.4f, 0f, 2.6f) };
            for (int i = 0; i < nests.Length; i++)
            {
                Vector3 n = nests[i];
                K.Prim(PrimitiveType.Sphere, "Nesthügel", t, n, new Vector3(1.7f - i * 0.2f, 0.55f, 1.5f - i * 0.15f), new Vector3(0f, i * 40f, 0f), _bark, false);
                for (int k = 0; k < 8; k++)
                    K.Box("Zweig", t, OnRingAt(n, k * 45f + i * 10f, 0.85f, 0.18f), new Vector3(0.04f, 0.04f, 0.9f), _woodDark, new Vector3(25f, k * 45f + 90f, 0f), false);
            }
            K.Box("Fetzen der Wagenplane", t, new Vector3(1.2f, 0.32f, -1.6f), new Vector3(1.6f, 0.03f, 1.1f), _canvas, new Vector3(8f, 30f, -12f), false);
            K.Box("Fetzen der Wagenplane", t, new Vector3(-2.2f, 0.4f, 1.3f), new Vector3(0.7f, 0.03f, 0.5f), _canvas, new Vector3(-10f, 70f, 6f), false);
        }

        /// <summary>
        /// U-3: Wrack im Salz – Rumpf eines kleinen Lastschiffs, geneigt und halb in der Salzkruste; der Mast steht schräg darüber
        /// (Hinweis im Auftrag: „Mast über der Salzkruste“). Die Marke des Orts liegt neben dem Rumpf.
        /// </summary>
        private static void K28Wreck(Transform t)
        {
            K.Prim(PrimitiveType.Cylinder, "Salzkruste", t, new Vector3(0f, 0.02f, 0f), new Vector3(7f, 0.02f, 11f), Vector3.zero, _salt, false);
            Transform hull = K.Group("Rumpf", t, new Vector3(0f, -0.7f, 0f), new Vector3(4f, 0f, 18f));
            K.Box("Bauch", hull, new Vector3(0f, 0.6f, 0f), new Vector3(3.4f, 1.4f, 12.5f), _woodDark);
            K.Box("Bordwand links", hull, new Vector3(-1.85f, 1.7f, 0f), new Vector3(0.25f, 2.2f, 12f), _wood);
            K.Box("Bordwand rechts", hull, new Vector3(1.85f, 1.5f, -0.6f), new Vector3(0.25f, 1.8f, 9.5f), _wood);
            K.Box("Bug", hull, new Vector3(0f, 1.4f, 6.6f), new Vector3(2.4f, 2.2f, 2.4f), _wood, new Vector3(0f, 45f, 0f));
            K.Box("Achterkastell", hull, new Vector3(0f, 2.3f, -5.4f), new Vector3(3.6f, 1.6f, 2.6f), _woodDark);
            for (int i = 0; i < 4; i++)
                K.Box("Deckplanke (gebrochen)", hull, new Vector3(-1f + i * 0.65f, 2.75f, 1.5f - i * 1.1f), new Vector3(0.5f, 0.06f, 2.2f - i * 0.3f), _wood,
                    new Vector3(i * 6f, 0f, -4f + i * 3f), false);
            Transform mast = K.Group("Mast", t, new Vector3(0.4f, 1.8f, 1.2f), new Vector3(6f, 0f, 14f));
            K.Prim(PrimitiveType.Cylinder, "Mastbaum", mast, new Vector3(0f, 4.5f, 0f), new Vector3(0.34f, 4.5f, 0.34f), Vector3.zero, _woodDark);
            K.Box("Rah", mast, new Vector3(0f, 7.6f, 0f), new Vector3(4.6f, 0.16f, 0.16f), _woodDark, new Vector3(0f, 25f, 6f), false);
            K.Box("Segelfetzen", mast, new Vector3(-0.6f, 6.6f, 0.1f), new Vector3(2.2f, 1.8f, 0.04f), _canvas, new Vector3(0f, 25f, 4f), false);
            K.Box("Tau", mast, new Vector3(0.9f, 4.3f, 0.4f), new Vector3(0.03f, 6.4f, 0.03f), _straw, new Vector3(0f, 0f, -18f), false);
            for (int i = 0; i < 3; i++)
                K.Prim(PrimitiveType.Sphere, "Schlick", t, new Vector3(-2.6f + i * 2.4f, 0f, -3.5f + i * 3.1f), new Vector3(1.4f, 0.25f, 1.1f), new Vector3(0f, i * 40f, 0f), _k28Silt, false);
        }

        /// <summary>U-5: Zeichenstein – Gestalt wie die Zeichensteine am ältesten Herd (G7), etwas kleiner; das Zeichen bleibt ungedeutet.</summary>
        private static void K28SignStone(Transform t)
        {
            Transform s = K.Group("Stein", t, Vector3.zero, new Vector3(0f, 0f, 3f));
            K.Block("Stein", s, new Vector3(0f, -0.35f, 0f), new Vector3(1.0f, 1.9f, 0.45f), _stone, 0f);
            K.Box("Zeichen", s, new Vector3(0f, 1.0f, 0.235f), new Vector3(0.48f, 0.48f, 0.02f), _stoneDark, default, false);
            K.Prim(PrimitiveType.Sphere, "Moos am Fuß", t, new Vector3(0.1f, 0.02f, -0.1f), new Vector3(1.3f, 0.2f, 0.8f), Vector3.zero, _moss, false);
        }

        /// <summary>
        /// U-6: verlassener Hof (Wüstung) – Mauerreste ohne Dach, eingestürzte Balken, Zaunreste; davor das Lager der Deserteure
        /// (Schlafrollen, kaltes Feuer, Kisten). Front (lokal +z) zur Straße.
        /// </summary>
        private static void K28Farmstead(Transform t, bool west)
        {
            K.Block("Rückwand", t, new Vector3(0f, -0.4f, -3f), new Vector3(8f, 2.8f, 0.5f), _stone);
            K.Block("Seitenwand", t, new Vector3(-4f, -0.4f, 0f), new Vector3(0.5f, 2.0f, 6f), _stone);
            K.Block("Seitenwand (eingestürzt)", t, new Vector3(4f, -0.4f, -1.5f), new Vector3(0.5f, 2.6f, 3f), _stone);
            K.Block("Front links", t, new Vector3(-2.6f, -0.4f, 3f), new Vector3(2.8f, 2.4f, 0.5f), _stone);
            K.Block("Front rechts (Rest)", t, new Vector3(3.1f, -0.4f, 3f), new Vector3(1.8f, 1.5f, 0.5f), _stone);
            // eingestürzte Dachbalken mit Kollision (Weltprüfung: 6 m lange Balken darf man nicht durchlaufen)
            for (int i = 0; i < 3; i++)
                K.Box("Dachbalken (eingestürzt)", t, new Vector3(-1.8f + i * 1.7f, 0.9f - i * 0.25f, 0f), new Vector3(0.25f, 0.25f, 6.4f), _woodDark,
                    new Vector3(14f - i * 9f, i * 7f, 0f));
            K.Prim(PrimitiveType.Sphere, "Schutt", t, new Vector3(2.6f, 0f, 1.2f), new Vector3(2.2f, 0.6f, 1.6f), Vector3.zero, _stoneDark, false);
            // Zaunreste neben dem Hof (als Baugruppe: die Latte bleibt auf den Pfählen)
            Transform fence = K.Group("Zaunreste", t, new Vector3(5.5f, 0f, 8.1f), Vector3.zero);
            for (int i = 0; i < 5; i++)
                K.Block("Zaunpfahl", fence, new Vector3(0f, -0.3f, -3.6f + i * 1.8f), new Vector3(0.14f, i == 3 ? 0.6f : 1.4f, 0.14f), _woodDark);
            K.Box("Zaunlatte", fence, new Vector3(0f, 0.75f, -1.8f), new Vector3(0.06f, 0.1f, 3.6f), _wood, new Vector3(0f, 0f, 6f), false);
            // Lager der Deserteure vor dem Hof
            for (int i = 0; i < 3; i++)
                K.Box("Schlafrolle", t, new Vector3(-3.4f + i * 1.3f, 0.06f, 5.4f + (i % 2) * 0.6f), new Vector3(0.8f, 0.12f, 2f), i == 1 ? _k28Cloak : _canvas,
                    new Vector3(0f, -12f + i * 9f, 0f), false);
            // Feuerstelle (am Osthof weiter links: dort steht rechts vorn ein Baum)
            var fire = new Vector3(west ? 0.8f : -1.6f, 0f, 7.2f);
            for (int i = 0; i < 6; i++)
                K.Box("Stein", t, OnRingAt(fire, i * 60f, 0.55f, 0.08f), new Vector3(0.24f, 0.18f, 0.22f), _stoneDark, new Vector3(0f, i * 60f, 0f), false);
            K.Prim(PrimitiveType.Sphere, "Asche (kalt)", t, fire + new Vector3(0f, 0.04f, 0f), new Vector3(0.6f, 0.08f, 0.6f), Vector3.zero, _charred, false);
            K.Block("Kiste", t, new Vector3(west ? 2.6f : -2.2f, 0f, 5.8f), new Vector3(0.8f, 0.6f, 0.7f), _wood, 18f);
            K.Block("Kiste", t, new Vector3(west ? 2.8f : -2.0f, 0.6f, 5.9f), new Vector3(0.6f, 0.45f, 0.5f), _wood, 31f);
        }

        /// <summary>U-6: Spur vom Westhof nach Süden (lokal +z) – Stiefelabdrücke mehrerer Leute und eine Schleifspur.</summary>
        private static void K28Trail(Transform t)
        {
            for (int i = 0; i < 14; i++)
            {
                float z = i * 0.75f;
                K.Box("Stiefelabdruck", t, new Vector3((i % 2 == 0 ? -0.18f : 0.18f) - 0.5f, 0.015f, z), new Vector3(0.12f, 0.015f, 0.28f), _charred, new Vector3(0f, 4f, 0f), false);
                K.Box("Stiefelabdruck", t, new Vector3((i % 2 == 0 ? 0.18f : -0.18f) + 0.6f, 0.015f, z + 0.35f), new Vector3(0.12f, 0.015f, 0.28f), _charred, new Vector3(0f, -6f, 0f), false);
            }
            K28GroundTrack(t, "Schleifspur", 0.05f, 0f, 11f, 0.45f, 4, _dirt, 2f);
        }

        /// <summary>U-7: alter Rastplatz – Bank mit leeren Schalen, Gabenstein, Stiefelabdrücke Richtung Schilf (lokal +z).</summary>
        private static void K28RestingPlace(Transform t)
        {
            // Bank und Gabenstein als Baugruppen (die Schalen bleiben beim Anpassen an den Hang auf Bank und Stein)
            Transform bench = K.Group("Bank mit Schalen", t, new Vector3(0f, 0f, -0.8f), Vector3.zero);
            K.Block("Bank", bench, Vector3.zero, new Vector3(2.2f, 0.45f, 0.45f), _woodDark);
            for (int i = 0; i < 3; i++)
                K.Prim(PrimitiveType.Cylinder, "Leere Schale", bench, new Vector3(-0.7f + i * 0.7f, 0.48f, 0f), new Vector3(0.24f, 0.035f, 0.24f), Vector3.zero, _wood, false);
            Transform gift = K.Group("Gabenstein mit Schale", t, new Vector3(1.9f, 0f, -0.2f), Vector3.zero);
            K.Block("Gabenstein", gift, new Vector3(0f, -0.15f, 0f), new Vector3(0.9f, 0.55f, 0.7f), _stone, 14f);
            K.Prim(PrimitiveType.Cylinder, "Leere Schale", gift, new Vector3(0f, 0.42f, 0f), new Vector3(0.24f, 0.035f, 0.24f), Vector3.zero, _wood, false);
            K.Label(t, new Vector3(0f, 1.0f, -0.8f), "Leere Schalen", "", WorldLabelStyle.Place, 10f, 0.6f);
            for (int i = 0; i < 9; i++)
                K.Box("Stiefelabdruck", t, new Vector3(i % 2 == 0 ? -0.17f : 0.17f, 0.015f, 0.6f + i * 0.8f), new Vector3(0.13f, 0.015f, 0.3f), _charred, new Vector3(0f, 3f, 0f), false);
            K.Label(t, new Vector3(0f, 0.6f, 3.2f), "Stiefelabdrücke", "schwer, keine Sandalen", WorldLabelStyle.Place, 10f, 0.6f);
        }

        /// <summary>U-7: Versteck im Schilf – Schilfhorste, Unterstand aus Schilf, Brotreste, zerrissener Wachmantel, kalte Feuerstelle.</summary>
        private static void K28Hideout(Transform t)
        {
            var rng = new System.Random(28);
            for (int c = 0; c < 12; c++)
            {
                float a = c * 30f + (float)rng.NextDouble() * 14f, rad = 2.6f + (float)rng.NextDouble() * 2.6f;
                Vector3 cc = OnRingAt(Vector3.zero, a, rad, 0f);
                for (int k = 0; k < 6; k++)
                {
                    float h = 1.0f + (float)rng.NextDouble() * 0.5f;
                    Vector3 o = new Vector3((float)rng.NextDouble() * 0.6f - 0.3f, h * 0.5f, (float)rng.NextDouble() * 0.6f - 0.3f);
                    K.Prim(PrimitiveType.Cylinder, "Schilf", t, cc + o, new Vector3(0.04f, h * 0.5f, 0.04f), new Vector3((float)rng.NextDouble() * 10f - 5f, 0f, (float)rng.NextDouble() * 10f - 5f), _k28Reed, false);
                }
            }
            // Unterstand: zwei Pfosten, schräges Schilfdach (Rücken zum Rastplatz)
            K.Block("Pfosten", t, new Vector3(-0.9f, -0.2f, -0.8f), new Vector3(0.1f, 1.3f, 0.1f), _woodDark);
            K.Block("Pfosten", t, new Vector3(0.9f, -0.2f, -0.8f), new Vector3(0.1f, 1.3f, 0.1f), _woodDark);
            K.Box("Schilfdach", t, new Vector3(0f, 0.7f, -0.2f), new Vector3(2.2f, 0.08f, 1.8f), _k28Reed, new Vector3(-35f, 0f, 0f));   // mit Kollision (Weltprüfung)
            K.Box("Lager aus Schilf", t, new Vector3(0f, 0.05f, 0.1f), new Vector3(0.9f, 0.1f, 1.9f), _k28Reed, default, false);
            K.Box("Zerrissener Wachmantel", t, new Vector3(0.7f, 0.1f, 0.4f), new Vector3(1.2f, 0.03f, 0.9f), _k28Cloak, new Vector3(0f, 25f, 6f), false);
            for (int i = 0; i < 4; i++)
                K.Box("Brotrest", t, new Vector3(-0.8f + i * 0.25f, 0.05f, 1.2f + (i % 2) * 0.15f), new Vector3(0.14f, 0.07f, 0.1f), _straw, new Vector3(0f, i * 40f, 0f), false);
            for (int i = 0; i < 5; i++)
                K.Box("Stein", t, OnRingAt(new Vector3(0f, 0f, 2.3f), i * 72f, 0.4f, 0.07f), new Vector3(0.2f, 0.14f, 0.18f), _stoneDark, new Vector3(0f, i * 72f, 0f), false);
            K.Prim(PrimitiveType.Sphere, "Asche (kalt)", t, new Vector3(0f, 0.03f, 2.3f), new Vector3(0.45f, 0.06f, 0.45f), Vector3.zero, _charred, false);
        }

        /// <summary>
        /// U-2: Tresen der „Schale“ (Südostecke des Gasthauses, Front zu den Tischen): Theke, Fässer, Krüge, Regal an der Wand –
        /// oben eine Lücke für Ebbas guten Krug; wer U-2 abgegeben hat, sieht ihn dort stehen (Phasing je Spieler).
        /// </summary>
        private static void K28Counter(Transform t)
        {
            K.Block("Theke", t, new Vector3(0f, -0.1f, 0f), new Vector3(2.8f, 1.1f, 0.7f), _wood);
            K.Box("Thekenplatte", t, new Vector3(0f, 1.03f, 0f), new Vector3(2.95f, 0.06f, 0.82f), _woodDark);
            for (int s = -1; s <= 1; s += 2)
                K.Prim(PrimitiveType.Cylinder, "Fass", t, new Vector3(s * 1.9f, 0.45f, 0.15f), new Vector3(0.7f, 0.45f, 0.7f), Vector3.zero, _wood);
            for (int i = 0; i < 3; i++)
                K.Prim(PrimitiveType.Cylinder, "Krug", t, new Vector3(-0.8f + i * 0.55f, 1.18f, 0.1f), new Vector3(0.14f, 0.11f, 0.14f), Vector3.zero, _k28Crock, false);
            // Regal an der Hauswand (lokal −z), zwei Bretter
            Transform shelf = K.Group("Regal", t, new Vector3(0f, 0f, -2.05f), Vector3.zero);
            for (int s = -1; s <= 1; s += 2)
                K.Block("Regalpfosten", shelf, new Vector3(s * 1.05f, -0.1f, 0f), new Vector3(0.08f, 1.95f, 0.08f), _woodDark);
            K.Box("Brett unten", shelf, new Vector3(0f, 1.0f, 0f), new Vector3(2.2f, 0.04f, 0.3f), _wood, default, false);
            K.Box("Brett oben", shelf, new Vector3(0f, 1.55f, 0f), new Vector3(2.2f, 0.04f, 0.3f), _wood, default, false);
            for (int i = 0; i < 4; i++)
                K.Prim(PrimitiveType.Cylinder, "Krug", shelf, new Vector3(-0.85f + i * 0.4f, 1.12f, 0f), new Vector3(0.13f, 0.1f, 0.13f), Vector3.zero, _k28Crock, false);
            GameObject good = K.Prim(PrimitiveType.Cylinder, "Ebbas guter Krug", shelf, new Vector3(0.6f, 1.71f, 0f), new Vector3(0.2f, 0.14f, 0.2f), Vector3.zero, _k28Crock, false);
            GameObject handle = K.Box("Henkel (mit Draht geflickt)", shelf, new Vector3(0.73f, 1.72f, 0f), new Vector3(0.04f, 0.12f, 0.03f), _metal, default, false);
            shelf.gameObject.AddComponent<StoryFlagProp>().Configure(KarethCatalog.FlagJugReturned, false, good, handle);
            K.MakeDynamic(shelf.gameObject);
            // Wirtshausschild am Pfosten neben der Theke
            K.Block("Schildpfosten", t, new Vector3(1.75f, -0.1f, -0.9f), new Vector3(0.1f, 2.6f, 0.1f), _woodDark);
            K.Box("Schildarm", t, new Vector3(1.4f, 2.35f, -0.9f), new Vector3(0.7f, 0.06f, 0.06f), _woodDark, default, false);
            K.Box("Wirtshausschild", t, new Vector3(1.2f, 2.0f, -0.9f), new Vector3(0.6f, 0.45f, 0.04f), _wood, new Vector3(0f, 90f, 0f), false);
            K.Prim(PrimitiveType.Cylinder, "Schale (Zeichen)", t, new Vector3(1.2f, 2.0f, -0.86f), new Vector3(0.3f, 0.03f, 0.3f), new Vector3(0f, 0f, 90f), _k28Crock, false);
            K.Label(t, new Vector3(0f, 2.9f, 0f), "Zur Schale", "Gasthaus", WorldLabelStyle.Place, 45f, 0.9f);
            // Ausgeruht wie Classic (Max 10.10.2026): die „Schale“ ist ein Gasthaus – Ruheort um Tresen, Tische und Herd
            var rest = new GameObject("Ruheort (Gasthaus Zur Schale)");
            rest.transform.SetParent(t, false);
            rest.AddComponent<RestArea>().Configure(18f);
        }

        /// <summary>Hocker (Wenna an der Kochstelle, Ebba hinter dem Tresen) – Sitzhöhe wie der Platz in der Beidatei.</summary>
        private static void K28Stool(Transform t, float seat)
        {
            K.Box("Sitz", t, new Vector3(0f, seat - 0.03f, 0f), new Vector3(0.4f, 0.06f, 0.4f), _wood, default, false);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                K.Box("Bein", t, new Vector3(Mathf.Sin(a) * 0.13f, (seat - 0.06f) * 0.5f, Mathf.Cos(a) * 0.13f), new Vector3(0.05f, seat - 0.06f, 0.05f), _woodDark,
                    new Vector3(Mathf.Cos(a) * -6f, 0f, Mathf.Sin(a) * 6f), false);
            }
        }
    }
}
