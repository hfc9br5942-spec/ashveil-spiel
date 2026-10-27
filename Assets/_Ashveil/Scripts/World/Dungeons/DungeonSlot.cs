using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>Stimmung eines Instanz-Platzes: unter Tage (Salzgruben) oder unter freiem Himmel (Kolosseum).</summary>
    public enum SlotAtmosphere
    {
        Underground = 0,
        OpenAir = 1
    }

    /// <summary>
    /// Ein Instanz-Platz eines Dungeons: eine vollstaendige Kopie der Raeume weit ausserhalb der Oberwelt.
    /// Der Server (DungeonDirector) vergibt freie Plaetze an Gruppen; alle Rechner haben die Geometrie in der Szene.
    /// Liefert ausserdem die Werte fuer die Stimmung unter Tage (IndoorAtmosphere, nur Darstellung).
    /// </summary>
    public sealed class DungeonSlot : MonoBehaviour
    {
        [SerializeField] private string dungeonId = "salzgruben";
        [SerializeField] private int index;
        [Tooltip("Bereich der Instanz (lokal) – wer darin steht, ist \"in\" dieser Instanz")]
        [SerializeField] private Vector3 boundsCenter = Vector3.zero;
        [SerializeField] private Vector3 boundsSize = new Vector3(100f, 60f, 100f);
        [Tooltip("Ankunft und Wiederbelebung (lokal)")]
        [SerializeField] private Vector3 arrival = Vector3.zero;
        [SerializeField] private float arrivalYaw;
        [Tooltip("Ausgang (Foerderkorb, lokal) und sein Radius")]
        [SerializeField] private Vector3 exit = Vector3.zero;
        [SerializeField] private float exitRadius = 2.2f;

        [Tooltip("Wo man nach dem Tod erwacht (Satzende fuer die Meldung, z. B. \"am Eingang der Grube\")")]
        [SerializeField] private string respawnText = "am Eingang der Grube";

        [Tooltip("Unter Tage: Sonne aus, dunkles Licht (IndoorAtmosphere). Freiluft: normaler Himmel.")]
        [SerializeField] private SlotAtmosphere atmosphere = SlotAtmosphere.Underground;

        [Header("Stimmung unter Tage")]
        [SerializeField] private Color ambientSky = new Color(0.15f, 0.145f, 0.17f);
        [SerializeField] private Color ambientEquator = new Color(0.13f, 0.115f, 0.1f);
        [SerializeField] private Color ambientGround = new Color(0.075f, 0.065f, 0.055f);
        [SerializeField] private Color fogColor = new Color(0.2f, 0.18f, 0.155f);
        [SerializeField] private float fogDensity = 0.01f;

        private static readonly List<DungeonSlot> Registered = new List<DungeonSlot>();

        public static IReadOnlyList<DungeonSlot> All => Registered;
        public string DungeonId => dungeonId;
        public int Index => index;
        public Bounds WorldBounds => new Bounds(transform.TransformPoint(boundsCenter), boundsSize);
        public Vector3 ArrivalWorld => transform.TransformPoint(arrival);
        public float ArrivalYaw => transform.eulerAngles.y + arrivalYaw;
        public Vector3 ExitWorld => transform.TransformPoint(exit);
        public float ExitRadius => exitRadius;
        public Color AmbientSky => ambientSky;
        public Color AmbientEquator => ambientEquator;
        public Color AmbientGround => ambientGround;
        public Color FogColor => fogColor;
        public float FogDensity => fogDensity;
        public SlotAtmosphere Atmosphere => atmosphere;
        public bool IsUnderground => atmosphere == SlotAtmosphere.Underground;
        public string RespawnText => string.IsNullOrEmpty(respawnText) ? "am Eingang" : respawnText;

        public void Configure(string id, int slotIndex, Vector3 center, Vector3 size, Vector3 arrivalLocal, float yaw, Vector3 exitLocal, float radius)
        {
            dungeonId = id;
            index = slotIndex;
            boundsCenter = center;
            boundsSize = size;
            arrival = arrivalLocal;
            arrivalYaw = yaw;
            exit = exitLocal;
            exitRadius = radius;
        }

        /// <summary>Stimmung und Text der Wiederbelebung (Kolosseum: Freiluft, "an der Kaempferpforte").</summary>
        public void ConfigureMood(SlotAtmosphere mood, string respawn)
        {
            atmosphere = mood;
            respawnText = respawn;
        }

        public bool Contains(Vector3 worldPos) => WorldBounds.Contains(worldPos);

        /// <summary>Instanz-Platz, in dem dieser Punkt liegt (oder null = Oberwelt).</summary>
        public static DungeonSlot At(Vector3 worldPos)
        {
            foreach (DungeonSlot s in Registered)
                if (s != null && s.Contains(worldPos)) return s;
            return null;
        }

        private void OnEnable()
        {
            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDisable() => Registered.Remove(this);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.9f, 1f, 0.5f);
            Bounds b = WorldBounds;
            Gizmos.DrawWireCube(b.center, b.size);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(ArrivalWorld, 1f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(ExitWorld, exitRadius);
        }
    }
}
