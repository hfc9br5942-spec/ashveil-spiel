using Ashveil.UI;
using TMPro;
using UnityEngine;

namespace Ashveil.Maps
{
    /// <summary>
    /// Zonenbanner wie in Classic: beim Betreten eines Gebiets groß der Name und darunter der PvP-Status in seiner Farbe,
    /// beim Betreten einer Unterzone deren Name. Neue Entdeckung (vom Server bestätigt, PROG-013): Zeile „Entdeckt: … +EP“.
    /// Blendet nach ein paar Sekunden aus. Lage: oben Mitte unter dem Quest-Hinweis (bis 330 px) und dem Stufenaufstieg
    /// (285–355 px) – beide können gleichzeitig erscheinen (Entdeckungs-EP lösen einen Aufstieg aus).
    /// </summary>
    public sealed class ZonenBannerUi : MonoBehaviour
    {
        private const float Stehzeit = 3.4f, Ausblenden = 1.2f;
        public const float Oben = 365f;

        private CanvasGroup _gruppe;
        private TextMeshProUGUI _titel, _unterzone, _pvp, _entdeckt;
        private float _zeit = -1f;
        private string _gezeigt;

        /// <summary>Zeigt das Banner gerade etwas (Tests)?</summary>
        public bool Sichtbar => _gruppe != null && _gruppe.alpha > 0.01f;
        public string Titel => _titel != null ? _titel.text : "";
        public string Unterzone => _unterzone != null ? _unterzone.text : "";
        public string EntdecktZeile => _entdeckt != null ? _entdeckt.text : "";

        public void Build(RectTransform parent)
        {
            RectTransform root = UiKit.CreateRect("Zonenbanner", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -Oben), new Vector2(1000f, 170f));
            _gruppe = root.gameObject.AddComponent<CanvasGroup>();
            _gruppe.alpha = 0f; _gruppe.interactable = false; _gruppe.blocksRaycasts = false;
            _titel = UiKit.CreateText("Gebiet", root, "", 46f, UiKit.TextColor, TextAlignmentOptions.Center);
            Platz(_titel.rectTransform, 0f, 58f);
            _unterzone = UiKit.CreateText("Unterzone", root, "", 30f, UiKit.TextColor, TextAlignmentOptions.Center);
            Platz(_unterzone.rectTransform, -58f, 40f);
            _pvp = UiKit.CreateText("PvP", root, "", 19f, UiKit.MutedText, TextAlignmentOptions.Center);
            Platz(_pvp.rectTransform, -100f, 26f);
            _entdeckt = UiKit.CreateText("Entdeckt", root, "", 19f, KartenGrafik.Gold, TextAlignmentOptions.Center);
            Platz(_entdeckt.rectTransform, -128f, 26f);
        }

        private static void Platz(RectTransform rt, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y); rt.sizeDelta = new Vector2(0f, h);
        }

        /// <summary>Neues Gebiet: Name groß, darunter die Unterzone (falls eine) und der PvP-Status.</summary>
        public void ZeigeZone(ZoneDaten zone, UnterzoneDaten unterzone, PvpArt pvp, string meerName)
        {
            if (_titel == null) return;
            Color c = KartenGrafik.FarbeFuer(pvp);
            string entdeckt = Behalten(unterzone);
            _titel.text = zone != null ? zone.name : meerName;
            _titel.color = c;
            _unterzone.text = unterzone != null ? unterzone.name : "";
            _unterzone.color = c;
            string stufe = zone != null && zone.stufe_bis > 0 ? $"   ·   Stufe {zone.Stufentext}" : "";
            _pvp.text = "(" + BannerText(pvp) + ")" + stufe;
            _pvp.color = c * 0.9f;
            _entdeckt.text = entdeckt;
            _gezeigt = unterzone != null ? unterzone.name : null;
            Einblenden(Stehzeit + 0.8f);
        }

        /// <summary>
        /// PvP-Status für die Klammer unter dem Gebietsnamen, ohne doppelte Klammern: „(Zuflucht, kein PvP)“ statt
        /// „(Zuflucht (kein PvP))“. Der Wortlaut bleibt der der Karte (<see cref="KartenGrafik.TextFuer"/>).
        /// </summary>
        internal static string BannerText(PvpArt p) => KartenGrafik.TextFuer(p).Replace(" (", ", ").Replace(")", "");

        /// <summary>Nur eine neue Unterzone im selben Gebiet.</summary>
        public void ZeigeUnterzone(UnterzoneDaten u, PvpArt pvp)
        {
            if (_titel == null || u == null) return;
            string entdeckt = Behalten(u);
            _titel.text = "";
            _unterzone.text = u.name;
            _unterzone.color = KartenGrafik.FarbeFuer(pvp);
            _pvp.text = "";
            _entdeckt.text = entdeckt;
            _gezeigt = u.name;
            Einblenden(Stehzeit);
        }

        /// <summary>Kam die Bestätigung des Servers schon vor der eigenen Anzeige, bleibt ihre Zeile stehen.</summary>
        private string Behalten(UnterzoneDaten u) => u != null && _zeit > 0f && _gezeigt == u.name ? _entdeckt.text : "";

        /// <summary>Vom Server bestätigte Entdeckung: Zeile ergänzen (zeigt das Banner gerade diese Unterzone) oder eigenes Banner.</summary>
        public void ZeigeEntdeckt(string name, int xp, Color farbe)
        {
            if (_titel == null || string.IsNullOrEmpty(name)) return;
            string zeile = xp > 0 ? $"Entdeckt: {name}   <color=#d8cfb4><size=85%>+{xp} EP</size></color>" : "Entdeckt: " + name;
            if (_zeit > 0f && _gezeigt == name)
            {
                _entdeckt.text = zeile;
                Einblenden(Mathf.Max(_zeit - Ausblenden, Stehzeit));
                return;
            }
            _titel.text = "";
            _unterzone.text = name;
            _unterzone.color = farbe;
            _pvp.text = "";
            _entdeckt.text = zeile;
            _gezeigt = name;
            Einblenden(Stehzeit);
        }

        private void Einblenden(float dauer)
        {
            _zeit = dauer + Ausblenden;
            _gruppe.alpha = 1f;
        }

        private void Update()
        {
            if (_zeit < 0f || _gruppe == null) return;
            _zeit -= Time.unscaledDeltaTime;
            _gruppe.alpha = Mathf.Clamp01(_zeit / Ausblenden);
            if (_zeit <= 0f) { _zeit = -1f; _gruppe.alpha = 0f; _gezeigt = null; }
        }
    }
}
