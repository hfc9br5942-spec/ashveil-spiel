using UnityEngine;

namespace Ashveil.World
{
    /// <summary>Dreht ein Objekt langsam um eine lokale Achse (das Zahnrad des Uhrwerks).</summary>
    public sealed class SlowRotator : MonoBehaviour
    {
        [SerializeField] private Vector3 localAxis = Vector3.up;
        [Tooltip("Grad pro Sekunde")]
        [SerializeField] private float degreesPerSecond = 2f;
        [Tooltip("Optional ruckweise drehen wie ein Uhrwerk (Sekunden pro Ruck, 0 = gleichmaessig)")]
        [SerializeField] private float tickInterval = 0f;

        private float _timer;

        private void Update()
        {
            if (tickInterval <= 0f)
            {
                transform.Rotate(localAxis, degreesPerSecond * Time.deltaTime, Space.Self);
                return;
            }

            _timer += Time.deltaTime;
            if (_timer >= tickInterval)
            {
                _timer -= tickInterval;
                transform.Rotate(localAxis, degreesPerSecond * tickInterval, Space.Self);
            }
        }
    }
}
