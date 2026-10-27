using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Itemqualitaet. Legendaer bleibt Einzelstuecken vorbehalten.</summary>
    public enum ItemQuality
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4
    }

    public static class ItemQualityColors
    {
        public static Color Get(ItemQuality q)
        {
            switch (q)
            {
                case ItemQuality.Uncommon: return new Color(0.2f, 0.9f, 0.25f);
                case ItemQuality.Rare: return new Color(0.25f, 0.55f, 1f);
                case ItemQuality.Epic: return new Color(0.72f, 0.35f, 1f);
                case ItemQuality.Legendary: return new Color(1f, 0.55f, 0.1f);
                default: return new Color(0.92f, 0.92f, 0.9f);
            }
        }

        /// <summary>Seltenheit als Text (zusaetzlich zur Farbe – auch fuer Farbenblinde lesbar).</summary>
        public static string Name(ItemQuality q)
        {
            switch (q)
            {
                case ItemQuality.Uncommon: return "Ungewöhnlich";
                case ItemQuality.Rare: return "Selten";
                case ItemQuality.Epic: return "Episch";
                case ItemQuality.Legendary: return "Legendär";
                default: return "Gewöhnlich";
            }
        }

        public static string Hex(ItemQuality q) => "#" + ColorUtility.ToHtmlStringRGB(Get(q));
    }
}
