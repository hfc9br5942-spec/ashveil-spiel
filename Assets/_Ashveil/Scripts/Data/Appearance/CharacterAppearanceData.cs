using System;
using System.Globalization;
using System.Text;

namespace Ashveil.Data
{
    /// <summary>Faerbung eines Ausruestungsplatzes: Palettenindex je Bereich (0 = Standardfarbe des Gegenstands).</summary>
    [Serializable]
    public struct DyeSet
    {
        public byte primary;
        public byte secondary;
        public byte detail;

        public bool IsDefault => primary == 0 && secondary == 0 && detail == 0;

        public byte Get(DyeChannel c) => c == DyeChannel.Primary ? primary : c == DyeChannel.Secondary ? secondary : detail;

        public void Set(DyeChannel c, byte v)
        {
            if (c == DyeChannel.Primary) primary = v;
            else if (c == DyeChannel.Secondary) secondary = v;
            else detail = v;
        }
    }

    /// <summary>
    /// Persoenliches Aussehen eines Charakters (bleibt bei jedem Ausruestungswechsel erhalten) plus Faerbungen und
    /// kosmetische Uebertragungen je Platz. Wird als kurzer Text uebertragen und gespeichert.
    /// </summary>
    [Serializable]
    public sealed class CharacterAppearanceData
    {
        public const int SlotCount = 16; // = InventoryModel.EquipCount (Kampfumbau: + Fernkampfplatz)

        /// <summary>Volk (Index in <see cref="RaceCatalog.All"/>, 0 = Mensch). Steht nach der Erstellung fest (Server erzwingt es).</summary>
        public int race;
        public int body;
        public int skin;
        public int hair;
        public int hairColor = 1;   // 18.10.: Standard Dunkelbraun (Katalog-Index 1, wie die Figur-Vorlage) statt Schwarz
        public int beard;
        /// <summary>M7: Gesichtsform (gemalte Gesichtsebene + Kopfform), Augenfarbe, Gesichtsmerkmal (Narbe, Bemalung …).</summary>
        public int face;
        public int eyeColor;
        public int feature;
        public bool hideHelm;
        public bool hideCloak;
        public DyeSet[] dyes = new DyeSet[SlotCount];
        /// <summary>Kosmetik: Item-ID, deren Aussehen der Platz zeigt (leer = eigenes Aussehen). Werte bleiben vom getragenen Item.</summary>
        public string[] transmog = new string[SlotCount];

        public CharacterAppearanceData Clone()
        {
            var c = (CharacterAppearanceData)MemberwiseClone();
            c.dyes = (DyeSet[])(dyes ?? new DyeSet[SlotCount]).Clone();
            c.transmog = (string[])(transmog ?? new string[SlotCount]).Clone();
            return c;
        }

        public void EnsureArrays()
        {
            if (dyes == null || dyes.Length != SlotCount) Array.Resize(ref dyes, SlotCount);
            if (transmog == null || transmog.Length != SlotCount) Array.Resize(ref transmog, SlotCount);
        }

        /// <summary>Kompakt: "1|b0|s0|h1|c2|d0|a1|e3|m0|f1|y9:1.0.3,...|t2=item.x,..."</summary>
        public string Encode()
        {
            EnsureArrays();
            var sb = new StringBuilder("1");
            if (race != 0) sb.Append("|r").Append(race);
            sb.Append("|b").Append(body).Append("|s").Append(skin).Append("|h").Append(hair).Append("|c").Append(hairColor)
              .Append("|d").Append(beard).Append("|a").Append(face).Append("|e").Append(eyeColor).Append("|m").Append(feature).Append("|f").Append((hideHelm ? 1 : 0) | (hideCloak ? 2 : 0));
            sb.Append("|y");
            bool first = true;
            for (int i = 0; i < SlotCount; i++)
            {
                if (dyes[i].IsDefault) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(i).Append(':').Append(dyes[i].primary).Append('.').Append(dyes[i].secondary).Append('.').Append(dyes[i].detail);
            }
            sb.Append("|t");
            first = true;
            for (int i = 0; i < SlotCount; i++)
            {
                if (string.IsNullOrEmpty(transmog[i])) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(i).Append('=').Append(transmog[i]);
            }
            return sb.ToString();
        }

        /// <summary>Liest den Text; Unlesbares wird ignoriert (nie eine Ausnahme).</summary>
        public static CharacterAppearanceData Decode(string s)
        {
            var d = new CharacterAppearanceData();
            if (string.IsNullOrEmpty(s)) return d;
            foreach (string part in s.Split('|'))
            {
                if (part.Length < 1) continue;
                string v = part.Substring(1);
                switch (part[0])
                {
                    case 'r': d.race = Int(v); break;
                    case 'b': d.body = Int(v); break;
                    case 's': d.skin = Int(v); break;
                    case 'h': d.hair = Int(v); break;
                    case 'c': d.hairColor = Int(v); break;
                    case 'd': d.beard = Int(v); break;
                    case 'a': d.face = Int(v); break;
                    case 'e': d.eyeColor = Int(v); break;
                    case 'm': d.feature = Int(v); break;
                    case 'f':
                        int f = Int(v);
                        d.hideHelm = (f & 1) != 0;
                        d.hideCloak = (f & 2) != 0;
                        break;
                    case 'y':
                        foreach (string e in v.Split(','))
                        {
                            string[] kv = e.Split(':');
                            if (kv.Length != 2) continue;
                            int slot = Int(kv[0]);
                            string[] c = kv[1].Split('.');
                            if (slot < 0 || slot >= SlotCount || c.Length != 3) continue;
                            d.dyes[slot] = new DyeSet { primary = Byte(c[0]), secondary = Byte(c[1]), detail = Byte(c[2]) };
                        }
                        break;
                    case 't':
                        foreach (string e in v.Split(','))
                        {
                            int eq = e.IndexOf('=');
                            if (eq <= 0) continue;
                            int slot = Int(e.Substring(0, eq));
                            if (slot >= 0 && slot < SlotCount) d.transmog[slot] = e.Substring(eq + 1);
                        }
                        break;
                }
            }
            return d;
        }

        private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        private static byte Byte(string s) => byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte v) ? v : (byte)0;
    }
}
