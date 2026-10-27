using UnityEngine;

namespace Ashveil.World
{
    /// <summary>Flackerndes Feuerlicht (Lagerfeuer, Laternen, Glutspalten).</summary>
    [RequireComponent(typeof(Light))]
    public sealed class LightFlicker : MonoBehaviour
    {
        [SerializeField] private float intensityVariation = 0.25f;
        [SerializeField] private float rangeVariation = 0.08f;
        [SerializeField] private float speed = 7f;

        /// <summary>M8: nachts leuchten Feuer und Laternen kraeftiger (0 = Tag, 1 = Nacht; setzt DayNightCycle).</summary>
        public static float NightBoost;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => NightBoost = 0f;

        private Light _light;
        private float _baseIntensity;
        private float _baseRange;
        private float _seed;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _baseIntensity = _light.intensity;
            _baseRange = _light.range;
            _seed = Random.value * 100f;
        }

        private void Update()
        {
            float n = Mathf.PerlinNoise(_seed, Time.time * speed) - 0.5f;
            _light.intensity = _baseIntensity * (1f + n * 2f * intensityVariation) * (1f + 0.7f * NightBoost);
            _light.range = _baseRange * (1f + n * 2f * rangeVariation) * (1f + 0.3f * NightBoost);
        }
    }
}
