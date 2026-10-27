using System.Collections.Generic;
using Ashveil.Core;
using UnityEngine;

namespace Ashveil.Gameplay.Colosseum
{
    /// <summary>
    /// Kolosseum Pass 4: Bereit-Glocke und Pruefpunkt-Truhe je Arena (Greybox). Werden zur Laufzeit auf Server UND Clients
    /// gleich erzeugt (reine Szenen-Deko mit Klick-Kollider, kein Netzwerkobjekt) – der Server entscheidet ueber Klicks
    /// per RPC, die Anzeige kommt ueber Signale (<see cref="ColosseumClient.CueBell"/>, <see cref="ColosseumClient.CueChest"/>).
    /// </summary>
    public static class ArenaProps
    {
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Glocke und Truhe fuer einen Arena-Platz bauen (einmal je Platz).</summary>
        public static void Build(Transform slot, int slotIndex, Vector3 bellPos, Vector3 chestPos, Vector3 faceTo, Material material)
        {
            if (slot == null || slot.Find("Arena-Requisiten") != null) return;
            var root = new GameObject("Arena-Requisiten").transform;
            root.SetParent(slot, true);

            // --- Glocke: zwei Pfosten, Querbalken, Glocke (schwingt), Klick-Kollider um alles
            Transform bell = Group("Bereit-Glocke", root, bellPos, faceTo);
            Box(bell, "Pfosten L", new Vector3(-1.1f, 1.5f, 0f), new Vector3(0.28f, 3f, 0.28f), material, new Color(0.28f, 0.2f, 0.14f));
            Box(bell, "Pfosten R", new Vector3(1.1f, 1.5f, 0f), new Vector3(0.28f, 3f, 0.28f), material, new Color(0.28f, 0.2f, 0.14f));
            Box(bell, "Balken", new Vector3(0f, 3.05f, 0f), new Vector3(2.7f, 0.3f, 0.36f), material, new Color(0.24f, 0.17f, 0.12f));
            Transform pivot = new GameObject("Glocke (Drehpunkt)").transform;
            pivot.SetParent(bell, false);
            pivot.localPosition = new Vector3(0f, 2.9f, 0f);
            Prim(PrimitiveType.Cylinder, pivot, "Glockenkoerper", new Vector3(0f, -0.55f, 0f), new Vector3(0.95f, 0.45f, 0.95f), material, new Color(0.55f, 0.4f, 0.18f));
            Prim(PrimitiveType.Sphere, pivot, "Glockenkrone", new Vector3(0f, -0.12f, 0f), new Vector3(0.7f, 0.35f, 0.7f), material, new Color(0.5f, 0.36f, 0.16f));
            Prim(PrimitiveType.Sphere, pivot, "Kloeppel", new Vector3(0f, -1.05f, 0f), new Vector3(0.2f, 0.2f, 0.2f), material, new Color(0.2f, 0.2f, 0.2f));
            var bc = bell.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 1.6f, 0f);
            bc.size = new Vector3(2.8f, 3.3f, 1.2f);
            var ab = bell.gameObject.AddComponent<ArenaBell>();
            ab.Setup(slotIndex, pivot);

            // --- Truhe: Kiste mit Beschlaegen, Leuchtband (nur wenn Beute wartet)
            Transform chest = Group("Truhe des Ringmeisters", root, chestPos, faceTo);
            Box(chest, "Kiste", new Vector3(0f, 0.4f, 0f), new Vector3(1.5f, 0.8f, 0.9f), material, new Color(0.32f, 0.2f, 0.12f));
            Box(chest, "Deckel", new Vector3(0f, 0.9f, 0f), new Vector3(1.56f, 0.24f, 0.96f), material, new Color(0.28f, 0.17f, 0.1f));
            Box(chest, "Beschlag L", new Vector3(-0.6f, 0.52f, 0f), new Vector3(0.1f, 1.08f, 0.98f), material, new Color(0.45f, 0.4f, 0.32f));
            Box(chest, "Beschlag R", new Vector3(0.6f, 0.52f, 0f), new Vector3(0.1f, 1.08f, 0.98f), material, new Color(0.45f, 0.4f, 0.32f));
            Renderer glow = Box(chest, "Leuchtband", new Vector3(0f, 0.78f, 0.47f), new Vector3(1.2f, 0.08f, 0.04f), material, new Color(0.3f, 0.26f, 0.2f));
            var cc = chest.gameObject.AddComponent<BoxCollider>();
            cc.center = new Vector3(0f, 0.55f, 0f);
            cc.size = new Vector3(1.7f, 1.2f, 1.1f);
            var ac = chest.gameObject.AddComponent<ArenaChest>();
            ac.Setup(slotIndex, glow);
        }

