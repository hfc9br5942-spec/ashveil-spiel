using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Die Grosse Kerbe als Punktfolge – veroeffentlicht vom Terrain-Generator (Vorgabe §86), gelesen vom Himmel:
    /// der Schleier steht ueber genau dieser Linie (Kanon 30.09., claude/ashveil-schleier-und-kerbe-2026-09-30.md).
    /// Kerbe und Schleier teilen sich dieselben Punkte und koennen nie auseinanderlaufen. Lage PROVISIONAL.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Welt/Kerbe (Schleier-Pfad)", fileName = "VeilScarPath")]
    public sealed class VeilScarPath : ScriptableObject
    {
        public const string ResourcePath = "World/VeilScarPath";

        [Serializable]
        public struct Point
        {
            public Vector2 position;
            [Tooltip("Breite der Kerbe (m)")] public float width;
            [Tooltip("Tiefe unter dem Rand (m)")] public float depth;
            [Tooltip("Wie stark der Schleier hier leuchtet (0…1)")] public float veilIntensity;
        }

        public List<Point> points = new List<Point>();
        [Tooltip("Scheinbare Hoehe des Schleiers (m) – bestimmt, wie hoch er aus der Ferne ueber dem Horizont steht")]
        public float veilHeight = 3500f;
        public int generationVersion;

        private Vector2[] _cache;

        private Vector2[] Positions()
        {
            if (_cache == null || _cache.Length != points.Count)
            {
                _cache = new Vector2[points.Count];
                for (int i = 0; i < points.Count; i++) _cache[i] = points[i].position;
            }
            return _cache;
        }

        private void OnValidate() => _cache = null;

        /// <summary>Naechster Punkt auf der Kerben-Mittellinie, Richtung der Kerbe dort und ihre Breite/Schleierstaerke.</summary>
        public float Nearest(Vector2 p, out Vector2 closest, out Vector2 tangent, out float width, out float veil)
        {
            closest = p;
            tangent = Vector2.right;
            width = 0f;
            veil = 0f;
            if (points.Count < 2) return float.MaxValue;
            Vector2[] pts = Positions();
            float d = WorldPolyline.Distance(pts, p, out int seg, out float t, out _, out closest);
            tangent = (pts[seg + 1] - pts[seg]).normalized;
            width = Mathf.Lerp(points[seg].width, points[seg + 1].width, t);
            veil = Mathf.Lerp(points[seg].veilIntensity, points[seg + 1].veilIntensity, t);
            return d;
        }

        private static VeilScarPath _loaded;
        private static float _nextTry = -1f;

        /// <summary>Aus Resources laden; null, solange der Generator nichts veroeffentlicht hat (dann alle 5 s neu versuchen).</summary>
        public static VeilScarPath Load()
        {
            if (_loaded == null && Time.realtimeSinceStartup >= _nextTry)
            {
                _nextTry = Time.realtimeSinceStartup + 5f;
                _loaded = Resources.Load<VeilScarPath>(ResourcePath);
            }
            return _loaded;
        }
    }
}
