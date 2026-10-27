using Ashveil.Gameplay.Colosseum;
using TMPro;
using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>
    /// Kolosseum Pass 4: Feld oben in der Mitte – Block und Uhr, Vorrat an Wiederbelebungen, Champion im Ring, Countdown
    /// der Glocke, „Du liegst am Boden“, Leiste beim Wiederbeleben, Ergebnis. Liest nur <see cref="ColosseumClient"/>.
    /// </summary>
    public sealed class ColosseumHud : MonoBehaviour
    {
        private RectTransform _panel;
        private TextMeshProUGUI _title, _info, _center, _down;
        private UiKit.Bar _channel;
        private int _shown = -1;
        private float _nextClock;

        public static ColosseumHud Ensure(Transform host)
        {
            var existing = host.GetComponentInChildren<ColosseumHud>(true);
            if (existing != null) return existing;
            var go = new GameObject("Kolosseum-HUD");
            go.transform.SetParent(host, false);
            var hud = go.AddComponent<ColosseumHud>();
            hud.Build();
            return hud;
        }

        private void Build()
        {
            Canvas canvas = UiKit.CreateCanvas("Kolosseum Canvas", transform, 12);
            Transform root = canvas.transform;
            _panel = UiKit.Panel("Kolosseum", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(360f, 58f));
            _panel = (RectTransform)_panel.parent;
            _title = UiKit.CreateText("Titel", _panel, "", 20f, UiKit.TextColor, TextAlignmentOptions.Center);
            Stretch(_title.rectTransform, new Vector2(0f, -4f), 26f);
            _info = UiKit.CreateText("Info", _panel, "", 14.5f, UiKit.MutedText, TextAlignmentOptions.Center);
            _info.richText = true;
            Stretch(_info.rectTransform, new Vector2(0f, -32f), 20f);

            _center = UiKit.CreateText("Ansage", root, "", 40f, new Color(1f, 0.82f, 0.4f), TextAlignmentOptions.Center);
            _center.richText = true;
            Place(_center.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(900f, 60f));
            _down = UiKit.CreateText("Am Boden", root, "", 22f, new Color(0.95f, 0.55f, 0.45f), TextAlignmentOptions.Center);
            Place(_down.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 110f), new Vector2(900f, 30f));
            _channel = UiKit.CreateBar("Wiederbeleben", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(300f, 20f),
                new Color(0.55f, 0.78f, 0.95f), 13f);
            _panel.gameObject.SetActive(false);
            _channel.Root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_panel == null) return;
            bool show = ColosseumClient.Visible;
            if (_panel.gameObject.activeSelf != show) _panel.gameObject.SetActive(show);
            if (!show)
            {
                _center.text = "";
                _down.text = "";
                if (_channel.Root.gameObject.activeSelf) _channel.Root.gameObject.SetActive(false);
                return;
            }

            // Uhr jede Zehntelsekunde, Rest bei Aenderungen
            if (_shown != ColosseumClient.Revision || Time.unscaledTime >= _nextClock)
            {
                _shown = ColosseumClient.Revision;
                _nextClock = Time.unscaledTime + 0.1f;
                int s = Mathf.Max(0, Mathf.FloorToInt(ColosseumClient.Elapsed));
                _title.text = ColosseumClient.InRun || ColosseumClient.Result != 0
                    ? $"{ColosseumClient.PhaseName}   <color=#e8c98a>{s / 60:00}:{s % 60:00}</color>"
                    : "Der Ring wartet";
                string boss = ColosseumClient.BossAlive ? "   <color=#e07860>Champion im Ring!</color>" : "";
                _info.text = ColosseumClient.InRun || ColosseumClient.Result != 0
                    ? $"Wiederbelebungen: <color=#e8c98a>{ColosseumClient.Revives}</color>{boss}"
                    : "Läutet die Glocke, wenn ihr bereit seid.";

                float cd = ColosseumClient.CountdownLeft;
                if (cd > 0f) _center.text = $"Die Tore öffnen sich in {Mathf.CeilToInt(cd)}";
                else if (ColosseumClient.Result == 1) _center.text = "Prüfung bestanden!<size=55%>\nDie Truhe des Ringmeisters wartet.</size>";
                else if (ColosseumClient.Result == 2) _center.text = "<color=#e07860>Der Ring hat euch besiegt</color>";
                else _center.text = "";
                _down.text = ColosseumClient.Downed
                    ? (ColosseumClient.Revives > 0 ? "Du liegst am Boden – ein Mitstreiter kann dich wiederbeleben." : "Du liegst am Boden – kein Wiederbeleben mehr übrig.")
                    : "";
            }

            float p = ColosseumClient.ChannelProgress;
            bool channel = p >= 0f;
            if (_channel.Root.gameObject.activeSelf != channel) _channel.Root.gameObject.SetActive(channel);
            if (channel)
            {
                _channel.Set(p);
                _channel.Label.text = "Wiederbeleben …";
            }
        }

        private static void Stretch(RectTransform rt, Vector2 top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = top;
            rt.sizeDelta = new Vector2(0f, height);
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
