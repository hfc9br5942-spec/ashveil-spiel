using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Story;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>
    /// „Lockrufe nach draußen“ (WORLD-239, Max 10.10.2026): Figuren des Lagers reden untereinander über echte Orte in Kareth
    /// (<see cref="KarethGespraeche"/>). Ein Gespräch läuft, wenn seine Figuren beieinanderstehen (Tagesablauf) und die eigene
    /// Figur nahe ist; die Zeilen stehen nacheinander als Schild über dem Kopf des Sprechers (WorldLabel, Stil „Ort“, nur in der
    /// Nähe lesbar). Höchstens ein Gespräch zur Zeit, je Gespräch eine Pause von einigen Minuten – Hintergrund, kein Fenster.
    /// Rein am Client, ohne Netz und ohne Server (jeder Spieler hört es zu seiner Zeit; es ist Stimmung, kein Spielzustand).
    /// </summary>
    public sealed class NpcGespraeche : MonoBehaviour
    {
        private const float Hoerweite = 18f, Naehe = 12f, Pause = 170f, Abstand = 22f, Takt = 0.75f;

        private KarethGespraeche.Gespraech[] _liste;
        private readonly Dictionary<string, NpcRoutine> _figuren = new Dictionary<string, NpcRoutine>();
        private float[] _bereitAb;
        private float _takt, _naechstesAb, _zeileBis;
        private int _laufend = -1, _zeile = -1;
        private WorldLabel _schild;
        private readonly List<int> _kandidaten = new List<int>();

        /// <summary>Gespräch, das gerade läuft (Tests); leer = keins.</summary>
        public string Laufend => _laufend >= 0 && _liste != null ? _liste[_laufend].Id : "";
        public int Gespielt { get; private set; }
        public string LetzteZeile { get; private set; } = "";

        public void Init(KarethGespraeche.Gespraech[] liste)
        {
            _liste = liste ?? new KarethGespraeche.Gespraech[0];
            _bereitAb = new float[_liste.Length];
            for (int i = 0; i < _bereitAb.Length; i++) _bereitAb[i] = Time.time + 8f + (i % 4) * 5f;   // nicht alle auf einmal
        }

        private void Awake()
        {
            if (_liste == null) Init(KarethGespraeche.Alle());
        }

        private void Update()
        {
            if (_liste == null || _liste.Length == 0) return;
            if (_laufend >= 0) { Weiter(); return; }
            _takt -= Time.deltaTime;
            if (_takt > 0f || Time.time < _naechstesAb) return;
            _takt = Takt;
            Transform spieler = LocalPlayerRef.Transform;
            if (spieler == null) return;
            Vector3 p = spieler.position;
            _kandidaten.Clear();
            for (int i = 0; i < _liste.Length; i++)
            {
                if (Time.time < _bereitAb[i]) continue;
                if (Beieinander(_liste[i], p)) _kandidaten.Add(i);
            }
            if (_kandidaten.Count == 0) return;
            Starten(_kandidaten[Random.Range(0, _kandidaten.Count)]);
        }

        /// <summary>Alle Figuren des Gesprächs sind da, wach, nahe beieinander und in Hörweite der eigenen Figur.</summary>
        private bool Beieinander(in KarethGespraeche.Gespraech g, Vector3 spieler)
        {
            Vector3 erste = Vector3.zero;
            for (int k = 0; k < g.Figuren.Length; k++)
            {
                NpcRoutine r = Figur(g.Figuren[k]);
                if (r == null || r.Sleeping || r.Walking) return false;
                Vector3 pos = r.transform.position;
                if (k == 0)
                {
                    erste = pos;
                    if ((pos - spieler).sqrMagnitude > Hoerweite * Hoerweite) return false;
                }
                else if ((pos - erste).sqrMagnitude > Naehe * Naehe) return false;
            }
            return true;
        }

        private NpcRoutine Figur(string name)
        {
            if (_figuren.TryGetValue(name, out NpcRoutine r) && r != null) return r;
            r = NpcRoutineRegistry.Find(name);
            if (r != null) _figuren[name] = r;
            return r;
        }

        private void Starten(int i)
        {
            _laufend = i;
            _zeile = -1;
            _zeileBis = 0f;
            Gespielt++;
            Weiter();
        }

        private void Weiter()
        {
            if (Time.time < _zeileBis) return;
            KarethGespraeche.Gespraech g = _liste[_laufend];
            _zeile++;
            if (_zeile >= g.Zeilen.Length) { Beenden(); return; }
            KarethGespraeche.Zeile z = g.Zeilen[_zeile];
            NpcRoutine r = z.Wer >= 0 && z.Wer < g.Figuren.Length ? Figur(g.Figuren[z.Wer]) : null;
            if (r == null) { Beenden(); return; }
            string text = Loc.T(z.Key);
            LetzteZeile = text;
            Zeigen(r, text);
            _zeileBis = Time.time + Mathf.Clamp(2.2f + text.Length / 14f, 2.5f, 7f);
        }

        private void Zeigen(NpcRoutine r, string text)
        {
            if (_schild == null)
            {
                var go = new GameObject("Gespräch");
                _schild = go.AddComponent<WorldLabel>();
            }
            Transform t = _schild.transform;
            t.SetParent(r.transform, false);
            t.localPosition = new Vector3(0f, 2.75f, 0f);     // über dem Namensschild (2,15 m)
            _schild.Configure(text, "", WorldLabelStyle.Place, Hoerweite + 4f, 0.85f);
            _schild.gameObject.SetActive(true);
        }

        private void Beenden()
        {
            if (_schild != null) _schild.gameObject.SetActive(false);
            if (_laufend >= 0) _bereitAb[_laufend] = Time.time + Pause + Random.Range(0f, 40f);
            _laufend = -1;
            _zeile = -1;
            _naechstesAb = Time.time + Abstand;
        }

        private void OnDisable()
        {
            if (_laufend >= 0) Beenden();
        }
    }
}
