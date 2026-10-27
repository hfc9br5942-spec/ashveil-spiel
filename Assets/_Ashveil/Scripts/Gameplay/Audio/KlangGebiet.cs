using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Audio
{
    /// <summary>
    /// Bereich, der bestimmt, welche Umgebung dort klingt (Lerchen am Tag, Grillen in der Nacht). Ein Quader in Weltlage;
    /// liegt der Hörer in keinem Gebiet, gibt es nur den Wind.
    /// </summary>
    public sealed class KlangGebiet : MonoBehaviour
    {
        public Vector3 groesse = new Vector3(100f, 400f, 100f);
        public AudioClip[] tagEinzeln;          // z. B. Lerchen
        public AudioClip nachtBett;             // z. B. Grillen
        [Range(0f, 1f)] public float tagLaut = 0.35f, nachtLaut = 0.5f;

        internal static readonly List<KlangGebiet> Alle = new List<KlangGebiet>();
        private void OnEnable() => Alle.Add(this);
        private void OnDisable() => Alle.Remove(this);

        public bool Enthaelt(Vector3 p)
        {
            Vector3 l = transform.InverseTransformPoint(p);
            return Mathf.Abs(l.x) <= groesse.x * 0.5f && Mathf.Abs(l.y) <= groesse.y * 0.5f && Mathf.Abs(l.z) <= groesse.z * 0.5f;
        }

        public void Konfigurieren(Vector3 g, AudioClip[] tag, AudioClip nacht)
        {
            groesse = g; tagEinzeln = tag; nachtBett = nacht;
        }
    }
}
