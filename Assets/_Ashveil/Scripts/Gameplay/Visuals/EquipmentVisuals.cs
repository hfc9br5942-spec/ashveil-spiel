using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// Sichtbare Ausruestung einer Spielfigur: nimmt angelegte Gegenstaende (<see cref="CharacterEquipment"/>) und das
    /// persoenliche Aussehen (<see cref="CharacterAppearanceData"/>, vom Netz gesetzt) und laesst sie vom
    /// <see cref="AppearanceRig"/> des aktiven Koerpers darstellen. Legt Waffen in die Hand oder an Huefte/Ruecken.
    /// Reine Darstellung – aendert nie Werte oder Inventar.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentVisuals : MonoBehaviour
    {
        [Tooltip("Pruefintervall fuer die Detailstufe (s)")]
        [SerializeField] private float lodInterval = 0.5f;

        private CharacterBodySwitcher _bodies;
        private CharacterEquipment _equipment;
        private WeaponUser _weapons;
        private WeaponFx _fx;
        private AppearanceRig _rig;
        private CharacterAppearanceData _look = new CharacterAppearanceData();
        private readonly List<Renderer> _meleePlaceholders = new List<Renderer>();
        private readonly List<Renderer> _rangedPlaceholders = new List<Renderer>();
        private float _lodTimer;
        private CharacterLod _lod;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        public CharacterAppearanceData Look => _look;
        public AppearanceRig Rig => _rig;

        /// <summary>Wo die gerade aktive Waffe steckt, wenn sie nicht in der Hand ist (aus ihrem Aussehen).</summary>
        public WeaponSheath DrawnSheath { get; private set; } = WeaponSheath.HipLeft;
        /// <summary>Hat die aktive Waffe ein eigenes Modell (sonst Platzhalter)?</summary>
        public bool DrawnHasModel { get; private set; }
        /// <summary>Modell in der Nebenhand (Schild am Arm, Fokus in der Hand) – null, wenn leer.</summary>
        public Transform OffhandModel => _rig != null ? _rig.WeaponModel(EquipmentSlot.OffHand) : null;

        /// <summary>Nach jedem Neuaufbau (Tests, Vorschau).</summary>
        public event System.Action Rebuilt;

        private void Awake()
        {
            _bodies = GetComponent<CharacterBodySwitcher>();
            _equipment = GetComponent<CharacterEquipment>();
            _weapons = GetComponent<WeaponUser>();
            _fx = GetComponentInChildren<WeaponFx>(true);
            // Detailstufe bei vielen Spielern (Einstellung "Figurendetail")
            if (GetComponent<CharacterLod>() == null) gameObject.AddComponent<CharacterLod>();
        }

        private void OnEnable()
        {
            if (_equipment != null) _equipment.Applied += Refresh;
            if (_bodies != null) _bodies.BodyApplied += OnBodyApplied;
            if (_weapons != null) _weapons.WeaponChanged += OnWeaponChanged;
        }

        private void OnDisable()
        {
            if (_equipment != null) _equipment.Applied -= Refresh;
            if (_bodies != null) _bodies.BodyApplied -= OnBodyApplied;
            if (_weapons != null) _weapons.WeaponChanged -= OnWeaponChanged;
        }

        private void Start()
        {
            CollectPlaceholders();
            OnBodyApplied(_bodies != null ? _bodies.ActiveIndex : 0);
        }

        private void CollectPlaceholders()
        {
            if (_fx == null) return;
            if (_fx.MeleePivot != null) _meleePlaceholders.AddRange(_fx.MeleePivot.GetComponentsInChildren<Renderer>(true));
            if (_fx.RangedPivot != null) _rangedPlaceholders.AddRange(_fx.RangedPivot.GetComponentsInChildren<Renderer>(true));
        }

        // ================================================================== Eingaenge

        /// <summary>Aussehen setzen (Netz / Einzelspiel). Koerperwechsel inklusive.</summary>
        public void SetAppearance(CharacterAppearanceData look)
        {
            _look = look != null ? look.Clone() : new CharacterAppearanceData();
            if (_bodies != null && _bodies.Count > 0 && _bodies.ActiveIndex != _look.body)
            {
                _bodies.Apply(_look.body); // loest OnBodyApplied -> Refresh aus
                return;
            }
            Refresh();
        }

        private void OnBodyApplied(int index)
        {
            GameObject model = _bodies != null ? _bodies.ActiveModel : null;
            AppearanceRig next = model != null ? model.GetComponent<AppearanceRig>() : null;
            if (model != null && next == null) next = model.AddComponent<AppearanceRig>();
            if (_rig != null && _rig != next) _rig.Clear(); // alter Koerper: alles freigeben
            _rig = next;
            if (_rig != null) _rig.Init(transform);
            Refresh();
        }

        private void OnWeaponChanged(WeaponDefinition w) => PlaceWeapons();

        // ================================================================== Aufbau

        public void Refresh()
        {
            if (_rig == null || !_rig.Ready) return;
            ItemStack[] eq = _equipment != null ? _equipment.Equipped : new ItemStack[InventoryModel.EquipCount];
            _rig.Apply(Resolve(eq, _look, GameDataBootstrap.Catalog), _look, GameDataBootstrap.Catalog);
            PlaceWeapons();
            Rebuilt?.Invoke();
        }

        /// <summary>
        /// Aussehen je Platz: kosmetische Uebertragung (falls gesetzt und zulaessig) &gt; eigenes Aussehen des Items &gt;
        /// kontrollierter Ersatz. Ausgeblendeter Helm/Umhang = leer (Werte bleiben aktiv).
        /// </summary>
        public static AppearanceDefinition[] Resolve(ItemStack[] equipment, CharacterAppearanceData look, AppearanceCatalog cat)
        {
            var defs = new AppearanceDefinition[InventoryModel.EquipCount];
            if (equipment == null) return defs;
            for (int i = 0; i < defs.Length && i < equipment.Length; i++)
            {
                if (equipment[i].IsEmpty) continue;
                if (look != null && ((i == (int)EquipmentSlot.Head && look.hideHelm) || (i == (int)EquipmentSlot.Cloak && look.hideCloak))) continue;
                ItemDefinition item = GameData.Item(equipment[i].id);
                if (item == null)
                {
                    WarnOnce("Unbekannter Gegenstand '" + equipment[i].id + "' – nicht dargestellt.");
                    continue;
                }
                AppearanceDefinition d = item.appearance;
                string over = look != null && look.transmog != null && i < look.transmog.Length ? look.transmog[i] : null;
                if (!string.IsNullOrEmpty(over))
                {
                    ItemDefinition src = GameData.Item(over);
                    if (src != null && Transmog.Compatible(item, src) && src.appearance != null) d = src.appearance;
                }
                if (d == null && IsVisibleSlot((EquipmentSlot)i))
                {
                    d = cat != null ? cat.Fallback(item.equipType) : null;
                    WarnOnce($"'{item.Id}' hat kein Aussehen – Ersatz {(d != null ? d.Id : "keiner")}.");
                }
                defs[i] = d;
            }
            return defs;
        }

        /// <summary>Plaetze, die am Koerper sichtbar sind (Schmuck vorerst nicht).</summary>
        public static bool IsVisibleSlot(EquipmentSlot s) =>
            s != EquipmentSlot.Neck && s != EquipmentSlot.Ring1 && s != EquipmentSlot.Ring2 && s != EquipmentSlot.Trinket1 && s != EquipmentSlot.Trinket2;

        private static void WarnOnce(string msg)
        {
            if (Warned.Add(msg)) Debug.LogWarning("[Ashveil] Aussehen: " + msg);
        }

        // ================================================================== Waffen

        private void PlaceWeapons()
        {
            if (_rig == null || !_rig.Ready) return;
            WeaponDefinition active = _weapons != null ? _weapons.ActiveWeapon : null;
            EquipmentSlot? drawn = null;
            ItemStack[] eq = _equipment != null ? _equipment.Equipped : null;
            if (active != null && eq != null)
            {
                foreach (EquipmentSlot s in new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Ranged })
                {
                    ItemDefinition item = eq[(int)s].IsEmpty ? null : GameData.Item(eq[(int)s].id);
                    if (item != null && item.weapon == active) { drawn = s; break; }
                }
            }
            Transform anchor = null;
            if (_fx != null && active != null) anchor = active.HeldLikeMelee ? _fx.MeleePivot : _fx.RangedPivot;
            _rig.PlaceWeapons(drawn, anchor, active != null && active.HeldLikeMelee);
            AppearanceDefinition drawnDef = drawn.HasValue ? _rig.SlotDefinition(drawn.Value) : null;
            DrawnSheath = drawnDef != null ? drawnDef.sheath : active != null && !active.HeldLikeMelee ? WeaponSheath.HipRight : WeaponSheath.HipLeft;

            // Platzhalter-Waffen nur, wenn die gezogene Waffe (noch) kein eigenes Modell hat
            bool drawnHasModel = drawn.HasValue && _rig.WeaponModel(drawn.Value) != null;
            DrawnHasModel = drawnHasModel;
            bool unarmed = drawn == null;
            bool showMelee = !unarmed && !drawnHasModel && active != null && active.HeldLikeMelee;
            bool showRanged = !unarmed && !drawnHasModel && active != null && !active.HeldLikeMelee;
            foreach (Renderer r in _meleePlaceholders) if (r != null && !IsOurs(r)) r.enabled = showMelee;
            foreach (Renderer r in _rangedPlaceholders) if (r != null && !IsOurs(r)) r.enabled = showRanged;
        }

        private static bool IsOurs(Renderer r) => r.name.StartsWith("Equip");

        // ================================================================== Detailstufe

        private void Update()
        {
            if (_rig == null) return;
            _lodTimer -= Time.unscaledDeltaTime;
            if (_lodTimer > 0f) return;
            _lodTimer = lodInterval;
            Camera cam = Camera.main;
            if (cam == null) return;
            float d = Vector3.Distance(cam.transform.position, transform.position);
            if (_lod == null) _lod = GetComponent<CharacterLod>();
            _rig.SetDetailVisible(d < _rig.DetailDistance && (_lod == null || _lod.DetailsAllowed));
        }
    }

    /// <summary>Regeln fuer kosmetische Uebertragung (spaeterer Ausbau – Datenmodell und Pruefung stehen).</summary>
    public static class Transmog
    {
        /// <summary>Darf <paramref name="source"/> das Aussehen fuer das getragene <paramref name="worn"/> liefern?</summary>
        public static bool Compatible(ItemDefinition worn, ItemDefinition source)
        {
            if (worn == null || source == null || source.appearance == null) return false;
            if (worn.equipType != source.equipType) return false;
            if (worn.equipType == EquipType.Weapon)
            {
                // Ein Gewehr wird nie als Schwert dargestellt: gleiche Waffenart und Handhabung
                if (worn.IsWeapon != source.IsWeapon) return false;
                if (worn.IsWeapon && worn.weapon.weaponClass != source.weapon.weaponClass) return false;
                if (worn.Hand != source.Hand) return false;
            }
            return true;
        }

        /// <summary>Server: zusaetzlich Klasse (Ruestungsart) und Freischaltung pruefen.</summary>
        public static bool Allowed(ItemDefinition worn, ItemDefinition source, CharacterClassDefinition cls, ICollection<string> unlocked)
        {
            if (!Compatible(worn, source)) return false;
            if (InventoryModel.ClassRestriction(source, cls) != null) return false;
            return unlocked != null && unlocked.Contains(source.appearance.Id);
        }
    }
}
