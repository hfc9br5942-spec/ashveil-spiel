using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Motion.Procedural;
using Ashveil.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Networking
{
    /// <summary>
    /// CORE-005 / NET-012 (02.10.2026): Prozesse OHNE Grafik (dedizierter Server, Lasttest-Bots mit -nographics) sehen nichts –
    /// Animation, Koerperhaltung und flackernde Lichter sind dort reine Rechenzeit. Lasttest mit 50 spielenden Bots:
    /// die Animatoren (inkl. der davon abhaengigen Haltungs-Skripte) kosteten am Server ≈ 21 ms von 31 ms je Bild.
    ///
    /// Greift NUR, wenn das Grafikgeraet "Null" ist (Start mit -nographics). Ein Spieler, der selbst hostet, behaelt alles.
    /// Spiel-Logik haengt nicht an der Animation (geprueft 02.10.: keine Animations-Ereignisse, keine Zustandsautomaten-
    /// Skripte, Treffer/Angriffe laufen ueber WeaponUser-Zeiten und Positionen, nicht ueber Knochen).
    /// Ausschluss-Messung 02.10. (50 Bots, ms je Bild, Rauschen ±3): Animator 21,6 · CharacterMotionPoser 5,8 · AutoInertializer 4,8 ·
    /// WeaponHandPoser 4,0 · EmotePoser 3,1 · WeaponFx 2,5 · LightFlicker 2,2 · StancePoser 1,8 – alle rein darstellend.
    /// Neue Figuren (Spieler, Gegner, Wiederkehr) werden alle 2 s nachgezogen.
    /// </summary>
    public sealed class ServerVisualStripper : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static int Stripped { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Active = false;
            Stripped = 0;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) return;
            // 11.10.2026: Protokollzeilen ohne Stapel (jede Zeile trug fuenf Zeilen Aufrufkette – Server-Log 24 MB in 5 min);
            // Warnungen, Fehler und Ausnahmen behalten ihren Stapel
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            if (ProfileSelection.HasArg("-ashveil-keep-visuals")) return; // Vergleichsmessung "mit Darstellung"
            var go = new GameObject("ServerVisualStripper");
            DontDestroyOnLoad(go);
            go.AddComponent<ServerVisualStripper>();
            Active = true;
            Debug.Log("[Ashveil] Ohne Grafik gestartet: Animation, Koerperhaltung und Lichtflackern werden abgeschaltet (Server-Profil).");
        }

        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            Strip<Animator>();
            Strip<CharacterMotionPoser>();
            Strip<WeaponHandPoser>();
            Strip<StancePoser>();
            Strip<LightFlicker>();
            Strip<AutoInertializer>();
            Strip<EmotePoser>();
            Strip<Ashveil.Gameplay.Weapons.WeaponFx>();
            // 11.10.2026 Ausschluss-Messung (50 Bots, Kareth mit Aktivierungsraster): Gegner-Darstellung und Haut-Renderer kosten am Server
            Strip<Ashveil.Gameplay.Enemies.EnemyVisual>();
            Strip<Ashveil.Gameplay.Combat.HitReaction>();
            // 11.10.2026 Lasttest r2.16: Lichter („Update Lights“) und Partikel („ParticleSystem.EndUpdateAll“) rechneten am Server mit
            Strip<Light>();
            foreach (SkinnedMeshRenderer r in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude))
                if (r != null && r.enabled) { r.enabled = false; Stripped++; }
            foreach (ParticleSystem ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude))
                StopParticles(ps);
            if (!_sceneRenderersOff && Time.unscaledTime > 5f)
            {
                _sceneRenderersOff = true; // einmal: alle Renderer der geladenen Welt (Begrenzungsvolumen kosten sonst je Bild)
                foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                    if (r != null && r.enabled) { r.enabled = false; Stripped++; }
                // 11.10.2026 (Max: „Bots leichter machen“): Netze, die nur der Darstellung dienen (254 von 287 MB je Prozess), loslassen.
                // Nur Weltgeometrie (nichts unter einem Netzobjekt – Waffen und Figuren lesen ihre Netz-Grenzen), nichts, was ein
                // MeshCollider braucht. Danach gibt Resources.UnloadUnusedAssets den Speicher frei.
                StartCoroutine(NetzeFreigeben());
            }
        }

        private bool _sceneRenderersOff;
        private bool _memoryReported;

        private System.Collections.IEnumerator NetzeFreigeben()
        {
            var gebraucht = new System.Collections.Generic.HashSet<Mesh>();
            foreach (MeshCollider mc in FindObjectsByType<MeshCollider>(FindObjectsInactive.Include))
                if (mc != null && mc.sharedMesh != null) gebraucht.Add(mc.sharedMesh);
            int frei = 0;
            foreach (MeshFilter mf in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                if (mf == null || mf.sharedMesh == null || gebraucht.Contains(mf.sharedMesh)) continue;
                if (mf.GetComponentInParent<FishNet.Object.NetworkObject>(true) != null) continue;
                mf.sharedMesh = null;
                frei++;
            }
            long vorher = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20;
            yield return Resources.UnloadUnusedAssets();
            Debug.Log($"[Ashveil] Ohne Grafik: {frei} Darstellungs-Netze losgelassen, belegt {vorher} → {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20} MB.");
        }

        /// <summary>
        /// 11.10.2026 (Max: „Bots leichter machen“): Einmal nach 45 s aufschreiben, was der Prozess ohne Grafik im Speicher hält –
        /// je Asset-Art die Summe und die größten Stücke. Grundlage, um im Bot-Profil freizugeben, was ein Bot nicht braucht.
        /// </summary>
        private void LateUpdate()
        {
            if (_memoryReported || Time.unscaledTime < 45f) return;
            _memoryReported = true;
            try { Debug.Log(SpeicherBericht()); }
            catch (System.Exception e) { Debug.Log("[Ashveil] Speicherbericht fehlgeschlagen: " + e.GetType().Name + " " + e.Message); }
        }

        public static string SpeicherBericht()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[Ashveil][Speicher] Prozess ohne Grafik: reserviert ")
              .Append(UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() >> 20).Append(" MB, belegt ")
              .Append(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20).Append(" MB, Mono-Heap ")
              .Append(UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20).Append(" MB, Grafiktreiber ")
              .Append(UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20).Append(" MB");
            // Unity-Speicherzaehler (was belegt die 370 MB „belegt“ und 820 MB „reserviert“?)
            foreach (string z in new[] { "System Used Memory", "Total Used Memory", "Total Reserved Memory", "GC Used Memory", "GC Reserved Memory", "Gfx Used Memory",
                                         "Audio Used Memory", "Profiler Used Memory", "Mesh Memory", "Texture Memory", "Physics Used Memory", "Managed Heap Memory",
                                         "Native Heap Memory", "Scripting Memory" })
            {
                try
                {
                    using (var rec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, z))
                        if (rec.Valid) sb.Append(" · ").Append(z).Append(' ').Append(rec.CurrentValue >> 20).Append(" MB");
                }
                catch (System.Exception) { }
            }
            Art<Texture2D>(sb, "Texturen"); Art<Mesh>(sb, "Netze"); Art<AudioClip>(sb, "Audio"); Art<TerrainData>(sb, "Gelaende");
            Art<Shader>(sb, "Shader"); Art<ComputeShader>(sb, "ComputeShader"); Art<AnimationClip>(sb, "Animationen"); Art<Material>(sb, "Materialien");
            Art<Texture2DArray>(sb, "Texturfelder"); Art<Cubemap>(sb, "Cubemaps"); Art<RenderTexture>(sb, "RenderTextures"); Art<Font>(sb, "Schriften");
            // Netze: wie viel davon braucht die Physik (MeshCollider), wie viel nur die Darstellung?
            var collider = new System.Collections.Generic.HashSet<Mesh>();
            foreach (MeshCollider mc in FindObjectsByType<MeshCollider>(FindObjectsInactive.Include)) if (mc != null && mc.sharedMesh != null) collider.Add(mc.sharedMesh);
            long phys = 0, rest = 0, readable = 0; int nPhys = 0, nRest = 0;
            foreach (Mesh m in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                if (m == null) continue;
                long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m);
                if (collider.Contains(m)) { phys += b; nPhys++; } else { rest += b; nRest++; }
                if (m.isReadable) readable += b;
            }
            sb.Append(" · Netze fuer Physik ").Append(nPhys).Append(" Stueck ").Append(phys >> 20).Append(" MB, nur Darstellung ").Append(nRest).Append(" Stueck ").Append(rest >> 20)
              .Append(" MB, lesbar (CPU-Kopie) ").Append(readable >> 20).Append(" MB");
            return sb.ToString();
        }

        private static void Art<T>(System.Text.StringBuilder sb, string name) where T : Object
        {
            T[] all = Resources.FindObjectsOfTypeAll<T>();
            long sum = 0;
            var top = new System.Collections.Generic.List<(string, long)>();
            foreach (T o in all)
            {
                if (o == null) continue;
                long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(o);
                sum += b;
                if (b >= 2L << 20) top.Add((o.name, b));
            }
            top.Sort((x, y) => y.Item2.CompareTo(x.Item2));
            sb.Append(" · ").Append(name).Append(' ').Append(all.Length).Append(" Stueck ").Append(sum >> 20).Append(" MB");
            for (int i = 0; i < top.Count && i < 6; i++) sb.Append(i == 0 ? " [" : ", ").Append(top[i].Item1).Append(' ').Append(top[i].Item2 >> 20).Append(" MB");
            if (top.Count > 0) sb.Append(']');
        }

        /// <summary>Server ohne Grafik: Renderer einer neu erzeugten Figur gleich abschalten (Gegner aus dem Aktivierungsraster).</summary>
        public static void StripRenderers(GameObject go)
        {
            if (!Active || go == null) return;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                if (r != null && r.enabled) { r.enabled = false; Stripped++; }
            foreach (Light l in go.GetComponentsInChildren<Light>(true))
                if (l != null && l.enabled) { l.enabled = false; Stripped++; }
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
                StopParticles(ps);
        }

        /// <summary>Partikel am Server: anhalten und leeren; Senden/Spielen bleibt aus (Darstellung, keine Spiel-Logik).</summary>
        private static void StopParticles(ParticleSystem ps)
        {
            if (ps == null) return;
            ParticleSystem.EmissionModule em = ps.emission;
            if (!em.enabled && !ps.isPlaying) return;
            em.enabled = false;
            if (ps.isPlaying) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            Stripped++;
        }

        private static void Strip<T>() where T : Behaviour
        {
            foreach (T b in FindObjectsByType<T>(FindObjectsInactive.Exclude))
            {
                if (b == null || !b.enabled) continue;
                b.enabled = false;
                Stripped++;
            }
        }
    }
}
