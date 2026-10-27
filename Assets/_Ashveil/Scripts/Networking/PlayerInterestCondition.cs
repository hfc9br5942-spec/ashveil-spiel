using FishNet.Connection;
using FishNet.Object;
using FishNet.Observing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Large-World N3: wann ein Client eine andere Spielerfigur bekommt. Sichtbar, wenn
    ///   · es die eigene Figur ist,
    ///   · eine Figur des Clients naeher als <see cref="maximumDistance"/> ist (10 % Hysterese) und im selben
    ///     Instanz-Platz steht (Kolosseum Pass 2: Nachbar-Instanzen liegen nur 300 m auseinander), oder
    ///   · beide in derselben Gruppe sind (Gruppenmitglieder bleiben ueber Regionen und Instanzen hinweg sichtbar –
    ///     "wo ist mein Freund?" wie in Classic).
    /// Chat, Gruppenfenster und Fluestern laufen ueber den Server und brauchen keine Sichtbarkeit.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Netzwerk/Sicht Spieler", fileName = "Sicht_Spieler")]
    public sealed class PlayerInterestCondition : ObserverCondition
    {
        [SerializeField] private float maximumDistance = 500f;
        [SerializeField] [Range(0f, 0.5f)] private float hidePercent = 0.1f;

        public void Configure(float distance, float hide)
        {
            maximumDistance = distance;
            hidePercent = hide;
        }

        public override bool ConditionMet(NetworkConnection connection, bool currentlyAdded, out bool notProcessed)
        {
            notProcessed = false;
            if (NetworkObject == null) return false;
            if (NetworkObject.Owner == connection) return true;
            float max = maximumDistance * (currentlyAdded ? 1f + hidePercent : 1f);
            float sqr = max * max;
            Vector3 here = NetworkObject.transform.position;
            Ashveil.World.DungeonSlot slot = null;
            bool slotKnown = false;
            foreach (NetworkObject nob in connection.Objects)
            {
                if (nob == null || (nob.transform.position - here).sqrMagnitude > sqr) continue;
                if (!slotKnown) { slot = Ashveil.World.DungeonSlot.At(here); slotKnown = true; }
                if (Ashveil.World.DungeonSlot.At(nob.transform.position) == slot) return true;
            }

            // gleiche Gruppe?
            var mine = NetworkObject.GetComponent<NetworkInventory>();
            string me = mine != null ? mine.ProfileId : null;
            if (string.IsNullOrEmpty(me)) return false;
            PartyService.Party party = PartyService.Of(me);
            if (party == null) return false;
            foreach (NetworkObject nob in connection.Objects)
            {
                var inv = nob != null ? nob.GetComponent<NetworkInventory>() : null;
                if (inv != null && !string.IsNullOrEmpty(inv.ProfileId) && party.Members.Contains(inv.ProfileId)) return true;
            }
            return false;
        }

        public override ObserverConditionType GetConditionType() => ObserverConditionType.Timed;
    }
}
