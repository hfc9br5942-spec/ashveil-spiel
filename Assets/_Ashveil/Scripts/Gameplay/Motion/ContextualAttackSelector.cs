using System.Collections.Generic;
using Ashveil.Data.Motion;
using UnityEngine;

namespace Ashveil.Gameplay.Motion
{
    /// <summary>Was die Auswahl ueber die Lage wissen muss (§27). Alles aus dem MotionContext plus Freiraum.</summary>
    public struct AttackQuery
    {
        public bool IsAbility;
        public bool HasTarget;
        public TargetSizeClass TargetSize;
        public float TargetDistance;
        /// <summary>Hoehe des gewaehlten Treffpunkts ueber dem Boden unter dem Angreifer (m).</summary>
        public float TargetPointHeight;
        public float TargetSpeed;
        public float BodyHeight;
        /// <summary>Freier Platz links/rechts (m); groesser als jeder Bedarf = kein Hindernis.</summary>
        public float ClearLeft, ClearRight;
        /// <summary>Klassen-Oekonomie 0..1 (Tank: kurze, kontrollierte Bewegungen, §127).</summary>
        public float Economy;
        public uint Seed;
    }

    public struct AttackChoice
    {
        public AttackMotionDefinition Definition;
        public float Weight;
        /// <summary>Kurzbegruendung fuer Debug/Tests (z. B. „Ziel zu hoch“).</summary>
        public string Note;
        public bool Valid => Definition != null;
    }

    /// <summary>
    /// Waehlt die sichtbar passendste Angriffsbewegung (§26–§29, §68–§71, §121). Gewichtet statt blind zufaellig, mit
    /// Verlauf gegen Wiederholung und Anschluss-Posen fuer fliessende Folgen. Entscheidet nie ueber Treffer: findet sich
    /// fuer einen unerreichbaren Punkt nichts, ist die Antwort „keine Bewegung“ (§30) – das Gameplay regelt den Rest.
    /// Deterministisch: gleiche Anfrage + gleicher Seed + gleicher Verlauf → gleiche Wahl.
    /// </summary>
    public sealed class ContextualAttackSelector
    {
        public const int HistoryLength = 3;
        /// <summary>Zielhoehe darf die Reichweite eines Angriffs um diesen Anteil der Koerperhoehe ueberschreiten.</summary>
        public const float HeightTolerance = 0.08f;
        /// <summary>Zonengrenzen als Anteil der Koerperhoehe des Angreifers.</summary>
        public const float LowZoneTop = 0.45f, HighZoneBottom = 0.8f;

        private readonly uint[] _history = new uint[HistoryLength];
        private int _count;
        private PoseTag _lastEndPose = PoseTag.GuardCenter;
        private readonly List<float> _weights = new List<float>(16);

        public PoseTag LastEndPose => _lastEndPose;
        public int HistoryCount => _count;
        public uint HistoryAt(int i) => i < _count ? _history[i] : (uint)0;

        public void Reset()
        {
            _count = 0;
            _lastEndPose = PoseTag.GuardCenter;
        }

        /// <summary>Gewaehlten Angriff als gespielt eintragen (auch fremde, per Netz gemeldete – haelt den Fluss).</summary>
        public void Remember(AttackMotionDefinition a)
        {
            if (a == null) return;
            for (int i = HistoryLength - 1; i > 0; i--) _history[i] = _history[i - 1];
            _history[0] = a.NetId;
            _count = Mathf.Min(HistoryLength, _count + 1);
            _lastEndPose = a.endPose;
        }

