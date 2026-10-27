using Ashveil.World;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Observing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Large-World N3 + Kolosseum Pass 2: wann ein Client einen Gegner bekommt. Sichtbar, wenn eine Figur des Clients
    ///   · naeher als <see cref="maximumDistance"/> ist (10 % Hysterese) UND
    ///   · im selben Instanz-Platz steht wie der Gegner (<see cref="DungeonSlot"/>; beide in der Oberwelt zaehlt als gleich).
    /// Instanz-Plaetze liegen nur 300 m auseinander – ohne die zweite Regel saehe eine Gruppe die Gegner der Nachbar-Instanz.
    /// M9.2 ergaenzt spaeter ein Raster; die Instanz-Regel bleibt.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Netzwerk/Sicht Gegner", fileName = "Sicht_Gegner")]
    public sealed class EnemyInterestCondition : ObserverCondition
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
            float max = maximumDistance * (currentlyAdded ? 1f + hidePercent : 1f);
            float sqr = max * max;
            Vector3 here = NetworkObject.transform.position;
            DungeonSlot mine = null;
            bool slotKnown = false;
            foreach (NetworkObject nob in connection.Objects)
            {
                if (nob == null) continue;
                Vector3 p = nob.transform.position;
                if ((p - here).sqrMagnitude > sqr) continue;
                if (!slotKnown) { mine = DungeonSlot.At(here); slotKnown = true; }
                if (DungeonSlot.At(p) == mine) return true;
            }
            return false;
        }

        public override ObserverConditionType GetConditionType() => ObserverConditionType.Timed;

        // ------------------------------------------------------------------ Absicherung beim Erzeugen

        private static EnemyInterestCondition _shared;
        private static readonly System.Reflection.FieldInfo ConditionsField =
            typeof(FishNet.Observing.NetworkObserver).GetField("_observerConditions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        private static bool _warned;

        /// <summary>
        /// Server, vor dem Spawn eines Gegners: stellt sicher, dass sein NetworkObserver die Instanz-Regel hat.
        /// Befund 28.09.: Zur Laufzeit kam die Regel aus dem Prefab nicht an (0 von 101 Gegnern) – jeder Client bekam
        /// alle Gegner. Deshalb setzt der Server sie hier selbst (eine gemeinsame Vorlage; FishNet klont sie je Objekt).
        /// </summary>
        public static void Ensure(GameObject go, float distance = 180f)   // 11.10.2026: 180 m (vorher 300, davor 500) – Classic-Dichte (≈ 3 650 Gegner in Kareth); Classic zeigt Gegner auf ≈ 100 yd
        {
            var observer = go != null ? go.GetComponent<FishNet.Observing.NetworkObserver>() : null;
            if (observer == null || ConditionsField == null) return;
            var list = ConditionsField.GetValue(observer) as System.Collections.Generic.List<ObserverCondition>;
            if (list == null) return;
            foreach (ObserverCondition c in list)
                if (c != null && (c is EnemyInterestCondition || c is StoryPhaseCondition)) return; // Geschichts-Objekte: eigene Regel
            if (_shared == null)
            {
                _shared = CreateInstance<EnemyInterestCondition>();
                _shared.hideFlags = HideFlags.DontSave;
                _shared.name = "Sicht_Gegner (Laufzeit)";
                _shared.Configure(distance, 0.1f);
            }
            if (!_warned)
            {
                _warned = true;
                Debug.Log($"[Ashveil] Server: Instanz-Sichtregel fehlte am Gegner '{go.name}' ({list.Count} Eintraege im Prefab) – wird zur Laufzeit gesetzt.");
            }
            list.Clear();
            list.Add(_shared);
        }
    }
}
