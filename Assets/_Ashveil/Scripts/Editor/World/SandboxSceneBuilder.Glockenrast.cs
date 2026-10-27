using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using Ashveil.World;
using UnityEngine;
using C = Ashveil.Data.GlockenrastCatalog;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// P3 Glockenrast (Stufe 5–9): alles, was die Auftraege G1–G13 in der Szene brauchen – nur hinzugefuegt, Kareth und die
    /// Quest-Orte (KarethQuestSites) bleiben unveraendert:
    ///   · Figuren mit Tagesablauf: Bruder Anselm und Moorwaechterin Runa an der Glockenrast, Merle am Rand des
    ///     Holzfaellerlagers, Pilger Edo am Wegschrein, Jasko verletzt im Nordmoor (Tamo und Veit gab es schon)
    ///   · Erkundungsziele (QuestArea): Holzfaellerlager, Harzerlager, Schaedeldecke, Glockenturm, Nordmoor
    ///   · Fundstellen und Geschichtsschritte (StoryAnchor + StoryInteractable + QuestNodeCondition, Kennungen im Katalog)
    ///   · Greybox: Glockenzeichen ueber Anselms Tuer (Vorzeichen), Turmspitze mit Inschrift, halb versunkene Kuppeln,
    ///     Zeichensteine an der Schaedeldecke, Schloss am Glockengalgen und Jasko im Holzfaellerlager (nur nach G13, Phasing)
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        private static Vector2 SiteXZ(string id)
        {
            foreach (KarethQuestSites.Site s in KarethQuestSites.All)
                if (s.Id == id) return new Vector2(s.X, s.Z);
            Debug.LogWarning("[Ashveil] Glockenrast: Quest-Ort fehlt – " + id);
            return Vector2.zero;
        }

        private static Vector2 HubXZ => new Vector2(KarethQuestSites.GlockenrastCenter.x, KarethQuestSites.GlockenrastCenter.z);
        private static Vector2 LumberXZ => new Vector2(KarethTerrainBuilder.LumberCamp.x, KarethTerrainBuilder.LumberCamp.z);
        private static readonly Vector2 JaskoSpot = new Vector2(-5f, 432f);

        // ================================================================== Aufbau

        private static void BuildGlockenrast(Transform env)
        {
            Loc.Reload();
            Transform root = K.Group("Glockenrast (P3 Aufträge)", env);
            BuildGlockenrastProps(K.Group("Requisiten", root));
            BuildGlockenrastAreas(K.Group("Erkundungsziele", root));
            BuildGlockenrastNodes(K.Group("Fundstellen und Geschichtsschritte", root));
            Transform npcs = K.Group("Figuren (Tagesablaeufe)", root);
            List<NpcSpec> list = GlockenrastNpcs();
            for (int i = 0; i < list.Count; i++) NpcFigure(npcs, list[i], 300 + i);
            BuildJaskoAfterHomecoming(npcs);
            Debug.Log($"[Ashveil] Glockenrast (P3): {list.Count} Figuren, 5 Erkundungsziele, {C.Nodes.Count} Fundstellen/Handlungen.");
        }

        // ------------------------------------------------------------------ Requisiten

        private static void BuildGlockenrastProps(Transform parent)
        {
            Vector2 c = HubXZ;
            // Vorzeichen (G10): kleines Glockenzeichen ueber Anselms Tuer – dasselbe Zeichen steht im Kassenbuch
            Transform door = K.Group("Glockenzeichen ueber Anselms Tuer", parent, Lage("gr.glockenzeichen", GroundV(c + new Vector2(-4.4f, 3.9f))), new Vector3(0f, LageYaw("gr.glockenzeichen", 70f), 0f));
            K.Block("Pfosten", door, Vector3.zero, new Vector3(0.12f, 2.3f, 0.12f), _woodDark);
            K.Box("Arm", door, new Vector3(0.3f, 2.2f, 0f), new Vector3(0.6f, 0.06f, 0.06f), _woodDark, default, false);
            K.Prim(PrimitiveType.Cylinder, "Glöckchen", door, new Vector3(0.55f, 2.02f, 0f), new Vector3(0.16f, 0.1f, 0.16f), Vector3.zero, _bronze, false);
            K.Box("Zeichenbrett", door, new Vector3(0f, 1.75f, 0.08f), new Vector3(0.3f, 0.3f, 0.03f), _wood, default, false);

            // Ende (G13): Runa haelt den Schluessel – am Glockengalgen haengt fuer diesen Spieler ein Schloss
            Transform gallows = K.Group("Schloss am Glockengalgen (nach G13)", parent, Lage("gr.schloss", GroundV(c + new Vector2(4f, -3f))), new Vector3(0f, LageYaw("gr.schloss", 15f), 0f));
            GameObject chain = K.Box("Kette", gallows, new Vector3(0f, 2.05f, 0f), new Vector3(0.05f, 0.5f, 0.05f), _metal, default, false);
            GameObject padlock = K.Box("Vorhaengeschloss", gallows, new Vector3(0f, 1.75f, 0f), new Vector3(0.18f, 0.2f, 0.08f), _metal, default, false);
            gallows.gameObject.AddComponent<StoryFlagProp>().Configure(C.FlagRunaHoldsKey, false, chain, padlock);
            K.MakeDynamic(gallows.gameObject);

            // Holzfaellerlager: Stapel Ersatzraeder (das Ersatzrad selbst ist eine Fundstelle)
            Vector2 lc = LumberXZ;
            Transform wheels = K.Group("Stellmacherecke", parent, Lage("gr.stellmacherecke", GroundV(lc + new Vector2(13.5f, 11.5f))), new Vector3(0f, LageYaw("gr.stellmacherecke", 30f), 0f));
            K.Block("Kiste", wheels, new Vector3(0.8f, 0f, 0.6f), new Vector3(0.9f, 0.7f, 0.8f), _wood, 12f);
            for (int i = 0; i < 2; i++)
                K.Prim(PrimitiveType.Cylinder, "Kleines Rad", wheels, new Vector3(-0.8f, 0.05f + i * 0.08f, 0.4f), new Vector3(0.9f, 0.04f, 0.9f), new Vector3(0f, i * 30f, 0f), _woodDark, false);

            // Harzerlager: umgestuerzter Stuhl, Harztoepfe unbenutzt – Seile und Schlitten fehlen (leerer Haken)
            Vector2 rc = SiteXZ("site.kareth.harzerlager");
            Transform hooks = K.Group("Leerer Seilhaken", parent, Lage("gr.seilhaken", GroundV(rc + new Vector2(-3.4f, 1.8f))), new Vector3(0f, LageYaw("gr.seilhaken", 200f), 0f));
            K.Block("Pfahl", hooks, Vector3.zero, new Vector3(0.14f, 1.8f, 0.14f), _woodDark);
            K.Box("Haken", hooks, new Vector3(0.12f, 1.6f, 0f), new Vector3(0.2f, 0.04f, 0.04f), _metal, default, false);
            K.Box("Schlittenspur", hooks, new Vector3(0f, 0.02f, 2.5f), new Vector3(0.9f, 0.02f, 4f), _dirt, default, false);

            // Versunkene Kuppeln: drei halb versunkene Dachkuppeln von Glockenstuehlen
            Vector2 kc = SiteXZ("site.kareth.kuppeln");
            Vector2[] domes = { new Vector2(-6f, 4f), new Vector2(5f, -3f), new Vector2(2f, 9f) };
            for (int i = 0; i < domes.Length; i++)
            {
                Transform d = K.Group("Kuppel " + (i + 1), parent, Lage("gr.kuppel" + i, GroundV(kc + domes[i])), new Vector3(0f, LageYaw("gr.kuppel" + i, i * 50f), 0f));
                float r = 3.2f - i * 0.5f;
                K.Prim(PrimitiveType.Sphere, "Kuppeldach", d, new Vector3(0f, -r * 0.35f, 0f), new Vector3(r * 2f, r * 1.3f, r * 2f), Vector3.zero, _stoneDark);
                K.Prim(PrimitiveType.Cylinder, "Laterne", d, new Vector3(0f, r * 0.35f, 0f), new Vector3(0.5f, 0.35f, 0.5f), Vector3.zero, _stone, false);
            }

            // Versunkener Glockenturm: nur die Spitze schaut aus dem Moor, mit Inschrift des Ordens
            Vector2 tc = SiteXZ("site.kareth.glockenturm");
            Transform tower = K.Group("Turmspitze", parent, Lage("gr.turmspitze", GroundV(tc)), new Vector3(0f, LageYaw("gr.turmspitze", 22f), 0f));
            for (int i = 0; i < 8; i++)
            {
                if (i == 3) continue; // Schallfenster, durch das man hineinsieht
                float a = i * Mathf.PI / 4f;
                K.Block("Mauer", tower, new Vector3(Mathf.Sin(a) * 2.2f, -0.6f, Mathf.Cos(a) * 2.2f), new Vector3(1.7f, 3.6f, 0.5f), _stone, a * Mathf.Rad2Deg);
            }
            K.Prim(PrimitiveType.Cylinder, "Turmhelm (schief)", tower, new Vector3(0.3f, 3.5f, 0f), new Vector3(4.2f, 0.9f, 4.2f), new Vector3(0f, 0f, 9f), _stoneDark);
            K.Box("Inschrift", tower, new Vector3(0f, 1.4f, 2.48f), new Vector3(1.4f, 0.5f, 0.05f), _stoneDark, default, false);
            K.Label(tower, new Vector3(0f, 2.1f, 2.6f), "Inschrift", Loc.T("story.glockenrast.inscription"), WorldLabelStyle.Place, 12f, 0.7f);

            // Schaedeldecke: drei Zeichensteine der ersten Siedler (die Abriebe sind Fundstellen daneben)
            Vector2 sc = SiteXZ("site.kareth.schaedeldecke");
            for (int i = 0; i < C.RubbingCount; i++)
            {
                Vector2 p = sc + RubbingOffset(i);
                Transform st = K.Group("Zeichenstein " + (i + 1), parent, Lage("gr.zeichenstein" + i, GroundV(p)), new Vector3(0f, LageYaw("gr.zeichenstein" + i, i * 120f + 180f), 0f));
                K.Block("Stein", st, new Vector3(0f, -0.3f, 0f), new Vector3(1.3f, 2.0f, 0.5f), _stone, 0f);
                K.Box("Zeichen", st, new Vector3(0f, 1.1f, 0.26f), new Vector3(0.6f, 0.6f, 0.02f), _stoneDark, default, false);
            }

            // Nordmoor: Jaskos Stein, daneben ein Stueck Glockenbronze und ein abgerissenes Seil
            Transform js = K.Group("Jaskos Stein", parent, Lage("gr.jaskostein", GroundV(JaskoSpot + new Vector2(0f, -0.8f))), new Vector3(0f, LageYaw("gr.jaskostein", 160f), 0f));
            K.Block("Stein", js, new Vector3(0f, -0.2f, 0f), new Vector3(1.6f, 1.2f, 1f), _stoneDark, 0f);
            K.Prim(PrimitiveType.Sphere, "Bronzesplitter", js, new Vector3(1.3f, 0.12f, 0.9f), new Vector3(0.7f, 0.25f, 0.5f), new Vector3(0f, 30f, 12f), _bronze, false);
            K.Box("Seilrest", js, new Vector3(-1.1f, 0.03f, 1.2f), new Vector3(0.06f, 0.06f, 1.8f), _straw, new Vector3(0f, 40f, 0f), false);
        }

        private static Vector2 RubbingOffset(int i)
        {
            float a = (i * 120f + 20f) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 9f;
        }

        private static Vector2 FragmentOffset(int i)
        {
            Vector2[] o = { new Vector2(-3f, 1f), new Vector2(7.5f, -0.5f), new Vector2(4f, 11.5f), new Vector2(-9f, 5.5f), new Vector2(0.5f, -7f) };
            return o[i % o.Length];
        }

        // ------------------------------------------------------------------ Erkundungsziele

        private static void BuildGlockenrastAreas(Transform parent)
        {
            GrArea(parent, C.AreaLumberCamp, LumberXZ, 14f);
            GrArea(parent, C.AreaResinCamp, SiteXZ("site.kareth.harzerlager"), 10f);
            GrArea(parent, C.AreaSkullcap, SiteXZ("site.kareth.schaedeldecke"), 16f);
            GrArea(parent, C.AreaBellTower, SiteXZ("site.kareth.glockenturm"), 10f);
            GrArea(parent, C.AreaNorthMoor, SiteXZ("site.kareth.nordmoor"), 18f);
        }

        private static void GrArea(Transform parent, string id, Vector2 xz, float radius)
        {
            var go = new GameObject("Erkundung " + id);
            go.transform.SetParent(parent, false);
            go.transform.position = GroundV(xz);
            QuestArea area = go.AddComponent<QuestArea>();
            area.Configure(id, radius, 20f);
            K.SetFloat(area, "above", 40f); // Gelaende ist hier uneben – Hoehe zaehlt nicht
        }

        // ------------------------------------------------------------------ Fundstellen und Geschichtsschritte

        private static void BuildGlockenrastNodes(Transform parent)
        {
            Material paper = K.Mat("Paper_Old", new Color(0.82f, 0.77f, 0.64f));
            Material leather = K.Mat("Leather_Book", new Color(0.36f, 0.24f, 0.15f));

            // G4 Ersatzrad (Holzfaellerlager) – zu gross, wie sich zeigen wird
            GrNode(parent, C.NodeWheel, LumberXZ + new Vector2(12f, 10f), 30f, null, v =>
            {
                K.Prim(PrimitiveType.Cylinder, "Ersatzrad", v, new Vector3(0f, 0.85f, 0f), new Vector3(1.7f, 0.06f, 1.7f), new Vector3(0f, 0f, 78f), _woodDark, false);
                K.Prim(PrimitiveType.Cylinder, "Nabe", v, new Vector3(0f, 0.85f, 0f), new Vector3(0.3f, 0.12f, 0.3f), new Vector3(0f, 0f, 78f), _metal, false);
            });

            // G6 Brief (auf dem Stumpf) und Kassenbuch (unter dem eingestuerzten Unterstand)
            Vector2 rc = SiteXZ("site.kareth.harzerlager");
            GrNode(parent, C.NodeLetter, rc + new Vector2(2.5f, -1.5f), 10f, null,
                v => K.Box("Brief", v, new Vector3(0f, 0.73f, 0f), new Vector3(0.24f, 0.02f, 0.17f), paper, new Vector3(0f, 25f, 0f), false));
            GrNode(parent, C.NodeLedger, rc + new Vector2(-1.8f, 1.2f), 60f, null,
                v => K.Box("Kassenbuch", v, new Vector3(0f, 0.04f, 0f), new Vector3(0.32f, 0.07f, 0.24f), leather, new Vector3(0f, 60f, 4f), false));

            // G7 Abriebe an den Zeichensteinen (der Stein bleibt, die Kohle-Markierung zeigt, wo schon abgerieben wurde)
            Vector2 sc = SiteXZ("site.kareth.schaedeldecke");
            for (int i = 0; i < C.RubbingCount; i++)
            {
                Vector2 dir = RubbingOffset(i).normalized;
                GrNode(parent, C.NodeRubbing(i), sc + RubbingOffset(i) - dir * 1.2f, i * 120f, null,
                    v => K.Box("Papier am Stein", v, new Vector3(0f, 0.05f, 0f), new Vector3(0.35f, 0.02f, 0.25f), paper, new Vector3(0f, 15f, 0f), false));
            }

            // G8 Glockenscherben bei den Kuppeln
            Vector2 kc = SiteXZ("site.kareth.kuppeln");
            for (int i = 0; i < C.FragmentCount; i++)
                GrNode(parent, C.NodeFragment(i), kc + FragmentOffset(i), i * 67f, null,
                    v => K.Prim(PrimitiveType.Sphere, "Scherbe", v, new Vector3(0f, 0.08f, 0f), new Vector3(0.45f, 0.14f, 0.3f), new Vector3(8f, 0f, 18f), _bronze, false));

            // G11 Scherbe an der Turmspitze anschlagen (bleibt liegen – der Boden antwortet)
            Vector2 tc = SiteXZ("site.kareth.glockenturm");
            GrNode(parent, C.NodeStrike, tc + new Vector2(1.2f, 3.4f), 0f, p =>
            {
                K.Block("Steinplatte", p, new Vector3(0f, -0.1f, 0f), new Vector3(1.2f, 0.5f, 0.9f), _stoneDark, 0f);
                K.Prim(PrimitiveType.Sphere, "Bronzescherbe", p, new Vector3(0f, 0.48f, 0f), new Vector3(0.6f, 0.16f, 0.4f), new Vector3(0f, 20f, 6f), _bronze, false);
            }, null);

            // G12 Jasko finden (Handlung neben der Figur – die Figur selbst bleibt NPC fuer Abgabe und G13)
            GrNode(parent, C.NodeJasko, JaskoSpot + new Vector2(1.3f, 1.3f), 0f, null, null);
        }

        /// <summary>Anker + Klickflaeche + Bedingung. permanent = bleibt immer, visual = verschwindet fuer den Spieler, der es genommen hat.</summary>
        private static void GrNode(Transform parent, string action, Vector2 xz, float yaw, System.Action<Transform> permanent, System.Action<Transform> visual)
        {
            if (!C.TryNode(action, out C.Node n))
            {
                Debug.LogWarning("[Ashveil] Glockenrast: unbekannte Fundstelle " + action);
                return;
            }
            Transform t = K.Group("Fundstelle " + action, parent, Lage("fund:" + action, GroundV(xz)), new Vector3(0f, LageYaw("fund:" + action, yaw), 0f));
            t.gameObject.AddComponent<StoryAnchor>().Configure(action, 2f);
            permanent?.Invoke(t);
            GameObject vis = null;
            if (visual != null)
            {
                Transform v = K.Group("Darstellung", t);
                visual(v);
                vis = v.gameObject;
            }
            StoryInteractable.Add(t.gameObject, action, n.LabelKey, n.Range, new Vector3(1.6f, 1.6f, 1.6f), new Vector3(0f, 0.7f, 0f));
            bool item = !string.IsNullOrEmpty(n.Item);
            t.gameObject.AddComponent<QuestNodeCondition>().Configure(n.Quest, n.Story ?? "", item ? C.TakenFlag(action) : "", item ? vis : null);
            K.MakeDynamic(t.gameObject);
        }

        // ------------------------------------------------------------------ Figuren

        private static List<NpcSpec> GlockenrastNpcs()
        {
            Vector2 c = HubXZ;
            Vector2 gallows = c + new Vector2(4f, -3f);
            var anselmFire = Facing(c.x - 1.5f, c.y + 0.9f, c);
            var anselmPost = Facing(c.x + 2.6f, c.y - 1.2f, c);
            var anselmPray = Facing(gallows.x - 0.2f, gallows.y + 1.4f, gallows);
            var runaFire = Facing(c.x + 1.4f, c.y - 1.1f, c);
            var runaLookout = Facing(c.x + 1.8f, c.y + 6f, new Vector2(c.x + 40f, c.y + 160f)); // Blick zum Moor
            Vector2 lc = LumberXZ;
            Vector2 toHub = (c - lc).normalized;
            Vector2 merle = lc + toHub * 30f;
            Vector2 merleFireXZ = merle + new Vector2(-2f, -2.5f);
            var merlePost = Facing(merle.x, merle.y, lc);
            var merleFire = Facing(merleFireXZ.x + 1.1f, merleFireXZ.y + 0.6f, merleFireXZ);
            var edoPray = Spot(Shrine.x - 2.4f, Shrine.y + 2.2f, 90f);
            var edoSeat = Facing(PilgrimRest.x, PilgrimRest.y - 1.6f, PilgrimRest);
            var jasko = Facing(JaskoSpot.x, JaskoSpot.y, JaskoSpot + new Vector2(6f, -6f));

            return new List<NpcSpec>
            {
                new NpcSpec
                {
                    Name = "Bruder Anselm", Title = "Stille Glocke", NpcId = C.Anselm, Quest = true,
                    Look = "1|b0|s1|h0|c5|d4|a5|e3|m0", Gear = new[] { "travel_mantle", "weaver_robe", "weaver_sandals", "rope_belt" },
                    Steps = new[]
                    {
                        At(0f, anselmFire, "Sit", note: "wacht am Feuer der Glockenrast"),
                        At(6f, anselmPray, "Kneel", note: "Morgengebet am Glockengalgen"),
                        At(7f, anselmPost, "Talk|FoldArms|Stand", note: "empfaengt Pilger an der Glockenrast"),
                        At(12.5f, anselmFire, "SitTalk|Sit", note: "Mittag am Feuer"),
                        At(13.25f, anselmPost, "Talk|Stand", note: "empfaengt Pilger an der Glockenrast"),
                        // TODO(P3): nach G13 betet er laut – der Tagesablauf ist fuer alle Spieler gleich; das laute Gebet steht
                        // deshalb im Gruss je Geschichtsmarke (npc.glockenrast.anselm.greeting.story.glockenrast.heimgekehrt).
                        At(19f, anselmPray, "Kneel", note: "Abendgebet am Glockengalgen"),
                        At(20f, anselmFire, "Sit", note: "wacht am Feuer der Glockenrast")
                    }
                },
                new NpcSpec
                {
                    Name = "Runa Fahl", Title = "Moorwächterin", NpcId = C.Runa, Quest = true, Female = true,
                    Look = "1|b1|s2|h3|c1|a2|e1|m1", Gear = new[] { "ashen_leather_jerkin", "hunter_breeches", "stalker_boots", "stalker_gloves", "wolfhide_cloak" },
                    Steps = new[]
                    {
                        At(0f, runaFire, "Sit", note: "wacht am Feuer der Glockenrast"),
                        At(6.5f, runaLookout, "FoldArms|Stand|Talk", note: "haelt Ausschau Richtung Moor"),
                        At(12f, runaFire, "SitTalk|Sit", note: "Mittag am Feuer"),
                        At(13f, runaLookout, "FoldArms|Stand", note: "haelt Ausschau Richtung Moor"),
                        At(20.5f, runaFire, "Sit", note: "wacht am Feuer der Glockenrast")
                    }
                },
                new NpcSpec
                {
                    Name = "Merle Harzbrenner", Title = "Holzfällerin", NpcId = C.Merle, Quest = true, Female = true,
                    Look = "1|b1|s1|h4|c3|a1|e2|m2", Gear = new[] { "frayed_gambeson", "travel_trousers", "stalker_boots", "stalker_gloves", "rope_belt" },
                    Steps = new[]
                    {
                        At(0f, merleFire, "Sit", note: "sitzt wach an ihrem kleinen Feuer"),
                        At(6f, merlePost, "FoldArms|Stand|Talk", note: "wartet am Rand ihres Lagers"),
                        At(12f, merleFire, "Sit", note: "isst am Feuer, den Blick zum Lager"),
                        At(13f, merlePost, "FoldArms|Stand", note: "wartet am Rand ihres Lagers"),
                        At(21f, merleFire, "Sit", note: "sitzt wach an ihrem kleinen Feuer")
                    }
                },
                new NpcSpec
                {
                    Name = "Pilger Edo", Title = "Wegschrein", NpcId = C.Pilgrim, Quest = true,
                    Look = "1|b0|s0|h1|c5|d5|a5|e4|m0", Gear = new[] { "travel_mantle", "frayed_gambeson", "travel_trousers", "foot_wraps", "travel_hood" },
                    Steps = new[]
                    {
                        At(0f, edoSeat, "Sit", note: "sitzt am Feuer der Pilgerrast"),
                        At(7f, edoPray, "Kneel|Stand", note: "betet am Wegschrein und zeichnet"),
                        At(18f, edoSeat, "Sit|SitTalk", note: "sitzt am Feuer der Pilgerrast")
                    }
                },
                new NpcSpec
                {
                    Name = "Jasko Harzbrenner", Title = "Holzfäller", NpcId = C.Jasko, Quest = true,
                    Look = "1|b0|s3|h6|c2|d2|a3|e1|m1", Gear = new[] { "ashen_leather_jerkin", "travel_trousers", "foot_wraps" },
                    Steps = new[] { At(0f, jasko, "Sit", note: "sitzt verletzt an einem Stein im Nordmoor") }
                }
            };
        }

        /// <summary>
        /// Ende (G13): Jasko arbeitet am anderen Ende des Holzfaellerlagers als Merle – nur fuer Spieler mit der Marke
        /// (Phasing, reine Darstellung ohne NpcIdentity). Seine Figur im Nordmoor bleibt fuer den Server bestehen (Abgabe G12,
        /// Geber G13 fuer andere Spieler); fuer Heimgekehrte werden dort nur Koerper und Namensschild ausgeblendet.
        /// </summary>
        private static void BuildJaskoAfterHomecoming(Transform npcs)
        {
            Vector2 lc = LumberXZ;
            Vector2 toHub = (HubXZ - lc).normalized;
            Vector2 far = lc - toHub * 26f; // Merle steht auf der Seite zur Glockenrast, Jasko am anderen Ende
            Vector2 chop = far + new Vector2(1.2f, 0f);
            var work = Facing(far.x, far.y, chop);
            var rest = Facing(far.x - 1.5f, far.y - 1.5f, lc);
            Transform group = K.Group("Jasko im Holzfaellerlager (nach G13)", npcs);
            NpcFigure(group, new NpcSpec
            {
                Name = "Jasko Harzbrenner", Title = "Holzfäller",
                Look = "1|b0|s3|h6|c2|d2|a3|e1|m1", Gear = new[] { "ashen_leather_jerkin", "travel_trousers", "foot_wraps" },
                Steps = new[]
                {
                    At(0f, rest, "Sit", note: "sitzt am anderen Ende des Lagers"),
                    At(6.5f, work, "Chop|Chop|Stand", note: "hackt Holz am anderen Ende des Lagers"),
                    At(12f, rest, "Sit", note: "isst allein"),
                    At(13f, work, "Chop|Stand", note: "hackt Holz am anderen Ende des Lagers"),
                    At(20f, rest, "Sit", note: "sitzt am anderen Ende des Lagers")
                }
            }, 320);
            JaskoPhasing(npcs, group);
        }

        /// <summary>
        /// Phasing des Endes (G13): die Figur in 'group' sieht nur, wer die Marke hat; der Nordmoor-Jasko unter 'npcs' wird fuer
        /// Heimgekehrte ausgeblendet. Auch fuer Kareth v4 (#27, eigene Tagesablaeufe).
        /// </summary>
        private static void JaskoPhasing(Transform npcs, Transform group)
        {
            Transform figure = group.childCount > 0 ? group.GetChild(0) : null;
            if (figure != null) group.gameObject.AddComponent<StoryFlagProp>().Configure(C.FlagSeparateEnds, false, figure.gameObject);

            // Nordmoor-Jasko: fuer Heimgekehrte Koerper und Namensschild aus (NpcIdentity/Anker bleiben – geteilte Welt)
            foreach (NpcIdentity id in npcs.GetComponentsInChildren<NpcIdentity>(true))
            {
                if (id.NpcId != C.Jasko) continue;
                var targets = new List<GameObject>();
                Transform visual = id.transform.Find("Visual");
                if (visual != null) targets.Add(visual.gameObject);
                foreach (WorldLabel l in id.GetComponentsInChildren<WorldLabel>(true)) targets.Add(l.gameObject);
                var hider = new GameObject("Phasing (nach G13 ausgeblendet)");
                hider.transform.SetParent(npcs, false);
                hider.AddComponent<StoryFlagProp>().Configure(C.FlagHomecoming, true, targets.ToArray());
            }
        }
    }
}
