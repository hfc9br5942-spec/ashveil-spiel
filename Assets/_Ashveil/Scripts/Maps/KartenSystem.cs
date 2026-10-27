using System.Collections.Generic;
using Ashveil.Core.Events;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Social;
using Ashveil.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Maps
{
    /// <summary>
    /// ASHVEIL – Karten im Spiel (UI-201, UI-202, UI-203; Spieltest r2, Max 08.10.2026): Zonenbanner, Minimap oben rechts und
    /// Karte (Taste M) mit Gebiets- und Weltkarte. Nur Anzeige: Die Daten liefert <see cref="KartenBezug"/> der Szene, die
    /// Entdeckungen bestätigt der Server (<see cref="KartenEntdeckungen"/>, PROG-013).
    ///
    /// Folgt der eigenen Spielfigur (<see cref="LocalPlayerSpawnedEvent"/>). Ohne Grafik (dedizierter Server) baut es keine Oberfläche.
    /// Gruppenmitglieder in Sichtweite des Netzes erscheinen auf Minimap und Karte (UI-201), Gegner nie.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-450)]      // nach den Fenstern (InventoryUi −500 setzt UiPointer.OverUi), vor der Spielsteuerung
    public sealed class KartenSystem : MonoBehaviour
    {
        public static KartenSystem Instanz { get; private set; }

        [Tooltip("Seite der Spielfigur (A oder B) für die PvP-Farben, bis der Charakter sie liefert")]
        [SerializeField] private string spielerSeite = "A";
        [SerializeField] private bool zonenbanner = true;
        [SerializeField] private bool minimap = true;

        public struct Mitglied
        {
            public string Name;
            public Vector2 Welt;
        }

        public struct Questgebiet
        {
            public string Id, Text;
            public float X, Z, Radius;
            public Color Farbe;
        }

        /// <summary>Stufe B (Quest Design Bible §4): ein Auftrag zeigt nur seine Unterzone – Name und schwacher Ring auf der Gebietskarte.</summary>
        public struct QuestUnterzone
        {
            public string Id;
            public int Unterzone;
            public Color Farbe;
        }

        private KartenBezug _bezug;
        private ZonenBannerUi _banner;
        private MinimapUi _minimap;
        private KartenFensterUi _fenster;
        private QuestFuehrung _fuehrung;
        private float _takt, _gruppeTakt;
        private int _zonenIndex = -1;
        private readonly List<ZoneDaten> _reichweite = new List<ZoneDaten>();
        private readonly Dictionary<string, Questgebiet> _quest = new Dictionary<string, Questgebiet>();
        private readonly Dictionary<string, QuestUnterzone> _questUnterzonen = new Dictionary<string, QuestUnterzone>();
        private readonly List<Mitglied> _gruppe = new List<Mitglied>();
        private readonly List<PlayerIdentity> _figuren = new List<PlayerIdentity>();
        private readonly HashSet<string> _gruppenNamen = new HashSet<string>();

        public ZonenDienst Dienst => _bezug != null ? _bezug.Dienst : null;
        public KartenBezug Bezug => _bezug;
        public ZoneDaten AktuelleZone { get; private set; }
        public UnterzoneDaten AktuelleUnterzone { get; private set; }
        public string SpielerSeite { get => spielerSeite; set => spielerSeite = value == "B" ? "B" : "A"; }
        public ZonenBannerUi Banner => _banner;
        public MinimapUi Minimap => _minimap;
        public KartenFensterUi Fenster => _fenster;
        public QuestFuehrung Fuehrung => _fuehrung;

        /// <summary>Spielfigur, der die Karten folgen (die eigene, vom Netz gemeldet).</summary>
        public Transform Ziel { get; set; }
        public bool HatSpieler => Ziel != null;
        /// <summary>Position der Spielfigur in Welt-m (x Ost, y = z Nord).</summary>
        public Vector2 SpielerWelt { get; private set; }
        /// <summary>Blickrichtung in Grad (0 = Norden, 90 = Osten).</summary>
        public float SpielerBlickrichtung { get; private set; }
        /// <summary>Gruppenmitglieder in Sichtweite des Netzes (ohne die eigene Figur), alle 0,5 s neu.</summary>
        public List<Mitglied> Gruppe => _gruppe;
        public IEnumerable<Questgebiet> Questgebiete => _quest.Values;
        public int AnzahlQuestgebiete => _quest.Count;
        public IEnumerable<QuestUnterzone> QuestUnterzonen => _questUnterzonen.Values;
        public int AnzahlQuestUnterzonen => _questUnterzonen.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Zuruecksetzen() => Instanz = null;   // Play ohne Neuladen der Skripte

        // ---------------------------------------------------------------- Lebenszyklus

        private void Awake()
        {
            if (Instanz != null && Instanz != this) { Destroy(this); return; }
            Instanz = this;
        }

        private void OnEnable()
        {
            EventBus<LocalPlayerSpawnedEvent>.Subscribe(OnSpieler);
            EventBus<KartenEntdeckungEvent>.Subscribe(OnEntdeckung);
        }

        private void OnDisable()
        {
            EventBus<LocalPlayerSpawnedEvent>.Unsubscribe(OnSpieler);
            EventBus<KartenEntdeckungEvent>.Unsubscribe(OnEntdeckung);
        }

        private void OnDestroy()
        {
            if (Instanz == this) Instanz = null;
        }

        private void OnSpieler(LocalPlayerSpawnedEvent e)
        {
            if (e.Player != null) Ziel = e.Player.transform;
            _zonenIndex = -1;
            AktuelleUnterzone = null;
            _takt = 0f;
        }

        private void OnEntdeckung(KartenEntdeckungEvent e)
        {
            if (_fenster != null) _fenster.Auffrischen();
            if (e.Xp > 0 && _banner != null)
                _banner.ZeigeEntdeckt(e.Name, e.Xp, KartenGrafik.FarbeFuer(ZonenDienst.PvpFuer(AktuelleZone, SpielerSeite)));
        }

        private void Start()
        {
            _bezug = GetComponent<KartenBezug>();
            if (_bezug == null) _bezug = KartenBezug.Instanz;
            if (Dienst == null) { Debug.LogWarning("[Karten] Kein Kartenbezug mit Daten in der Szene – Karten aus."); return; }
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;   // Server ohne Grafik
            if (UiKit.Font == null) { Debug.LogWarning("[Karten] Keine TextMeshPro-Schrift – Karten ohne Oberfläche."); return; }
            Canvas canvas = UiKit.CreateCanvas("Karten Canvas", transform, 12);
            var root = (RectTransform)canvas.transform;
            if (zonenbanner) { _banner = gameObject.AddComponent<ZonenBannerUi>(); _banner.Build(root); }
            if (minimap) { _minimap = gameObject.AddComponent<MinimapUi>(); _minimap.Build(root, this); }
            // Die Karte liegt über dem übrigen HUD (eigene Ebene)
            Canvas oben = UiKit.CreateCanvas("Karte Canvas", transform, 30);
            _fenster = gameObject.AddComponent<KartenFensterUi>();
            _fenster.Build((RectTransform)oben.transform, this);
            UiFensterstapel.Anmelden(_fenster, "Karte", () => _fenster != null && _fenster.Offen, () => _fenster.Schliessen());
            // Auftragsführung (A/B/C, Max 10.10.2026): hält Kreise und Unterzonen auf dem Stand der eigenen Aufträge
            _fuehrung = gameObject.AddComponent<QuestFuehrung>();
            _fuehrung.Init(this);
        }

        private void Update()
        {
            if (Dienst == null) return;
            bool gesperrt = KartenEingabe.Gesperrt();
            if (_fenster != null)
            {
                if (gesperrt) { if (_fenster.Offen) _fenster.Schliessen(); }
                else if (!KartenEingabe.TextfeldAktiv() && HatSpieler && KartenEingabe.KarteGedrueckt()) _fenster.Umschalten();
            }
            // Klicks und Mausrad über Karte oder Minimap gehören der Oberfläche (kein Zielen, kein Kameradrehen, kein Kamerazoom)
            Vector2 maus = KartenEingabe.Mausposition();
            if (!gesperrt && ((_fenster != null && _fenster.ZeigerDarueber(maus)) || (_minimap != null && _minimap.ZeigerDarueber(maus))))
                UiPointer.OverUi = true;

            Transform t = Ziel;
            if (t == null) return;
            Vector3 p = t.position;
            SpielerWelt = _bezug.ZuWelt(p);
            SpielerBlickrichtung = t.eulerAngles.y;

            _gruppeTakt -= Time.unscaledDeltaTime;
            if (_gruppeTakt <= 0f)
            {
                _gruppeTakt = 0.5f;
                GruppeAuffrischen();
            }

            _takt -= Time.unscaledDeltaTime;
            if (_takt > 0f || gesperrt) return;   // gesperrt (Charakter-Editor): Banner erst danach
            _takt = 0.2f;
            Pruefen();
        }

        // ---------------------------------------------------------------- Gebiet und Unterzone (nur Anzeige)

        private void Pruefen()
        {
            Vector2 w = SpielerWelt;
            int idx = Dienst.ZonenIndexBei(w.x, w.y);
            ZoneDaten zone = Dienst.ZoneNachIndex(idx);
            PvpArt pvp = ZonenDienst.PvpFuer(zone, SpielerSeite);
            UnterzoneDaten u = Dienst.UnterzoneBei(w.x, w.y);
            bool zoneNeu = idx != _zonenIndex, unterNeu = u != AktuelleUnterzone;
            if (!zoneNeu && !unterNeu) return;

            ZoneDaten vorher = AktuelleZone;
            _zonenIndex = idx;
            AktuelleZone = zone;
            AktuelleUnterzone = u;

            // Banner: neues Gebiet groß (mit Unterzone und PvP-Status), sonst nur die Unterzone
            if (_banner != null)
            {
                if (zoneNeu) _banner.ZeigeZone(zone, u, pvp, Dienst.Daten.meer_name);
                else if (u != null) _banner.ZeigeUnterzone(u, pvp);
            }
            if (_minimap != null)
                _minimap.ZeigeOrt(u != null ? u.name : zone != null ? zone.name : Dienst.Daten.meer_name, KartenGrafik.FarbeFuer(pvp));
            if (zoneNeu && _fenster != null)
            {
                _fenster.Auffrischen();
                _fenster.GebietVorladen(zone);      // Gebietskarte nebenbei laden – das erste Öffnen (M) hängt dann nicht
            }

            if (zoneNeu) EventBus<ZoneBetretenEvent>.Raise(new ZoneBetretenEvent(zone, vorher, pvp));
            if (unterNeu && u != null) EventBus<UnterzoneBetretenEvent>.Raise(new UnterzoneBetretenEvent(u, zone));
        }

        /// <summary>Gebiete, deren Kartenrahmen den Kreis berührt (für Marken in der Minimap). Liste wird wiederverwendet.</summary>
        public List<ZoneDaten> ZonenInReichweite(Vector2 p, float r)
        {
            _reichweite.Clear();
            if (Dienst == null) return _reichweite;
            ZoneDaten[] alle = Dienst.Daten.zonen;                   // Feld statt IEnumerable: keine Allokation je Bild
            for (int i = 0; i < alle.Length; i++)
            {
                ZoneDaten z = alle[i];
                if (z == null) continue;
                float[] b = z.rahmen;
                if (b == null || b.Length < 4) continue;
                if (p.x + r < b[0] || p.x - r > b[2] || p.y + r < b[1] || p.y - r > b[3]) continue;
                _reichweite.Add(z);
            }
            return _reichweite;
        }

        /// <summary>Ist ein Gebiet bekannt? (Eine seiner Unterzonen entdeckt oder man steht darin.)</summary>
        public bool GebietBekannt(ZoneDaten z)
        {
            if (z == null) return false;
            if (z == AktuelleZone) return true;
            if (z.unterzonen == null) return false;
            foreach (UnterzoneDaten u in z.unterzonen)
                if (u != null && KartenEntdeckungen.Enthaelt(u.id)) return true;
            return false;
        }

        // ---------------------------------------------------------------- Gruppe (UI-201)

        private void GruppeAuffrischen()
        {
            _gruppe.Clear();
            ISocialService social = SocialClient.Current;
            if (social == null || !social.Ready || social.Party == null || social.Party.Count == 0) return;
            _gruppenNamen.Clear();
            foreach (PartyMemberView m in social.Party)
                if (m != null && !m.Self && m.Online && !string.IsNullOrEmpty(m.Name)) _gruppenNamen.Add(m.Name);
            if (_gruppenNamen.Count == 0) return;
            _figuren.Clear();
            _figuren.AddRange(FindObjectsByType<PlayerIdentity>());   // 2 × je Sekunde, nur mit Gruppe
            foreach (PlayerIdentity f in _figuren)
                if (f != null && !f.IsLocal && _gruppenNamen.Contains(f.DisplayName))
                    _gruppe.Add(new Mitglied { Name = f.DisplayName, Welt = _bezug.ZuWelt(f.transform.position) });
        }

        // ---------------------------------------------------------------- Questgebiete (UI-202: nur grob, kein Punkt)

        public void ZeigeQuestgebiet(string id, float x, float z, float radius, Color farbe, string text = null)
        {
            if (string.IsNullOrEmpty(id)) return;
            _quest[id] = new Questgebiet { Id = id, X = x, Z = z, Radius = Mathf.Max(radius, 60f), Farbe = farbe, Text = text };
            if (_fenster != null) _fenster.Auffrischen();
        }

        public void EntferneQuestgebiet(string id)
        {
            if (id != null && _quest.Remove(id) && _fenster != null) _fenster.Auffrischen();
        }

        /// <summary>Stufe B: nur die Unterzone (Gebietskarte: Name in der Auftragsfarbe und schwacher Ring; nicht auf der Minimap).</summary>
        public void ZeigeQuestUnterzone(string id, int unterzone, Color farbe)
        {
            if (string.IsNullOrEmpty(id) || unterzone <= 0) return;
            _questUnterzonen[id] = new QuestUnterzone { Id = id, Unterzone = unterzone, Farbe = farbe };
            if (_fenster != null) _fenster.Auffrischen();
        }

        public void EntferneQuestUnterzone(string id)
        {
            if (id != null && _questUnterzonen.Remove(id) && _fenster != null) _fenster.Auffrischen();
        }
    }
}
