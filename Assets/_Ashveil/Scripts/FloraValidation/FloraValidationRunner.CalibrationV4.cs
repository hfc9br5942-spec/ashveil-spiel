using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ashveil.FloraValidation
{
    /// <summary>
    /// v4-Sondertest (Max 01.10., Option B): Kalibrierung NUR der Restfaelle. Bestandene v3-Werte (Mip-Alpha F01/F04a,
    /// F10-LOD0/Farben, Impostor-Format) werden aus calibration_v3.json uebernommen und nicht neu optimiert.
    ///   A3  F04a V3 Windform: FAR4 (positionsgebundene Cluster) gegen FAR v2 – Alpha-Schwelle (Flaeche) + Farbe gegen MID
    ///   B4  Impostor v4 (unbeleuchteter Bake + richtungsneutrales AO): Schwelle, Rohzustand (Gain 1), Licht-/AO-Kalibrierung,
    ///       Schaerfe der Ansichtsueberblendung, 1024-Vergleich; Blickwinkel 0/45/90/180/erhoeht 30/60 + Sonne 120/240
    /// </summary>
    public partial class FloraValidationRunner
    {
        private static readonly string[] V4Conds = { "vorn", "schraeg45", "seitlich90", "hinten180", "erhoeht30", "licht120", "licht240", "erhoeht60" };
        private const int V4ObjConds = 7;   // erhoeht60 wird gemessen, aber nicht optimiert (Sonderfall Flug/Bergblick)

        private IEnumerator CalibrationSuiteV4()
        {
            string src = Ashveil.Core.ProjectPaths.SourceArt("FloraValidation_v2", "calibration_v3.json");
            // v4b: eingefrorene v3-Basis bleibt bei Wiederholungen unveraendert (calibration_v3_original.json)
            string orig = Ashveil.Core.ProjectPaths.SourceArt("FloraValidation_v2", "calibration_v3_original.json");
            if (!File.Exists(orig) && File.Exists(src)) File.Copy(src, orig);
            string basis = File.Exists(orig) ? orig : src;
            _cal = File.Exists(basis) ? JsonUtility.FromJson<FloraCalibrationData>(File.ReadAllText(basis)) : new FloraCalibrationData();
            _cal.tints.RemoveAll(x => x.asset != null && x.asset.EndsWith("_far4"));
            if (!File.Exists(src)) Log("   WARNUNG: keine v3-Kalibrierung gefunden – v4 startet ohne eingefrorene Werte");
            File.WriteAllText(Path.Combine(_out, "calibration_v3_eingefroren.json"), JsonUtility.ToJson(_cal, true));
            _cal.created = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " (v4 auf Basis v3)";
            _cal.rows.Clear();
            _cal.notes.Add("v4: A (Mip-Alpha), C (F10) und D-Format aus v3 eingefroren; neu: A3 FAR4 Windform, B4 Impostor v4.");
            if (areaCalib == null) throw new Exception("Kalibrierbereich fehlt – Szene neu bauen (Menue 2)");
            Activate(areaCalib);
            env.ApplyPreset(FloraEnvironment.WindPreset.Windstille); env.season = 0; env.leafFall = 0; env.snow = 0; env.Push();
            yield return null;
            // eingefrorene Mip-Werte auf die Kalibrierobjekte
            foreach (string t in CalTrees)
            {
                Transform grp = areaCalib.Find("FV_Cal_" + t); if (grp == null) continue;
                float s = t.StartsWith("f01") ? _cal.mipF01 : _cal.mipF04a;
                foreach (Transform c in grp) SetProp(c.gameObject, "_MipAlpha", s, IsCard);
            }
            Log($"   eingefroren: F01 mip {_cal.mipF01:0.00} · F04a mip {_cal.mipF04a:0.00} · F10 LOD0 {_cal.f10Lod0} · Impostor-Format {_cal.impostorMaxSize}");
            yield return Step("v4 A3: F04a Windform FAR4", CalFar4());
            yield return Step("v4 B4: Impostor v4 (Bake, AO, Blickwinkel)", CalImpostorsV4());
            yield return Step("Kalibrierung D: Speicher", CalMemory());
            string json = JsonUtility.ToJson(_cal, true);
            File.WriteAllText(src, json);
            File.WriteAllText(Path.Combine(_out, "calibration_v4.json"), json);
            Log("Kalibrierung v4 geschrieben: " + src);
        }

        // ---------------- A3
        private IEnumerator CalFar4()
        {
            const string t = "f04a_v3_windform_s1";
            Transform grp = areaCalib.Find("FV_Cal_" + t);
            if (grp == null) { Log("   fehlt: " + t); yield break; }
            GameObject near = Child(grp, $"cal_{t}_near"), mid = Child(grp, $"cal_{t}_mid"), far = Child(grp, $"cal_{t}_far"), far4 = Child(grp, $"cal_{t}_far4");
            if (far4 == null) { Log("   FAR4 fehlt"); yield break; }
            Only(grp, near); yield return null;
            Bounds b = BoundsOf(near.transform);
            var res = new Dictionary<string, float>();
            foreach (string view in new[] { "vorn", "seitlich90" })
            {
                ViewPose(b, 80, view);
                yield return PairCo(grp, mid, far, view == "vorn" ? "v4cal_Windform_MID-FAR_v2" : null);
                Row("A3 MID-FAR v2", t, $"far mip {_cal.mipF04a:0.00}", view, 80, _last); res["far|" + view] = _last.iou;
            }
            // Alpha-Schwelle der FAR4-Karten: Flaeche gegen MID (vorn + seitlich)
            float lo = 0.2f, hi = 0.9f;
            for (int it = 0; it < 7; it++)
            {
                float c = (lo + hi) * 0.5f; SetProp(far4, "_Cutoff", c, IsCard);
                float dA = 0;
                foreach (string view in new[] { "vorn", "seitlich90" }) { ViewPose(b, 80, view); yield return PairCo(grp, mid, far4); dA += _last.dArea; }
                if (dA > 0) lo = c; else hi = c;
            }
            float cut = (lo + hi) * 0.5f; SetProp(far4, "_Cutoff", cut, IsCard);
            // v4b: Feinsuche der Schwelle um den Flaechenabgleich auf hoechste mittlere IoU (|ΔFlaeche| <= 6 %)
            {
                float cBest = cut, iBest = -1;
                foreach (float dc in new[] { -0.12f, -0.08f, -0.04f, 0f, 0.04f, 0.08f, 0.12f })
                {
                    float c = Mathf.Clamp(cut + dc, 0.1f, 0.95f); SetProp(far4, "_Cutoff", c, IsCard);
                    float iS = 0, aS = 0;
                    foreach (string view in new[] { "vorn", "seitlich90" }) { ViewPose(b, 80, view); yield return PairCo(grp, mid, far4); iS += _last.iou; aS = Mathf.Max(aS, Mathf.Abs(_last.dArea)); }
                    Log($"     FAR4 Schwelle {c:0.00}: Ø IoU {iS / 2:0.000} · max |ΔFläche| {aS * 100:0.0} %");
                    if (aS <= 0.06f && iS > iBest) { iBest = iS; cBest = c; }
                }
                cut = cBest; SetProp(far4, "_Cutoff", cut, IsCard);
            }
            // Farbe gegen MID (je Kanal, gedaempft, bester Stand bleibt)
            Vector3 kk = Vector3.one, kBest = Vector3.one; float eBest = 9;
            for (int it = 0; it < 6; it++)
            {
                ViewPose(b, 80, "vorn"); yield return PairCo(grp, mid, far4);
                float e = Mathf.Abs(_last.dLum);
                if (e < eBest) { eBest = e; kBest = kk; }
                if (e < 0.01f) break;
                Vector3 k = new Vector3(Rat(_last.rgbA.x, _last.rgbB.x), Rat(_last.rgbA.y, _last.rgbB.y), Rat(_last.rgbA.z, _last.rgbB.z));
                MulColor(far4, k, IsCard); kk = Vector3.Scale(kk, k);
            }
            { Vector3 back = new Vector3(kBest.x / kk.x, kBest.y / kk.y, kBest.z / kk.z); MulColor(far4, back, IsCard); kk = kBest; }
            foreach (string view in new[] { "vorn", "seitlich90" })
            {
                ViewPose(b, 80, view);
                yield return PairCo(grp, mid, far4, view == "vorn" ? "v4cal_Windform_MID-FAR4" : null);
                Row("A3 MID-FAR4", t, $"far4 Schwelle {cut:0.00}", view, 80, _last, cut); res["far4|" + view] = _last.iou;
            }
            ViewPose(b, 160, "vorn"); yield return PairCo(grp, near, far4);
            Row("A3 NEAR-FAR4", t, "far4", "vorn", 160, _last, cut);
            bool use = res["far4|vorn"] >= 0.80f || res["far4|vorn"] > res["far|vorn"] + 0.01f;
            _cal.useFar4 = use;
            if (use)
                for (int sd = 1; sd <= 6; sd++)
                    _cal.tints.Add(new TintCal { asset = $"f04a_v3_windform_s{sd}_far4", material = "needle", r = kk.x, g = kk.y, b = kk.z, cutoff = cut });
            Log($"   Windform MID->FAR IoU vorn: FAR v2 {res["far|vorn"]:0.00} -> FAR4 {res["far4|vorn"]:0.00} (seitlich {res["far|seitlich90"]:0.00} -> {res["far4|seitlich90"]:0.00}) · Schwelle {cut:0.00} · Farbe ×({kk.x:0.00}, {kk.y:0.00}, {kk.z:0.00}) -> {(use ? "FAR4" : "FAR v2")}");
            _cal.notes.Add($"A3: Windform FAR4 {(use ? "uebernommen" : "verworfen")} (IoU vorn {res["far|vorn"]:0.00} -> {res["far4|vorn"]:0.00})");
            if (!use) far4.SetActive(false);
        }

        // ---------------- B4
        private IEnumerator CalImpostorsV4()
        {
            Quaternion sun0 = sun.transform.rotation;
            _cal.impostors.Clear();
            var iou2k = new List<float>(); var iou1k = new List<float>(); var lum2k = new List<float>(); var lum1k = new List<float>();
            var raw = new List<float>(); var gains = new List<float>();
            foreach (string t in CalTrees)
            {
                Transform grp = areaCalib.Find("FV_Cal_" + t); if (grp == null) continue;
                bool f04 = t.StartsWith("f04a");
                float s = f04 ? _cal.mipF04a : _cal.mipF01;
                GameObject near = Child(grp, $"cal_{t}_near"), imp = Child(grp, $"cal_{t}_vfar4"), imp1k = Child(grp, $"cal_{t}_vfar4k");
                if (imp == null) { Log("   vfar4 fehlt: " + t); continue; }
                GameObject reff = (t == "f04a_v3_windform_s1" && _cal.useFar4) ? Child(grp, $"cal_{t}_far4") : Child(grp, $"cal_{t}_far");
                Only(grp, near); yield return null;
                Bounds b = BoundsOf(near.transform);
                var refs = new Texture2D[V4Conds.Length];
                for (int k = 0; k < V4Conds.Length; k++) { yield return CondPose(b, V4Conds[k], sun0); yield return Shot(grp, reff, x => refs[k] = x); sun.transform.rotation = sun0; }
                foreach (GameObject o in new[] { imp, imp1k })
                {
                    if (o == null) continue;
                    SetProp(o, "_MipAlpha", s, IsImp); SetProp(o, "_OctGain", 1f, IsImp); SetProp(o, "_AOStrength", 1f, IsImp); SetProp(o, "_AODirect", 0f, IsImp);
                    SetProp(o, "_NormalBlend", 0f, IsImp); SetProp(o, "_OctBlendSharp", 1f, IsImp);
                }
                // Alpha-Schwelle: Flaechenabgleich gegen FAR (vorn, seitlich, erhoeht 30)
                float lo = 0.08f, hi = 0.92f;
                for (int it = 0; it < 7; it++)
                {
                    float c = (lo + hi) * 0.5f; SetProp(imp, "_Cutoff", c, IsImp);
                    float dA = 0;
                    foreach (int k in new[] { 0, 2, 4 }) { yield return CondPose(b, V4Conds[k], sun0); Texture2D ti = null; yield return Shot(grp, imp, x => ti = x); sun.transform.rotation = sun0; dA += Compare(refs[k], ti).dArea; Destroy(ti); }
                    if (dA > 0) lo = c; else hi = c;
                }
                float cut = (lo + hi) * 0.5f; SetProp(imp, "_Cutoff", cut, IsImp);
                // Rohzustand: Gain 1, AO nur Umgebung, keine Licht-Zusatzparameter (Beleg fuer den Bake)
                float rawErr = 0;
                for (int k = 0; k < V4Conds.Length; k++)
                {
                    yield return CondPose(b, V4Conds[k], sun0); Texture2D ti = null; yield return Shot(grp, imp, x => ti = x); sun.transform.rotation = sun0;
                    M3 m = Compare(refs[k], ti); Destroy(ti);
                    Row("B4 roh (Gain 1)", t, "vfar4", V4Conds[k], 160, m, cut);
                    if (k == 0) rawErr = m.dLum;
                }
                raw.Add(rawErr);
                // Licht-/AO-Kalibrierung (Gain weiterhin frei, Ziel ≈ 1)
                float tr0 = GetProp(imp, "_Translucency", IsImp, 0.18f);
                float bestErr = 9, bNb = 0, bTr = tr0, bG = 1f, bAoS = 1f, bAoD = 0f;
                foreach (float nb in new[] { 0f, 0.3f })
                    foreach (float tr in new[] { tr0, tr0 * 1.8f })
                        foreach (float aoS in new[] { 0.5f, 1f })
                            foreach (float aoD in new[] { 0f, 0.35f, 0.7f })
                            {
                                SetProp(imp, "_NormalBlend", nb, IsImp); SetProp(imp, "_Translucency", tr, IsImp);
                                SetProp(imp, "_AOStrength", aoS, IsImp); SetProp(imp, "_AODirect", aoD, IsImp);
                                float g = 1f;
                                for (int it = 0; it < 3; it++)
                                {
                                    SetProp(imp, "_OctGain", g, IsImp);
                                    double ratio = 0, wsum = 0; float errV = 0, errSum = 0;
                                    for (int k = 0; k < V4ObjConds; k++)
                                    {
                                        yield return CondPose(b, V4Conds[k], sun0); Texture2D ti = null; yield return Shot(grp, imp, x => ti = x); sun.transform.rotation = sun0;
                                        M3 m = Compare(refs[k], ti); Destroy(ti);
                                        float w = k == 0 ? 2f : 1f; ratio += w * m.lumA / Mathf.Max(1f, m.lumB); wsum += w;
                                        errSum += Mathf.Abs(m.dLum); if (k == 0) errV = Mathf.Abs(m.dLum);
                                    }
                                    ratio /= wsum;
                                    float err = errV + errSum / V4ObjConds + 0.02f * Mathf.Abs(Mathf.Log(g));   // leichte Strafe fuer Gain-Kompensation
                                    if (err < bestErr) { bestErr = err; bNb = nb; bTr = tr; bG = g; bAoS = aoS; bAoD = aoD; }
                                    g = Mathf.Clamp(g * Mathf.Pow((float)ratio, 2.2f), 0.4f, 4f);
                                }
                            }
                SetProp(imp, "_NormalBlend", bNb, IsImp); SetProp(imp, "_Translucency", bTr, IsImp); SetProp(imp, "_OctGain", bG, IsImp);
                SetProp(imp, "_AOStrength", bAoS, IsImp); SetProp(imp, "_AODirect", bAoD, IsImp);
                // v4b: Glanz (Smoothness) wie das FAR-Laubmaterial (Himmelsreflex an flachen Winkeln, Seitenansichten)
                float sm0 = GetProp(imp, "_Smoothness", IsImp, 0.1f), bSm = sm0;
                foreach (float sm in new[] { 0.2f, 0.3f })
                {
                    SetProp(imp, "_Smoothness", sm, IsImp);
                    float g = bG;
                    for (int it = 0; it < 3; it++)
                    {
                        SetProp(imp, "_OctGain", g, IsImp);
                        double ratio = 0, wsum = 0; float errV = 0, errSum = 0;
                        for (int k = 0; k < V4ObjConds; k++)
                        {
                            yield return CondPose(b, V4Conds[k], sun0); Texture2D ti = null; yield return Shot(grp, imp, x => ti = x); sun.transform.rotation = sun0;
                            M3 m = Compare(refs[k], ti); Destroy(ti);
                            float w = k == 0 ? 2f : 1f; ratio += w * m.lumA / Mathf.Max(1f, m.lumB); wsum += w;
                            errSum += Mathf.Abs(m.dLum); if (k == 0) errV = Mathf.Abs(m.dLum);
                        }
                        ratio /= wsum;
                        float err = errV + errSum / V4ObjConds + 0.02f * Mathf.Abs(Mathf.Log(g));
                        Log($"     {t} Glanz {sm:0.0} Gain {g:0.00}: Fehler {err:0.000}");
                        if (err < bestErr) { bestErr = err; bG = g; bSm = sm; }
                        g = Mathf.Clamp(g * Mathf.Pow((float)ratio, 2.2f), 0.4f, 4f);
                    }
                }
                SetProp(imp, "_Smoothness", bSm, IsImp); SetProp(imp, "_OctGain", bG, IsImp);
                // Schaerfe der Ansichtsueberblendung: hoechste mittlere IoU gegen FAR, Helligkeit darf nicht schlechter werden
                float bSharp = 1f, bIou = -1f, bLum = 9f;
                foreach (float sh in new[] { 1f, 2f, 3f })
                {
                    SetProp(imp, "_OctBlendSharp", sh, IsImp);
                    float iouS = 0, lumS = 0;
                    for (int k = 0; k < V4ObjConds; k++)
                    {
                        yield return CondPose(b, V4Conds[k], sun0); Texture2D ti = null; yield return Shot(grp, imp, x => ti = x); sun.transform.rotation = sun0;
                        M3 m = Compare(refs[k], ti); Destroy(ti); iouS += m.iou; lumS += Mathf.Abs(m.dLum);
                    }
                    iouS /= V4ObjConds; lumS /= V4ObjConds;
                    Log($"     {t} Ansichtsueberblendung Schaerfe {sh:0}: Ø IoU {iouS:0.000} · Ø |ΔL| {lumS * 100:0.0} %");
                    if (bIou < 0 || (iouS > bIou + 0.003f && lumS <= bLum + 0.005f)) { bIou = iouS; bLum = lumS; bSharp = sh; }
                }
                SetProp(imp, "_OctBlendSharp", bSharp, IsImp);
                var ic = new ImpostorCal { asset = $"{t}_vfar4", cutoff = cut, gain = bG, mipAlpha = s, normalBlend = bNb, translucency = bTr, aoStrength = bAoS, aoDirect = bAoD, blendSharp = bSharp, smoothness = bSm };
                _cal.impostors.Add(ic); gains.Add(bG);
                float iA = 0, lA = 0;
                for (int k = 0; k < V4Conds.Length; k++)
                {
                    yield return CondPose(b, V4Conds[k], sun0); Texture2D ti = null; yield return Shot(grp, imp, x => ti = x); sun.transform.rotation = sun0;
                    M3 m = Compare(refs[k], ti);
                    Row("B4 FAR-VFAR4 kal.", t, $"g {bG:0.00} ao {bAoS:0.0}/{bAoD:0.00} nb {bNb:0.0} tr {bTr:0.00} sh {bSharp:0} gl {bSm:0.0}", V4Conds[k], 160, m, bG);
                    if (k == 0) SaveCropPair($"v4cal_Impostor_{t}_vorn", refs[k], ti);
                    if (V4Conds[k] == "seitlich90") SaveCropPair($"v4cal_Impostor_{t}_seitlich90", refs[k], ti);
                    if (V4Conds[k] == "erhoeht60") SaveCropPair($"v4cal_Impostor_{t}_erhoeht60", refs[k], ti);
                    if (k < V4ObjConds) { iA += m.iou; lA += Mathf.Abs(m.dLum); }
                    Destroy(ti);
                }
                iou2k.Add(iA / V4ObjConds); lum2k.Add(lA / V4ObjConds);
                // 1024-Variante mit denselben Werten
                if (imp1k != null)
                {
                    foreach (var (p, v) in new[] { ("_Cutoff", cut), ("_OctGain", bG), ("_NormalBlend", bNb), ("_Translucency", bTr), ("_AOStrength", bAoS), ("_AODirect", bAoD), ("_OctBlendSharp", bSharp) })
                        SetProp(imp1k, p, v, IsImp);
                    float i1 = 0, l1 = 0;
                    for (int k = 0; k < V4ObjConds; k++)
                    {
                        yield return CondPose(b, V4Conds[k], sun0); Texture2D ti = null; yield return Shot(grp, imp1k, x => ti = x); sun.transform.rotation = sun0;
                        M3 m = Compare(refs[k], ti); Destroy(ti); i1 += m.iou; l1 += Mathf.Abs(m.dLum);
                    }
                    iou1k.Add(i1 / V4ObjConds); lum1k.Add(l1 / V4ObjConds);
                    Row("B4 1024 Ø", t, "vfar4k", "Ø7", 160, new M3 { iou = i1 / V4ObjConds, dLum = l1 / V4ObjConds }, 1024);
                }
                foreach (var x in refs) Destroy(x);
                Log($"   Impostor v4 {t}: Schwelle {cut:0.00} · roh ΔL vorn {rawErr * 100:+0.0;-0.0} % · Gain {bG:0.00} · AO Umgebung {bAoS:0.0} / direkt {bAoD:0.00} · Normal-Daempfung {bNb:0.0} · Transluzenz {bTr:0.00} · Ueberblendung {bSharp:0} · Glanz {bSm:0.0}");
            }
            sun.transform.rotation = sun0;
            float Mean(List<float> l) { if (l.Count == 0) return 0; float a = 0; foreach (float q in l) a += q; return a / l.Count; }
            _cal.useVfar4 = _cal.impostors.Count > 0;
            bool use1k = iou1k.Count == iou2k.Count && iou1k.Count > 0 && Mean(iou1k) >= Mean(iou2k) - 0.02f && Mean(lum1k) <= Mean(lum2k) + 0.01f;
            _cal.impostorMaxSize = use1k ? 1024 : 2048;
            _cal.impostorBc = true;
            Log($"   Impostor v4: roh |ΔL vorn| Ø {Mean(raw.ConvertAll(x => Mathf.Abs(x))) * 100:0.0} % · Gain Ø {Mean(gains):0.00} (v3: 1,9–3,0) · Ø IoU 2048 {Mean(iou2k):0.000} / 1024 {Mean(iou1k):0.000} · Ø |ΔL| 2048 {Mean(lum2k) * 100:0.0} % / 1024 {Mean(lum1k) * 100:0.0} % -> {(use1k ? "1024" : "2048")}");
            _cal.notes.Add($"B4: Impostor v4, Gain Ø {Mean(gains):0.00}, Format {(use1k ? 1024 : 2048)}");
        }
    }
}
