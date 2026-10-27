using Ashveil.Gameplay.Npcs;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden (Lückenliste Punkt 2, 12.10.2026): Auftragsfiguren der Westweiden-Zelle mit demselben Figurenbau wie Kareth
    /// (Menschenkörper, Namensschild, Tagesablauf, NpcIdentity). Der Tagesablauf ist vorerst ein Platz rund um die Uhr
    /// (Dienst-Figuren bleiben ansprechbar); volle Tagesabläufe kommen mit Lückenliste Punkt 3.
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        internal struct WwFigur
        {
            public string Name, Title, NpcId, Look, Activity, Note;
            public bool Female;
            public string[] Gear;
            public Vector3 Pos;
            public float Yaw;
        }

        /// <summary>Figur in die offene Szene setzen (Weltkoordinaten).</summary>
        internal static void WestweidenFigur(Transform parent, WwFigur f, int seed)
        {
            CreateMaterials();
            var spec = new NpcSpec
            {
                Name = f.Name, Title = f.Title, NpcId = f.NpcId, Quest = true, Female = f.Female, Look = f.Look, Gear = f.Gear,
                Steps = new[] { At(0f, f.Pos, f.Yaw, string.IsNullOrEmpty(f.Activity) ? "Talk|FoldArms|Stand" : f.Activity, note: f.Note) }
            };
            NpcFigure(parent, spec, seed);
        }

        /// <summary>Lückenliste Punkt 3 (13.10.2026): Bewohner-Platzhalter mit vollem Tagesablauf (keine Kennung, kein Auftrag).</summary>
        internal static void WestweidenBewohnerFigur(Transform parent, string name, string title, bool female, string look, string[] gear,
            Ashveil.Gameplay.Npcs.RoutineStep[] steps, int seed)
        {
            CreateMaterials();
            var spec = new NpcSpec { Name = name, Title = title, Female = female, Look = look, Gear = gear, Steps = steps };
            NpcFigure(parent, spec, seed);
        }

        /// <summary>
        /// Lückenliste Punkt 6 (14.10.2026): Herdentier der Westweiden (Schaf, Rind, Pferd) mit demselben Vierbeiner-Bau und derselben
        /// prozeduralen Bewegung wie die Lagertiere (<see cref="AnimalVisual"/>, <see cref="Ashveil.Gameplay.Npcs.AnimalRoutine"/>):
        /// tagsüber grasen im Weidekreis, mittags Wiederkäuer-Rast, nachts liegen. Ohne Namensschild (Herde, keine Figur), ohne
        /// Kollision und Netzverkehr (wie die Lagertiere). Lage in Szenen-/Weltkoordinaten zum Zeitpunkt des Aufrufs.
        /// </summary>
        internal static void WestweidenHerdentier(Transform parent, string art, string name, Vector3 mitte, Vector3 nachtPlatz, float radius,
            float yaw, float groesse, int seed)
        {
            AnimalBody b = HerdenKoerper(art, groesse);
            b.Art = art; b.G = groesse;
            Transform a = K.Group(name, parent, nachtPlatz, new Vector3(0f, yaw, 0f));
            AnimalVisual vis = AnimalModel(a, b, false, out _);
            // bewegte Teile nicht in die Strahlenverfolgung des dynamischen GI (sonst Erneuerung der Beschleunigungsstruktur je Bild)
            foreach (Renderer r in a.GetComponentsInChildren<Renderer>(true)) r.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.Off;
            bool pferd = art == "pferd";
            var steps = new[]
            {
                new AnimalStep { from = 0f, center = nachtPlatz, radius = 0f, yaw = yaw, pose = pferd ? AnimalPose.Sleep : AnimalPose.Lie, note = "ruht nachts" },
                new AnimalStep { from = 5.5f + (seed % 5) * 0.1f, center = mitte, radius = radius, pose = AnimalPose.Graze, note = "grast" },
                new AnimalStep { from = 12f, center = mitte, radius = radius * 0.5f, pose = pferd ? AnimalPose.Stand : AnimalPose.Lie, yaw = yaw + 40f, note = "Mittagsrast" },
                new AnimalStep { from = 14f, center = mitte, radius = radius, pose = AnimalPose.Graze, note = "grast" },
                new AnimalStep { from = 20.5f + (seed % 4) * 0.15f, center = nachtPlatz, radius = 0f, yaw = yaw, pose = pferd ? AnimalPose.Stand : AnimalPose.Lie, note = "zieht sich zusammen" }
            };
            var routine = a.gameObject.AddComponent<AnimalRoutine>();
            routine.Configure(steps, vis, new[] { AnimalPose.Graze, AnimalPose.Graze, AnimalPose.Graze, AnimalPose.Look, AnimalPose.Stand }, pferd ? 0.8f : 0.45f, seed);
            K.MakeDynamic(a.gameObject);
            // Klang (14.10.2026): Glocken – jedes zweite Schaf, jedes dritte Rind (tiefer); Pferde ohne
            if ((art == "schaf" && seed % 2 == 0) || (art == "rind" && seed % 3 == 0)) WestweidenKlang.Glocke(a, art == "rind");
        }

        private static AnimalBody HerdenKoerper(string art, float g)
        {
            Material horn = K.Mat("Horn", new Color(0.55f, 0.5f, 0.42f));
            switch (art)
            {
                case "pferd":
                    return new AnimalBody { Length = 1.75f * g, Height = 1.5f * g, Radius = 0.34f * g, LegLength = 0.92f * g, LegThick = 0.12f * g, HeadSize = 0.32f * g,
                        TailLength = 0.75f * g, Stride = 1.5f * g, Fur = K.Mat("Fur_Pferd", new Color(0.45f, 0.30f, 0.20f)), Dark = K.Mat("Fur_Pferd_Dark", new Color(0.14f, 0.10f, 0.07f)),
                        Accent = K.Mat("Fur_Pferd_Dark", new Color(0.14f, 0.10f, 0.07f)), Mane = true };
                case "rind":
                    return new AnimalBody { Length = 1.9f * g, Height = 1.35f * g, Radius = 0.44f * g, LegLength = 0.66f * g, LegThick = 0.14f * g, HeadSize = 0.33f * g,
                        TailLength = 0.75f * g, Stride = 1.1f * g, Fur = K.Mat("Fur_Rind", new Color(0.55f, 0.26f, 0.15f)), Dark = K.Mat("Fur_Rind_Dark", new Color(0.26f, 0.13f, 0.08f)),
                        Accent = horn, Horns = true };
                default:   // Schaf: dicke Wolle, dunkles Gesicht und dunkle Beine
                    return new AnimalBody { Length = 0.95f * g, Height = 0.78f * g, Radius = 0.31f * g, LegLength = 0.36f * g, LegThick = 0.07f * g, HeadSize = 0.18f * g,
                        TailLength = 0.14f * g, Stride = 0.5f * g, Fur = K.Mat("Fur_Wolle", new Color(0.86f, 0.84f, 0.78f)), Dark = K.Mat("Fur_Wolle_Dark", new Color(0.18f, 0.16f, 0.14f)),
                        Accent = K.Mat("Fur_Wolle_Dark", new Color(0.18f, 0.16f, 0.14f)) };
            }
        }
    }
}
