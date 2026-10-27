using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Woher ein Gegner in die Arena kommt (Kanon §36). Spiegelt ArenaGateKind (Gameplay) als Bitmaske.</summary>
    [Flags]
    public enum ArenaEntryMask
    {
        None = 0,
        Gate = 1,
        FloorOpening = 2,
        Gallery = 4,
        All = Gate | FloorOpening | Gallery
    }

    /// <summary>Ein Block der Stunde („Prüfung I“ …): Budget je Minute (für 5 Spieler), gleichzeitige Gegner, Elite-Anteil.</summary>
    [Serializable]
    public sealed class WavePhaseDefinition
    {
        public string displayName = "Prüfung I";
        public float startMinute;
        public float endMinute = 5f;
        [Tooltip("Gegnerpunkte je Minute bei 5 Spielern (Schwach 1, Elite 5, Beschwörer 7, Mini-Boss 20)")]
        public float budgetPerMinute = 10f;
        [Tooltip("Hoechstens so viele lebende Gegner gleichzeitig (5 Spieler)")]
        public int aliveCap = 18;
        [Range(0f, 1f)] public float eliteChance;
        [Tooltip("Welche Eingaenge in diesem Block Gegner liefern")]
        public ArenaEntryMask entries = ArenaEntryMask.Gate;
        [Tooltip("Stufe der Gegner: Grundstufe des Rings + dieser Wert")]
        public int levelBonus;
        [Tooltip("Sekunden zwischen zwei Trupps (Bereich)")]
        public Vector2 packInterval = new Vector2(7f, 13f);
    }

    /// <summary>Kuratierter Eintrag des Gegnerpools (Kanon §34: kuratierte Pools + Seed, nicht voellig zufaellig).</summary>
    [Serializable]
    public sealed class ArenaPoolEntry
    {
        public string role = "Schwarm";
        public GameObject prefab;
        [Tooltip("Punkte je Gegner")] public int cost = 1;
        public float weight = 1f;
        [Tooltip("Ab welchem Block (0 = Prüfung I)")] public int minPhase;
        [Tooltip("Elite-Eintrag: wird nur mit der Elite-Chance des Blocks gewaehlt")] public bool elite;
        public int packMin = 1;
        public int packMax = 1;
        public ArenaEntryMask entries = ArenaEntryMask.Gate;
    }

    /// <summary>Champion zu einer festen Zeit (Pass 4: 5:00 und 10:00). Verstaerkter Gegner aus dem Haupttor.</summary>
    [Serializable]
    public sealed class ArenaBossEntry
    {
        [Tooltip("Ansage, z. B. „Champion des Rings“")]
        public string title = "Champion des Rings";
        [Tooltip("Sekunde des Laufs")]
        public float atSeconds = 300f;
        public GameObject prefab;
        [Tooltip("Stufe: Grundstufe + dieser Wert (hoechstens maxLevel + 1)")]
        public int levelBonus = 2;
        [Tooltip("Lebensfaktor = Basis + je Gruppenfaktor × Wert (allein ≈ 3,7, zu fuenft 6)")]
        public float healthBase = 2f;
        public float healthPerGroup = 4f;
        public float damageMultiplier = 1.25f;
    }

    /// <summary>
    /// Kolosseum: Daten fuer den Wellen-Direktor (Kanon §33–36). Werte zum Tunen im Inspector; der Plan eines Laufs
    /// entsteht daraus vorab aus einem Seed (WavePlan) – gleicher Seed, gleicher Plan.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Kolosseum/Kolosseum-Definition", fileName = "Kolosseum")]
    public sealed class ColosseumDefinition : ScriptableObject
    {
        [Tooltip("Laenge eines Laufs in Sekunden (Pass 3/4: 10-Minuten-Prototyp)")]
        public float runSeconds = 600f;
        [Tooltip("Sekunden zwischen Betreten und erster Welle")]
        public float startDelay = 10f;
        public int baseLevel = 14;
        public int maxLevel = 16;
        [Tooltip("Gruppenfaktor = (Basis + je Spieler × n) / (Basis + je Spieler × 5)")]
        public float groupBase = 0.5f;
        public float groupPerPlayer = 0.25f;
        [Tooltip("Mindestzahl gleichzeitiger Gegner (auch allein)")]
        public int minAliveCap = 4;

        [HideInInspector] public int builderVersion;

        [Header("Pass 4: Glocke, Bosse, Wiederbelebung, Pruefpunkt (Max 28.09.)")]
        [Tooltip("Sekunden zwischen Glockenschlag und erster Welle")]
        public float bellCountdown = 10f;
        [Tooltip("Leer = automatisch: erster Elite-Eintrag bei 5:00, zweiter bei 10:00")]
        public List<ArenaBossEntry> bosses = new List<ArenaBossEntry>();
        [Tooltip("Gemeinsamer Vorrat an Wiederbelebungen je Lauf")]
        public int revivePool = 3;
        [Tooltip("+ so viele je besiegtem Champion")]
        public int revivePerBoss = 1;
        public float reviveSeconds = 5f;
        public float reviveRange = 3.5f;
        [Range(0.1f, 1f)] public float reviveHealth = 0.5f;
        [Tooltip("Pruefpunkt-Truhe: Kupfer je Spieler")]
        public int checkpointMoney = 180;
        [Tooltip("Pruefpunkt-Truhe: Erfahrung = so viele Toetungen eines Gegners der Ringstufe")]
        public int checkpointXpKills = 10;

        public List<WavePhaseDefinition> phases = new List<WavePhaseDefinition>();
        public List<ArenaPoolEntry> pool = new List<ArenaPoolEntry>();

        /// <summary>0..1: Anteil von Budget und Gegnerzahl gegenueber einer vollen 5er-Gruppe.</summary>
        public float GroupFactor(int players)
        {
            int n = Mathf.Clamp(players, 1, 5);
            float full = groupBase + groupPerPlayer * 5f;
            return full > 0f ? (groupBase + groupPerPlayer * n) / full : 1f;
        }

        public int AliveCap(int phase, int players) =>
            phase < 0 || phase >= phases.Count ? minAliveCap : Mathf.Max(minAliveCap, Mathf.RoundToInt(phases[phase].aliveCap * GroupFactor(players)));

        public int PhaseAt(float seconds)
        {
            float m = seconds / 60f;
            for (int i = 0; i < phases.Count; i++)
                if (m >= phases[i].startMinute && m < phases[i].endMinute) return i;
            return phases.Count - 1;
        }
    }
}