        public AttackChoice Select(IReadOnlyList<AttackMotionDefinition> attacks, in AttackQuery q)
        {
            _weights.Clear();
            float total = 0f;
            int valid = 0;
            string lastNote = "keine Angriffe fuer diese Waffe";
            string reachNote = null;
            for (int i = 0; i < (attacks != null ? attacks.Count : 0); i++)
            {
                float w = Score(attacks[i], q, out string note);
                if (w > 0f) valid++;
                else if (note.StartsWith("Ziel")) reachNote = note;
                else lastNote = note;
                _weights.Add(w);
                total += w;
            }
            if (reachNote != null) lastNote = reachNote; // Erreichbarkeit ist die wichtigere Auskunft
            if (total <= 0f) return new AttackChoice { Note = lastNote };

            // Wiederholung daempfen – ausser es passt nur diese eine Bewegung (§28)
            if (valid > 1)
            {
                total = 0f;
                for (int i = 0; i < _weights.Count; i++)
                {
                    if (_weights[i] <= 0f) continue;
                    uint id = attacks[i].NetId;
                    if (_count > 0 && _history[0] == id) _weights[i] *= 0.05f;
                    else if (_count > 1 && _history[1] == id) _weights[i] *= 0.35f;
                    else if (_count > 2 && _history[2] == id) _weights[i] *= 0.6f;
                    total += _weights[i];
                }
            }

            var rng = new MotionRandom(q.Seed);
            float pick = rng.Value * total;
            for (int i = 0; i < _weights.Count; i++)
            {
                if (_weights[i] <= 0f) continue;
                pick -= _weights[i];
                if (pick <= 0f) return new AttackChoice { Definition = attacks[i], Weight = _weights[i] / total, Note = "" };
            }
            for (int i = _weights.Count - 1; i >= 0; i--)
                if (_weights[i] > 0f) return new AttackChoice { Definition = attacks[i], Weight = _weights[i] / total, Note = "" };
            return new AttackChoice { Note = lastNote };
        }

        /// <summary>Gewicht einer Bewegung fuer die Lage; 0 = nicht glaubwuerdig/erreichbar.</summary>
        public float Score(AttackMotionDefinition a, in AttackQuery q, out string note)
        {
            note = "";
            if (a == null || a.motion == null) { note = "ohne Clip"; return 0f; }
            if (a.abilityOnly && !q.IsAbility) { note = "nur Faehigkeit"; return 0f; }
            float w = Mathf.Max(0.01f, a.baseWeight);

            if (q.HasTarget)
            {
                // Erreichbarkeit (§30): nie so tun, als treffe man einen unerreichbaren Punkt
                float h = Mathf.Max(0.5f, q.BodyHeight);
                float rel = q.TargetPointHeight / h;
                if (rel > a.heightRange.y + HeightTolerance) { note = "Ziel zu hoch"; return 0f; }
                if (rel < a.heightRange.x - HeightTolerance) { note = "Ziel zu tief"; return 0f; }
                float mid = (a.heightRange.x + a.heightRange.y) * 0.5f;
                float half = Mathf.Max(0.05f, (a.heightRange.y - a.heightRange.x) * 0.5f);
                w *= Mathf.Lerp(1.6f, 0.7f, Mathf.Clamp01(Mathf.Abs(rel - mid) / half));
                // Zone des Treffpunkts: tief / mittig / hoch (§128 – Bein tief, Rumpf mittig, Kopf hoch)
                BodyZone zone = rel < LowZoneTop ? BodyZone.Low : rel > HighZoneBottom ? BodyZone.High : BodyZone.Mid;
                if (a.zone != BodyZone.Any) w *= a.zone == zone ? 2.5f : 0.5f;

                // Zielgroesse (§29)
                if (q.TargetSize != TargetSizeClass.None && (q.TargetSize < a.minTargetSize || q.TargetSize > a.maxTargetSize)) w *= 0.25f;

                // Distanz (§68) – Gameplay bewegt, hier nur Vorliebe
                if (q.TargetDistance > 0f)
                {
                    float d = Mathf.Abs(q.TargetDistance - a.preferredRange);
                    w *= Mathf.Lerp(1.3f, 0.6f, Mathf.Clamp01(d / 1.5f));
                }

                // Zielbewegung (§71)
                if (q.TargetSpeed > 2.5f) { if (a.quick) w *= 1.8f; if (a.committed) w *= 0.4f; }
                else if (q.TargetSize >= TargetSizeClass.Large && a.committed) w *= 1.6f;
            }

            // Freiraum (§69): Schlag zur zugestellten Seite meiden
            if (a.swingSide < -0.3f && q.ClearLeft < a.attackArc / 90f * 0.9f) w *= 0.15f;
            if (a.swingSide > 0.3f && q.ClearRight < a.attackArc / 90f * 0.9f) w *= 0.15f;

            // Fluss (§121/§122)
            if (a.startPose == PoseTag.Any || a.startPose == _lastEndPose) w *= 1.5f;
            else w *= 0.8f;

            // Klassenhaltung (§127): Tank bevorzugt kurze, kontrollierte Bewegungen
            if (a.quick) w *= 1f + q.Economy * 0.5f;
            if (a.committed) w *= 1f - q.Economy * 0.5f;
            return w;
        }
    }
}
