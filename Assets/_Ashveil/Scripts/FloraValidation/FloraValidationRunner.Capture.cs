using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.FloraValidation
{
    /// <summary>Bilder, Clips, Bildmetriken und Diagnose-Materialtausch fuer den Flora-Messlauf.</summary>
    public partial class FloraValidationRunner
    {
        private static readonly Color KeyColor = new Color(1f, 0f, 1f, 1f);
        private readonly Dictionary<Renderer, Material[]> _savedMats = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<(Material, int, Color), Material> _debugMats = new Dictionary<(Material, int, Color), Material>();

        // ------------------------------------------------------------------ Einzelbilder

        /// <summary>Rendert die Kamera in ein eigenes Bild (Spielkamera-Pipeline) und speichert es als PNG.</summary>
        private Texture2D Render(int w, int h, bool keyBackground, bool keep, string file, RenderTextureFormat fmt = RenderTextureFormat.ARGB32, bool linear = false)
        {
            CameraClearFlags cf = cam.clearFlags; Color bg = cam.backgroundColor;
            if (keyBackground) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = KeyColor; SetGrounds(false); }
            var rt = RenderTexture.GetTemporary(w, h, 24, fmt, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            var tex = new Texture2D(w, h, fmt == RenderTextureFormat.ARGBHalf ? TextureFormat.RGBAHalf : TextureFormat.RGB24, false, linear);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            if (keyBackground) { cam.clearFlags = cf; cam.backgroundColor = bg; SetGrounds(true); }
            if (!string.IsNullOrEmpty(file))
            {
                string p = Path.Combine(_out, "Bilder", file + ".png");
                File.WriteAllBytes(p, tex.EncodeToPNG());
                _report.images.Add("Bilder/" + file + ".png");
            }
            if (!keep) { Destroy(tex); return null; }
            return tex;
        }

        private void Still(string file, int w = 1920, int h = 1080) => Render(w, h, false, false, file);

        // ------------------------------------------------------------------ Clips (JPEG-Folge in einer Datei) + Pop-Metrik

        /// <summary>
        /// Nimmt eine Bildfolge mit fester Zeitbasis auf (Time.captureFramerate = 30, damit Crossfade und Wind je Bild gleich weit laufen).
        /// Datei: Clips/&lt;name&gt;.fvclip = "FVC1", int32 w, h, n, dann je Bild int32 Laenge + JPEG.
        /// Pop-Metrik: mittlere Helligkeitsdifferenz zwischen aufeinanderfolgenden Bildern; Spitzen = sichtbare Spruenge.
        /// </summary>
        private IEnumerator Clip(string name, int frames, Action<int> perFrame, PopResult pop = null, int w = 640, int h = 360)
        {
            if (quick) frames = Mathf.Min(frames, 40);
            Time.captureFramerate = 30;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            string path = Path.Combine(_out, "Clips", name + ".fvclip");
            var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            var bw = new BinaryWriter(fs);
            bw.Write(new[] { (byte)'F', (byte)'V', (byte)'C', (byte)'1' }); bw.Write(w); bw.Write(h); bw.Write(frames);
            float[] last = null;
            var diffs = new List<float>(frames);
            for (int i = 0; i < frames; i++)
            {
                perFrame(i);
                yield return new WaitForEndOfFrame();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                RenderTexture.active = null;
                byte[] jpg = tex.EncodeToJPG(82);
                bw.Write(jpg.Length); bw.Write(jpg);
                float[] lum = Luma(tex, 4);
                if (last != null) diffs.Add(MeanAbs(lum, last));
                last = lum;
            }
            bw.Flush(); fs.Close();
            cam.targetTexture = prev;
            Time.captureFramerate = 0;
            Destroy(tex); rt.Release(); Destroy(rt);
            if (pop != null)
            {
                var s = new List<float>(diffs); s.Sort();
                pop.frames = frames; pop.clip = "Clips/" + name + ".fvclip";
                pop.medianDiff = s.Count > 0 ? s[s.Count / 2] : 0;
                pop.p99Diff = s.Count > 0 ? s[Mathf.Min(s.Count - 1, (int)(s.Count * 0.99f))] : 0;
                pop.maxDiff = s.Count > 0 ? s[s.Count - 1] : 0;
                pop.popScore = pop.maxDiff / Mathf.Max(0.05f, pop.medianDiff);
                pop.diffs = diffs;
                _report.pops.Add(pop);
                Log($"   Clip {name}: {frames} Bilder · Median-Diff {pop.medianDiff:0.00} · Max {pop.maxDiff:0.00} · Pop-Score {pop.popScore:0.0}");
            }
            else Log($"   Clip {name}: {frames} Bilder");
        }

        private static float[] Luma(Texture2D t, int step)
        {
            Color32[] px = t.GetPixels32();
            int w = t.width, h = t.height; var o = new float[(w / step) * (h / step)]; int k = 0;
            for (int y = 0; y + step <= h; y += step)
                for (int x = 0; x + step <= w; x += step, k++)
                {
                    if (k >= o.Length) break;
                    Color32 c = px[y * w + x];
                    o[k] = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                }
            return o;
        }

        private static float MeanAbs(float[] a, float[] b)
        {
            double s = 0; int n = Mathf.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) s += Mathf.Abs(a[i] - b[i]);
            return (float)(s / Mathf.Max(1, n));
        }

        // ------------------------------------------------------------------ Masken-Metrik (Schlüsselfarbe)

        private static bool[] Mask(Texture2D t, out float lum, out int pxHeight)
        {
            Color32[] px = t.GetPixels32(); var m = new bool[px.Length]; double ls = 0; int n = 0; int ymin = int.MaxValue, ymax = -1;
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                bool bg = c.r > 200 && c.g < 60 && c.b > 200;
                m[i] = !bg;
                if (!bg)
                {
                    ls += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; n++;
                    int y = i / t.width; ymin = Mathf.Min(ymin, y); ymax = Mathf.Max(ymax, y);
                }
            }
            lum = n > 0 ? (float)(ls / n) : 0; pxHeight = ymax >= 0 ? ymax - ymin : 0;
            return m;
        }

        private StaticLodResult CompareMasks(string asset, string transition, float dist, Texture2D a, Texture2D b)
        {
            bool[] ma = Mask(a, out float la, out int ha), mb = Mask(b, out float lb, out _);
            int inter = 0, uni = 0, ca = 0, cb = 0;
            for (int i = 0; i < ma.Length; i++)
            {
                if (ma[i] && mb[i]) inter++;
                if (ma[i] || mb[i]) uni++;
                if (ma[i]) ca++;
                if (mb[i]) cb++;
            }
            var r = new StaticLodResult
            {
                asset = asset, transition = transition, distance = dist, iou = uni > 0 ? (float)inter / uni : 0,
                dLum = la > 0 ? lb / la - 1f : 0, areaChange = ca > 0 ? (float)(cb - ca) / ca : 0, pxHeight = ha,
            };
            _report.lodStatic.Add(r);
            Log($"   {asset,-24} {transition,-10} @{dist,4:0} m · IoU {r.iou:0.00} · ΔHelligkeit {r.dLum * 100:+0.0;-0.0} % · ΔFläche {r.areaChange * 100:+0.0;-0.0} % · {ha} px");
            return r;
        }

        // ------------------------------------------------------------------ Diagnose-Materialtausch

        private Material DebugMat(Material src, int mode, Color color)
        {
            var key = (src, mode, color);
            if (_debugMats.TryGetValue(key, out Material m)) return m;
            m = new Material(debugShader) { name = "FV_Debug_" + (src != null ? src.name : "null") + "_" + mode, enableInstancing = true };
            bool imp = src != null && src.shader != null && src.shader.name.Contains("FloraImpostor");
            if (src != null && src.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
            if (src != null && src.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", src.GetFloat("_Cutoff"));
            if (src != null && src.HasProperty("_MipAlpha")) m.SetFloat("_MipAlpha", src.GetFloat("_MipAlpha"));
            m.SetFloat("_AlphaClip", src != null && src.IsKeywordEnabled("_ALPHATEST_ON") ? 1f : 0f);
            m.SetFloat("_Cull", src != null && src.HasProperty("_Cull") ? src.GetFloat("_Cull") : 0f);
            if (imp)
            {
                m.SetFloat("_Impostor", 1f); m.SetVector("_OctBox", src.GetVector("_OctBox")); m.SetVector("_OctExt", src.GetVector("_OctExt")); m.SetFloat("_OctGrid", src.GetFloat("_OctGrid")); m.SetFloat("_Cull", 0f);
            }
            m.SetFloat("_Mode", mode);
            m.SetColor("_DebugColor", color);
            if (mode == 0)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One); m.SetFloat("_DstBlend", (float)BlendMode.One);
                m.SetFloat("_ZTest", (float)CompareFunction.Always); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Increment", 1f / 64f);
                m.renderQueue = 3000;
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One); m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_ZTest", (float)CompareFunction.LessEqual); m.SetFloat("_ZWrite", 1f);
                m.renderQueue = 2450;
            }
            _debugMats[key] = m;
            return m;
        }

        private static readonly Color[] LodColors =
        {
            new Color(0.15f, 0.75f, 0.2f), new Color(0.95f, 0.85f, 0.1f), new Color(0.95f, 0.45f, 0.05f), new Color(0.85f, 0.1f, 0.75f), new Color(0.5f, 0.5f, 0.5f),
        };

        /// <summary>mode 0 = Overdraw, 1 = LOD-Farben.</summary>
        private void SwapDebug(Transform root, int mode)
        {
            var lodOf = new Dictionary<Renderer, int>();
            foreach (LODGroup g in root.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = g.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers) if (r != null) lodOf[r] = lods.Length == 1 ? 4 : i;
            }
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!_savedMats.ContainsKey(r)) _savedMats[r] = r.sharedMaterials;
                int lvl = lodOf.TryGetValue(r, out int l) ? l : 4;
                Material[] src = _savedMats[r]; var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = DebugMat(src[i], mode, LodColors[Mathf.Clamp(lvl, 0, 4)]);
                r.sharedMaterials = dst;
            }
        }

        private void RestoreMaterials()
        {
            foreach (var kv in _savedMats) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            _savedMats.Clear();
        }

        /// <summary>Overdraw: Schichten pro Pixel (ZTest Always = obere Schranke der schattierten Fragmente) + Heatmap.</summary>
        private OverdrawResult Overdraw(string view, Transform root, int w = 1920, int h = 1080)
        {
            SwapDebug(root, 0);
            var data = cam.GetUniversalAdditionalCameraData();
            bool pp = data.renderPostProcessing; data.renderPostProcessing = false;
            CameraClearFlags cf = cam.clearFlags; Color bg = cam.backgroundColor;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            SetGrounds(false);
            Texture2D t = Render(w, h, false, true, null, RenderTextureFormat.ARGBHalf, true);
            cam.clearFlags = cf; cam.backgroundColor = bg; SetGrounds(true);
            data.renderPostProcessing = pp;
            RestoreMaterials();
            Color[] px = t.GetPixels();
            double sumC = 0; int cov = 0, o4 = 0, o8 = 0, o16 = 0; float mx = 0;
            var heat = new Texture2D(w, h, TextureFormat.RGB24, false);
            var hp = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                float layers = Mathf.Round(px[i].r * 64f);
                if (layers > 0.5f) { cov++; sumC += layers; }
                if (layers > 4) o4++;
                if (layers > 8) o8++;
                if (layers > 16) o16++;
                mx = Mathf.Max(mx, layers);
                hp[i] = Heat(layers);
            }
            heat.SetPixels32(hp); heat.Apply(false);
            string file = "11_Overdraw_" + view;
            File.WriteAllBytes(Path.Combine(_out, "Bilder", file + ".png"), heat.EncodeToPNG());
            _report.images.Add("Bilder/" + file + ".png");
            Destroy(heat); Destroy(t);
            int n = px.Length;
            var r = new OverdrawResult
            {
                view = view, image = "Bilder/" + file + ".png", coverage = (float)cov / n, avgLayersCovered = cov > 0 ? (float)(sumC / cov) : 0,
                avgLayersScreen = (float)(sumC / n), pctOver4 = (float)o4 / n, pctOver8 = (float)o8 / n, pctOver16 = (float)o16 / n, maxLayers = mx,
            };
            _report.overdraw.Add(r);
            Log($"   Overdraw {view,-26} Deckung {r.coverage * 100,5:0.0} % · Ø {r.avgLayersCovered,5:0.0} Schichten (bedeckt) · Ø {r.avgLayersScreen,5:0.00} (Bild) · >4 {r.pctOver4 * 100,5:0.0} % · >8 {r.pctOver8 * 100,5:0.0} % · >16 {r.pctOver16 * 100,5:0.0} % · max {mx:0}");
            return r;
        }

        private static Color32 Heat(float l)
        {
            if (l <= 0.5f) return new Color32(0, 0, 0, 255);
            Color c;
            if (l <= 1) c = new Color(0.1f, 0.15f, 0.6f);
            else if (l <= 2) c = new Color(0.1f, 0.5f, 0.85f);
            else if (l <= 4) c = new Color(0.15f, 0.75f, 0.25f);
            else if (l <= 8) c = new Color(0.95f, 0.85f, 0.1f);
            else if (l <= 16) c = new Color(0.95f, 0.45f, 0.05f);
            else if (l <= 32) c = new Color(0.85f, 0.08f, 0.08f);
            else c = Color.white;
            return c;
        }
    }
}
