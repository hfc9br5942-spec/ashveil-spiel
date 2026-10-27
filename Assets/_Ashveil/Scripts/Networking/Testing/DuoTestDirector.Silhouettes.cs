using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Core;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Visuals;
using Ashveil.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Networking
{
    /// <summary>
    /// Art-Pass 5 (Art-Direction-Bible § 70–73, § 85): Silhouettentest in der echten Spielkamera.
    /// Figuren der Silhouetten-Buehne (Basis Mann/Frau, Cael, Elara, Seris, Quaternius zum Vergleich) aus 2/5/15/30 m,
    /// vorn · 3/4 · Seite · hinten, jeweils Farbe, Graustufen (Rec. 709) und reine Silhouette (Figur schwarz auf weiss).
    /// Licht 15:30 (gestaltetes Kareth-Licht) und 22:00 (Nacht). Bilder: Logs/Ashveil/Silhouetten/.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string SilhouetteFlag = "silhouetten";
        private static bool _silhouetteMode;

        // Spielkamera (MMOCameraController): Blickwinkel 60°, Drehpunkt 1,65 m ueber der Figur, Neigung 14°
        private const float CamFov = 60f, CamPivot = 1.65f, CamPitch = 14f;

        private IEnumerator SilhouetteSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(EmoteA)) != null, 30f);
            Check(a != null, "Maxmod (Editor-Host) angemeldet");
            yield return new WaitForSecondsRealtime(1f);

            Line("");
            Line("[Silhouetten-Bühne]");
            var stage = FindAnyObjectByType<SilhouetteStage>(FindObjectsInactive.Include);
            Check(stage != null && stage.Figures.Count == 7, $"Bühne mit {(stage != null ? stage.Figures.Count : 0)} Figuren: {(stage != null ? string.Join(", ", stage.Labels) : "-")}");
            if (stage == null || stage.Figures.Count == 0) yield break;
            stage.Show(true);
            NetworkWorldTime.ServerJumpTo(15.5f);
            yield return null;
            // Buehne zur Sonne drehen: die Kamera steht vor den Figuren, das Licht kommt von hinter der Kamera
            Light sunLight = RenderSettings.sun;
            Transform stageT = stage.transform;
            Quaternion stageRot0 = stageT.rotation;
            if (sunLight != null)
            {
                Vector3 toSun = -sunLight.transform.forward;
                toSun.y = 0f;
                if (toSun.sqrMagnitude > 1e-4f) stageT.rotation = Quaternion.LookRotation(toSun.normalized) * Quaternion.Euler(0f, -25f, 0f);
            }
            foreach (Transform f in stage.Figures)
            {
                Vector3 p = f.position;
                if (WorldGround.TrySample(p, out float gh)) p.y = gh;
                f.position = p;
            }
            // Neutrales Idle: die Spieler-Standhaltung (Schwert aufstuetzen) passt nicht zu Figuren ohne gezogene Waffe
            foreach (Transform f in stage.Figures)
                foreach (StancePoser sp in f.GetComponentsInChildren<StancePoser>(true)) sp.enabled = false;
            float until = Time.realtimeSinceStartup + 10f;
            List<NpcBody> bodies = stage.Figures.Select(f => f.GetComponentInChildren<NpcBody>(true)).ToList();
            while (Time.realtimeSinceStartup < until && bodies.Any(b => b == null || !b.Built)) yield return null;
            Check(bodies.All(b => b != null && b.Built), $"Alle Figuren eingekleidet ({bodies.Count(b => b != null && b.Built)}/{bodies.Count})");
            yield return new WaitForSecondsRealtime(1.5f);

            // Retargeting: Figuren bewegen sich (Idle) und stehen auf dem Boden
            var anims = stage.Figures.Select(f => f.GetComponentInChildren<Animator>(true)).ToList();
            var hands = anims.Select(an => an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.RightHand) : null).ToList();
            var p0 = hands.Select(h => h != null ? h.position : Vector3.zero).ToList();
            yield return new WaitForSecondsRealtime(1.2f);
            int moving = hands.Where((h, i) => h != null && (h.position - p0[i]).sqrMagnitude > 1e-6f).Count();
            Check(anims.All(an => an != null && an.isHuman && an.avatar != null && an.avatar.isValid) && moving == anims.Count,
                $"Humanoid-Animationen laufen auf allen Figuren (Quaternius-Clips per Retargeting, {moving}/{anims.Count} bewegen sich)");
            var ground = new List<string>();
            bool feetOk = true;
            for (int i = 0; i < anims.Count; i++)
            {
                if (anims[i] == null || !anims[i].isHuman) continue;
                Transform fl = anims[i].GetBoneTransform(HumanBodyBones.LeftFoot), fr = anims[i].GetBoneTransform(HumanBodyBones.RightFoot);
                float g = WorldGround.SampleHeight(fl.position, 0f);
                float lowest = Mathf.Min(fl.position.y, fr.position.y) - g;
                feetOk &= lowest > -0.05f && lowest < 0.16f;
                ground.Add($"{stage.Labels[i]} {lowest * 100f:0} cm");
                // Kopfhoehe (Stand) fuer den Bericht
            }
            Check(feetOk, "Füße auf dem Boden (Fußgelenk über Gelände: " + string.Join(", ", ground) + ")");
            var heights = new List<string>();
            for (int i = 0; i < anims.Count; i++)
            {
                if (anims[i] == null || !anims[i].isHuman) continue;
                var smr = anims[i].GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0).FirstOrDefault();
                if (smr == null) continue;
                heights.Add($"{stage.Labels[i]} {smr.bounds.max.y - stage.Figures[i].position.y:0.00} m");
            }
            Info("Höhe im Stand (Körper, ohne Haare): " + string.Join(" · ", heights));
            var rigs = stage.Figures.Select(f => f.GetComponentInChildren<AppearanceRig>(true)).ToList();
            Check(rigs[2] != null && rigs[2].WeaponModel(Ashveil.Data.EquipmentSlot.MainHand) != null && rigs[4] != null && rigs[4].WeaponModel(Ashveil.Data.EquipmentSlot.MainHand) != null,
                "Waffen-Blockouts verstaut: Eidwächter-Langschwert (Cael, Elara), Seelenfokus (Seris)");

            // Gegner und Spieler im Bild ausblenden (Aschenfeld: der Glockenhirte zieht umher)
            var hidden = new List<Renderer>();
            // Deren Detailsteuerung schaltet Renderer sonst 4× je Sekunde wieder ein
            var lods = new List<CharacterLod>();
            foreach (var lod in FindObjectsByType<CharacterLod>(FindObjectsSortMode.None))
            {
                if (!lod.enabled || lod.GetComponentInParent<SilhouetteStage>() != null) continue;
                lod.enabled = false;
                lods.Add(lod);
            }
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled) continue;
                Transform t = r.transform;
                bool enemy = false;
                while (t != null) { if (t.name.StartsWith("Enemy_") || t.GetComponent<NetworkInventory>() != null) { enemy = true; break; } t = t.parent; }
                // ... ebenso Deko direkt neben der Buehne (Aschenfeld-Requisiten im Bildrand)
                if (!enemy && r.GetComponentInParent<SilhouetteStage>() == null)
                {
                    Vector3 c = r.bounds.center;
                    Vector3 sc = stage.transform.position;
                    enemy = new Vector2(c.x - sc.x, c.z - sc.z).magnitude < 8f && r.bounds.size.magnitude < 12f;
                }
                if (!enemy) continue;
                r.enabled = false;
                hidden.Add(r);
            }

            string dir = ProjectPaths.Logs("Silhouetten");
            Directory.CreateDirectory(dir);
            Camera cam = Camera.main;
            if (cam == null) { Check(false, "Kamera"); yield break; }
            Vector3 pos0 = cam.transform.position;
            Quaternion rot0 = cam.transform.rotation;
            float fov0 = cam.fieldOfView;
            var figRot0 = stage.Figures.Select(f => f.localRotation).ToList();
            // Renderer je Figur bei jeder Aufnahme frisch (Ausruestung und Waffen entstehen erst nach dem Einkleiden)
            List<Renderer[]> FigRenderers() => stage.Figures.Select(f => f.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray()).ToList();
            int images = 0;
            var coverage30 = new List<string>();
            bool coverOk = true;

            Line("");
            Line("[Aufnahmen]");
            (string tag, float yaw)[] views = { ("vorn", 0f), ("dreiviertel", 45f), ("seite", 90f), ("hinten", 180f) };
            foreach (float hour in new[] { 15.5f, 22f })
            {
                NetworkWorldTime.ServerJumpTo(hour);
                yield return null;
                yield return null;
                string time = hour < 20f ? "1530" : "2200";

                // 2 m und 5 m: je Figur, vier Ansichten -> Kontaktbogen (Spalten = Figuren, Zeilen = Ansichten)
                foreach (float d in new[] { 2f, 5f })
                {
                    if (hour > 20f && d < 5f) continue; // Nacht nur 5/15/30 m
                    const int TW = 300, TH = 600;
                    var sheets = new[] { new Texture2D(TW * 7, TH * 4, TextureFormat.RGB24, false), new Texture2D(TW * 7, TH * 4, TextureFormat.RGB24, false),
                                         new Texture2D(TW * 7, TH * 4, TextureFormat.RGB24, false) };
                    for (int v = 0; v < views.Length; v++)
                    {
                        for (int i = 0; i < stage.Figures.Count; i++)
                            stage.Figures[i].localRotation = figRot0[i] * Quaternion.Euler(0f, views[v].yaw, 0f);
                        yield return null;
                        for (int i = 0; i < stage.Figures.Count; i++)
                        {
                            var figRenderers = FigRenderers();
                            // Nachbarn fuer die Einzelaufnahme weit wegstellen. Gehaeutete Figuren folgen erst im naechsten
                            // Bild (die Haeutung dieses Bildes ist schon gerechnet) – daher ein Bild abwarten.
                            for (int o = 0; o < stage.Figures.Count; o++)
                                if (o != i) stage.Figures[o].position += Vector3.down * 500f;
                            yield return null;
                            Aim(cam, stage.Figures[i].position, stage.Figures[i].parent.forward, d);
                            var (col, grey, sil) = Triple(cam, TW, TH, figRenderers[i]);
                            for (int o = 0; o < stage.Figures.Count; o++)
                                if (o != i) stage.Figures[o].position += Vector3.up * 500f;
                            if (col == null) continue;
                            int x = i * TW, y = (3 - v) * TH;
                            sheets[0].SetPixels(x, y, TW, TH, col.GetPixels());
                            sheets[1].SetPixels(x, y, TW, TH, grey.GetPixels());
                            sheets[2].SetPixels(x, y, TW, TH, sil.GetPixels());
                            Destroy(col); Destroy(grey); Destroy(sil);
                        }
                    }
                    string[] kind = { "farbe", "grau", "silhouette" };
                    for (int k = 0; k < 3; k++)
                    {
                        sheets[k].Apply();
                        File.WriteAllBytes(Path.Combine(dir, $"Figuren_{d:0}m_{time}_{kind[k]}.png"), sheets[k].EncodeToPNG());
                        Destroy(sheets[k]);
                        images++;
                    }
                }

                // 15 m und 30 m: ganze Reihe in einem Bild (vorn und 3/4)
                foreach (float d in new[] { 15f, 30f })
                {
                    foreach (var (tag, yaw) in new[] { views[0], views[1] })
                    {
                        for (int i = 0; i < stage.Figures.Count; i++) stage.Figures[i].localRotation = figRot0[i] * Quaternion.Euler(0f, yaw, 0f);
                        yield return null;
                        Transform st = stage.Figures[0].parent;
                        Vector3 mid = stage.Figures.Aggregate(Vector3.zero, (s, f) => s + f.position) / stage.Figures.Count;
                        Aim(cam, mid, st.forward, d);
                        var all = FigRenderers().SelectMany(r => r).ToArray();
                        var (col, grey, sil) = Triple(cam, 1920, 1080, all);
                        if (col == null) continue;
                        File.WriteAllBytes(Path.Combine(dir, $"Reihe_{d:0}m_{time}_{tag}_farbe.png"), col.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(dir, $"Reihe_{d:0}m_{time}_{tag}_grau.png"), grey.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(dir, $"Reihe_{d:0}m_{time}_{tag}_silhouette.png"), sil.EncodeToPNG());
                        images += 3;
                        if (d >= 30f && tag == "vorn" && hour < 20f)
                        {
                            // Groesse jeder Figur in der Silhouette (Pixel bei 1080p): Hoehe und groesste Breite
                            cam.aspect = 1920f / 1080f;
                            var xs = stage.Figures.Select(f => cam.WorldToViewportPoint(f.position + Vector3.up * 0.9f).x * 1920f).ToList();
                            for (int i = 0; i < stage.Figures.Count; i++)
                            {
                                int cx = Mathf.RoundToInt(xs[i]);
                                float gap = float.MaxValue;
                                if (i > 0) gap = Mathf.Min(gap, Mathf.Abs(xs[i] - xs[i - 1]));
                                if (i < xs.Count - 1) gap = Mathf.Min(gap, Mathf.Abs(xs[i + 1] - xs[i]));
                                Vector2Int hw = Measure(sil, cx, Mathf.Max(4, Mathf.FloorToInt(gap * 0.5f) - 1));
                                coverOk &= hw.x > 20;
                                coverage30.Add($"{stage.Labels[i]} {hw.x}×{hw.y} px");
                            }
                            cam.ResetAspect();
                        }
                        Destroy(col); Destroy(grey); Destroy(sil);
                    }
                }
            }

            for (int i = 0; i < stage.Figures.Count; i++) stage.Figures[i].localRotation = figRot0[i];
            stageT.rotation = stageRot0;
            foreach (Transform f in stage.Figures)
                foreach (StancePoser sp in f.GetComponentsInChildren<StancePoser>(true)) sp.enabled = true;
            cam.transform.SetPositionAndRotation(pos0, rot0);
            cam.fieldOfView = fov0;
            foreach (var r in hidden) if (r != null) r.enabled = true;
            foreach (var lod in lods) if (lod != null) lod.enabled = true;
            stage.Show(false);
            NetworkWorldTime.ServerJumpTo((float)WorldClock.DefaultStartHour);

            Check(images >= 33, $"{images} Bilder (Farbe, Graustufen, Silhouette) nach Logs/Ashveil/Silhouetten");
            Check(coverOk, "Auf 30 m ist jede Figur als Silhouette sichtbar (Höhe × Breite bei 1080p): " + string.Join(" · ", coverage30));
            Info("Bewertung (Bible § 70–73: Klasse, Geschlecht, Silhouette auf 30 m; Werte-Hierarchie im Graubild) trifft Max anhand der Kontaktbögen.");
        }

        /// <summary>Spielkamera nachbilden: Drehpunkt 1,65 m ueber der Figur, Abstand d, Neigung 14°, von vorn.</summary>
        private static void Aim(Camera cam, Vector3 figure, Vector3 facing, float d)
        {
            Vector3 pivot = figure + Vector3.up * CamPivot;
            Vector3 flat = new Vector3(facing.x, 0f, facing.z).normalized;
            float pr = CamPitch * Mathf.Deg2Rad;
            Vector3 pos = pivot + flat * (d * Mathf.Cos(pr)) + Vector3.up * (d * Mathf.Sin(pr));
            // Nah (2 m): auf Brust/Kopf, sonst wie im Spiel auf den Drehpunkt
            Vector3 look = d <= 2.5f ? figure + Vector3.up * 1.45f : pivot - Vector3.up * Mathf.Min(0.75f, d * 0.05f);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            cam.fieldOfView = CamFov;
        }

        /// <summary>Farbbild, Graustufen (Rec. 709) und Silhouette (Differenz mit/ohne Figur, Schatten der Figur aus).</summary>
        private static Material _silhouetteMat;

        private static (Texture2D col, Texture2D grey, Texture2D sil) Triple(Camera cam, int w, int h, Renderer[] figure)
        {
            Texture2D col = Grab(cam, w, h);
            if (col == null) return (null, null, null);
            Color[] c = col.GetPixels();
            var g = new Color[c.Length];
            for (int i = 0; i < c.Length; i++)
            {
                float l = 0.2126f * c[i].r + 0.7152f * c[i].g + 0.0722f * c[i].b;
                g[i] = new Color(l, l, l);
            }
            var grey = new Texture2D(w, h, TextureFormat.RGB24, false);
            grey.SetPixels(g);
            grey.Apply();

            // Silhouette: Figur kurz einfarbig (unbeleuchtetes Magenta) rendern und diese Farbe maskieren –
            // unabhaengig davon, wie dunkel Ruestung und Boden sind
            if (_silhouetteMat == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
                _silhouetteMat = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
                _silhouetteMat.SetColor("_BaseColor", new Color(1f, 0f, 1f, 1f));
            }
            var saved = figure.Select(r => r.sharedMaterials).ToArray();
            foreach (var r in figure)
            {
                var m = new Material[r.sharedMaterials.Length];
                for (int k = 0; k < m.Length; k++) m[k] = _silhouetteMat;
                r.sharedMaterials = m;
            }
            Texture2D magenta = Grab(cam, w, h);
            for (int i = 0; i < figure.Length; i++) figure[i].sharedMaterials = saved[i];
            var s = new Color[c.Length];
            if (magenta != null)
            {
                Color[] a = magenta.GetPixels();
                for (int i = 0; i < s.Length; i++)
                {
                    Color p = a[i];
                    s[i] = p.r - p.g > 0.22f && p.b - p.g > 0.22f ? Color.black : Color.white;
                }
                Destroy(magenta);
            }
            var sil = new Texture2D(w, h, TextureFormat.RGB24, false);
            sil.SetPixels(s);
            sil.Apply();
            return (col, grey, sil);
        }

        /// <summary>Hoehe und groesste Breite der schwarzen Flaeche um eine Bildspalte (Suchbreite +-halfWidth).</summary>
        private static Vector2Int Measure(Texture2D sil, int cx, int halfWidth)
        {
            int w = sil.width, h = sil.height;
            Color[] p = sil.GetPixels();
            int top = -1, bottom = h, maxW = 0;
            for (int y = 0; y < h; y++)
            {
                int left = -1, right = -1;
                for (int x = Mathf.Max(0, cx - halfWidth); x <= Mathf.Min(w - 1, cx + halfWidth); x++)
                {
                    if (p[y * w + x].r > 0.5f) continue;
                    if (left < 0) left = x;
                    right = x;
                }
                if (left < 0) continue;
                top = Mathf.Max(top, y);
                bottom = Mathf.Min(bottom, y);
                maxW = Mathf.Max(maxW, right - left + 1);
            }
            return top < 0 ? Vector2Int.zero : new Vector2Int(top - bottom + 1, maxW);
        }
    }
}
