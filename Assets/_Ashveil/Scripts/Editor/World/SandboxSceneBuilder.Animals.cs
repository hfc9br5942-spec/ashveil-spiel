using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Gameplay.Npcs;
using Ashveil.World;
using UnityEngine;
using L = Ashveil.EditorTools.StartCampLayout;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// M8.4: Tiere im Startlager – Katze „Asche“, Hund „Bolt“ (gehoert zur Wache Brenn), zwei Ziegen und ein Maultier im
    /// Pferch (das Maultier wird spaeter das Zugtier der Karren). Greybox aus Grundformen, prozedural bewegt
    /// (<see cref="AnimalVisual"/>), Tagesablauf aus der Weltuhr (<see cref="AnimalRoutine"/>). Keine Kollision und kein
    /// Netzverkehr – rein Atmosphaere. Soziale Momente: Hedda streichelt um 14 Uhr die Katze, der Hund begleitet Brenn.
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        private struct AnimalBody
        {
            public float Length, Height, Radius, LegLength, LegThick, HeadSize, TailLength, Stride, SitAngle;
            public Material Fur, Dark, Accent;
            public bool Wag, Horns, LongEars, Mane, Beard;
            /// <summary>Art mit eigenem Modell (<see cref="TierModelle"/>: schaf, rind, pferd) und ihre Größe; leer = Grundkörper.</summary>
            public string Art; public float G;
        }

        private static void BuildCampAnimals(Transform camp)
        {
            Transform root = K.Group("Tiere", camp);
            CampAnimalBodies(out AnimalBody cat, out AnimalBody dog, out AnimalBody goat, out AnimalBody mule);

            // Orte
            Vector3 G(float x, float z) => new Vector3(x, KarethTerrainBuilder.Height(x, z), z);
            Vector3 kitchen = G(L.Kitchen.x + 2f, L.Kitchen.y + 3f), tents = G(L.Tents.x, L.Tents.y), fire = G(-2f, 7.5f);
            Vector3 tentFireCat = G(L.Tents.x + 0.9f, L.Tents.y - 1.2f), crates = G(-18.2f, -34.5f) + Vector3.up * 0.72f; // auf der grossen Vorratskiste
            Vector3 northGate = G(-3.4f, 23.2f), eastGate = G(25.8f, -9.6f), table = G(-15.6f, -30.1f), fireDog = G(-5.6f, -3.8f);
            Vector3 pen = G(L.Pen.x, L.Pen.y), mulePos = G(L.Pen.x + 2.2f, L.Pen.y + 0.4f);
            Vector3 tentDog = G(L.Tents.x - 3.2f, L.Tents.y - 3.9f);

            AnimalStep S(float from, Vector3 c, float radius, AnimalPose pose, string note, float yaw = 0f) =>
                new AnimalStep { from = from, center = c, radius = radius, pose = pose, note = note, yaw = yaw };

            Animal(root, "Katze Asche", cat, 0.9f, 101, new[] { AnimalPose.Sit, AnimalPose.Groom, AnimalPose.Look, AnimalPose.Lie, AnimalPose.Sniff }, new[]
            {
                S(0f, tentFireCat, 0f, AnimalPose.Sleep, "schlaeft am Zeltfeuer", 40f),
                S(6f, kitchen, 4.5f, AnimalPose.Sit, "streift um die Kochstelle"),
                S(10f, crates, 0f, AnimalPose.Lie, "liegt auf den Vorraeten", 120f),
                S(12.3f, G(-15f, -27.5f), 3.5f, AnimalPose.Sit, "bettelt an den Langtischen"),
                S(13.3f, kitchen, 3f, AnimalPose.Groom, "putzt sich"),
                S(14f, G(-3.3f, 4.45f), 0f, AnimalPose.Sit, "wird von Hedda gestreichelt", 20f),
                S(14.4f, tents, 6f, AnimalPose.Look, "streift bei den Zelten"),
                S(18f, fire, 4f, AnimalPose.Groom, "am Wachtfeuer"),
                S(21f, tentFireCat, 0f, AnimalPose.Lie, "liegt am Zeltfeuer", 40f)
            });
            Animal(root, "Hund Bolt", dog, 1.5f, 202, new[] { AnimalPose.Sit, AnimalPose.Sniff, AnimalPose.Look, AnimalPose.Lie }, new[]
            {
                S(0f, tentDog, 0f, AnimalPose.Sleep, "schlaeft vor Brenns Zelt", 150f),
                S(6f, northGate, 3.5f, AnimalPose.Sit, "mit Brenn am Nordtor"),
                S(12f, table, 0f, AnimalPose.Lie, "liegt unter dem Tisch", 90f),
                S(12.75f, eastGate, 3f, AnimalPose.Sniff, "mit Brenn am Osttor"),
                S(15f, northGate, 3.5f, AnimalPose.Sit, "mit Brenn am Nordtor"),
                S(18f, table, 0f, AnimalPose.Lie, "liegt unter dem Tisch", 90f),
                S(19f, fireDog, 0f, AnimalPose.Lie, "liegt am Wachtfeuer", 60f),
                S(23f, tentDog, 0f, AnimalPose.Sleep, "schlaeft vor Brenns Zelt", 150f)
            }, dog.Wag);
            for (int i = 0; i < 2; i++)
                Animal(root, i == 0 ? "Ziege Rinde" : "Ziege Flocke", goat, 0.55f, 303 + i, new[] { AnimalPose.Graze, AnimalPose.Graze, AnimalPose.Look, AnimalPose.Stand }, new[]
                {
                    S(0f, G(pen.x - 1.5f + i * 1.3f, pen.z - 1.4f), 0f, AnimalPose.Sleep, "schlaeft im Pferch", 80f + i * 140f),
                    S(5.5f, pen, 2.6f, AnimalPose.Graze, "grast im Pferch"),
                    S(12f, G(pen.x - 2.8f + i * 1.2f, pen.z - 1.6f), 0f, AnimalPose.Graze, "frisst am Heu", 180f),
                    S(13f, pen, 2.6f, AnimalPose.Graze, "grast im Pferch"),
                    S(21f, G(pen.x - 1.5f + i * 1.3f, pen.z - 1.4f), 0f, AnimalPose.Lie, "liegt im Pferch", 80f + i * 140f)
                });
            Animal(root, "Maultier Knorre", mule, 0.7f, 404, new[] { AnimalPose.Stand, AnimalPose.Graze, AnimalPose.Look }, new[]
            {
                S(0f, mulePos, 0f, AnimalPose.Sleep, "doest im Pferch", 250f),
                S(5f, mulePos, 1.6f, AnimalPose.Stand, "steht im Pferch"),
                S(11f, G(pen.x + 2.8f, pen.z - 2.2f), 0f, AnimalPose.Graze, "trinkt an der Traenke", 180f),
                S(11.5f, mulePos, 1.6f, AnimalPose.Graze, "steht im Pferch"),
                S(22f, mulePos, 0f, AnimalPose.Sleep, "doest im Pferch", 250f)
            });
        }

        /// <summary>Koerper und Fellfarben der Lagertiere (auch Kareth v4, #27).</summary>
        private static void CampAnimalBodies(out AnimalBody cat, out AnimalBody dog, out AnimalBody goat, out AnimalBody mule)
        {
            Material catFur = K.Mat("Fur_Ash", new Color(0.42f, 0.41f, 0.4f)), catDark = K.Mat("Fur_Ash_Dark", new Color(0.22f, 0.21f, 0.21f));
            Material dogFur = K.Mat("Fur_Brown", new Color(0.45f, 0.32f, 0.2f)), dogDark = K.Mat("Fur_Brown_Dark", new Color(0.24f, 0.17f, 0.11f));
            Material goatFur = K.Mat("Fur_Goat", new Color(0.7f, 0.66f, 0.58f)), goatDark = K.Mat("Fur_Goat_Dark", new Color(0.33f, 0.3f, 0.27f));
            Material muleFur = K.Mat("Fur_Mule", new Color(0.36f, 0.3f, 0.26f)), muleDark = K.Mat("Fur_Mule_Dark", new Color(0.16f, 0.14f, 0.12f));
            Material horn = K.Mat("Horn", new Color(0.55f, 0.5f, 0.42f));

            cat = new AnimalBody { Length = 0.46f, Height = 0.26f, Radius = 0.09f, LegLength = 0.17f, LegThick = 0.05f, HeadSize = 0.12f, TailLength = 0.34f, Stride = 0.32f, SitAngle = 38f, Fur = catFur, Dark = catDark, Accent = catDark };
            dog = new AnimalBody { Length = 0.72f, Height = 0.46f, Radius = 0.15f, LegLength = 0.3f, LegThick = 0.08f, HeadSize = 0.19f, TailLength = 0.34f, Stride = 0.6f, SitAngle = 34f, Fur = dogFur, Dark = dogDark, Accent = dogDark, Wag = true };
            goat = new AnimalBody { Length = 0.82f, Height = 0.6f, Radius = 0.19f, LegLength = 0.4f, LegThick = 0.07f, HeadSize = 0.19f, TailLength = 0.12f, Stride = 0.55f, SitAngle = 0f, Fur = goatFur, Dark = goatDark, Accent = horn, Horns = true, Beard = true };
            mule = new AnimalBody { Length = 1.45f, Height = 1.18f, Radius = 0.3f, LegLength = 0.78f, LegThick = 0.12f, HeadSize = 0.3f, TailLength = 0.55f, Stride = 1.2f, SitAngle = 0f, Fur = muleFur, Dark = muleDark, Accent = muleDark, LongEars = true, Mane = true };
        }

        private static void Animal(Transform parent, string name, AnimalBody b, float speed, int seed, AnimalPose[] idles, AnimalStep[] steps, bool wag = false)
        {
            Transform a = K.Group(name, parent, steps[0].center, new Vector3(0f, steps[0].yaw, 0f));
            AnimalVisual vis = AnimalModel(a, b, wag, out float hipY);
            var routine = a.gameObject.AddComponent<AnimalRoutine>();
            routine.Configure(steps, vis, idles, speed, seed);
            K.MakeDynamic(a.gameObject);
            K.Label(a, new Vector3(0f, hipY + b.Radius * 2f + (b.Mane ? 0.9f : 0.35f), 0f), name.Substring(name.IndexOf(' ') + 1), name.Substring(0, name.IndexOf(' ')),
                WorldLabelStyle.Friendly, 12f, 0.7f);
        }

        /// <summary>Maultier-Koerper wie im Pferch (fuer Zugtiere der Fuhrwerke, M8.6).</summary>
        private static AnimalBody MuleBody() => new AnimalBody
        {
            Length = 1.45f, Height = 1.18f, Radius = 0.3f, LegLength = 0.78f, LegThick = 0.12f, HeadSize = 0.3f, TailLength = 0.55f, Stride = 1.2f, SitAngle = 0f,
            Fur = K.Mat("Fur_Mule", new Color(0.36f, 0.3f, 0.26f)), Dark = K.Mat("Fur_Mule_Dark", new Color(0.16f, 0.14f, 0.12f)),
            Accent = K.Mat("Fur_Mule_Dark", new Color(0.16f, 0.14f, 0.12f)), LongEars = true, Mane = true
        };

        /// <summary>Koerper eines Vierbeiners aus Grundformen unter 'a' bauen; liefert die prozedurale Bewegung.</summary>
        private static AnimalVisual AnimalModel(Transform a, AnimalBody b, bool wag, out float hipY)
        {
            Transform model = K.Group("Model", a);
            hipY = b.LegLength + b.Radius * 0.3f;
            float half = b.Length * 0.5f;
            if (!string.IsNullOrEmpty(b.Art) && TierModelle.Hat(b.Art)) return AnimalModelGeformt(a, model, b, wag, hipY, half);

            // Rumpf
            K.Prim(PrimitiveType.Capsule, "Rumpf", model, new Vector3(0f, hipY + b.Radius * 0.4f, 0f), new Vector3(b.Radius * 2f, half + b.Radius, b.Radius * 2.1f),
                new Vector3(90f, 0f, 0f), b.Fur, false);
            if (b.Mane) K.Box("Maehne", model, new Vector3(0f, hipY + b.Radius * 1.5f, half * 0.75f), new Vector3(0.06f, 0.18f, half * 0.7f), b.Dark, new Vector3(-30f, 0f, 0f), false);

            // Kopf (Drehpunkt am Hals)
            float neckY = hipY + b.Radius * (b.Mane ? 1.6f : 1.1f), neckZ = half + b.Radius * 0.3f;
            Transform head = K.Group("Head", model, new Vector3(0f, neckY, neckZ), Vector3.zero);
            float hs = b.HeadSize;
            if (b.Mane)
            {
                K.Prim(PrimitiveType.Capsule, "Hals", head, new Vector3(0f, hs * 0.5f, hs * 0.5f), new Vector3(hs * 0.9f, hs * 1.1f, hs * 0.9f), new Vector3(35f, 0f, 0f), b.Fur, false);
                K.Box("Schaedel", head, new Vector3(0f, hs * 1.3f, hs * 1.3f), new Vector3(hs * 0.8f, hs * 0.8f, hs * 1.9f), b.Fur, new Vector3(30f, 0f, 0f), false);
                K.Box("Maul", head, new Vector3(0f, hs * 0.8f, hs * 2.1f), new Vector3(hs * 0.66f, hs * 0.66f, hs * 0.7f), b.Dark, new Vector3(30f, 0f, 0f), false);
            }
            else
            {
                K.Prim(PrimitiveType.Sphere, "Schaedel", head, new Vector3(0f, hs * 0.35f, hs * 0.5f), new Vector3(hs, hs * 0.95f, hs * 1.05f), Vector3.zero, b.Fur, false);
                K.Box("Schnauze", head, new Vector3(0f, hs * 0.2f, hs * 1.05f), new Vector3(hs * 0.5f, hs * 0.42f, hs * 0.5f), b.Dark, Vector3.zero, false);
            }
            float earH = b.LongEars ? hs * 1.1f : hs * 0.45f;
            for (int s = -1; s <= 1; s += 2)
            {
                K.Box(s < 0 ? "Ohr L" : "Ohr R", head, new Vector3(s * hs * 0.32f, (b.Mane ? hs * 1.75f : hs * 0.85f), (b.Mane ? hs * 1.0f : hs * 0.35f)),
                    new Vector3(hs * 0.22f, earH, hs * 0.12f), b.Dark, new Vector3(-10f, 0f, s * -14f), false);
                if (b.Horns) K.Box("Horn", head, new Vector3(s * hs * 0.2f, hs * 0.95f, hs * 0.25f), new Vector3(hs * 0.12f, hs * 0.7f, hs * 0.12f), b.Accent, new Vector3(-40f, 0f, s * -10f), false);
                K.Box(s < 0 ? "Auge L" : "Auge R", head, new Vector3(s * hs * 0.3f, (b.Mane ? hs * 1.45f : hs * 0.5f), (b.Mane ? hs * 1.55f : hs * 0.95f)),
                    new Vector3(hs * 0.12f, hs * 0.1f, hs * 0.06f), b.Dark, Vector3.zero, false);
            }
            if (b.Beard) K.Box("Bart", head, new Vector3(0f, -hs * 0.2f, hs * 0.95f), new Vector3(hs * 0.18f, hs * 0.45f, hs * 0.12f), b.Dark, new Vector3(10f, 0f, 0f), false);

            // Beine (Drehpunkt an Schulter/Huefte)
            var legs = new Transform[4];
            string[] names = { "Bein VL", "Bein VR", "Bein HL", "Bein HR" };
            float legX = b.Radius * 0.62f, legZ = half * 0.78f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 lp = new Vector3(i % 2 == 0 ? -legX : legX, hipY, i < 2 ? legZ : -legZ);
                legs[i] = K.Group(names[i], model, lp, Vector3.zero);
                K.Prim(PrimitiveType.Capsule, "Bein", legs[i], new Vector3(0f, -b.LegLength * 0.5f, 0f), new Vector3(b.LegThick, b.LegLength * 0.5f + b.LegThick * 0.3f, b.LegThick),
                    Vector3.zero, b.Fur, false);
                K.Box("Pfote", legs[i], new Vector3(0f, -b.LegLength + b.LegThick * 0.25f, b.LegThick * 0.2f), new Vector3(b.LegThick * 1.1f, b.LegThick * 0.5f, b.LegThick * 1.4f), b.Dark, default, false);
            }

            // Schwanz
            Transform tail = K.Group("Tail", model, new Vector3(0f, hipY + b.Radius * 0.7f, -half - b.Radius * 0.2f), Vector3.zero);
            K.Prim(PrimitiveType.Capsule, "Schwanz", tail, new Vector3(0f, b.TailLength * 0.15f, -b.TailLength * 0.45f),
                new Vector3(b.LegThick * 0.7f, b.TailLength * 0.5f, b.LegThick * 0.7f), new Vector3(b.Wag ? -60f : -110f, 0f, 0f), b.Dark, false);

            var vis = a.gameObject.AddComponent<AnimalVisual>();
            vis.Configure(model, head, tail, legs, b.LegLength, b.Stride, new Vector2(hipY, -legZ), b.SitAngle, wag);
            return vis;
        }

        /// <summary>
        /// Lückenliste Punkt 6 „Tiere“ (14.10.2026): dieselben Drehpunkte wie die Grundkörper (Kopf, Beine, Schwanz – die Bewegung
        /// bleibt gleich), aber das Blender-Modell aus <see cref="TierModelle"/> (ein Netz mit Haut und Skelett, die Knochen hängen
        /// an den Drehpunkten); Augen als kleine dunkle Punkte.
        /// </summary>
        private static AnimalVisual AnimalModelGeformt(Transform a, Transform model, AnimalBody b, bool wag, float hipY, float half)
        {
            float g = b.G > 0f ? b.G : 1f;
            float neckY = hipY + b.Radius * (b.Mane ? 1.6f : 1.1f), neckZ = half + b.Radius * 0.3f;
            Transform head = K.Group("Head", model, new Vector3(0f, neckY, neckZ), Vector3.zero);
            var legs = new Transform[4];
            string[] names = { "Bein VL", "Bein VR", "Bein HL", "Bein HR" };
            float legX = b.Radius * 0.62f, legZ = half * 0.78f;
            for (int i = 0; i < 4; i++)
                legs[i] = K.Group(names[i], model, new Vector3(i % 2 == 0 ? -legX : legX, hipY, i < 2 ? legZ : -legZ), Vector3.zero);
            Transform tail = K.Group("Tail", model, new Vector3(0f, hipY + b.Radius * 0.7f, -half - b.Radius * 0.2f), Vector3.zero);
            var erg = TierModelle.Bauen(b.Art, g, model, head, legs, tail);
            if (!erg.ok) Debug.LogError("[Ashveil] Tiermodell fehlt: " + b.Art);
            // Augen (Kopfnetz bei Größe 1 → mal g)
            Vector3 auge = b.Art == "rind" ? new Vector3(0.14f, -0.06f, 0.36f) : b.Art == "pferd" ? new Vector3(0.095f, 0.33f, 0.52f) : new Vector3(0.07f, 0.0f, 0.15f);
            Material augeMat = b.Art == "schaf" ? K.Mat("Auge_Hell", new Color(0.55f, 0.48f, 0.3f)) : b.Dark;
            for (int s = -1; s <= 1 && !erg.kreatur; s += 2)        // Kreaturmodelle haben gemalte Augen
                K.Box(s < 0 ? "Auge L" : "Auge R", head, new Vector3(s * auge.x, auge.y, auge.z) * g, new Vector3(0.02f, 0.025f, 0.03f) * g * (b.Art == "schaf" ? 1f : 1.5f),
                    augeMat, Vector3.zero, false);
            var vis = a.gameObject.AddComponent<AnimalVisual>();
            vis.Configure(model, head, tail, legs, b.LegLength, b.Stride, new Vector2(hipY, -legZ), b.SitAngle, wag);
            if (erg.animator != null) vis.Clips(erg.animator, erg.clips);      // aufgenommene Bewegung (MOV-127)
            else if (erg.kreatur) vis.Gelenke(erg.mitte, erg.unten, erg.fessel, b.Art == "pferd" || b.Art == "rind");
            return vis;
        }
    }
}
