using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>Bewegungssprache einer Rasse (§20). Unbestaetigte Rassen nur als Vorbereitung.</summary>
    [CreateAssetMenu(menuName = "Ashveil/Motion/Rasse", fileName = "MotionRace_")]
    public sealed class RaceMotionProfile : MotionLayerProfile
    {
        [Tooltip("Rassen-ID aus RaceCatalog (z. B. race.human).")]
        public string raceId = "race.human";
        [Tooltip("Nur Architektur – noch keine fertige Bewegungssprache.")]
        public bool provisional;
        [Tooltip("Versatz der Waffensockel je Rasse (Proportionen).")]
        public Vector3 socketOffset;
    }
}
