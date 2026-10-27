using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using L = Ashveil.FloraValidation.FloraValidationLayout;

namespace Ashveil.FloraValidation
{
    /// <summary>Testplan des Flora-Messlaufs (Szenarien A–D, Instancing, Schatten, LOD, Impostor, Wind, Saison, Overdraw, Speicher).</summary>
    public partial class FloraValidationRunner
    {
        // Habitat-Posen (Unity-Koordinaten, x gegenueber dem Offline-Habitat gespiegelt)
        private static readonly Vector3 HabEyePos = new Vector3(-6, 2.9f, 36), HabEyeTgt = new Vector3(4, 5.5f, -10);
        private static readonly Vector3 HabOverPos = new Vector3(-62, 42, 78), HabOverTgt = new Vector3(6, 2, -18);
        private static readonly Vector3 HabEdgePos = new Vector3(14, 2.6f, -6), HabEdgeTgt = new Vector3(26, 6, -40);
        private static readonly Vector3 HabStandPos = new Vector3(40, 2.2f, -40), HabStandTgt = new Vector3(34, 5, -60);
        private static readonly Vector3 HabClearPos = new Vector3(24, 2.0f, -42), HabClearTgt = new Vector3(36, 2.5f, -54);

        private float M => quick ? 1.5f : 4f;

        /// <summary>Wartet, bis animierte LOD-Crossfades nach einem Kamerasprung abgeschlossen sind (Standbilder ohne Dither-Mischzustand).</summary>
        private object Settle() => new WaitForSecondsRealtime(quick ? 0.6f : 0.9f);

        // ------------------------------------------------------------------ Leistung je Szenario

        private IEnumerator PerfSuite()
        {
            env.ApplyPreset(FloraEnvironment.WindPreset.Normal); env.season = 0; env.leafFall = 0; env.snow = 0;
            // A – einzelne Hero-Pflanze (typische Spielkamera)
            Activate(areaHero);
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = L.HeroPos(i);
                ThirdPerson(p + new Vector3(0, 0, -16), Vector3.forward);
                yield return Measure("A Hero", L.HeroItems[i], M, null, areaHero);
            }
            // B – kleine Gruppe
            Activate(areaGroup);
            ThirdPerson(L.Group, Vector3.forward);
            yield return Measure("B Gruppe", "Spielkamera", M, null, areaGroup);
            Pose(L.Group + new Vector3(0, 1.7f, 0), L.Group + new Vector3(0, 6, 30));
            yield return Measure("B Gruppe", "Augenhoehe", M, null, areaGroup);
            // C – Bestand / Waldrand (Mini-Habitat v2)
            Activate(areaHabitat);
            foreach (var (n, p, t) in new[] { ("Augenhoehe Wiese", HabEyePos, HabEyeTgt), ("Waldrand", HabEdgePos, HabEdgeTgt), ("im Bestand", HabStandPos, HabStandTgt), ("Uebersicht", HabOverPos, HabOverTgt) })
            {
                Pose(L.Habitat + p, L.Habitat + t);
                yield return Measure("C Habitat", n, M, null, areaHabitat);
            }
            // D – Stresstest (9 Habitat-Kacheln, nur Messung)
            Activate(areaStress);
            Pose(L.Stress + HabStandPos, L.Stress + HabStandTgt);
            yield return Measure("D Stress", "im Bestand", M, null, areaStress);
            Pose(L.Stress + HabEyePos, L.Stress + HabEyeTgt);
            yield return Measure("D Stress", "Augenhoehe Wiese", M, null, areaStress);
            Pose(L.Stress + new Vector3(-300, 140, 330), L.Stress);
            yield return Measure("D Stress", "Uebersicht", M, null, areaStress);
            // F10: volle Geometrie gegen LOD-Test (Feld 10x10 Gruppen = 85 x 85 m)
            foreach (var (area, name) in new[] { (areaF10Full, "volle Geometrie (v2)"), (areaF10Lod, "LOD-Test") })
            {
                Activate(area);
                Pose(area.position + new Vector3(-3, 1.7f, -3), area.position + new Vector3(42, 0.5f, 42));
                yield return Measure("F10 Feld", name + " Augenhoehe", M, null, area);
                Pose(area.position + new Vector3(-15, 22, -15), area.position + new Vector3(42, 0, 42));
                yield return Measure("F10 Feld", name + " Uebersicht", M, null, area);
            }
            // F19: 30 Stamm-Wirte voll gegen LOD
            foreach (var (area, name) in new[] { (areaF19Full, "volle Geometrie (v2)"), (areaF19Lod, "LOD-Test") })
            {
                Activate(area);
                Pose(area.position + new Vector3(-6, 1.7f, -6), area.position + new Vector3(35, 3, 28));
                yield return Measure("F19 Feld", name, M, null, area);
            }
        }

        // ------------------------------------------------------------------ Instancing / SRP-Batcher

        private IEnumerator InstancingSuite()
        {
            env.ApplyPreset(FloraEnvironment.WindPreset.Normal);
            foreach (var (area, name) in new[] { (areaInstSame, "A gleiche Instanz (1 Mesh)"), (areaInstSeeds, "B 6 Seeds eines Archetyps"), (areaInstMixed, "C gemischter Bestand") })
            {
                Activate(area);
                foreach (bool srp in new[] { true, false })
                {
                    GraphicsSettings.useScriptableRenderPipelineBatching = srp;
                    string mode = srp ? "SRP-Batcher" : "GPU-Instancing (SRP aus)";
                    Pose(area.position + new Vector3(-20, 30, -20), area.position + new Vector3(85, 0, 85));
                    yield return Measure("Instancing " + name, mode + " Uebersicht", M, null, area);
                    Pose(area.position + new Vector3(-5, 1.7f, -5), area.position + new Vector3(85, 8, 85));
                    yield return Measure("Instancing " + name, mode + " Augenhoehe", M, null, area);
                }
                GraphicsSettings.useScriptableRenderPipelineBatching = _srpBatcherInitial;
            }
        }

        // ------------------------------------------------------------------ Schatten

