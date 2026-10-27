using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Items;
using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// Baut sichtbare Ausruestung und persoenliches Aussehen auf ein Koerpermodell (Spielfigur oder Vorschau):
    ///   · Kleidungsschalen als SkinnedMeshRenderer auf demselben Skelett (folgen jeder Animation)
    ///   · starre Teile als Kinder der Knochen (Helm am Kopf, Platten an Armen/Beinen, Waffen am Griff)
    ///   · Haut unter Kleidung ausgeblendet, Haare/Bart unter Helm/Maske ausgeblendet
    ///   · Frisur, Bart, Hautton, Haarfarbe bleiben bei jedem Ausruestungswechsel erhalten
    ///   · M7: Gesicht (gemalte Ebene + dezente Kopfform), Augenfarbe, Gesichtsmerkmal, Bartstoppeln – die Ebenen
    ///     liegen auf einer duennen Kopie der Gesichts-Dreiecke (geteilt, keine eigene Textur je Figur)
    /// Aendert sich ein Platz, wird nur dieser neu gebaut (alte Objekte werden zerstoert – nie doppelt).
    /// Meshes und Materialien sind zwischen allen Figuren geteilt.
    /// </summary>
    public sealed class AppearanceRig : MonoBehaviour
    {
        private const int Slots = InventoryModel.EquipCount;
        private const float ReferenceHeight = 1.81f;

        private sealed class Built
        {
            public AppearanceDefinition Def;
            public string Label;
            public int DyeHash;
            public GameObject Root;
            public Transform Weapon;
            /// <summary>Objekte ausserhalb von Root (an Knochen/Koerper) – werden mit dem Platz entfernt.</summary>
            public readonly List<GameObject> Owned = new List<GameObject>();
            public readonly List<Renderer> Details = new List<Renderer>();
            public readonly List<ShellLayer> Shells = new List<ShellLayer>();
            /// <summary>21.10.: Renderer eines echten Kleidungsmodells mit ihrer Detailstufe (0 = voll, aus Namensendung _Stufe&lt;n&gt;).</summary>
            public readonly List<(int Stufe, Renderer R)> Kleid = new List<(int Stufe, Renderer R)>();
            public int RendererCount;
        }

        private Transform _charRoot;
        private SkinnedMeshRenderer _body;
        private Mesh _bodyMesh;
        private Material[] _bodyMaterials;
        private SkinnedMeshRenderer _eyebrows;
        private Material _eyebrowMaterial;
        private SkinnedMeshRenderer _eyes;
        private SkinnedMeshRenderer _builtinHair;
        private SkinnedMeshRenderer _builtinHairKarten;
        private Material _builtinHairMaterial;
        private Material _eyeMaterial;
        private int _eyeKey = -1;
        private SkinnedMeshRenderer _mpfbEyes;
        private Material _mpfbEyeMaterial;
        private int _mpfbEyeKey = -1;
        private Transform _head;
        private Vector3 _headBase = Vector3.one, _headScale = Vector3.one;
        private SkinnedMeshRenderer _overlay;
        private Mesh _overlaySource;
        private string _overlayKey = "";
        private readonly List<Texture2D> _overlayTextures = new List<Texture2D>();
        private Built _hairParts, _beardParts;
        private BodyShells _shells;
        // 19.10. abends (LODs): Detailstufen des Körpers (<Name>_Stufe1..n aus ashveil_hf_export.py) – bekommen dieselbe Haut und
        // dieselbe Verdeckung unter Kleidung wie Stufe 0; eine LODGroup schaltet sie (nur der Körper, Ausrüstung bleibt voll –
        // 21.10.: außer echten Kleidungsmodellen, deren Stufen mit in die LODGroup gehen, siehe UpdateLods).
        private sealed class Stufe { public SkinnedMeshRenderer R; public Mesh Mesh; public Material[] Mats; public BodyShells Shells; }
        private readonly List<Stufe> _stufen = new List<Stufe>();
        private static readonly float[] StufenHoehe = { 0.30f, 0.14f, 0.06f, 0.012f };   // Bildschirmanteil, ab dem die Stufe gilt
        // 21.10. (Tripo-Kleidung): LODGroup und Körper-Renderer je Stufe – echte Kleidungsmodelle kommen mit ihren Stufen dazu (UpdateLods)
        private LODGroup _lodGroup;
        private Renderer[][] _lodKoerper;
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Quaternion> _bindRot = new Dictionary<string, Quaternion>();
        private readonly Built[] _slots = new Built[Slots];
        private float _sizeScale = 1f;
        private float _breitenScale = 1f;   // 22.10.: Größe für Schulterteile (Schulterbreite statt nur Höhe)
        private const float ReferenceSchulterVerhaeltnis = 0.236f;   // Schultergelenkabstand / Körperhöhe des Referenzkörpers (CHR_M_TR01)
        private bool _detailVisible = true;

        private GameObject _hair, _beard;
        private int _hairKey = -1, _beardKey = -1, _skinKey = -1;
        private Color _hairColor = Color.white;
        private readonly HashSet<string> _warned = new HashSet<string>();

        public bool Ready { get; private set; }
        public float SizeScale => _sizeScale;
        public float BreitenScale => _breitenScale;

        /// <summary>Ruhelage eines Knochens (kanonischer Name) in Weltkoordinaten aus den Bindposen, sonst null.</summary>
        private Vector3? BindWelt(Transform[] bones, Matrix4x4[] bind, string kanonisch)
        {
            for (int i = 0; i < bones.Length && i < bind.Length; i++)
                if (bones[i] != null && (bones[i].name == kanonisch || SkelettNamen.Kanonisch(bones[i].name) == kanonisch))
                    return _body.transform.TransformPoint(bind[i].inverse.GetColumn(3));
            return null;
        }

        /// <summary>Teil sitzt an Schulter/Oberarm – wird nach der Schulterbreite skaliert.</summary>
        private static bool SchulterKnochen(string bone) => bone.StartsWith("upperarm") || bone.StartsWith("clavicle");
        public int BuildCount { get; private set; }

        // ================================================================== Einrichten

        public void Init(Transform characterRoot)
        {
            if (Ready) return;
            _charRoot = characterRoot != null ? characterRoot : transform;
            // Koerper = groesster SkinnedMeshRenderer des Modells
            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                string n = smr.name.ToLowerInvariant();
                if (n.Contains("eyebrow") || n.EndsWith("_brauen")) { _eyebrows = smr; _eyebrowMaterial = smr.sharedMaterial; continue; }
                // 17.10. nachts: MPFB-Körper (MakeHuman) bringen Augen, Wimpern und Haare als eigene Teile mit eigener UV mit.
                // Augen/Wimpern behalten ihr Material (Katalog-Augentexturen sind für Quaternius-UVs); Haare = eingebaute Frisur.
                // 18.10.: MPFB-Augen bekommen die Augenfarbe aus eigenen Texturen (Resources/Augen/T_Augen_MPFB_<Index>, gleiche
                // Reihenfolge wie der Katalog) – die Katalog-Augentexturen passen nur auf die Quaternius-UVs.
                if (n.EndsWith("_augen")) { _mpfbEyes = smr; _mpfbEyeMaterial = smr.sharedMaterial; continue; }
                if (n.EndsWith("_wimpern")) continue;
                if (n.EndsWith("_haare")) { _builtinHair = smr; _builtinHairMaterial = smr.sharedMaterial; continue; }
                if (n.EndsWith("_haarkarten") || n.EndsWith("_zopf")) { _builtinHairKarten = smr; continue; }   // 19.10.: Strähnen-Karten / Meshy-Zopf gehören zur eingebauten Frisur
                if (n.Contains("eye")) { _eyes = smr; _eyeMaterial = smr.sharedMaterial; continue; }
                if (n.StartsWith("equip") || n.StartsWith("hair") || n.StartsWith("beard")) continue;
                if (n.Contains("_stufe")) { _stufen.Add(new Stufe { R = smr, Mesh = smr.sharedMesh, Mats = smr.sharedMaterials }); continue; }
                if (_body == null || smr.sharedMesh.vertexCount > _body.sharedMesh.vertexCount) _body = smr;
            }
            if (_body == null)
            {
                Debug.LogWarning($"[Ashveil] Kein Koerpermodell unter '{name}' – Ausruestung wird nicht angezeigt.");
                return;
            }
            _bodyMesh = _body.sharedMesh;
            _bodyMaterials = _body.sharedMaterials;
            // 21.10.: Mixamo-Skelett (Tripo) behält seine Namen – zusätzlich unter unserem Namen eintragen (SkelettNamen)
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;
                string k = SkelettNamen.Kanonisch(t.name);
                if (k != t.name && !_bones.ContainsKey(k)) _bones[k] = t;
            }
            // 19.10.: Zopf schwingt nach (nur Darstellung, lokal) – wenn das Modell Zopf-Knochen hat
            if (_bones.TryGetValue("zopf_01", out Transform zopf) && zopf.GetComponentInParent<ZopfPendel>() == null && zopf.parent != null)
                zopf.parent.gameObject.AddComponent<ZopfPendel>().Einrichten(zopf);

            // Ruhepose jedes Knochens im Figurenraum (aus den Bindposen, unabhaengig von der aktuellen Animation)
            Transform[] bones = _body.bones;
            Matrix4x4[] bind = _bodyMesh.bindposes;
            Quaternion toChar = Quaternion.Inverse(_charRoot.rotation) * _body.transform.rotation;
            for (int i = 0; i < bones.Length && i < bind.Length; i++)
            {
                if (bones[i] == null) continue;
                _bindRot[bones[i].name] = toChar * bind[i].inverse.rotation;
                string k = SkelettNamen.Kanonisch(bones[i].name);
                if (k != bones[i].name) _bindRot[k] = _bindRot[bones[i].name];
            }
            if (_bones.TryGetValue("Head", out Transform head)) { _head = head; _headBase = head.localScale; }
            _shells = BodyShells.For(_body, _charRoot);
            SetupStufen();
            if (_shells != null && _shells.Valid) _sizeScale = Mathf.Clamp(_shells.HeightMeters / ReferenceHeight, 0.7f, 1.3f);
            // 22.10.2026 (Max: Rüstung unisex): Teile an Schulter/Oberarm nach der Schulterbreite – Frauen haben bei gleicher Höhe
            // deutlich schmalere Schultern (Gelenkabstand/Höhe gemessen: Mann 0,236, Frau 0,187). Nur nach der Höhe skaliert saß die
            // Stahl-Schulterplatte an der Frau zu groß und zu weit innen (Brustflügel an der Brust).
            _breitenScale = _sizeScale;
            if (_shells != null && _shells.Valid && _shells.HeightMeters > 0.5f)
            {
                Vector3? l = BindWelt(bones, bind, "upperarm_l"), r = BindWelt(bones, bind, "upperarm_r");
                if (l.HasValue && r.HasValue)
                    _breitenScale = _sizeScale * Mathf.Clamp((l.Value - r.Value).magnitude / _shells.HeightMeters / ReferenceSchulterVerhaeltnis, 0.75f, 1.1f);
            }
            Ready = true;
        }

        // ================================================================== Anwenden

        /// <summary>
        /// defs: Aussehen je Ausruestungsplatz (null = leer/ausgeblendet). Nur geaenderte Plaetze werden neu gebaut.
        /// </summary>
        public void Apply(AppearanceDefinition[] defs, CharacterAppearanceData look, AppearanceCatalog catalog)
        {
            if (!Ready) return;
            look = look ?? new CharacterAppearanceData();
            look.EnsureArrays();
            // 18.10.: Körper-Index aus dem eigenen Modell bestimmen – passt look.body nicht zu diesem Körper (z. B. Lab schaltet
            // auf die Frau um, das Aussehen meldet weiter Körper 0), bekam die Frau die Haut des Mannes. Kopie, Original bleibt.
            int eigen = OwnBodyIndex(catalog);
            if (eigen >= 0 && eigen != look.body) { look = look.Clone(); look.body = eigen; }
            bool umgebaut = false;
            for (int i = 0; i < Slots; i++)
            {
                AppearanceDefinition d = defs != null && i < defs.Length ? defs[i] : null;
                if (d != null && (d.bodies & VariantOfThisBody(catalog, look)) == 0) d = null; // passt nicht zu diesem Koerper
                int dyeHash = d != null ? DyeHash(look.dyes[i]) : 0;
                Built b = _slots[i];
                if (b != null && b.Def == d && b.DyeHash == dyeHash) continue;
                DestroyBuilt(b);
                _slots[i] = d != null ? Build((EquipmentSlot)i, d, look.dyes[i], catalog) : null;
                BuildCount++;
                umgebaut = true;
            }
            ApplyPersonal(look, catalog);
            UpdateBodyMask();
            UpdateHides();
            SetDetailVisible(_detailVisible, true);
            if (umgebaut) UpdateLods();
        }

        private int _ownBodyIndex = -2; private AppearanceCatalog _ownBodyCat;
        private int OwnBodyIndex(AppearanceCatalog cat)
        {
            if (cat == _ownBodyCat && _ownBodyIndex != -2) return _ownBodyIndex;   // einmal je Figur (viele Figuren, kein Suchen je Apply)
            _ownBodyCat = cat; _ownBodyIndex = FindOwnBody(cat); return _ownBodyIndex;
        }
        private int FindOwnBody(AppearanceCatalog cat)
        {
            if (cat == null || _body == null || _body.sharedMesh == null) return -1;
            for (int i = 0; i < cat.bodies.Count; i++)
            {
                GameObject m = cat.bodies[i].model;
                if (m == null) continue;
                foreach (SkinnedMeshRenderer r in m.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.sharedMesh == _body.sharedMesh) return i;
            }
            return -1;
        }

        private BodyVariantMask VariantOfThisBody(AppearanceCatalog catalog, CharacterAppearanceData look) =>
            catalog != null ? catalog.VariantOf(look.body) : BodyVariantMask.All;

        private static int DyeHash(DyeSet d) => d.primary | (d.secondary << 8) | (d.detail << 16);

        private static Color ChannelColor(AppearanceDefinition d, DyeChannel c, DyeSet dye, AppearanceCatalog cat)
        {
            byte idx = dye.Get(c);
            if (idx > 0 && cat != null && idx <= cat.dyes.Count) return cat.dyes[idx - 1].color;
            return d.DefaultColor(c);
        }

        private Built Build(EquipmentSlot slot, AppearanceDefinition d, DyeSet dye, AppearanceCatalog cat)
        {
            var b = new Built { Def = d, DyeHash = DyeHash(dye) };
            b.Root = new GameObject("Equip " + slot + " (" + d.Id + ")");
            b.Root.transform.SetParent(transform, false);
            b.Root.layer = gameObject.layer;

            // 21.10.2026 (Tripo-Kleidung): echtes Modell fuer genau diesen Koerper → Schalen UND Grundformen dieses Platzes entfallen
            // (sonst laegen z. B. die Torus-Saeume des Gambesons ueber der Tunika). Passt kein Eintrag: alles wie bisher.
            SkinnedModel km = KleidFuer(d);
            if (km != null && BuildKleid(b, slot, km, ChannelColor(d, km.channel, dye, cat))) return b;

            // Verformbare Schalen: gleiches Skelett, gleiche Lage wie der Koerper
            if (_shells != null && _shells.Valid)
            {
                foreach (ShellLayer s in d.shells)
                {
                    Mesh m = _shells.Shell(s);
                    if (m == null) continue;
                    var go = new GameObject("Equip Shell " + s.regions);
                    go.layer = gameObject.layer;
                    go.transform.SetParent(_body.transform.parent, false);
                    go.transform.localPosition = _body.transform.localPosition;
                    go.transform.localRotation = _body.transform.localRotation;
                    go.transform.localScale = _body.transform.localScale;
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = m;
                    smr.bones = _body.bones;
                    smr.rootBone = _body.rootBone;
                    smr.localBounds = _body.localBounds;
                    smr.sharedMaterial = SurfaceMaterials.Get(s.surface, ChannelColor(d, s.channel, dye, cat));
                    b.Owned.Add(go);
                    b.Shells.Add(s);
                    b.RendererCount++;
                }
            }

            // Starre Teile
            bool weapon = d.slot == EquipType.Weapon;
            Transform weaponRoot = null;
            if (weapon)
            {
                weaponRoot = new GameObject("Weapon Model").transform;
                weaponRoot.SetParent(b.Root.transform, false);
                weaponRoot.gameObject.layer = gameObject.layer;
                b.Owned.Add(weaponRoot.gameObject); // wandert zwischen Hand und Koerper
                b.Weapon = weaponRoot;
            }
            foreach (RigidPart p in d.parts)
            {
                Color col = ChannelColor(d, p.channel, dye, cat);
                if (weapon || p.bone == "Grip")
                {
                    AddPart(b, p, weaponRoot != null ? weaponRoot : b.Root.transform, false, col, true);
                    continue;
                }
                // 22.10.2026 (Max: Ausrüstung soll anliegen wie in WoW): Ringe und flache Streifen werden eng anliegende Bänder
                if (AddBand(b, d, p, false, col)) { if (p.mirror) AddBand(b, d, p, true, col); continue; }
                AddPart(b, p, null, false, col, false);
                if (p.mirror) AddPart(b, p, null, true, col, false);
            }
            return b;
        }

        // ================================================================== Echte Kleidung (21.10.2026)

        /// <summary>Kleidungsmodell fuer DIESEN Koerper (Name des Koerpernetzes, ersatzweise des Renderers) – null = Schalen wie bisher.</summary>
        private SkinnedModel KleidFuer(AppearanceDefinition d)
        {
            if (d == null || d.modelle == null || d.modelle.Count == 0 || _bodyMesh == null) return null;
            SkinnedModel km = d.ModellFuer(_bodyMesh.name) ?? d.ModellFuer(_body.name);
            return km != null && km.model != null ? km : null;
        }

        /// <summary>
        /// 21.10.2026 (Tripo-Kleidung, DEC-20261021-001): verformbares Kleid mit dem Skelett des Koerpers (Mixamo, gleiche Bindposen)
        /// nach Knochennamen einhaengen – wie die Frisur. Faerbbarer, beidseitiger Stoff (offene Raender, keine UV). Die Haut darunter
        /// blendet <see cref="SkinnedModel.hide"/> aus (UpdateBodyMask, auch fuer jede Koerperstufe). Detailstufen _Stufe&lt;n&gt; gehen
        /// in die LODGroup des Koerpers (UpdateLods).
        /// </summary>
        private bool BuildKleid(Built b, EquipmentSlot slot, SkinnedModel km, Color color)
        {
            Material mat = SurfaceMaterials.Get(km.surface, color);
            GameObject inst = Einhaengen(km.model, "Equip " + slot + " Kleid", _ => mat);
            if (inst == null) return false;
            SkinnedMeshRenderer[] smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length == 0)
            {
                Warn("Kleid '" + km.model.name + "' ohne verformbares Netz – Schalen statt Modell.");
                Zerstoeren(inst);
                return false;
            }
            b.Owned.Add(inst);
            foreach (SkinnedMeshRenderer smr in smrs)
            {
                int stufe = KleidStufe(smr.name);
                smr.name = "Equip " + slot + " Kleid S" + stufe;   // Init ueberspringt „equip…“; EquipmentVisuals erkennt es als unser Teil
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                foreach (Transform t in smr.bones)
                    if (t == null) { Warn("Kleid '" + km.model.name + "': Knochen fehlt am Koerper '" + _bodyMesh.name + "' (anderes Skelett?)."); break; }
                b.Kleid.Add((stufe, smr));
                b.RendererCount++;
            }
            if (km.hide != null) b.Shells.AddRange(km.hide);   // nur so wird verdeckt (BodyShells.Masked unveraendert)
            return true;
        }

        /// <summary>Detailstufe aus der Namensendung „_Stufe&lt;n&gt;“ (Blender-Export wie beim Koerper), sonst 0.</summary>
        public static int KleidStufe(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            int i = name.LastIndexOf("_Stufe", System.StringComparison.OrdinalIgnoreCase);
            return i >= 0 && int.TryParse(name.Substring(i + 6), out int s) && s >= 0 ? s : 0;
        }

        /// <summary>
        /// LODGroup des Koerpers neu setzen: Stufe i = Koerperstufe i + je Platz die Kleidstufe i (fehlt sie, die letzte vorhandene
        /// darunter). Kleidstufen, die in keiner Stufe vorkommen, werden ausgeschaltet (sonst immer gezeichnet). Ohne Koerperstufen:
        /// nur Kleidstufe 0. Schalen und Grundformen bleiben wie bisher ausserhalb (immer voll).
        /// </summary>
        private void UpdateLods()
        {
            var kleid = new List<(int Stufe, Renderer R)>();
            foreach (Built b in _slots)
                if (b != null)
                    foreach ((int Stufe, Renderer R) k in b.Kleid)
                        if (k.R != null) kleid.Add(k);
            if (_lodGroup == null || _lodKoerper == null)
            {
                foreach ((int Stufe, Renderer R) k in kleid) k.R.enabled = k.Stufe == 0;
                return;
            }
            var genutzt = new HashSet<Renderer>();
            var lods = new LOD[_lodKoerper.Length];
            for (int i = 0; i < lods.Length; i++)
            {
                var rs = new List<Renderer>();
                foreach (Renderer r in _lodKoerper[i]) if (r != null) rs.Add(r);
                foreach (Built b in _slots)
                {
                    if (b == null || b.Kleid.Count == 0) continue;
                    int best = -1;
                    foreach ((int Stufe, Renderer R) k in b.Kleid) if (k.R != null && k.Stufe <= i && k.Stufe > best) best = k.Stufe;
                    foreach ((int Stufe, Renderer R) k in b.Kleid)
                        if (k.R != null && k.Stufe == best) { rs.Add(k.R); genutzt.Add(k.R); }
                }
                lods[i] = new LOD(StufenHoehe[i], rs.ToArray());
            }
            foreach ((int Stufe, Renderer R) k in kleid) k.R.enabled = genutzt.Contains(k.R);
            _lodGroup.SetLODs(lods);
            _lodGroup.RecalculateBounds();
        }

        /// <summary>Im Spiel Destroy, im Edit-Modus (EditMode-Tests, Vorschau) DestroyImmediate – Destroy ist dort verboten.</summary>
        private static void Zerstoeren(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        /// <summary>
        /// Ring/Streifen als Band am Körper (BodyShells.Band): einige Millimeter über der Schale dieses Gegenstands (bzw. der Haut),
        /// gleiches Skelett – sitzt in jeder Pose. false = bleibt starres Teil (Kuppel, Kugel, gedrehte Platte, keine Schalen).
        /// </summary>
        private bool AddBand(Built b, AppearanceDefinition d, RigidPart p, bool mirror, Color color)
        {
            if (_shells == null || !_shells.Valid || _body == null) return false;
            float basis = 0.006f;
            foreach (ShellLayer s in d.shells) basis = Mathf.Max(basis, s.offset);
            // jedes weitere Band dieses Gegenstands 2 mm weiter aussen – übereinanderliegende Bänder (zwei Kapuzenlagen, Gürtel über Saum) flimmern sonst
            int schon = 0;
            foreach (GameObject o in b.Owned) if (o != null && o.name.StartsWith("Equip Band")) schon++;
            if (!_shells.Band(p, mirror, basis + 0.006f + 0.002f * schon, out ShellLayer band)) return false;
            Mesh m = _shells.Shell(band);
            if (m == null || m.triangles.Length == 0) return false;   // Band trifft keine Haut (anderer Körperbau) → starres Teil
            var go = new GameObject("Equip Band " + p.shape);
            go.layer = gameObject.layer;
            go.transform.SetParent(_body.transform.parent, false);
            go.transform.localPosition = _body.transform.localPosition;
            go.transform.localRotation = _body.transform.localRotation;
            go.transform.localScale = _body.transform.localScale;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = m;
            smr.bones = _body.bones;
            smr.rootBone = _body.rootBone;
            smr.localBounds = _body.localBounds;
            smr.sharedMaterial = SurfaceMaterials.Band(band.surface, color);
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.Owned.Add(go);
            b.RendererCount++;
            return true;
        }

        private void AddPart(Built b, RigidPart p, Transform gripParent, bool mirror, Color color, bool isGrip)
        {
            string boneName = mirror ? Mirror(p.bone) : p.bone;
            Transform bone = null;
            if (!isGrip && !_bones.TryGetValue(boneName, out bone))
            {
                Warn("Knochen '" + boneName + "' fehlt (" + (b.Def != null ? b.Def.Id : b.Label) + ") – Teil ausgelassen.");
                return;
            }

            GameObject go;
            if (p.prefab != null)
            {
                go = Instantiate(p.prefab);
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Zerstoeren(c);
                // Frisur/Bart (M7): Modell bekommt die Haarfarbe. 22.10.2026: Anbauteile der Ausrüstung (Helm, Schulter, Schnalle) ebenso
                // die Oberfläche des Gegenstands samt Rüstungstextur und Farbe – wie Schalen und Bänder, einheitlich gefärbt.
                // Waffenmodelle (Griff) behalten ihre eigenen Materialien (Kampftest: Nachhall-Lichtpuls der Klinge fehlte sonst).
                if (b.Def == null || !isGrip)
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    // 22.10.: modellierte Anbauteile mit eigener Textur (Tripo/Artlist: Nieten, Lederriemen, Schnallen) behalten ihr Material
                    // und werden nur leicht in der Gegenstandsfarbe getönt – die Rüstungstextur würde die Riemen zu Metall machen
                    if (b.Def != null && mats.Length > 0 && mats[0] != null && mats[0].mainTexture != null)
                    {
                        for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = SurfaceMaterials.Modell(mats[i], p.surface, color);
                        r.sharedMaterials = mats;
                        continue;
                    }
                    for (int i = 0; i < mats.Length; i++) mats[i] = SurfaceMaterials.Get(p.surface, color);
                    r.sharedMaterials = mats;
                }
            }
            else
            {
                go = new GameObject("Equip Part " + p.shape);
                go.AddComponent<MeshFilter>().sharedMesh = ProcMesh.Get(p);
                go.AddComponent<MeshRenderer>().sharedMaterial = SurfaceMaterials.Get(p.surface, color);
            }
            SetLayer(go, gameObject.layer);
            Vector3 offset = p.offset, euler = p.euler, size = p.size;
            if (mirror)
            {
                offset.x = -offset.x;
                euler = new Vector3(euler.x, -euler.y, -euler.z);
                size.x = -size.x;
            }

            if (isGrip)
            {
                go.transform.SetParent(gripParent, false);
                go.transform.localPosition = offset;
                go.transform.localRotation = Quaternion.Euler(euler);
                go.transform.localScale = size;
            }
            else
            {
                float rootScale = _charRoot.lossyScale.x;
                Quaternion rel = _bindRot.TryGetValue(boneName, out Quaternion r) ? r : Quaternion.Inverse(_charRoot.rotation) * bone.rotation;
                float boneScale = Mathf.Max(1e-5f, bone.lossyScale.x);
                float skala = SchulterKnochen(boneName) ? _breitenScale : _sizeScale;
                go.transform.SetParent(bone, false);
                go.transform.localRotation = Quaternion.Inverse(rel) * Quaternion.Euler(euler);
                go.transform.localPosition = Quaternion.Inverse(rel) * (offset * skala * rootScale) / boneScale;
                go.transform.localScale = size * skala * rootScale / boneScale;
                b.Owned.Add(go);
            }
            foreach (Renderer rend in go.GetComponentsInChildren<Renderer>(true))
            {
                rend.shadowCastingMode = p.detail ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                if (p.detail) b.Details.Add(rend);
                b.RendererCount++;
            }
        }

        private static string Mirror(string b)
        {
            if (b.EndsWith("_l")) return b.Substring(0, b.Length - 2) + "_r";
            if (b.EndsWith("_r")) return b.Substring(0, b.Length - 2) + "_l";
            return b;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        private void Warn(string msg)
        {
            if (_warned.Add(msg)) Debug.LogWarning("[Ashveil] Aussehen: " + msg);
        }

        // ================================================================== Persoenliches Aussehen

        private void ApplyPersonal(CharacterAppearanceData look, AppearanceCatalog cat)
        {
            if (cat == null) return;
            // Hautton
            ApplyFace(look, cat);
            int skinKey = look.body * 100 + look.skin;
            if (skinKey != _skinKey)
            {
                _skinKey = skinKey;
                Texture2D tex = null;
                Color tint = Color.white;
                List<Texture2D> texs = look.body < cat.bodies.Count ? cat.bodies[look.body].skinTextures : null;
                if (cat.skinTones.Count > 0 && texs != null && texs.Count > 0)
                {
                    AppearanceCatalog.SkinTone tone = cat.skinTones[Mathf.Clamp(look.skin, 0, cat.skinTones.Count - 1)];
                    tex = texs[Mathf.Clamp(tone.baseTexture, 0, texs.Count - 1)];
                    tint = tone.tint;
                }
                else if (texs != null && look.skin < texs.Count) tex = texs[look.skin];
                var mats = (Material[])_bodyMaterials.Clone();
                if (tex != null && mats.Length > 0) mats[0] = SurfaceMaterials.Skin(_bodyMaterials[0], tex, tint);
                _body.sharedMaterials = mats;
                foreach (Stufe st in _stufen) st.R.sharedMaterials = StufenMats(mats, st);
            }
            Color hairColor = cat.hairColors.Count > 0 ? cat.hairColors[Mathf.Clamp(look.hairColor, 0, cat.hairColors.Count - 1)].color : Color.white;
            bool colorChanged = hairColor != _hairColor;
            _hairColor = hairColor;

            int hairKey = look.hair;
            if (hairKey != _hairKey || colorChanged)
            {
                _hairKey = hairKey;
                if (_hair != null) Zerstoeren(_hair);
                DestroyBuilt(_hairParts);
                bool ok = cat.StyleAllowed(cat.hairstyles, look.hair, look.body);
                _hair = ok ? Attach(cat.hairstyles[look.hair].model, "Hair", hairColor) : null;
                _hairParts = ok ? BuildParts(cat.hairstyles[look.hair].parts, PartColor(hairColor), "Frisur") : null;
            }
            int beardKey = look.beard;
            if (beardKey != _beardKey || colorChanged)
            {
                _beardKey = beardKey;
                if (_beard != null) Zerstoeren(_beard);
                DestroyBuilt(_beardParts);
                bool ok = cat.StyleAllowed(cat.beards, look.beard, look.body);
                _beard = ok ? Attach(cat.beards[look.beard].model, "Beard", hairColor) : null;
                _beardParts = ok ? BuildParts(cat.beards[look.beard].parts, PartColor(hairColor), "Bart") : null;
            }
            if (_eyebrows != null && _eyebrowMaterial != null) _eyebrows.sharedMaterial = SurfaceMaterials.Tint(_eyebrowMaterial, Color.Lerp(hairColor, Color.black, 0.15f));
            if (_builtinHair != null && _builtinHairMaterial != null)
            {
                // eingebaute Frisur nur ohne gewählte Katalog-Frisur (sonst doppelte Haare)
                _builtinHairWanted = _hair == null && (_hairParts == null || _hairParts.Owned.Count == 0);
                // 18.10. nachts (ART-122): Haare nach Max' Vorlage tragen ihre echte Farbe in der Textur – nicht umfärben
                Texture ht = _builtinHairMaterial.HasProperty("_BaseMap") ? _builtinHairMaterial.GetTexture("_BaseMap") : null;
                bool vorlageHaar = ht != null && ht.name.Contains("_Vorlage_");
                _builtinHair.sharedMaterial = vorlageHaar ? _builtinHairMaterial : SurfaceMaterials.Tint(_builtinHairMaterial, hairColor);
                UpdateHides();
            }
        }
        private bool _builtinHairWanted = true;

        /// <summary>Grundformen einer Frisur/eines Barts (Zopf, Irokese, Kinnbart …) in Haarfarbe an die Knochen haengen.</summary>
        private Built BuildParts(List<RigidPart> parts, Color color, string label)
        {
            if (parts == null || parts.Count == 0) return null;
            var b = new Built { Label = label };
            foreach (RigidPart p in parts)
            {
                AddPart(b, p, null, false, color, false);
                if (p.mirror) AddPart(b, p, null, true, color, false);
            }
            return b;
        }

        /// <summary>Haarmodelle sind Textur (~0,56 grau) mal Haarfarbe – Grundformen gleich dunkel faerben.</summary>
        private static Color PartColor(Color hair) => new Color(hair.r * 0.58f, hair.g * 0.58f, hair.b * 0.58f, 1f);

        private static void SetBuiltActive(Built b, bool on)
        {
            if (b == null) return;
            foreach (GameObject go in b.Owned) if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        // ================================================================== Gesicht (M7)

        private void ApplyFace(CharacterAppearanceData look, AppearanceCatalog cat)
        {
            // Kopfform
            _headScale = cat.faces.Count > 0 ? cat.faces[Mathf.Clamp(look.face, 0, cat.faces.Count - 1)].headScale : Vector3.one;
            if (_headScale.x <= 0f || _headScale.y <= 0f || _headScale.z <= 0f) _headScale = Vector3.one;

            // Augenfarbe
            if (_eyes != null && _eyeMaterial != null && cat.eyeColors.Count > 0 && look.eyeColor != _eyeKey)
            {
                _eyeKey = look.eyeColor;
                Texture2D t = cat.eyeColors[Mathf.Clamp(look.eyeColor, 0, cat.eyeColors.Count - 1)].texture;
                _eyes.sharedMaterial = t != null ? SurfaceMaterials.Skin(_eyeMaterial, t) : _eyeMaterial;
            }

            if (_mpfbEyes != null && _mpfbEyeMaterial != null && look.eyeColor != _mpfbEyeKey)
            {
                _mpfbEyeKey = look.eyeColor;
                int idx = Mathf.Clamp(look.eyeColor, 0, Mathf.Max(0, cat.eyeColors.Count - 1));
                Texture2D t = Resources.Load<Texture2D>("Augen/T_Augen_MPFB_" + idx);
                _mpfbEyes.sharedMaterial = t != null ? SurfaceMaterials.Skin(_mpfbEyeMaterial, t) : _mpfbEyeMaterial;
            }

            // Ebenen: Gesicht, Bartstoppeln, Merkmal (in dieser Reihenfolge uebereinander)
            _overlayTextures.Clear();
            if (cat.faces.Count > 0) Add(AppearanceCatalog.OverlayFor(cat.faces[Mathf.Clamp(look.face, 0, cat.faces.Count - 1)].overlays, look.body));
            if (cat.StyleAllowed(cat.beards, look.beard, look.body)) Add(AppearanceCatalog.OverlayFor(cat.beards[look.beard].overlays, look.body));
            if (cat.FeatureAllowed(look.feature, look.body)) Add(AppearanceCatalog.OverlayFor(cat.features[look.feature].overlays, look.body));
            var key = new System.Text.StringBuilder();
            foreach (Texture2D t in _overlayTextures) key.Append(t.name).Append(',');
            _overlayMaterialsDirty |= key.ToString() != _overlayKey;
            _overlayKey = key.ToString();
            _overlayTemplate = cat.overlayTemplate;
            RefreshOverlay();

            void Add(Texture2D t) { if (t != null) _overlayTextures.Add(t); }
        }

        private Material _overlayTemplate;
        private bool _overlayMaterialsDirty = true;

        /// <summary>Gesichts-Ebenen neu aufsetzen (nach Ausruestungs- oder Aussehenswechsel).</summary>
        private void RefreshOverlay()
        {
            if (_body == null) return;
            // 18.10.: Die Gesichts-/Bart-/Merkmal-Ebenen sind auf die UV der Quaternius-Körper gemalt. Auf dem MPFB-Körper
            // (MakeHuman-UV) landeten sie als dunkle Striche auf Brust, Achsel und Hals → für MPFB aus, bis eigene Ebenen existieren.
            bool mpfb = _mpfbEyes != null || _body.sharedMesh != null && _body.sharedMesh.name.IndexOf("MPFB", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (mpfb) _overlayTextures.Clear();   // auch für SetDetailVisible (schaltet sonst wieder ein)
            bool want = _overlayTextures.Count > 0 && _overlayTemplate != null;
            if (!want)
            {
                if (_overlay != null) _overlay.enabled = false;
                return;
            }
            Mesh src = _body.sharedMesh;
            if (_overlay == null)
            {
                var go = new GameObject("Face Overlay");
                go.layer = gameObject.layer;
                go.transform.SetParent(_body.transform.parent, false);
                go.transform.localPosition = _body.transform.localPosition;
                go.transform.localRotation = _body.transform.localRotation;
                go.transform.localScale = _body.transform.localScale;
                _overlay = go.AddComponent<SkinnedMeshRenderer>();
                _overlay.bones = _body.bones;
                _overlay.rootBone = _body.rootBone;
                _overlay.localBounds = _body.localBounds;
                _overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _overlay.updateWhenOffscreen = false;
                _overlaySource = null;
                _overlayMaterialsDirty = true;
            }
            if (_overlaySource != src)
            {
                _overlaySource = src;
                _overlay.sharedMesh = FaceOverlayMesh.For(src);
            }
            if (_overlayMaterialsDirty)
            {
                _overlayMaterialsDirty = false;
                var mats = new Material[_overlayTextures.Count];
                for (int i = 0; i < mats.Length; i++) mats[i] = SurfaceMaterials.Overlay(_overlayTemplate, _overlayTextures[i], i);
                _overlay.sharedMaterials = mats;
            }
            _overlay.enabled = _overlay.sharedMesh != null && _detailVisible;
        }

        /// <summary>Test/Bericht: aktive Gesichtsebenen (Namen der Texturen, durch Komma getrennt).</summary>
        public string OverlaySummary()
        {
            if (_overlay == null || !_overlay.enabled) return "";
            var sb = new System.Text.StringBuilder();
            foreach (Material m in _overlay.sharedMaterials)
            {
                if (m == null) continue;
                Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(t != null ? t.name : "?");
            }
            return sb.ToString();
        }

        public string EyeTextureName
        {
            get
            {
                // MPFB-Augen haben eine eigene UV und einen eigenen Renderer; dessen tatsaechliches Material melden.
                SkinnedMeshRenderer eyes = _mpfbEyes != null ? _mpfbEyes : _eyes;
                if (eyes == null || eyes.sharedMaterial == null) return "";
                Material m = eyes.sharedMaterial;
                Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                return t != null ? t.name : "";
            }
        }

        public Vector3 HeadScale => _headScale;
        public int OverlayTriangles => _overlay != null && _overlay.sharedMesh != null ? _overlay.sharedMesh.triangles.Length / 3 : 0;

        private void LateUpdate()
        {
            // nach der Animation: Kopfform (die Animation darf die Skalierung nicht zuruecksetzen)
            if (_head != null && _headScale != Vector3.one) _head.localScale = Vector3.Scale(_headBase, _headScale);
            else if (_head != null && _head.localScale != _headBase) _head.localScale = _headBase;
        }

        /// <summary>Frisur/Bart (am selben Skelett geriggt): Knochen per Namen auf dieses Skelett umhaengen.</summary>
        private GameObject Attach(GameObject prefab, string label, Color color) => Einhaengen(prefab, label, m => SurfaceMaterials.Tint(m, color));

        /// <summary>
        /// Verformbares Modell (Frisur, Bart, 21.10.: Kleid) mit eigenem Skelett gleicher Namen: Knochen per Namen auf dieses Skelett
        /// umhaengen, Bindposen bei Bedarf anpassen (RebindToBody), Materialien ueber <paramref name="material"/> setzen.
        /// </summary>
        private GameObject Einhaengen(GameObject prefab, string label, System.Func<Material, Material> material)
        {
            if (prefab == null) return null;
            GameObject inst = Instantiate(prefab, transform, false);
            inst.name = label + " (" + prefab.name + ")";
            SetLayer(inst, gameObject.layer);
            foreach (SkinnedMeshRenderer smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform[] src = smr.bones;
                var mapped = new Transform[src.Length];
                for (int i = 0; i < src.Length; i++)
                    mapped[i] = src[i] != null && _bones.TryGetValue(src[i].name, out Transform t) ? t : null;
                smr.bones = mapped;
                if (smr.rootBone != null && _bones.TryGetValue(smr.rootBone.name, out Transform rb)) smr.rootBone = rb;
                RebindToBody(smr, src);
                var mats = smr.sharedMaterials;
                // Kleidung wird ohne Material importiert – mindestens ein Platz je Teilnetz
                if (mats.Length == 0 && smr.sharedMesh != null) mats = new Material[Mathf.Max(1, smr.sharedMesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = material(mats[i]);
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = false;
            }
            // Eigenes Skelett der Vorlage wird nicht gebraucht (erst sammeln: DestroyImmediate im Edit-Modus aendert die Kinderliste)
            var weg = new List<GameObject>();
            foreach (Transform child in inst.transform)
                if (child.GetComponent<SkinnedMeshRenderer>() == null && child.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
                    weg.Add(child.gameObject);
            foreach (GameObject g in weg) Zerstoeren(g);
            foreach (Animator a in inst.GetComponentsInChildren<Animator>(true)) Zerstoeren(a);
            return inst;
        }

        /// <summary>
        /// Art-Pass 3: Frisuren sind fuer das Quaternius-Skelett gebunden. Hat der Koerper andere Knochenachsen oder -positionen
        /// (eigene Basiskoerper), wuerde die Frisur verdreht sitzen. Dann die Bindposen so umrechnen, dass sie im Figurenraum
        /// dieselbe Lage zum Knochen hat wie auf der Vorlage (nur verschoben, nicht gedreht). Bei Quaternius-Koerpern: unveraendert.
        /// </summary>
        private void RebindToBody(SkinnedMeshRenderer smr, Transform[] templateBones)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null || _body == null || _bodyMesh == null) return;
            if (RebindCache.TryGetValue((mesh, _bodyMesh), out Mesh cached))
            {
                if (cached != null) smr.sharedMesh = cached;
                return;
            }
            Matrix4x4[] hairBind = mesh.bindposes;
            Matrix4x4[] bodyBind = _bodyMesh.bindposes;
            Transform[] bodyBones = _body.bones;
            var index = new Dictionary<string, int>();
            for (int j = 0; j < bodyBones.Length && j < bodyBind.Length; j++)
                if (bodyBones[j] != null && !index.ContainsKey(bodyBones[j].name)) index[bodyBones[j].name] = j;
            var next = (Matrix4x4[])hairBind.Clone();
            bool changed = false;
            for (int i = 0; i < hairBind.Length && i < templateBones.Length; i++)
            {
                if (templateBones[i] == null || !index.TryGetValue(templateBones[i].name, out int j)) continue;
                // Ruhelage beider Knochen in Weltkoordinaten (jeweils ueber das eigene Renderer-Transform – die Mesh-Raeume
                // von Vorlage und Koerper koennen gedreht sein)
                Matrix4x4 hairL2W = smr.transform.localToWorldMatrix;
                Matrix4x4 bodyRest = _body.transform.localToWorldMatrix * bodyBind[j].inverse;
                Matrix4x4 templRest = hairL2W * hairBind[i].inverse;
                Vector3 delta = bodyRest.GetPosition() - templRest.GetPosition();
                float angle = Quaternion.Angle(bodyRest.rotation, templRest.rotation);
                if (angle < 2f && delta.sqrMagnitude < 0.005f * 0.005f) continue;
                // Frisur behaelt ihre Lage und Drehung zur Figur, wird nur auf den neuen Kopf verschoben
                next[i] = bodyRest.inverse * Matrix4x4.Translate(delta) * hairL2W;
                changed = true;
            }
            if (!changed)
            {
                RebindCache[(mesh, _bodyMesh)] = null;
                return;
            }
            var copy = Instantiate(mesh);
            copy.name = mesh.name + " (angepasst)";
            copy.hideFlags = HideFlags.DontSave;
            copy.bindposes = next;
            RebindCache[(mesh, _bodyMesh)] = copy;
            smr.sharedMesh = copy;
        }

        private static readonly Dictionary<(Mesh, Mesh), Mesh> RebindCache = new Dictionary<(Mesh, Mesh), Mesh>();

        // ================================================================== Verdecken

        private void UpdateBodyMask()
        {
            if (_shells == null || !_shells.Valid) return;
            var covering = new List<ShellLayer>();
            foreach (Built b in _slots) if (b != null) covering.AddRange(b.Shells);
            Mesh m = _shells.Masked(covering);
            if (_body.sharedMesh != m) _body.sharedMesh = m;
            foreach (Stufe st in _stufen)
            {
                Mesh sm = st.Shells != null && st.Shells.Valid ? st.Shells.Masked(covering) : st.Mesh;
                if (st.R.sharedMesh != sm) st.R.sharedMesh = sm;
            }
            RefreshOverlay(); // Gesichtsebenen folgen dem (evtl. verdeckten) Koerper
        }

        private void UpdateHides()
        {
            AppearanceHides h = AppearanceHides.None;
            foreach (Built b in _slots) if (b != null) h |= b.Def.hides;
            if (_hair != null) _hair.SetActive((h & AppearanceHides.Hair) == 0);
            if (_beard != null) _beard.SetActive((h & AppearanceHides.Beard) == 0);
            SetBuiltActive(_hairParts, (h & AppearanceHides.Hair) == 0);
            SetBuiltActive(_beardParts, (h & AppearanceHides.Beard) == 0);
            if (_eyebrows != null) _eyebrows.enabled = (h & AppearanceHides.Eyebrows) == 0;
            if (_builtinHair != null) _builtinHair.enabled = _builtinHairWanted && (h & AppearanceHides.Hair) == 0;
            if (_builtinHairKarten != null) _builtinHairKarten.enabled = _builtinHairWanted && (h & AppearanceHides.Hair) == 0;
        }

        // ================================================================== Detailstufen

        public void SetDetailVisible(bool visible, bool force = false)
        {
            if (visible == _detailVisible && !force) return;
            _detailVisible = visible;
            foreach (Built b in _slots)
                if (b != null)
                    foreach (Renderer r in b.Details)
                        if (r != null) r.enabled = visible;
            if (_overlay != null) _overlay.enabled = visible && _overlayTextures.Count > 0 && _overlay.sharedMesh != null;
        }

        public float DetailDistance
        {
            get
            {
                float d = 25f;
                foreach (Built b in _slots) if (b != null) d = Mathf.Max(d, b.Def.detailDistance);
                return d;
            }
        }

        // ================================================================== Waffen


        public Transform WeaponModel(EquipmentSlot slot)
        {
            Built b = _slots[(int)slot];
            return b != null && b.Weapon != null ? b.Weapon : null;
        }

        public AppearanceDefinition SlotDefinition(EquipmentSlot slot) => _slots[(int)slot]?.Def;

        /// <summary>
        /// Gezogene Waffe in die Hand (Anker), alle anderen an ihren Platz am Koerper. Schild bleibt am Arm.
        /// </summary>
        /// <param name="meleeAnchor">Anker ist der Nahkampf-Drehpunkt: dort liegt die Handflaeche auf +Y und die Schneide zeigt
        /// in Handrichtung (−X). Die Modelle sind mit der Klingenbreite auf Y gebaut – daher 90° um die Klinge drehen,
        /// sonst zeigen Schneide und Parierstange zur Seite statt nach vorn.</param>
        public void PlaceWeapons(EquipmentSlot? drawn, Transform drawnAnchor, bool meleeAnchor = false)
        {
            foreach (EquipmentSlot slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Ranged })
            {
                Transform model = WeaponModel(slot);
                if (model == null) continue;
                AppearanceDefinition d = _slots[(int)slot].Def;
                if (drawn.HasValue && drawn.Value == slot && drawnAnchor != null && d.sheath != WeaponSheath.Arm)
                {
                    model.SetParent(drawnAnchor, false);
                    model.localPosition = Vector3.zero;
                    model.localRotation = meleeAnchor ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
                    model.localScale = Vector3.one;
                    continue;
                }
                WeaponSheath kind = d.sheath;
                // Zweite Waffe (Beidhaendig): gespiegelt an der anderen Huefte, damit sie nicht in der ersten steckt
                if (slot == EquipmentSlot.OffHand && WeaponModel(EquipmentSlot.MainHand) != null)
                {
                    if (kind == WeaponSheath.HipLeft) kind = WeaponSheath.HipRightOff;
                    else if (kind == WeaponSheath.HipRight) kind = WeaponSheath.HipLeftOff;
                }
                Sheathe(model, kind);
            }
        }

        private void Sheathe(Transform model, WeaponSheath sheath)
        {
            if (!SheathPose(sheath, out Transform t, out Vector3 lp, out Quaternion lr, out float ls)) return;
            model.SetParent(t, false);
            model.localRotation = lr;
            model.localPosition = lp;
            model.localScale = Vector3.one * ls;
        }

        /// <summary>
        /// Wo eine Waffe dieser Art am Koerper steckt: Knochen und Lage (Waffen-Ursprung = Griff, +Z = Klinge/Lauf)
        /// relativ zu diesem Knochen. Die Motion-Schicht haengt beim Wegstecken genau dorthin.
        /// </summary>
        public bool SheathPose(WeaponSheath sheath, out Transform bone, out Vector3 localPos, out Quaternion localRot, out float localScale)
        {
            string name;
            Vector3 offset;
            Quaternion rot;
            switch (sheath)
            {
                case WeaponSheath.HipLeft:
                    // Griff nach vorn geneigt (wie getragene Ritterschwerter): die rechte Hand greift quer ueber den
                    // Bauch mit natuerlich gedrehtem Unterarm. 16.10. (Rundumsicht): 35° → 22° Neigung, 12° → 6° Seitkippung –
                    // mit der langen Klinge stand die Spitze beim Laufen 50 cm hinter dem Bein und pendelte mit dem Becken.
                    // 17.10. (Max: „siehst du was noch falsch ist?“ – Spitze am Boden, Klinge durch den Oberschenkel beim Sprint):
                    // 22° → 30° nach hinten geneigt, 6 cm hoeher, 3 cm weiter aussen.
                    // 21.10. abends (Tripo-Standard, Körper stehen jetzt richtig herum): Spitze schleifte weiter am Boden → 30° → 38° nach
                    // hinten, Seitkippung 6° → 8° (Spitze endet bei 1,74–1,88 m Körpergröße etwa auf Kniehöhe hinter dem Bein).
                    // 21.10. nachts (Haut-Pruefung im Lab: die locker haengende linke Hand lag mit den Fingern 1,2–1,8 cm im Knauf/
                    // Parier): Griff 6 cm weiter nach vorn, 3 cm zur Mitte und 10 cm tiefer (Knauf stand auf Magenhoehe, Gurt-Schwert
                    // traegt ihn etwa in Nabelhoehe) – die linke Hand ruht im Stehen/Gehen darauf (WeaponHandPoser „Knaufhand“).
                    name = "pelvis"; offset = new Vector3(-0.22f, -0.02f, 0.12f);   // Frau: Parier lag bei −0,20 im Oberschenkel
                    rot = Quaternion.AngleAxis(-8f, Vector3.forward) * Quaternion.Euler(128f, 0f, 0f);
                    break;
                case WeaponSheath.HipRight:
                    // 21.10. nachts (Haut-Pruefung: Dolch-Parier 0,9 cm im rechten Oberschenkel): 2 cm weiter aussen, 3 cm hoeher
                    name = "pelvis"; offset = new Vector3(0.22f, 0.03f, 0.02f);
                    rot = Quaternion.AngleAxis(8f, Vector3.forward) * Quaternion.Euler(100f, 0f, 0f);
                    break;
                case WeaponSheath.Back:
                    // Griff ueber der rechten Schulter, Klinge/Schaft schraeg nach links unten ueber den Ruecken –
                    // so kann die rechte Hand ueber die Schulter greifen und nach oben-vorn ziehen
                    // 21.10. nachts (Haut-Pruefung: Parierstange 2,5 cm im rechten Oberarm, Knauf 1,4 cm im Ruecken): Griff 6 cm
                    // zur Mitte, 10 cm hoeher (Parierstange ueber der Schulter statt im Deltamuskel) und 3 cm weiter nach hinten.
                    name = "spine_03"; offset = new Vector3(0.06f, 0.22f, -0.25f);   // Parier hinter den Deltamuskel (höher allein half nicht)
                    rot = Quaternion.LookRotation(new Vector3(-0.42f, -0.88f, -0.2f).normalized, Vector3.back);
                    break;
                case WeaponSheath.BackBow:
                    // Bogen quer ueber dem Ruecken: Wurfarme von rechts unten nach links oben, Griff hinter der linken Schulter
                    name = "spine_03"; offset = new Vector3(-0.1f, 0.02f, -0.19f);
                    rot = Quaternion.LookRotation(new Vector3(0.83f, 0.55f, 0f), new Vector3(-0.55f, 0.83f, 0f));
                    break;
                case WeaponSheath.HipRightOff:
                    // Zweite Waffe (Beidhaendig): gespiegelt zur linken Hueftscheide – die linke Hand greift quer
                    // 21.10. nachts (Haut-Pruefung: Parierstange 1,4 cm im rechten Oberschenkel): genau gespiegelt zur linken Hueftscheide
                    name = "pelvis"; offset = new Vector3(0.22f, -0.02f, 0.12f);
                    rot = Quaternion.AngleAxis(8f, Vector3.forward) * Quaternion.Euler(128f, 0f, 0f);
                    break;
                case WeaponSheath.HipLeftOff:
                    // Zweiter Dolch: gespiegelt zur rechten Hueftscheide
                    name = "pelvis"; offset = new Vector3(-0.22f, 0.03f, 0.02f);   // gespiegelt zu HipRight (21.10. nachts)
                    rot = Quaternion.AngleAxis(-8f, Vector3.forward) * Quaternion.Euler(100f, 0f, 0f);
                    break;
                case WeaponSheath.Arm:
                    // 21.10. abends (Max: „das Schild clippt durch den Arm“): Abstand zur Unterarmachse 7,5 → 9,5 cm. Gemessen: Schild
                    // 3,5 cm dick (Rückseite 1,75 cm hinter der Mitte), Unterarm Tripo-Mann an der Schildseite bis 5,9 cm, Frau 4,3 cm.
                    name = "lowerarm_l"; offset = new Vector3(-0.13f, 0.095f, 0f);
                    rot = Quaternion.identity;
                    break;
                case WeaponSheath.ShieldBack:
                    // 16.10. (Max, Rundumsicht: Schild pumpte beim Laufen am Unterarm vor der Brust mit und schnitt durch den
                    // Koerper): mit gesteckter Waffe haengt der Schild auf dem Ruecken (Classic-Trageweise, MOV-021). Lage wie am
                    // Unterarm gedacht: Armachse (+X des Knochens) zeigt nach unten, Schildflaeche (+Y) nach hinten.
                    // 21.10. abends (Max: Schild auf dem Rücken clippt durch die Oberarme): 52 cm Schild ist breiter als der Spalt zwischen
                    // den haengenden Oberarmen → 10 cm weiter nach hinten (hinter die Rückseite der Ellbogen, Haut-Pruefung) und 2 cm hoeher (Haut-Pruefung im Lab: tiefer
                    // stach der untere Rand in die haengenden Unterarme, 1,7 cm).
                    name = "spine_03"; offset = new Vector3(0f, -0.05f, -0.27f); // Rundumsicht 17:47: Oberkante stand ueber den Schultern
                    rot = Quaternion.LookRotation(Vector3.right, Vector3.back);
                    break;
                default:
                    name = "hand_l"; offset = new Vector3(-0.1f, -0.02f, 0.05f);
                    rot = Quaternion.identity;
                    break;
            }
            localPos = Vector3.zero; localRot = Quaternion.identity; localScale = 1f;
            if (!_bones.TryGetValue(name, out bone) || bone == null) return false;
            float rootScale = _charRoot.lossyScale.x;
            Quaternion rel = _bindRot.TryGetValue(name, out Quaternion r) ? r : Quaternion.identity;
            float boneScale = Mathf.Max(1e-5f, bone.lossyScale.x);
            localRot = Quaternion.Inverse(rel) * rot;
            localPos = Quaternion.Inverse(rel) * (offset * _sizeScale * rootScale) / boneScale;
            localScale = rootScale / boneScale;
            return true;
        }

        // ================================================================== Pruefen / Aufraeumen

        /// <summary>Kurzbericht: "Head=appearance.x/5,Chest=.../12,..." (nur belegte Plaetze).</summary>
        public string Summary()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Slots; i++)
            {
                if (_slots[i] == null) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append((EquipmentSlot)i).Append('=').Append(_slots[i].Def.Id).Append('/').Append(_slots[i].RendererCount);
            }
            return sb.ToString();
        }

        public int RendererCount(EquipmentSlot slot) => _slots[(int)slot]?.RendererCount ?? 0;
        public bool BodyMasked => _body != null && _body.sharedMesh != _bodyMesh;
        // 22.10. (Codex Duo-Frisur): auch eine reine Teilefrisur zählt – wie HasHair/UpdateHides
        public bool HairVisible => (_hair != null && _hair.activeSelf) || (_hairParts != null && _hairParts.Owned.Exists(o => o != null && o.activeSelf));
        public bool HasHair => _hair != null || (_hairParts != null && _hairParts.Owned.Count > 0);
        public int HairPartCount => _hairParts != null ? _hairParts.Owned.Count : 0;
        public int BeardPartCount => _beardParts != null ? _beardParts.Owned.Count : 0;
        public SkinnedMeshRenderer Body => _body;

        /// <summary>Alle Ausruestungsobjekte entfernen (Koerperwechsel).</summary>
        public void Clear()
        {
            for (int i = 0; i < Slots; i++)
            {
                DestroyBuilt(_slots[i]);
                _slots[i] = null;
            }
            if (_hair != null) Zerstoeren(_hair);
            if (_beard != null) Zerstoeren(_beard);
            DestroyBuilt(_hairParts);
            DestroyBuilt(_beardParts);
            _hairParts = _beardParts = null;
            _hair = _beard = null;
            _hairKey = _beardKey = _skinKey = _eyeKey = -1;
            if (_overlay != null) Zerstoeren(_overlay.gameObject);
            _overlay = null;
            _overlaySource = null;
            _overlayKey = "";
            if (_head != null) _head.localScale = _headBase;
            _headScale = Vector3.one;
            if (_eyes != null && _eyeMaterial != null) _eyes.sharedMaterial = _eyeMaterial;
            if (_mpfbEyes != null && _mpfbEyeMaterial != null) { _mpfbEyes.sharedMaterial = _mpfbEyeMaterial; _mpfbEyeKey = -1; }
            if (_body != null)
            {
                _body.sharedMesh = _bodyMesh;
                _body.sharedMaterials = _bodyMaterials;
            }
            foreach (Stufe st in _stufen) { st.R.sharedMesh = st.Mesh; st.R.sharedMaterials = st.Mats; }
            if (_lodGroup != null) UpdateLods();   // 21.10.: Kleidstufen aus der LODGroup nehmen
        }

        /// <summary>Stufen sortieren (meiste Punkte zuerst), Verdeckung je Stufe vorbereiten, LODGroup am Figurenmodell.</summary>
        private void SetupStufen()
        {
            if (_stufen.Count == 0) return;
            _stufen.Sort((a, b) => b.Mesh.vertexCount.CompareTo(a.Mesh.vertexCount));
            foreach (Stufe st in _stufen) st.Shells = BodyShells.For(st.R, _charRoot);
            int n = Mathf.Min(_stufen.Count + 1, StufenHoehe.Length);
            var lods = new LOD[n];
            lods[0] = new LOD(StufenHoehe[0], new Renderer[] { _body });
            for (int i = 1; i < n; i++) lods[i] = new LOD(StufenHoehe[i], new Renderer[] { _stufen[i - 1].R });
            for (int i = n - 1; i < _stufen.Count; i++) _stufen[i].R.enabled = false;   // mehr Stufen als Schwellen: übrige aus
            LODGroup g = _body.transform.parent != null ? _body.transform.parent.GetComponent<LODGroup>() : null;
            if (g == null) g = (_body.transform.parent != null ? _body.transform.parent.gameObject : gameObject).AddComponent<LODGroup>();
            g.SetLODs(lods);
            g.RecalculateBounds();
            // 21.10.: merken – Kleidstufen werden in UpdateLods dazugenommen
            _lodGroup = g;
            _lodKoerper = new Renderer[n][];
            for (int i = 0; i < n; i++) _lodKoerper[i] = lods[i].renderers;
        }

        private static Material[] StufenMats(Material[] mats, Stufe st)
        {
            var r = (Material[])st.Mats.Clone();
            for (int i = 0; i < r.Length && i < mats.Length; i++) r[i] = mats[i];
            return r;
        }

        private static void DestroyBuilt(Built b)
        {
            if (b == null) return;
            foreach (GameObject go in b.Owned) if (go != null) Zerstoeren(go);
            b.Owned.Clear();
            b.Kleid.Clear();
            if (b.Root != null) Zerstoeren(b.Root);
        }

        /// <summary>Anzahl lebender Ausruestungsobjekte (Test: keine Doppelten nach vielen Wechseln).</summary>
        public int LiveObjectCount
        {
            get
            {
                int n = 0;
                foreach (Built b in _slots)
                {
                    if (b == null) continue;
                    if (b.Root != null) n++;
                    foreach (GameObject go in b.Owned) if (go != null) n++;
                }
                return n;
            }
        }

        private void OnDestroy()
        {
            // 21.10.: im Edit-Modus (EditMode-Tests) raeumt Unity die Kinder mit dem Modell selbst ab – alle gebauten Teile haengen
            // unter diesem Modell; Destroy ist im Edit-Modus verboten, DestroyImmediate auf Kinder eines Objekts im Abbau vermeiden.
            if (Ready && Application.isPlaying) Clear();
        }
    }
}
