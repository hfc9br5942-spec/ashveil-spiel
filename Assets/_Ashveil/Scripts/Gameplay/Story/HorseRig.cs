using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>Haltung eines Pferdes (Greybox): Kopf und Hals, Beine aus dem Tempo.</summary>
    public enum HorsePose : byte
    {
        Stand,
        Graze,
        /// <summary>Kopf hoch, Ohren nach vorn – etwas bemerkt.</summary>
        Alert,
        /// <summary>Kopf tief nach vorn gestreckt – schnuppern.</summary>
        Sniff,
        /// <summary>Unruhig: Kopf schlaegt, Tritt auf der Stelle.</summary>
        Panic,
        /// <summary>Haengt fest (Fohlen im Halfter): zerrt rueckwaerts, Kopf gesenkt.</summary>
        Stuck,
        /// <summary>Entspannt: Kopf gesenkt, ein Hinterbein locker.</summary>
        Relaxed
    }

    /// <summary>Aussehen eines Pferdes (aus der Story-Identitaet oder fuer Herdentiere frei).</summary>
    public struct HorseLook
    {
        public Color Coat, Mane;
        public HorseBlazePattern Blaze;
        public HorseLegMarkings Legs;
        public HorseScarPattern Scar;
        /// <summary>Widerristhoehe (m). Fohlen ≈ 1,0.</summary>
        public float Withers;
        public bool Foal;
        public bool Saddle;
        /// <summary>Alter Militaersattel ohne Schabracke (Weidegang-Ende) statt der weinroten Aschewacht-Decke.</summary>
        public bool OldHarness;
        public int Seed;

        public static HorseLook FromIdentity(StoryHorseIdentity id, bool foal, bool saddle)
        {
            var l = new HorseLook
            {
                Coat = HorseStoryCatalog.CoatColor(id.coat),
                Mane = HorseStoryCatalog.ManeColor(id.coat),
                Blaze = id.blaze,
                Legs = id.legs,
                Scar = foal && id.scar == HorseScarPattern.RightShoulderHealed ? HorseScarPattern.RightShoulderFresh : id.scar,
                Withers = foal ? 1.02f : id.adultWithers,
                Foal = foal,
                Saddle = saddle,
                Seed = id.appearanceSeed
            };
            return l;
        }

        /// <summary>Herdentier: Fell aus einem Preset je Seed (feste Reihe – die Herde sieht immer gleich aus).</summary>
        public static HorseLook Herd(int seed)
        {
            HorseCoatPreset[] coats = { HorseCoatPreset.Bay, HorseCoatPreset.Chestnut, HorseCoatPreset.DarkBay, HorseCoatPreset.Dun, HorseCoatPreset.Grey, HorseCoatPreset.Bay, HorseCoatPreset.Chestnut };
            HorseCoatPreset c = coats[Mathf.Abs(seed) % coats.Length];
            float jitter = ((seed * 7919) % 100) / 100f;
            return new HorseLook
            {
                Coat = HorseStoryCatalog.CoatColor(c) * (0.92f + jitter * 0.14f),
                Mane = HorseStoryCatalog.ManeColor(c),
                Blaze = (HorseBlazePattern)(Mathf.Abs(seed * 31) % 3 == 0 ? 1 : Mathf.Abs(seed * 13) % 4 == 0 ? 3 : 0),
                Legs = (HorseLegMarkings)(Mathf.Abs(seed * 17) % 4),
                Withers = 1.56f + jitter * 0.12f,
                Seed = seed
            };
        }
    }

    /// <summary>
    /// Pferd aus Grundkoerpern (Greybox bis zum Blender-Modell) mit Gangarten (Schritt, Trab, Galopp) aus dem Tempo und
    /// Haltungen. Genutzt fuer Herde, Fohlen, das Story-Pferd auf der Weide und das Klassenreittier. Keine Kollision
    /// (Tiere stehen dem Spieler nicht im Weg), keine Aura, keine Panzerung.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HorseRig : MonoBehaviour
    {
        private Transform _root, _body, _neck, _head, _tail;
        private readonly Transform[] _legs = new Transform[4];
        private readonly Transform[] _shins = new Transform[4];
        private float _phase, _speed, _poseBlendHead, _poseBlendNeck, _headYaw;
        private HorseLook _look;
        private float _scale = 1f;

        public HorsePose Pose { get; set; }
        /// <summary>Tempo in m/s (Gangart). Wer das Pferd bewegt, setzt es jeden Frame.</summary>
        public float Speed { get => _speed; set => _speed = Mathf.Max(0f, value); }
        /// <summary>Kopf seitlich drehen (Grad, z. B. zum Spieler schauen).</summary>
        public float HeadYaw { get; set; }
        public HorseLook Look => _look;
        public Transform Root => _root;
        /// <summary>0 Stand, 1 Schritt, 2 Trab, 3 Galopp.</summary>
        public int Gait => _speed < 0.3f ? 0 : _speed < 3.2f ? 1 : _speed < 8f ? 2 : 3;
        public float WithersHeight => _look.Withers;

        public static HorseRig Create(Transform parent, string name, HorseLook look)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<HorseRig>();
            rig.Build(look);
            return rig;
        }

        public void Build(HorseLook look)
        {
            _look = look;
            if (_root != null) Destroy(_root.gameObject);
            // Basismodell hat ~1,70 m Widerrist
            _scale = Mathf.Max(0.3f, look.Withers / 1.7f);
            _root = new GameObject("Pferd (Greybox)").transform;
            _root.SetParent(transform, false);
            _root.localScale = Vector3.one * _scale;
            float legLen = look.Foal ? 1.28f : 1f;   // Fohlen: lange Beine, kurzer Rumpf
            float bodyLen = look.Foal ? 0.82f : 1f;
            float legShift = (legLen - 1f) * 1.12f;

            Color coat = look.Coat, mane = look.Mane;
            Color hoof = new Color(0.07f, 0.06f, 0.05f), white = new Color(0.86f, 0.84f, 0.8f);
            _body = Part(PrimitiveType.Capsule, "Rumpf", _root, new Vector3(0f, 1.3f + legShift, 0f), new Vector3(0.78f, 1.05f * bodyLen, 0.9f), new Vector3(90f, 0f, 0f), coat);
            Part(PrimitiveType.Sphere, "Brust", _body, new Vector3(0f, 0.55f, -0.05f), new Vector3(0.95f, 0.45f, 0.95f), Vector3.zero, coat);
            Part(PrimitiveType.Sphere, "Kruppe", _body, new Vector3(0f, -0.62f, -0.08f), new Vector3(1.05f, 0.45f, 1.02f), Vector3.zero, coat);
            // Narbe/Verletzung an der rechten Schulter
            if (look.Scar != HorseScarPattern.None)
            {
                Color sc = look.Scar == HorseScarPattern.RightShoulderFresh ? new Color(0.42f, 0.12f, 0.1f) : new Color(0.55f, 0.48f, 0.42f);
                Part(PrimitiveType.Cube, "Narbe Schulter rechts", _root, new Vector3(0.4f, 1.36f + legShift, 0.52f * bodyLen), new Vector3(0.03f, 0.08f, 0.26f), new Vector3(0f, 0f, 18f), sc);
            }
            if (look.Saddle)
            {
                Color leather = new Color(0.33f, 0.2f, 0.11f), cloth = new Color(0.34f, 0.08f, 0.08f), dark = new Color(0.08f, 0.06f, 0.05f);
                if (!look.OldHarness) Part(PrimitiveType.Cube, "Schabracke", _root, new Vector3(0f, 1.62f + legShift, -0.05f), new Vector3(0.84f, 0.1f, 0.9f), Vector3.zero, cloth);
                else Part(PrimitiveType.Cube, "Alte Satteldecke", _root, new Vector3(0f, 1.62f + legShift, -0.05f), new Vector3(0.8f, 0.06f, 0.74f), Vector3.zero, new Color(0.3f, 0.28f, 0.22f));
                Part(PrimitiveType.Cube, "Sattel", _root, new Vector3(0f, 1.74f + legShift, -0.02f), new Vector3(0.5f, 0.14f, 0.62f), Vector3.zero, leather);
                Part(PrimitiveType.Cube, "Sattelknauf", _root, new Vector3(0f, 1.86f + legShift, 0.26f), new Vector3(0.18f, 0.16f, 0.1f), Vector3.zero, leather);
                Part(PrimitiveType.Cube, "Steigbuegel L", _root, new Vector3(-0.36f, 1.18f + legShift, 0.02f), new Vector3(0.06f, 0.5f, 0.06f), Vector3.zero, dark);
                Part(PrimitiveType.Cube, "Steigbuegel R", _root, new Vector3(0.36f, 1.18f + legShift, 0.02f), new Vector3(0.06f, 0.5f, 0.06f), Vector3.zero, dark);
            }

            _neck = new GameObject("Hals").transform;
            _neck.SetParent(_root, false);
            _neck.localPosition = new Vector3(0f, 1.62f + legShift, 0.78f * bodyLen);
            _neck.localRotation = Quaternion.Euler(-38f, 0f, 0f);
            Part(PrimitiveType.Capsule, "Halsmuskel", _neck, new Vector3(0f, 0f, 0.38f), new Vector3(0.42f, 0.5f, 0.5f), new Vector3(90f, 0f, 0f), coat);
            Part(PrimitiveType.Cube, "Maehne", _neck, new Vector3(0f, 0.2f, 0.3f), new Vector3(0.08f, look.Foal ? 0.1f : 0.18f, 0.8f), Vector3.zero, mane);
            _head = new GameObject("Kopf").transform;
            _head.SetParent(_neck, false);
            _head.localPosition = new Vector3(0f, 0f, 0.78f);
            _head.localRotation = Quaternion.Euler(95f, 0f, 0f);
            Part(PrimitiveType.Capsule, "Schaedel", _head, new Vector3(0f, 0f, 0.24f), new Vector3(0.3f, 0.36f, 0.34f), new Vector3(90f, 0f, 0f), coat);
            Part(PrimitiveType.Cube, "Ohr L", _head, new Vector3(-0.09f, 0.16f, -0.02f), new Vector3(0.06f, 0.16f, 0.05f), new Vector3(-20f, 0f, 0f), coat);
            Part(PrimitiveType.Cube, "Ohr R", _head, new Vector3(0.09f, 0.16f, -0.02f), new Vector3(0.06f, 0.16f, 0.05f), new Vector3(-20f, 0f, 0f), coat);
            Part(PrimitiveType.Sphere, "Auge L", _head, new Vector3(-0.15f, 0.07f, 0.1f), new Vector3(0.05f, 0.05f, 0.05f), Vector3.zero, new Color(0.03f, 0.025f, 0.02f));
            Part(PrimitiveType.Sphere, "Auge R", _head, new Vector3(0.15f, 0.07f, 0.1f), new Vector3(0.05f, 0.05f, 0.05f), Vector3.zero, new Color(0.03f, 0.025f, 0.02f));
            // Blesse: vorn auf dem Nasenruecken (lokal +y = Stirnseite)
            switch (look.Blaze)
            {
                case HorseBlazePattern.Star:
                    Part(PrimitiveType.Cube, "Stern", _head, new Vector3(0f, 0.16f, 0.12f), new Vector3(0.08f, 0.02f, 0.08f), Vector3.zero, white);
                    break;
                case HorseBlazePattern.NarrowBroken:
                    Part(PrimitiveType.Cube, "Blesse oben", _head, new Vector3(0f, 0.165f, 0.1f), new Vector3(0.05f, 0.02f, 0.14f), Vector3.zero, white);
                    Part(PrimitiveType.Cube, "Blesse unten", _head, new Vector3(0.008f, 0.15f, 0.36f), new Vector3(0.04f, 0.02f, 0.12f), new Vector3(0f, 6f, 0f), white);
                    break;
                case HorseBlazePattern.Wide:
                    Part(PrimitiveType.Cube, "Blesse", _head, new Vector3(0f, 0.16f, 0.24f), new Vector3(0.13f, 0.02f, 0.46f), Vector3.zero, white);
                    break;
            }
            if (look.Saddle) Part(PrimitiveType.Cube, "Zaum", _head, new Vector3(0f, 0.02f, 0.3f), new Vector3(0.33f, 0.3f, 0.04f), Vector3.zero, new Color(0.25f, 0.15f, 0.08f));

            _tail = new GameObject("Schweif").transform;
            _tail.SetParent(_root, false);
            _tail.localPosition = new Vector3(0f, 1.45f + legShift, -0.98f * bodyLen);
            Part(PrimitiveType.Cube, "Haar", _tail, new Vector3(0f, -0.35f, -0.08f), new Vector3(0.12f, look.Foal ? 0.4f : 0.75f, 0.1f), new Vector3(-15f, 0f, 0f), mane);

            Vector3[] hips =
            {
                new Vector3(-0.24f, 1.12f + legShift, 0.58f * bodyLen), new Vector3(0.24f, 1.12f + legShift, 0.58f * bodyLen),
                new Vector3(-0.25f, 1.12f + legShift, -0.62f * bodyLen), new Vector3(0.25f, 1.12f + legShift, -0.62f * bodyLen)
            };
            string[] names = { "Vorderbein L", "Vorderbein R", "Hinterbein L", "Hinterbein R" };
            for (int i = 0; i < 4; i++)
            {
                Transform leg = new GameObject(names[i]).transform;
                leg.SetParent(_root, false);
                leg.localPosition = hips[i];
                leg.localScale = new Vector3(1f, legLen, 1f);
                Part(PrimitiveType.Capsule, "Oberschenkel", leg, new Vector3(0f, -0.28f, 0f), new Vector3(0.2f, 0.3f, 0.24f), Vector3.zero, coat);
                Transform shin = new GameObject("Unterschenkel").transform;
                shin.SetParent(leg, false);
                shin.localPosition = new Vector3(0f, -0.56f, 0f);
                bool sock = look.Legs == HorseLegMarkings.FourSocks || (i >= 2 && look.Legs == HorseLegMarkings.BothHindSocks) || (i == 2 && look.Legs == HorseLegMarkings.LeftHindSock);
                Part(PrimitiveType.Capsule, "Roehre", shin, new Vector3(0f, -0.24f, 0f), new Vector3(0.13f, 0.27f, 0.13f), Vector3.zero, sock ? white : coat);
                Part(PrimitiveType.Cylinder, "Huf", shin, new Vector3(0f, -0.52f, 0.02f), new Vector3(0.17f, 0.05f, 0.19f), Vector3.zero, hoof);
                _legs[i] = leg;
                _shins[i] = shin;
            }
            _phase = (look.Seed % 97) / 97f;
        }

        private static Material _material;

        private static Material SharedMaterial
        {
            get
            {
                if (_material != null) return _material;
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) return null;
                _material = new Material(sh) { name = "Pferd Greybox" };
                _material.SetFloat("_Smoothness", 0.22f);
                return _material;
            }
        }

        private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (SharedMaterial != null) r.sharedMaterial = SharedMaterial;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        public void SetVisible(bool on)
        {
            if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        /// <summary>Bewegung und Haltung fortschreiben (vom Besitzer der Figur, jeden Frame).</summary>
        public void Animate(float dt)
        {
            if (_root == null) return;
            float spd = _speed / _scale; // kleine Tiere machen mehr Schritte
            int gait = spd < 0.3f ? 0 : spd < 3.2f ? 1 : spd < 8f ? 2 : 3;
            float cadence = gait == 0 ? 0f : gait == 1 ? 1.1f : gait == 2 ? 1.7f : 2.3f;
            bool panic = Pose == HorsePose.Panic || Pose == HorsePose.Stuck;
            if (gait == 0 && panic) cadence = Pose == HorsePose.Stuck ? 1.4f : 0.9f;
            _phase = Mathf.Repeat(_phase + cadence * dt, 1f);
            float amp = gait == 0 ? (panic ? 10f : 0f) : gait == 1 ? 18f : gait == 2 ? 28f : 40f;
            float[] offsets = gait == 3 ? new[] { 0f, 0.1f, 0.5f, 0.6f } : new[] { 0f, 0.5f, 0.5f, 0f };
            for (int i = 0; i < 4; i++)
            {
                float ph = (_phase + offsets[i]) * Mathf.PI * 2f;
                float swing = Mathf.Sin(ph) * amp;
                float knee = Mathf.Max(0f, Mathf.Cos(ph)) * amp * (i < 2 ? 1.2f : -0.9f);
                if (Pose == HorsePose.Relaxed && gait == 0 && i == 3) knee = -14f; // Hinterbein locker
                if (Pose == HorsePose.Stuck && gait == 0 && i < 2) swing -= 12f;    // stemmt sich nach hinten
                _legs[i].localRotation = Quaternion.Euler(swing, 0f, 0f);
                _shins[i].localRotation = Quaternion.Euler(knee, 0f, 0f);
            }
            float t = Time.time + _look.Seed * 0.37f;
            float bob = gait == 0 ? Mathf.Sin(t * 1.6f) * 0.012f : Mathf.Abs(Mathf.Sin(_phase * Mathf.PI * 2f)) * (gait == 3 ? 0.09f : 0.045f);
            float pitch = gait == 3 ? Mathf.Sin(_phase * Mathf.PI * 2f) * 4f : 0f;
            if (Pose == HorsePose.Stuck) pitch -= 4f;
            _root.localPosition = new Vector3(0f, bob * _scale, 0f);
            _root.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // Hals/Kopf je Haltung (weich ueberblendet)
            float neckTarget, headTarget;
            switch (gait > 0 ? (panic ? HorsePose.Alert : HorsePose.Stand) : Pose)
            {
                case HorsePose.Graze: neckTarget = 38f; headTarget = 118f; break;
                case HorsePose.Alert: neckTarget = -58f; headTarget = 88f; break;
                case HorsePose.Sniff: neckTarget = 8f; headTarget = 100f; break;
                case HorsePose.Panic: neckTarget = -60f + Mathf.Sin(t * 7f) * 12f; headTarget = 80f + Mathf.Sin(t * 9f) * 14f; break;
                case HorsePose.Stuck: neckTarget = -5f + Mathf.Sin(t * 5f) * 10f; headTarget = 105f; break;
                case HorsePose.Relaxed: neckTarget = -18f; headTarget = 104f; break;
                default: neckTarget = -38f + Mathf.Sin(t * 0.5f) * 4f; headTarget = 95f; break;
            }
            if (gait > 0) neckTarget += Mathf.Sin(_phase * Mathf.PI * 2f) * 6f;
            float k = 1f - Mathf.Exp(-4f * dt);
            _poseBlendNeck = Mathf.Lerp(_poseBlendNeck == 0f ? neckTarget : _poseBlendNeck, neckTarget, k);
            _poseBlendHead = Mathf.Lerp(_poseBlendHead == 0f ? headTarget : _poseBlendHead, headTarget, k);
            _headYaw = Mathf.Lerp(_headYaw, Mathf.Clamp(HeadYaw, -55f, 55f), k);
            _neck.localRotation = Quaternion.Euler(0f, _headYaw * 0.6f, 0f) * Quaternion.Euler(_poseBlendNeck, 0f, 0f);
            _head.localRotation = Quaternion.Euler(0f, _headYaw * 0.4f, 0f) * Quaternion.Euler(_poseBlendHead, 0f, 0f);
            float tailSpeed = gait == 0 ? (panic ? 6f : 1.3f) : 5f;
            _tail.localRotation = Quaternion.Euler(Mathf.Sin(t * tailSpeed) * (panic ? 22f : 10f), Mathf.Sin(t * 0.9f) * 12f, 0f);
        }

        private void LateUpdate()
        {
            if (AutoAnimate) Animate(Time.deltaTime);
        }

        /// <summary>true = animiert sich selbst in LateUpdate (sonst ruft der Besitzer <see cref="Animate"/>).</summary>
        public bool AutoAnimate { get; set; } = true;
    }
}