        internal static void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(Block);
            Block.SetColor(BaseColor, c);
            Block.SetColor(ColorId, c);
            r.SetPropertyBlock(Block);
        }

        private static Transform Group(string name, Transform parent, Vector3 world, Vector3 faceTo)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, true);
            t.position = world;
            Vector3 d = faceTo - world;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            return t;
        }

        private static Renderer Box(Transform parent, string name, Vector3 local, Vector3 size, Material m, Color c) =>
            Prim(PrimitiveType.Cube, parent, name, local, size, m, c);

        private static Renderer Prim(PrimitiveType type, Transform parent, string name, Vector3 local, Vector3 size, Material m, Color c)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>()); // Klicks treffen den einen Kollider an der Gruppe
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (m != null) r.sharedMaterial = m;
            Tint(r, c);
            return r;
        }
    }

    /// <summary>Bereit-Glocke einer Arena (Max 28.09.: einer laeutet, 10 s Countdown).</summary>
    public sealed class ArenaBell : MonoBehaviour
    {
        private static readonly List<ArenaBell> Registered = new List<ArenaBell>();
        public static IReadOnlyList<ArenaBell> All => Registered;

        public int SlotIndex { get; private set; }
        private Transform _pivot;
        private float _swing;

        internal void Setup(int slot, Transform pivot)
        {
            SlotIndex = slot;
            _pivot = pivot;
        }

        private void OnEnable()
        {
            Registered.Add(this);
            EncounterCues.Received += OnCue;
        }

        private void OnDisable()
        {
            Registered.Remove(this);
            EncounterCues.Received -= OnCue;
        }

        private void OnCue(Vector3 where, string cue, float value)
        {
            if (cue != ColosseumClient.CueBell || (where - transform.position).sqrMagnitude > 90f * 90f) return;
            _swing = 2.5f;
        }

        private void Update()
        {
            if (_pivot == null) return;
            if (_swing > 0f) _swing = Mathf.Max(0f, _swing - Time.deltaTime);
            float a = _swing > 0f ? Mathf.Sin(_swing * 9f) * 28f * (_swing / 2.5f) : 0f;
            _pivot.localRotation = Quaternion.Euler(a, 0f, 0f);
        }
    }

    /// <summary>Pruefpunkt-Truhe einer Arena: persoenliche Belohnung; leuchtet, solange fuer mich etwas darin liegt.</summary>
    public sealed class ArenaChest : MonoBehaviour
    {
        private static readonly List<ArenaChest> Registered = new List<ArenaChest>();
        public static IReadOnlyList<ArenaChest> All => Registered;

        public int SlotIndex { get; private set; }
        public bool Glowing { get; private set; }
        private Renderer _glow;

        internal void Setup(int slot, Renderer glow)
        {
            SlotIndex = slot;
            _glow = glow;
        }

        private void OnEnable()
        {
            Registered.Add(this);
            EncounterCues.Received += OnCue;
        }

        private void OnDisable()
        {
            Registered.Remove(this);
            EncounterCues.Received -= OnCue;
        }

        private void OnCue(Vector3 where, string cue, float value)
        {
            if ((where - transform.position).sqrMagnitude > 90f * 90f) return;
            if (cue == ColosseumClient.CueChest) SetGlow(value > 0.5f);
            else if (cue == EncounterCues.Reset || (cue == ColosseumClient.CueEnd && value < 0.5f)) SetGlow(false);
        }

        private void SetGlow(bool on)
        {
            Glowing = on;
            ArenaProps.Tint(_glow, on ? new Color(1f, 0.78f, 0.3f) : new Color(0.3f, 0.26f, 0.2f));
        }

        private void Update()
        {
            if (!Glowing || _glow == null) return;
            float k = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
            ArenaProps.Tint(_glow, new Color(1f * k, 0.78f * k, 0.3f * k));
        }
    }
}
