using System;
using System.Collections.Generic;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// Etwas in der Welt, mit dem man in einer Geschichte handelt (Rechtsklick): Fohlen befreien, Weidetor oeffnen, Hand
    /// hinhalten. Nur sichtbar/anklickbar, wenn die Geschichte es gerade zulaesst (<see cref="Available"/>). Die Handlung ist eine
    /// Bitte an den Server (<see cref="Quests.StoryClient.Send"/>), der Entfernung und Stand selbst prueft.
    /// </summary>
    public sealed class StoryInteractable : MonoBehaviour
    {
        [SerializeField] private string action;
        [SerializeField] private string labelKey;
        [SerializeField] private float range = 4f;

        private static readonly List<StoryInteractable> Registry = new List<StoryInteractable>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();

        /// <summary>Darf man gerade? (vom Besitzer gesetzt – z. B. „Stand = Fohlen gefunden“).</summary>
        public Func<bool> Condition;

        public string Action => action;
        public string Label => Loc.T(labelKey);
        public float Range => range;
        public bool Available => Condition == null || Condition();
        public static IReadOnlyList<StoryInteractable> All => Registry;

        public void Configure(string actionId, string label, float useRange, Func<bool> condition = null)
        {
            action = actionId;
            labelKey = label;
            range = useRange;
            Condition = condition;
        }

        /// <summary>Klickflaeche (Trigger, blockiert keine Bewegung) auf der Interaktions-Ebene.</summary>
        public static StoryInteractable Add(GameObject host, string actionId, string label, float useRange, Vector3 size, Vector3 center, Func<bool> condition = null)
        {
            var si = host.GetComponent<StoryInteractable>();
            if (si == null) si = host.AddComponent<StoryInteractable>();
            si.Configure(actionId, label, useRange, condition);
            var box = host.GetComponent<BoxCollider>();
            if (box == null) box = host.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            box.center = center;
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0) host.layer = layer;
            return si;
        }

        private void OnEnable() { if (!Registry.Contains(this)) Registry.Add(this); }
        private void OnDisable() => Registry.Remove(this);

        /// <summary>Naechste verfuegbare Handlung in Reichweite (fuer den Hinweis am Bildschirm).</summary>
        public static StoryInteractable NearestAvailable(Vector3 from, float extra = 0f)
        {
            StoryInteractable best = null;
            float bd = float.MaxValue;
            foreach (StoryInteractable s in Registry)
            {
                if (s == null || !s.isActiveAndEnabled || !s.Available) continue;
                float d = Vector3.Distance(s.transform.position, from);
                if (d > s.range + extra || d >= bd) continue;
                bd = d;
                best = s;
            }
            return best;
        }
    }
}
