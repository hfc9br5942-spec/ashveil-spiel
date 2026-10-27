using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Gameplay.Motion.Procedural;
using UnityEngine;
using UnityEngine.Audio;

namespace Ashveil.Gameplay.Audio
{
    /// <summary>Böden, nach denen Schritte klingen.</summary>
    public enum Boden { Gras, Kies, Fels, Matsch, Holz, Wasser }

    /// <summary>
    /// Schritte nach Boden (Claude 14.10.2026, Klang Schritt 4, TECH-125). Hört auf <see cref="CharacterMotionPoser.Fussaufsatz"/>
    /// (Fuss setzt auf – eigene Figur, andere Spieler, Figuren mit Beinen in voller Detailstufe), bestimmt den Boden unter dem Fuss
    /// und spielt einen zufälligen Schritt (10 Varianten je Boden, nie zweimal derselbe, Tonhöhe leicht gestreut) räumlich ab.
    /// Boden: flaches Wasser (WaterVolume) › Gelände (stärkste Malschicht unter dem Fuss, nach ihrem Namen) › Bauteil (Material-
    /// und Objektname). Ergebnis je Kollider/Schicht zwischengespeichert. MMO-Regeln (CORE-005): nur am Client (gehört zur
    /// KlangLandschaft), nur im Umkreis von <see cref="Reichweite"/> m um den Hörer, feste Zahl Quellen (Ringpuffer).
    /// In Höhlen über die Mischpult-Gruppe „Hoehle“ – Schritte hallen im Kreidekeller.
    /// </summary>
    public sealed class SchrittKlang
    {
        public const float Reichweite = 30f;
        private const int Quellen = 8;

        private readonly AudioClip[][] _clips;
        private readonly AudioSource[] _pool = new AudioSource[Quellen];
        private int _naechste;
        private readonly int[] _zuletzt = new int[6];
        private readonly Dictionary<Collider, Boden> _kollider = new Dictionary<Collider, Boden>();
        private readonly Dictionary<TerrainData, Boden[]> _schichten = new Dictionary<TerrainData, Boden[]>();
        private readonly Dictionary<CharacterMotionPoser, float> _letzter = new Dictionary<CharacterMotionPoser, float>();
        private static readonly float[] Laut = { 0.45f, 0.7f, 0.65f, 0.55f, 0.75f, 0.7f };

        /// <summary>Diagnose/Tests: Schritte bisher und der Boden des letzten Schritts.</summary>
        public int Anzahl { get; private set; }
        public Boden Letzter { get; private set; }

        public SchrittKlang(Transform eltern, AudioClip[][] clips, AudioMixerGroup gruppe)
        {
            _clips = clips;
            for (int i = 0; i < Quellen; i++)
            {
                var go = new GameObject("Klang Schritt " + i);
                go.transform.SetParent(eltern, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = false; s.spatialBlend = 1f; s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 1.5f; s.maxDistance = Reichweite;
                s.spatialize = true; s.priority = 96; s.outputAudioMixerGroup = gruppe;
                _pool[i] = s;
            }
            for (int i = 0; i < _zuletzt.Length; i++) _zuletzt[i] = -1;
        }

        public void Gruppe(AudioMixerGroup g)
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i].outputAudioMixerGroup = g;
        }

        /// <summary>Ein Fuss setzt auf.</summary>
        public void Aufsatz(CharacterMotionPoser wer, Vector3 fuss, float tempo, Vector3 hoerer, float umgebung)
        {
            if ((fuss - hoerer).sqrMagnitude > Reichweite * Reichweite) return;
            float jetzt = Time.time;
            if (_letzter.TryGetValue(wer, out float t) && jetzt - t < 0.16f) return;   // ein Aufsatz, nicht mehrere Zuckungen
            _letzter[wer] = jetzt;
            if (_letzter.Count > 256) _letzter.Clear();

            Boden b = Ermitteln(fuss);
            AudioClip[] c = _clips[(int)b];
            if (c == null || c.Length == 0) { b = Boden.Gras; c = _clips[0]; }
            if (c == null || c.Length == 0) return;
            int k = Random.Range(0, c.Length);
            if (c.Length > 1 && k == _zuletzt[(int)b]) k = (k + 1 + Random.Range(0, c.Length - 1)) % c.Length;
            _zuletzt[(int)b] = k;

            if (!KlangLandschaft.Bereit(c[k])) return;
            AudioSource s = _pool[_naechste];
            _naechste = (_naechste + 1) % _pool.Length;
            s.transform.position = fuss;
            s.pitch = Random.Range(0.94f, 1.06f);
            float v = Laut[(int)b] * Mathf.Lerp(0.75f, 1.1f, Mathf.InverseLerp(1.5f, 6f, tempo)) * umgebung;
            s.PlayOneShot(c[k], v);
            Anzahl++;
            Letzter = b;
        }

