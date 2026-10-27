using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Verformbare Kleidungsschicht: eine versetzte Huelle eines Koerperbereichs, die mit dem Skelett mitgeht
    /// (gleiche Knochengewichte wie der Koerper). yMin/yMax sind relativ zur Koerpergroesse (0 = Fuss, 1 = Scheitel).
    /// </summary>
    [Serializable]
    public class ShellLayer
    {
        public BodyRegion regions;
        [Range(0f, 1f)] public float yMin = 0f;
        [Range(0f, 1f)] public float yMax = 1f;
        [Tooltip("Abstand zur Haut (m)")]
        public float offset = 0.02f;
        public SurfaceKind surface = SurfaceKind.Cloth;
        public DyeChannel channel = DyeChannel.Primary;
        [Tooltip("Zusaetzliche Weite nach unten (Roben, Maentel): m pro m unterhalb von flareStart")]
        public float flare;
        [Range(0f, 1f)] public float flareStart = 1f;
        [Tooltip("Haut darunter ausblenden (verhindert Durchstechen)")]
        public bool hideBody = true;

        // 22.10.2026 (Max: Rüstung wie in WoW anliegend): Band – aus einem starren Ring/Streifen der Grundformen erzeugt
        // (BodyShells.Band), nur zur Laufzeit. Bedeckt nur die Haut nahe der Bandmitte entlang der Bandachse (± bandHalf, Meter)
        // und, falls angleHalf > 0, nur im Winkelbereich um angleCenter (Grad, 0 = vorn).
        [NonSerialized] public bool band;
        [NonSerialized] public Vector3 bandCenter, bandAxis;
        [NonSerialized] public float bandHalf, bandRadius, angleCenter, angleHalf;
        [NonSerialized] public sbyte bandSide;   // 22.10.: -1 links, +1 rechts, 0 Mitte – Band deckt nur Haut seiner Seite
    }

    /// <summary>
    /// Starres Anbauteil an einem Knochen (Helm, Schulterplatte, Schnalle, Klinge ...). Versatz und Drehung sind im
    /// Zeichenraum der Figur in Ruhepose angegeben (x = rechts, y = oben, z = vorn) – unabhaengig von den Knochenachsen.
    /// Knochen "Grip" = Griff der Waffe (Klinge/Lauf entlang +Z).
    /// </summary>
    [Serializable]
    public class RigidPart
    {
        public ProcShape shape = ProcShape.Box;
        public string bone = "Head";
        public Vector3 offset;
        public Vector3 euler;
        public Vector3 size = new Vector3(0.1f, 0.1f, 0.1f);
        public SurfaceKind surface = SurfaceKind.Metal;
        public DyeChannel channel = DyeChannel.Primary;
        [Tooltip("Zusaetzlich gespiegelt auf der anderen Koerperseite (_l <-> _r)")]
        public bool mirror;
        [Tooltip("Kleines Detail – wird auf Entfernung ausgeblendet")]
        public bool detail;
        [Header("Form")]
        public float taperX = 1f;
        public float taperZ = 1f;
        [Range(10f, 360f)] public float arc = 360f;
        [Tooltip("Richtung der Oeffnung/Mitte eines Teilbogens (0 = vorn, 180 = hinten)")]
        public float facing;
        public float thickness;
        [Range(0.05f, 1f)] public float cut = 0.5f;
        [Range(3, 48)] public int segments = 16;
        public bool capped = true;
        [Tooltip("Optional: echtes Modell statt der Grundform (Groesse = Skalierung)")]
        public GameObject prefab;
    }

    /// <summary>
    /// 21.10.2026 (Tripo-Kleidung, DEC-20261021-001): echtes verformbares Kleidungsmodell fuer GENAU einen Koerper. Das FBX
    /// traegt dasselbe Skelett wie der Koerper (Mixamo, gleiche Bindposen) und wird nach Knochennamen eingehaengt. Gewaehlt
    /// ueber den Namen des Koerpernetzes (exakt, z. B. "CHR_W_TR01") – passt kein Eintrag, gelten Schalen und Grundformen
    /// wie bisher (Quaternius/MPFB bleiben unveraendert). <see cref="hide"/> dient nur dem Ausblenden der Haut darunter.
    /// </summary>
    [Serializable]
    public class SkinnedModel
    {
        [Tooltip("Name des Koerpernetzes (exakter Vergleich), z. B. CHR_W_TR01")]
        public string body;
        public GameObject model;
        [Tooltip("Pfad relativ zu Assets/_Ashveil – wird bei jedem Katalogbau neu aufgeloest")]
        public string modelPath;
        public SurfaceKind surface = SurfaceKind.Cloth;
        public DyeChannel channel = DyeChannel.Primary;
        [Tooltip("Nur zum Verdecken: Haut in diesen Bereichen/Hoehenbaendern ausblenden (gemessen in Blender, verdeckung.json)")]
        public List<ShellLayer> hide = new List<ShellLayer>();
    }

    /// <summary>
    /// Aussehen eines Gegenstands – getrennt von seinen Spielwerten. Mehrere Items koennen dasselbe Aussehen teilen
    /// (Sets, Varianten); kosmetische Uebertragung setzt nur eine andere AppearanceDefinition ein.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Appearance", fileName = "Appearance_New")]
    public sealed class AppearanceDefinition : GameDefinition
    {
        [Tooltip("Welcher Platz (fuer kosmetische Uebertragung und Pruefungen)")]
        public EquipType slot = EquipType.Chest;
        public BodyVariantMask bodies = BodyVariantMask.All;
        [Header("Standardfarben der Faerbebereiche")]
        public Color primary = new Color(0.6f, 0.55f, 0.48f);
        public Color secondary = new Color(0.35f, 0.3f, 0.26f);
        public Color detail = new Color(0.66f, 0.52f, 0.25f);
        [Header("Verdecken")]
        public AppearanceHides hides;
        [Header("Aufbau")]
        public List<ShellLayer> shells = new List<ShellLayer>();
        public List<RigidPart> parts = new List<RigidPart>();
        [Header("Echte Modelle je Koerper (21.10.)")]
        [Tooltip("Ersetzt Schalen und Grundformen dieses Platzes, wenn der Koerper passt")]
        public List<SkinnedModel> modelle = new List<SkinnedModel>();
        [Header("Waffen")]
        public WeaponSheath sheath = WeaponSheath.None;
        [Header("Darstellung")]
        [Tooltip("Ab dieser Entfernung (m) werden Details ausgeblendet")]
        public float detailDistance = 25f;
        [Tooltip("Einfaches Prototyp-Modell aus Grundformen")]
        public bool placeholder = true;

        protected override string IdPrefix => "appearance";

        public Color DefaultColor(DyeChannel c) => c == DyeChannel.Primary ? primary : c == DyeChannel.Secondary ? secondary : detail;

        /// <summary>21.10.: Kleidungsmodell fuer diesen Koerper (exakter Name des Koerpernetzes), sonst null.</summary>
        public SkinnedModel ModellFuer(string bodyMesh)
        {
            if (string.IsNullOrEmpty(bodyMesh) || modelle == null) return null;
            foreach (SkinnedModel m in modelle)
                if (m != null && string.Equals(m.body, bodyMesh, StringComparison.Ordinal)) return m;
            return null;
        }

        /// <summary>Kurzer Fingerabdruck der Geometrie (ohne Farben) – zum Pruefen, dass Items sich in der Form unterscheiden.</summary>
        public string GeometrySignature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (ShellLayer s in shells) sb.Append($"S{(int)s.regions}:{s.yMin:0.00}-{s.yMax:0.00}:{s.offset:0.000}:{s.flare:0.00}|");
            foreach (RigidPart p in parts)
                sb.Append($"P{(int)p.shape}:{p.bone}:{p.offset.x:0.00},{p.offset.y:0.00},{p.offset.z:0.00}:{p.size.x:0.00},{p.size.y:0.00},{p.size.z:0.00}:{p.arc:0}:{p.taperX:0.00}" +
                          // 22.10.: Anbauteile mit Modell (Helm, Schulter) – das Modell ist die Form (sonst glichen sich Wächter- und Jägerschulter)
                          (p.prefab != null ? ":" + p.prefab.name : "") + "|");
            // 21.10.: echte Modelle je Koerper gehoeren zur Form (DuoTestDirector prueft, dass sich die Formen unterscheiden)
            if (modelle != null)
                foreach (SkinnedModel m in modelle)
                    if (m != null) sb.Append($"M{m.body}:{(m.model != null ? m.model.name : m.modelPath)}|");
            return sb.ToString();
        }
    }
}
