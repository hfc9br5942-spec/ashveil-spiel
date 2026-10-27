using System;
using System.IO;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Idylle um Untersteg (WORLD-244; Max 10.10.2026: „Erst Idylle, später Kacheln“, Wassermühle am Muldenbach, Korn, Flachs und Heu,
    /// Felder und Mühle zuerst). Beidateien aus <c>claude/kareth_v4/integration/idylle.py</c>:
    /// <list type="bullet">
    /// <item><c>KarethV4_Idylle.json</c> – Gebäude (Mühle) und Spuren (Mühlrad, Wehr, Feldzaun, Heuschober, Lesesteinwälle,
    /// Vogelscheuchen) in denselben Klassen wie der Plan; gebaut von <see cref="KarethV4Camp"/>.</item>
    /// <item><c>KarethV4_Felder.bytes</c> – je Zelle der Schichtkarte (Zeilen Süd→Nord) ein Feldcode: 0 nichts, 1 Korn, 2 Flachs,
    /// 3 Acker, 4 Heuwiese, 5 Rain, 6 Obstgarten (frei). Gelesen von <see cref="KarethV4Builder"/> für die Feldpflanzen.</item>
    /// </list>
    /// Fehlen die Dateien, baut Kareth v4 wie vorher.
    /// </summary>
    [Serializable]
    public sealed class KarethV4IdylleDaten
    {
        public int version;
        public string stand, name;
        public int felderRes;
        public KarethV4Gebaeude[] gebaeude;
        public KarethV4Spur[] spuren;
    }

    public static class KarethV4Idylle
    {
        public const byte Korn = 1, Flachs = 2, Acker = 3, Heuwiese = 4, Rain = 5, ObstFrei = 6;
        public static string JsonPfad => KarethV4Builder.DataFolder + "/KarethV4_Idylle.json";
        public static string FelderPfad => KarethV4Builder.DataFolder + "/KarethV4_Felder.bytes";

        public static KarethV4IdylleDaten Load()
        {
            if (!File.Exists(JsonPfad)) return null;
            try { return JsonUtility.FromJson<KarethV4IdylleDaten>(File.ReadAllText(JsonPfad)); }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Kareth v4 Idylle: Datei nicht lesbar – " + e.Message);
                return null;
            }
        }

        /// <summary>Feldcodes (Länge res², Zeilen Süd→Nord) oder null, wenn die Datei fehlt oder nicht passt.</summary>
        public static byte[] Felder(int res)
        {
            if (!File.Exists(FelderPfad)) return null;
            byte[] b = File.ReadAllBytes(FelderPfad);
            if (b.Length != res * res)
            {
                Debug.LogWarning($"[Ashveil] Kareth v4 Idylle: Felderkarte hat {b.Length} Bytes, erwartet {res * res} – ohne Feldpflanzen gebaut.");
                return null;
            }
            return b;
        }
    }
}
