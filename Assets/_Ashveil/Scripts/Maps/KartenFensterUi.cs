using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ashveil.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Ashveil.Maps
{
    /// <summary>
    /// Karte (Taste M) wie in Classic. Gebietskarte: Aufnahme von oben aus dem gebauten Gebiet (Max 08.10.2026), unentdeckte
    /// Unterzonen liegen unter einem Pergament-Schleier; Namen, Orte und Flugpunkte erscheinen erst nach der Entdeckung.
    /// Weltkarte (Rechtsklick): Pergament mit Handzeichnung (UI-203), unbekannte Gebiete verhüllt; ein Linksklick auf ein bekanntes
    /// Gebiet öffnet seine Gebietskarte. Questgebiete nur als grobe Kreise (UI-202), nie als genauer Punkt. Gruppenmitglieder in
    /// Sichtweite des Netzes als Punkte (UI-201). Bekannt ist ein Gebiet, sobald eine seiner Unterzonen entdeckt ist oder man darin steht.
    ///
    /// Ruckler (CORE-005, Messung 09.10.2026: 55–60 ms beim ersten Öffnen): Weltkarte und Gebietskarte werden nebenbei geladen
    /// (Entpacken auf einem Hilfsfaden), die Gebiete des Weltschleiers auf einem Hilfsfaden gerechnet, und die Schleier werden nur
    /// neu gezeichnet, wenn sich die Entdeckungen geändert haben. Ist etwas beim Öffnen noch nicht fertig, lädt die Karte es wie
    /// bisher selbst.
    /// </summary>
    public sealed class KartenFensterUi : MonoBehaviour
    {
        private const float Breite = 1440f, Hoehe = 940f;
        private const int SchleierWeltBreite = 400, SchleierGebiet = 256;

        private KartenSystem _system;
        private RectTransform _fenster, _flaeche, _bild, _marken, _schliessen;
        private RawImage _bildImg, _schleierImg;
        private TextMeshProUGUI _titel, _hinweis;
        private Texture2D _weltkarte, _schleierWelt, _schleierGebiet;
        private Image _pfeil;
        private readonly BildSpeicher _speicher = new BildSpeicher(4);
        private readonly List<GameObject> _markenListe = new List<GameObject>();
        private readonly List<Image> _gruppenPunkte = new List<Image>();
        private Material _markenMaterial;

        private ZoneDaten _zone;            // angezeigtes Gebiet (null = Weltkarte)
        private bool _welt;
        private readonly float[] _rahmen = new float[4];
        private bool _neuZeichnen;
        private int _revision = -1;

        // Weltschleier: Gebietsindex je Pixel (offene See im Küstensaum → nächstes Land), einmal berechnet
        private int[] _weltGebiet;
        private int _weltB, _weltH;
        private readonly bool[] _bekannt = new bool[256];

        // Vorbereitung nebenbei (CORE-005)
        private readonly List<UnityWebRequest> _anfragen = new List<UnityWebRequest>();
        private readonly HashSet<string> _laedt = new HashSet<string>();
        private Task<int[]> _weltGebietRechnung;
        private bool _weltkarteLaedt;
        // Schleier: nur neu, wenn sich die Entdeckungen geändert haben; gerechnet auf einem Hilfsfaden, solange die Karte zu ist
        private readonly bool[] _weltSchleierBekannt = new bool[256];          // Stand des angewandten Weltschleiers
        private bool _weltSchleierGueltig;
        private ZoneDaten _gebietSchleierZone;                                  // Stand des angewandten Gebietsschleiers
        private int _gebietSchleierStand = int.MinValue;
        private Task<Color32[]> _weltRechnung;
        private readonly bool[] _weltRechnungBekannt = new bool[256];
        private Task<SchleierBild> _gebietRechnung;
        private ZoneDaten _gebietRechnungZone;
        private int _gebietRechnungStand;
        private float _vorbereitenTakt;

        /// <summary>Ergebnis einer Schleier-Rechnung (Hilfsfaden oder Hauptfaden).</summary>
        internal sealed class SchleierBild
        {
            public Color32[] Pixel;
            public int Breite, Hoehe, Verhuellt;
        }

        public bool Offen => _fenster != null && _fenster.gameObject.activeSelf;
        public bool ZeigtWelt => Offen && _welt;
        public ZoneDaten ZeigtGebiet => Offen && !_welt ? _zone : null;
        /// <summary>Bekannte Gebiete (Weltkarte) bzw. entdeckte Unterzonen des Gebiets beim letzten Zeichnen – Tests.</summary>
        public int BekannteGebiete { get; private set; }
        public int VerhuellteUnterzonen { get; private set; }
        public int Beschriftungen => _markenListe.Count;

        /// <summary>Liegt der Mauszeiger über dem offenen Kartenfenster?</summary>
        public bool ZeigerDarueber(Vector2 maus) => Offen && RectTransformUtility.RectangleContainsScreenPoint(_fenster, maus, null);

        public void Build(RectTransform parent, KartenSystem system)
        {
            _system = system;
            RectTransform inner = UiKit.Panel("Karte", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(Breite, Hoehe));
            _fenster = (RectTransform)inner.parent;
            _titel = UiKit.CreateText("Titel", inner, "", 24f, UiKit.TextColor, TextAlignmentOptions.Left);
            Ecke(_titel.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(Breite - 80f, 30f));
            _hinweis = UiKit.CreateText("Hinweis", inner, "", 15f, UiKit.MutedText, TextAlignmentOptions.Right);
            Ecke(_hinweis.rectTransform, new Vector2(1f, 0f), new Vector2(-16f, 8f), new Vector2(Breite - 32f, 22f));

            _schliessen = UiKit.CreateRect("Schließen", inner, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f));
            UiKit.AddImage(_schliessen, UiKit.BorderColor);
            UiKit.AddImage(UiKit.Stretch("Innen", _schliessen, 1.5f), new Color(0.12f, 0.1f, 0.09f, 0.95f));
            TextMeshProUGUI x = UiKit.CreateText("×", _schliessen, "×", 20f, UiKit.TextColor, TextAlignmentOptions.Center);
            RectTransform xr = x.rectTransform; xr.anchorMin = Vector2.zero; xr.anchorMax = Vector2.one; xr.offsetMin = xr.offsetMax = Vector2.zero;

            _flaeche = UiKit.CreateRect("Kartenfläche", inner, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(Breite - 28f, Hoehe - 84f));
            _bild = UiKit.CreateRect("Bild", _flaeche, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, _flaeche.sizeDelta);
            _bildImg = _bild.gameObject.AddComponent<RawImage>(); _bildImg.raycastTarget = false;
            RectTransform nb = UiKit.Stretch("Schleier", _bild);
            _schleierImg = nb.gameObject.AddComponent<RawImage>(); _schleierImg.raycastTarget = false;
            _marken = UiKit.Stretch("Marken", _bild);
            RectTransform pf = UiKit.CreateRect("Spieler", _bild, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f));
            _pfeil = pf.gameObject.AddComponent<Image>(); _pfeil.sprite = KartenGrafik.Pfeil; _pfeil.color = new Color(1f, 0.95f, 0.7f); _pfeil.raycastTarget = false;
            _fenster.gameObject.SetActive(false);
            if (isActiveAndEnabled) StartCoroutine(Vorbereiten());
        }

        /// <summary>
        /// Karte nebenbei vorbereiten, damit das erste Öffnen nicht hängt: die Gebiete des Weltschleiers auf einem Hilfsfaden
        /// rechnen und die Weltkarte laden (Entpacken auf einem Hilfsfaden). Die Gebietskarte lädt <see cref="GebietVorladen"/>.
        /// </summary>
        private IEnumerator Vorbereiten()
        {
            for (int i = 0; i < 20; i++) yield return null;            // erst die Welt ankommen lassen
            ZonenDienst d = _system != null ? _system.Dienst : null;
            if (d == null || d.Daten == null) yield break;
            if (_weltGebiet == null && _weltGebietRechnung == null)
            {
                KartenDatei k = d.Daten;
                int b = SchleierWeltBreite, h = WeltSchleierHoehe(k);
                _weltGebietRechnung = Task.Run(() => WeltGebieteRechnen(d, k, b, h));
            }
            if (_weltkarte == null && !_weltkarteLaedt)
            {
                string pfad = WeltkartenPfad(d);
                if (pfad != null)
                {
                    _weltkarteLaedt = true;
                    yield return KartenGrafik.BildLadenNebenbei(pfad, true, _anfragen, tex =>
                    {
                        if (tex == null) return;                       // dann lädt ZeigeWelt sie selbst
                        if (_weltkarte == null) _weltkarte = tex; else Destroy(tex);
                    });
                    _weltkarteLaedt = false;
                }
            }
            while (_weltGebietRechnung != null && !_weltGebietRechnung.IsCompleted) yield return null;
            WeltGebieteUebernehmen();
        }

        /// <summary>
        /// Gebietskarte nebenbei laden – das Kartensystem ruft das beim Betreten eines Gebiets auf. Öffnet man die Karte, bevor
        /// das Bild fertig ist, lädt <see cref="ZeigeGebiet"/> es selbst (das spätere Ergebnis wird dann verworfen).
        /// </summary>
        public void GebietVorladen(ZoneDaten z)
        {
            if (z == null || _system == null || _system.Dienst == null || !isActiveAndEnabled) return;
            string pfad = KartenGrafik.ErstesBild(Path.Combine(_system.Dienst.Ordner, "gebietskarten"), z.key);
            if (pfad == null || _speicher.Hat(pfad) || !_laedt.Add(pfad)) return;
            StartCoroutine(KartenGrafik.BildLadenNebenbei(pfad, true, _anfragen, tex =>
            {
                _laedt.Remove(pfad);
                _speicher.Ablegen(pfad, tex);
            }));
        }

        /// <summary>Wird gerade ein Bild nebenbei geladen? (Tests)</summary>
        public bool LaedtNebenbei => _laedt.Count > 0 || _weltkarteLaedt;

        private static string WeltkartenPfad(ZonenDienst d) =>
            KartenGrafik.ErstesBild(d.Ordner, "weltkarte_2048") ?? KartenGrafik.ErstesBild(d.Ordner, "weltkarte");

        private static void Ecke(RectTransform rt, Vector2 anker, Vector2 pos, Vector2 groesse)
        {
            rt.anchorMin = rt.anchorMax = anker; rt.pivot = anker; rt.anchoredPosition = pos; rt.sizeDelta = groesse;
        }

        private void OnDestroy()
        {
            foreach (UnityWebRequest u in _anfragen)
            {
                try { u.Abort(); u.Dispose(); }
                catch (System.Exception) { }
            }
            _anfragen.Clear();
            _speicher.Leeren();
            if (_weltkarte != null) Destroy(_weltkarte);
            if (_schleierWelt != null) Destroy(_schleierWelt);
            if (_schleierGebiet != null) Destroy(_schleierGebiet);
            if (_markenMaterial != null) Destroy(_markenMaterial);
        }

        // ---------------------------------------------------------------- Öffnen, Wechseln

        public void Umschalten()
        {
            if (_fenster == null) return;
            if (Offen) { Schliessen(); return; }
            _fenster.gameObject.SetActive(true);
            ZoneDaten z = _system.AktuelleZone;
            if (z != null) ZeigeGebiet(z); else ZeigeWelt();
        }

        public void Schliessen() { if (_fenster != null) _fenster.gameObject.SetActive(false); }

        /// <summary>Neu zeichnen (nach einer Entdeckung, einem Gebietswechsel oder einem neuen Questgebiet).</summary>
        public void Auffrischen() { _neuZeichnen = true; }

        public void ZeigeGebiet(ZoneDaten z)
        {
            if (z == null || z.rahmen == null || z.rahmen.Length < 4) { ZeigeWelt(); return; }
            _welt = false;
            _zone = z;
            System.Array.Copy(z.rahmen, _rahmen, 4);
            string pfad = KartenGrafik.ErstesBild(Path.Combine(_system.Dienst.Ordner, "gebietskarten"), z.key);
            Setze(_speicher.Holen(pfad, true), (_rahmen[2] - _rahmen[0]) / Mathf.Max(_rahmen[3] - _rahmen[1], 1f));
            PvpArt pvp = ZonenDienst.PvpFuer(z, _system.SpielerSeite);
            _titel.text = $"{z.name}   <size=70%><color=#{ColorUtility.ToHtmlStringRGB(KartenGrafik.FarbeFuer(pvp))}>Stufe {z.Stufentext} · {KartenGrafik.TextFuer(pvp)}</color></size>";
            _hinweis.text = "Rechtsklick: Weltkarte   ·   M oder Esc: schließen";
            _neuZeichnen = true;
        }

        public void ZeigeWelt()
        {
            _welt = true;
            _zone = null;
            KartenDatei d = _system.Dienst.Daten;
            _rahmen[0] = 0f; _rahmen[1] = 0f; _rahmen[2] = d.welt_breite_m; _rahmen[3] = d.welt_hoehe_m;
            if (_weltkarte == null)
            {
                string pfad = WeltkartenPfad(_system.Dienst);               // noch nicht nebenbei geladen: jetzt selbst
                _weltkarte = KartenGrafik.BildLaden(pfad, true);            // verkleinert dargestellt: Mipmaps gegen Flimmern
            }
            Setze(_weltkarte, d.welt_breite_m / Mathf.Max(d.welt_hoehe_m, 1f));
            _titel.text = "Welt von Ashveil";
            _hinweis.text = "Linksklick auf ein bekanntes Gebiet: Gebietskarte   ·   M oder Esc: schließen";
            _neuZeichnen = true;
        }

        private void Setze(Texture2D tex, float seitenverhaeltnis)
        {
            _bildImg.texture = tex;
            _bildImg.color = tex != null ? Color.white : new Color(0.15f, 0.2f, 0.24f);
            Vector2 f = _flaeche.sizeDelta;
            float w = f.x, h = f.x / Mathf.Max(seitenverhaeltnis, 0.01f);
            if (h > f.y) { h = f.y; w = h * seitenverhaeltnis; }
            _bild.sizeDelta = new Vector2(w, h);
        }

        // ---------------------------------------------------------------- Laufend

        private void Update()
        {
            if (_system == null || _system.Dienst == null) return;
            if (!Offen) { SchleierVorbereiten(); return; }
            // Esc schließt die Karte über den Fensterstapel (UI-204), das Kartensystem meldet sie dort an
            Vector2 maus = KartenEingabe.Mausposition();
            if (KartenEingabe.LinksklickGedrueckt() && RectTransformUtility.RectangleContainsScreenPoint(_schliessen, maus, null)) { Schliessen(); return; }
            if (RectTransformUtility.RectangleContainsScreenPoint(_bild, maus, null))
            {
                if (!_welt && KartenEingabe.RechtsklickGedrueckt()) ZeigeWelt();
                else if (_welt && KartenEingabe.LinksklickGedrueckt() &&
                         RectTransformUtility.ScreenPointToLocalPointInRectangle(_bild, maus, null, out Vector2 lp))
                {
                    Vector2 w = Welt(lp);
                    ZoneDaten z = _system.Dienst.ZoneBei(w.x, w.y);
                    if (z != null && _system.GebietBekannt(z)) ZeigeGebiet(z);
                }
            }
            if (_revision != KartenEntdeckungen.Revision) _neuZeichnen = true;
            if (_neuZeichnen)
            {
                _neuZeichnen = false;
                _revision = KartenEntdeckungen.Revision;
                Zeichnen();
            }

            // Spielerpfeil
            Vector2 p = _system.SpielerWelt;
            bool da = _system.HatSpieler && Drin(p.x, p.y);
            _pfeil.enabled = da;
            if (da)
            {
                _pfeil.rectTransform.anchoredPosition = Lokal(p.x, p.y);
                _pfeil.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_system.SpielerBlickrichtung);
                _pfeil.rectTransform.SetAsLastSibling();
            }

            // Gruppe (UI-201)
            List<KartenSystem.Mitglied> gruppe = _system.Gruppe;
            int g = 0;
            for (int i = 0; i < gruppe.Count; i++)
            {
                Vector2 w = gruppe[i].Welt;
                if (!Drin(w.x, w.y)) continue;
                Image im = GruppenPunkt(g++);
                im.rectTransform.anchoredPosition = Lokal(w.x, w.y);
            }
            for (; g < _gruppenPunkte.Count; g++)
                if (_gruppenPunkte[g].gameObject.activeSelf) _gruppenPunkte[g].gameObject.SetActive(false);
        }

        private bool Drin(float x, float z) => x >= _rahmen[0] && x <= _rahmen[2] && z >= _rahmen[1] && z <= _rahmen[3];

        private Vector2 Lokal(float x, float z)
        {
            Vector2 s = _bild.sizeDelta;
            float u = (x - _rahmen[0]) / Mathf.Max(_rahmen[2] - _rahmen[0], 1f), v = (z - _rahmen[1]) / Mathf.Max(_rahmen[3] - _rahmen[1], 1f);
            return new Vector2((u - 0.5f) * s.x, (v - 0.5f) * s.y);
        }

        private Vector2 Welt(Vector2 lokal)
        {
            Vector2 s = _bild.sizeDelta;
            float u = lokal.x / Mathf.Max(s.x, 1f) + 0.5f, v = lokal.y / Mathf.Max(s.y, 1f) + 0.5f;
            return new Vector2(_rahmen[0] + u * (_rahmen[2] - _rahmen[0]), _rahmen[1] + v * (_rahmen[3] - _rahmen[1]));
        }

        private Image GruppenPunkt(int k)
        {
            while (_gruppenPunkte.Count <= k)
            {
                RectTransform rt = UiKit.CreateRect("Gruppe " + _gruppenPunkte.Count, _bild, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11f, 11f));
                Image img = rt.gameObject.AddComponent<Image>(); img.sprite = KartenGrafik.Kreis; img.color = KartenGrafik.Gruppe; img.raycastTarget = false;
                _gruppenPunkte.Add(img);
            }
            Image im = _gruppenPunkte[k];
            if (!im.gameObject.activeSelf) im.gameObject.SetActive(true);
            return im;
        }

        // ---------------------------------------------------------------- Schleier und Marken

        private void Zeichnen()
        {
            foreach (GameObject g in _markenListe) if (g != null) Destroy(g);
            _markenListe.Clear();
            ZonenDienst d = _system.Dienst;
            if (_welt)
            {
                WeltSchleier();
                foreach (ZoneDaten z in d.Daten.zonen)
                {
                    if (z == null || !_system.GebietBekannt(z)) continue;
                    float lx = z.name_x, lz = z.name_z;
                    if (lx == 0f && lz == 0f && z.umriss_xz != null && z.umriss_xz.Length >= 2)          // ältere Daten: Mittel des Umrisses
                    {
                        int n = z.umriss_xz.Length / 2;
                        for (int i = 0; i + 1 < z.umriss_xz.Length; i += 2) { lx += z.umriss_xz[i]; lz += z.umriss_xz[i + 1]; }
                        lx /= n; lz /= n;
                    }
                    Color c = KartenGrafik.FarbeFuer(ZonenDienst.PvpFuer(z, _system.SpielerSeite));
                    Text(Lokal(lx, lz), z.stufe_bis > 0 ? $"{z.name}\n<size=75%>{z.Stufentext}</size>" : z.name, 16f, c);
                }
            }
            else if (_zone != null)
            {
                GebietSchleier();
                if (_zone.unterzonen != null)
                    foreach (UnterzoneDaten u in _zone.unterzonen)
                        if (u != null && KartenEntdeckungen.Enthaelt(u.id))
                        {
                            Vector2 np = d.Namenspunkt(u);
                            Text(Lokal(np.x, np.y), u.name, 17f, UiKit.TextColor);
                        }
                if (_zone.orte != null)
                    foreach (OrtDaten o in _zone.orte)
                    {
                        if (!o.Kartenpunkt || !Aufgedeckt(o.x, o.z)) continue;      // Orte erst nach der Entdeckung
                        Punkt(Lokal(o.x, o.z), KartenGrafik.Gold, o.Gross ? 11f : 8f);
                        Text(Lokal(o.x, o.z) + new Vector2(0f, -15f), o.name, 13f, KartenGrafik.Gold);
                    }
                if (_zone.flugpunkte != null)
                    foreach (PunktDaten f in _zone.flugpunkte)
                    {
                        if (!Aufgedeckt(f.x, f.z)) continue;
                        Punkt(Lokal(f.x, f.z), KartenGrafik.Zuflucht, 9f);
                        Text(Lokal(f.x, f.z) + new Vector2(0f, 15f), f.name, 12f, KartenGrafik.Zuflucht);
                    }
            }
            // Questgebiete (UI-202): nur grobe Kreise
            foreach (KartenSystem.Questgebiet q in _system.Questgebiete)
            {
                if (q.X < _rahmen[0] - q.Radius || q.X > _rahmen[2] + q.Radius || q.Z < _rahmen[1] - q.Radius || q.Z > _rahmen[3] + q.Radius) continue;
                float r = Mathf.Max(q.Radius / Mathf.Max(_rahmen[2] - _rahmen[0], 1f) * _bild.sizeDelta.x, 16f);   // auf der 4-km-Karte sonst 10 px (Lesbarkeit)
                Kreis(Lokal(q.X, q.Z), r, q.Farbe);
            }
            // Stufe B (Quest Design Bible §4): nur die Unterzone – ihr Name in der Auftragsfarbe (auch unter dem Schleier, das ist der
            // Hinweis) und ein schwacher Ring um ihre Mitte; kein Kreis um das Ziel
            if (!_welt && _zone != null)
                foreach (KartenSystem.QuestUnterzone qu in _system.QuestUnterzonen)
                {
                    UnterzoneDaten u = d.UnterzoneNachId(qu.Unterzone);
                    if (u == null || d.ZoneDerUnterzone(u.id) != _zone) continue;
                    Vector2 np = d.Namenspunkt(u);
                    if (!KartenEntdeckungen.Enthaelt(u.id)) Text(Lokal(np.x, np.y), u.name, 17f, qu.Farbe);
                    Text(Lokal(np.x, np.y) + new Vector2(0f, -20f), "<size=80%>Auftrag</size>", 15f, qu.Farbe);
                    float r = Mathf.Max(u.radius_m, 120f) / Mathf.Max(_rahmen[2] - _rahmen[0], 1f) * _bild.sizeDelta.x;
                    Ring(Lokal(np.x, np.y), r, new Color(qu.Farbe.r, qu.Farbe.g, qu.Farbe.b, 0.35f));
                }
        }

        private bool Aufgedeckt(float x, float z)
        {
            int id = _system.Dienst.AufdeckungBei(x, z);
            return id <= 0 || KartenEntdeckungen.Enthaelt(id);
        }

        /// <summary>Gebietskarte: unentdeckte Unterzonen dieses Gebiets unter Pergament.</summary>
        private void GebietSchleier()
        {
            int stand = KartenEntdeckungen.Revision;
            if (_schleierGebiet == null || _gebietSchleierZone != _zone || _gebietSchleierStand != stand)
            {
                SchleierBild e = null;
                if (_gebietRechnung != null && _gebietRechnungZone == _zone && _gebietRechnungStand == stand)
                    e = Abholen(ref _gebietRechnung);                   // nebenbei gerechnet (höchstens den Rest abwarten)
                if (e == null) e = GebietSchleierRechnen(_system.Dienst, _zone, _rahmen, new HashSet<int>(KartenEntdeckungen.Alle));
                GebietSchleierAnwenden(e, _zone, stand);
            }
            _schleierImg.texture = _schleierGebiet;
            _schleierImg.enabled = true;
        }

        /// <summary>
        /// Weltkarte: unbekannte Gebiete (und die See vor ihren Küsten) unter Pergament, etwas lichter als auf der Gebietskarte.
        /// Aufgedeckt wird nach den gezeichneten Gebietsgrenzen der Weltdaten (das gebaute Kareth v4 ist größer als das geplante).
        /// </summary>
        private void WeltSchleier()
        {
            if (_weltGebiet == null) WeltGebieteVorbereiten();
            BekannteGebiete = BekannteFuellen();
            if (!_weltSchleierGueltig || !Gleich(_bekannt, _weltSchleierBekannt))
            {
                Color32[] px = null;
                if (_weltRechnung != null && Gleich(_bekannt, _weltRechnungBekannt)) px = Abholen(ref _weltRechnung);
                if (px == null) px = WeltSchleierRechnen(_weltGebiet, _bekannt, _weltB, _weltH);
                WeltSchleierAnwenden(px, _bekannt);
            }
            _schleierImg.texture = _schleierWelt;
            _schleierImg.enabled = true;
        }

        /// <summary>
        /// Solange die Karte zu ist (alle 0,5 s): fertige Rechnungen übernehmen und die Schleier für das aktuelle Gebiet und die
        /// Weltkarte auf einem Hilfsfaden vorrechnen, wenn sich Entdeckungen oder Gebiet geändert haben. Das Öffnen findet sie
        /// dann fertig vor (CORE-005: die Schleier kosteten im fertigen Spiel beim ersten Öffnen am meisten).
        /// </summary>
        private void SchleierVorbereiten()
        {
            if (_gebietRechnung != null && _gebietRechnung.IsCompleted)
            {
                ZoneDaten rz = _gebietRechnungZone;
                int rs = _gebietRechnungStand;
                SchleierBild e = Abholen(ref _gebietRechnung);
                if (e != null && rs == KartenEntdeckungen.Revision) GebietSchleierAnwenden(e, rz, rs);   // veraltet: verwerfen
            }
            if (_weltRechnung != null && _weltRechnung.IsCompleted)
            {
                var rb = (bool[])_weltRechnungBekannt.Clone();
                Color32[] px = Abholen(ref _weltRechnung);
                BekannteFuellen();
                if (px != null && Gleich(rb, _bekannt)) WeltSchleierAnwenden(px, rb);                    // veraltet: verwerfen
            }
            _vorbereitenTakt -= Time.unscaledDeltaTime;
            if (_vorbereitenTakt > 0f) return;
            _vorbereitenTakt = 0.5f;

            ZoneDaten z = _system.AktuelleZone;
            int stand = KartenEntdeckungen.Revision;
            if (_gebietRechnung == null && z != null && z.rahmen != null && z.rahmen.Length >= 4 &&
                (_schleierGebiet == null || _gebietSchleierZone != z || _gebietSchleierStand != stand))
            {
                ZonenDienst d = _system.Dienst;
                float[] rahmen = (float[])z.rahmen.Clone();
                var entdeckt = new HashSet<int>(KartenEntdeckungen.Alle);
                _gebietRechnungZone = z;
                _gebietRechnungStand = stand;
                _gebietRechnung = Task.Run(() => GebietSchleierRechnen(d, z, rahmen, entdeckt));
            }
            if (_weltRechnung == null && _weltGebiet != null && _schleierWelt != null)
            {
                BekannteFuellen();
                if (!_weltSchleierGueltig || !Gleich(_bekannt, _weltSchleierBekannt))
                {
                    System.Array.Copy(_bekannt, _weltRechnungBekannt, _bekannt.Length);
                    var bekannt = (bool[])_bekannt.Clone();
                    int[] gebiet = _weltGebiet;
                    int b = _weltB, h = _weltH;
                    _weltRechnung = Task.Run(() => WeltSchleierRechnen(gebiet, bekannt, b, h));
                }
            }
        }

        /// <summary>Ergebnis einer Rechnung abholen (höchstens den Rest abwarten); null, wenn sie gescheitert ist.</summary>
        private static T Abholen<T>(ref Task<T> rechnung) where T : class
        {
            Task<T> r = rechnung;
            rechnung = null;
            if (r == null) return null;
            try { r.Wait(); }
            catch (System.AggregateException) { return null; }
            return r.Status == TaskStatus.RanToCompletion ? r.Result : null;
        }

        private void GebietSchleierAnwenden(SchleierBild e, ZoneDaten zone, int stand)
        {
            if (_schleierGebiet == null || _schleierGebiet.width != e.Breite || _schleierGebiet.height != e.Hoehe)
            {
                if (_schleierGebiet != null) Destroy(_schleierGebiet);
                _schleierGebiet = new Texture2D(e.Breite, e.Hoehe, TextureFormat.RGBA32, false) { name = "Kartenschleier Gebiet", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            }
            _schleierGebiet.SetPixels32(e.Pixel);
            _schleierGebiet.Apply(false);
            _gebietSchleierZone = zone;
            _gebietSchleierStand = stand;
            VerhuellteUnterzonen = e.Verhuellt;
        }

        private void WeltSchleierAnwenden(Color32[] px, bool[] bekannt)
        {
            _schleierWelt.SetPixels32(px);
            _schleierWelt.Apply(false);
            System.Array.Copy(bekannt, _weltSchleierBekannt, _weltSchleierBekannt.Length);
            _weltSchleierGueltig = true;
        }

        /// <summary>Bekannte Gebiete nach Index in <see cref="_bekannt"/> eintragen; gibt die Anzahl zurück.</summary>
        private int BekannteFuellen()
        {
            System.Array.Clear(_bekannt, 0, _bekannt.Length);
            int bekannt = 0;
            foreach (ZoneDaten z in _system.Dienst.Daten.zonen)
                if (z != null && z.index > 0 && z.index < _bekannt.Length && _system.GebietBekannt(z)) { _bekannt[z.index] = true; bekannt++; }
            return bekannt;
        }

        private static bool Gleich(bool[] a, bool[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// Gebietsschleier (unentdeckte Unterzonen des Gebiets) als Bild; Zeile 0 = Süden. Reine Rechnung über die Rasterdaten und
        /// eine Kopie der Entdeckungen – läuft auch auf einem Hilfsfaden.
        /// </summary>
        internal static SchleierBild GebietSchleierRechnen(ZonenDienst d, ZoneDaten zone, float[] rahmen, HashSet<int> entdeckt)
        {
            float sv = (rahmen[2] - rahmen[0]) / Mathf.Max(rahmen[3] - rahmen[1], 1f);
            int b = sv >= 1f ? SchleierGebiet : Mathf.Max(8, Mathf.RoundToInt(SchleierGebiet * sv));
            int h = sv >= 1f ? Mathf.Max(8, Mathf.RoundToInt(SchleierGebiet / sv)) : SchleierGebiet;
            var deckung = new float[b * h];
            var ids = new HashSet<int>();
            float x0 = rahmen[0], z0 = rahmen[1], dx = (rahmen[2] - rahmen[0]) / b, dz = (rahmen[3] - rahmen[1]) / h;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < b; i++)
                {
                    int id = d.AufdeckungBei(x0 + (i + 0.5f) * dx, z0 + (j + 0.5f) * dz);
                    bool zu = id > 0 && !entdeckt.Contains(id) && d.ZoneDerUnterzone(id) == zone;
                    deckung[j * b + i] = zu ? 1f : 0f;
                    if (zu) ids.Add(id);
                }
            var px = new Color32[b * h];
            KartenGrafik.Schleier(deckung, b, h, px, 2, 5.1f);
            return new SchleierBild { Pixel = px, Breite = b, Hoehe = h, Verhuellt = ids.Count };
        }

        /// <summary>Weltschleier (unbekannte Gebiete und ihre Küstensäume) als Bild. Reine Rechnung – läuft auch auf einem Hilfsfaden.</summary>
        internal static Color32[] WeltSchleierRechnen(int[] gebiet, bool[] bekannt, int b, int h)
        {
            int n = b * h;
            var deckung = new float[n];
            for (int k = 0; k < n; k++)
            {
                int g = gebiet[k];
                deckung[k] = g > 0 && g < bekannt.Length && bekannt[g] ? 0f : 1f;
            }
            var px = new Color32[n];
            KartenGrafik.Schleier(deckung, b, h, px, 3, 17.3f, 0.8f);
            return px;
        }

        /// <summary>Gebiet je Schleierpixel; offene See bis zum Küstensaum (kueste_m) gehört zum nächsten Land.</summary>
        private void WeltGebieteVorbereiten()
        {
            WeltGebieteUebernehmen();                                  // nebenbei schon gerechnet?
            if (_weltGebiet != null) return;
            ZonenDienst d = _system.Dienst;
            SetzeWeltGebiet(WeltGebieteRechnen(d, d.Daten, SchleierWeltBreite, WeltSchleierHoehe(d.Daten)));
        }

        /// <summary>Ergebnis der Rechnung auf dem Hilfsfaden übernehmen (höchstens den Rest abwarten).</summary>
        private void WeltGebieteUebernehmen()
        {
            if (_weltGebiet != null || _weltGebietRechnung == null) return;
            Task<int[]> r = _weltGebietRechnung;
            _weltGebietRechnung = null;
            try { r.Wait(); }
            catch (System.AggregateException) { return; }               // dann rechnet WeltGebieteVorbereiten selbst
            if (r.Status == TaskStatus.RanToCompletion && r.Result != null) SetzeWeltGebiet(r.Result);
        }

        private void SetzeWeltGebiet(int[] gebiet)
        {
            _weltB = SchleierWeltBreite;
            _weltH = WeltSchleierHoehe(_system.Dienst.Daten);
            _weltGebiet = gebiet;
            if (_schleierWelt == null)
                _schleierWelt = new Texture2D(_weltB, _weltH, TextureFormat.RGBA32, false) { name = "Kartenschleier Welt", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        }

        private static int WeltSchleierHoehe(KartenDatei k) =>
            Mathf.Max(8, Mathf.RoundToInt(SchleierWeltBreite * k.welt_hoehe_m / Mathf.Max(k.welt_breite_m, 1f)));

        /// <summary>
        /// Gebietsindex je Pixel des Weltschleiers (b × h, Zeile 0 = Süden); die offene See bis zum Küstensaum gehört zum
        /// nächsten Land. Reine Rechnung über die Rasterdaten, ohne Unity-Objekte – läuft auch auf einem Hilfsfaden.
        /// </summary>
        internal static int[] WeltGebieteRechnen(ZonenDienst d, KartenDatei k, int b, int h)
        {
            int n = b * h;
            var gebiet = new int[n];
            var abstand = new int[n];
            var reihe = new Queue<int>();
            float dx = k.welt_breite_m / b, dz = k.welt_hoehe_m / h;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < b; i++)
                {
                    int p = j * b + i;
                    int g = d.WeltZonenIndexBei((i + 0.5f) * dx, (j + 0.5f) * dz);   // gezeichnete (geplante) Grenzen, nicht das Szenenrechteck
                    gebiet[p] = g;
                    abstand[p] = g > 0 ? 0 : int.MaxValue;
                    if (g > 0) reihe.Enqueue(p);
                }
            int saum = Mathf.Max(1, Mathf.CeilToInt((k.kueste_m > 0f ? k.kueste_m : 1200f) / dx));
            while (reihe.Count > 0)
            {
                int p = reihe.Dequeue();
                int a = abstand[p];
                if (a >= saum) continue;
                int i = p % b, j = p / b;
                for (int r = 0; r < 4; r++)
                {
                    int ni = i + (r == 0 ? 1 : r == 1 ? -1 : 0), nj = j + (r == 2 ? 1 : r == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= b || nj >= h) continue;
                    int q = nj * b + ni;
                    if (abstand[q] <= a + 1) continue;
                    abstand[q] = a + 1;
                    gebiet[q] = gebiet[p];
                    reihe.Enqueue(q);
                }
            }
            return gebiet;
        }

        /// <summary>One owned material for the small labels on map/parchment; global UI/font assets stay unchanged.</summary>
        private Material MarkenMaterial
        {
            get
            {
                if (_markenMaterial != null || UiKit.TextMaterial == null) return _markenMaterial;
                _markenMaterial = new Material(UiKit.TextMaterial) { name = "Kartenbeschriftung (Kontrast)" };
                _markenMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.28f);
                _markenMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.95f));
                if (_markenMaterial.HasProperty(ShaderUtilities.ID_UnderlayColor))
                {
                    _markenMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.75f));
                    _markenMaterial.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.1f);
                }
                return _markenMaterial;
            }
        }

        private void Text(Vector2 pos, string text, float groesse, Color farbe)
        {
            TextMeshProUGUI t = UiKit.CreateText("Marke", _marken, text, groesse, farbe, TextAlignmentOptions.Center);
            if (MarkenMaterial != null) t.fontSharedMaterial = MarkenMaterial;
            RectTransform rt = t.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(260f, 44f);
            _markenListe.Add(t.gameObject);
        }

        private void Punkt(Vector2 pos, Color farbe, float groesse)
        {
            RectTransform rt = UiKit.CreateRect("Ort", _marken, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(groesse, groesse));
            Image i = rt.gameObject.AddComponent<Image>(); i.sprite = KartenGrafik.Kreis; i.color = farbe; i.raycastTarget = false;
            _markenListe.Add(rt.gameObject);
        }

        private void Ring(Vector2 pos, float radius, Color farbe)
        {
            RectTransform rt = UiKit.CreateRect("Auftragsregion", _marken, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(radius * 2f, radius * 2f));
            Image ri = rt.gameObject.AddComponent<Image>(); ri.sprite = KartenGrafik.Ring; ri.color = farbe; ri.raycastTarget = false;
            _markenListe.Add(rt.gameObject);
        }

        private void Kreis(Vector2 pos, float radius, Color farbe)
        {
            RectTransform rt = UiKit.CreateRect("Questgebiet", _marken, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(radius * 2f, radius * 2f));
            Image f = rt.gameObject.AddComponent<Image>(); f.sprite = KartenGrafik.Kreis; f.color = new Color(farbe.r, farbe.g, farbe.b, 0.18f); f.raycastTarget = false;
            RectTransform rr = UiKit.Stretch("Rand", rt);
            Image ri = rr.gameObject.AddComponent<Image>(); ri.sprite = KartenGrafik.Ring; ri.color = new Color(farbe.r, farbe.g, farbe.b, 0.75f); ri.raycastTarget = false;
            _markenListe.Add(rt.gameObject);
        }
    }
}
