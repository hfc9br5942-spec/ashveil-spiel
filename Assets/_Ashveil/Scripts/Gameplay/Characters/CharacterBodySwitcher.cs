using System;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Waehlt das sichtbare Koerpermodell (maennlich / weiblich) und haengt die Waffen an die Hand.
    ///
    /// Bis zum Charaktereditor (Milestone 7) kann man mit F2 zwischen den Koerpern wechseln.
    /// Jeder Koerper hat einen eigenen Animator (eigener Override-Controller moeglich), damit Animationen
    /// pro Koerperbau angepasst werden koennen.
    /// </summary>
    public sealed class CharacterBodySwitcher : MonoBehaviour
    {
        [Serializable]
        public sealed class Body
        {
            public string name = "Koerper";
            public GameObject model;
            [Tooltip("Feinkorrektur Schwert: Versatz (m) und Drehung (Grad) relativ zur automatisch ermittelten Griffhaltung")]
            public Vector3 swordPosition = Vector3.zero;
            public Vector3 swordRotation = Vector3.zero;
            [Tooltip("Feinkorrektur Pistole")]
            public Vector3 pistolPosition = Vector3.zero;
            public Vector3 pistolRotation = Vector3.zero;
        }

        [SerializeField] private Body[] bodies = new Body[0];
        [SerializeField] private int activeIndex;
        [Tooltip("Platzhalter-Kapsel, wird ausgeblendet, sobald ein Modell aktiv ist")]
        [SerializeField] private GameObject placeholderBody;
        [SerializeField] private CharacterAnimatorBridge bridge;
        [SerializeField] private WeaponFx weaponFx;
        [SerializeField] private PlaceholderBodyMotion placeholderMotion;
        [SerializeField] private bool allowDebugSwitch = true;

        private Transform _swordHome;
        private Transform _pistolHome;
        private Pose _swordHomePose;
        private Pose _pistolHomePose;
        private Vector3 _swordHomeScale = Vector3.one;
        private Vector3 _pistolHomeScale = Vector3.one;
        private bool _started;

        public int ActiveIndex => activeIndex;
        public int Count => HasBodies ? bodies.Length : 0;

        /// <summary>Koerper gewechselt (Index) – fuer Ausruestungsdarstellung.</summary>
        public event Action<int> BodyApplied;

        /// <summary>
        /// F2 wurde gedrueckt: Ist ein Empfaenger angemeldet (Netz/Aussehen), entscheidet dieser – sonst wird direkt gewechselt.
        /// </summary>
        public event Action<int> BodyChangeRequested;

        /// <summary>Aktives Koerpermodell (null = Platzhalter).</summary>
        public GameObject ActiveModel => HasBodies && activeIndex < bodies.Length ? bodies[activeIndex].model : null;

        /// <summary>Nur der eigene Spieler darf mit F2 wechseln; fremde Spieler folgen dem Netz.</summary>
        public void SetLocalControl(bool local) => allowDebugSwitch = local;

        /// <summary>EINSTIEG-03 (21.10.): Wirkt F2 an dieser Figur? Vernetzte Spielerfiguren nie, nur Figuren in Offline-Szenen.</summary>
        public bool DebugSwitchAllowed => allowDebugSwitch;
        public string ActiveName => HasBodies ? bodies[activeIndex].name : "Platzhalter";
        private bool HasBodies => bodies != null && bodies.Length > 0;

        private void Start()
        {
            if (weaponFx != null)
            {
                Transform m = weaponFx.MeleePivot, r = weaponFx.RangedPivot;
                if (m != null)
                {
                    _swordHome = m.parent;
                    _swordHomePose = new Pose(m.localPosition, m.localRotation);
                    _swordHomeScale = m.localScale;
                }
                if (r != null)
                {
                    _pistolHome = r.parent;
                    _pistolHomePose = new Pose(r.localPosition, r.localRotation);
                    _pistolHomeScale = r.localScale;
                }
            }
            _started = true;
            Apply(activeIndex);
        }

        private void Update()
        {
            if (allowDebugSwitch && HasBodies && bodies.Length > 1 && !Controls.UiPointer.KeyboardCaptured &&
                Controls.RawInput.WasPressed(Controls.GameKey.SwitchBody))
            {
                int next = (activeIndex + 1) % bodies.Length;
                if (BodyChangeRequested != null) BodyChangeRequested(next);
                else Apply(next);
            }
        }

        public void Apply(int index)
        {
            // Vor Start (z. B. Netz meldet den Koerper sehr frueh): nur merken, Start wendet ihn an –
            // sonst wuerde Start die Waffen-Ausgangslage an der falschen Stelle merken.
            if (!_started)
            {
                activeIndex = Mathf.Clamp(index, 0, Mathf.Max(0, Count - 1));
                return;
            }
            if (!HasBodies)
            {
                if (placeholderBody != null) placeholderBody.SetActive(true);
                return;
            }

            activeIndex = Mathf.Clamp(index, 0, bodies.Length - 1);
            for (int i = 0; i < bodies.Length; i++)
                if (bodies[i].model != null) bodies[i].model.SetActive(i == activeIndex);

            Body body = bodies[activeIndex];
            bool hasModel = body.model != null;
            if (placeholderBody != null) placeholderBody.SetActive(!hasModel);
            if (placeholderMotion != null) placeholderMotion.enabled = !hasModel;

            Animator anim = hasModel ? body.model.GetComponentInChildren<Animator>(true) : null;
            if (bridge != null) bridge.SetAnimator(anim);

            AttachWeapons(anim, body);
            BodyApplied?.Invoke(activeIndex);
        }

        private void AttachWeapons(Animator anim, Body body)
        {
            if (weaponFx == null) return;
            Transform hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;

            if (hand != null)
            {
                AttachToHand(weaponFx.MeleePivot, anim, hand, body.swordPosition, body.swordRotation, true);
                AttachToHand(weaponFx.RangedPivot, anim, hand, body.pistolPosition, body.pistolRotation, false);
            }
            else
            {
                Restore(weaponFx.MeleePivot, _swordHome, _swordHomePose, _swordHomeScale);
                Restore(weaponFx.RangedPivot, _pistolHome, _pistolHomePose, _pistolHomeScale);
            }

            // Erst die neue Lage als Ruhelage merken, dann prozedurale Bewegung umschalten
            // (sonst wuerde die alte Ruhelage die berechnete Griffhaltung ueberschreiben).
            weaponFx.CaptureRestPose();
            weaponFx.SetProceduralMotion(hand == null);
        }

        /// <summary>
        /// Haengt die Waffe an den Handknochen – so, wie eine Faust einen Griff wirklich umschliesst:
        ///   Griffachse  = Linie der Fingerknoechel (kleiner Finger -> Zeigefinger)
        ///   Klinge      = tritt auf der Daumen-/Zeigefingerseite aus der Faust
        ///   Schneide    = in Richtung Unterarm (Schlagrichtung)
        ///   Pistolenlauf= zeigt aus der Faust nach vorn, Griff liegt entlang der Knoechel
        /// Das haengt nicht davon ab, wie die Knochenachsen im importierten Modell liegen.
        /// Die Feinkorrektur aus dem Inspector kommt obendrauf.
        /// </summary>
        public static void AttachToHand(Transform pivot, Animator anim, Transform hand, Vector3 posOffset, Vector3 rotOffset, bool sword)
        {
            if (pivot == null || hand == null) return;

            Transform index = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            Transform little = anim.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            Transform middle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            Transform thumb = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            Transform lowerArm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

            // Handrichtung (Handgelenk -> Fingeransatz). Nur Knochen der Hand selbst verwenden,
            // damit das Ergebnis nicht von der aktuellen Pose des Handgelenks abhaengt.
            Transform fingerRef = middle != null ? middle : index;
            Vector3 wrist = fingerRef != null ? fingerRef.position - hand.position
                : lowerArm != null ? hand.position - lowerArm.position : hand.up;
            wrist.Normalize();

            // Knoechelachse (kleiner Finger -> Zeigefinger = Daumenseite)
            Vector3 rawKnuckles = index != null && little != null ? index.position - little.position
                : thumb != null ? thumb.position - hand.position : hand.right;
            Vector3 knuckles = Vector3.ProjectOnPlane(rawKnuckles, wrist);
            knuckles = knuckles.magnitude > 0.005f ? knuckles.normalized : Vector3.Cross(wrist, Vector3.up).normalized;

            // Handflaechen-Normale: senkrecht zu Hand und Knoecheln, Seite ueber den Daumen bestimmt
            Vector3 palm = Vector3.Cross(wrist, knuckles).normalized;
            if (thumb != null && Vector3.Dot(palm, thumb.position - hand.position) < 0f) palm = -palm;

            // Mitte der Faust: zwischen den Knoecheln, etwas in die Handflaeche
            Vector3 knuckleMid = index != null && little != null ? (index.position + little.position) * 0.5f
                : fingerRef != null ? fingerRef.position : hand.position + wrist * 0.07f;
            Vector3 gripCenter = knuckleMid + palm * 0.025f - wrist * 0.01f;

            Quaternion world;
            Vector3 gripInModel; // Lage des Griffs im Platzhaltermodell (siehe PlayerRigBuilder)
            if (sword)
            {
                world = Quaternion.LookRotation(knuckles, palm);   // Klinge +Z aus der Faust
                gripInModel = new Vector3(0f, 0f, -0.04f);
            }
            else
            {
                Vector3 barrel = Vector3.ProjectOnPlane(wrist, knuckles).normalized;
                world = Quaternion.LookRotation(barrel, knuckles);  // Lauf nach vorn, Griff nach unten (-Y)
                gripInModel = new Vector3(0f, -0.07f, -0.08f);
            }

            // Feinkorrektur relativ zum Griff: Drehung um den Griff, Versatz in Waffen-Achsen (m)
            world *= Quaternion.Euler(rotOffset);

            pivot.SetParent(hand, true);
            pivot.rotation = world;
            pivot.position = gripCenter - world * (gripInModel - posOffset);
            float s = Mathf.Max(0.0001f, hand.lossyScale.x);
            pivot.localScale = Vector3.one / s;
        }

        private static void Restore(Transform pivot, Transform home, Pose pose, Vector3 scale)
        {
            if (pivot == null || home == null) return;
            pivot.SetParent(home, false);
            pivot.localPosition = pose.position;
            pivot.localRotation = pose.rotation;
            pivot.localScale = scale;
        }
    }
}
