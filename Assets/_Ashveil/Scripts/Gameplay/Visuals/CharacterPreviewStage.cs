using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// Eigene kleine Buehne fuer die 3D-Charaktervorschau im Charakterfenster: eigenes Modell, eigene Kamera,
    /// eigene Ebene – vollstaendig getrennt von der Spielfigur. Eine Vorschau (auch "Anprobe") veraendert nie die
    /// echte Ausruestung oder Werte. Die Kamera rendert nur, solange das Fenster offen ist.
    /// </summary>
    public sealed class CharacterPreviewStage : MonoBehaviour
    {
        public const string LayerName = "Preview";
        private static CharacterPreviewStage _instance;

        private Camera _cam;
        private Transform _pivot;
        private GameObject _model;
        private AppearanceRig _rig;
        private int _body = -1;
        private float _yaw;
        private int _layer;

        public RenderTexture Texture { get; private set; }
        public AppearanceRig Rig => _rig;
        public ItemStack[] LastEquipment { get; private set; } = new ItemStack[Ashveil.Gameplay.Items.InventoryModel.EquipCount];

        public static CharacterPreviewStage Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("Character Preview Stage");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CharacterPreviewStage>();
            _instance.Setup();
            return _instance;
        }

        public static CharacterPreviewStage Existing => _instance;

        private void Setup()
        {
            _layer = LayerMask.NameToLayer(LayerName);
            if (_layer < 0) _layer = 31;
            transform.position = new Vector3(0f, -1000f, 0f);
            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(transform, false);

            Texture = new RenderTexture(824, 600, 24, RenderTextureFormat.ARGB32) { name = "Character Preview", antiAliasing = 4 };
            var camGo = new GameObject("Preview Camera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.fieldOfView = 25f;
            _cam.nearClipPlane = 0.5f;
            _cam.farClipPlane = 20f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 0f);
            _cam.cullingMask = 1 << _layer;
            _cam.targetTexture = Texture;
            _cam.enabled = false;

            var key = new GameObject("Preview Light");
            key.transform.SetParent(transform, false);
            key.transform.localPosition = new Vector3(1.5f, 2.4f, 3.2f);
            var l = key.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 9f;
            l.intensity = 6f;
            l.color = new Color(1f, 0.9f, 0.78f);
            l.cullingMask = 1 << _layer;

            // M7: Aufhelllicht (kuehl, von links) und Kantenlicht (warm, von hinten) – Gesicht gut lesbar
            AddLight("Preview Fill", new Vector3(-2.2f, 1.6f, 2.6f), 3.2f, new Color(0.72f, 0.8f, 1f), 9f);
            AddLight("Preview Rim", new Vector3(0.8f, 2.4f, -1.8f), 5f, new Color(1f, 0.62f, 0.32f), 7f);

            Camera main = Camera.main;
            if (main != null) main.cullingMask &= ~(1 << _layer);
            ApplyZoom();
        }

        private void AddLight(string name, Vector3 pos, float intensity, Color color, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = intensity;
            l.color = color;
            l.cullingMask = 1 << _layer;
        }

        public void Show(bool on)
        {
            if (_cam != null && _cam.enabled != on) _cam.enabled = on;
            Camera main = Camera.main;
            if (on && main != null) main.cullingMask &= ~(1 << _layer);
        }

        /// <summary>Test/Bericht: aktuelle Vorschau als PNG speichern.</summary>
        public bool Capture(string path, int width = 600, int height = 760)
        {
            if (_cam == null) return false;
            // Gedrehte Figur im selben Frame: Skin-Matrizen sonst noch vom letzten Frame (Teile wirken verschoben)
            foreach (SkinnedMeshRenderer smr in _pivot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.forceMatrixRecalculationPerRender = true;
            float fov = _cam.fieldOfView;
            _cam.fieldOfView = 30f;
            bool ok = CaptureUtil.Capture(_cam, path, width, height);
            _cam.fieldOfView = fov;
            _cam.targetTexture = Texture;
            return ok;
        }

        // ---- M7: Zoom aufs Gesicht (Charakter-Editor)
        private static readonly Vector3 FaceCam = new Vector3(0f, 1.62f, 1.6f);
        private static readonly Vector3 FaceLook = new Vector3(0f, -0.02f, -1f);
        private float _zoom, _zoomTarget;

        /// <summary>0 = ganze Figur, 1 = Gesicht. Weich angefahren (sofort = ohne Uebergang).</summary>
        public void SetZoom(float z, bool immediate = false)
        {
            _zoomTarget = Mathf.Clamp01(z);
            if (immediate) { _zoom = _zoomTarget; ApplyZoom(); }
        }

        public float Zoom => _zoomTarget;

        private void ApplyZoom()
        {
            if (_cam == null) return;
            float t = Mathf.SmoothStep(0f, 1f, _zoom);
            float h = _rig != null ? _rig.SizeScale : 1f; // kleinere Figuren: Gesicht etwas tiefer
            Vector3 face = new Vector3(FaceCam.x, 0.25f + (FaceCam.y - 0.25f) * h, FaceCam.z);
            // SizeScale uses the rig's 1.81 m reference body. Centre the complete figure with 20% vertical room.
            float height = 1.81f * h;
            float distance = height * 0.6f / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 body = new Vector3(0f, height * 0.52f, distance);
            _cam.transform.localPosition = Vector3.Lerp(body, face, t);
            _cam.transform.localRotation = Quaternion.LookRotation(Vector3.Slerp(Vector3.back, FaceLook, t));
        }

        private void Update()
        {
            if (Mathf.Approximately(_zoom, _zoomTarget)) return;
            _zoom = Mathf.MoveTowards(_zoom, _zoomTarget, Time.unscaledDeltaTime * 2.5f);
            ApplyZoom();
        }

        public float Yaw => _yaw;

        /// <summary>M8: Emote in der Vorschau (Sichtpruefung/Bilder).</summary>
        public void PlayEmote(Ashveil.Data.EmoteId id)
        {
            if (_model == null) return;
            var poser = _model.GetComponent<Ashveil.Gameplay.Characters.EmotePoser>();
            if (poser == null) poser = _model.AddComponent<Ashveil.Gameplay.Characters.EmotePoser>();
            poser.Play(id);
        }

        public void SetYaw(float degrees)
        {
            _yaw = degrees;
            _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        public void Rotate(float degrees)
        {
            _yaw += degrees;
            _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Zeigt eine Ausruestung + Aussehen (echte oder Anprobe).</summary>
        public void SetState(ItemStack[] equipment, CharacterAppearanceData look)
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            if (cat == null || look == null) return;
            int body = Mathf.Clamp(look.body, 0, Mathf.Max(0, cat.bodies.Count - 1));
            if (body != _body || _model == null)
            {
                if (_model != null) Destroy(_model);
                _model = null;
                _rig = null;
                _body = body;
                GameObject prefab = cat.bodies.Count > body ? cat.bodies[body].model : null;
                if (prefab == null) return;
                _model = Instantiate(prefab, _pivot, false);
                _model.name = "Preview Body";
                foreach (Transform t in _model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _layer;
                Animator anim = _model.GetComponentInChildren<Animator>(true);
                if (anim == null) anim = _model.AddComponent<Animator>();
                anim.applyRootMotion = false;
                if (cat.previewController != null) anim.runtimeAnimatorController = cat.previewController;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _rig = _model.AddComponent<AppearanceRig>();
                _rig.Init(_pivot);
            }
            if (_rig == null) return;
            LastEquipment = equipment != null ? (ItemStack[])equipment.Clone() : new ItemStack[Ashveil.Gameplay.Items.InventoryModel.EquipCount];
            _rig.Apply(EquipmentVisuals.Resolve(LastEquipment, look, cat), look, cat);
            _rig.PlaceWeapons(null, null); // in der Vorschau sind Waffen verstaut sichtbar
            ApplyZoom(); // A body change must update framing even when the zoom target did not change.
        }
    }
}
