using System;
using System.Collections.Generic;
using System.IO;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4, Welle 0 (#27): Inhalte des gebauten Kareth an ihren v4-Lagen – Figuren mit Tagesablauf, Lagertiere,
    /// Auftragsorte, Erkundungsziele, Fundstellen und Requisiten für Q1–Q5 und G1–G13.
    ///
    /// Python plant und prüft, Unity baut (wie Plan und Gelände): Die Beidatei <c>KarethV4_Inhalte.json</c> schreibt
    /// <c>claude/kareth_v4/integration/inhalte_w0.py</c> aus Plan und Höhenkarte (Plätze aus den Bauplänen des Lagers,
    /// Neigung, Wasser, Hindernisse, Belegung der Sitze, Abstand zu Gegnern). Gebaut wird in
    /// <see cref="SandboxSceneBuilder"/> (KarethV4-Teil): gleiche Körper, Kennungen und Saat wie in der Sandbox, neue Lagen.
    /// Aufruf aus <see cref="KarethV4Builder.Build"/> nach den Schwellen, also vor Spielsystemen, Speichern und NavMesh.
    /// </summary>
    public static class KarethV4Inhalte
    {
        public const string DataPath = "Assets/_Ashveil/Data/KarethV4/KarethV4_Inhalte.json";
        public const string RootName = "Inhalte Welle 0 (Figuren, Aufträge, Tiere)";

        // ------------------------------------------------------------------ Datenklassen (JsonUtility)

        [Serializable]
        public class Daten
        {
            public int version;
            public string stand;
            public Platz[] plaetze;
            public Figur[] figuren;
            public Tier[] tiere;
            public Ort[] orte;
            public Bereich[] bereiche;
            public Fund[] fundstellen;
            public Requisit[] requisiten;
        }

        /// <summary>Platz: x/z in Weltmetern; hoehe "gelaende" = Gelände + y, "boden" = y ist der Fußboden (absolut).</summary>
        [Serializable]
        public class Platz
        {
            public string id, hoehe, art;
            public float x, z, yaw, y;
        }

        [Serializable]
        public class Figur
        {
            /// <summary>titel leer = Titel der Sandbox-Figur.</summary>
            public string name, quelle, karren, titel;
            public int saat;
            public float tempo;
            public Schritt[] schritte;
        }

        [Serializable]
        public class Schritt
        {
            public float ab, sitz;
            public string platz, tun, notiz;
            public bool traegt, schiebt, faehrt;
        }

        [Serializable]
        public class Tier
        {
            public string name, koerper;
            public float tempo;
            public int saat;
            public bool wedelt;
            public string[] posen;
            public TierSchritt[] schritte;
        }

        [Serializable]
        public class TierSchritt
        {
            public float ab, radius, yaw;
            public string platz, pose, notiz;
        }

        /// <summary>Auftragsort; sx/sz = Versatz des Schmucks (Greybox) zur Marke, damit die Marke zu Fuß erreichbar bleibt.</summary>
        [Serializable]
        public class Ort
        {
            public string id, name, zone, hub, art, status, notiz, schmuck, schild;
            public float x, z, r, sx, sz;
            public int min, max;
            public bool erreichbar;
        }

        [Serializable]
        public class Bereich
        {
            public string id;
            public float x, z, r, unten, oben;
        }

        [Serializable]
        public class Fund
        {
            public string aktion;
            public float x, z, yaw;
        }

        [Serializable]
        public class Requisit
        {
            public string art, name, hoehe;
            public float x, z, yaw, y;
        }

        // ------------------------------------------------------------------ Laden und Bauen

        /// <summary>Beidatei lesen (null, wenn sie fehlt oder kaputt ist).</summary>
        public static Daten Load()
        {
            if (!File.Exists(DataPath)) return null;
            try
            {
                Daten d = JsonUtility.FromJson<Daten>(File.ReadAllText(DataPath));
                return d != null && d.plaetze != null && d.figuren != null ? d : null;
            }
            catch (Exception e)
            {
                Debug.LogError("[Ashveil] Kareth v4: Inhalte nicht lesbar – " + e.Message);
                return null;
            }
        }

        /// <summary>Inhalte in die offene Kareth-v4-Szene setzen (unter root). Gibt eine Zusammenfassung zurück.</summary>
        public static string Build(Transform root)
        {
            Daten d = Load();
            if (d == null)
            {
                Debug.LogError("[Ashveil] Kareth v4: Inhalte fehlen (" + DataPath + ") – claude/kareth_v4/integration/inhalte_w0.py ausführen.");
                return "Inhalte fehlen";
            }
            Terrain terrain = UnityEngine.Object.FindAnyObjectByType<Terrain>();
            if (terrain == null) return "Inhalte: kein Terrain";
            // Auftrags- und Figurendaten (Q1–Q5, G1–G13, Händler) auf den Stand bringen – bisher nur beim Sandbox-Bau. Schreibt
            // Standardtexte nur bei neuer DefaultsVersion (5: Orte von Q2–Q4 in Kareth v4); G-Texte kommen ohnehin aus glockenrast.txt.
            Loc.Reload();
            QuestContentBuilder.Build();
            return SandboxSceneBuilder.BuildKarethV4Inhalte(root, d, terrain);
        }

        // ------------------------------------------------------------------ Listen (Tests, Weltprüfung)

        /// <summary>Namen aller Figuren (GameObject-Namen in der Szene).</summary>
        public static List<string> FigurNamen(Daten d)
        {
            var list = new List<string>();
            if (d?.figuren != null) foreach (Figur f in d.figuren) list.Add(SandboxSceneBuilder.V4FigurName(f.name));
            return list;
        }

        public static List<string> BereichKennungen(Daten d)
        {
            var list = new List<string>();
            if (d?.bereiche != null) foreach (Bereich b in d.bereiche) list.Add(b.id);
            return list;
        }

        public static List<string> FundAktionen(Daten d)
        {
            var list = new List<string>();
            if (d?.fundstellen != null) foreach (Fund f in d.fundstellen) list.Add(f.aktion);
            return list;
        }

        public static List<string> OrtKennungen(Daten d)
        {
            var list = new List<string>();
            if (d?.orte != null) foreach (Ort o in d.orte) list.Add(o.id);
            return list;
        }

        /// <summary>NpcIdentity-Kennungen, die der Bau vergibt (aus den Sandbox-Figuren gleichen Namens).</summary>
        public static List<string> FigurKennungen(Daten d) => SandboxSceneBuilder.V4FigurKennungen(d);
    }
}
