using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Observing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>Wem ein Geschichts-Objekt gehoert (Server setzt es vor dem Spawn; die Sichtregel liest es).</summary>
    [DisallowMultipleComponent]
    public sealed class StoryPhaseTag : MonoBehaviour
    {
        public string OwnerProfile;
        /// <summary>Gruppenmitglieder sehen es nur in dieser Naehe (m).</summary>
        public float PartyRange = 180f;
    }

    /// <summary>
    /// Phasing fuer Geschichts-Begegnungen (Vorgabe §Multiplayer): sichtbar nur fuer den Besitzer der Geschichte und seine
    /// Gruppenmitglieder in der Naehe. Fremde sehen weder Herde noch Raubtier – sie koennen nichts stehlen, verschieben oder
    /// ausloesen. Eine gemeinsame Vorlage fuer alle (FishNet klont sie je Objekt); die Daten stehen am Objekt (<see cref="StoryPhaseTag"/>).
    /// </summary>
    public sealed class StoryPhaseCondition : ObserverCondition
    {
        public override bool ConditionMet(NetworkConnection connection, bool currentlyAdded, out bool notProcessed)
        {
            notProcessed = false;
            if (NetworkObject == null) return false;
            StoryPhaseTag tag = NetworkObject.GetComponent<StoryPhaseTag>();
            if (tag == null || string.IsNullOrEmpty(tag.OwnerProfile)) return false;
            Vector3 here = NetworkObject.transform.position;
            float range = tag.PartyRange * (currentlyAdded ? 1.1f : 1f);
            PartyService.Party party = null;
            bool partyKnown = false;
            foreach (NetworkObject nob in connection.Objects)
            {
                var inv = nob != null ? nob.GetComponent<NetworkInventory>() : null;
                if (inv == null || string.IsNullOrEmpty(inv.ProfileId)) continue;
                if (inv.ProfileId == tag.OwnerProfile) return true;
                if (!partyKnown) { party = PartyService.Of(tag.OwnerProfile); partyKnown = true; }
                if (party != null && party.Members.Contains(inv.ProfileId) && (nob.transform.position - here).sqrMagnitude <= range * range) return true;
            }
            return false;
        }

        public override ObserverConditionType GetConditionType() => ObserverConditionType.Timed;

        private static StoryPhaseCondition _shared;
        private static readonly System.Reflection.FieldInfo ConditionsField =
            typeof(NetworkObserver).GetField("_observerConditions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        /// <summary>Server, vor dem Spawn: Objekt dem Besitzer (und seiner Gruppe) zuordnen – ersetzt die normale Sichtregel.</summary>
        public static void Apply(GameObject go, string ownerProfile)
        {
            if (go == null) return;
            StoryPhaseTag tag = go.GetComponent<StoryPhaseTag>();
            if (tag == null) tag = go.AddComponent<StoryPhaseTag>();
            tag.OwnerProfile = ownerProfile;
            var observer = go.GetComponent<NetworkObserver>();
            if (observer == null) observer = go.AddComponent<NetworkObserver>();
            if (ConditionsField == null) return;
            var list = ConditionsField.GetValue(observer) as List<ObserverCondition>;
            if (list == null)
            {
                list = new List<ObserverCondition>();
                ConditionsField.SetValue(observer, list);
            }
            if (_shared == null)
            {
                _shared = CreateInstance<StoryPhaseCondition>();
                _shared.hideFlags = HideFlags.DontSave;
                _shared.name = "Sicht_Geschichte (Laufzeit)";
            }
            list.Clear();
            list.Add(_shared);
            var so = observer;
            SetOverride(so);
        }

        private static readonly System.Reflection.FieldInfo OverrideField =
            typeof(NetworkObserver).GetField("_overrideType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static void SetOverride(NetworkObserver o)
        {
            // Nur diese Regel zaehlt (nicht zusaetzlich die globale Sichtverwaltung)
            if (OverrideField == null) return;
            object ignore = System.Enum.Parse(OverrideField.FieldType, "IgnoreManager");
            OverrideField.SetValue(o, ignore);
        }
    }
}
