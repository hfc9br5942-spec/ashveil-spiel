using UnityEngine;

namespace Ashveil.FloraValidation
{
    /// <summary>
    /// Globale Wind- und Saisonparameter der Flora-Validation (nur Testszene). Setzt ausschliesslich globale Shader-Werte –
    /// keine CPU-Steuerung einzelner Blaetter oder Baeume, damit Wind und Saison GPU-basiert und instancing-kompatibel bleiben.
    /// Aufrufer: FloraValidationRunner (Presets), Inspector.
    /// </summary>
    [ExecuteAlways]
    public class FloraEnvironment : MonoBehaviour
    {
        public enum WindPreset { Windstille, Leicht, Normal, Stark }

        [Header("Wind")]
        public Vector3 windDirection = new Vector3(1f, 0f, 0.25f);
        [Range(0f, 1.5f)] public float windStrength = 0.35f;
        [Range(0f, 1f)] public float gust = 0.35f;
        public float flutter = 1f;
        public float branch = 1f;
        [Tooltip("Eigene Uhr (deterministisch in Clips, folgt Time.deltaTime)")]
        public float windTime;

        [Header("Saison")]
        [Range(0f, 1f)] public float season;
        [Range(0f, 1.01f)] public float leafFall;
        [Range(0f, 1f)] public float snow;
        [Range(0f, 0.3f)] public float regionalSpread = 0.08f;

        private static readonly int WindDirId = Shader.PropertyToID("_FV_WindDir");
        private static readonly int WindParamsId = Shader.PropertyToID("_FV_WindParams");
        private static readonly int SeasonId = Shader.PropertyToID("_FV_SeasonParams");

        public void ApplyPreset(WindPreset p)
        {
            switch (p)
            {
                case WindPreset.Windstille: windStrength = 0f; gust = 0f; break;
                case WindPreset.Leicht: windStrength = 0.2f; gust = 0.2f; break;
                case WindPreset.Normal: windStrength = 0.45f; gust = 0.5f; break;
                case WindPreset.Stark: windStrength = 0.85f; gust = 0.7f; break;
            }
        }

        private void Update()
        {
            if (Application.isPlaying) windTime += Time.deltaTime;
            Push();
        }

        private void OnValidate() => Push();
        private void OnEnable() => Push();

        public void Push()
        {
            Vector3 d = windDirection.sqrMagnitude > 1e-6f ? windDirection.normalized : Vector3.right;
            Shader.SetGlobalVector(WindDirId, new Vector4(d.x, d.y, d.z, windStrength));
            Shader.SetGlobalVector(WindParamsId, new Vector4(windTime, gust, flutter, branch));
            Shader.SetGlobalVector(SeasonId, new Vector4(season, leafFall, snow, regionalSpread));
        }
    }
}
