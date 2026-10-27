using System;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Logischer Zustand der Pferde-Geschichte des menschlichen Tanks (Vorgabe §HorseStoryState). Erst der Zustand,
    /// die Darstellung (Fohlen auf der Weide, erwachsenes Pferd, gealterter Hof, Epilog) leitet sich daraus ab.
    /// Werte nie umnummerieren – sie stehen als Zahl im Speicherstand.
    /// </summary>
    public enum HorseStoryState
    {
        Unknown = 0,
        FoalMissing = 1,
        FoalFound = 2,
        FoalRescued = 3,
        ReturnedToFarm = 4,
        Growing = 5,
        AdultAtFarm = 6,
        Released = 7,
        BondedToPlayer = 8
    }

    /// <summary>Fellgrundton (Presets statt freier Farbe – das Tier bleibt immer dasselbe).</summary>
    public enum HorseCoatPreset { DarkBay = 0, BrownBlack = 1, Chestnut = 2, Grey = 3, Dun = 4, Bay = 5 }
    public enum HorseBlazePattern { None = 0, Star = 1, NarrowBroken = 2, Wide = 3 }
    public enum HorseLegMarkings { None = 0, LeftHindSock = 1, BothHindSocks = 2, FourSocks = 3 }
    public enum HorseEyePreset { Dark = 0, Amber = 1 }
    public enum HorseScarPattern { None = 0, RightShoulderFresh = 1, RightShoulderHealed = 2 }
    public enum HorseGender { Mare = 0, Stallion = 1, Gelding = 2 }

    /// <summary>
    /// Feste Identitaet eines Story-Pferdes (nie pro Spawn zufaellig). Fohlen und erwachsenes Tier teilen sie – nur die Narbe
    /// ist beim Fohlen frisch und beim erwachsenen Tier verheilt.
    /// </summary>
    [Serializable]
    public sealed class StoryHorseIdentity
    {
        public string horseStoryId;
        public HorseCoatPreset coat;
        public HorseBlazePattern blaze;
        public HorseLegMarkings legs;
        public HorseEyePreset eyes;
        public HorseScarPattern scar;
        public HorseGender gender;
        public int appearanceSeed;
        /// <summary>Widerristhoehe erwachsen (m).</summary>
        public float adultWithers = 1.72f;

        public StoryHorseIdentity Clone() => (StoryHorseIdentity)MemberwiseClone();
    }

    /// <summary>Kennungen und feste Werte der Geschichte „Wo die Herde floh“ / „Der letzte Weidegang“.</summary>
    public static class HorseStoryCatalog
    {
        public const string StoryHorseId = "storyhorse.human_tank_01";
        public const string BreederNpcId = "npc.human.horsebreeder_01";
        public const string FoalRescueQuest = "quest.human_tank.foal_rescue";
        public const string LastPastureQuest = "quest.human_tank.last_pasture";

        // Welt-/Geschichtsmarken (storyFlags im Speicherstand)
        public const string FlagFoalRescueCompleted = "story.horse.foal_rescue_completed";
        public const string FlagStoryHorseMetPlayer = "story.horse.met_player";
        public const string FlagMessengerHeard = "story.horse.messenger_heard";
        /// <summary>Fruehe Einfuehrung (Wachtfeuer) – Voraussetzung fuer Q1.</summary>
        public const string MilestoneIntro = "milestone.kareth.arrived";
        /// <summary>Hauptgeschichte (Salzgruben-Finale) – Voraussetzung fuer Q2.</summary>
        public const string MilestoneMainStory = "milestone.story.salt_heart";

        public const int FoalRescueMinLevel = 3;
        public const int LastPastureMinLevel = 20;
        /// <summary>Epilog (stiller Hof, Grab) fruehestens so viele Echtzeitstunden nach der Bindung – und erst beim naechsten Login.</summary>
        public const double EpilogueDelayHours = 2.0;

        /// <summary>
        /// Das Fohlen / Kriegspferd: sehr dunkles Braunschwarz, schmale gebrochene helle Blesse, Verletzung an der rechten
        /// Schulter (erwachsen: helle Narbe), 1,70–1,75 m Widerrist, Hengst. Keine Aura, keine Panzerung.
        /// </summary>
        public static readonly StoryHorseIdentity Warhorse = new StoryHorseIdentity
        {
            horseStoryId = StoryHorseId,
            coat = HorseCoatPreset.BrownBlack,
            blaze = HorseBlazePattern.NarrowBroken,
            legs = HorseLegMarkings.None,
            eyes = HorseEyePreset.Dark,
            scar = HorseScarPattern.RightShoulderHealed,
            gender = HorseGender.Stallion,
            appearanceSeed = 71413,
            adultWithers = 1.73f
        };

        /// <summary>Fellfarbe eines Presets (Greybox-Darstellung).</summary>
        public static Color CoatColor(HorseCoatPreset p)
        {
            switch (p)
            {
                case HorseCoatPreset.BrownBlack: return new Color(0.085f, 0.06f, 0.05f);
                case HorseCoatPreset.Chestnut: return new Color(0.36f, 0.17f, 0.08f);
                case HorseCoatPreset.Grey: return new Color(0.55f, 0.54f, 0.52f);
                case HorseCoatPreset.Dun: return new Color(0.5f, 0.4f, 0.26f);
                case HorseCoatPreset.Bay: return new Color(0.3f, 0.16f, 0.09f);
                default: return new Color(0.2f, 0.13f, 0.09f);
            }
        }

        /// <summary>Mähne/Schweif zum Preset.</summary>
        public static Color ManeColor(HorseCoatPreset p)
        {
            switch (p)
            {
                case HorseCoatPreset.Grey: return new Color(0.7f, 0.69f, 0.66f);
                case HorseCoatPreset.Chestnut: return new Color(0.42f, 0.2f, 0.1f);
                case HorseCoatPreset.Dun: return new Color(0.12f, 0.1f, 0.08f);
                default: return new Color(0.04f, 0.035f, 0.03f);
            }
        }

        public static bool AtLeast(HorseStoryState s, HorseStoryState min) => (int)s >= (int)min;
    }
}
