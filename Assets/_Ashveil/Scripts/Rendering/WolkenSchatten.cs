using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering
{
    /// <summary>
    /// Ziehende Wolkenschatten (Claude 12.10.2026, Westwind-Stimmung der Westweiden, VORSCHLAG an Max).
    /// Verschiebt die Lichtkachel (Cookie) der Sonne mit dem Wind. Rein kosmetisch und je Client: kein Netzverkehr, kein Zustand,
    /// keine Spielwirkung (CORE-005). Die Verschiebung hängt nur an der Laufzeit; zwei Spieler sehen nicht zwingend
    /// dieselben Wolken an derselben Stelle – für Schatten ohne Spielwirkung ist das unkritisch.
    /// Kosten: eine Kachelabfrage im Hauptlicht je Pixel (URP Light Cookie), kein Update-Aufwand außer einer Vektorzuweisung.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class WolkenSchatten : MonoBehaviour
    {
        [Tooltip("Windrichtung in der Welt (x/z) – Westweiden: Westwind, Wolken ziehen nach Osten")]
        public Vector2 richtung = new Vector2(1f, 0.25f);
        [Tooltip("Zuggeschwindigkeit der Wolken in m/s (über Grund)")]
        public float geschwindigkeit = 9f;
        [Tooltip("Größe einer Kachel in Metern")]
        public float kachel = 3000f;

        private UniversalAdditionalLightData daten;

        private void OnEnable()
        {
            daten = GetComponent<UniversalAdditionalLightData>();
            if (daten == null) daten = gameObject.AddComponent<UniversalAdditionalLightData>();
            daten.lightCookieSize = new Vector2(kachel, kachel);
        }

        private void Update()
        {
            if (daten == null) return;
            Vector2 v = richtung.sqrMagnitude > 0f ? richtung.normalized : Vector2.right;
            daten.lightCookieOffset = v * (geschwindigkeit * Time.time % kachel);
        }
    }
}
