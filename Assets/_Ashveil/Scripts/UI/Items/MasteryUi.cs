using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Mastery;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Fenster „Erfahrung“ (Praxis-Meisterschaften §50–§54, §94/§95): je Meisterschaft Rang, Fortschrittsbalken,
    /// aktive Wirkungen in Klartext und der naechste Meilenstein, darunter die Lebenszeit-Statistik
    /// („Zu Fuß zurückgelegt: 2 418 km“). Geoeffnet ueber den Knopf „Erfahrung“ im Charakterfenster.
    /// </summary>
    public sealed class MasteryUi : MonoBehaviour
    {
        private sealed class Row
        {
            public MasteryDefinition Def;
            /// <summary>Reittier-Bindung: nur sichtbar, wenn man dieses Tier besitzt.</summary>
            public ClassMountDefinition Mount;
            /// <summary>Nachlese (Heiler-Signatur): nur sichtbar, wenn erlernt.</summary>
            public bool Salvage;
            /// <summary>Gefaehrten-Bindung: nur sichtbar, wenn man diesen Gefaehrten hat.</summary>
            public CompanionSpecies Companion;
            public TextMeshProUGUI Title, Detail;
            public UiKit.Bar Bar;
        }

        public static MasteryUi Instance { get; private set; }
        public bool IsOpen { get; private set; }

        private RectTransform _win, _close;
        private TextMeshProUGUI _stats;
        private readonly System.Collections.Generic.List<Row> _rows = new System.Collections.Generic.List<Row>();
        private int _shownRevision = -1;

        public static MasteryUi Ensure(Transform host)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Erfahrung (UI)");
            go.transform.SetParent(host, false);
            Instance = go.AddComponent<MasteryUi>();
            Instance.Build();
            return Instance;
        }

        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (_win != null) _win.gameObject.SetActive(open);
            _shownRevision = -1;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            Canvas canvas = UiKit.CreateCanvas("Erfahrung Canvas", transform, 21);
            var root = (RectTransform)canvas.transform;
            const float w = 420f;
            float h = 70f + (MasteryCatalog.All.Length + MountCatalog.All.Length + CompanionCatalog.All.Length + 1) * 150f + 90f;
            RectTransform inner = UiKit.Panel("Erfahrung", root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(616f, 20f), new Vector2(w, h));
            _win = (RectTransform)inner.parent;
            TextMeshProUGUI title = UiKit.CreateText("Titel", _win, "Erfahrung", 20f, UiKit.TextColor);
            Place(title.rectTransform, new Vector2(14f, -8f), new Vector2(w - 60f, 26f));
            _close = UiKit.CreateRect("Schliessen", _win, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f));
            UiKit.AddImage(_close, UiKit.BorderColor);
            TextMeshProUGUI x = UiKit.CreateText("x", _close, "×", 18f, UiKit.TextColor, TextAlignmentOptions.Center);
            x.rectTransform.anchorMin = Vector2.zero;
            x.rectTransform.anchorMax = Vector2.one;
            x.rectTransform.offsetMin = x.rectTransform.offsetMax = Vector2.zero;
            TextMeshProUGUI sub = UiKit.CreateText("Untertitel", _win, "Was du tust, darin wirst du besser.", 13f, UiKit.MutedText);
            Place(sub.rectTransform, new Vector2(14f, -36f), new Vector2(w - 28f, 20f));

            float y = -66f;
            var defs = new System.Collections.Generic.List<(MasteryDefinition, ClassMountDefinition)>();
            foreach (MasteryDefinition d0 in MasteryCatalog.All) defs.Add((d0, null));
            foreach (ClassMountDefinition m in MountCatalog.All) defs.Add((m.bond, m));
            defs.Add((MasteryCatalog.Salvage, null));
            foreach (CompanionSpecies cs in CompanionCatalog.All) if (cs.bond != null) defs.Add((cs.bond, null));
            foreach ((MasteryDefinition d, ClassMountDefinition mount) in defs)
            {
                CompanionSpecies comp = null;
                foreach (CompanionSpecies cs in CompanionCatalog.All) if (cs.bond == d) comp = cs;
                var row = new Row { Def = d, Mount = mount, Salvage = d == MasteryCatalog.Salvage, Companion = comp };
                row.Title = UiKit.CreateText(d.displayName, _win, "", 17f, UiKit.TextColor);
                row.Title.richText = true;
                Place(row.Title.rectTransform, new Vector2(14f, y), new Vector2(w - 28f, 24f));
                row.Bar = UiKit.CreateBar("Fortschritt", _win, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, y - 26f), new Vector2(w - 28f, 16f), UiKit.StaminaColor);
                row.Detail = UiKit.CreateText("Wirkung", _win, "", 13.5f, UiKit.MutedText);
                row.Detail.richText = true;
                row.Detail.textWrappingMode = TextWrappingModes.Normal;
                Place(row.Detail.rectTransform, new Vector2(14f, y - 48f), new Vector2(w - 28f, 96f));
                _rows.Add(row);
                y -= 150f;
            }
            _stats = UiKit.CreateText("Statistik", _win, "", 14f, UiKit.TextColor);
            _stats.richText = true;
            Place(_stats.rectTransform, new Vector2(14f, y - 4f), new Vector2(w - 28f, 76f));
            _win.gameObject.SetActive(false);
            UiFensterstapel.Anmelden(this, "Meisterschaften", () => IsOpen, () => SetOpen(false));
        }

        private void Update()
        {
            if (_win == null || !IsOpen) return;
            Vector2 mouse = RawInput.MousePosition;
            if (RectTransformUtility.RectangleContainsScreenPoint(_win, mouse, null)) UiPointer.OverUi = true;
            if (RawInput.LeftMousePressed && RectTransformUtility.RectangleContainsScreenPoint(_close, mouse, null)) { SetOpen(false); return; }
            int rev = MasteryClient.Revision * 1000000 + Ashveil.Gameplay.Mounts.MountClient.Revision * 1000 + Ashveil.Gameplay.Items.SalvageClient.Revision * 10 +
                      Ashveil.Gameplay.Companions.CompanionClient.Revision * 7;
            if (_shownRevision == rev) return;
            _shownRevision = rev;
            Refresh();
        }

        private void Refresh()
        {
            const float w = 420f;
            float y = -66f;
            foreach (Row r in _rows)
            {
                bool show = r.Salvage ? Ashveil.Gameplay.Items.SalvageClient.Unlocked
                    : r.Companion != null ? Ashveil.Gameplay.Companions.CompanionClient.Species == r.Companion.id
                    : r.Mount == null || Ashveil.Gameplay.Mounts.MountClient.MountId == r.Mount.id;
                r.Title.gameObject.SetActive(show);
                r.Bar.Root.gameObject.SetActive(show);
                r.Detail.gameObject.SetActive(show);
                if (!show) continue;
                Place(r.Title.rectTransform, new Vector2(14f, y), new Vector2(w - 28f, 24f));
                r.Bar.Root.anchoredPosition = new Vector2(14f, y - 26f);
                Place(r.Detail.rectTransform, new Vector2(14f, y - 48f), new Vector2(w - 28f, 96f));
                y -= 150f;
                double pts = MasteryClient.Points(r.Def.id);
                int rank = r.Def.RankFor(pts, out double into, out double next);
                string title = r.Mount != null ? $"{Ashveil.Gameplay.Mounts.MountClient.Name} – {r.Def.displayName}"
                    : r.Companion != null ? $"{(string.IsNullOrEmpty(Ashveil.Gameplay.Companions.CompanionClient.Name) ? r.Companion.displayName : Ashveil.Gameplay.Companions.CompanionClient.Name)} – {r.Def.displayName}"
                    : r.Def.displayName;
                r.Title.text = $"{title}  <color=#e8c98a>Rang {rank}</color><size=13><color=#8a8478> / {r.Def.maxRank}</color></size>";
                r.Bar.Set(next > 0 ? (float)(into / next) : 1f);
                r.Bar.Label.text = next > 0 ? Amount(r.Def, into) + " / " + Amount(r.Def, next) : "Meisterschaft vollendet";
                var sb = new StringBuilder();
                sb.Append("<i>").Append(r.Def.description).Append("</i>\n");
                if (rank == 0) sb.Append("Noch keine Wirkung – sie wächst mit jedem Rang.\n");
                else
                    foreach (MasteryEffect e in r.Def.effects)
                        sb.Append(e.sign).Append(Mathf.RoundToInt(r.Def.Effect(e.key, rank) * 100f)).Append(" % ").Append(e.displayName).Append('\n');
                int milestone = Mathf.Min(r.Def.maxRank, (rank / r.Def.milestoneEvery + 1) * r.Def.milestoneEvery);
                if (rank < r.Def.maxRank)
                    sb.Append("<color=#8a8478>Nächster Meilenstein: Rang ").Append(milestone).Append(" – ")
                      .Append(r.Def.EffectsText(milestone)).Append("</color>");
                r.Detail.text = sb.ToString();
            }
            Place(_stats.rectTransform, new Vector2(14f, y - 4f), new Vector2(w - 28f, 96f));
            _win.sizeDelta = new Vector2(w, -y + 110f);
            _stats.text = $"Zu Fuß zurückgelegt: <color=#e8c98a>{MasteryClient.MetersOnFoot / 1000.0:N1} km</color>\n" +
                          $"Angriffen ausgewichen: <color=#e8c98a>{MasteryClient.MeaningfulDodges}</color>\n" +
                          $"Mit dem Schild abgefangen: <color=#e8c98a>{MasteryClient.DamageBlocked:N0} Schaden</color>" +
                          (Ashveil.Gameplay.Items.SalvageClient.Unlocked
                              ? $"\nGeborgen: <color=#e8c98a>{Ashveil.Gameplay.Items.SalvageClient.Salvages}</color>, davon selten <color=#e8c98a>{Ashveil.Gameplay.Items.SalvageClient.RareSalvages}</color>"
                              : "");
        }

        /// <summary>Laufkunst in km/m, sonst Punkte.</summary>
        private static string Amount(MasteryDefinition d, double v)
        {
            if (d.id == MasteryCatalog.RunningId || IsBond(d)) return v >= 1000 ? $"{v / 1000.0:0.0} km" : $"{v:0} m";
            if (d.id == MasteryCatalog.BlockingId) return $"{v:N0}";
            if (d.id == MasteryCatalog.SalvageId) return $"{v:0.#}";
            return $"{v:0.#}";
        }

        private static bool IsBond(MasteryDefinition d)
        {
            foreach (ClassMountDefinition m in MountCatalog.All) if (m.bond == d) return true;
            return false;
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }
    }
}
