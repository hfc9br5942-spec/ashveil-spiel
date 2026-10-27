using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Audio
{
    /// <summary>
    /// Geräuschquelle in der Welt (Claude 14.10.2026, Klangteppich, TECH-125): Schleife (Bach, Brandung, Mühle) oder gelegentlicher
    /// Einzelklang (Schafglocke). Hat selbst KEINE AudioSource – die <see cref="KlangLandschaft"/> gibt nur den nächsten Quellen
    /// eine aus ihrem Pool (Obergrenze hörbarer Quellen, MMO-Regel). Nur Darstellung am Client, kein Netzverkehr.
    /// </summary>
    public sealed class KlangQuelle : MonoBehaviour
    {
        public AudioClip[] clips;
        public bool schleife = true;
        [Range(0f, 1.5f)] public float lautstaerke = 1f;
        public float minAbstand = 4f;
        public float maxAbstand = 60f;
        [Tooltip("Einzelklang: Pause zwischen zwei Klängen (s)")] public Vector2 pause = new Vector2(4f, 12f);
        [Tooltip("Zufällige Tonhöhe ± (Einzelklang)")] public float tonhoehe = 0.05f;
        [Tooltip("Grundtonhöhe (Rinderglocke tiefer als Schafglocke)")] public float grundTon = 1f;
        [Tooltip("0 = immer, 1 = nur am Tag, 2 = nur nachts")] public int tageszeit;

        internal static readonly List<KlangQuelle> Alle = new List<KlangQuelle>();
        internal int Platz = -1;          // Index im Pool der KlangLandschaft (−1 = stumm)
        internal float Naechster;         // Einzelklang: nächster Zeitpunkt
        internal float Abstand2;

        public void Konfigurieren(AudioClip[] c, bool loop, float vol, float min, float max, int zeit = 0)
        {
            clips = c; schleife = loop; lautstaerke = vol; minAbstand = min; maxAbstand = max; tageszeit = zeit;
        }

        private void OnEnable() => Alle.Add(this);

        private void OnDisable()
        {
            Alle.Remove(this);
            KlangLandschaft.Freigeben(this);
        }
    }
}
