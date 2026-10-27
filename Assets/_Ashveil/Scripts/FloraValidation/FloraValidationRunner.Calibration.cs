using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ashveil.FloraValidation
{
    /// <summary>
    /// v3-Kalibrierlauf (Menue „4 v3-Kalibrierung“): misst Kandidaten direkt unter Unity-/URP-Licht und schreibt
    /// calibration_v3.json (SourceArt). Geprueft werden nur die freigegebenen v3-Punkte:
    ///   A  Alpha-Schaerfung je Mip (F01/F04a) und F04a FAR v2 gegen FAR v3 (MID->FAR, Masse gegen NEAR)
    ///   B  Impostor: alt (quadratisch) / v3 2048 / v3 1024 – Alpha-Schwelle (Flaechenabgleich) und Helligkeit (Gain) gegen FAR
    ///   C  F10: LOD0-Varianten gegen volle v2-Gruppe, Karten-Farbabgleich (LOD1/LOD2) und Alpha-Schwelle
    ///   D  Impostor-Speicher (Formate, Groessen)
    /// Materialaenderungen nur an Laufzeitkopien (Renderer.materials) – keine Asset-Aenderung.
    /// </summary>
    public partial class FloraValidationRunner
    {
        public Transform areaCalib;

        private FloraCalibrationData _cal;
        private readonly Dictionary<Renderer, Material[]> _calMats = new Dictionary<Renderer, Material[]>();
        private static readonly string[] CalTrees =
            { "f01_v1_bestand_s1", "f01_v2_solitaer_s1", "f01_v3_altbaum_s1", "f04a_v1_bestand_s1", "f04a_v2_freistand_s1", "f04a_v3_windform_s1" };
        private static readonly float[] MipCandidates = { 0f, 0.2f, 0.38f, 0.55f, 0.75f };
        private static readonly string[] ImpViews = { "vorn", "seitlich90", "erhoeht30" };

        private struct M3 { public float iou, dLum, dArea, lumA, lumB; public Vector3 rgbA, rgbB; }
        private M3 _last;

        // ------------------------------------------------------------------ Hilfen

        private Material[] Mats(Renderer r)
        {
            if (!_calMats.TryGetValue(r, out Material[] m)) { m = r.materials; _calMats[r] = m; }
            return m;
        }

        private static bool IsCard(Material m) => m != null && m.shader != null && m.shader.name.Contains("FloraLit") && m.IsKeywordEnabled("_ALPHATEST_ON");
        private static bool IsImp(Material m) => m != null && m.shader != null && m.shader.name.Contains("FloraImpostor");
        private static bool IsLit(Material m) => m != null && m.shader != null && m.shader.name.Contains("FloraLit");

        private void SetProp(GameObject go, string prop, float v, Func<Material, bool> f)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in Mats(r))
                    if (f(m) && m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        private float GetProp(GameObject go, string prop, Func<Material, bool> f, float def)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in Mats(r))
                    if (f(m) && m.HasProperty(prop)) return m.GetFloat(prop);
            return def;
        }

        private void MulColor(GameObject go, Vector3 k, Func<Material, bool> f)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in Mats(r))
                    if (f(m) && m.HasProperty("_BaseColor"))
                    {
                        Color c = m.GetColor("_BaseColor");
                        m.SetColor("_BaseColor", new Color(c.r * k.x, c.g * k.y, c.b * k.z, c.a));
                    }
        }

        private Vector3 GetColor(GameObject go, Func<Material, bool> f)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in Mats(r))
                    if (f(m) && m.HasProperty("_BaseColor")) { Color c = m.GetColor("_BaseColor"); return new Vector3(c.r, c.g, c.b); }
            return Vector3.one;
        }

        private void Only(Transform grp, GameObject keep)
        {
            // Nur diese Kalibriergruppe aktiv (andere Gruppen koennten sonst aus seitlichen/fernen Ansichten ins Bild ragen)
            if (areaCalib != null && grp.parent == areaCalib)
                foreach (Transform g in areaCalib) if (g.name != FloraValidationLayout.GroundName && g.gameObject.activeSelf != (g == grp)) g.gameObject.SetActive(g == grp);
            foreach (Transform c in grp) if (c.name != FloraValidationLayout.GroundName) c.gameObject.SetActive(c.gameObject == keep);
        }

        private void SetVec(GameObject go, string prop, Vector4 v)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in Mats(r))
                    if (IsImp(m) && m.HasProperty(prop)) m.SetVector(prop, v);
        }

        /// <summary>Ansicht/Licht fuer die Impostor-Lichtkalibrierung: Blickrichtungen auf Augenhoehe/erhoeht oder Sonne gedreht.</summary>
        private IEnumerator CondPose(Bounds b, string c, Quaternion sun0)
        {
            if (c == "licht120") sun.transform.rotation = Quaternion.AngleAxis(120, Vector3.up) * sun0;
            else if (c == "licht240") sun.transform.rotation = Quaternion.AngleAxis(240, Vector3.up) * sun0;
            if (c == "hinten180")
            {
                Vector3 tgt = new Vector3(b.center.x, b.center.y * 0.75f, b.center.z);
                Vector3 pos = tgt + Vector3.forward * 160f; pos.y = 1.7f; Pose(pos, tgt, 55f);
            }
            else ViewPose(b, 160, c.StartsWith("licht") ? "vorn" : c);
            yield break;
        }

        private static GameObject Child(Transform grp, string name)
        {
            Transform t = grp.Find(name);
            return t != null ? t.gameObject : null;
        }

        /// <summary>Kameraposition: vorn / seitlich90 auf Augenhoehe, erhoeht30 = 30° ueber dem Horizont (v1-Fehler korrigiert: Elevation nach oben).</summary>
        private void ViewPose(Bounds b, float d, string view, float fov = 55f)
        {
            float az = view == "seitlich90" ? 90f : (view == "schraeg45" ? 45f : (view == "hinten180" ? 180f : 0f));
            float el = view == "erhoeht30" ? 30f : (view == "erhoeht60" ? 60f : 0f);
            Vector3 tgt = new Vector3(b.center.x, b.center.y * 0.75f, b.center.z);
            Vector3 dir = Quaternion.AngleAxis(az, Vector3.up) * (Quaternion.AngleAxis(el, Vector3.right) * Vector3.back);
            Vector3 pos = tgt + dir * d;
            if (el == 0f) pos.y = 1.7f;
            Pose(pos, tgt, fov);
        }

        private static M3 Compare(Texture2D a, Texture2D b)
        {
            Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
            int inter = 0, uni = 0, ca = 0, cb = 0; double la = 0, lb = 0; Vector3 sa = Vector3.zero, sb = Vector3.zero;
            for (int i = 0; i < pa.Length; i++)
            {
                Color32 x = pa[i], y = pb[i];
                bool ma = !(x.r > 200 && x.g < 60 && x.b > 200), mb = !(y.r > 200 && y.g < 60 && y.b > 200);
                if (ma && mb) inter++;
                if (ma || mb) uni++;
                if (ma) { ca++; la += 0.2126 * x.r + 0.7152 * x.g + 0.0722 * x.b; sa += new Vector3(x.r, x.g, x.b); }
                if (mb) { cb++; lb += 0.2126 * y.r + 0.7152 * y.g + 0.0722 * y.b; sb += new Vector3(y.r, y.g, y.b); }
            }
            var r = new M3();
            r.iou = uni > 0 ? (float)inter / uni : 0f;
            r.lumA = ca > 0 ? (float)(la / ca) : 0f; r.lumB = cb > 0 ? (float)(lb / cb) : 0f;
            r.dLum = r.lumA > 0 ? r.lumB / r.lumA - 1f : 0f;
            r.dArea = ca > 0 ? (float)(cb - ca) / ca : 0f;
            r.rgbA = ca > 0 ? sa / ca : Vector3.zero; r.rgbB = cb > 0 ? sb / cb : Vector3.zero;
            return r;
        }

        private IEnumerator Shot(Transform grp, GameObject o, Action<Texture2D> got)
        {
            Only(grp, o);
            yield return null; yield return null;
            got(Render(1920, 1080, true, true, null));
        }

        private IEnumerator PairCo(Transform grp, GameObject a, GameObject b, string save = null)
        {
            Texture2D ta = null, tb = null;
            yield return Shot(grp, a, t => ta = t);
            yield return Shot(grp, b, t => tb = t);
            _last = Compare(ta, tb);
            if (save != null) SaveCropPair(save, ta, tb);
            Destroy(ta); Destroy(tb);
        }

        private void Row(string step, string asset, string variant, string view, float d, M3 m, float value = 0f)
        {
            _cal.rows.Add(new CalRow { step = step, asset = asset, variant = variant, view = view, distance = d, iou = m.iou, dLum = m.dLum, dArea = m.dArea, value = value });
            Log($"   {step,-12} {asset,-22} {variant,-26} {view,-10} @{d,4:0} m · IoU {m.iou:0.00} · ΔHelligkeit {m.dLum * 100:+0.0;-0.0} % · ΔFläche {m.dArea * 100:+0.0;-0.0} %{(value != 0 ? $" · Wert {value:0.###}" : "")}");
        }

        // ------------------------------------------------------------------ Ablauf

        private IEnumerator CalibrationSuite()
        {
            _cal = new FloraCalibrationData { created = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            if (areaCalib == null) throw new Exception("Kalibrierbereich fehlt – Szene neu bauen (Menue 2)");
            Activate(areaCalib);
            env.ApplyPreset(FloraEnvironment.WindPreset.Windstille); env.season = 0; env.leafFall = 0; env.snow = 0; env.Push();
            yield return null;
            yield return Step("Kalibrierung A: Mip-Alpha + F04a FAR", CalTreesLod());
            yield return Step("Kalibrierung B: Impostor", CalImpostors());
            yield return Step("Kalibrierung C: F10", CalF10());
            yield return Step("Kalibrierung D: Speicher", CalMemory());
            string json = JsonUtility.ToJson(_cal, true);
            string dst = Ashveil.Core.ProjectPaths.SourceArt("FloraValidation_v2", "calibration_v3.json");
            File.WriteAllText(dst, json);
            File.WriteAllText(Path.Combine(_out, "calibration_v3.json"), json);
            Log("Kalibrierung geschrieben: " + dst);
        }

        // ---------------- A
        private IEnumerator CalTreesLod()
        {
            // Ergebnis je (Baum, s, Variante)
            var mf = new Dictionary<string, float>(); var nm = new Dictionary<string, float>(); var nfA = new Dictionary<string, float>();
            var nmL = new Dictionary<string, float>(); var mfL = new Dictionary<string, float>();
            foreach (string t in CalTrees)
            {
                Transform grp = areaCalib.Find("FV_Cal_" + t);
                if (grp == null) { Log("   fehlt: " + t); continue; }
                GameObject near = Child(grp, $"cal_{t}_near"), mid = Child(grp, $"cal_{t}_mid"), far = Child(grp, $"cal_{t}_far"), far3 = Child(grp, $"cal_{t}_far3");
                Only(grp, near); yield return null;
                Bounds b = BoundsOf(near.transform);
                foreach (float s in MipCandidates)
                {
                    foreach (GameObject o in new[] { near, mid, far, far3 }) if (o != null) SetProp(o, "_MipAlpha", s, IsCard);
                    ViewPose(b, 35, "vorn");
                    yield return PairCo(grp, near, mid); nm[$"{t}|{s}"] = _last.iou; nmL[$"{t}|{s}"] = _last.dLum; Row("A NEAR-MID", t, $"mip {s:0.00}", "vorn", 35, _last, s);
                    foreach (GameObject f in new[] { far, far3 })
                    {
                        if (f == null) continue;
                        string v = f == far ? "far" : "far3";
                        ViewPose(b, 80, "vorn");
                        yield return PairCo(grp, mid, f, s == 0f || s == 0.38f ? $"v3cal_{t}_MID-{v}_mip{s:0.00}" : null);
                        mf[$"{t}|{s}|{v}"] = _last.iou; mfL[$"{t}|{s}|{v}"] = _last.dLum; Row("A MID-FAR", t, $"{v} mip {s:0.00}", "vorn", 80, _last, s);
                        ViewPose(b, 160, "vorn");
                        yield return PairCo(grp, near, f); nfA[$"{t}|{s}|{v}"] = _last.dArea; Row("A NEAR-FAR", t, $"{v} mip {s:0.00}", "vorn", 160, _last, s);
                    }
                }
            }
            // Entscheidung F04a: groesste minimale MID->FAR-IoU; NEAR->MID darf nicht schlechter werden (Toleranz 0,02)
            float best = -9; float bestS = 0; string bestV = "far";
            foreach (float s in MipCandidates)
                foreach (string v in new[] { "far", "far3" })
                {
                    float minMf = 9, nmPen = 0, aPen = 0; int n = 0;
                    foreach (string t in CalTrees)
                    {
                        if (!t.StartsWith("f04a") || !mf.ContainsKey($"{t}|{s}|{v}")) continue;
                        minMf = Mathf.Min(minMf, mf[$"{t}|{s}|{v}"]);
                        nmPen += Mathf.Max(0, nm[$"{t}|0"] - nm[$"{t}|{s}"] - 0.02f);
                        aPen += Mathf.Abs(nfA[$"{t}|{s}|{v}"]); n++;
                    }
                    if (n == 0) continue;
                    float score = minMf - nmPen - 0.05f * aPen / n;
                    Log($"   F04a Kandidat mip {s:0.00} {v}: min MID->FAR IoU {minMf:0.00} · Masse |ΔFläche NEAR->FAR| Ø {aPen / n * 100:0} % · Score {score:0.000}");
                    if (score > best) { best = score; bestS = s; bestV = v; }
                }
            _cal.mipF04a = bestS; _cal.useFar3 = bestV == "far3";
            // A2: FAR v3 (Mindestbreite, dichtere Reihen) ist zu massig -> Alpha-Schwelle der Fernkarte so, dass die Flaeche zu MID passt
            float minChosen = 9, minTrim = 9; var trimCut = new Dictionary<string, float>();
            foreach (string t in CalTrees)
            {
                if (!t.StartsWith("f04a")) continue;
                Transform grp = areaCalib.Find("FV_Cal_" + t); if (grp == null) continue;
                GameObject near = Child(grp, $"cal_{t}_near"), mid = Child(grp, $"cal_{t}_mid"), f3 = Child(grp, $"cal_{t}_far3");
                if (f3 == null) continue;
                minChosen = Mathf.Min(minChosen, mf[$"{t}|{bestS}|{bestV}"]);
                foreach (GameObject o in new[] { near, mid, f3 }) SetProp(o, "_MipAlpha", bestS, IsCard);
                Only(grp, near); yield return null;
                Bounds b = BoundsOf(near.transform);
                ViewPose(b, 80, "vorn");
                float lo = 0.3f, hi = 0.95f;
                for (int it = 0; it < 7; it++)
                {
                    float c = (lo + hi) * 0.5f; SetProp(f3, "_Cutoff", c, IsCard);
                    yield return PairCo(grp, mid, f3);
                    if (_last.dArea > 0) lo = c; else hi = c;
                }
                float cut = (lo + hi) * 0.5f; SetProp(f3, "_Cutoff", cut, IsCard); trimCut[t] = cut;
                yield return PairCo(grp, mid, f3, $"v3cal_{t}_MID-far3trim");
                Row("A2 MID-FAR3 Schwelle", t, $"far3 mip {bestS:0.00}", "vorn", 80, _last, cut); minTrim = Mathf.Min(minTrim, _last.iou);
                ViewPose(b, 160, "vorn");
                yield return PairCo(grp, near, f3);
                Row("A2 NEAR-FAR3 Schwelle", t, $"far3 mip {bestS:0.00}", "vorn", 160, _last, cut);
                SetProp(f3, "_Cutoff", 0.5f, IsCard);
            }
            Log($"   F04a FAR v3 mit Flaechen-Schwelle: min MID->FAR IoU {minTrim:0.00} (gewaehlt bisher {minChosen:0.00})");
            if (minTrim < 9 && minTrim > minChosen + 0.005f)
            {
                _cal.useFar3 = true;
                foreach (var kv in trimCut)
                    for (int sd = 1; sd <= 6; sd++)
                        _cal.tints.Add(new TintCal { asset = kv.Key.Replace("_s1", "_s" + sd) + "_far3", material = "needle", cutoff = kv.Value });
            }
            else _cal.useFar3 = bestV == "far3";
            // F01: nur uebernehmen, wenn keine Uebergangs-IoU schlechter wird (Teilkette ist PLAYABLE)
            float bestF01 = 0; float bestMin = -1;
            foreach (float s in MipCandidates)
            {
                float mn = 9; bool ok = true;
                foreach (string t in CalTrees)
                {
                    if (!t.StartsWith("f01") || !nm.ContainsKey($"{t}|{s}")) continue;
                    float a = nm[$"{t}|{s}"], c = mf[$"{t}|{s}|far"];
                    mn = Mathf.Min(mn, Mathf.Min(a, c));
                    if (a < nm[$"{t}|0"] - 0.01f || c < mf[$"{t}|0|far"] - 0.01f) ok = false;
                    if (Mathf.Abs(nmL[$"{t}|{s}"]) > Mathf.Abs(nmL[$"{t}|0"]) + 0.03f || Mathf.Abs(mfL[$"{t}|{s}|far"]) > Mathf.Abs(mfL[$"{t}|0|far"]) + 0.03f) ok = false;
                }
                Log($"   F01 Kandidat mip {s:0.00}: min IoU {mn:0.00} · {(ok ? "keine Verschlechterung" : "verschlechtert mind. einen Uebergang")}");
                if (ok && mn > bestMin + 0.005f) { bestMin = mn; bestF01 = s; }
            }
            _cal.mipF01 = bestF01;
            Log($"   Gewaehlt: F01 mip {_cal.mipF01:0.00} · F04a mip {_cal.mipF04a:0.00} · F04a FAR {(_cal.useFar3 ? "v3" : "v2")}");
            _cal.notes.Add($"A: F01 mip {_cal.mipF01:0.00}, F04a mip {_cal.mipF04a:0.00}, F04a FAR {(_cal.useFar3 ? "v3" : "v2")}");
            // gewaehlte Werte fuer die folgenden Schritte setzen
            foreach (string t in CalTrees)
            {
                Transform grp = areaCalib.Find("FV_Cal_" + t); if (grp == null) continue;
                float s = t.StartsWith("f01") ? _cal.mipF01 : _cal.mipF04a;
                foreach (Transform c in grp) SetProp(c.gameObject, "_MipAlpha", s, IsCard);
                foreach (TintCal tc in _cal.tints)
                    if (tc.asset == t + "_far3" && tc.cutoff > 0) { GameObject f3 = Child(grp, $"cal_{t}_far3"); if (f3 != null) SetProp(f3, "_Cutoff", tc.cutoff, IsCard); }
            }
        }

        // ---------------- B
        private IEnumerator CalImpostors()
        {
            Quaternion sun0 = sun.transform.rotation;
            var iouByVar = new Dictionary<string, List<float>>(); var lumByVar = new Dictionary<string, List<float>>();
            var calByVar = new Dictionary<string, ImpostorCal>();
            foreach (string t in CalTrees)
            {
                Transform grp = areaCalib.Find("FV_Cal_" + t); if (grp == null) continue;
                bool f04 = t.StartsWith("f04a");
                float s = f04 ? _cal.mipF04a : _cal.mipF01;
                GameObject near = Child(grp, $"cal_{t}_near");
                GameObject reff = (f04 && _cal.useFar3) ? Child(grp, $"cal_{t}_far3") : Child(grp, $"cal_{t}_far");
                Only(grp, near); yield return null;
                Bounds b = BoundsOf(near.transform);
                var refT = new Dictionary<string, Texture2D>(); var nearT = new Dictionary<string, Texture2D>();
                foreach (string v in ImpViews)
                {
                    ViewPose(b, 160, v);
                    yield return Shot(grp, reff, x => refT[v] = x);
                    yield return Shot(grp, near, x => nearT[v] = x);
                }
                foreach (string variant in new[] { "vfar", "vfar3", "vfar3k" })
                {
                    GameObject imp = Child(grp, $"cal_{t}_{variant}");
                    if (imp == null) continue;
                    SetProp(imp, "_MipAlpha", s, IsImp);
                    // Alpha-Schwelle: Flaechenabgleich gegen FAR (Mittel der Ansichten)
                    float lo = 0.08f, hi = 0.92f;
                    for (int it = 0; it < 7; it++)
                    {
                        float c = (lo + hi) * 0.5f; SetProp(imp, "_Cutoff", c, IsImp);
                        float dA = 0;
                        foreach (string v in ImpViews)
                        {
                            ViewPose(b, 160, v); Texture2D ti = null;
                            yield return Shot(grp, imp, x => ti = x);
                            dA += Compare(refT[v], ti).dArea; Destroy(ti);
                        }
                        dA /= ImpViews.Length;
                        if (dA > 0) lo = c; else hi = c;
                    }
                    float cut = (lo + hi) * 0.5f; SetProp(imp, "_Cutoff", cut, IsImp);
                    // Helligkeit: Gain gegen FAR (lineares Licht ~ sRGB^2.2)
                    float g = GetProp(imp, "_OctGain", IsImp, 1f);
                    for (int it = 0; it < 4; it++)
                    {
                        double ratio = 0;
                        foreach (string v in ImpViews)
                        {
                            ViewPose(b, 160, v); Texture2D ti = null;
                            yield return Shot(grp, imp, x => ti = x);
                            M3 m = Compare(refT[v], ti); ratio += m.lumA / Mathf.Max(1f, m.lumB); Destroy(ti);
                        }
                        ratio /= ImpViews.Length;
                        g = Mathf.Clamp(g * Mathf.Pow((float)ratio, 2.2f), 0.4f, 4f);
                        SetProp(imp, "_OctGain", g, IsImp);
                        if (Math.Abs(ratio - 1) < 0.01) break;
                    }
                    var cal = new ImpostorCal { asset = $"{t}_{(variant == "vfar" ? "vfar" : "vfar3")}", cutoff = cut, gain = g, mipAlpha = s };
                    calByVar[$"{t}|{variant}"] = cal;
                    if (!iouByVar.ContainsKey(variant)) { iouByVar[variant] = new List<float>(); lumByVar[variant] = new List<float>(); }
                    foreach (string v in ImpViews)
                    {
                        ViewPose(b, 160, v); Texture2D ti = null;
                        yield return Shot(grp, imp, x => ti = x);
                        M3 m = Compare(refT[v], ti); Row("B FAR-VFAR", t, variant, v, 160, m, g);
                        iouByVar[variant].Add(m.iou); lumByVar[variant].Add(Mathf.Abs(m.dLum));
                        M3 mn = Compare(nearT[v], ti); Row("B NEAR-VFAR", t, variant, v, 160, mn, cut);
                        if (v == "vorn") SaveCropPair($"v3cal_Impostor_{t}_{variant}", refT[v], ti);
                        Destroy(ti);
                    }
                    // Lichtwechsel (Sonne 120° gedreht): Impostor muss weiter auf Licht reagieren
                    sun.transform.rotation = Quaternion.AngleAxis(120, Vector3.up) * sun0;
                    ViewPose(b, 160, "vorn");
                    Texture2D lr = null, li = null;
                    yield return Shot(grp, reff, x => lr = x);
                    yield return Shot(grp, imp, x => li = x);
                    Row("B Licht120", t, variant, "vorn", 160, Compare(lr, li), g);
                    Destroy(lr); Destroy(li);
                    sun.transform.rotation = sun0;
                }
                foreach (var x in refT.Values) Destroy(x);
                foreach (var x in nearT.Values) Destroy(x);
            }
            sun.transform.rotation = sun0;
            float Mean(List<float> lst) { if (lst == null || lst.Count == 0) return 0; float acc = 0; foreach (float q in lst) acc += q; return acc / lst.Count; }
            float iOld = Mean(iouByVar.ContainsKey("vfar") ? iouByVar["vfar"] : null), i3 = Mean(iouByVar.ContainsKey("vfar3") ? iouByVar["vfar3"] : null),
                  i3k = Mean(iouByVar.ContainsKey("vfar3k") ? iouByVar["vfar3k"] : null);
            float l3k = Mean(lumByVar.ContainsKey("vfar3k") ? lumByVar["vfar3k"] : null);
            _cal.useVfar3 = i3 >= iOld - 0.01f;
            bool use1k = _cal.useVfar3 && i3k >= i3 - 0.02f && l3k <= 0.05f;
            _cal.impostorMaxSize = use1k ? 1024 : 2048;
            _cal.impostorBc = true;
            string chosen = _cal.useVfar3 ? (use1k ? "vfar3k" : "vfar3") : "vfar";
            foreach (string t in CalTrees)
                if (calByVar.TryGetValue($"{t}|{chosen}", out ImpostorCal c)) _cal.impostors.Add(c);
            // B2: Lichtwirkung (Normal-Atlas-Daempfung, Transluzenz) + Gain gemeinsam: kleinste maximale |ΔL| ueber
            //     Augenhoehe vorn/seitlich, erhoeht 30° und Sonne +120° (keine gebackene Lichtrichtung).
            foreach (string t in CalTrees)
            {
                Transform grp = areaCalib.Find("FV_Cal_" + t); if (grp == null) continue;
                if (!calByVar.TryGetValue($"{t}|{chosen}", out ImpostorCal ic)) continue;
                bool f04 = t.StartsWith("f04a");
                GameObject near = Child(grp, $"cal_{t}_near"), imp = Child(grp, $"cal_{t}_{chosen}");
                GameObject reff = (f04 && _cal.useFar3) ? Child(grp, $"cal_{t}_far3") : Child(grp, $"cal_{t}_far");
                if (f04 && _cal.useFar3) foreach (TintCal tc in _cal.tints) if (tc.asset == t + "_far3" && tc.cutoff > 0) SetProp(reff, "_Cutoff", tc.cutoff, IsCard);
                Only(grp, near); yield return null;
                Bounds b = BoundsOf(near.transform);
                string[] conds = { "vorn", "seitlich90", "hinten180", "erhoeht30", "licht120", "licht240" };
                var refs = new Texture2D[conds.Length];
                for (int k = 0; k < conds.Length; k++)
                {
                    yield return CondPose(b, conds[k], sun0);
                    yield return Shot(grp, reff, x => refs[k] = x);
                    sun.transform.rotation = sun0;
                }
                float bestErr = 9, bestNb = 0, bestTr = -1, bestG = ic.gain, bestSx = -1, bestSz = 1;
                float tr0 = GetProp(imp, "_Translucency", IsImp, 0.18f);
                // Achsvorzeichen-Diagnose (Lauf 3): (-1, +1) = Standard war fuer 5 von 6 Archetypen am besten -> kein Achsfehler, fest.
                foreach (Vector2 sg in new[] { new Vector2(-1, 1) })
                    foreach (float nb in new[] { 0f, 0.3f })
                        foreach (float tr in new[] { tr0, tr0 * 1.8f, tr0 * 2.8f })
                        {
                            SetVec(imp, "_NormalSign", new Vector4(sg.x, 1, sg.y, 0));
                            SetProp(imp, "_NormalBlend", nb, IsImp); SetProp(imp, "_Translucency", tr, IsImp);
                            float g = ic.gain;
                            for (int it = 0; it < 3; it++)
                            {
                                SetProp(imp, "_OctGain", g, IsImp);
                                // Ziel: Standard-Testlicht von vorn (v1-Messgroesse) doppelt gewichtet + Mittel aller Bedingungen
                                double ratio = 0, wsum = 0; float err = 0, errV = 0, errSum = 0;
                                for (int k = 0; k < conds.Length; k++)
                                {
                                    yield return CondPose(b, conds[k], sun0);
                                    Texture2D ti = null;
                                    yield return Shot(grp, imp, x => ti = x);
                                    sun.transform.rotation = sun0;
                                    M3 m = Compare(refs[k], ti); Destroy(ti);
                                    float w = k == 0 ? 2f : 1f;
                                    ratio += w * m.lumA / Mathf.Max(1f, m.lumB); wsum += w;
                                    errSum += Mathf.Abs(m.dLum); if (k == 0) errV = Mathf.Abs(m.dLum);
                                }
                                ratio /= wsum;
                                err = errV + errSum / conds.Length;
                                if (err < bestErr) { bestErr = err; bestNb = nb; bestTr = tr; bestG = g; bestSx = sg.x; bestSz = sg.y; }
                                g = Mathf.Clamp(g * Mathf.Pow((float)ratio, 2.2f), 0.4f, 6f);
                            }
                            Log($"     {t} Vorzeichen ({sg.x:+0;-0}, {sg.y:+0;-0}) · Daempfung {nb:0.0} · Transluzenz {tr:0.00}: bester Wert |ΔL vorn| + Ø|ΔL| bisher {bestErr * 100:0.0} %");
                        }
                SetVec(imp, "_NormalSign", new Vector4(bestSx, 1, bestSz, 0));
                SetProp(imp, "_NormalBlend", bestNb, IsImp); SetProp(imp, "_Translucency", bestTr, IsImp); SetProp(imp, "_OctGain", bestG, IsImp);
                ic.normalBlend = bestNb; ic.translucency = bestTr; ic.gain = bestG; ic.signX = bestSx; ic.signZ = bestSz;
                for (int k = 0; k < conds.Length; k++)
                {
                    yield return CondPose(b, conds[k], sun0);
                    Texture2D ti = null;
                    yield return Shot(grp, imp, x => ti = x);
                    sun.transform.rotation = sun0;
                    Row("B2 FAR-VFAR kal.", t, $"{chosen} s({bestSx:+0;-0},{bestSz:+0;-0}) nb {bestNb:0.0} tr {bestTr:0.00}", conds[k], 160, Compare(refs[k], ti), bestG);
                    Destroy(ti);
                }
                foreach (var x in refs) Destroy(x);
                Log($"   Impostor {t}: Vorzeichen ({bestSx:+0;-0}, {bestSz:+0;-0}) · Normal-Daempfung {bestNb:0.0} · Transluzenz {bestTr:0.00} · Gain {bestG:0.00} -> |ΔL vorn| + Ø|ΔL| = {bestErr * 100:0.0} % (6 Bedingungen)");
            }
            Log($"   Impostor Ø IoU gegen FAR: alt {iOld:0.00} · v3 2048 {i3:0.00} · v3 1024 {i3k:0.00} (|ΔL| Ø {l3k * 100:0.0} %) -> gewaehlt {chosen}");
            _cal.notes.Add($"B: Impostor {chosen} (Ø IoU alt {iOld:0.00}, v3 {i3:0.00}, v3-1024 {i3k:0.00})");
        }

        // ---------------- C
        private IEnumerator CalF10()
        {
            foreach (string season in new[] { "sommer", "trocken" })
            {
                Transform grp = areaCalib.Find("FV_Cal_f10_" + season); if (grp == null) continue;
                string pre = $"cal_f10_group_{season}_";
                GameObject full = Child(grp, pre + "full"), l1 = Child(grp, pre + "lod1"), l2 = Child(grp, pre + "lod2");
                Only(grp, full); yield return null;
                Bounds b = BoundsOf(full.transform);
                void PoseF(float dist) => Pose(new Vector3(b.center.x, 1.7f, b.center.z - dist), new Vector3(b.center.x, 0.3f, b.center.z), 60f);
                // LOD0-Varianten gegen volle Gruppe
                string bestV = "lod0"; float bestScore = 9;
                foreach (string v in new[] { "lod0", "lod0a3", "lod0b3" })
                {
                    GameObject o = Child(grp, pre + v); if (o == null) continue;
                    float sc = 0;
                    foreach (float d in new[] { 8f, 14f })
                    {
                        PoseF(d);
                        yield return PairCo(grp, full, o, d == 8f ? $"v3cal_F10_{season}_voll-{v}_8m" : null);
                        Row("C LOD0", $"f10_{season}", v, "vorn", d, _last); sc += Mathf.Abs(_last.dArea) + Mathf.Abs(_last.dLum);
                    }
                    if (v != "lod0" && sc < bestScore) { bestScore = sc; bestV = v; }
                }
                _cal.f10Lod0 = bestV == "lod0" ? "" : bestV.Substring(4);
                GameObject l0 = Child(grp, pre + bestV);
                // Farbabgleich LOD0 gegen volle Gruppe (je Kanal, lineares Licht)
                Vector3 k0 = Vector3.one, kBest0 = Vector3.one; float eBest0 = 9;
                for (int it = 0; it < 6; it++)
                {
                    Vector3 rf = Vector3.zero, rl = Vector3.zero; float e = 0;
                    foreach (float d in new[] { 8f, 14f }) { PoseF(d); yield return PairCo(grp, full, l0); rf += _last.rgbA; rl += _last.rgbB; e = Mathf.Max(e, Mathf.Abs(_last.dLum)); }
                    if (e < eBest0) { eBest0 = e; kBest0 = k0; }
                    if (e < 0.01f) break;
                    Vector3 k = new Vector3(Rat(rf.x, rl.x), Rat(rf.y, rl.y), Rat(rf.z, rl.z));
                    MulColor(l0, k, IsLit); k0 = Vector3.Scale(k0, k);
                }
                { Vector3 back = new Vector3(kBest0.x / k0.x, kBest0.y / k0.y, kBest0.z / k0.z); MulColor(l0, back, IsLit); k0 = kBest0; }
                foreach (float d in new[] { 8f, 14f }) { PoseF(d); yield return PairCo(grp, full, l0); Row("C LOD0 kal.", $"f10_{season}", bestV, "vorn", d, _last); }
                foreach (string mk in new[] { "grass", "grasscard" })
                    _cal.tints.Add(new TintCal { asset = $"f10_group_{season}_{bestV}", material = mk, r = k0.x, g = k0.y, b = k0.z });
                // LOD1 gegen LOD0 (18 m), LOD2 gegen LOD1 (50 m): Alpha-Schwelle (Flaeche), dann Farbe
                foreach (var (a, c, d, an) in new[] { (l0, l1, 18f, "lod1"), (l1, l2, 50f, "lod2") })
                {
                    PoseF(d);
                    yield return PairCo(grp, a, c, $"v3cal_F10_{season}_{an}_vorher");
                    Row("C " + an + " vorher", $"f10_{season}", an, "vorn", d, _last);
                    float lo = 0.1f, hi = 0.9f;
                    for (int it = 0; it < 6; it++)
                    {
                        float cc = (lo + hi) * 0.5f; SetProp(c, "_Cutoff", cc, IsCard);
                        yield return PairCo(grp, a, c);
                        if (_last.dArea > 0) lo = cc; else hi = cc;
                    }
                    float cut = (lo + hi) * 0.5f; SetProp(c, "_Cutoff", cut, IsCard);
                    Vector3 kk = Vector3.one, kBest = Vector3.one; float eBest = 9;
                    for (int it = 0; it < 8; it++)
                    {
                        yield return PairCo(grp, a, c);
                        float e = Mathf.Abs(_last.dLum);
                        if (e < eBest) { eBest = e; kBest = kk; }
                        if (e < 0.01f) break;
                        Vector3 k = new Vector3(Rat(_last.rgbA.x, _last.rgbB.x), Rat(_last.rgbA.y, _last.rgbB.y), Rat(_last.rgbA.z, _last.rgbB.z));
                        MulColor(c, k, IsCard); kk = Vector3.Scale(kk, k);
                    }
                    { Vector3 back = new Vector3(kBest.x / kk.x, kBest.y / kk.y, kBest.z / kk.z); MulColor(c, back, IsCard); kk = kBest; }
                    yield return PairCo(grp, a, c, $"v3cal_F10_{season}_{an}_nachher");
                    Row("C " + an + " kal.", $"f10_{season}", an, "vorn", d, _last, cut);
                    _cal.tints.Add(new TintCal { asset = $"f10_group_{season}_{an}", material = "grasscard", r = kk.x, g = kk.y, b = kk.z, cutoff = cut });
                }
                Log($"   F10 {season}: LOD0 {bestV}, Farbe LOD0 ×({k0.x:0.00}, {k0.y:0.00}, {k0.z:0.00})");
            }
            _cal.notes.Add($"C: F10 LOD0 {(_cal.f10Lod0 == "" ? "v2" : _cal.f10Lod0)}");
        }

        // gedaempfter Schritt (sRGB -> linear ~2,2, hier 1,4 gegen Ueberschwingen); bester Stand wird behalten
        private static float Rat(float target, float cur) => Mathf.Clamp(Mathf.Pow(Mathf.Max(1f, target) / Mathf.Max(1f, cur), 1.4f), 0.75f, 1.33f);

        // ---------------- D
        private IEnumerator CalMemory()
        {
            var seen = new HashSet<Texture>();
            foreach (Transform grp in areaCalib)
                foreach (Renderer r in grp.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (!IsImp(m)) continue;
                        foreach (string p in new[] { "_BaseMap", "_NormalAtlas" })
                        {
                            Texture t = m.GetTexture(p);
                            if (t == null || !seen.Add(t)) continue;
                            long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                            _cal.rows.Add(new CalRow { step = "D Speicher", asset = t.name, variant = $"{t.width}x{t.height} {t.graphicsFormat}", value = bytes / 1048576f });
                            Log($"   Speicher {t.name,-48} {t.width}x{t.height} {t.graphicsFormat,-28} {bytes / 1048576f,7:0.00} MB");
                        }
                    }
            yield return null;
        }
    }
}
