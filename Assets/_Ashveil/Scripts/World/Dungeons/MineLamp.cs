using System.Collections.Generic;
using Ashveil.Core;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Grubenlampe der Herzkaverne (Grubenherz, M6.4) – Darstellung auf jedem Rechner. Brennt oder ist erloschen,
    /// je nach Signal „lampN“ der Begegnung; „reset“ zuendet sie wieder an. Den Zustand fuehrt der Server.
    /// </summary>
    public sealed class MineLamp : MonoBehaviour
    {
        private static readonly List<MineLamp> All = new List<MineLamp>();

        [SerializeField] private int index;
        [SerializeField] private Material litMaterial;
        [SerializeField] private Material darkMaterial;
        [SerializeField] private Renderer glass;

        private Light[] _lights;
        private float[] _intensity;
        private DungeonSlot _slot;
        private bool _lit = true;
        private float _fade = 1f;

        public int Index => index;
        public bool IsLit => _lit;

        /// <summary>Lampen im angegebenen Instanz-Platz, nach Nummer sortiert (Tests).</summary>
        public static List<MineLamp> In(DungeonSlot slot)
        {
            var list = new List<MineLamp>();
            foreach (MineLamp l in All)
                if (l != null && l.Slot == slot) list.Add(l);
            list.Sort((a, b) => a.index.CompareTo(b.index));
            return list;
        }

        private DungeonSlot Slot => _slot != null ? _slot : (_slot = DungeonSlot.At(transform.position));

        private void Awake()
        {
            _lights = GetComponentsInChildren<Light>(true);
            _intensity = new float[_lights.Length];
            for (int i = 0; i < _lights.Length; i++) _intensity[i] = _lights[i].intensity;
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            EncounterCues.Received += OnCue;
        }

        private void OnDisable()
        {
            All.Remove(this);
            EncounterCues.Received -= OnCue;
        }

        private void OnCue(Vector3 where, string cue, float value)
        {
            if (Slot == null || DungeonSlot.At(where) != Slot) return;
            if (cue == EncounterCues.Reset) Set(true);
            else if (cue == EncounterCues.Lamp(index)) Set(value > 0.5f);
        }

        private void Set(bool lit)
        {
            if (_lit == lit) return;
            _lit = lit;
            if (glass != null) glass.sharedMaterial = lit ? litMaterial : darkMaterial;
        }

        private void Update()
        {
            float goal = _lit ? 1f : 0f;
            if (Mathf.Approximately(_fade, goal)) return;
            // Erloeschen flackert kurz, Anzuenden geht schnell
            _fade = Mathf.MoveTowards(_fade, goal, (_lit ? 3f : 1.2f) * Time.deltaTime);
            float flicker = !_lit && _fade > 0.05f ? Random.Range(0.4f, 1f) : 1f;
            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i] != null) _lights[i].intensity = _intensity[i] * _fade * flicker;
        }
    }
}
