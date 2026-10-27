using System.IO;
using UnityEngine;

namespace Ashveil.Maps
{
    /// <summary>
    /// Kartenbezug einer Szene (Spieltest r2, 08.10.2026): lädt die Kartendaten einmal – für Server und Client im selben Prozess
    /// gemeinsam – und rechnet Szenen- in Weltkoordinaten um. Der Server liest daraus die Unterzone der Figur (Entdeckungs-EP,
    /// PROG-013: „Der Server entscheidet und speichert; der Client zeigt nur an“), der Client zeigt Banner, Minimap und Karte
    /// (<see cref="KartenSystem"/>). Ohne Kartenbezug in der Szene gibt es keine Unterzonen-Entdeckung (z. B. Sandbox_M1).
    ///
    /// Daten: StreamingAssets/&lt;Ordner&gt; (karten_unity.json, zonen.bytes, szenen/&lt;Szenenraster&gt;.*). Der Kareth-v4-Bau setzt
    /// Ordner „Ashveil/Karten“, Szenenraster „kareth_v4“ und Weltversatz (8800, 17270) (<c>KarethV4Spiel</c>).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-600)]
    public sealed class KartenBezug : MonoBehaviour
    {
        public static KartenBezug Instanz { get; private set; }

        [Tooltip("Ordner unter StreamingAssets")]
        [SerializeField] private string datenOrdner = "Ashveil/Karten";
        [Tooltip("Szenenraster unter <Ordner>/szenen (leer = nur Weltraster)")]
        [SerializeField] private string szenenRaster = "";
        [Tooltip("Weitere Szenenraster (Weltzellen, z. B. westweiden_v2) – gelten in ihrem Rechteck")]
        [SerializeField] private string[] weitereRaster = new string[0];
        [Tooltip("Szene → Welt in Metern (wird zur Szenenposition addiert)")]
        [SerializeField] private Vector2 weltversatz = Vector2.zero;

        public ZonenDienst Dienst { get; private set; }
        public string Fehler { get; private set; }
        public Vector2 Weltversatz => weltversatz;
        public string SzenenRaster => szenenRaster;
        public string Ordner => Path.Combine(Application.streamingAssetsPath, datenOrdner);

        /// <summary>Vom Szenenbau gesetzt (Editor).</summary>
        public void Konfigurieren(string ordner, string szene, Vector2 versatz, params string[] weitere)
        {
            datenOrdner = ordner;
            szenenRaster = szene ?? "";
            weltversatz = versatz;
            weitereRaster = weitere ?? new string[0];
        }

        public string[] WeitereRaster => weitereRaster;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Zuruecksetzen() => Instanz = null;   // Play ohne Neuladen der Skripte

        private void Awake()
        {
            if (Instanz != null && Instanz != this)
            {
                Debug.LogWarning("[Karten] Zweiter Kartenbezug in der Szene – ignoriert.");
                enabled = false;
                return;
            }
            Instanz = this;
            Laden();
        }

        private void OnDestroy()
        {
            if (Instanz == this) Instanz = null;
        }

        /// <summary>Daten (neu) laden; false, wenn etwas fehlt (Meldung in <see cref="Fehler"/>).</summary>
        public bool Laden()
        {
            float t0 = Time.realtimeSinceStartup;
            Dienst = ZonenDienst.Laden(Ordner, out string fehler);
            Fehler = fehler;
            if (Dienst == null)
            {
                Debug.LogWarning("[Karten] " + fehler);
                return false;
            }
            if (!string.IsNullOrEmpty(szenenRaster) && !Dienst.LadeSzene(szenenRaster, out string szFehler))
            {
                Fehler = szFehler;
                Debug.LogWarning("[Karten] " + szFehler + " – nur Weltraster");
            }
            if (weitereRaster != null)
                foreach (string w in weitereRaster)
                    if (!string.IsNullOrEmpty(w) && !Dienst.LadeWeitereSzene(w, out string wFehler)) Debug.LogWarning("[Karten] " + wFehler);
            int ms = Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f);
            string szene = Dienst.Szene != null ? $"Szenenraster {Dienst.Szene.name} ({Dienst.SzenenZone.name}, {Dienst.Szene.breite}×{Dienst.Szene.hoehe} je {Dienst.Szene.zelle_m:0} m)" : "ohne Szenenraster";
            Debug.Log($"[Karten] Kartendaten geladen: {Dienst.Daten.zonen.Length} Gebiete, {Dienst.AnzahlUnterzonen} Unterzonen, {szene}, {ms} ms (Stand {Dienst.Daten.stand})");
            return true;
        }

        /// <summary>Szenenposition → Welt (x Ost, y = z Nord).</summary>
        public Vector2 ZuWelt(Vector3 szene) => new Vector2(szene.x + weltversatz.x, szene.z + weltversatz.y);

        /// <summary>Welt → Szenenposition (Höhe y wie übergeben).</summary>
        public Vector3 ZuSzene(float x, float z, float y = 0f) => new Vector3(x - weltversatz.x, y, z - weltversatz.y);

        public UnterzoneDaten UnterzoneBei(Vector3 szene)
        {
            if (Dienst == null) return null;
            Vector2 w = ZuWelt(szene);
            return Dienst.UnterzoneBei(w.x, w.y);
        }

        public ZoneDaten ZoneBei(Vector3 szene)
        {
            if (Dienst == null) return null;
            Vector2 w = ZuWelt(szene);
            return Dienst.ZoneBei(w.x, w.y);
        }
    }
}
