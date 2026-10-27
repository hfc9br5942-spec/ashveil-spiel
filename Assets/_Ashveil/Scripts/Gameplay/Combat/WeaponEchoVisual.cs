using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Lesbarkeit des „Nachhall“: kurzer Lichtpuls in den Spalten der Klinge (Energie-Material), auf allen Clients.
    /// Liegt auf dem Spielmodell der Waffe; ausgeloest ueber <see cref="PulseOn"/> (Netz-RPC am Ziel).
    /// Ohne Puls ist die Komponente deaktiviert (kein Update, SRP-Batching unveraendert).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponEchoVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly List<WeaponEchoVisual> Found = new List<WeaponEchoVisual>(4);

        /// <summary>Standardwerte (Handprobe 04.10.: 2,4-fach war auf Spielkamera-Entfernung kaum zu sehen).</summary>
        public const float PeakDefault = 3.5f, DecayDefault = 0.55f;

        [Tooltip("Materialname-Anfang der Energie in den Spalten")]
        public string energyMaterialPrefix = "Nachhall_Energy";
        [Tooltip("Helligkeit auf dem Hoehepunkt relativ zur Grundhelligkeit")]
        public float peakFactor = PeakDefault;
        public float riseTime = 0.08f;
        public float decayTime = DecayDefault;

        private struct Slot { public Renderer R; public int Index; public Color Base; }
        private readonly List<Slot> _slots = new List<Slot>();
        private MaterialPropertyBlock _mpb;
        private float _t = -1f;
        private bool _collected;

        /// <summary>Alle Puls-Komponenten unterhalb der Figur ausloesen (getragene Waffe).</summary>
        public static void PulseOn(GameObject root)
        {
            if (root == null) return;
            Found.Clear();
            root.GetComponentsInChildren(true, Found);
            foreach (WeaponEchoVisual v in Found) v.Pulse();
            Found.Clear();
        }

        /// <summary>Zuletzt ausgeloeste Pulse (Selbsttest).</summary>
        public static int PulseCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PulseCount = 0;

        private void Awake() => enabled = false;

        public void Pulse()
        {
            if (!isActiveAndEnabled && !gameObject.activeInHierarchy) return;
            Collect();
            if (_slots.Count == 0) return;
            PulseCount++;
            _t = 0f;
            enabled = true;
        }

        private void Collect()
        {
            if (_collected) return;
            _collected = true;
            _mpb = new MaterialPropertyBlock();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || !m.name.StartsWith(energyMaterialPrefix) || !m.HasProperty(BaseColorId)) continue;
                    _slots.Add(new Slot { R = r, Index = i, Base = m.GetColor(BaseColorId) });
                }
            }
        }

        private void Update()
        {
            if (_t < 0f) { enabled = false; return; }
            _t += Time.deltaTime;
            float total = riseTime + decayTime;
            if (_t >= total)
            {
                _t = -1f;
                foreach (Slot s in _slots) if (s.R != null) s.R.SetPropertyBlock(null, s.Index);
                enabled = false;
                return;
            }
            float k = _t < riseTime ? _t / Mathf.Max(1e-4f, riseTime) : 1f - (_t - riseTime) / Mathf.Max(1e-4f, decayTime);
            float f = 1f + (peakFactor - 1f) * Mathf.SmoothStep(0f, 1f, k);
            foreach (Slot s in _slots)
            {
                if (s.R == null) continue;
                _mpb.Clear();
                Color c = s.Base * f;
                c.a = s.Base.a;
                _mpb.SetColor(BaseColorId, c);
                s.R.SetPropertyBlock(_mpb, s.Index);
            }
        }

        private void OnDisable()
        {
            if (_t < 0f) return;
            _t = -1f;
            foreach (Slot s in _slots) if (s.R != null) s.R.SetPropertyBlock(null, s.Index);
        }
    }
}
