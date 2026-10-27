using System.Collections.Generic;
using System.Diagnostics;
using Ashveil.Data;
using Ashveil.Gameplay.Visuals;
using UnityEngine;

namespace Ashveil.Gameplay.Npcs
{
    /// <summary>
    /// M8.3: Aussehen eines NPCs mit echtem Menschenkoerper – Kurzform wie beim Spieler ("0|b0|s2|h3|c1|…") plus
    /// sichtbare Ausruestung (Item-IDs). Wird beim Start ueber dieselbe <see cref="AppearanceRig"/> gebaut wie Spieler.
    /// Leistung (14.10.2026, CORE-005): Der Bau kostet ≈ 1 ms je Figur; eine Weltzelle bringt 52 Figuren mit, die bisher alle im
    /// selben Bild gebaut wurden (≈ 55 ms Hänger beim Laden der Westweiden). Jetzt reiht Start nur ein; gebaut wird nach Nähe
    /// zur Kamera, höchstens <see cref="BudgetMs"/> je Bild (mindestens eine Figur).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcBody : MonoBehaviour
    {
        [SerializeField] private string look = "";
        [SerializeField] private string[] gear = System.Array.Empty<string>();
        [SerializeField] private Transform characterRoot;

        public bool Built { get; private set; }
        public string Look => look;

        public void Configure(string encodedLook, string[] items, Transform root)
        {
            look = encodedLook ?? "";
            gear = items ?? System.Array.Empty<string>();
            characterRoot = root;
        }

        /// <summary>Höchstens so viele Millisekunden je Bild für den Bau von NPC-Körpern.</summary>
        public static float BudgetMs = 3f;
        private static readonly List<NpcBody> Warteschlange = new List<NpcBody>();
        private static Bauer _bauer;

        private void Start()
        {
            Warteschlange.Add(this);
            if (_bauer == null)
            {
                var go = new GameObject("NpcBody-Bau") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(go);
                _bauer = go.AddComponent<Bauer>();
            }
        }

        /// <summary>Baut die eingereihten Körper – nächste zur Kamera zuerst, im Zeitbudget.</summary>
        private sealed class Bauer : MonoBehaviour
        {
            private readonly Stopwatch _uhr = new Stopwatch();

            private void Update()
            {
                Warteschlange.RemoveAll(b => b == null || b._versucht);
                if (Warteschlange.Count == 0) return;
                Camera cam = Camera.main;
                if (cam != null && Warteschlange.Count > 1)
                {
                    Vector3 c = cam.transform.position;
                    Warteschlange.Sort((a, b) => (a.transform.position - c).sqrMagnitude.CompareTo((b.transform.position - c).sqrMagnitude));
                }
                _uhr.Restart();
                int gebaut = 0;
                for (int i = 0; i < Warteschlange.Count; i++)
                {
                    if (gebaut > 0 && _uhr.Elapsed.TotalMilliseconds >= BudgetMs) break;
                    NpcBody b = Warteschlange[i];
                    if (b == null || !b.isActiveAndEnabled) continue;   // ausgeschaltet: wartet, bis es wieder an ist
                    b.Bauen();
                    gebaut++;
                }
            }
        }

        private bool _versucht;

        private void Bauen()
        {
            if (_versucht) return;
            _versucht = true;
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            if (cat == null) return;
            CharacterAppearanceData data = CharacterAppearanceData.Decode(look) ?? new CharacterAppearanceData();
            data.EnsureArrays();
            cat.Sanitize(data);
            var equipment = new ItemStack[CharacterAppearanceData.SlotCount];
            if (GameData.Database != null)
                foreach (string id in gear)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    string full = id.StartsWith("item.") ? id : "item." + id;
                    if (!GameData.Database.TryGet(full, out ItemDefinition item)) continue;
                    for (int s = 0; s < equipment.Length; s++)
                        if (equipment[s].IsEmpty && item.FitsSlot((EquipmentSlot)s)) { equipment[s] = new ItemStack(item.Id, 1); break; }
                }
            var rig = GetComponent<AppearanceRig>();
            if (rig == null) rig = gameObject.AddComponent<AppearanceRig>();
            rig.Init(characterRoot != null ? characterRoot : transform.parent);
            AppearanceDefinition[] defs = EquipmentVisuals.Resolve(equipment, data, cat);
            AddLooks(defs);
            rig.Apply(defs, data, cat);
            rig.PlaceWeapons(null, null);
            Built = rig.Ready;
        }

        /// <summary>
        /// Art-Pass 4: Aussehen ohne Gegenstand ("appearance.ref_…") direkt tragen – fuer Referenzfiguren und die
        /// Silhouetten-Buehne. Waffe: erste in die Haupthand, zweite in die Nebenhand.
        /// </summary>
        private void AddLooks(AppearanceDefinition[] defs)
        {
            if (GameData.Database == null) return;
            foreach (string id in gear)
            {
                if (string.IsNullOrEmpty(id) || !id.StartsWith("appearance.")) continue;
                if (!GameData.Database.TryGet(id, out AppearanceDefinition d) || d == null) continue;
                int slot = SlotOf(d.slot);
                if (d.slot == EquipType.Weapon && defs[slot] != null) slot = (int)EquipmentSlot.OffHand;
                if (slot >= 0 && slot < defs.Length) defs[slot] = d;
            }
        }

        private static int SlotOf(EquipType t)
        {
            switch (t)
            {
                case EquipType.Head: return (int)EquipmentSlot.Head;
                case EquipType.Shoulders: return (int)EquipmentSlot.Shoulders;
                case EquipType.Chest: return (int)EquipmentSlot.Chest;
                case EquipType.Hands: return (int)EquipmentSlot.Hands;
                case EquipType.Legs: return (int)EquipmentSlot.Legs;
                case EquipType.Feet: return (int)EquipmentSlot.Feet;
                case EquipType.Belt: return (int)EquipmentSlot.Belt;
                case EquipType.Cloak: return (int)EquipmentSlot.Cloak;
                case EquipType.Weapon: return (int)EquipmentSlot.MainHand;
                default: return -1;
            }
        }
    }
}
