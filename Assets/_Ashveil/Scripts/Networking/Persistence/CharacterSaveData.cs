using System;
using System.Collections.Generic;
using Ashveil.Data;

namespace Ashveil.Networking
{
    [Serializable]
    public struct ProfessionSkill
    {
        public string id;
        public int skill;
    }

    /// <summary>Praxis-Meisterschaft: gesammelte Lernpunkte (der Rang ergibt sich daraus, <see cref="MasteryDefinition"/>).</summary>
    [Serializable]
    public sealed class MasterySave
    {
        public string id;
        public double points;
    }

    /// <summary>Lebenszeit-Statistik (Praxis-Meisterschaften §94/§95): „Zu Fuß zurückgelegt: 2 418 km“.</summary>
    [Serializable]
    public sealed class LifetimeStats
    {
        public double metersOnFoot;
        public int meaningfulDodges;
        public int emptyDodges;
        /// <summary>Mit dem Schild abgefangener Schaden (Schildblock, nur gegen Gegner).</summary>
        public double damageBlocked;
        public int blocks;
        /// <summary>Im Sattel zurueckgelegt (Klassenreittier).</summary>
        public double metersRidden;
        /// <summary>Nachlese: geborgene Stoffe (Anzahl Bergungen) und davon seltene.</summary>
        public int salvages;
        public int rareSalvages;
        /// <summary>Dominator: gebundene Wesen und Siege, an denen der Gefaehrte beteiligt war.</summary>
        public int dominations;
        public int companionKills;
    }

    /// <summary>Gefaehrte des Dominators (§86: spaeter mehrere, einer aktiv – im Prototyp genau einer).</summary>
    [Serializable]
    public sealed class CompanionSave
    {
        public string species;
        public string name;
        /// <summary>Gerufen (beim Anmelden wieder da) oder entlassen.</summary>
        public bool active = true;
    }

    /// <summary>Klassenreittier (Class Identity Masterplan §168): welches Tier, eigener Name, Bindung, Optik (spaeter).</summary>
    [Serializable]
    public sealed class MountSave
    {
        public string id;
        public string name;
        public string cosmetics;
    }

    /// <summary>
    /// Seit v8: Pferde-Geschichte des menschlichen Tanks (Vorgabe „Wo die Herde floh“ / „Der letzte Weidegang“).
    /// Logischer Zustand zuerst – alles Sichtbare leitet sich daraus ab. Zeitstempel in UTC (die Spielzeit wird nicht gespeichert).
    /// </summary>
    [Serializable]
    public sealed class HorseStorySave
    {
        public string storyId = HorseStoryCatalog.StoryHorseId;
        /// <summary><see cref="HorseStoryState"/> als Zahl.</summary>
        public int state;
        /// <summary>Feinschritt innerhalb der laufenden Quest (Hinweise, Kampfphase, Szene).</summary>
        public int step;
        /// <summary>Meter entlang der Heimweg-Route der Herde (Wiederaufnahme nach Reconnect).</summary>
        public float herdRouteProgress;
        public string foalRescuedAtUtc;
        public string releasedAtUtc;
        public string bondedAtUtc;
        /// <summary>Epilog (stiller Hof) ist fuer diesen Charakter eingetreten.</summary>
        public bool epilogue;

        public HorseStoryState State => (HorseStoryState)state;
    }

    /// <summary>Seit v10: gelernte Klassenfaehigkeit (hoechster Rang) und ob sie schon einmal auf die Leiste gelegt wurde.</summary>
    [Serializable]
    public sealed class SpellSave
    {
        public string id;
        public int rank;
        public bool placed;
    }

    /// <summary>Seit v10: gewaehltes Talent und seine Punkte.</summary>
    [Serializable]
    public sealed class TalentSave
    {
        public string id;
        public int rank;
    }

    /// <summary>Stand eines angenommenen Auftrags (Fortschritt je Ziel).</summary>
    [Serializable]
    public sealed class QuestSave
    {
        public string id;
        public int[] progress = new int[0];
    }

    /// <summary>
    /// Speicherstand eines Charakters. Enthaelt nur IDs und Zahlen – kein Asset, keine Unity-Referenz.
    /// Format-Version erhoehen, wenn Felder ihre Bedeutung aendern.
    /// </summary>
    [Serializable]
    public sealed class CharacterSaveData
    {
        public const int CurrentVersion = 12; // 12: Ausgeruht (Ruhe-EP, Zeitstempel, im Gasthaus) · 11: entdeckte Orte (Entdeckungs-EP, P3 Glockenrast) · 10: Klassenfaehigkeiten (gelernte Raenge), Talente · 9: Taschen/Koecher (4 Taschenplaetze), Munitionsplatz, Fernkampfplatz · 2: Erfahrung, Geld, Auftraege · 3: Actionbar · 4: Charakter-Editor (Gesicht, Augen, Merkmal, eigener Name) · 5: Weltposition (Large World) · 6: Praxis-Meisterschaften · 7: Klassenreittier · 8: Volk, Geschichtsmarken, Pferde-Geschichte

