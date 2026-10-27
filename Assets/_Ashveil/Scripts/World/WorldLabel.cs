using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>Darstellungsart eines Namensschilds (Farbe, Groesse, Verhalten).</summary>
    public enum WorldLabelStyle
    {
        /// <summary>Freundlicher NPC: gruener Name, Titel in spitzen Klammern.</summary>
        Friendly,
        /// <summary>NPC mit Auftrag: wie Friendly, zusaetzlich gelbes Ausrufezeichen.</summary>
        QuestGiver,
        /// <summary>Feindlich: roter Name.</summary>
        Hostile,
        /// <summary>Ort / Landmarke: warme Pergamentfarbe, etwas groesser.</summary>
        Place,
        /// <summary>Entwickler-Info (Testparcours): hellgrau, klein.</summary>
        Info,
        /// <summary>Neutral (angreifbar, greift nicht an): gelber Name.</summary>
        Neutral
    }

    /// <summary>Auftragsmarkierung ueber einem NPC (je Spieler von der Oberflaeche gesetzt).</summary>
    public enum WorldLabelMarker
    {
        None,
        /// <summary>Graues "?" – Auftrag laeuft</summary>
        InProgress,
        /// <summary>Gelbes "!" – neuer Auftrag</summary>
        Available,
        /// <summary>Gelbes "?" – abgeben</summary>
        Complete
    }

    /// <summary>
    /// Markiert einen Punkt in der Welt, ueber dem ein Name oder Hinweis angezeigt wird.
    ///
    /// Die Komponente enthaelt nur Daten. Gezeichnet wird zentral vom WorldLabelRenderer
    /// (Ashveil.UI) als scharfer Bildschirmtext in konstanter Pixelgroesse – wie Namensschilder
    /// in klassischen MMOs. So bleibt Text unabhaengig von Entfernung und Winkel lesbar.
    /// </summary>
    public sealed class WorldLabel : MonoBehaviour
    {
        private static readonly List<WorldLabel> ActiveLabels = new List<WorldLabel>();

        [SerializeField] private string title = "Name";
        [Tooltip("NPC-Titel (wird als <Titel> angezeigt) oder zweite Zeile")]
        [SerializeField] private string subtitle = "";
        [SerializeField] private WorldLabelStyle style = WorldLabelStyle.Friendly;
        [Tooltip("Versatz in Weltkoordinaten relativ zum Objekt")]
        [SerializeField] private Vector3 offset = Vector3.zero;
        [Tooltip("Ab dieser Entfernung (m) wird das Schild ausgeblendet")]
        [SerializeField] private float maxDistance = 40f;
        [SerializeField] private float sizeMultiplier = 1f;

        /// <summary>Alle aktiven Schilder (vom Renderer gelesen).</summary>
        public static IReadOnlyList<WorldLabel> Active => ActiveLabels;

        public string Title => title;
        public string Subtitle => subtitle;
        public WorldLabelStyle Style => style;
        public float MaxDistance => maxDistance;
        public float SizeMultiplier => sizeMultiplier;
        public Vector3 WorldPosition => transform.position + offset;

        /// <summary>Steigt bei jeder Aenderung, damit der Renderer den Text nur dann neu setzt.</summary>
        public int Version { get; private set; }

        [System.NonSerialized] private bool _markerControlled;
        [System.NonSerialized] private WorldLabelMarker _marker;

        /// <summary>true = Markierung kommt vom Spielstand (statt festem "!" bei QuestGiver).</summary>
        public bool MarkerControlled => _markerControlled;
        public WorldLabelMarker Marker => _marker;

        public void SetMarker(WorldLabelMarker marker)
        {
            if (_markerControlled && _marker == marker) return;
            _markerControlled = true;
            _marker = marker;
            Version++;
        }

        public void Configure(string newTitle, string newSubtitle, WorldLabelStyle newStyle, float newMaxDistance, float newSize = 1f)
        {
            title = newTitle;
            subtitle = newSubtitle ?? "";
            style = newStyle;
            maxDistance = newMaxDistance;
            sizeMultiplier = newSize;
            Version++;
        }

        public void SetText(string newTitle, string newSubtitle)
        {
            title = newTitle;
            subtitle = newSubtitle ?? "";
            Version++;
        }

        private void OnEnable()
        {
            if (!ActiveLabels.Contains(this)) ActiveLabels.Add(this);
            Version++;
        }

        private void OnDisable()
        {
            ActiveLabels.Remove(this);
        }

        private void OnValidate()
        {
            maxDistance = Mathf.Max(1f, maxDistance);
            sizeMultiplier = Mathf.Clamp(sizeMultiplier, 0.3f, 4f);
            Version++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            ActiveLabels.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f);
            Gizmos.DrawWireSphere(WorldPosition, 0.15f);
        }
    }
}
