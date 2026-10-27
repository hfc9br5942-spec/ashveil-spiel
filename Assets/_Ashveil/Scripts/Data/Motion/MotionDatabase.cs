using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>Ergebnis der Schichten fuer eine Figur – einmal beim Ausruesten berechnet, pro Frame nur gelesen.</summary>
    public struct ResolvedMotionTuning
    {
        public MotionModifiers modifiers;
        public BodyMotionProfile body;
        public RaceMotionProfile race;
        public ClassMotionProfile classMotion;
        public ArmorMotionProfile armor;
        public WeaponMotionProfile weapon;
        /// <summary>Welche Schichten per Rueckfall kamen (fuer Warnungen, §143).</summary>
        public string fallbackNote;

        public bool IsValid => weapon != null || classMotion != null || body != null;
    }

    /// <summary>
    /// Nachschlagewerk des Motion-Systems (§82). Liegt unter Resources/Motion/MotionDatabase, wird vom Editor gefuellt.
    /// Die Indizes entstehen einmal beim ersten Zugriff – zur Laufzeit keine Suche ueber alle Assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Motion/Datenbank", fileName = "MotionDatabase")]
    public sealed class MotionDatabase : ScriptableObject
    {
        public const string ResourcePath = "Motion/MotionDatabase";

        public List<BodyMotionProfile> bodies = new List<BodyMotionProfile>();
        public List<RaceMotionProfile> races = new List<RaceMotionProfile>();
        public List<ClassMotionProfile> classes = new List<ClassMotionProfile>();
        public List<ArmorMotionProfile> armors = new List<ArmorMotionProfile>();
        public List<WeaponMotionProfile> weapons = new List<WeaponMotionProfile>();
        public List<MotionClipMetadata> clips = new List<MotionClipMetadata>();
        public List<MotionIdentityProfile> identities = new List<MotionIdentityProfile>();

        [Tooltip("Referenzfall (Mensch · Tank · schwer · Schwert+Schild) – Rueckfall, wenn nichts passt.")]
        public MotionIdentityProfile golden;

        [NonSerialized] private bool _indexed;
        [NonSerialized] private Dictionary<string, MotionClipMetadata> _byId;
        [NonSerialized] private Dictionary<uint, MotionClipMetadata> _byNet;
        [NonSerialized] private Dictionary<uint, AttackMotionDefinition> _attackByNet;
        [NonSerialized] private WeaponMotionProfile[] _byFamily;

        private static MotionDatabase _instance;

        /// <summary>Gemeinsame Datenbank (Resources). null, wenn noch nicht erzeugt – alles laeuft dann wie bisher.</summary>
        public static MotionDatabase Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<MotionDatabase>(ResourcePath);
                return _instance;
            }
        }

        /// <summary>Editor: nach dem Neuaufbau Indizes verwerfen.</summary>
        public void Invalidate() => _indexed = false;

        private void EnsureIndex()
        {
            if (_indexed) return;
            _indexed = true;
            _byId = new Dictionary<string, MotionClipMetadata>(clips.Count);
            _byNet = new Dictionary<uint, MotionClipMetadata>(clips.Count);
            foreach (MotionClipMetadata m in clips)
            {
                if (m == null || string.IsNullOrEmpty(m.motionId)) continue;
                _byId[m.motionId] = m;
                if (_byNet.TryGetValue(m.NetId, out MotionClipMetadata other) && other != m)
                    Debug.LogWarning($"[Ashveil] Motion: Netz-ID-Kollision {m.motionId} / {other.motionId} – Kennung aendern.");
                _byNet[m.NetId] = m;
            }
            _attackByNet = new Dictionary<uint, AttackMotionDefinition>();
            _byFamily = new WeaponMotionProfile[32];
            foreach (WeaponMotionProfile w in weapons)
            {
                if (w == null) continue;
                if ((int)w.family < _byFamily.Length && _byFamily[(int)w.family] == null) _byFamily[(int)w.family] = w;
                foreach (AttackMotionDefinition a in w.attacks)
                    if (a != null) _attackByNet[a.NetId] = a;
            }
        }

        public MotionClipMetadata Clip(string motionId)
        {
            EnsureIndex();
            return motionId != null && _byId.TryGetValue(motionId, out MotionClipMetadata m) ? m : null;
        }

        public MotionClipMetadata ClipByNetId(uint id)
        {
            EnsureIndex();
            return _byNet.TryGetValue(id, out MotionClipMetadata m) ? m : null;
        }

        public AttackMotionDefinition AttackByNetId(uint id)
        {
            EnsureIndex();
            return _attackByNet.TryGetValue(id, out AttackMotionDefinition a) ? a : null;
        }

        /// <summary>Waffenprofil einer Familie (mit Rueckfall-Kette des Profils, §143).</summary>
        public WeaponMotionProfile Weapon(WeaponFamily family)
        {
            EnsureIndex();
            int i = (int)family;
            return i >= 0 && i < _byFamily.Length ? _byFamily[i] : null;
        }

        /// <summary>
        /// Schichten fuer eine Figur waehlen und zusammenrechnen: Koerper → Rasse → Klasse → Ruestung → Waffe (§73).
        /// Fehlt eine Schicht, springt der Referenzfall ein und das wird vermerkt.
        /// </summary>
        public ResolvedMotionTuning Resolve(string raceId, BodyMotionVariant body, ClassRole role, ArmorCategory armor, WeaponFamily family)
        {
            EnsureIndex();
            var r = new ResolvedMotionTuning();
            string note = "";

            r.body = Find(bodies, b => b.variant == body);
            if (r.body == null && golden != null) { r.body = golden.body; note += "Koerper "; }
            r.race = Find(races, x => x.raceId == raceId);
            if (r.race == null && golden != null) { r.race = golden.race; note += "Rasse "; }
            r.classMotion = Find(classes, c => (c.role & role) != 0);
            if (r.classMotion == null && golden != null) { r.classMotion = golden.classMotion; note += "Klasse "; }
            r.armor = Find(armors, a => a.category == armor);
            if (r.armor == null && armor != ArmorCategory.None && golden != null) { r.armor = golden.armor; note += "Ruestung "; }
            r.weapon = Weapon(family);
            if (r.weapon == null && family != WeaponFamily.None && golden != null) { r.weapon = golden.weapon; note += "Waffe "; }

            MotionModifiers m = MotionModifiers.Neutral;
            if (r.body != null) m = MotionModifiers.Combine(m, r.body.modifiers);
            if (r.race != null) m = MotionModifiers.Combine(m, r.race.modifiers);
            if (r.classMotion != null) m = MotionModifiers.Combine(m, r.classMotion.modifiers);
            if (r.armor != null) m = MotionModifiers.Combine(m, r.armor.modifiers);
            if (r.weapon != null) m = MotionModifiers.Combine(m, r.weapon.modifiers);
            r.modifiers = m;
            r.fallbackNote = note.Trim();
            return r;
        }

        private static T Find<T>(List<T> list, Func<T, bool> match) where T : UnityEngine.Object
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && match(list[i])) return list[i];
            return null;
        }
    }
}