        private Boden Ermitteln(Vector3 fuss)
        {
            if (WaterVolume.TrySurface(fuss, out float wy, 2f) && wy > fuss.y + 0.03f && wy - fuss.y < 0.9f) return Boden.Wasser;
            if (!Physics.Raycast(fuss + Vector3.up * 0.35f, Vector3.down, out RaycastHit h, 0.9f, ~0, QueryTriggerInteraction.Ignore))
                return Letzter;
            if (h.collider is TerrainCollider tc) return Gelaende(tc.GetComponent<Terrain>(), h.point);
            Collider id = h.collider;
            if (_kollider.TryGetValue(id, out Boden bb)) return bb;
            var r = h.collider.GetComponent<Renderer>() ?? h.collider.GetComponentInParent<Renderer>();
            Transform tr = h.collider.transform;
            string n = (r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "") + " " + tr.name +
                       (tr.parent != null ? " " + tr.parent.name : "");
            bb = Wort(n, Boden.Fels);
            if (_kollider.Count > 4096) _kollider.Clear();
            _kollider[id] = bb;
            return bb;
        }

        private Boden Gelaende(Terrain t, Vector3 p)
        {
            if (t == null || t.terrainData == null) return Boden.Gras;
            TerrainData td = t.terrainData;
            if (!_schichten.TryGetValue(td, out Boden[] art))
            {
                TerrainLayer[] ls = td.terrainLayers;
                art = new Boden[ls.Length];
                for (int i = 0; i < ls.Length; i++) art[i] = Wort(ls[i] != null ? ls[i].name : "", Boden.Gras);
                _schichten[td] = art;
            }
            if (art.Length == 0) return Boden.Gras;
            Vector3 l = p - t.transform.position;
            int x = Mathf.Clamp((int)(l.x / td.size.x * td.alphamapWidth), 0, td.alphamapWidth - 1);
            int z = Mathf.Clamp((int)(l.z / td.size.z * td.alphamapHeight), 0, td.alphamapHeight - 1);
            float[,,] a = td.GetAlphamaps(x, z, 1, 1);
            int best = 0;
            for (int i = 1; i < a.GetLength(2) && i < art.Length; i++) if (a[0, 0, i] > a[0, 0, best]) best = i;
            return art[best];
        }

        /// <summary>Boden aus einem Namen (Malschicht, Material, Objekt).</summary>
        public static Boden Wort(string n, Boden sonst)
        {
            n = (n ?? "").ToLowerInvariant();
            if (Hat(n, "moor", "matsch", "schlamm", "mud", "sumpf", "ufer")) return Boden.Matsch;
            if (Hat(n, "holz", "wood", "diele", "brett", "planke", "steg", "brücke", "bruecke", "balken")) return Boden.Holz;
            if (Hat(n, "granit", "fels", "stein", "kreide", "rock", "stone", "pflaster", "mauer", "felsen")) return Boden.Fels;
            if (Hat(n, "kies", "gravel", "erdweg", "weg", "pfad", "sand", "strand", "salz", "asche", "schotter", "erde")) return Boden.Kies;
            if (Hat(n, "wiese", "gras", "grass", "wald", "heide", "moos", "feld", "korn")) return Boden.Gras;
            return sonst;
        }

        private static bool Hat(string n, params string[] w)
        {
            for (int i = 0; i < w.Length; i++) if (n.Contains(w[i])) return true;
            return false;
        }
    }
}
