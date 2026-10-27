using System;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using UnityEngine;

namespace Ashveil.Gameplay.Targeting
{
    /// <summary>
    /// Zielauswahl des Spielers:
    ///   Tab        – naechstes angreifbares Ziel vor dem Spieler (nach Winkel & Entfernung gewichtet),
    ///                erneutes Tab wechselt reihum
    ///   Linksklick – Ziel unter dem Mauszeiger
    ///   Esc        – Ziel aufheben
    ///   Klick auf eine Koerperzone / Taste „Schwachpunkt“ (G) – Teil des Ziels waehlen (Boss-Anatomie §4)
    /// Das Ziel verfaellt, wenn es stirbt oder weiter als <see cref="dropDistance"/> entfernt ist.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TargetingSystem : MonoBehaviour
    {
        [SerializeField] private float tabRange = 40f;
        [Tooltip("Nur Ziele innerhalb dieses Blickkegels (Grad, gesamt) kommen fuer Tab in Frage")]
        [SerializeField] private float tabCone = 150f;
        [SerializeField] private float dropDistance = 60f;
        [SerializeField] private float clickRange = 80f;

        private CombatTarget _current;
        private byte _part;

        public CombatTarget Current => _current != null && _current.isActiveAndEnabled ? _current : null;
        public event Action<CombatTarget> TargetChanged;
        /// <summary>Gewaehltes Koerperteil des Ziels (0 = Koerper).</summary>
        public byte CurrentPart => Current != null ? _part : (byte)0;
        /// <summary>Ziel oder Teil hat gewechselt (fuer den Server: welches Teil wird getroffen).</summary>
        public event Action<CombatTarget, byte> AimChanged;

        /// <summary>Wird einmal pro Frame vom Spieler aufgerufen.</summary>
        public void Tick(bool tabPressed, bool clearPressed, bool clickSelect, Vector2 clickPosition, Camera cam)
        {
            if (clearPressed) SetTarget(null);
            if (tabPressed) TabNext(cam);
            if (clickSelect && cam != null) ClickSelect(cam, clickPosition);

            CombatTarget t = Current;
            if (t != null && (!t.Health.IsAlive || Vector3.Distance(transform.position, t.transform.position) > dropDistance))
                SetTarget(null);
            else if (t == null && _current != null)
                SetTarget(null); // Ziel wurde zerstoert/deaktiviert
        }

        public void SetTarget(CombatTarget target)
        {
            if (target == _current) return;
            _current = target;
            _part = 0;
            TargetChanged?.Invoke(target);
            AimChanged?.Invoke(target, 0);
        }

        /// <summary>Ziel und Teil zugleich setzen (Klick auf eine Zone, Tests).</summary>
        public void SetTarget(CombatTarget target, byte part)
        {
            SetTarget(target);
            SetPart(part);
        }

        public void SetPart(byte part)
        {
            if (Current == null) part = 0;
            if (part == _part) return;
            _part = part;
            AimChanged?.Invoke(Current, part);
        }

        /// <summary>
        /// Naechsten Schwachpunkt des Ziels waehlen: reihum durch alle Teile, die nicht zerstoert sind, dann zurueck zum
        /// Koerper. Ohne Ziel erst Tab.
        /// </summary>
        public void CycleWeakPoint(Camera cam)
        {
            if (Current == null) TabNext(cam);
            CombatTarget t = Current;
            EnemyAnatomy a = t != null ? t.GetComponent<EnemyAnatomy>() : null;
            if (a == null || a.Parts.Count == 0)
            {
                if (t != null) Ashveil.Core.Events.CombatTextEvent.Error("Dieses Ziel hat keine Schwachpunkte");
                return;
            }
            int start = a.IndexOf(_part);
            for (int step = 1; step <= a.Parts.Count + 1; step++)
            {
                int i = start + step;
                if (i >= a.Parts.Count) { SetPart(0); return; }
                if (a.Parts[i].State != BodyPartState.Destroyed) { SetPart(a.Parts[i].Def.id); return; }
            }
        }

        private void TabNext(Camera cam)
        {
            Vector3 origin = transform.position;
            Vector3 forward = cam != null ? cam.transform.forward : transform.forward;
            forward.y = 0f;
            forward.Normalize();

            CombatTarget best = null;
            float bestScore = float.MaxValue;
            CombatTarget firstAfterCurrent = null;
            float firstScore = float.MaxValue;
            float currentScore = _current != null ? Score(_current, origin, forward) : float.MinValue;

            foreach (CombatTarget ct in CombatTarget.All)
            {
                if (ct == null || !ct.IsAttackable) continue;
                float score = Score(ct, origin, forward);
                if (score == float.MaxValue) continue;

                if (score < bestScore) { bestScore = score; best = ct; }
                // Reihum: das naechstbessere Ziel nach dem aktuellen
                if (_current != null && ct != _current && score > currentScore && score < firstScore)
                {
                    firstScore = score;
                    firstAfterCurrent = ct;
                }
            }

            CombatTarget pick = _current == null ? best : (firstAfterCurrent != null ? firstAfterCurrent : best);
            if (pick != null)
            {
                SetTarget(pick);
            }
            else
            {
                Ashveil.Core.Events.CombatTextEvent.Error("Kein Ziel in der Naehe");
            }
        }

        /// <summary>Kleiner Wert = besseres Ziel. MaxValue = ungeeignet.</summary>
        private float Score(CombatTarget ct, Vector3 origin, Vector3 forward)
        {
            Vector3 to = ct.transform.position - origin;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > tabRange) return float.MaxValue;
            float angle = dist > 0.01f ? Vector3.Angle(forward, to) : 0f;
            if (angle > tabCone * 0.5f) return float.MaxValue;
            return angle * 0.6f + dist; // Blickrichtung zaehlt mehr als reine Naehe
        }

