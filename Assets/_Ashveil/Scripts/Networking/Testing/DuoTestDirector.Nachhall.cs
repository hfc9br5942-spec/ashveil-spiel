using System.Collections;
using System.Linq;
using Ashveil.Data;
using Ashveil.Data.Motion;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Motion;
using Ashveil.Gameplay.Visuals;
using Ashveil.Gameplay.Weapons;
using FishNet;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Handprobe des legendaeren Schwerts „Nachhall“ (beide Klingen) an der echten Spielfigur des Editor-Hosts:
    /// anlegen, Sitz am Koerper (Huefte bzw. Ruecken), Stehen, Rueckansicht, Hieb gegen die Trainingspuppe und der
    /// Lichtpuls in den Spalten. Teil des Kampf-Prototyp-Tests. Bilder: Logs/Ashveil/Ausruestung/Nachhall_*.png
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        /// <summary>Abstand Griffmitte (Modellursprung) zur rechten Hand: gezogen &lt; 0,15 m, weggesteckt deutlich mehr.</summary>
        private static float GripToRightHand(Transform root, Transform model)
        {
            if (model == null) return -1f;
            foreach (Transform x in root.GetComponentsInChildren<Transform>(true))
                if (SkelettNamen.Kanonisch(x.name) == "hand_r") return Vector3.Distance(x.position, model.position);   // Tripo-Körper: mixamorig:RightHand
            return -1f;
        }

        private IEnumerator NachhallHandProbe(NetworkInventory a)
        {
            Line("");
            Line("[Nachhall: Handprobe]");
            var vis = a.GetComponent<EquipmentVisuals>();
            AppearanceRig rig = vis != null ? vis.Rig : null;
            Check(rig != null, "Figur mit sichtbarer Ausrüstung");
            if (rig == null) yield break;

            var camGo = new GameObject("Nachhall Capture Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 30f;
            int preview = LayerMask.NameToLayer(CharacterPreviewStage.LayerName);
            if (preview >= 0) cam.cullingMask &= ~(1 << preview);
            Transform t = a.transform;

            void Shot(string name, Vector3 offset, float lookHeight)
            {
                Vector3 target = t.position + Vector3.up * lookHeight;
                camGo.transform.position = t.position + t.rotation * offset;
                camGo.transform.LookAt(target);
                CaptureUtil.Capture(cam, ImagePath("Nachhall_" + name), 900, 900);
            }

            // Platzhalter-Mindeststufe 18: Testfigur kurz auf Stufe 20 heben, danach zuruecksetzen
            int levelBefore = Mathf.Max(1, a.Level);
            a.ServerSetLevel(20);
            yield return new WaitForSecondsRealtime(0.3f);
            Info($"Klasse der Testfigur: {(a.Class != null ? a.Class.DisplayName : "-")}, Stufe {levelBefore} → 20");

            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 open = (spawn != null ? spawn.transform.position : Vector3.zero) + new Vector3(-8f, 0f, 8f);
            TrainingDummy dummy = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None).FirstOrDefault();

            foreach ((string key, string label) in new[] { ("nachhall_short", "Kurz"), ("nachhall_great", "Gross") })
            {
                a.ServerResetForTest();
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Equip(a, key);
                yield return new WaitForSecondsRealtime(0.4f);
                Check(Equipped(a, EquipmentSlot.MainHand) == "item." + key, $"{label}: angelegt (Haupthand)");
                Transform model = rig.WeaponModel(EquipmentSlot.MainHand);
                bool lod = model != null && model.GetComponentInChildren<LODGroup>(true) != null;
                bool pulse = model != null && model.GetComponentInChildren<WeaponEchoVisual>(true) != null;
                Check(lod && pulse, $"{label}: Spielmodell mit LOD-Gruppe und Lichtpuls am Körper");
                Info($"{label}: hängt an „{(model != null && model.parent != null ? model.parent.name : "-")}“");

                var orch = a.GetComponentInChildren<CharacterMotionOrchestrator>();
                var input = a.GetComponent<PlayerInputReader>();

                // Weggesteckt (Erkunden): Sitz an Huefte bzw. Ruecken
                orch?.ForceStance(MotionStance.Exploration);
                yield return Ask(a, "goto", V(open, 30f));
                yield return new WaitForSecondsRealtime(1.8f);
                Transform stowedAt = model != null ? model.parent : null;
                Info($"{label}: weggesteckt an „{(stowedAt != null ? stowedAt.name : "-")}“");
                float stowDist = GripToRightHand(t, model);
                Check(stowDist > 0.25f, $"{label}: weggesteckt am Körper (Griff {stowDist:0.00} m von der rechten Hand)");
                Shot(label + "_1_Stehen", new Vector3(1.1f, 1.4f, 2.6f), 1.0f);
                Shot(label + "_2_Ruecken", new Vector3(0.9f, 1.4f, -2.6f), 1.0f);
                Shot(label + "_3_Sitz_nah", new Vector3(label == "Kurz" ? -1.1f : 0.6f, 1.35f, label == "Kurz" ? 0.4f : -1.3f), label == "Kurz" ? 0.95f : 1.25f);

                // Gezogen (Kampfhaltung), danach in Bewegung
                orch?.ForceStance(MotionStance.Combat);
                yield return new WaitForSecondsRealtime(1.2f);
                float drawDist = GripToRightHand(t, model);
                Check(drawDist >= 0f && drawDist < 0.15f, $"{label}: gezogen in der rechten Hand (Griff {drawDist:0.00} m von der Hand)");
                Shot(label + "_4_Kampfhaltung", new Vector3(1.6f, 1.3f, 2.2f), 1.1f);
                input?.InjectMovement(1f, false);
                yield return new WaitForSecondsRealtime(0.9f);
                Shot(label + "_5_Laufen_gezogen", new Vector3(2.4f, 1.3f, 0.6f), 1.0f);
                orch?.ForceStance(MotionStance.Exploration);
                yield return new WaitForSecondsRealtime(1.2f);
                Shot(label + "_6_Laufen_weggesteckt", new Vector3(2.4f, 1.3f, 0.6f), 1.0f);
                input?.InjectMovement(0f, false);
                orch?.ForceStance(null);
                yield return new WaitForSecondsRealtime(0.8f);

                if (dummy == null) { Info("Keine Trainingspuppe – Hieb übersprungen"); continue; }
                Vector3 d = t.position - dummy.transform.position;
                d.y = 0f;
                d = d.sqrMagnitude < 0.01f ? Vector3.forward : d.normalized;
                yield return Ask(a, "goto", V(dummy.transform.position + d * 1.7f, Quaternion.LookRotation(-d).eulerAngles.y));
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Ask(a, "attack", dummy.GetComponent<NetworkObject>().ObjectId.ToString());
                yield return new WaitForSecondsRealtime(0.45f);
                Shot(label + "_7_Hieb", new Vector3(2.2f, 1.3f, 1.6f), 1.1f);
                yield return new WaitForSecondsRealtime(0.5f);
                Shot(label + "_8_Hieb2", new Vector3(2.2f, 1.3f, 1.6f), 1.1f);
                yield return Ask(a, "stop");

                // Lichtpuls: gleiche Kamera nah an der Klinge, vorher und auf dem Hoehepunkt
                WeaponDefinition w = GameData.Get<WeaponDefinition>("weapon." + key);
                Health dh = dummy.GetComponent<Health>();
                if (w != null && dh != null && model != null)
                {
                    orch?.ForceStance(MotionStance.Combat);
                    yield return new WaitForSecondsRealtime(w.echoCooldown + 0.2f);
                    Vector3 bladeMid = model.position + model.forward * (label == "Kurz" ? 0.4f : 0.7f);
                    Vector3 camPos = bladeMid - t.forward * -1.0f + t.right * 0.5f + Vector3.up * 0.15f;
                    float old = w.echoChance;
                    w.echoChance = 1f;
                    try
                    {
                        camGo.transform.position = camPos;
                        camGo.transform.LookAt(bladeMid);
                        CaptureUtil.Capture(cam, ImagePath("Nachhall_" + label + "_9_Klinge_vorher"), 900, 900);
                        int p0 = WeaponEchoVisual.PulseCount;
                        dh.ApplyDamage(new DamageInfo { Amount = 30f, Source = a.gameObject, Direction = -d, WeaponId = w.Id });
                        yield return new WaitForSecondsRealtime(w.echoDelay + 0.1f);
                        camGo.transform.position = camPos;
                        camGo.transform.LookAt(bladeMid);
                        CaptureUtil.Capture(cam, ImagePath("Nachhall_" + label + "_10_Klinge_Puls"), 900, 900);
                        Check(WeaponEchoVisual.PulseCount > p0, $"{label}: Lichtpuls in der getragenen Klinge ausgelöst");
                    }
                    finally { w.echoChance = old; orch?.ForceStance(null); }
                }
                yield return new WaitForSecondsRealtime(0.6f);
            }
            a.ServerResetForTest();
            a.ServerSetLevel(levelBefore);
            Destroy(camGo);
        }
    }
}
