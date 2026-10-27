using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Strohpuppe zum Testen von Waffen: faellt nie um und fuellt ihr Leben
    /// nach kurzer Ruhe wieder auf. Zeigt so Schaden pro Sekunde ehrlich an.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class TrainingDummy : MonoBehaviour
    {
        [SerializeField] private float resetAfterSeconds = 5f;

        private Health _health;

        private void Awake()
        {
            _health = GetComponent<Health>();
        }

        private void Update()
        {
            if (_health.IsReplica) return; // der Server setzt die Puppe zurueck
            if (_health.Current < _health.Max && Time.time - _health.LastDamageTime > resetAfterSeconds)
                _health.ResetToFull();
        }
    }
}
