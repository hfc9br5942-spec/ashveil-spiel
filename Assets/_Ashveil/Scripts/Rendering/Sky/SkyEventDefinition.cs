using System;
using UnityEngine;

namespace Ashveil.Rendering.Sky
{
    /// <summary>Seltenes Himmelsereignis (§ 29) – deterministisch aus dem Seed. (Pass 12)</summary>
    [CreateAssetMenu(menuName = "Ashveil/Himmel/Himmelsereignis", fileName = "SkyEvent_")]
    public sealed class SkyEventDefinition : ScriptableObject
    {
        public string id = "meteor_shower";
        public string displayName = "Sternschnuppenschauer";
        [Tooltip("Chance je Welt-Tag, dass eine Nacht ein Schauer hat")] [Range(0f, 1f)] public float chancePerDay = 0.05f;
        public Vector2 startHour = new Vector2(22f, 2f);
        public Vector2 durationHours = new Vector2(1.5f, 4f);
        [Range(0f, 1f)] public float intensity = 0.6f;
        [Tooltip("Meteore je Spielminute im Hoehepunkt")] public float frequency = 3f;
        public Vector2 radiant = new Vector2(60f, 40f); // Rektaszension/Deklination
        public float trailLifetime = 0.8f;
    }
}