        private IEnumerator ShadowSuite()
        {
            // v3: provisorische Schatten-Policy (nicht kanonisiert). P0 = alle LOD werfen (v1-Stand).
            var policies = new (string n, bool near, bool mid, bool far, bool vfar)[]
            {
                ("P0 alle LOD", true, true, true, true),
                ("P1 NEAR+MID, FAR/VFAR aus", true, true, false, false),
                ("P2 nur NEAR", true, false, false, false),
                ("P3 NEAR+MID+VFAR-Billboard", true, true, false, true),
            };
            var scenes = new (string n, Transform area, Vector3 pos, Vector3 tgt, string img)[]
            {
                ("Schatten C Wiese", areaHabitat, L.Habitat + HabEyePos, L.Habitat + HabEyeTgt, "Wiese"),
                ("Schatten C Waldrand", areaHabitat, L.Habitat + HabEdgePos, L.Habitat + HabEdgeTgt, "Waldrand"),
                ("Schatten C Bestand", areaHabitat, L.Habitat + HabStandPos, L.Habitat + HabStandTgt, null),
                ("Schatten D Stress", areaStress, L.Stress + HabStandPos, L.Stress + HabStandTgt, null),
            };
            foreach (var sc in scenes)
            {
                Activate(sc.area);
                Pose(sc.pos, sc.tgt);
                for (int k = 0; k < policies.Length; k++)
                {
                    var p = policies[k];
                    ShadowPolicy(sc.area, p.near, p.mid, p.far, p.vfar);
                    yield return Measure(sc.n, p.n, M, null, sc.area);
                    if (sc.img != null) { yield return Settle(); Still($"12_Schatten_{sc.img}_P{k}"); }
                }
                ShadowPolicy(sc.area, true, true, true, true);
            }
        }

