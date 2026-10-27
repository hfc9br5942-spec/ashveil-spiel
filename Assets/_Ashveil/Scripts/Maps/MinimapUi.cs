using System.Collections.Generic;
using System.IO;
using Ashveil.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.Maps
{
    /// <summary>
    /// Minimap wie in Classic (UI-201): rund, oben rechts, Norden oben, der Pfeil dreht sich mit der Figur. Zeigt das Gelände
    /// aus der Aufnahme von oben (Kacheln 1 024 m, 2 m je Pixel), Orte, Flugpunkte und Gruppenmitglieder in der Nähe; keine Gegner.
    /// Darüber der Name der Unterzone (sonst des Gebiets) in der PvP-Farbe. Zoom: Mausrad über der Minimap oder die Knöpfe + und −.
    /// Belegt oben rechts <see cref="HudPlatz.ObenRechts"/> – die Auftragsliste rückt darunter.
    /// </summary>
    public sealed class MinimapUi : MonoBehaviour
    {
        private const float Durchmesser = 200f, Rand = 20f, Kopf = 24f;
        /// <summary>Meter quer über die Minimap (Zoomstufen, 0 = am nächsten).</summary>
        public static readonly float[] Sichtweiten = { 360f, 600f, 1000f };

        private KartenSystem _system;
        private RectTransform _root, _rund, _inhalt, _kachelEbene, _kreisEbene, _punktEbene, _pfeil, _plus, _minus;
        private TextMeshProUGUI _ortName;
        private readonly List<RawImage> _kacheln = new List<RawImage>();
        private readonly List<long> _belegung = new List<long>();
        private readonly List<Image> _punkte = new List<Image>();
        private readonly List<RectTransform> _kreise = new List<RectTransform>();   // Zielkreise der Aufträge (UI-205)
        private readonly BildSpeicher _speicher = new BildSpeicher(16);      // höchstens 3 × 3 sichtbar; Kareth hat 25 Kacheln
        private int _zoom = 1;

        public int Zoomstufe => _zoom;
        public float Sichtweite => Sichtweiten[_zoom];
        public string Ortsname => _ortName != null ? _ortName.text : "";
        /// <summary>Gezeichnete Punkte im letzten Bild (Orte, Flugpunkte, Gruppe) – Tests.</summary>
        public int Punkte { get; private set; }
        /// <summary>Zielkreise im Bild (ganz oder angeschnitten) und Randmarker für Ziele außerhalb – Tests.</summary>
        public int Kreise { get; private set; }
        public int Randmarker { get; private set; }
        public int SichtbareKacheln { get; private set; }

        public void Build(RectTransform parent, KartenSystem system)
        {
            _system = system;
            _root = UiKit.CreateRect("Minimap", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -Rand), new Vector2(Durchmesser + 20f, Durchmesser + Kopf + Rand));
            HudPlatz.ObenRechts = Rand + Durchmesser + Kopf + Rand;
            _ortName = UiKit.CreateText("Ort", _root, "", 17f, UiKit.TextColor, TextAlignmentOptions.Center);
            RectTransform on = _ortName.rectTransform;
            on.anchorMin = new Vector2(0f, 1f); on.anchorMax = new Vector2(1f, 1f); on.pivot = new Vector2(0.5f, 1f);
            on.anchoredPosition = Vector2.zero; on.sizeDelta = new Vector2(60f, Kopf);

            _rund = UiKit.CreateRect("Rund", _root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(Durchmesser, Durchmesser));
            Image maske = _rund.gameObject.AddComponent<Image>();
            maske.sprite = KartenGrafik.Kreis; maske.color = new Color(0.12f, 0.2f, 0.26f, 1f); maske.raycastTarget = false;
            Mask m = _rund.gameObject.AddComponent<Mask>(); m.showMaskGraphic = true;
            _inhalt = UiKit.CreateRect("Inhalt", _rund, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _kachelEbene = UiKit.CreateRect("Kacheln", _inhalt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _kreisEbene = UiKit.CreateRect("Zielkreise", _inhalt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _punktEbene = UiKit.CreateRect("Punkte", _inhalt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            RectTransform ring = UiKit.CreateRect("Rahmen", _root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(Durchmesser + 8f, Durchmesser + 8f));
            Image ri = ring.gameObject.AddComponent<Image>(); ri.sprite = KartenGrafik.Ring; ri.color = UiKit.BorderColor; ri.raycastTarget = false;
            TextMeshProUGUI nord = UiKit.CreateText("N", ring, "N", 15f, UiKit.TextColor, TextAlignmentOptions.Center);
            RectTransform nr = nord.rectTransform; nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 1f); nr.pivot = new Vector2(0.5f, 0.5f);
            nr.anchoredPosition = new Vector2(0f, -2f); nr.sizeDelta = new Vector2(20f, 20f);

            _pfeil = UiKit.CreateRect("Spieler", _rund, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            Image pi = _pfeil.gameObject.AddComponent<Image>(); pi.sprite = KartenGrafik.Pfeil; pi.color = new Color(1f, 0.95f, 0.75f); pi.raycastTarget = false;

            // Zoomknöpfe rechts unten am Ring (wie Classic)
            _plus = Knopf("Zoom +", "+", new Vector2(-2f, 40f));
            _minus = Knopf("Zoom −", "−", new Vector2(-2f, 12f));
        }

        private RectTransform Knopf(string name, string zeichen, Vector2 pos)
        {
            RectTransform rt = UiKit.CreateRect(name, _root, new Vector2(1f, 0f), new Vector2(1f, 0f), pos, new Vector2(24f, 24f));
            Image rand = rt.gameObject.AddComponent<Image>(); rand.sprite = KartenGrafik.Kreis; rand.color = UiKit.BorderColor; rand.raycastTarget = false;
            RectTransform innen = UiKit.CreateRect("Innen", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
            Image ii = innen.gameObject.AddComponent<Image>(); ii.sprite = KartenGrafik.Kreis; ii.color = new Color(0.1f, 0.09f, 0.08f, 0.95f); ii.raycastTarget = false;
            TextMeshProUGUI t = UiKit.CreateText("Zeichen", rt, zeichen, 18f, UiKit.TextColor, TextAlignmentOptions.Center);
            RectTransform tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Liegt der Mauszeiger über der Minimap oder ihren Knöpfen? (Klicks und Mausrad gehören dann der Oberfläche.)</summary>
        public bool ZeigerDarueber(Vector2 maus) =>
            _rund != null && _rund.gameObject.activeInHierarchy &&
            (RectTransformUtility.RectangleContainsScreenPoint(_rund, maus, null) || Ueber(_plus, maus) || Ueber(_minus, maus));

        private static bool Ueber(RectTransform rt, Vector2 maus) => rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, maus, null);

        /// <summary>Zoom: +1 = näher, −1 = weiter.</summary>
        public void Zoomen(int richtung)
        {
            if (richtung > 0) _zoom = Mathf.Max(_zoom - 1, 0);
            else if (richtung < 0) _zoom = Mathf.Min(_zoom + 1, Sichtweiten.Length - 1);
        }

        public void ZeigeOrt(string name, Color farbe)
        {
            if (_ortName == null) return;
            _ortName.text = name;
            _ortName.color = farbe;
        }

        private void OnDestroy()
        {
            _speicher.Leeren();
            HudPlatz.ObenRechts = 0f;
        }

        private void LateUpdate()
        {
            if (_system == null || _system.Dienst == null || _rund == null) return;
            bool an = _system.HatSpieler && !KartenEingabe.Gesperrt();
            if (_root.gameObject.activeSelf != an) _root.gameObject.SetActive(an);
            HudPlatz.ObenRechts = an ? Rand + Durchmesser + Kopf + Rand : 0f;
            if (!an) return;

            // Zoom: Mausrad über der Minimap oder die Knöpfe (nicht unter dem Spielmenü, UI-204)
            Vector2 maus = KartenEingabe.Mausposition();
            if (!KartenEingabe.ModalOffen())
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_rund, maus, null))
                {
                    float rad = KartenEingabe.Mausrad();
                    if (rad > 0.01f) Zoomen(1);
                    else if (rad < -0.01f) Zoomen(-1);
                }
                if (KartenEingabe.LinksklickGedrueckt())
                {
                    if (Ueber(_plus, maus)) Zoomen(1);
                    else if (Ueber(_minus, maus)) Zoomen(-1);
                }
            }

            Vector2 p = _system.SpielerWelt;
            float sicht = Sichtweiten[_zoom], s = Durchmesser / sicht, r = sicht * 0.5f;
            KartenDatei d = _system.Dienst.Daten;
            float T = d.minimap_kachel_m > 0f ? d.minimap_kachel_m : 1024f;
            int i0 = Mathf.FloorToInt((p.x - r) / T), i1 = Mathf.FloorToInt((p.x + r) / T);
            int j0 = Mathf.FloorToInt((p.y - r) / T), j1 = Mathf.FloorToInt((p.y + r) / T);
            int k = 0;
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    int slot = k++;
                    RawImage img = Kachel(slot);
                    long schluessel = ((long)i << 32) | (uint)j;
                    if (_belegung[slot] != schluessel)                       // Bild nur beim Wechsel der Kachel holen
                    {
                        _belegung[slot] = schluessel;
                        Texture2D tex = _system.Dienst.HatMinimapKachel(i, j) ? _speicher.Holen(Path.Combine(_system.Dienst.Ordner, "minimap", $"m_{i}_{j}.jpg"), true) : null;
                        img.texture = tex;
                        img.color = tex != null ? Color.white : new Color(0.16f, 0.27f, 0.36f, 1f);   // keine Kachel = Meer
                    }
                    RectTransform rt = img.rectTransform;
                    rt.anchoredPosition = new Vector2((i * T + T * 0.5f - p.x) * s, (j * T + T * 0.5f - p.y) * s);
                    rt.sizeDelta = new Vector2(T * s + 1f, T * s + 1f);
                    if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                }
            SichtbareKacheln = k;
            for (; k < _kacheln.Count; k++)                     // verborgene Plätze vergessen ihr Bild (der Speicher darf es verdrängen)
            {
                if (_kacheln[k].gameObject.activeSelf) _kacheln[k].gameObject.SetActive(false);
                _kacheln[k].texture = null;
                _belegung[k] = long.MinValue;
            }

            // Zielkreise der Aufträge (UI-205, Max 10.10.2026: auch auf der Minimap). Liegt der Kreis ganz außerhalb, zeigt ein
            // Randmarker am Ring die Richtung – weich, kein Pfeil, kein Punkt auf dem Ziel (UI-202).
            int kk = 0, rand = 0;
            float randR = Durchmesser * 0.5f - 7f;
            foreach (KartenSystem.Questgebiet qg in _system.Questgebiete)
            {
                float dx = qg.X - p.x, dz = qg.Z - p.y, abst = Mathf.Sqrt(dx * dx + dz * dz);
                if (abst - qg.Radius < r)
                {
                    RectTransform kr = Zielkreis(kk++);
                    kr.anchoredPosition = new Vector2(dx * s, dz * s);
                    kr.sizeDelta = new Vector2(qg.Radius * 2f * s, qg.Radius * 2f * s);
                    kr.GetComponent<Image>().color = new Color(qg.Farbe.r, qg.Farbe.g, qg.Farbe.b, 0.16f);
                    kr.GetChild(0).GetComponent<Image>().color = new Color(qg.Farbe.r, qg.Farbe.g, qg.Farbe.b, 0.8f);
                }
                else if (abst > 0.001f)
                {
                    Punkt(rand++, p.x + dx / abst * randR / s, p.y + dz / abst * randR / s, p, s, qg.Farbe, 9f);
                }
            }
            Kreise = kk;
            Randmarker = rand;
            for (int c = kk; c < _kreise.Count; c++)
                if (_kreise[c].gameObject.activeSelf) _kreise[c].gameObject.SetActive(false);

            // Orte und Flugpunkte im Umkreis (alle Gebiete, deren Rahmen den Kreis berührt), dann die Gruppe (nach den Randmarkern)
            int q = rand;
            float innen = r * 0.92f;
            List<ZoneDaten> zonen = _system.ZonenInReichweite(p, r);
            for (int z = 0; z < zonen.Count; z++)
            {
                ZoneDaten zd = zonen[z];
                if (zd.orte != null)
                    foreach (OrtDaten o in zd.orte)
                        if (o.Kartenpunkt && InKreis(o.x, o.z, p, innen)) Punkt(q++, o.x, o.z, p, s, KartenGrafik.Gold, o.Gross ? 9f : 7f);
                if (zd.flugpunkte != null)
                    foreach (PunktDaten f in zd.flugpunkte)
                        if (InKreis(f.x, f.z, p, innen)) Punkt(q++, f.x, f.z, p, s, KartenGrafik.Zuflucht, 7f);
            }
            List<KartenSystem.Mitglied> gruppe = _system.Gruppe;
            for (int g = 0; g < gruppe.Count; g++)
            {
                Vector2 w = gruppe[g].Welt;
                if (InKreis(w.x, w.y, p, innen)) Punkt(q++, w.x, w.y, p, s, KartenGrafik.Gruppe, 8f);
            }
            Punkte = q;
            for (; q < _punkte.Count; q++)
                if (_punkte[q].gameObject.activeSelf) _punkte[q].gameObject.SetActive(false);

            _pfeil.localRotation = Quaternion.Euler(0f, 0f, -_system.SpielerBlickrichtung);
        }

        private static bool InKreis(float x, float z, Vector2 p, float r) => (x - p.x) * (x - p.x) + (z - p.y) * (z - p.y) <= r * r;

        private RectTransform Zielkreis(int k)
        {
            while (_kreise.Count <= k)
            {
                RectTransform rt = UiKit.CreateRect("Zielkreis " + _kreise.Count, _kreisEbene, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
                Image f = rt.gameObject.AddComponent<Image>(); f.sprite = KartenGrafik.Kreis; f.raycastTarget = false;
                RectTransform rr = UiKit.Stretch("Rand", rt);
                Image ri = rr.gameObject.AddComponent<Image>(); ri.sprite = KartenGrafik.Ring; ri.raycastTarget = false;
                _kreise.Add(rt);
            }
            RectTransform r = _kreise[k];
            if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
            return r;
        }

        private RawImage Kachel(int k)
        {
            while (_kacheln.Count <= k)
            {
                RectTransform rt = UiKit.CreateRect("Kachel " + _kacheln.Count, _kachelEbene, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one);
                RawImage img = rt.gameObject.AddComponent<RawImage>(); img.raycastTarget = false;
                _kacheln.Add(img); _belegung.Add(long.MinValue);
            }
            return _kacheln[k];
        }

        private void Punkt(int q, float x, float z, Vector2 p, float s, Color c, float groesse)
        {
            while (_punkte.Count <= q)
            {
                RectTransform rt = UiKit.CreateRect("Punkt " + _punkte.Count, _punktEbene, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 8f));
                Image img = rt.gameObject.AddComponent<Image>(); img.sprite = KartenGrafik.Kreis; img.raycastTarget = false;
                _punkte.Add(img);
            }
            Image im = _punkte[q];
            im.color = c;
            im.rectTransform.anchoredPosition = new Vector2((x - p.x) * s, (z - p.y) * s);
            im.rectTransform.sizeDelta = new Vector2(groesse, groesse);
            if (!im.gameObject.activeSelf) im.gameObject.SetActive(true);
        }
    }
}