        private static readonly RaycastHit[] ClickHits = new RaycastHit[24];

        private void ClickSelect(Camera cam, Vector2 screenPos) => SelectByRay(cam.ScreenPointToRay(screenPos));

        /// <summary>Ziel (und Koerperzone) unter diesem Strahl waehlen – der Mausklick, auch fuer Tests. true = etwas gewaehlt.</summary>
        public bool SelectByRay(Ray ray)
        {
            int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask(Ashveil.Core.GameLayers.Player); // eigene Figur ignorieren
            // Trigger zaehlen nur, wenn sie Trefferzonen sind (Koerperteile); sonst das naechste feste Objekt
            int n = Physics.RaycastNonAlloc(ray, ClickHits, clickRange, mask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Collider pick = null;
            for (int i = 0; i < n; i++)
            {
                Collider c = ClickHits[i].collider;
                if (c == null || (c.isTrigger && c.GetComponent<BodyPartHitbox>() == null)) continue;
                if (ClickHits[i].distance < best) { best = ClickHits[i].distance; pick = c; }
            }
            if (pick == null) return false;
            var ct = pick.GetComponentInParent<CombatTarget>();
            if (ct == null || !ct.isActiveAndEnabled) return false;
            // Die Kapsel des Gegners liegt um seine Zonen herum: liegt auf dem Strahl eine Zone desselben Gegners, gilt sie
            BodyPartHitbox zone = null;
            float zoneDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var z = ClickHits[i].collider != null ? ClickHits[i].collider.GetComponent<BodyPartHitbox>() : null;
                if (z == null || z.Anatomy == null || z.Anatomy.gameObject != ct.gameObject || ClickHits[i].distance >= zoneDist) continue;
                zoneDist = ClickHits[i].distance;
                zone = z;
            }
            SetTarget(ct, zone != null ? zone.PartId : (byte)0);
            return true;
        }

        private void OnDisable()
        {
            SetTarget(null);
        }
    }
}