        public int version = CurrentVersion;
        public string profileId;
        /// <summary>Charaktername (seit M7 im Charakter-Editor gewaehlt; alte Staende: aus der Profil-ID erzeugt).</summary>
        public string name;
        public string classId;
        /// <summary>Seit v8: Volk (race.*). Alte Staende: Mensch. Steht nach der Erstellung fest.</summary>
        public string race = RaceCatalog.HumanId;
        public int level = 1;
        public int xp;
        /// <summary>Geld in Kupfer.</summary>
        public long money;
        /// <summary>Angenommene, noch nicht abgegebene Auftraege.</summary>
        public List<QuestSave> quests = new List<QuestSave>();
        /// <summary>Abgegebene Auftraege (IDs).</summary>
        public List<string> completedQuests = new List<string>();
        public ItemStack[] bag = new ItemStack[20];
        public ItemStack[] equipment = new ItemStack[16];
        /// <summary>v9: die 4 Taschenplaetze (Tasche, Koecher, Munitionsbeutel).</summary>
        public ItemStack[] containers = new ItemStack[4];
        /// <summary>v9: gewaehlte Munitionssorte (Item-ID).</summary>
        public string ammoId = "";
        public List<ProfessionSkill> professions = new List<ProfessionSkill>();
        public List<string> knownRecipes = new List<string>();
        /// <summary>Persoenliches Aussehen, Faerbungen, kosmetische Uebertragung.</summary>
        public CharacterAppearanceData appearance = new CharacterAppearanceData();
        /// <summary>Freigeschaltete Aussehen (fuer spaetere kosmetische Uebertragung).</summary>
        public List<string> unlockedAppearances = new List<string>();
        /// <summary>Belegung der Actionbar (Bank A und B). Tasten/Modus sind Einstellungen des Rechners, nicht hier.</summary>
        public List<ActionBarEntry> actionBar = new List<ActionBarEntry>();
        /// <summary>
        /// Seit v5: wo der Charakter die Welt verlassen hat (Region, Zelle, Position, Blick, Weltversion). Leer = am
        /// Wachtfeuer beginnen. Passt die Weltversion nicht mehr, gilt der naechste sichere Anker der Zelle (Masterplan §47–49).
        /// </summary>
        public Ashveil.World.WorldPosition position;
        /// <summary>Seit v6: Praxis-Meisterschaften (Lernpunkte je Meisterschaft) und Lebenszeit-Statistik. Alte Staende beginnen bei 0.</summary>
        public List<MasterySave> mastery = new List<MasterySave>();
        /// <summary>Seit v10: gelernte Klassenfaehigkeiten (Rang 1 der Startfaehigkeiten kommt beim Anmelden dazu).</summary>
        public List<SpellSave> spells = new List<SpellSave>();
        /// <summary>Seit v10: Talente (ab Stufe 10 ein Punkt je Stufe) und wie oft sie zurueckgesetzt wurden.</summary>
        public List<TalentSave> talents = new List<TalentSave>();
        public int talentResets;
        public LifetimeStats lifetime = new LifetimeStats();
        /// <summary>Klassenreittier (leer = keines). Bindung steht als Eintrag in <see cref="mastery"/>.</summary>
        public MountSave mount;
        /// <summary>Freigeschaltete Klassen-Signaturen ("salvage" = Nachlese des Heilers).</summary>
        public List<string> signatures = new List<string>();
        public CompanionSave companion;
        /// <summary>Seit v8: Geschichts- und Weltmarken (Meilensteine, Szenen gesehen …) – nur Kennungen.</summary>
        public List<string> storyFlags = new List<string>();
        /// <summary>Seit v8: Pferde-Geschichte (leer = nie begonnen).</summary>
        public HorseStorySave horseStory = new HorseStorySave();
        /// <summary>
        /// Seit v11: entdeckte Orte (Kennungen site.* der Quest-Orte) – je Ort gibt es einmal Entdeckungs-Erfahrung (wie Classic).
        /// Alte Staende beginnen leer: bekannte Orte zaehlen beim naechsten Betreten einmal als Entdeckung.
        /// </summary>
        public List<string> discoveredAreas = new List<string>();
        /// <summary>Seit v12: angesammelte Ruhe (Zusatz-EP aus Siegen, wie Classic). Alte Staende beginnen bei 0.</summary>
        public int restedXp;
        /// <summary>Seit v12: letzter Ruhe-Zeitstempel (Unix-Sekunden, Serverzeit) – daraus die Ruhe nach dem Abmelden. 0 = noch nie.</summary>
        public long restStampUnix;
        /// <summary>Seit v12: stand der Charakter beim letzten Zeitstempel in einem Gasthaus (volle Rate statt einem Viertel)?</summary>
        public bool restInRestArea;
        public string savedAtUtc;

        public bool HasFlag(string flag) => storyFlags != null && storyFlags.Contains(flag);

        public int ProfessionSkillOf(string professionId)
        {
            foreach (ProfessionSkill p in professions) if (p.id == professionId) return p.skill;
            return -1;
        }
    }
}
