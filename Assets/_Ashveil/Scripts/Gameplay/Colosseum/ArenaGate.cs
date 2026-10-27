using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Colosseum
{
    /// <summary>Art eines Arena-Eingangs (Kanon §36): sichtbare Tore, Bodenoeffnung, obere Galerie.</summary>
    public enum ArenaGateKind
    {
        Gate = 0,
        FloorOpening = 1,
        Gallery = 2
    }

    /// <summary>
    /// Kolosseum: Eingang, aus dem Gegner in die Arena kommen (Nord-/Sued-/Ost-/Westtor, Bodenoeffnung, Galerie).
    /// Der Marker steht am Erscheinungspunkt (hinten im Torgang, fuer Spieler sichtbar); <see cref="EntryWorld"/> ist
    /// der Punkt in der Arena, zu dem neue Gegner zuerst laufen. So erkennt man am Tor, woher Gefahr kommt – niemand
    /// erscheint direkt vor einem Spieler (Kanon §36). Der Wellen-Direktor (Pass 3) waehlt Tore; hier nur Daten.
    /// </summary>
    public sealed class ArenaGate : MonoBehaviour
    {
        [SerializeField] private string gateId = "nord";
        [SerializeField] private string displayName = "Nordtor";
        [SerializeField] private ArenaGateKind kind = ArenaGateKind.Gate;
        [Tooltip("Punkt in der Arena (lokal zum Tor), den neue Gegner zuerst ansteuern")]
        [SerializeField] private Vector3 entryLocal = new Vector3(0f, 0f, 20f);

        private static readonly List<ArenaGate> Registered = new List<ArenaGate>();

        public static IReadOnlyList<ArenaGate> All => Registered;
        public string GateId => gateId;
        public string DisplayName => displayName;
        public ArenaGateKind Kind => kind;
        public Vector3 SpawnWorld => transform.position;
        public Vector3 EntryWorld => transform.TransformPoint(entryLocal);

        public void Configure(string id, string name, ArenaGateKind gateKind, Vector3 entryPointLocal)
        {
            gateId = id;
            displayName = name;
            kind = gateKind;
            entryLocal = entryPointLocal;
        }

        private void OnEnable()
        {
            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDisable() => Registered.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = kind == ArenaGateKind.Gate ? new Color(1f, 0.4f, 0.2f) : kind == ArenaGateKind.Gallery ? new Color(0.9f, 0.8f, 0.3f) : new Color(0.6f, 0.3f, 1f);
            Gizmos.DrawWireSphere(SpawnWorld, 1.2f);
            Gizmos.DrawLine(SpawnWorld, EntryWorld);
            Gizmos.DrawWireCube(EntryWorld, Vector3.one);
        }
    }
}
