using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ashveil.FloraValidation
{
    /// <summary>
    /// Final Impostor Lighting Micro-Pass (Max 01.10., Option 2). Nur die VERY-FAR-Beleuchtung wird neu kalibriert, alles andere
    /// bleibt aus der v4-Kalibrierung eingefroren (calibration_v4_original.json). Testreihenfolge nach Vorgabe, nach jedem Schritt
    /// gemessen:
    ///   A  unbeleuchtete Base Color (nur Gain)
    ///   B  + richtungsneutrales AO (v4-Werte)
    ///   C  + Kronen-Grossnormale (Ellipsoid aus der FAR-Belaubung, Anteil kalibriert)
    ///   D  + Dicke-Term fuer die Transluzenz (aus derselben Ellipsoid-Sehne)
    ///   E  alternative Ansichtsdarstellung – nur, falls D nicht reicht (wird dann nur gemeldet)
    /// Pruefmatrix: Kamera vorn/45°/90°/180°/erhoeht 30° × Sonne Standard/+120°/+240° (15 Bedingungen, Ziel |ΔL| ≤ 10 %),
    /// erhoeht 60° × 3 Sonnen getrennt (High-Angle). Primaeres Format 1024 (Vorgabe §13), 2048 zum Vergleich.
    /// </summary>
    public partial class FloraValidationRunner
    {
        private static readonly string[] V5Views = { "vorn", "schraeg45", "seitlich90", "hinten180", "erhoeht30", "erhoeht60" };
        private static readonly string[] V5Suns = { "", "_L120", "_L240" };
        private static string[] _v5Conds;
        private static string[] V5Conds
        {
            get
            {
                if (_v5Conds != null) return _v5Conds;
                var l = new List<string>();
                foreach (string v in V5Views) foreach (string s in V5Suns) l.Add(v + s);
                return _v5Conds = l.ToArray();
            }
        }
        private static bool V5Primary(string c) => !c.StartsWith("erhoeht60");

        private IEnumerator CondPose5(Bounds b, string c, Quaternion sun0)
        {
            float az = c.EndsWith("_L120") ? 120f : (c.EndsWith("_L240") ? 240f : 0f);
            string view = c.Replace("_L120", "").Replace("_L240", "");
            sun.transform.rotation = Quaternion.AngleAxis(az, Vector3.up) * sun0;
            if (view == "hinten180")
            {
                Vector3 tgt = new Vector3(b.center.x, b.center.y * 0.75f, b.center.z);
                Vector3 pos = tgt + Vector3.forward * 160f; pos.y = 1.7f; Pose(pos, tgt, 55f);
            }
            else ViewPose(b, 160, view);
            yield break;
        }

        private IEnumerator CalibrationSuiteV5()
        {
            string src = Ashveil.Core.ProjectPaths.SourceArt("FloraValidation_v2", "calibration_v3.json");
            string orig = Ashveil.Core.ProjectPaths.SourceArt("FloraValidation_v2", "calibration_v4_original.json");
            if (!File.Exists(orig) && File.Exists(src)) File.Copy(src, orig);
            _cal = File.Exists(orig) ? JsonUtility.FromJson<FloraCalibrationData>(File.ReadAllText(orig)) : new FloraCalibrationData();
            File.WriteAllText(Path.Combine(_out, "calibration_v4_eingefroren.json"), JsonUtility.ToJson(_cal, true));
            _cal.created = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " (Micro-Pass auf Basis v4)";
            _cal.rows.Clear();
            _cal.notes.Add("Step E (01.10.): Billboard-Spiegelung im Impostor-Shader behoben; Impostor-Beleuchtung auf dieser Basis neu kalibriert (Schritte A–D wie Micro-Pass); alles andere aus v4 eingefroren.");
            if (areaCalib == null) throw new Exception("Kalibrierbereich fehlt – Szene neu bauen (Menue 2)");
            Activate(areaCalib);
            env.ApplyPreset(FloraEnvironment.WindPreset.Windstille); env.season = 0; env.leafFall = 0; env.snow = 0; env.Push();
            yield return null;
            foreach (string t in CalTrees)
            {
                Transform grp = areaCalib.Find("FV_Cal_" + t); if (grp == null) continue;
                float s = t.StartsWith("f01") ? _cal.mipF01 : _cal.mipF04a;
                foreach (Transform c in grp) SetProp(c.gameObject, "_MipAlpha", s, IsCard);
            }
            // eingefrorene FAR4-Farbe der Windform auf das Kalibrierobjekt (Referenz fuer den Impostor)
            foreach (TintCal tc in _cal.tints)
                if (tc.asset == "f04a_v3_windform_s1_far4")
                {
                    Transform g = areaCalib.Find("FV_Cal_f04a_v3_windform_s1");
                    GameObject f4 = g != null ? Child(g, "cal_f04a_v3_windform_s1_far4") : null;
                    if (f4 != null) { MulColor(f4, new Vector3(tc.r, tc.g, tc.b), IsCard); if (tc.cutoff > 0) SetProp(f4, "_Cutoff", tc.cutoff, IsCard); }
                }
            yield return Step("Micro-Pass: Impostor-Beleuchtung", CalImpostorsV5());
            yield return Step("Kalibrierung D: Speicher", CalMemory());
            string json = JsonUtility.ToJson(_cal, true);
            File.WriteAllText(src, json);
            File.WriteAllText(Path.Combine(_out, "calibration_v5.json"), json);
            Log("Kalibrierung Micro-Pass geschrieben: " + src);
        }

        private struct V5Stat { public float mean, max, iou, maxHigh; public string worst; }

        private IEnumerator CalImpostorsV5()
        {
            Quaternion sun0 = sun.transform.rotation;
            var frozen = new Dictionary<string, ImpostorCal>();
            foreach (ImpostorCal ic in _cal.impostors) frozen[ic.asset] = ic;
            _cal.impostors.Clear();
            string[] conds = V5Conds;
            var sum1k = new List<V5Stat>(); var sum2k = new List<V5Stat>();
            foreach (string t in CalTrees)
            {
                Transform grp = areaCalib.Find("FV_Cal_" + t); if (grp == null) continue;
                GameObject near = Child(grp, $"cal_{t}_near"), imp2 = Child(grp, $"cal_{t}_vfar4"), imp1 = Child(grp, $"cal_{t}_vfar4k");
                if (imp2 == null) { Log("   vfar4 fehlt: " + t); continue; }
                GameObject P = imp1 != null ? imp1 : imp2;   // Vorgabe §13: 1024 als Teststandard
                GameObject reff = (t == "f04a_v3_windform_s1" && _cal.useFar4) ? Child(grp, $"cal_{t}_far4") : Child(grp, $"cal_{t}_far");
                frozen.TryGetValue($"{t}_vfar4", out ImpostorCal f4);
                if (f4 == null) f4 = new ImpostorCal { asset = $"{t}_vfar4", mipAlpha = t.StartsWith("f01") ? _cal.mipF01 : _cal.mipF04a };
                Only(grp, near); yield return null;
                Bounds b = BoundsOf(near.transform);
                var refs = new Texture2D[conds.Length];
                for (int k = 0; k < conds.Length; k++) { yield return CondPose5(b, conds[k], sun0); yield return Shot(grp, reff, x => refs[k] = x); }
                sun.transform.rotation = sun0;

                float tr0 = f4.translucency >= 0 ? f4.translucency : 0.18f;
                float sm0 = f4.smoothness >= 0 ? f4.smoothness : 0.1f;
                void Apply(GameObject o, float g, float aoS, float aoD, float nb, float tr, float sm, float mb, float tk, float cut, float sk = 0f, float sh = 0f)
                {
                    if (o == null) return;
                    SetProp(o, "_MipAlpha", f4.mipAlpha, IsImp); SetProp(o, "_OctGain", g, IsImp);
                    SetProp(o, "_AOStrength", aoS, IsImp); SetProp(o, "_AODirect", aoD, IsImp); SetProp(o, "_NormalBlend", nb, IsImp);
                    SetProp(o, "_Translucency", tr, IsImp); SetProp(o, "_Smoothness", sm, IsImp); SetProp(o, "_OctBlendSharp", Mathf.Max(1f, f4.blendSharp), IsImp);
                    SetProp(o, "_MacroBlend", mb, IsImp); SetProp(o, "_ThickK", tk, IsImp); SetProp(o, "_SelfK", sk, IsImp); SetProp(o, "_Sheen", sh, IsImp);
                    if (cut > 0) SetProp(o, "_Cutoff", cut, IsImp);
                }

                // Alpha-Schwelle fuer das 1024-Format (2048 behaelt den v4-Wert); Flaechenabgleich vorn/seitlich/erhoeht 30
                Apply(P, f4.gain, f4.aoStrength, f4.aoDirect, f4.normalBlend, tr0, sm0, 0, 0, f4.cutoff);
                float cutP = f4.cutoff;
                if (P == imp1)
                {
                    float lo = 0.08f, hi = 0.92f;
                    for (int it = 0; it < 7; it++)
                    {
                        float c = (lo + hi) * 0.5f; SetProp(P, "_Cutoff", c, IsImp);
                        float dA = 0;
                        foreach (string cn in new[] { "vorn", "seitlich90", "erhoeht30" })
                        {
                            int k = Array.IndexOf(conds, cn);
                            yield return CondPose5(b, cn, sun0); Texture2D ti = null; yield return Shot(grp, P, x => ti = x);
                            dA += Compare(refs[k], ti).dArea; Destroy(ti);
                        }
                        if (dA > 0) lo = c; else hi = c;
                    }
                    cutP = (lo + hi) * 0.5f;
                }
                sun.transform.rotation = sun0;

                // Bewertung: Ziel = Ø|ΔL| + 0,5 × max|ΔL| ueber die 15 Bodenbedingungen + leichte Gain-Strafe
                V5Stat st = default;
                IEnumerator Eval(GameObject o)
                {
                    float s = 0, mx = 0, io = 0, mh = 0; int n = 0; string w = "";
                    for (int k = 0; k < conds.Length; k++)
                    {
                        yield return CondPose5(b, conds[k], sun0); Texture2D ti = null; yield return Shot(grp, o, x => ti = x);
                        M3 m = Compare(refs[k], ti); Destroy(ti);
                        float a = Mathf.Abs(m.dLum);
                        if (V5Primary(conds[k])) { s += a; io += m.iou; n++; if (a > mx) { mx = a; w = conds[k]; } }
                        else mh = Mathf.Max(mh, a);
                    }
                    sun.transform.rotation = sun0;
                    st = new V5Stat { mean = s / Mathf.Max(1, n), max = mx, iou = io / Mathf.Max(1, n), maxHigh = mh, worst = w };
                }
                float Obj(V5Stat x, float g) => x.mean + 0.5f * x.max + 0.05f * Mathf.Abs(Mathf.Log(g));   // Gain-Strafe: Ziel ≈ 1 (Kriterium C)
                // Gain-Anpassung (3 Schritte) – Verhaeltnis ueber die Bodenbedingungen, vorn doppelt gewichtet
                float gFit = 1f;
                IEnumerator FitGain(GameObject o, float g0)
                {
                    float g = g0, bestO = 9; float bestG = g0; V5Stat bestS = default;
                    for (int it = 0; it < 3; it++)
                    {
                        SetProp(o, "_OctGain", g, IsImp);
                        double ratio = 0, wsum = 0;
                        for (int k = 0; k < conds.Length; k++)
                        {
                            if (!V5Primary(conds[k])) continue;
                            yield return CondPose5(b, conds[k], sun0); Texture2D ti = null; yield return Shot(grp, o, x => ti = x);
                            M3 m = Compare(refs[k], ti); Destroy(ti);
                            float w = conds[k] == "vorn" ? 2f : 1f; ratio += w * m.lumA / Mathf.Max(1f, m.lumB); wsum += w;
                        }
                        sun.transform.rotation = sun0;
                        yield return Eval(o);
                        float ob = Obj(st, g);
                        if (ob < bestO) { bestO = ob; bestG = g; bestS = st; }
                        g = Mathf.Clamp(g * Mathf.Pow((float)(ratio / wsum), 2.2f), 0.4f, 4f);
                    }
                    SetProp(o, "_OctGain", bestG, IsImp); gFit = bestG; st = bestS;
                }
                void LogStep(string step, float g, string p)
                {
                    Log($"     {t,-22} Schritt {step,-34} Gain {g:0.00} · Boden Ø|ΔL| {st.mean * 100,5:0.0} % · max {st.max * 100,5:0.0} % ({st.worst}) · Ø IoU {st.iou:0.000} · erhoeht 60° max {st.maxHigh * 100,5:0.0} % · {p}");
                }

                // A · nur unbeleuchtete Base Color
                Apply(P, 1f, 0, 0, 0, 0.18f, 0.1f, 0, 0, cutP);
                yield return FitGain(P, 1f); V5Stat sA = st; float gA = gFit;
                LogStep("A Base Color", gA, "AO aus, Normal-Atlas, Transluzenz 0,18");
                // B · + richtungsneutrales AO (v4-Kalibrierung)
                Apply(P, f4.gain, f4.aoStrength, f4.aoDirect, f4.normalBlend, tr0, sm0, 0, 0, cutP);
                yield return FitGain(P, f4.gain); V5Stat sB = st; float gB = gFit;
                LogStep("B + AO (v4)", gB, $"AO {f4.aoStrength:0.0}/{f4.aoDirect:0.00} nb {f4.normalBlend:0.0} tr {tr0:0.00} gl {sm0:0.0}");
                float bG = gB, bMb = 0, bTk = 0, bAoD = f4.aoDirect, bTr = tr0; V5Stat best = sB; float bestO = Obj(sB, gB); string reached = "B";
                // C · Normal-Atlas: (1) Zug zur Kronen-Grossnormale (Lauf 1/2: verschlechtert, daher nur 0,15 als Beleg),
                //     (2) Himmelsglanz streifender Karten aus dem Normal je Ansicht, ggf. ohne Normal-Daempfung zur Blickrichtung
                float bSk = 0f, bSh = 0f, bNb = f4.normalBlend;
                if (best.max > 0.10f)
                {
                    Apply(P, bG, f4.aoStrength, f4.aoDirect, f4.normalBlend, tr0, sm0, 0.15f, 0, cutP);
                    yield return FitGain(P, bG);
                    LogStep("C Grossnormale 0,15", gFit, "");
                    { float o = Obj(st, gFit); if (o < bestO - 0.002f) { bestO = o; best = st; bG = gFit; bMb = 0.15f; reached = "C"; } }
                    foreach (float sh in new[] { 0.5f, 1f, 2f, 3f })
                        foreach (float nb in (f4.normalBlend > 0f ? new[] { f4.normalBlend, 0f } : new[] { 0f }))
                        {
                            Apply(P, bG, f4.aoStrength, f4.aoDirect, nb, tr0, sm0, bMb, 0, cutP, 0f, sh);
                            yield return FitGain(P, bG);
                            LogStep($"C Himmelsglanz {sh:0.0} Normal-Daempfung {nb:0.0}", gFit, $"Grossnormale {bMb:0.00}");
                            float o = Obj(st, gFit);
                            if (o < bestO - 0.002f) { bestO = o; best = st; bG = gFit; bSh = sh; bNb = nb; reached = "C"; }
                        }
                }
                else Log($"     {t}: nach B bereits ≤ 10 % – C/D nicht noetig");
                // D · Dicke/Transmission ueber das Kronenvolumen: Eigenschatten (Lichtweg zur aktuellen Sonne) + Daempfung der Transluzenz
                if (best.max > 0.10f)
                {
                    foreach (float sk in new[] { 0f, 0.5f, 1f, 2f })
                        foreach (float tk in new[] { 0f, 1f })
                            foreach (float aoD in (f4.aoDirect > 0f ? new[] { 0f, f4.aoDirect } : new[] { 0f }))
                            {
                                if (sk == 0f && tk == 0f && aoD == bAoD) continue;
                                Apply(P, bG, f4.aoStrength, aoD, bNb, tr0, sm0, bMb, tk, cutP, sk, bSh);
                                yield return FitGain(P, bG);
                                LogStep($"D Eigenschatten {sk:0.0} Transm.-Dicke {tk:0} AOdir {aoD:0.00}", gFit, $"Glanz {bSh:0.0}");
                                float o = Obj(st, gFit);
                                if (o < bestO - 0.002f) { bestO = o; best = st; bG = gFit; bTk = tk; bSk = sk; bAoD = aoD; reached = "D"; }
                            }
                }
                if (best.max > 0.10f) Log($"     {t}: nach D weiterhin max {best.max * 100:0.0} % ({best.worst}) – Schritt E (andere Ansichtsdarstellung) waere noetig, im Micro-Pass nicht umgesetzt");

                // Endstand auf beide Formate, Matrix protokollieren
                Apply(P, bG, f4.aoStrength, bAoD, bNb, bTr, sm0, bMb, bTk, cutP, bSk, bSh);
                for (int k = 0; k < conds.Length; k++)
                {
                    yield return CondPose5(b, conds[k], sun0); Texture2D ti = null; yield return Shot(grp, P, x => ti = x);
                    M3 m = Compare(refs[k], ti);
                    Row("M5 FAR-VFAR " + (P == imp1 ? "1024" : "2048"), t, $"g {bG:0.00} gl {bSh:0.0} nb {bNb:0.0} gn {bMb:0.00} es {bSk:0.0} dk {bTk:0} aoD {bAoD:0.00}", conds[k], 160, m, bG);
                    if (conds[k] == "vorn" || conds[k] == "seitlich90" || conds[k] == "seitlich90_L240" || conds[k] == "hinten180" || conds[k] == "erhoeht60")
                        SaveCropPair($"v5cal_Impostor_{t}_{conds[k]}", refs[k], ti);
                    Destroy(ti);
                }
                sun.transform.rotation = sun0;
                yield return Eval(P); sum1k.Add(st);
                if (imp1 != null)
                {
                    Apply(imp2, bG, f4.aoStrength, bAoD, bNb, bTr, sm0, bMb, bTk, f4.cutoff, bSk, bSh);
                    yield return Eval(imp2); sum2k.Add(st);
                    Log($"     {t,-22} 2048 zum Vergleich: Boden Ø|ΔL| {st.mean * 100:0.0} % · max {st.max * 100:0.0} % · Ø IoU {st.iou:0.000}");
                }
                foreach (var x in refs) Destroy(x);
                var icN = new ImpostorCal
                {
                    asset = $"{t}_vfar4", cutoff = cutP, gain = bG, mipAlpha = f4.mipAlpha, normalBlend = bNb, translucency = bTr,
                    signX = f4.signX, signZ = f4.signZ, aoStrength = f4.aoStrength, aoDirect = bAoD, blendSharp = f4.blendSharp, smoothness = sm0,
                    macroBlend = bMb, thickK = bTk, selfK = bSk, sheen = bSh,
                };
                _cal.impostors.Add(icN);
                Log($"   Micro-Pass {t}: erreicht bis Schritt {reached} · A Ø/max {sA.mean * 100:0.0}/{sA.max * 100:0.0} % → B {sB.mean * 100:0.0}/{sB.max * 100:0.0} % → final {best.mean * 100:0.0}/{best.max * 100:0.0} % ({best.worst}) · Gain {bG:0.00} · Himmelsglanz {bSh:0.0} · Normal-Daempfung {bNb:0.0} · Grossnormale {bMb:0.00} · Eigenschatten {bSk:0.0} · Transm.-Dicke {bTk:0} · Transluzenz {bTr:0.00} · AO direkt {bAoD:0.00} · erhoeht 60° max {best.maxHigh * 100:0.0} %");
            }
            sun.transform.rotation = sun0;
            float Avg(List<V5Stat> l, Func<V5Stat, float> f) { if (l.Count == 0) return 0; float a = 0; foreach (var q in l) a += f(q); return a / l.Count; }
            _cal.useVfar4 = _cal.impostors.Count > 0;
            bool use1k = sum2k.Count == 0 || (Avg(sum1k, x => x.mean) <= Avg(sum2k, x => x.mean) + 0.01f && Avg(sum1k, x => x.iou) >= Avg(sum2k, x => x.iou) - 0.02f);
            _cal.impostorMaxSize = use1k ? 1024 : 2048;
            _cal.impostorBc = true;
            Log($"   Micro-Pass gesamt: 1024 Boden Ø|ΔL| {Avg(sum1k, x => x.mean) * 100:0.0} % (Ø max {Avg(sum1k, x => x.max) * 100:0.0} %), IoU {Avg(sum1k, x => x.iou):0.000} · 2048 Ø|ΔL| {Avg(sum2k, x => x.mean) * 100:0.0} %, IoU {Avg(sum2k, x => x.iou):0.000} → Format {(use1k ? 1024 : 2048)}");
            _cal.notes.Add($"Micro-Pass: Format {(use1k ? 1024 : 2048)}");
        }
    }
}
