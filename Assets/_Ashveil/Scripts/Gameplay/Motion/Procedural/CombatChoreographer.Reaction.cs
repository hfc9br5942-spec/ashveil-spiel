using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// 22.10.2026 (Max: „keine wow vergleichbaren animationen für ausweichen parieren treffer einstecken“; Plan
    /// REQ-20261022-CODEX-KAMPFREAKTIONEN-DETAILPLAN): Treffer einstecken als Ganzkörperreaktion statt des einen Hit_Chest-Clips.
    /// Richtung (vorn/hinten/links/rechts aus der Schlagrichtung), Stärke (leicht/schwer), zwei Varianten je Typ – die Variante
    /// folgt aus dem vom Server gesendeten Trefferpunkt, darum zeigen Host und Clients dieselbe. Der Impuls kommt aus der Hüfte:
    /// Becken weicht in Schlagrichtung aus und sinkt (Knie fangen auf, Füße bleiben stehen), Rumpf knickt mit, Kopf schnappt nach.
    /// Die Hände bleiben, wo Haltung/Angriff sie hingesetzt haben (sie hängen am Brustkorb und gehen mit) – Griff und Schild bleiben.
    /// Hüllkurve zeitbasiert: kurzer Impuls, kurze Last, lange weiche Rückkehr. Trefferketten starten nicht jedes Mal neu.
    /// Nur Darstellung: kein Einfluss auf Schaden, Bewegung, Wirken.
    /// </summary>
    public sealed partial class CombatChoreographer
    {
        private enum Reaktion : byte { Keine, Leicht, Schwer, Absorbiert, Tick }

        private Reaktion _reakt;
        private float _reaktT = -1f, _reaktDur, _reaktAmp;
        private Vector3 _reaktDir;      // Schubrichtung im Körperraum (x rechts, z vorn), horizontal, normiert
        private int _reaktVar;
        private float _letzterTick = -10f;

        /// <summary>Lab/Vorschau: Treffer aus <paramref name="attackerDir"/> (Richtung vom Ziel zum Angreifer) zeigen.</summary>
        public void PlayHit(Vector3 attackerDir, bool heavy, int variante = 0, float amp = 1f)
        {
            if (!EnsureBones()) return;
            _u = _model.up; _f = _model.forward; _rt = _model.right;
            StartReaktion(heavy ? Reaktion.Schwer : Reaktion.Leicht, -attackerDir, variante, amp);
        }

        /// <summary>Welche Körperreaktion ein Treffer auslöst (Ausweichen und Schildblock behandelt OnDamaged vorher).</summary>
        private Reaktion WaehleReaktion(in DamageInfo info, out float amp)
        {
            amp = 1f;
            // 23.10. (Codex Paket C): das Netz-Ergebnis zuerst – reine Clients kennen die Waffen-Id nicht, und Gift/Blutung heißen nicht „.dot“
            if (info.Reaktion == Ashveil.Gameplay.Combat.TrefferReaktion.AbsorbVoll) return Reaktion.Absorbiert;
            bool tick = info.Reaktion == Ashveil.Gameplay.Combat.TrefferReaktion.Tick || info.Periodisch ||
                        (info.WeaponId != null && (info.WeaponId.EndsWith(".dot") || info.Environmental));
            if (info.Environmental || tick) return Reaktion.Tick;
            if (info.Amount <= 0f) return info.Absorbed > 0f || info.Blocked <= 0f ? Reaktion.Absorbiert : Reaktion.Keine;
            float max = _health != null ? Mathf.Max(1f, _health.Max) : 100f;
            bool schwer = info.IsCritical || info.Knockback > 0.01f || info.Amount >= 0.15f * max;
            // Teilweise absorbiert: Restschaden als gedämpfte Reaktion
            if (info.Absorbed > 0f || info.Reaktion == Ashveil.Gameplay.Combat.TrefferReaktion.AbsorbTeil) amp = 0.7f;
            return schwer ? Reaktion.Schwer : Reaktion.Leicht;
        }

        /// <summary>Variante aus dem Trefferpunkt (vom Server gesendet) – gleich auf Host und Clients, ohne eigenen Zufallswurf.</summary>
        private static int VarianteAus(Vector3 p)
        {
            int h = Mathf.RoundToInt(p.x * 50f) * 73856093 ^ Mathf.RoundToInt(p.y * 50f) * 19349663 ^ Mathf.RoundToInt(p.z * 50f) * 83492791;
            return h & 1;
        }

        private void TrefferReaktion(in DamageInfo info)
        {
            Reaktion r = WaehleReaktion(info, out float amp);
            if (r == Reaktion.Keine) return;
            if (r == Reaktion.Tick)
            {
                // Gift/Brennen/Umgebung: dosiert und gedrosselt – höchstens alle 0,9 s ein kleiner Impuls
                if (Time.time - _letzterTick < 0.9f) return;
                _letzterTick = Time.time;
            }
            StartReaktion(r, info.Direction, VarianteAus(info.HitPoint), amp);
        }

        private void StartReaktion(Reaktion r, Vector3 schlagRichtungWelt, int variante, float amp)
        {
            float dur = r == Reaktion.Schwer ? 0.55f : r == Reaktion.Leicht ? 0.32f : 0.26f;
            float a = r == Reaktion.Schwer ? 1f : r == Reaktion.Leicht ? 0.55f : r == Reaktion.Absorbiert ? 0.3f : 0.25f;
            a *= amp;
            // Trefferkette: läuft schon eine Reaktion, nur übernehmen, wenn die neue stärker ist oder die alte schon abklingt –
            // sonst bleibt die laufende (kein endloses Neustarten bei schnellen Treffern)
            if (_reaktT >= 0f)
            {
                float rest = Huelle(_reaktT / _reaktDur) * _reaktAmp;
                if (a < rest * 1.1f && _reaktT / _reaktDur < 0.5f) return;
                a = Mathf.Max(a, rest);
            }
            Vector3 d = ToBodyDir(schlagRichtungWelt);
            d.y = 0f;
            _reaktDir = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector3(0f, 0f, -1f);   // ohne Richtung: von vorn
            _reakt = r; _reaktT = 0f; _reaktDur = dur; _reaktAmp = a; _reaktVar = variante;
        }

        /// <summary>Hüllkurve 0..1: Impuls bis 18 %, Last bis 30 %, danach weiche Rückkehr.</summary>
        private static float Huelle(float p)
        {
            if (p <= 0f || p >= 1f) return 0f;
            if (p < 0.18f) return MotionMath.SmoothStep01(p / 0.18f);
            if (p < 0.30f) return 1f;
            return 1f - MotionMath.SmoothStep01((p - 0.30f) / 0.70f);
        }

        private void StopReaktion() { _reaktT = -1f; _reakt = Reaktion.Keine; }

        /// <summary>
        /// Auf die fertige Pose aufsetzen (nach Angriff/Bogen/Haltung). Ausführlich: Becken, Beine (IK), Rumpf, Kopf.
        /// Vereinfacht (entfernte Figuren, LOD 1): nur Rumpf und Kopf – die Silhouette reagiert trotzdem lesbar.
        /// </summary>
        private void UpdateReaktion(float dt, bool detailed)
        {
            _reaktT += dt;
            if (_reaktT >= _reaktDur) { StopReaktion(); return; }
            float k = Huelle(_reaktT / _reaktDur) * _reaktAmp;
            if (k <= 0.001f) return;
            Vector3 d = _reaktDir;                       // wohin der Schlag schiebt (Körperraum)
            float vorn = d.z, seite = d.x;               // vorn > 0: von hinten getroffen, Schub nach vorn
            bool schwer = _reakt == Reaktion.Schwer;
            float v = _reaktVar == 0 ? 1f : -1f;         // Variante: Rumpf dreht mit oder gegen die Schlagseite

            // Rumpf: knickt in Schubrichtung (Treffer von vorn → nach hinten weg), seitlich weg, Drehung um die Hochachse
            // Erster Film 22.10.: mit 14°/7 cm Absinken las sich der schwere Treffer als Hocke, nicht als Schlag – der Hals nimmt in
            // Torso() knapp die Hälfte der Neigung zurück. Darum deutlich mehr Rumpf (Schulterlinie kippt sichtbar) und weniger Absinken.
            float lean = vorn * (schwer ? 26f : 14f);
            float side = seite * (schwer ? 22f : 12f);
            float twist = (seite * 22f + v * 9f * (1f - Mathf.Abs(seite))) * (schwer ? 1f : 0.6f);
            if (_reakt == Reaktion.Absorbiert || _reakt == Reaktion.Tick) { lean *= 0.5f; side *= 0.5f; twist *= 0.3f; }

            if (detailed)
            {
                Vector3 fl = _lFoot != null ? _lFoot.position : Vector3.zero, fr = _rFoot != null ? _rFoot.position : Vector3.zero;
                // Fußrotation sichern: TwoBone hält nur das Sprunggelenk – die mitgedrehte Fußspitze glitt sonst bis 10 cm (Codex 22.10.)
                Quaternion rl = _lFoot != null ? _lFoot.rotation : Quaternion.identity, rr = _rFoot != null ? _rFoot.rotation : Quaternion.identity;
                // Becken: weicht in Schubrichtung aus und federt leicht ein – aus der Hüfte, nicht nur Oberkörper
                float weg = schwer ? 0.08f : 0.04f, sink = schwer ? 0.035f : 0.015f;
                _hips.position += (M(new Vector3(seite, 0f, vorn)) * weg - _u * sink) * k;
                _hips.rotation = Quaternion.AngleAxis(twist * 0.35f * k, _u) * _hips.rotation;
                if (_lFoot != null) { LimbIK.TwoBone(_lUpLeg, _lLeg, _lFoot, fl, _f, 1f); _lFoot.rotation = rl; }
                if (_rFoot != null) { LimbIK.TwoBone(_rUpLeg, _rLeg, _rFoot, fr, _f, 1f); _rFoot.rotation = rr; }
            }
            Torso(twist * 0.65f * k, lean * k, side * k, 1f);
            // Kopf schnappt nach (läuft dem Rumpf leicht hinterher: stärker in der Lastphase)
            if (_head != null) Rot(_head, twist * 0.2f * k, lean * 0.5f * k, side * 0.6f * k);
        }
    }
}