        /// <summary>Schattenwurf je LOD-Stufe (Baeume: NEAR/MID/FAR/VFAR; 3-stufige Gruppen: LOD0/LOD1/LOD2 = near/mid/far).</summary>
        private static void ShadowPolicy(Transform root, bool near, bool mid, bool far, bool vfar)
        {
            foreach (LODGroup g in root.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = g.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    bool on = i == 0 ? near : i == 1 ? mid : i == 2 ? far : vfar;
                    if (lods.Length == 1) on = true;
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null) r.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
                }
            }
        }

        // ------------------------------------------------------------------ GPU Resident Drawer (isolierter Test, Menue 6)

        private IEnumerator GrdSuite()
        {
            if (grdPipeline == null) throw new Exception("GRD-Testpipeline fehlt (Menue 6 legt sie an)");
            RenderPipelineAsset orig = QualitySettings.renderPipeline;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    string tag = pass == 0 ? "Standard (GRD aus)" : "GPU Resident Drawer";
                    if (pass == 1) { QualitySettings.renderPipeline = grdPipeline; yield return null; yield return null; yield return new WaitForSecondsRealtime(1f); }
                    var urpA = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                    Log($"   Pipeline aktiv: {(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "?")} · GRD-Modus {(urpA != null ? urpA.GetType().GetProperty("gpuResidentDrawerMode")?.GetValue(urpA)?.ToString() ?? "?" : "?")} · Rendering-Pfad Forward+ erwartet");
                    foreach (var (area, name) in new[] { (areaInstSame, "Inst A 1 Mesh"), (areaInstSeeds, "Inst B 6 Seeds"), (areaInstMixed, "Inst C gemischt") })
                    {
                        Activate(area);
                        Pose(area.position + new Vector3(-20, 30, -20), area.position + new Vector3(85, 0, 85));
                        yield return Measure("GRD " + name + " Uebersicht", tag, M, null, area);
                        Pose(area.position + new Vector3(-5, 1.7f, -5), area.position + new Vector3(85, 8, 85));
                        yield return Measure("GRD " + name + " Augenhoehe", tag, M, null, area);
                    }
                    Activate(areaHabitat);
                    Pose(L.Habitat + HabEyePos, L.Habitat + HabEyeTgt);
                    yield return Measure("GRD C Wiese", tag, M, null, areaHabitat);
                    yield return Settle(); Still($"16_GRD_{pass}_Habitat_Wiese");
                    Pose(L.Habitat + HabStandPos, L.Habitat + HabStandTgt);
                    yield return Measure("GRD C Bestand", tag, M, null, areaHabitat);
                    Activate(areaStress);
                    Pose(L.Stress + HabStandPos, L.Stress + HabStandTgt);
                    yield return Measure("GRD D Stress Bestand", tag, M, null, areaStress);
                    Pose(L.Stress + new Vector3(-300, 140, 330), L.Stress);
                    yield return Measure("GRD D Stress Uebersicht", tag, M, null, areaStress);
                    yield return Settle(); Still($"16_GRD_{pass}_Stress");
                    Activate(areaF10Lod);
                    Pose(areaF10Lod.position + new Vector3(-3, 1.7f, -3), areaF10Lod.position + new Vector3(42, 0.5f, 42));
                    yield return Measure("GRD F10-Feld LOD", tag, M, null, areaF10Lod);
                    using (var rG = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory"))
                    using (var rT = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory"))
                    {
                        yield return null; yield return null;
                        float gfx = rG.Valid ? rG.LastValue / 1048576f : -1, tot = rT.Valid ? rT.LastValue / 1048576f : -1;
                        _report.memory.Add(new MemoryResult { group = "GRD " + tag, gfxUsedMB = gfx, totalUsedMB = tot });
                        Log($"   Speicher {tag}: Gfx {gfx:0} MB · Prozess {tot:0} MB");
                    }
                }
            }
            finally
            {
                QualitySettings.renderPipeline = orig;
                Log("   Pipeline zurueckgestellt: " + (orig != null ? orig.name : "Standard"));
            }
        }

        // ------------------------------------------------------------------ LOD statisch (erzwungene Stufen am Umschaltpunkt)

        private IEnumerator LodStaticSuite()
        {
            env.ApplyPreset(FloraEnvironment.WindPreset.Windstille);
            yield return null;
            foreach (var (area, items) in new[] { (areaLineupF01, L.LineupF01Items), (areaLineupF04, L.LineupF04Items) })
            {
                Activate(area);
                for (int k = 0; k < items.Length; k++)
                {
                    Transform tree = Find(area, items[k]);
                    for (int j = 0; j < items.Length; j++) Find(area, items[j]).gameObject.SetActive(j == k);
                    LODGroup g = tree.GetComponent<LODGroup>();
                    LODFadeMode fm = g.fadeMode; g.fadeMode = LODFadeMode.None;
                    Bounds b = BoundsOf(tree);
                    foreach (var (a, c, d) in new[] { (0, 1, 35f), (1, 2, 80f), (2, 3, 160f) })
                    {
                        Pose(new Vector3(b.center.x, 1.7f, b.center.z - d), new Vector3(b.center.x, b.center.y * 0.75f, b.center.z), 55f);
                        g.ForceLOD(a); yield return null; yield return null;
                        Texture2D ta = Render(1920, 1080, true, true, null);
                        g.ForceLOD(c); yield return null; yield return null;
                        Texture2D tb = Render(1920, 1080, true, true, null);
                        CompareMasks(items[k], $"{LodName(a)}->{LodName(c)}", d, ta, tb);
                        SaveCropPair($"07_LOD_{items[k]}_{LodName(a)}-{LodName(c)}", ta, tb);
                        Destroy(ta); Destroy(tb);
                    }
                    g.ForceLOD(-1); g.fadeMode = fm;
                }
                for (int j = 0; j < items.Length; j++) Find(area, items[j]).gameObject.SetActive(true);
            }
            env.ApplyPreset(FloraEnvironment.WindPreset.Normal);
        }

        private static string LodName(int i) => i switch { 0 => "NEAR", 1 => "MID", 2 => "FAR", _ => "VFAR" };

        private static Bounds BoundsOf(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.one);
            foreach (Renderer r in rs) if (!r.name.Contains("VFAR")) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Speichert beide Bilder auf die gemeinsame Objektflaeche zugeschnitten (nebeneinander, links alt, rechts neu).</summary>
        private void SaveCropPair(string file, Texture2D a, Texture2D b)
        {
            Mask(a, out _, out _);
            int w = a.width, h = a.height; int x0 = w, x1 = 0, y0 = h, y1 = 0;
            Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
            for (int i = 0; i < pa.Length; i++)
            {
                bool fa = !(pa[i].r > 200 && pa[i].g < 60 && pa[i].b > 200), fb = !(pb[i].r > 200 && pb[i].g < 60 && pb[i].b > 200);
                if (!fa && !fb) continue;
                int x = i % w, y = i / w; x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
            }
            if (x1 < x0) return;
            int m = 8; x0 = Mathf.Max(0, x0 - m); y0 = Mathf.Max(0, y0 - m); x1 = Mathf.Min(w - 1, x1 + m); y1 = Mathf.Min(h - 1, y1 + m);
            int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
            var o = new Texture2D(cw * 2 + 4, ch, TextureFormat.RGB24, false);
            var px = new Color32[o.width * o.height];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < o.width; x++)
                {
                    Color32 c = new Color32(40, 40, 38, 255);
                    if (x < cw) c = pa[(y0 + y) * w + x0 + x];
                    else if (x >= cw + 4) c = pb[(y0 + y) * w + x0 + x - cw - 4];
                    if (c.r > 200 && c.g < 60 && c.b > 200) c = new Color32(205, 214, 222, 255);
                    px[y * o.width + x] = c;
                }
            o.SetPixels32(px); o.Apply(false);
            File.WriteAllBytes(Path.Combine(_out, "Bilder", file + ".png"), o.EncodeToPNG());
            _report.images.Add("Bilder/" + file + ".png");
            Destroy(o);
        }

        // ------------------------------------------------------------------ LOD-Fahrten (Crossfade an/aus) + Pop-Metrik

        private IEnumerator LodDriveSuite()
        {
            env.ApplyPreset(FloraEnvironment.WindPreset.Leicht);
            foreach (var (area, fam) in new[] { (areaLineupF01, "F01"), (areaLineupF04, "F04a") })
            {
                Activate(area);
                LODGroup[] groups = area.GetComponentsInChildren<LODGroup>(true);
                foreach (bool fade in new[] { true, false })
                {
                    var modes = new LODFadeMode[groups.Length];
                    for (int i = 0; i < groups.Length; i++) { modes[i] = groups[i].fadeMode; if (!fade) groups[i].fadeMode = LODFadeMode.None; }
                    int frames = 300;
                    var pop = new PopResult { asset = fam + " Lineup (3 Archetypen)", mode = fade ? "Crossfade (Dither, animiert)" : "ohne Crossfade" };
                    Vector3 c = area.position;
                    yield return Clip($"{(fam == "F01" ? "02" : "04")}_{fam}_LOD-Fahrt_{(fade ? "Crossfade" : "ohne_Crossfade")}", frames, i =>
                    {
                        float d = Mathf.Lerp(12f, 250f, i / (float)(frames - 1));
                        Pose(c + new Vector3(0, 1.7f, -d), c + new Vector3(0, 8f, 0), 60f);
                    }, pop);
                    for (int i = 0; i < groups.Length; i++) groups[i].fadeMode = modes[i];
                }
                // Standbilder aus der Fahrt (gleiche Pose, normales Rendering)
                foreach (float d in new[] { 25f, 60f, 120f, 220f })
                {
                    Pose(area.position + new Vector3(0, 1.7f, -d), area.position + new Vector3(0, 8f, 0), 60f);
                    yield return Settle();
                    Still($"{(fam == "F01" ? "02" : "04")}_{fam}_LOD-Fahrt_{d:0}m");
                }
            }
        }

        // ------------------------------------------------------------------ LOD-Pop am Umschaltpunkt (Crossfade an/aus)

        /// <summary>
        /// Kamera faehrt langsam (8 m in 3 s) ueber den jeweiligen Umschaltabstand, Wind aus, Hintergrund ausgeblendet.
        /// Gemessen wird die groesste Bild-zu-Bild-Aenderung innerhalb der Baumflaeche (Luma 0..255): ohne Crossfade
        /// ein einzelner Sprung, mit Crossfade auf ~15 Bilder verteilt.
        /// </summary>
        private IEnumerator PopSuite()
        {
            env.ApplyPreset(FloraEnvironment.WindPreset.Windstille);
            yield return null;
            foreach (var (area, items) in new[] { (areaLineupF01, L.LineupF01Items), (areaLineupF04, L.LineupF04Items) })
            {
                Activate(area);
                for (int k = 0; k < items.Length; k++)
                {
                    for (int j = 0; j < items.Length; j++) Find(area, items[j]).gameObject.SetActive(j == k);
                    Transform tree = Find(area, items[k]); LODGroup g = tree.GetComponent<LODGroup>(); Bounds b = BoundsOf(tree);
                    LODFadeMode fm = g.fadeMode;
                    LOD[] lods = g.GetLODs();
                    // SetLODs setzt die (zur Laufzeit normierte) LOD-Groesse zurueck → nach jedem SetLODs wiederherstellen
                    float size0 = g.size;
                    Vector3 rp = g.transform.TransformPoint(g.localReferencePoint);
                    float sc = Mathf.Max(Mathf.Abs(g.transform.lossyScale.x), Mathf.Max(Mathf.Abs(g.transform.lossyScale.y), Mathf.Abs(g.transform.lossyScale.z)));
                    float kk = 2f * Mathf.Tan(30f * Mathf.Deg2Rad);
                    foreach (var (li, tr) in new[] { (0, "NEAR->MID"), (1, "MID->FAR"), (2, "FAR->VFAR") })
                    {
                        // tatsaechlicher Umschaltabstand dieser LODGroup (gleiche Formel wie Unity), Kamera auf Augenhoehe
                        float d = g.size * sc * QualitySettings.lodBias / (lods[li].screenRelativeTransitionHeight * kk);
                        float dy = rp.y - 1.7f;
                        // v4: zusaetzlich breiterer Crossfade fuer FAR->VFAR bei F04a (nur Diagnose, Standardzeilen bleiben vergleichbar)
                        // Micro-Pass: FAR->VFAR fuer beide Lineups mit breitem (×2) und sehr breitem (×3) Crossfade
                        int nVar = li == 2 ? 4 : 2;
                        for (int fv = 0; fv < nVar; fv++)
                        {
                            // Die LODGroups blenden zeitbasiert (animateCrossFading, Dauer global 0,5 s) – die Breite wirkt nicht.
                            // Micro-Pass: kleine Crossfade-Korrektur ueber die Dauer (1,0 s / 1,5 s), danach zurueck.
                            bool fade = fv != 1, wide = fv >= 2; float dur = fv == 3 ? 1.5f : 1.0f;
                            string wTag = fv == 3 ? "CF15s" : "CF10s", wName = fv == 3 ? " Crossfade 1,5 s" : " Crossfade 1,0 s";
                            g.fadeMode = fade ? fm : LODFadeMode.None;
                            float dur0 = LODGroup.crossFadeAnimationDuration;
                            if (wide) LODGroup.crossFadeAnimationDuration = dur;
                            Vector3 tgt = new Vector3(b.center.x, b.center.y * 0.75f, b.center.z);
                            float hz0 = Mathf.Sqrt(Mathf.Max(1f, (d - 4f) * (d - 4f) - dy * dy));
                            Pose(new Vector3(rp.x, 1.7f, rp.z - hz0), tgt, 60f);
                            yield return Settle();
                            Texture2D m0 = Render(640, 360, true, true, null);
                            bool[] mk = Mask(m0, out _, out _); int cov = 0; foreach (bool q in mk) if (q) cov++;
                            Destroy(m0);
                            CameraClearFlags cf = cam.clearFlags; Color bg = cam.backgroundColor;
                            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = KeyColor; SetGrounds(false);
                            var pop = new PopResult { asset = items[k], mode = tr + (wide ? wName : (fade ? " Crossfade" : " ohne Crossfade")) };
                            float fd = d;
                            yield return Clip($"14_Pop_{items[k]}_{tr.Replace("->", "-")}_{(wide ? wTag : (fade ? "CF" : "ohne"))}", 90, i =>
                            {
                                float dist = (fd - 4f) + 8f * i / 89f;
                                float hz = Mathf.Sqrt(Mathf.Max(1f, dist * dist - dy * dy));
                                Pose(new Vector3(rp.x, 1.7f, rp.z - hz), tgt, 60f);
                            }, pop);
                            cam.clearFlags = cf; cam.backgroundColor = bg; SetGrounds(true);
                            float frac = Mathf.Max(1e-4f, cov / (float)mk.Length);
                            pop.coverage = frac;
                            pop.jumpInTree = (pop.maxDiff - pop.medianDiff) / frac;
                            Log($"   Pop {items[k],-22} {pop.mode,-26} @{fd,5:0} m · Baumflaeche {frac * 100,5:0.0} % · groesster Sprung im Baum {pop.jumpInTree,6:0.0} Luma · Median {pop.medianDiff / frac,5:0.0} · Bild max {pop.maxDiff:0.000}");
                            LODGroup.crossFadeAnimationDuration = dur0;
                        }
                    }
                    g.fadeMode = fm; g.ForceLOD(-1);
                }
                for (int j = 0; j < items.Length; j++) Find(area, items[j]).gameObject.SetActive(true);
            }
            env.ApplyPreset(FloraEnvironment.WindPreset.Normal);
        }

        // ------------------------------------------------------------------ Micro-Pass: Blindvergleich + Crossfade-Kosten

        /// <summary>
        /// Blindclips aus normaler Spielkamera (Himmel, Boden, Augenhoehe 1,7 m, Annaeherung von d+15 auf d-25 m ueber 150 Bilder):
        /// je Baum Variante „Wechsel“ (breiter Crossfade) und „kein Wechsel“ (FAR erzwungen). Dateinamen neutral (A/B), Schluessel
        /// getrennt in Blind_Schluessel.txt. Danach GPU-Kosten des breiten Crossfades in den Uebersichtsszenen (Overdraw-Hotspot-Pruefung).
        /// </summary>
        private IEnumerator BlindCrossfadeSuite()
        {
            env.ApplyPreset(FloraEnvironment.WindPreset.Windstille);
            yield return null;
            var key = new System.Text.StringBuilder("Blindvergleich FAR->VFAR – Schluessel (erst nach der Bewertung oeffnen)\n");
            var rnd = new System.Random(20261002);   // Step E: neue Zuordnung, damit die alte Reihenfolge nicht wiedererkannt wird
            int n = 0;
            Quaternion sun0 = sun.transform.rotation;
            // Faelle: Restsprung F04a V1 (vorn) und die groessten Licht-Ausreisser der Kalibrierung (Seite / gedrehte Sonne)
            foreach (var (area, item, az, sunAz) in new[]
            {
                (areaLineupF04, "f04a_v1_bestand_s1", 0f, 0f), (areaLineupF04, "f04a_v3_windform_s1", 90f, 0f), (areaLineupF04, "f04a_v3_windform_s1", 0f, 240f),
                (areaLineupF01, "f01_v1_bestand_s1", 90f, 0f), (areaLineupF01, "f01_v2_solitaer_s1", 45f, 240f),
            })
            {
                sun.transform.rotation = Quaternion.AngleAxis(sunAz, Vector3.up) * sun0;
                Vector3 adir = Quaternion.AngleAxis(az, Vector3.up) * Vector3.back;
                Activate(area);
                string[] items = area == areaLineupF04 ? L.LineupF04Items : L.LineupF01Items;
                foreach (string it in items) Find(area, it).gameObject.SetActive(it == item);
                Transform tree = Find(area, item); LODGroup g = tree.GetComponent<LODGroup>(); Bounds b = BoundsOf(tree);
                LOD[] lods = g.GetLODs(); float w0 = lods[2].fadeTransitionWidth; float size0 = g.size;
                Vector3 rp = g.transform.TransformPoint(g.localReferencePoint);
                float sc = Mathf.Max(Mathf.Abs(g.transform.lossyScale.x), Mathf.Max(Mathf.Abs(g.transform.lossyScale.y), Mathf.Abs(g.transform.lossyScale.z)));
                float d = g.size * sc * QualitySettings.lodBias / (lods[2].screenRelativeTransitionHeight * 2f * Mathf.Tan(30f * Mathf.Deg2Rad));
                Vector3 tgt = new Vector3(b.center.x, b.center.y * 0.6f, b.center.z);
                n++;
                bool swapAB = rnd.Next(2) == 1;
                for (int v = 0; v < 2; v++)
                {
                    bool withSwitch = (v == 0) != swapAB;
                    string label = v == 0 ? "A" : "B";
                    float dur0 = LODGroup.crossFadeAnimationDuration; LODGroup.crossFadeAnimationDuration = 1.0f;
                    if (!withSwitch) g.ForceLOD(2); else g.ForceLOD(-1);
                    float fd = d; int vis3 = 0, vis2 = 0;
                    Renderer r3 = lods[3].renderers.Length > 0 ? lods[3].renderers[0] : null, r2 = lods[2].renderers.Length > 0 ? lods[2].renderers[0] : null;
                    yield return Clip($"17_Blind_{n}_{label}", 150, i =>
                    {
                        if (r3 != null && r3.isVisible) vis3++;
                        if (r2 != null && r2.isVisible) vis2++;
                        float dist = (fd + 15f) - 40f * i / 149f;
                        Vector3 cp = rp + adir * dist; cp.y = 1.7f;
                        Pose(cp, tgt, 60f);
                    }, null, 960, 540);
                    Log($"   Blind {n}{label}: {item} d {fd:0} m · Groesse {g.size:0.00} · Abstand Start {Vector3.Distance(rp + adir * (fd + 15f), rp):0} m · Bilder mit VFAR sichtbar {vis3} · mit FAR sichtbar {vis2} · Wechsel {(withSwitch ? "ja" : "nein")}");
                    key.AppendLine($"17_Blind_{n}_{label}: {item} · Kamera {az:0}° · Sonne +{sunAz:0}° · {(withSwitch ? "MIT Wechsel FAR→VFAR (Crossfade 1,0 s)" : "OHNE Wechsel (FAR erzwungen)")} · Umschaltung bei {d:0} m");
                    LODGroup.crossFadeAnimationDuration = dur0; g.ForceLOD(-1);
                }
                foreach (string it in items) Find(area, it).gameObject.SetActive(true);
            }
            sun.transform.rotation = sun0;
            File.WriteAllText(Path.Combine(_out, "Blind_Schluessel.txt"), key.ToString());
            Log($"   Blindclips: {n} Paare (17_Blind_*), Schluessel getrennt in Blind_Schluessel.txt");

            // Kosten des breiten Crossfades: alle Baeume FAR->VFAR ×2 gegen Standard, in den Uebersichten mit vielen fernen Baeumen
            env.ApplyPreset(FloraEnvironment.WindPreset.Normal);
            foreach (var (area, name, pos, look) in new[] { (areaStress, "D Stress Uebersicht", L.Stress + new Vector3(-300, 140, 330), L.Stress), (areaHabitat, "C Habitat Uebersicht", L.Habitat + HabOverPos, L.Habitat + HabOverTgt) })
            {
                Activate(area);
                var saved = new List<(LODGroup, LOD[], float)>();
                foreach (LODGroup lg in area.GetComponentsInChildren<LODGroup>(true)) { LOD[] l = lg.GetLODs(); if (l.Length == 4) saved.Add((lg, l, lg.size)); }
                float d0 = LODGroup.crossFadeAnimationDuration;
                for (int v = 0; v < 2; v++)
                {
                    LODGroup.crossFadeAnimationDuration = v == 0 ? d0 : 1.0f;
                    // Bewegung durch die Szene, damit laufend Uebergaenge stattfinden (stehende Kamera hat keine Fades)
                    Vector3 p0 = pos, fwd = (look - pos); fwd.y = 0; fwd = fwd.normalized;
                    Pose(p0, look);
                    yield return Measure("CF-Kosten " + name, v == 0 ? "Crossfade 0,5 s (Standard), Kamera faehrt" : "Crossfade 1,0 s, Kamera faehrt", M, t => { Pose(p0 + fwd * (t * 15f), look + fwd * (t * 15f)); return true; }, area);
                }
                LODGroup.crossFadeAnimationDuration = d0;
            }
        }

        // ------------------------------------------------------------------ Impostor (VERY FAR)

        private IEnumerator ImpostorSuite()
        {
            env.ApplyPreset(FloraEnvironment.WindPreset.Windstille);
            Quaternion sun0 = sun.transform.rotation;
            foreach (var (area, items) in new[] { (areaLineupF01, L.LineupF01Items), (areaLineupF04, L.LineupF04Items) })
            {
                Activate(area);
                for (int k = 0; k < items.Length; k++)
                {
                    for (int j = 0; j < items.Length; j++) Find(area, items[j]).gameObject.SetActive(j == k);
                    Transform tree = Find(area, items[k]); LODGroup g = tree.GetComponent<LODGroup>(); Bounds b = BoundsOf(tree);
                    LODFadeMode fm = g.fadeMode; g.fadeMode = LODFadeMode.None;
                    // Micro-Pass: volle Matrix Kamera (0/45/90/180/erhoeht 30/erhoeht 60) × Sonne (Standard/+120/+240)
                    var vl = new List<(string n, float az, float el, float sunAz)>();
                    foreach (var (vn, az, el) in new[] { ("vorn", 0f, 0f), ("schraeg45", 45f, 0f), ("seitlich90", 90f, 0f), ("hinten180", 180f, 0f), ("erhoeht30", 0f, 30f), ("erhoeht60", 0f, 60f) })
                        foreach (float sa in new[] { 0f, 120f, 240f })
                            vl.Add((sa == 0f ? vn : $"{vn}_L{sa:0}", az, el, sa));
                    var views = vl.ToArray();
                    foreach (var v in views)
                    {
                        sun.transform.rotation = Quaternion.AngleAxis(v.sunAz, Vector3.up) * sun0;
                        float d = 160f;
                        Vector3 dir = Quaternion.AngleAxis(v.az, Vector3.up) * (Quaternion.AngleAxis(v.el, Vector3.right) * Vector3.back);   // v3: Elevation nach oben (v1: versehentlich unter dem Horizont)
                        Vector3 tgt = new Vector3(b.center.x, b.center.y * 0.75f, b.center.z);
                        Pose(tgt + dir * d + (v.el == 0 ? new Vector3(0, 1.7f - tgt.y, 0) : Vector3.zero), tgt, 55f);
                        g.ForceLOD(2); yield return null; yield return null;
                        Texture2D ta = Render(1920, 1080, true, true, null);
                        g.ForceLOD(3); yield return null; yield return null;
                        Texture2D tb = Render(1920, 1080, true, true, null);
                        CompareMasks(items[k], "FAR->VFAR " + v.n, d, ta, tb);
                        if (v.n == "vorn" || v.n == "erhoeht30" || v.n == "seitlich90")
                        {
                            // Referenz: volle NEAR-Geometrie aus derselben Entfernung gegen Impostor (zeigt, welche Seite abweicht)
                            g.ForceLOD(0); yield return null; yield return null;
                            Texture2D tn = Render(1920, 1080, true, true, null);
                            CompareMasks(items[k], "NEAR->VFAR " + v.n, d, tn, tb);
                            CompareMasks(items[k], "NEAR->FAR " + v.n, d, tn, ta);
                            if (v.n == "vorn") SaveCropPair($"08_Referenz_NEAR-VFAR_{items[k]}_{v.n}", tn, tb);
                            Destroy(tn);
                        }
                        if (v.n == "vorn" || v.n == "seitlich90" || v.n == "seitlich90_L240" || v.n == "hinten180" || v.n == "erhoeht30" || v.n == "erhoeht60")
                            SaveCropPair($"08_Impostor_{items[k]}_{v.n}", ta, tb);
                        Destroy(ta); Destroy(tb);
                    }
                    sun.transform.rotation = sun0;
                    // Schattenbild (Boden sichtbar): FAR gegen Impostor aus 60 m, erhoeht
                    Pose(b.center + new Vector3(-35, 30, -45), new Vector3(b.center.x, 0, b.center.z + 8), 50f);
                    g.ForceLOD(2); yield return Settle(); Still($"08_Impostor_Schatten_{items[k]}_FAR", 960, 540);
                    g.ForceLOD(3); yield return Settle(); Still($"08_Impostor_Schatten_{items[k]}_VFAR", 960, 540);
                    g.ForceLOD(-1); g.fadeMode = fm;
                }
                for (int j = 0; j < items.Length; j++) Find(area, items[j]).gameObject.SetActive(true);
            }
            sun.transform.rotation = sun0;
        }

        // ------------------------------------------------------------------ Abnahmebilder

        private IEnumerator ScreenshotSuite()
        {
            env.ApplyPreset(FloraEnvironment.WindPreset.Leicht); env.season = 0; env.leafFall = 0; env.snow = 0;
            Quaternion sun0 = sun.transform.rotation;
            Activate(areaGroup);
            ThirdPerson(L.Group, Vector3.forward); yield return Settle(); Still("01_F01_ThirdPerson");
            Pose(L.Group + new Vector3(0, 1.7f, 0), L.Group + new Vector3(0, 7, 30)); yield return Settle(); Still("01b_F01_Augenhoehe");
            // Gegenlicht: Sonne gegenueber der Kamera, flach
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(0, -0.35f, -1f));
            Pose(L.Group + new Vector3(0, 1.7f, 0), L.Group + new Vector3(0, 7, 30)); yield return Settle(); Still("01c_F01_Gegenlicht");
            sun.transform.rotation = sun0;
            ThirdPerson(L.Group + new Vector3(22, 0, 22), new Vector3(0.3f, 0, 1)); yield return Settle(); Still("03_F04a_ThirdPerson");
            Pose(L.Group + new Vector3(22, 1.7f, 22), L.Group + new Vector3(32, 9, 48)); yield return Settle(); Still("03b_F04a_Augenhoehe");

            Activate(areaHero);
            for (int i = 0; i < 6; i++)
            {
                Bounds b = BoundsOf(Find(areaHero, L.HeroItems[i]));
                Pose(L.HeroPos(i) + new Vector3(0, 1.7f, -18), L.HeroPos(i) + new Vector3(0, b.size.y * 0.55f, 0)); yield return Settle();
                Still($"0{(i < 3 ? 1 : 3)}d_{L.HeroItems[i]}_18m");
            }
            // F19 nah/mittel: LOD-Prefab automatisch, volle Geometrie zum Vergleich, erzwungene Stufen
            int iT = Array.IndexOf(L.HeroItems, "f19_trunk"), iF = Array.IndexOf(L.HeroItems, "f19_trunk_full");
            foreach (float d in new[] { 4f, 12f, 25f })
            {
                Pose(L.HeroPos(iF) + new Vector3(0, 1.7f, -d), L.HeroPos(iF) + new Vector3(0, 2.5f, 0)); yield return Settle(); Still($"09_F19_Stamm_voll_{d:0}m");
                Pose(L.HeroPos(iT) + new Vector3(0, 1.7f, -d), L.HeroPos(iT) + new Vector3(0, 2.5f, 0)); yield return Settle(); Still($"09_F19_Stamm_LOD_auto_{d:0}m");
            }
            LODGroup gT = Find(areaHero, "f19_trunk").GetComponent<LODGroup>();
            for (int l = 0; l < 3; l++)
            {
                gT.ForceLOD(l); Pose(L.HeroPos(iT) + new Vector3(0, 1.7f, -6), L.HeroPos(iT) + new Vector3(0, 2.5f, 0)); yield return Settle();
                Still($"09_F19_Stamm_LOD{l}_6m");
            }
            gT.ForceLOD(-1);
            foreach (string n in new[] { "f19_wall", "f19_rock" })
            {
                int i = Array.IndexOf(L.HeroItems, n);
                foreach (float d in new[] { 6f, 18f })
                {
                    Pose(L.HeroPos(i) + new Vector3(0, 1.7f, -d), L.HeroPos(i) + new Vector3(0, 1.2f, 0)); yield return Settle(); Still($"09_{n}_{d:0}m");
                }
            }
            // F10 nah/mittel: volle Geometrie gegen LOD-Test
            int gH = Array.IndexOf(L.HeroItems, "f10_group_sommer_hero"), gL = Array.IndexOf(L.HeroItems, "f10_group_sommer");
            foreach (float d in new[] { 5f, 12f, 25f, 45f })
            {
                Pose(L.HeroPos(gH) + new Vector3(0, 1.7f, -d), L.HeroPos(gH) + new Vector3(0, 0.3f, 0)); yield return Settle(); Still($"10_F10_voll_{d:0}m");
                Pose(L.HeroPos(gL) + new Vector3(0, 1.7f, -d), L.HeroPos(gL) + new Vector3(0, 0.3f, 0)); yield return Settle(); Still($"10_F10_LOD_auto_{d:0}m");
            }
            LODGroup gF = Find(areaHero, "f10_group_sommer").GetComponent<LODGroup>();
            for (int l = 0; l < 3; l++)
            {
                gF.ForceLOD(l); Pose(L.HeroPos(gL) + new Vector3(0, 1.7f, -10), L.HeroPos(gL) + new Vector3(0, 0.3f, 0)); yield return Settle();
                Still($"10_F10_LOD{l}_10m");
            }
            gF.ForceLOD(-1);
            // Totholz
            foreach (string n in new[] { "dead_snag_A", "dead_log_B", "dead_stump_C" })
            {
                int i = Array.IndexOf(L.HeroItems, n);
                Pose(L.HeroPos(i) + new Vector3(3, 2.2f, -9), L.HeroPos(i) + new Vector3(0, 1.0f, 0)); yield return Settle(); Still($"13_Totholz_{n}");
            }
            // Waldrand / dichter Bestand / Lichtung
            Activate(areaHabitat);
            Pose(L.Habitat + HabEdgePos, L.Habitat + HabEdgeTgt); yield return Settle(); Still("05_Waldrand");
            Pose(L.Habitat + HabStandPos, L.Habitat + HabStandTgt); yield return Settle(); Still("06_dichter_Bestand");
            Pose(L.Habitat + HabClearPos, L.Habitat + HabClearTgt); yield return Settle(); Still("06b_Lichtung");
            Pose(L.Habitat + HabEyePos, L.Habitat + HabEyeTgt); yield return Settle(); Still("06c_Habitat_Augenhoehe");
            Pose(L.Habitat + HabOverPos, L.Habitat + HabOverTgt); yield return Settle(); Still("06d_Habitat_Uebersicht");
            Activate(areaStress);
            Pose(L.Stress + new Vector3(-300, 140, 330), L.Stress); yield return Settle(); Still("06e_Stresstest_Uebersicht");
        }

        // ------------------------------------------------------------------ Wind

        private IEnumerator WindSuite()
        {
            Activate(areaGroup);
            env.season = 0; env.leafFall = 0;
            Vector3 pos = L.Group + new Vector3(4, 1.7f, -2), tgt = L.Group + new Vector3(4, 8, 30);
            Pose(pos, tgt);
            var presets = new[] { FloraEnvironment.WindPreset.Windstille, FloraEnvironment.WindPreset.Leicht, FloraEnvironment.WindPreset.Normal, FloraEnvironment.WindPreset.Stark };
            foreach (var p in presets)
            {
                env.ApplyPreset(p); yield return Settle();
                Still($"07_Wind_{p}");
                var pop = new PopResult { asset = "Gruppe B", mode = "Wind " + p };
                yield return Clip($"07_Wind_{p}", 75, i => Pose(pos, tgt), pop);
            }
            env.ApplyPreset(FloraEnvironment.WindPreset.Normal);
        }

        // ------------------------------------------------------------------ Saison

        private IEnumerator SeasonSuite()
        {
            Activate(areaGroup);
            env.ApplyPreset(FloraEnvironment.WindPreset.Leicht);
            Vector3 pos = L.Group + new Vector3(0, 6, -18), tgt = L.Group + new Vector3(0, 7, 34);
            Pose(pos, tgt);
            var stages = new (string n, float s, float f, float snow)[]
            {
                ("1_Sommer", 0f, 0f, 0f), ("2_Fruehherbst", 0.32f, 0f, 0f), ("3_Hochherbst", 0.6f, 0.32f, 0f), ("4_Spaetherbst", 0.85f, 0.66f, 0f),
                ("5_Winter", 1f, 1.01f, 0f), ("6_Schnee-Test", 1f, 1.01f, 0.85f),
            };
            foreach (var st in stages)
            {
                env.season = st.s; env.leafFall = st.f; env.snow = st.snow; env.Push(); yield return Settle();
                Still("08_Saison_" + st.n);
            }
            yield return Clip("08_Herbstuebergang", 180, i =>
            {
                float t = i / 179f;
                env.season = Mathf.Clamp01(t * 1.25f); env.leafFall = Mathf.Clamp01((t - 0.3f) / 0.7f) * 1.01f; env.snow = 0; env.Push();
                Pose(pos, tgt);
            });
            env.season = 0; env.leafFall = 0; env.snow = 0; env.Push();
        }

        // ------------------------------------------------------------------ Overdraw

        private IEnumerator OverdrawSuite()
        {
            env.ApplyPreset(FloraEnvironment.WindPreset.Windstille);
            Activate(areaGroup); ThirdPerson(L.Group, Vector3.forward); yield return Settle(); Overdraw("B_Gruppe_Spielkamera", areaGroup);
            Activate(areaHabitat);
            Pose(L.Habitat + HabEyePos, L.Habitat + HabEyeTgt); yield return Settle(); Overdraw("C_Wiese_Augenhoehe", areaHabitat);
            Pose(L.Habitat + HabEdgePos, L.Habitat + HabEdgeTgt); yield return Settle(); Overdraw("C_Waldrand", areaHabitat);
            Pose(L.Habitat + HabStandPos, L.Habitat + HabStandTgt); yield return Settle(); Overdraw("C_im_Bestand", areaHabitat);
            Activate(areaHero);
            for (int i = 0; i < 6; i++)
            {
                Bounds b = BoundsOf(Find(areaHero, L.HeroItems[i]));
                Pose(L.HeroPos(i) + new Vector3(0, 1.7f, -18), L.HeroPos(i) + new Vector3(0, b.size.y * 0.55f, 0)); yield return Settle();
                Overdraw($"A_{L.HeroItems[i]}_18m", areaHero);
            }
            int gH = Array.IndexOf(L.HeroItems, "f10_group_sommer_hero"), gL = Array.IndexOf(L.HeroItems, "f10_group_sommer");
            LODGroup gF = Find(areaHero, "f10_group_sommer").GetComponent<LODGroup>();
            Pose(L.HeroPos(gH) + new Vector3(0, 1.7f, -6), L.HeroPos(gH) + new Vector3(0, 0.3f, 0)); yield return Settle(); Overdraw("F10_voll_6m", areaHero);
            for (int l = 0; l < 3; l++)
            {
                gF.ForceLOD(l); Pose(L.HeroPos(gL) + new Vector3(0, 1.7f, -6), L.HeroPos(gL) + new Vector3(0, 0.3f, 0)); yield return Settle();
                Overdraw($"F10_LOD{l}_6m", areaHero);
            }
            gF.ForceLOD(-1);
            int iT = Array.IndexOf(L.HeroItems, "f19_trunk"), iF = Array.IndexOf(L.HeroItems, "f19_trunk_full");
            LODGroup gT = Find(areaHero, "f19_trunk").GetComponent<LODGroup>();
            Pose(L.HeroPos(iF) + new Vector3(0, 1.7f, -5), L.HeroPos(iF) + new Vector3(0, 2.5f, 0)); yield return Settle(); Overdraw("F19_Stamm_voll_5m", areaHero);
            for (int l = 1; l < 3; l++)
            {
                gT.ForceLOD(l); Pose(L.HeroPos(iT) + new Vector3(0, 1.7f, -5), L.HeroPos(iT) + new Vector3(0, 2.5f, 0)); yield return Settle();
                Overdraw($"F19_Stamm_LOD{l}_5m", areaHero);
            }
            gT.ForceLOD(-1);
            foreach (var (area, name) in new[] { (areaF10Full, "F10_Feld_voll"), (areaF10Lod, "F10_Feld_LOD") })
            {
                Activate(area); Pose(area.position + new Vector3(-3, 1.7f, -3), area.position + new Vector3(42, 0.5f, 42)); yield return Settle(); Overdraw(name + "_Augenhoehe", area);
            }
            env.ApplyPreset(FloraEnvironment.WindPreset.Normal);
        }

        // ------------------------------------------------------------------ LOD-/Schatten-Debug

        private IEnumerator DebugViewSuite()
        {
            Activate(areaHabitat);
            foreach (var (n, p, t) in new[] { ("Habitat_Uebersicht", HabOverPos, HabOverTgt), ("Habitat_Augenhoehe", HabEyePos, HabEyeTgt), ("Habitat_Bestand", HabStandPos, HabStandTgt) })
            {
                Pose(L.Habitat + p, L.Habitat + t); yield return Settle();
                SwapDebug(areaHabitat, 1); Still("12_LOD-Farben_" + n); RestoreMaterials();
            }
            Activate(areaStress);
            Pose(L.Stress + new Vector3(-300, 140, 330), L.Stress); yield return Settle();
            SwapDebug(areaStress, 1); Still("12_LOD-Farben_Stresstest"); RestoreMaterials();
            Activate(areaLineupF04);
            Pose(areaLineupF04.position + new Vector3(0, 1.7f, -90), areaLineupF04.position + new Vector3(0, 8, 0)); yield return Settle();
            SwapDebug(areaLineupF04, 1); Still("12_LOD-Farben_F04a_90m"); RestoreMaterials();
        }

        // ------------------------------------------------------------------ Schnelle Bewegung durch den Bestand

        private IEnumerator FastMoveSuite()
        {
            Activate(areaHabitat);
            env.ApplyPreset(FloraEnvironment.WindPreset.Normal);
            Vector3 a = L.Habitat + new Vector3(-6, 1.8f, 30), b = L.Habitat + new Vector3(40, 1.8f, -70);
            Vector3 dir = (b - a).normalized; float len = Vector3.Distance(a, b);
            float speed = 15f;
            yield return Measure("C Bewegung", "15 m/s durch den Bestand (Messung)", len / speed, t =>
            {
                Vector3 p = a + dir * Mathf.Min(len, t * speed);
                Pose(p, p + dir * 20 + Vector3.up * 1.5f);
                return true;
            }, areaHabitat);
            var pop = new PopResult { asset = "Habitat C", mode = "Bewegung 15 m/s" };
            int frames = Mathf.RoundToInt(len / speed * 30f);
            yield return Clip("09_Bewegung_15ms_durch_Bestand", frames, i =>
            {
                Vector3 p = a + dir * Mathf.Min(len, i / 30f * speed);
                Pose(p, p + dir * 20 + Vector3.up * 1.5f);
            }, pop);
        }

        // ------------------------------------------------------------------ Speicher

        private IEnumerator MemorySuite()
        {
            Activate(_areas.ToArray());
            yield return null;
            var meshes = new HashSet<Mesh>(); var texs = new HashSet<Texture>();
            var famMesh = new Dictionary<string, long>(); var famTex = new Dictionary<string, long>();
            foreach (MeshFilter mf in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                Mesh m = mf.sharedMesh; if (m == null || !meshes.Add(m)) continue;
                string fam = Family(m.name);
                famMesh[fam] = (famMesh.TryGetValue(fam, out long v) ? v : 0) + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m);
            }
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    foreach (string p in new[] { "_BaseMap", "_BumpMap", "_NormalAtlas" })
                    {
                        if (!mat.HasProperty(p)) continue;
                        Texture t = mat.GetTexture(p); if (t == null || !texs.Add(t)) continue;
                        string fam = t.name.StartsWith("T_FV_oct") ? "impostor" : "textures";
                        famTex[fam] = (famTex.TryGetValue(fam, out long v) ? v : 0) + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                    }
                }
            float total = 0, gfx = 0, tex = 0, mesh = 0;
            using (var rT = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory"))
            using (var rG = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory"))
            using (var rX = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory"))
            using (var rM = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Mesh Memory"))
            {
                yield return null; yield return null;
                total = rT.Valid ? rT.LastValue / 1048576f : -1; gfx = rG.Valid ? rG.LastValue / 1048576f : -1;
                tex = rX.Valid ? rX.LastValue / 1048576f : -1; mesh = rM.Valid ? rM.LastValue / 1048576f : -1;
            }
            foreach (var kv in famMesh)
                _report.memory.Add(new MemoryResult { group = "Meshes " + kv.Key, meshBytes = kv.Value });
            foreach (var kv in famTex)
                _report.memory.Add(new MemoryResult { group = "Texturen " + kv.Key, textureBytes = kv.Value });
            _report.memory.Add(new MemoryResult { group = "Prozess gesamt (alle Bereiche aktiv)", meshes = meshes.Count, textures = texs.Count, totalUsedMB = total, gfxUsedMB = gfx, textureMB = tex, meshMB = mesh });
            foreach (var kv in famMesh) Log($"   Meshes {kv.Key,-12} {kv.Value / 1048576f,7:0.0} MB");
            foreach (var kv in famTex) Log($"   Texturen {kv.Key,-10} {kv.Value / 1048576f,7:0.0} MB");
            Log($"   {meshes.Count} Meshes · {texs.Count} Texturen · Prozess {total:0} MB · Gfx {gfx:0} MB · Texturspeicher {tex:0} MB · Meshspeicher {mesh:0} MB");
            Activate(areaHero);
        }

        private static string Family(string n)
        {
            if (n.StartsWith("SM_FV_")) n = n.Substring(6);
            if (n.StartsWith("f01")) return "F01";
            if (n.StartsWith("f04a")) return "F04a";
            if (n.StartsWith("f19")) return "F19";
            if (n.StartsWith("f10")) return "F10";
            if (n.StartsWith("dead")) return "Totholz";
            return "sonstige";
        }
    }
}
