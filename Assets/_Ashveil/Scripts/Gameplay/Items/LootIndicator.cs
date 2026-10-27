using UnityEngine;

namespace Ashveil.Gameplay.Items
{
    /// <summary>
    /// Client: glimmende Funken ueber einer Leiche, wenn sie Beute fuer den eigenen Spieler hat (golden), oder – nur
    /// fuer einen Heiler mit Nachlese – etwas zu bergen (blassgruen, erst wenn die Beute leer ist). Rein optisch;
    /// Rechtsklick oeffnet das Beutefenster bzw. beginnt das Bergen.
    /// </summary>
    public sealed class LootIndicator : MonoBehaviour
    {
        [SerializeField] private float height = 0.9f;
        [SerializeField] private Color color = new Color(1f, 0.78f, 0.3f);
        [Tooltip("Leuchtmaterial (wird vom Prefab-Bau gesetzt, damit es auch im Build vorhanden ist)")]
        [SerializeField] private Material moteMaterial;

        private Transform _root;
        private Transform[] _motes;
        private bool _available, _salvage;
        private Material _lootMat, _salvageMat;
        private Light _light;
        private static readonly Color SalvageColor = new Color(0.55f, 1f, 0.72f);

        public bool Available => _available;
        /// <summary>Nur beim berechtigten Heiler: hier ist noch etwas zu bergen.</summary>
        public bool SalvageAvailable => _salvage;

        public void SetAvailable(bool available)
        {
            _available = available;
            Refresh();
        }

        public void SetSalvage(bool available)
        {
            _salvage = available;
            Refresh();
        }

        private void Refresh()
        {
            bool show = _available || _salvage;
            if (show) EnsureBuilt();
            if (_root == null) return;
            _root.gameObject.SetActive(show);
            if (!show) return;
            // Erst die normale Beute (golden), danach die Nachlese (gruen) – wie Kuerschnern nach dem Pluendern
            bool green = !_available && _salvage;
            Material m = green ? SalvageMaterial() : _lootMat;
            foreach (Transform t in _motes) t.GetComponent<Renderer>().sharedMaterial = m;
            _light.color = green ? SalvageColor : color;
        }

        private Material SalvageMaterial()
        {
            if (_salvageMat != null) return _salvageMat;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            _salvageMat = _lootMat != null ? new Material(_lootMat) : new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            _salvageMat.color = SalvageColor;
            if (_salvageMat.HasProperty("_BaseColor")) _salvageMat.SetColor("_BaseColor", SalvageColor);
            if (_salvageMat.HasProperty("_EmissionColor")) _salvageMat.SetColor("_EmissionColor", SalvageColor * 2f);
            return _salvageMat;
        }

        private void OnDestroy()
        {
            if (_salvageMat != null) Destroy(_salvageMat);
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new GameObject("Loot Glimmer").transform;
            _root.SetParent(transform, false);
            _root.localPosition = Vector3.up * height;
            Material mat = moteMaterial;
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                mat = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { color = color };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            }
            _lootMat = mat;
            _motes = new Transform[5];
            for (int i = 0; i < _motes.Length; i++)
            {
                GameObject m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(m.GetComponent<Collider>());
                m.name = "Mote";
                m.transform.SetParent(_root, false);
                m.transform.localScale = Vector3.one * 0.07f;
                m.GetComponent<Renderer>().sharedMaterial = mat;
                _motes[i] = m.transform;
            }
            var light = _root.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 2.5f;
            light.intensity = 1.2f;
            _light = light;
        }

        private void Update()
        {
            if (!(_available || _salvage) || _motes == null) return;
            float t = Time.time;
            for (int i = 0; i < _motes.Length; i++)
            {
                float a = t * 1.3f + i * Mathf.PI * 2f / _motes.Length;
                float r = 0.35f + 0.08f * Mathf.Sin(t * 2f + i);
                _motes[i].localPosition = new Vector3(Mathf.Cos(a) * r, 0.15f * Mathf.Sin(t * 2.4f + i * 1.7f), Mathf.Sin(a) * r);
            }
        }
    }
}
