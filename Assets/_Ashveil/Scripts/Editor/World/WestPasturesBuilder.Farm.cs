using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using Ashveil.Gameplay.Visuals;
using Ashveil.Networking;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;
using L = Ashveil.EditorTools.WestPasturesLayout;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Pferdehof Hallgrund und alles, was die Geschichte in der Welt braucht (Vorgabe: Haus, kleiner Stall, Koppel, Nordzaun,
    /// Traenke, Futterlager, Werkbank, Weidetor fuer Q2). Hinweise, gebrochener Zaun, Spuren, Kadaver, Grab: statische
    /// Requisiten, die je nach eigenem Geschichtsstand sichtbar sind (<see cref="StoryPhasedProp"/>).
    /// </summary>
    public static partial class WestPasturesBuilder
    {
        // Hofplan (Welt-Koordinaten)
        private static readonly Vector2 Yard = new Vector2(-798f, 256f);
        private static readonly Vector2 House = new Vector2(-790f, 273f);
        private static readonly Vector2 Barn = new Vector2(-813f, 277f);
        private const float PadW = -850f, PadE = -815f, PadS = 228f, PadN = 262f; // Koppel
        private static readonly Vector2 Gate = new Vector2(PadW, 245f);           // Weidetor (Westzaun, nach Westen)
        private static readonly Vector2 BigPasture = new Vector2(-905f, 250f);

        private static float FY => L.FarmFloor;
        private static Vector3 F(float x, float z, float lift = 0f) => G(x, z, lift);

        private static int Mask(params FarmPhase[] p) => StoryPhasedProp.Mask(p);
        private static readonly int NotCrisis = StoryPhasedProp.Mask(FarmPhase.Normal, FarmPhase.Growing, FarmPhase.Aged, FarmPhase.After, FarmPhase.Epilogue);
        private static readonly int CrisisOnly = StoryPhasedProp.Mask(FarmPhase.Crisis, FarmPhase.Return);
        private static readonly int Weathered = StoryPhasedProp.Mask(FarmPhase.Aged, FarmPhase.After, FarmPhase.Epilogue);

        /// <summary>Gruppe, deren erstes Kind je nach Geschichtsstand sichtbar ist.</summary>
        private static Transform Phased(Transform parent, string name, int mask, int step = -1)
        {
            Transform holder = K.Group(name, parent);
            holder.gameObject.AddComponent<StoryPhasedProp>().Configure(mask, step);
            return K.Group("Inhalt", holder);
        }

        // ================================================================== Hof

        private static void BuildFarm(Transform root)
        {
            // Wohnhaus (Tuer nach Sueden)
            Transform h = K.Group("Wohnhaus", root, F(House.x, House.y), Vector3.zero);
            K.Block("Waende", h, new Vector3(0f, -0.4f, 0f), new Vector3(8f, 3.6f, 6f), _wood);
            K.Box("Dach Sued", h, new Vector3(0f, 4.1f, -1.6f), new Vector3(8.8f, 0.3f, 3.9f), _roof, new Vector3(32f, 0f, 0f));
            K.Box("Dach Nord", h, new Vector3(0f, 4.1f, 1.6f), new Vector3(8.8f, 0.3f, 3.9f), _roof, new Vector3(-32f, 0f, 0f));
            K.Box("Kamin", h, new Vector3(2.6f, 4.4f, 1.2f), new Vector3(0.8f, 2.4f, 0.8f), _stoneDark);
            K.Box("Tuer", h, new Vector3(0f, 1f, -3.02f), new Vector3(1.1f, 2.1f, 0.08f), _woodDark, default, false);
            K.Box("Fenster", h, new Vector3(-2.4f, 1.7f, -3.02f), new Vector3(0.9f, 0.8f, 0.06f), _warm, default, false);
            K.Box("Fenster Seite", h, new Vector3(-4.02f, 1.7f, 0f), new Vector3(0.06f, 0.8f, 0.9f), _warm, default, false);
            K.Particles("Rauch", h, new Vector3(2.6f, 5.8f, 1.2f), new Color(0.4f, 0.38f, 0.36f, 0.35f), 0.8f, 5f, 0.6f, 4f, 5f, 0.3f);
            // Bank neben der Tuer (dort sitzt er im Weidegang)
            K.Block("Bank", h, new Vector3(4.2f, -0.1f, -3.6f), new Vector3(1.8f, 0.45f, 0.45f), _woodDark);

            // Stall (vorn offen)
            Transform b = K.Group("Stall", root, F(Barn.x, Barn.y), Vector3.zero);
            K.Block("Rueckwand", b, new Vector3(0f, -0.4f, 3.3f), new Vector3(10f, 3.4f, 0.3f), _wood);
            K.Block("Seite West", b, new Vector3(-5f, -0.4f, 0f), new Vector3(0.3f, 3.4f, 6.6f), _wood);
            K.Block("Seite Ost", b, new Vector3(5f, -0.4f, 0f), new Vector3(0.3f, 3.4f, 6.6f), _wood);
            K.Box("Dach", b, new Vector3(0f, 3.4f, 0f), new Vector3(10.8f, 0.3f, 7.4f), _roof, new Vector3(-8f, 0f, 0f));
            for (int i = -1; i <= 1; i++) K.Block("Box " + (i + 2), b, new Vector3(i * 3.2f, -0.2f, 1.2f), new Vector3(0.12f, 1.3f, 3.6f), _woodDark);
            K.Block("Stroh", b, new Vector3(0f, -0.2f, 1.6f), new Vector3(9f, 0.25f, 3f), _straw);

            // Werkbank unter dem Vordach
            Transform w = K.Group("Werkbank", root, F(-780f, 262f), new Vector3(0f, 180f, 0f));
            K.Block("Tisch", w, new Vector3(0f, -0.1f, 0f), new Vector3(2f, 0.9f, 0.8f), _woodDark);
            K.Box("Hufeisen", w, new Vector3(0.4f, 0.85f, 0f), new Vector3(0.14f, 0.03f, 0.14f), K.Mat("WW_Eisen", new Color(0.3f, 0.3f, 0.32f), 0.4f), default, false);
            K.Box("Vordach", w, new Vector3(0f, 2.6f, -0.4f), new Vector3(3f, 0.12f, 2f), _roof, new Vector3(-12f, 0f, 0f), false);

            // Futterlager (Heuballen unter Pultdach)
            Transform fl = K.Group("Futterlager", root, F(-823f, 266f), Vector3.zero);
            for (int i = 0; i < 5; i++) K.Block("Heuballen", fl, new Vector3((i % 3) * 1.2f - 1.2f, -0.1f + (i / 3) * 0.7f, 0f), new Vector3(1.1f, 0.7f, 0.8f), _straw);
            K.Box("Pultdach", fl, new Vector3(0f, 2.3f, 0.3f), new Vector3(4.2f, 0.12f, 2.4f), _roof, new Vector3(14f, 0f, 0f));

            // Traenke an der Koppel
            Transform tr = K.Group("Traenke", root, F(-812f, 246f), new Vector3(0f, 90f, 0f));
            K.Block("Trog", tr, new Vector3(0f, -0.1f, 0f), new Vector3(2.2f, 0.55f, 0.7f), _woodDark);
            K.Box("Wasser", tr, new Vector3(0f, 0.42f, 0f), new Vector3(2f, 0.04f, 0.5f), _water, default, false);

            // Koppel: Zaun ringsum, Westseite mit Weidetor (Q2) und dem Stueck, das in Q1 eingedrueckt ist
            Transform pad = K.Group("Koppel", root);
            FenceLine(pad, "Zaun Sued", new Vector2(PadW, PadS), new Vector2(PadE, PadS));
            FenceLine(pad, "Zaun Nord", new Vector2(PadE, PadN), new Vector2(PadW, PadN));
            FenceLine(pad, "Zaun Ost Sued", new Vector2(PadE, PadS), new Vector2(PadE, 240f));
            FenceLine(pad, "Zaun Ost Nord", new Vector2(PadE, 250f), new Vector2(PadE, PadN)); // Durchgang zum Hof
            FenceLine(pad, "Zaun West Sued", new Vector2(PadW, PadS), new Vector2(PadW, Gate.y - 2.5f));
            FenceLine(pad, "Zaun West Mitte", new Vector2(PadW, Gate.y + 2.5f), new Vector2(PadW, 252f));
            // das Stueck Westzaun bei z 252–262: heil (Alltag) oder von aussen eingedrueckt (Krise) – Hinweis 1
            FenceLine(Phased(pad, "Westzaun heil", NotCrisis), "Zaun West Nord", new Vector2(PadW, 252f), new Vector2(PadW, PadN));
            Transform broken = Phased(pad, "Westzaun eingedrueckt (Hinweis 1)", CrisisOnly);
            BrokenFence(broken, new Vector2(PadW, 252f), new Vector2(PadW, PadN));
            // Weidetor (Q2): geschlossen bis zum Freilassen
            Transform gateClosed = Phased(pad, "Weidetor zu", StoryPhasedProp.Mask(FarmPhase.Normal, FarmPhase.Crisis, FarmPhase.Return, FarmPhase.Growing, FarmPhase.Aged));
            GateLeaf(gateClosed, 0f);
            Transform gateOpen = Phased(pad, "Weidetor offen", StoryPhasedProp.Mask(FarmPhase.After, FarmPhase.Epilogue));
            GateLeaf(gateOpen, 100f);
            // Nordzaun (Weidegrenze zum Wald)
            FenceLine(K.Group("Nordzaun", root), "Nordzaun", new Vector2(-880f, 305f), new Vector2(-760f, 305f));

            // Aecker: suedoestlich und noerdlich (bebautes Land)
            Field(root, "Acker Suedost", new Vector2(-770f, 212f), 40f, 26f, 0f);
            Field(root, "Acker Nord", new Vector2(-805f, 318f), 44f, 18f, 0f);
            // Acker am Waldrand (Hinweis 2): Saat oder zertrampelt
            Field(Phased(root, "Acker Waldrand (heil)", NotCrisis), "Acker Waldrand", new Vector2(-925f, 128f), 36f, 30f, 12f);
            Transform trampled = Phased(root, "Acker Waldrand (zertrampelt, Hinweis 2)", CrisisOnly);
            Field(trampled, "Acker Waldrand zertrampelt", new Vector2(-925f, 128f), 36f, 30f, 12f, true);

            // Baum mit Grab (Epilog) – der Baum steht immer
            Vector3 tree = F(-774f, 290f);
            if (ArtModels.Has("SM_DeadTree")) ArtModels.Place("SM_DeadTree", root, tree, 40f, 1.3f, ArtModels.ColliderKind.Trunk, true);
            else K.Prim(PrimitiveType.Cylinder, "Baum", root, tree + Vector3.up * 3f, new Vector3(0.6f, 3f, 0.6f), Vector3.zero, _woodDark);
            Transform grave = Phased(root, "Grab (Epilog)", StoryPhasedProp.Mask(FarmPhase.Epilogue));
            K.Block("Grabhuegel", grave, tree + new Vector3(2.5f, -0.2f, -1f), new Vector3(1f, 0.35f, 2f), _soil);
            K.Block("Holzkreuz", grave, tree + new Vector3(2.5f, 0f, -2.1f), new Vector3(0.1f, 1.2f, 0.1f), _woodDark);
            K.Box("Querholz", grave, tree + new Vector3(2.5f, 0.85f, -2.1f), new Vector3(0.6f, 0.08f, 0.08f), _woodDark, default, false);

            // Gealtert (Weidegang und danach): verwitterte Latten, Gras am Stall, altes Wagenrad – ohne Jahreszahl (Vorgabe)
            Transform aged = Phased(root, "Hof gealtert", Weathered);
            K.Box("Lose Latte", aged, F(PadE + 0.2f, 256f, 0.35f), new Vector3(0.1f, 0.08f, 2.4f), _woodDark, new Vector3(0f, 0f, 40f), false);
            K.Box("Wagenrad", aged, F(-805f, 270f, 0.5f), new Vector3(1.1f, 1.1f, 0.12f), _woodDark, new Vector3(0f, 20f, 70f), false);
            for (int i = 0; i < 9; i++)
                K.Prim(PrimitiveType.Sphere, "Wildwuchs", aged, F(Barn.x - 5f + i * 1.2f, Barn.y + 3.8f, 0.2f), new Vector3(0.9f, 0.5f, 0.9f), Vector3.zero,
                    K.Mat("WW_Wildwuchs", new Color(0.3f, 0.36f, 0.18f)), false);
            // Normalzustand: sauber aufgeschichtetes Holz (verschwindet im Alter)
            Transform tidy = Phased(root, "Hof gepflegt", StoryPhasedProp.Mask(FarmPhase.Normal, FarmPhase.Crisis, FarmPhase.Return, FarmPhase.Growing));
            for (int i = 0; i < 4; i++) K.Block("Brennholz", tidy, F(House.x + 4.6f, House.y + 0.5f - i * 0.1f, -0.1f + i * 0.35f), new Vector3(0.6f, 0.34f, 1.6f), _wood);
        }

        private static void FenceLine(Transform parent, string name, Vector2 a, Vector2 b)
        {
            Transform t = K.Group(name, parent);
            float len = Vector2.Distance(a, b);
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 2.6f) + 1);
            for (int i = 0; i < posts; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)(posts - 1));
                K.Block("Pfosten", t, F(p.x, p.y, -0.2f), new Vector3(0.18f, 1.5f, 0.18f), _woodDark);
            }
            Vector2 d = b - a;
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            for (int k = 0; k < 2; k++)
            {
                Vector2 m = (a + b) * 0.5f;
                float y = (L.Height(a.x, a.y) + L.Height(b.x, b.y)) * 0.5f + 0.55f + k * 0.45f;
                K.Box("Latte", t, new Vector3(m.x, y, m.y), new Vector3(0.1f, 0.1f, len), _wood, new Vector3(0f, yaw, 0f));
            }
        }

        /// <summary>Zaun, von aussen (Westen) nach innen gedrueckt: Pfosten schief, Latten gebrochen und nach innen gefallen.</summary>
        private static void BrokenFence(Transform parent, Vector2 a, Vector2 b)
        {
            int posts = 5;
            for (int i = 0; i < posts; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)(posts - 1));
                float lean = i == 1 || i == 2 ? 38f : i == 3 ? 20f : 0f; // nach Osten (innen) gekippt
                K.Box("Pfosten schief", parent, F(p.x + lean * 0.01f, p.y, 0.5f), new Vector3(0.18f, 1.5f, 0.18f), _woodDark, new Vector3(0f, 0f, -lean));
            }
            K.Box("Latte gebrochen A", parent, F(a.x + 1.2f, a.y + 3f, 0.15f), new Vector3(0.1f, 0.1f, 3.2f), _wood, new Vector3(0f, 20f, 8f), false);
            K.Box("Latte gebrochen B", parent, F(a.x + 1.8f, a.y + 5.5f, 0.12f), new Vector3(0.1f, 0.1f, 2.4f), _wood, new Vector3(0f, -35f, 0f), false);
            K.Box("Latte haengt", parent, F(a.x, b.y - 2f, 0.7f), new Vector3(0.1f, 0.1f, 3.6f), _wood, new Vector3(28f, 0f, 0f), false);
            // Fellbueschel am Pfosten: keine Pferdehaare – dunkles, grobes Fell (der Raeuber)
            K.Prim(PrimitiveType.Sphere, "Fellbueschel", parent, F(a.x - 0.2f, a.y + 4.2f, 0.9f), new Vector3(0.18f, 0.12f, 0.14f), Vector3.zero, _fur, false);
        }

        private static void GateLeaf(Transform parent, float swing)
        {
            Transform hinge = K.Group("Tor", parent, F(Gate.x, Gate.y - 2.5f), new Vector3(0f, swing, 0f));
            for (int k = 0; k < 3; k++) K.Box("Brett", hinge, new Vector3(0f, 0.45f + k * 0.4f, 2.5f), new Vector3(0.08f, 0.14f, 5f), _wood);
            K.Box("Strebe", hinge, new Vector3(0f, 0.85f, 2.5f), new Vector3(0.08f, 0.14f, 5.4f), _woodDark, new Vector3(-18f, 0f, 0f));
        }

        private static void Field(Transform parent, string name, Vector2 c, float w, float d, float yaw, bool trampled = false)
        {
            Transform f = K.Group(name, parent, F(c.x, c.y), new Vector3(0f, yaw, 0f));
            int rows = Mathf.FloorToInt(d / 1.6f);
            for (int r = 0; r < rows; r++)
            {
                float z = -d * 0.5f + r * 1.6f;
                K.Box("Furche", f, new Vector3(0f, 0.05f, z), new Vector3(w, 0.1f, 0.7f), _soil, default, false);
                if (trampled && r % 3 != 0) continue; // zertrampelt: viele Reihen platt
                K.Box("Saat", f, new Vector3(0f, trampled ? 0.12f : 0.3f, z), new Vector3(w * (trampled ? 0.4f : 1f), trampled ? 0.06f : 0.35f, 0.35f), _crop,
                    new Vector3(0f, trampled ? r * 17f : 0f, 0f), false);
            }
            if (!trampled) return;
            // Hufspuren quer durch, und ein Halfterstueck
            for (int i = 0; i < 18; i++)
                K.Box("Huf", f, new Vector3(-w * 0.5f + i * (w / 18f), 0.13f, Mathf.Sin(i * 0.9f) * d * 0.3f), new Vector3(0.18f, 0.02f, 0.22f), _soil, default, false);
            K.Box("Halfterstueck", f, new Vector3(2f, 0.16f, -3f), new Vector3(0.05f, 0.03f, 0.9f), _leather, new Vector3(0f, 35f, 0f), false);
        }

        // ================================================================== Geschichte

        private static void BuildStory(Transform root)
        {
            Transform anchors = K.Group("Anker", root);
            Anchor(anchors, NetworkProgression.AnchorYard, Yard, 30f, 180f);
            Anchor(anchors, NetworkProgression.AnchorPasture, new Vector2(-832f, 245f), 12f, -90f);
            Anchor(anchors, NetworkProgression.AnchorGate, Gate, 3f, -90f);
            Anchor(anchors, NetworkProgression.AnchorBigPasture, BigPasture, 26f, -90f);
            // Senke: Blick "nach hinten" = zur Rueckwand im Westen, Eingang (Hohlweg) von Osten
            Anchor(anchors, NetworkProgression.AnchorBasin, L.BasinCenter, L.BasinRadius, -90f);
            Vector2 foal = L.BasinCenter + new Vector2(-15f, -4f);
            Anchor(anchors, NetworkProgression.AnchorFoal, foal, 2.5f, -90f);
            Vector3 entry = L.Ravine.PointAt(L.Ravine.Length - 26.0);
            Anchor(anchors, NetworkProgression.AnchorBasinEntry, new Vector2(entry.x, entry.z), 3f, -90f);
            // Weidegang: drei Bedrohungen (Wolfsrudel am Waldrand, Nadelspeier am Bach, Einzelgaenger am Hang)
            Anchor(anchors, NetworkProgression.AnchorThreatWolves, new Vector2(-905f, 348f), 8f, 0f);
            Anchor(anchors, NetworkProgression.AnchorThreatSpitters, new Vector2(-962f, 182f), 6f, 0f);
            Anchor(anchors, NetworkProgression.AnchorThreatLone, new Vector2(-705f, 338f), 5f, 0f);
            for (int i = 0; i < L.Clues.Length; i++) Anchor(anchors, NetworkProgression.ClueAnchor(i), L.Clues[i].pos, L.Clues[i].radius, 0f);

            // Heimweg der Herde (kuratiert, Punkte auf dem Boden)
            var routeGo = new GameObject("Heimweg der Herde");
            routeGo.transform.SetParent(root, false);
            var pts = new List<Vector3>();
            for (double s = 0; s < L.HerdReturn.Length; s += 4.0)
            {
                Vector3 p = L.HerdReturn.PointAt(s);
                pts.Add(G(p.x, p.z));
            }
            Vector3 end = L.HerdReturn.PointAt(L.HerdReturn.Length);
            pts.Add(G(end.x, end.z));
            routeGo.AddComponent<HerdRoute>().Configure(NetworkProgression.RouteReturn, pts.ToArray());

            // Hofherde (lokal, je Spieler)
            var herdGo = new GameObject("Hofherde");
            herdGo.transform.SetParent(root, false);
            herdGo.transform.position = F(-832f, 245f);
            var pastureT = anchors.Find("Anker " + NetworkProgression.AnchorPasture);
            var bigT = anchors.Find("Anker " + NetworkProgression.AnchorBigPasture);
            herdGo.AddComponent<FarmHerdLocal>().Bind(pastureT, bigT);

            // Weidetor: Handlung (Q2)
            var gate = new GameObject("Weidetor (Handlung)");
            gate.transform.SetParent(root, false);
            gate.transform.position = F(Gate.x, Gate.y, 0.2f);
            StoryInteractable.Add(gate, "open_gate", "story.horse.action.gate", 4.5f, new Vector3(1.2f, 2f, 5.4f), new Vector3(0f, 1f, 0f));
            gate.AddComponent<StoryGateCondition>();

            BuildFarmer(root);
            BuildClues(K.Group("Hinweise und Spuren", root));
            BuildBasinProps(K.Group("Felskessel", root));
        }

        private static void Anchor(Transform parent, string id, Vector2 p, float radius, float yaw)
        {
            var go = new GameObject("Anker " + id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(G(p.x, p.y, 0.05f), Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<StoryAnchor>().Configure(id, radius);
        }

        // ------------------------------------------------------------------ Farmer

        private static void BuildFarmer(Transform root)
        {
            Transform npc = K.Group("Brun Haller (Hof)", root, F(Yard.x, Yard.y), Vector3.zero);
            npc.gameObject.AddComponent<NpcIdentity>().Configure(HorseStoryCatalog.BreederNpcId);
            Transform fig = K.Group("Figur", npc);
            fig.position = F(-781f, 260.5f);
            var col = fig.gameObject.AddComponent<CapsuleCollider>();
            col.direction = 1; col.radius = 0.35f; col.height = 1.8f; col.center = new Vector3(0f, 0.9f, 0f);
            Transform visual = K.Group("Visual", fig);
            Animator anim = null;
            EmotePoser poser = null;
            if (QuaterniusImport.IsImported)
            {
                string path = CharacterBodies.PathFor(false);
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
                GameObject model = PlayerRigBuilder.InstantiateModel(path, "Model", visual, controller);
                if (model != null)
                {
                    anim = model.GetComponent<Animator>();
                    anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    poser = model.AddComponent<EmotePoser>();
                    K.SetRef(poser, "animator", anim);
                    // alter Mann, grauer Bart, Arbeitskleidung (Aussehen fest, nicht zufaellig)
                    model.AddComponent<NpcBody>().Configure("1|b0|s1|h3|c6|d4|a2|e3|m2", new[] { "travel_hood", "travel_trousers", "foot_wraps", "rope_belt" }, fig);
                    fig.gameObject.AddComponent<CharacterLod>();
                }
            }
            if (anim == null)
                K.Prim(PrimitiveType.Capsule, "Koerper (Platzhalter)", visual, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Vector3.zero, _cloth, false);
            WorldLabel label = K.Label(fig, new Vector3(0f, 2.15f, 0f), Loc.T("npc.human.horsebreeder_01.name"), Loc.T("npc.human.horsebreeder_01.title"), WorldLabelStyle.Friendly, 40f);

            Transform spots = K.Group("Plaetze", npc);
            Transform Spot(string n, float x, float z, float yaw)
            {
                Transform t = K.Group(n, spots);
                t.SetPositionAndRotation(F(x, z), Quaternion.Euler(0f, yaw, 0f));
                return t;
            }
            Transform[] work = { Spot("Werkbank", -780.5f, 261.2f, 0f), Spot("Koppel", -817f, 252f, -90f), Spot("Traenke", -812f, 247.6f, 180f) };
            Transform fence = Spot("Westzaun", -846f, 258f, -90f);
            Transform bench = Spot("Bank", House.x + 4.2f, House.y - 3.6f, 180f);
            Transform door = Spot("Tuer", House.x, House.y - 3.4f, 0f);
            Transform gateWatch = Spot("Torblick", -846f, 238f, -90f);
            var fp = npc.gameObject.AddComponent<FarmerPresenter>();
            fp.Bind(fig, anim, poser, work, fence, bench, door, gateWatch);
            K.SetLayerRecursively(npc.gameObject, LayerMask.NameToLayer(GameLayers.Interactable));
            K.MakeDynamic(npc.gameObject);
            EditorUtility.SetDirty(fp);
            _ = label;
        }

        // ------------------------------------------------------------------ Hinweise

        private static void BuildClues(Transform root)
        {
            // Suchgebiete: grober Hinweis am Ort (nur, solange genau dieser Schritt laeuft) – keine Pfeilkette
            for (int i = 0; i < L.Clues.Length; i++)
            {
                Transform area = Phased(root, "Suchgebiet " + (i + 1), StoryPhasedProp.Mask(FarmPhase.Crisis), i);
                Vector2 c = L.Clues[i].pos;
                K.Label(area, F(c.x, c.y, 4f), Loc.T("story.horse.search_area"), Loc.T("story.horse.search_hint"), WorldLabelStyle.Info, 140f);
            }
            // Spuren entlang des Weges der Herde (sichtbar, solange die Suche laeuft)
            Transform tracks = Phased(root, "Hufspuren", StoryPhasedProp.Mask(FarmPhase.Crisis));
            var trail = L.SearchTrail;
            int n = 0;
            for (double s = 14; s < trail.Length - 20; s += 9.0, n++)
            {
                Vector3 p = trail.PointAt(s);
                Vector3 dir = trail.DirAt(s);
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                for (int k = -1; k <= 1; k += 2)
                {
                    Vector3 q = p + side * (k * 0.8f + (n % 3 - 1) * 0.5f);
                    if (L.Brook.Dist(q.x, q.z, out _) < 2.5) continue;
                    K.Box("Huf", tracks, G(q.x, q.z, 0.02f), new Vector3(0.2f, 0.02f, 0.24f), _soil, new Vector3(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f), false);
                }
                if (n % 5 == 2)
                {
                    // einzelne grosse Pfote neben der Herde (der Treiber)
                    Vector3 q = p + side * 3.2f;
                    K.Box("Pfote gross", tracks, G(q.x, q.z, 0.02f), new Vector3(0.32f, 0.02f, 0.36f), K.Mat("WW_Pfote", new Color(0.12f, 0.1f, 0.09f)), default, false);
                }
            }

            // Hinweis 3: Traenke am Bach – Hufspuren bis ans Wasser, dann Kehrtwende
            Transform c3 = Phased(root, "Hinweis 3 (Bach)", CrisisOnly);
            Vector2 b = L.Clues[2].pos;
            for (int i = 0; i < 14; i++)
            {
                float a = i / 13f * Mathf.PI;
                Vector2 p = b + new Vector2(Mathf.Cos(a) * 5f + 6f, Mathf.Sin(a) * 4f - 2f);
                K.Box("Huf Kehre", c3, G(p.x, p.y, 0.02f), new Vector3(0.2f, 0.02f, 0.24f), _soil, new Vector3(0f, a * Mathf.Rad2Deg, 0f), false);
            }

            // Hinweis 4: Kadaver eines alten Wallachs im Dickicht (nicht gefressen)
            Transform c4 = Phased(root, "Hinweis 4 (Wallach)", CrisisOnly);
            Vector2 w = L.Clues[3].pos + new Vector2(3f, 2f);
            Transform body = K.Group("Wallach", c4, G(w.x, w.y, 0.35f), new Vector3(0f, 60f, 90f));
            K.Prim(PrimitiveType.Capsule, "Rumpf", body, Vector3.zero, new Vector3(0.75f, 1.0f, 0.85f), new Vector3(90f, 0f, 0f), _hide, false);
            K.Prim(PrimitiveType.Capsule, "Hals", body, new Vector3(0f, 0.2f, 1.3f), new Vector3(0.35f, 0.5f, 0.4f), new Vector3(60f, 0f, 0f), _hide, false);
            for (int i = 0; i < 4; i++)
                K.Prim(PrimitiveType.Capsule, "Bein", body, new Vector3(i < 2 ? 0.3f : -0.3f, -0.6f, i % 2 == 0 ? 0.6f : -0.6f), new Vector3(0.14f, 0.5f, 0.14f), new Vector3(0f, 0f, 90f), _hide, false);
            for (int i = 0; i < 7; i++)
                K.Box("Aeste", c4, G(w.x + Mathf.Sin(i) * 2f, w.y + Mathf.Cos(i * 1.7f) * 2f, 0.4f), new Vector3(0.08f, 0.08f, 2.4f), _woodDark, new Vector3(20f, i * 51f, 0f), false);

            // Hinweis 5: Biss- und Kratzspuren an beiden Seiten, in Brusthoehe
            Transform c5 = Phased(root, "Hinweis 5 (Spuren)", CrisisOnly);
            Vector2 m = L.Clues[4].pos;
            var scratch = K.Mat("WW_Kratzer", new Color(0.62f, 0.52f, 0.38f));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 stump = G(m.x + side * 4f, m.y + 1f);
                K.Prim(PrimitiveType.Cylinder, "Stamm", c5, stump + Vector3.up * 1.5f, new Vector3(0.5f, 1.5f, 0.5f), Vector3.zero, _woodDark);
                for (int k = 0; k < 3; k++)
                    K.Box("Kratzer", c5, stump + new Vector3(-side * 0.26f, 1.2f + k * 0.08f, -0.1f + k * 0.1f), new Vector3(0.02f, 0.03f, 0.3f), scratch, new Vector3(0f, 0f, 25f), false);
                K.Prim(PrimitiveType.Sphere, "Maehnenhaar", c5, stump + new Vector3(-side * 0.3f, 1.3f, 0.3f), new Vector3(0.2f, 0.1f, 0.2f), Vector3.zero, K.Mat("WW_Maehnenhaar", new Color(0.06f, 0.05f, 0.04f)), false);
            }

            // Hinweis 6: Senke – zusammengedraengte Hufspuren, eine grosse Faehrte fuehrt bergauf
            Transform c6 = Phased(root, "Hinweis 6 (Senke)", CrisisOnly);
            Vector2 hol = L.Clues[5].pos;
            var rng = new System.Random(66);
            for (int i = 0; i < 40; i++)
            {
                Vector2 p = hol + new Vector2((float)rng.NextDouble() * 10f - 5f, (float)rng.NextDouble() * 8f - 4f);
                K.Box("Huf eng", c6, G(p.x, p.y, 0.02f), new Vector3(0.2f, 0.02f, 0.24f), _soil, new Vector3(0f, (float)rng.NextDouble() * 360f, 0f), false);
            }
            Vector2 to = new Vector2((float)L.Ravine.X[0], (float)L.Ravine.Z[0]);
            for (int i = 0; i < 12; i++)
            {
                Vector2 p = Vector2.Lerp(hol, to, i / 12f);
                K.Box("Pfote gross", c6, G(p.x, p.y, 0.02f), new Vector3(0.32f, 0.02f, 0.36f), K.Mat("WW_Pfote", new Color(0.12f, 0.1f, 0.09f)), default, false);
            }
        }

        private static void BuildBasinProps(Transform root)
        {
            // Wurzel, an der das Fohlen mit dem zerrissenen Halfter haengt (solange die Geschichte laeuft)
            Vector2 foal = L.BasinCenter + new Vector2(-15f, -4f);
            Transform held = Phased(root, "Wurzel mit Halfter", CrisisOnly);
            Vector3 rootPos = G(foal.x - 1.6f, foal.y);
            K.Prim(PrimitiveType.Cylinder, "Wurzel", held, rootPos + new Vector3(0f, 0.3f, 0f), new Vector3(0.35f, 0.6f, 0.35f), new Vector3(0f, 0f, 70f), _woodDark);
            K.Box("Halfterstrick", held, rootPos + new Vector3(0.8f, 0.6f, 0f), new Vector3(1.4f, 0.04f, 0.04f), _leather, new Vector3(0f, 0f, -12f), false);
            // Die Wurzel bleibt (ohne Strick) – der Ort erinnert sich
            K.Prim(PrimitiveType.Cylinder, "Wurzelstock", root, rootPos + new Vector3(-0.8f, 0.4f, 0f), new Vector3(0.5f, 0.7f, 0.5f), new Vector3(0f, 0f, 20f), _woodDark);
        }
    }
}
