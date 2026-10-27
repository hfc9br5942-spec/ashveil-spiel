using System;
using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Quests;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;
using Q = Ashveil.EditorTools.KarethQuestSites;
using V4 = Ashveil.EditorTools.KarethV4Inhalte;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4, Welle 0 (#27): die Inhalte des gebauten Kareth an ihren v4-Lagen (Daten: <see cref="KarethV4Inhalte"/>).
    ///   · Figuren: gleiche Körper, Ausrüstung, Kennungen und Saat wie in der Sandbox (aus deren Listen), neue Tagesabläufe.
    ///     Dienst- und Auftragsfiguren schlafen nicht (WORLD-096): sie wachen am Wachtfeuer bzw. dösen sitzend.
    ///   · Lagertiere, Auftragsorte (eigene v4-Tabelle, die Sandbox-Tabelle formt das Sandbox-Gelände), Erkundungsziele und die
    ///     13 Fundstellen der Glockenrast-Kette.
    ///   · Requisiten: die Sandbox-Bauer werden mitbenutzt, ihre Lagen lenkt <see cref="_v4Lage"/> um (null = Sandbox, unverändert);
    ///     neu sind nur Kartentisch und Stuhl vor der Halle, Kochstelle, Aussichtsfels am Muldenrand-Ausblick (Q3), das Uhrwerk-Tor und die Aschenweide.
    /// Höhe: Gelände der v4-Szene (Terrain.SampleHeight) bzw. Fußboden aus dem Plan.
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        // ================================================================== Lage-Umlenkung (nur während des v4-Baus gesetzt)

        /// <summary>Kareth v4 (#27): Lage und Blickrichtung je Schlüssel; null = Sandbox-Lage (dann unverändert wie bisher).</summary>
        private static Func<string, (Vector3 pos, float yaw)?> _v4Lage;

        private static Vector3 Lage(string key, Vector3 sandbox)
        {
            if (_v4Lage == null) return sandbox;
            (Vector3 pos, float yaw)? l = _v4Lage(key);
            return l.HasValue ? l.Value.pos : sandbox;
        }

        private static float LageYaw(string key, float sandbox)
        {
            if (_v4Lage == null) return sandbox;
            (Vector3 pos, float yaw)? l = _v4Lage(key);
            return l.HasValue ? l.Value.yaw : sandbox;
        }

        // ================================================================== Bau

        private const string NachG13 = " (nach G13)";
        private static Terrain _v4Terrain;

        private static float V4Y(float x, float z) =>
            _v4Terrain != null ? _v4Terrain.SampleHeight(new Vector3(x, 0f, z)) + _v4Terrain.transform.position.y : 0f;

        private static Vector3 V4Pos(float x, float z, string hoehe, float y) =>
            new Vector3(x, hoehe == "boden" ? y : V4Y(x, z) + y, z);

        /// <summary>Name der Figur in der Szene (das Ende nach G13 heißt wie Jasko, steht aber in einer eigenen Gruppe).</summary>
        internal static string V4FigurName(string name) => name.EndsWith(NachG13) ? name.Substring(0, name.Length - NachG13.Length) : name;

        /// <summary>Körper, Titel und Kennung der Sandbox-Figuren (Name → Spec). Nur lesen – baut nichts.</summary>
        private static Dictionary<string, NpcSpec> SandboxFiguren()
        {
            var all = new List<NpcSpec>();
            all.AddRange(CampNpcs());
            all.AddRange(SceneNpcs());
            all.AddRange(RoadNpcs());
            all.AddRange(GlockenrastNpcs());
            all.AddRange(KarethNpcs());   // #28: Figuren nur für Kareth v4 (Ebba Salweg)
            var map = new Dictionary<string, NpcSpec>();
            foreach (NpcSpec s in all) if (!map.ContainsKey(s.Name)) map[s.Name] = s;
            return map;
        }

        internal static List<string> V4FigurKennungen(V4.Daten d)
        {
            var list = new List<string>();
            if (d?.figuren == null) return list;
            Dictionary<string, NpcSpec> sb = SandboxFiguren();
            foreach (V4.Figur f in d.figuren)
                if (!f.name.EndsWith(NachG13) && sb.TryGetValue(f.name, out NpcSpec s) && !string.IsNullOrEmpty(s.NpcId)) list.Add(s.NpcId);
            return list;
        }

        internal static string BuildKarethV4Inhalte(Transform parent, V4.Daten d, Terrain terrain)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _v4Terrain = terrain;
            CreateMaterials();
            Q.EnsureMaterials();
            Loc.Reload();

            var plaetze = new Dictionary<string, V4.Platz>();
            foreach (V4.Platz p in d.plaetze) plaetze[p.id] = p;
            var lagen = new Dictionary<string, (Vector3 pos, float yaw)>();
            foreach (V4.Requisit r in d.requisiten)
            {
                string key = r.art.StartsWith("gr.") ? r.art : "req:" + r.art;
                if (!lagen.ContainsKey(key)) lagen[key] = (V4Pos(r.x, r.z, r.hoehe, r.y), r.yaw);
            }
            foreach (V4.Fund f in d.fundstellen) lagen["fund:" + f.aktion] = (V4Pos(f.x, f.z, "gelaende", 0f), f.yaw);
            var fehlt = new SortedSet<string>();
            _v4Lage = key =>
            {
                if (lagen.TryGetValue(key, out (Vector3 pos, float yaw) l)) return l;
                fehlt.Add(key);
                return null;
            };

            Transform root = K.Group(V4.RootName, parent);
            int figuren = 0, tiere = 0, orte = 0, requisiten = 0;
            try
            {
                // ---------------------------------------------------------- Requisiten (vor dem NavMesh – sie sind Hindernisse)
                Transform lager = K.Group("Lager: Szenen und Requisiten", root);
                Transform unterwegs = K.Group("Unterwegs: Wegschrein, Wagen, Fuhrwerk", root);
                Transform glocken = K.Group("Glockenrast und Glockenrast-Kette", root);
                Transform landmarken = K.Group("Landmarken: Aschenweide, Uhrwerk, Ausblick", root);
                NpcCart handkarren = null;
                Transform k28 = null;
                Vector3? lookoutTop = null;
                foreach (V4.Requisit r in d.requisiten)
                {
                    Vector3 pos = V4Pos(r.x, r.z, r.hoehe, r.y);
                    requisiten++;
                    switch (r.art)
                    {
                        case "lazarett": BuildInfirmary(K.Group("Lazarett", lager)); break;
                        case "pult": BuildScribeDesk(K.Group("Schreibpult", lager)); break;
                        case "handkarren": handkarren = BuildHandcart(lager); break;
                        case "kartentisch": V4MapTable(lager, r.name, pos, r.yaw); break;
                        case "stuhl": V4Chair(lager, r.name, pos, r.yaw); break;
                        case "kochstelle": V4CookingFire(lager, r.name, pos); break;
                        case "wegschrein": BuildShrine(K.Group("Wegschrein und Pilgerrast", unterwegs)); break;   // baut beide (Lagen umgelenkt)
                        case "pilgerrast": break;
                        case "feuerstelle": V4SmallFire(unterwegs, r.name, pos); break;
                        case "wagen":
                        {
                            Transform w = BrokenWagonBody(K.Group("Tamos Wagen", unterwegs), pos, r.yaw);
                            K.Label(w, new Vector3(0f, 2.6f, 0f), "Liegengebliebener Wagen", "Kiefernweg", WorldLabelStyle.Place, 40f, 0.75f);
                            break;
                        }
                        case "salzfuhrwerk": BuildSaltWagon(unterwegs); break;
                        case "aussicht": lookoutTop = V4Lookout(landmarken, r.name, pos); break;
                        case "uhrwerk": V4Clockwork(landmarken, r.name, pos, r.yaw); break;
                        case "aschenweide": V4AshWillow(landmarken, r.name, r.x, r.z, r.yaw); break;
                        case "glockenrast.feuer": Q.Campfire(K.Group(r.name, glocken, pos, Vector3.zero), Vector3.zero); break;
                        case "glockenrast.zelt": Q.Tent(K.Group(r.name, glocken, pos, Vector3.zero), new Vector3(0f, -0.15f, 0f), r.yaw, Q.ClothGrey); break;
                        case "glockenrast.bank": Q.Bench(K.Group(r.name, glocken, pos, Vector3.zero), new Vector3(0f, -0.2f, 0f), r.yaw); break;
                        case "glockenrast.kisten": Q.Crates(K.Group(r.name, glocken, pos, Vector3.zero), new Vector3(0f, -0.2f, 0f), 2); break;
                        case "glockenrast.galgen": Q.BellGallows(K.Group(r.name, glocken, pos, Vector3.zero), new Vector3(0f, -0.3f, 0f), r.yaw); break;
                        default:
                            // #28 Ergänzungsaufträge: eigene Greybox-Requisiten (SandboxSceneBuilder.Kareth28)
                            if (r.art.StartsWith("k28.") && BuildKareth28Prop(k28 ??= K.Group("Ergänzungsaufträge (#28)", root), r, pos)) break;
                            requisiten--;
                            if (!r.art.StartsWith("gr.")) Debug.LogWarning("[Ashveil] Kareth v4: unbekanntes Requisit " + r.art);
                            break;
                    }
                }
                // Glockenrast-Kette: Requisiten (12) und Fundstellen (13) mit den Sandbox-Bauern an den v4-Lagen
                BuildGlockenrastProps(K.Group("Requisiten (Glockenrast-Kette)", glocken));
                BuildGlockenrastNodes(K.Group("Fundstellen und Geschichtsschritte", glocken));

                // ---------------------------------------------------------- Auftragsorte (eigene v4-Tabelle)
                Transform ortRoot = K.Group(Q.RootName, root);
                foreach (V4.Ort o in d.orte)
                {
                    Vector3 p = V4Pos(o.x, o.z, "gelaende", 0f);
                    var kind = (QuestSiteKind)Enum.Parse(typeof(QuestSiteKind), o.art);
                    var status = (QuestSiteStatus)Enum.Parse(typeof(QuestSiteStatus), o.status);
                    Transform t = K.Group(o.name, ortRoot, p, Vector3.zero);
                    t.gameObject.AddComponent<WorldQuestSite>().Configure(o.id, o.name, o.zone, o.hub, kind, status, o.min, o.max, o.r, o.erreichbar, o.notiz);
                    if (status != QuestSiteStatus.Planned)
                    {
                        // Schmuck an der Marke oder versetzt (Ödis Hütte: die Marke steht vor der Tür, nicht im festen Block)
                        Transform at = o.sx != 0f || o.sz != 0f
                            ? K.Group("Schmuck", t, V4Pos(o.x + o.sx, o.z + o.sz, "gelaende", 0f) - p, Vector3.zero)
                            : t;
                        switch (o.schmuck)
                        {
                            case "harzerlager": Q.DressAt(at, Q.Dressing.Harzerlager); break;
                            case "oedi": Q.DressAt(at, Q.Dressing.OediHuette); break;
                            case "speiernest": Q.DressAt(at, Q.Dressing.Speiernest); break;
                            case "aschenwacht": Q.DressAt(at, Q.Dressing.HubOstwacht); break;
                        }
                    }
                    if (!string.IsNullOrEmpty(o.schild))
                        K.Label(t, new Vector3(0f, kind == QuestSiteKind.Hub ? 5.5f : 4f, 0f), o.schild, $"Stufe {o.min}–{o.max}", WorldLabelStyle.Place,
                            kind == QuestSiteKind.Hub ? 80f : 50f);
                    orte++;
                }

                // ---------------------------------------------------------- Erkundungsziele (QuestArea)
                Transform areas = K.Group("Erkundungsziele", root);
                foreach (V4.Bereich b in d.bereiche)
                {
                    var go = new GameObject("Erkundung " + b.id);
                    go.transform.SetParent(areas, false);
                    // Q3: Ziel ist die Oberseite des Aussichtsfelsens (Springen wie in der Sandbox), sonst Gelände
                    go.transform.position = b.id == QuestContentBuilder.LookoutArea && lookoutTop.HasValue ? lookoutTop.Value : V4Pos(b.x, b.z, "gelaende", 0f);
                    QuestArea area = go.AddComponent<QuestArea>();
                    area.Configure(b.id, b.r, b.unten);
                    K.SetFloat(area, "above", b.oben);
                }

                // ---------------------------------------------------------- Figuren
                Dictionary<string, NpcSpec> sandbox = SandboxFiguren();
                Transform npcs = K.Group("Figuren (Tagesablaeufe)", root);
                Transform nachG13 = null;
                foreach (V4.Figur f in d.figuren)
                {
                    string name = V4FigurName(f.name);
                    if (!sandbox.TryGetValue(name, out NpcSpec spec))
                    {
                        Debug.LogError("[Ashveil] Kareth v4: Figur ohne Sandbox-Vorlage – " + f.name);
                        continue;
                    }
                    var steps = new RoutineStep[f.schritte.Length];
                    for (int i = 0; i < steps.Length; i++)
                    {
                        V4.Schritt s = f.schritte[i];
                        if (!plaetze.TryGetValue(s.platz, out V4.Platz pl))
                        {
                            fehlt.Add("platz:" + s.platz);
                            continue;
                        }
                        steps[i] = new RoutineStep
                        {
                            from = s.ab, pos = V4Pos(pl.x, pl.z, pl.hoehe, pl.y), yaw = pl.yaw, activity = s.tun, seat = s.sitz,
                            carry = s.traegt, cart = s.schiebt, ride = s.faehrt, note = string.IsNullOrEmpty(s.notiz) ? s.tun : s.notiz
                        };
                    }
                    spec.Steps = steps;
                    if (!string.IsNullOrEmpty(f.titel)) spec.Title = f.titel;
                    spec.Cart = f.karren == "handkarren" ? handkarren : null;
                    spec.Speed = f.tempo;
                    Transform at = npcs;
                    if (f.name.EndsWith(NachG13))
                    {
                        // Ende G13: Darstellung ohne Kennung (die Nordmoor-Figur bleibt Geber und Abgabe), nur mit der Marke sichtbar
                        spec.NpcId = null;
                        spec.Quest = false;
                        nachG13 = K.Group("Jasko im Holzfaellerlager (nach G13)", npcs);
                        at = nachG13;
                    }
                    NpcFigure(at, spec, f.saat);
                    figuren++;
                }
                if (nachG13 != null) JaskoPhasing(npcs, nachG13);

                // ---------------------------------------------------------- Tiere
                CampAnimalBodies(out AnimalBody cat, out AnimalBody dog, out AnimalBody goat, out AnimalBody mule);
                Transform animals = K.Group("Tiere", root);
                foreach (V4.Tier tier in d.tiere)
                {
                    AnimalBody body = tier.koerper == "katze" ? cat : tier.koerper == "hund" ? dog : tier.koerper == "ziege" ? goat : mule;
                    var idles = new AnimalPose[tier.posen.Length];
                    for (int i = 0; i < idles.Length; i++) idles[i] = (AnimalPose)Enum.Parse(typeof(AnimalPose), tier.posen[i]);
                    var steps = new AnimalStep[tier.schritte.Length];
                    for (int i = 0; i < steps.Length; i++)
                    {
                        V4.TierSchritt s = tier.schritte[i];
                        if (!plaetze.TryGetValue(s.platz, out V4.Platz pl))
                        {
                            fehlt.Add("platz:" + s.platz);
                            continue;
                        }
                        steps[i] = new AnimalStep
                        {
                            from = s.ab, center = V4Pos(pl.x, pl.z, pl.hoehe, pl.y), radius = s.radius, yaw = s.yaw,
                            pose = (AnimalPose)Enum.Parse(typeof(AnimalPose), s.pose), note = s.notiz
                        };
                    }
                    Animal(animals, tier.name, body, tier.tempo, tier.saat, idles, steps, tier.wedelt);
                    tiere++;
                }
            }
            finally
            {
                _v4Lage = null;
                _v4Terrain = null;
            }

            if (fehlt.Count > 0)
                Debug.LogError("[Ashveil] Kareth v4: Inhalte ohne v4-Lage (stehen falsch): " + string.Join(", ", fehlt));
            string msg = $"Inhalte {figuren} Figuren, {tiere} Tiere, {orte} Orte, {d.bereiche.Length} Erkundungsziele, {d.fundstellen.Length} Fundstellen, " +
                         $"{requisiten} Requisiten ({sw.Elapsed.TotalSeconds:0.0} s){(fehlt.Count > 0 ? $", {fehlt.Count} OHNE LAGE" : "")}";
            Debug.Log("[Ashveil] Kareth v4: " + msg);
            return msg;
        }

        // ================================================================== Neue Requisiten (Greybox, nur Kareth v4)

        /// <summary>Kartentisch vor der Halle: Bralt steht dahinter (Süden), Spieler kommen von Norden.</summary>
        private static void V4MapTable(Transform parent, string name, Vector3 pos, float yaw)
        {
            Transform t = K.Group(name, parent, pos, new Vector3(0f, yaw, 0f));
            Material paper = K.Mat("Paper_Old", new Color(0.82f, 0.77f, 0.64f));
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
                K.Block("Bein", t, new Vector3(ix * 0.7f, -0.1f, iz * 0.38f), new Vector3(0.1f, 0.96f, 0.1f), _woodDark);
            K.Box("Platte", t, new Vector3(0f, 0.9f, 0f), new Vector3(1.6f, 0.07f, 0.9f), _wood);
            K.Box("Karte", t, new Vector3(0.05f, 0.94f, 0f), new Vector3(1.1f, 0.01f, 0.7f), paper, new Vector3(0f, 6f, 0f), false);
            for (int i = 0; i < 3; i++)
                K.Prim(PrimitiveType.Sphere, "Beschwerstein", t, new Vector3(-0.45f + i * 0.45f, 0.97f, i == 1 ? 0.3f : -0.3f), new Vector3(0.1f, 0.06f, 0.1f),
                    Vector3.zero, _stoneDark, false);
            K.Prim(PrimitiveType.Cylinder, "Kerze", t, new Vector3(0.65f, 1.0f, 0.3f), new Vector3(0.06f, 0.06f, 0.06f), Vector3.zero, _straw, false);
        }

        private static void V4Chair(Transform parent, string name, Vector3 pos, float yaw)
        {
            Transform t = K.Group(name, parent, pos, new Vector3(0f, yaw, 0f));
            K.Block("Sitz", t, new Vector3(0f, -0.1f, 0f), new Vector3(0.62f, 0.6f, 0.58f), _woodDark);
            K.Box("Lehne", t, new Vector3(0f, 0.95f, -0.27f), new Vector3(0.62f, 0.8f, 0.07f), _woodDark);
            K.Box("Fell", t, new Vector3(0f, 0.52f, 0.02f), new Vector3(0.58f, 0.04f, 0.5f), _canvas, default, false);
        }

        /// <summary>Kochstelle der Küche im Freien: Steinring, Glut, Dreibein mit Kessel (Wenna kocht, Nessa holt heißes Wasser).</summary>
        private static void V4CookingFire(Transform parent, string name, Vector3 pos)
        {
            Transform t = K.Group(name, parent, pos, Vector3.zero);
            for (int i = 0; i < 8; i++)
                K.Box("Stein", t, OnRingAt(Vector3.zero, i * 45f, 0.62f, 0.08f), new Vector3(0.26f, 0.22f, 0.24f), _stoneDark, new Vector3(0f, i * 45f, 0f));
            K.Prim(PrimitiveType.Sphere, "Glut", t, new Vector3(0f, 0.08f, 0f), new Vector3(0.6f, 0.16f, 0.6f), Vector3.zero, _fire, false);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                K.Box("Dreibein", t, new Vector3(Mathf.Sin(a) * 0.45f, 0.75f, Mathf.Cos(a) * 0.45f), new Vector3(0.05f, 1.6f, 0.05f), _woodDark,
                    new Vector3(Mathf.Cos(a) * -16f, 0f, Mathf.Sin(a) * 16f), false);
            }
            K.Prim(PrimitiveType.Cylinder, "Kessel", t, new Vector3(0f, 0.62f, 0f), new Vector3(0.5f, 0.24f, 0.5f), Vector3.zero, _metal, false);
            K.PointLight("Kochfeuer", t, new Vector3(0f, 0.9f, 0f), new Color(1f, 0.55f, 0.22f), 7f, 1.4f, true);
            K.Particles("Dampf", t, new Vector3(0f, 0.9f, 0f), new Color(0.75f, 0.74f, 0.72f, 0.35f), 0.5f, 4f, 0.5f, 3f, 8f, 0.15f);
        }

        /// <summary>Kleines Feuer für Rastplätze (Edo, Merle, Oswald): Steinkranz, Glut, Licht, Flammen.</summary>
        private static void V4SmallFire(Transform parent, string name, Vector3 pos)
        {
            Transform fire = K.Group(name, parent, pos, Vector3.zero);
            for (int i = 0; i < 6; i++)
                K.Box("Stein", fire, OnRingAt(Vector3.zero, i * 60f, 0.5f, 0.1f), new Vector3(0.22f, 0.18f, 0.2f), _stoneDark, new Vector3(0f, i * 60f, 0f), false);
            K.Prim(PrimitiveType.Sphere, "Glut", fire, new Vector3(0f, 0.08f, 0f), new Vector3(0.45f, 0.14f, 0.45f), Vector3.zero, _fire, false);
            K.PointLight("Feuer", fire, new Vector3(0f, 0.8f, 0f), new Color(1f, 0.55f, 0.22f), 7f, 1.5f, true);
            K.Particles("Flammen", fire, new Vector3(0f, 0.1f, 0f), new Color(1f, 0.55f, 0.2f, 0.85f), 0.3f, 9f, 0.7f, 0.5f, 7f, 0.15f);
        }

        /// <summary>
        /// Aussichtsfels am Muldenrand-Ausblick (Q3 „Blick über die Senke“, Kareth-Pass Teil 10; Max 07.10.: „Kleiner Fels am
        /// Ausblick“ – Q3 übt wieder das Springen wie in der Sandbox). Der Hang ist dort ≈ 25° steil, deshalb liegt zuerst eine ebene
        /// Steinplatte als Absatz im Hang (oben 0,1 m über dem Boden ihrer Mitte, hangauf im Gelände, hangab bis 1 m tief im Boden);
        /// darauf zwei Stufen und der Fels, je 0,8 m höher (Sprunghöhe 1,48 m; eine Stufe überspringen geht nicht). Die Platte schaut
        /// hangab zur Mulde und liegt 4,5 m neben dem Planpunkt entlang der Höhenlinie: Die Marke des Auftragsorts bleibt davor auf
        /// dem Boden (zu Fuß erreichbar), die erste Stufe beginnt an ihr. Oben am hinteren Rand ein Steinmann (ohne Kollision).
        /// Gibt die Mitte der Felsoberseite zurück (dort sitzt das Erkundungsziel area.lookout).
        /// </summary>
        private static Vector3 V4Lookout(Transform parent, string name, Vector3 pos)
        {
            float gx = V4Y(pos.x + 3f, pos.z) - V4Y(pos.x - 3f, pos.z), gz = V4Y(pos.x, pos.z + 3f) - V4Y(pos.x, pos.z - 3f);
            float yaw = gx * gx + gz * gz > 1e-4f ? Mathf.Atan2(-gx, -gz) * Mathf.Rad2Deg : 0f;   // lokal +z = hangab
            Vector3 c = pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(4.5f, 0f, 0f);
            c.y = V4Y(c.x, c.z);
            Transform t = K.Group(name, parent, c, new Vector3(0f, yaw, 0f));
            K.Box("Steinplatte", t, new Vector3(0f, -1.1f, 0f), new Vector3(6f, 2.4f, 5f), _stone, new Vector3(0f, 6f, 0f));   // oben 0,1
            K.Block("Stufe 1", t, new Vector3(-2.2f, -0.2f, 0.3f), new Vector3(1.6f, 1.1f, 1.6f), _stone, 17f);                 // oben 0,9
            K.Block("Stufe 2", t, new Vector3(-0.6f, -0.2f, 0.4f), new Vector3(1.6f, 1.9f, 1.6f), _stone, 34f);                 // oben 1,7
            K.Block("Fels", t, new Vector3(1.4f, -0.2f, 0.3f), new Vector3(2.2f, 2.7f, 2.2f), _stone, 51f);                     // oben 2,5
            var top = new Vector3(1.4f, 2.5f, 0.3f);
            for (int i = 0; i < 4; i++)
                K.Box("Steinmann", t, top + new Vector3(0.6f, 0.14f + i * 0.28f, -0.5f), new Vector3(0.46f - i * 0.08f, 0.26f, 0.42f - i * 0.07f),
                    _stoneDark, new Vector3(0f, i * 37f, 0f), false);
            K.Label(t, top + Vector3.up * 2.6f, "Aussichtsfels", "Muldenrand", WorldLabelStyle.Place, 45f, 0.8f);
            return t.TransformPoint(top);
        }

        /// <summary>
        /// Die Aschenweide (Kareth-Pass §3, subregionale Landmarke, MOVE aus dem alten Kareth): riesiger toter Baum im Glockenmoor,
        /// 40 m hoch, gleiche Gestalt wie in der Sandbox (Stamm, sieben Äste aus derselben Saat). Neu: Die Wurzeln fallen vom Stamm
        /// zum Boden ab (in der Sandbox standen sie schräg nach oben), und der alte Schacht unter dem Baum entfällt – er gehörte zu
        /// Morghul, der seit dem 03.10. auf Sarvenn liegt (Kanon). Der Stamm sitzt so tief, dass er an der Hangseite der Bodenwelle
        /// nicht schwebt. G9: „An der Aschenweide sind Torfschrate“ – J5 Nord liegt 22 m ostnordöstlich.
        /// </summary>
        private static void V4AshWillow(Transform parent, string name, float x, float z, float yaw)
        {
            float low = V4Y(x, z);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                low = Mathf.Min(low, V4Y(x + Mathf.Sin(a) * 3.2f, z + Mathf.Cos(a) * 3.2f));
            }
            Transform t = K.Group(name, parent, new Vector3(x, low - 0.4f, z), new Vector3(0f, yaw, 0f));
            K.Prim(PrimitiveType.Cylinder, "Stamm", t, new Vector3(0f, 20f, 0f), new Vector3(6f, 20f, 6f), new Vector3(0f, 0f, 2f), _bark);
            var rng = new System.Random(5);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 51f + (float)rng.NextDouble() * 20f;
                float h = 26f + (float)rng.NextDouble() * 12f;
                Transform branch = K.Group("Ast", t, new Vector3(0f, h, 0f), new Vector3(0f, a, 0f));
                float tilt = 45f + (float)rng.NextDouble() * 25f;
                K.Prim(PrimitiveType.Cylinder, "Ast", branch, Vector3.zero, new Vector3(1.4f, 9f, 1.4f), new Vector3(tilt, 0f, 0f), _bark, false);
            }
            for (int i = 0; i < 5; i++)
            {
                Transform root = K.Group("Wurzel", t, Vector3.zero, new Vector3(0f, i * 72f + 20f, 0f));
                K.Prim(PrimitiveType.Cylinder, "Wurzel", root, new Vector3(0f, 1.2f, 4.6f), new Vector3(1.6f, 4f, 1.6f), new Vector3(110f, 0f, 0f), _bark);
            }
            K.Label(t, new Vector3(0f, 3f, -8f), "Aschenweide", "Glockenmoor", WorldLabelStyle.Place, 120f);
        }

        /// <summary>
        /// Das Uhrwerk (sichtbare Zukunft, Stufe 10–12, kein Dungeon): versiegeltes Doppeltor in einem Felsblock am Ende des
        /// Uhrwerk-Pfads, darüber ein langsam drehendes Zahnrad halb im Fels. yaw = Blick vom Pfad zum Tor.
        /// </summary>
        private static void V4Clockwork(Transform parent, string name, Vector3 pos, float yaw)
        {
            Transform c = K.Group(name, parent, pos, new Vector3(0f, yaw, 0f));
            K.Box("Fels um das Tor", c, new Vector3(0f, 7f, 9f), new Vector3(30f, 26f, 12f), _granite);
            Transform gate = K.Group("Versiegeltes Tor", c, new Vector3(0f, -0.4f, 2.6f), Vector3.zero);
            K.Block("Torflügel L", gate, new Vector3(-2.6f, 0f, 0f), new Vector3(5.2f, 11f, 1.2f), _metal);
            K.Block("Torflügel R", gate, new Vector3(2.6f, 0f, 0f), new Vector3(5.2f, 11f, 1.2f), _metal);
            K.Box("Siegellinie", gate, new Vector3(0f, 5.5f, -0.65f), new Vector3(0.18f, 10f, 0.06f), _coldGlow, default, false);
            K.Block("Bogen", gate, new Vector3(0f, 11f, 0f), new Vector3(12.5f, 2.2f, 1.8f), _stoneDark);
            K.PointLight("Siegellicht", gate, new Vector3(0f, 5f, -2.5f), new Color(0.4f, 0.7f, 1f), 12f, 1.6f);
            Transform gear = K.Group("Zahnrad", c, new Vector3(6f, 19f, 3.4f), new Vector3(90f, 0f, 0f));
            K.Prim(PrimitiveType.Cylinder, "Scheibe", gear, Vector3.zero, new Vector3(16f, 0.8f, 16f), Vector3.zero, _bronze, false);
            K.Prim(PrimitiveType.Cylinder, "Nabe", gear, Vector3.zero, new Vector3(3f, 1.4f, 3f), Vector3.zero, _metal, false);
            for (int i = 0; i < 14; i++)
            {
                float a = i * (360f / 14f);
                K.Box("Zahn", gear, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 8.4f), new Vector3(1.6f, 1.2f, 2f), _bronze, new Vector3(0f, a, 0f), false);
            }
            for (int i = 0; i < 4; i++)
                K.Box("Speiche", gear, Vector3.zero, new Vector3(0.8f, 0.9f, 14f), _metal, new Vector3(0f, i * 45f, 0f), false);
            var rot = gear.gameObject.AddComponent<SlowRotator>();
            K.SetVector3(rot, "localAxis", Vector3.up);
            K.SetFloat(rot, "degreesPerSecond", 1.5f);
            K.SetFloat(rot, "tickInterval", 2f);
            K.MakeDynamic(gear.gameObject);
            K.Label(c, new Vector3(0f, 3f, -2f), "Das Uhrwerk", "Versiegelt", WorldLabelStyle.Place, 90f);
        }
    }
}
