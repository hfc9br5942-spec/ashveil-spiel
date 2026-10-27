using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.Rendering.PathTracing.Lab
{
    /// <summary>
    /// PathTracingTestScene (§101–110): baut im Play-Modus nacheinander die Pruefszenen auf, laesst den Pathtracer
    /// konvergieren und prueft Zahlen statt Augenmass – Weissofen (keine Energie aus dem Nichts), Cornell-Box
    /// (Farbuebertrag, weiche Schatten), Metall/Rauheit-Reihe, Glas (Brechzahl 1 = unsichtbar), kleines helles Licht
    /// (alle drei Strategien liefern dasselbe Mittel, MIS hat den kleinsten Fehler), Sonne + Himmel, Lagerfeuer bei
    /// Nacht, Reproduzierbarkeit und Rechenzeit. Bericht: Logs/Ashveil/Ashveil_PathTracingLab.txt, Bilder (PNG + EXR)
    /// unter Logs/Ashveil/Pathtracing/.
    /// </summary>
    public sealed class PathTracingLab : MonoBehaviour
    {
        public const string ReportPath = "Logs/Ashveil/Ashveil_PathTracingLab.txt";
        public const string ImageFolder = "Logs/Ashveil/Pathtracing";

        /// <summary>Nur diese Tests (leer = alle), z. B. "ofen,cornell".</summary>
        public string only = "";
        public int samples = 256;
        public const int Width = 1280, Height = 720;
        private RenderTexture _target;

        private void OnGUI()
        {
            if (_target == null || Event.current.type != EventType.Repaint) return;
            GUI.depth = 100;   // hinter der Pathtracing-Anzeige
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _target, ScaleMode.ScaleToFit, false);
        }

        private readonly StringBuilder _report = new StringBuilder();
        private int _pass, _fail;
        private Camera _cam;
        private Transform _root;
        private AshveilPathTracer _pt;
        private Shader _lit;

        public static bool Finished { get; private set; }

        private void Start()
        {
            Finished = false;
            StartCoroutine(Run());
        }

        private void Line(string s)
        {
            _report.AppendLine(s);
            Debug.Log("[Ashveil] PT-Labor: " + s);
        }

        private void Check(bool ok, string what)
        {
            if (ok) _pass++; else _fail++;
            Line((ok ? "  OK     " : "  FEHLER ") + what);
        }

        private bool Want(string key) => string.IsNullOrEmpty(only) || only.Contains(key);

        private IEnumerator Run()
        {
            Line("ASHVEIL – Pathtracing-Labor");
            Line($"Zeit: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Line($"Geraet: {SystemInfo.graphicsDeviceName} · {SystemInfo.graphicsDeviceType} · Pathtracing {PathTracingCapability.Describe()}");
            if (!PathTracingCapability.IsSupported)
            {
                Check(false, "Pathtracing verfuegbar (Editor mit DirectX 12 starten)");
                Finish();
                yield break;
            }
            _lit = Shader.Find("Universal Render Pipeline/Lit");
            SetupCamera();
            AshveilPathTracer.SetEnabled(true);
            _pt = AshveilPathTracer.Instance;
            _pt.targetCamera = _cam;
            _pt.showHud = true;
            yield return null;
            Line($"Bildgroesse: {_cam.pixelWidth} × {_cam.pixelHeight}");

            if (Want("ofen")) yield return Furnace();
            if (Want("cornell")) yield return Cornell();
            if (Want("metall")) yield return MetalRoughness();
            if (Want("glas")) yield return Glass();
            if (Want("mis")) yield return SmallLight();
            if (Want("sonne")) yield return SunSky();
            if (Want("feuer")) yield return Fire();
            if (Want("repro")) yield return Reproducible();
            if (Want("entrauschen")) yield return DenoiseTest();
            if (Want("adaptiv")) yield return AdaptiveTest();
            if (Want("zeit")) yield return Timing();
            Finish();
        }

        private void Finish()
        {
            Line("");
            Line($"ERGEBNIS: {_pass} bestanden, {_fail} fehlgeschlagen");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, _report.ToString());
            Finished = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // ------------------------------------------------------------------ Aufbau-Helfer

        private void SetupCamera()
        {
            var go = new GameObject("Labor-Kamera");
            _cam = go.AddComponent<Camera>();
            go.tag = "MainCamera";
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 500f;
            _cam.allowHDR = true;
            // feste Aufloesung, unabhaengig von der Groesse des Game-Fensters (Pruefbereiche bleiben vergleichbar)
            _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Labor-Bild" };
            _target.Create();
            _cam.targetTexture = _target;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            // neutrale Belichtung/Tonemapping fuer die PNGs (Pruefwerte kommen aus dem linearen Bild)
            var vol = new GameObject("Labor-Nachbearbeitung").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = vol.profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
        }

        private void Clear()
        {
            if (_root != null) { _root.gameObject.SetActive(false); Destroy(_root.gameObject); }
            _root = new GameObject("Laborszene").transform;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) { l.gameObject.SetActive(false); Destroy(l.gameObject); }
        }

        private Material Mat(Color c, float smooth = 0f, float metal = 0f, Color? emission = null)
        {
            var m = new Material(_lit);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
            }
            return m;
        }

        private GameObject Box(Vector3 pos, Vector3 size, Material m, Vector3? euler = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            if (euler.HasValue) go.transform.eulerAngles = euler.Value;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        private GameObject Sphere(Vector3 pos, float radius, Material m)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * radius * 2f;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        private Light PointLight(Vector3 pos, Color c, float intensity, float radius, float range = 20f)
        {
            var go = new GameObject("Licht");
            go.transform.SetParent(_root, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.Soft;
            go.AddComponent<PathTraceLight>().radius = radius;
            return l;
        }

        private Light Sun(Vector3 euler, Color c, float intensity)
        {
            var go = new GameObject("Sonne");
            go.transform.SetParent(_root, false);
            go.transform.eulerAngles = euler;
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.shadows = LightShadows.Soft;
            RenderSettings.sun = l;
            return l;
        }

        private void Look(Vector3 from, Vector3 at, float fov)
        {
            _cam.transform.position = from;
            _cam.transform.LookAt(at);
            _cam.fieldOfView = fov;
        }

        /// <summary>Bildbereich um einen Weltpunkt (Pixel, Zeile 0 = unten), halbe Kantenlaenge in Pixeln.</summary>
        private RectInt Around(Vector3 world, int half)
        {
            Vector3 s = _cam.WorldToScreenPoint(world);
            int w = _pt.Stats.Width > 0 ? _pt.Stats.Width : _cam.pixelWidth;
            int h = _pt.Stats.Height > 0 ? _pt.Stats.Height : _cam.pixelHeight;
            // Kamera-Pixel → Pathtracing-Pixel (Renderskalierung)
            float kx = w / (float)_cam.pixelWidth, ky = h / (float)_cam.pixelHeight;
            int cx = Mathf.RoundToInt(s.x * kx), cy = Mathf.RoundToInt(s.y * ky);
            return new RectInt(cx - half, cy - half, half * 2, half * 2);
        }

        private void Configure(Action<PathTraceSettings> tweak)
        {
            var s = new PathTraceSettings();
            s.ApplyPreset(PathTracePreset.PhotoHigh);
            s.adaptive = false;
            s.denoise = PathTraceDenoise.Aus;
            s.targetSamples = samples;
            s.environment = PathTraceEnvironment.Aus;
            s.freezeWorld = true;
            s.filterGlossy = 0f;   // Pruefungen erwartungstreu; Bildtests setzen ihn selbst
            tweak?.Invoke(s);
            _pt.settings = s;
            _pt.Recapture();
        }

        private IEnumerator Converge(float timeout = 120f)
        {
            yield return null;
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (!_pt.Stats.Done && Time.realtimeSinceStartup - t0 < timeout) yield return null;
            yield return null;
            var st = _pt.Stats;
            Line($"  [{st.Samples} Proben in {Time.realtimeSinceStartup - t0:0.0} s · {st.Width}×{st.Height} · RTAS {st.Instances} Instanzen, {st.Triangles} Dreiecke · GPU {st.GpuMs:0.00} ms · Lichter {st.LocalLights} · Leuchtdreiecke {st.EmissiveTriangles} · Hauptlicht {st.MainLight} E={st.SunIrradiance:0.00}{(st.MainIsMoon ? " (Mond)" : "")}]");
        }

        private AshveilPathTracer.RegionStats _region;
        private int _debugRegions = 3;
        private IEnumerator Read(RectInt r)
        {
            bool ready = false;
            _pt.ReadRegion(r, s => { _region = s; ready = true; });
            if (_debugRegions-- > 0) Line($"  (Bereich {r.xMin},{r.yMin} {r.width}×{r.height})");
            float t0 = Time.realtimeSinceStartup;
            while (!ready && Time.realtimeSinceStartup - t0 < 10f) yield return null;
        }

        private IEnumerator Save(string name)
        {
            bool ready = false;
            _pt.SaveCapture(Path.Combine(ImageFolder, name), true, true, _ => ready = true);
            float t0 = Time.realtimeSinceStartup;
            while (!ready && Time.realtimeSinceStartup - t0 < 20f) yield return null;
        }

        private static string V(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";

        // ------------------------------------------------------------------ Test 1: Weissofen
        private IEnumerator Furnace()
        {
            Line("");
            Line("Test 1 – Weissofen (gleichmaessiger Himmel 1,0; Kugel darf keine Energie erzeugen)");
            Clear();
            GameObject s = Sphere(Vector3.zero, 1f, Mat(Color.white, 0f, 0f));
            Look(new Vector3(0, 0, -3.2f), Vector3.zero, 45f);
            Configure(c => { c.environment = PathTraceEnvironment.Gleichmaessig; c.uniformEnvironment = Color.white; });
            yield return Converge();
            yield return Read(Around(Vector3.zero, 40));
            var diffuse = _region;
            Line($"  weiss diffus: Mittel {V(diffuse.Mean)}, hellster Pixel {diffuse.MaxLuminance:0.000}, Fehler {diffuse.RelError * 100:0.0} %, Proben {diffuse.MinSamples}");
            Check(diffuse.Pixels > 1000 && diffuse.Invalid == 0, "Bereich gueltig, keine NaN/Inf");
            Check(diffuse.Luminance > 0.9f && diffuse.Luminance < 1.005f, $"weisses Lambert-Material ≈ 1 (ist {diffuse.Luminance:0.000}, erlaubt 0,90–1,005)");
            yield return Read(Around(new Vector3(1.4f, 1.0f, 0), 8));
            Check(Mathf.Abs(_region.Luminance - 1f) < 0.01f, $"Himmel = 1,0 (ist {_region.Luminance:0.000})");
            yield return Save("01_weissofen_diffus");

            s.GetComponent<MeshRenderer>().sharedMaterial = Mat(Color.white, 0.5f, 1f);
            _pt.Recapture();
            yield return Converge();
            yield return Read(Around(Vector3.zero, 40));
            Line($"  Metall (Glaette 0,5): Mittel {V(_region.Mean)}, hellster Pixel {_region.MaxLuminance:0.000}");
            Check(_region.Luminance > 0.85f && _region.Luminance < 1.005f, $"weisses Metall ≤ 1 und kaum Verlust (ist {_region.Luminance:0.000})");

            s.GetComponent<MeshRenderer>().sharedMaterial = Mat(Color.white, 0.9f, 0f);
            _pt.Recapture();
            yield return Converge();
            yield return Read(Around(Vector3.zero, 40));
            Line($"  Lack (Glaette 0,9): Mittel {V(_region.Mean)}, hellster Pixel {_region.MaxLuminance:0.000}");
            Check(_region.Luminance > 0.9f && _region.Luminance < 1.01f, $"glaenzend weiss ≈ 1 (ist {_region.Luminance:0.000})");
            Check(_pt.Stats.NaNSamples == 0, $"keine ungueltigen Proben (NaN/Inf/negativ: {_pt.Stats.NaNSamples})");
        }

        // ------------------------------------------------------------------ Test 2: Cornell-Box
        private IEnumerator Cornell()
        {
            Line("");
            Line("Test 2 – Cornell-Box (indirektes Licht, Farbuebertrag, weiche Schatten)");
            Clear();
            BuildCornell();
            Configure(null);
            yield return Converge();
            yield return Read(Around(new Vector3(-0.85f, 0.02f, 0.0f), 12));
            var nearRed = _region;
            yield return Read(Around(new Vector3(0.85f, 0.02f, 0.0f), 12));
            var nearGreen = _region;
            yield return Read(Around(new Vector3(0f, 1.0f, 0.99f), 20));
            var back = _region;
            yield return Read(Around(new Vector3(0.05f, 0.02f, -0.75f), 12));
            var center = _region;
            Line($"  Boden an roter Wand {V(nearRed.Mean)}, an gruener Wand {V(nearGreen.Mean)}, Rueckwand {V(back.Mean)}");
            Check(nearRed.Invalid == 0 && nearGreen.Invalid == 0 && _pt.Stats.NaNSamples == 0, $"keine ungueltigen Proben ({_pt.Stats.NaNSamples})");
            float rg(Vector3 v) => v.x / Mathf.Max(v.y, 1e-5f);
            Line($"  Rot/Gruen-Verhaeltnis: rote Seite {rg(nearRed.Mean):0.00}, Mitte {rg(center.Mean):0.00}, gruene Seite {rg(nearGreen.Mean):0.00}");
            Check(rg(nearRed.Mean) > rg(center.Mean) * 1.1f, "Farbuebertrag: Boden neben roter Wand roetlicher als die Mitte");
            Check(rg(nearGreen.Mean) < rg(center.Mean) / 1.1f, "Farbuebertrag: Boden neben gruener Wand gruenlicher als die Mitte");
            Check(back.Luminance > 0.02f && back.Luminance < 5f, $"Rueckwand plausibel hell ({back.Luminance:0.000})");
            Check(_pt.Stats.EmissiveTriangles == 2, $"Deckenlicht als leuchtende Dreiecke gesampelt ({_pt.Stats.EmissiveTriangles})");
            yield return Save("02_cornell");
        }

        private void BuildCornell()
        {
            Material white = Mat(new Color(0.73f, 0.73f, 0.73f)), red = Mat(new Color(0.65f, 0.05f, 0.05f)), green = Mat(new Color(0.12f, 0.45f, 0.15f));
            Box(new Vector3(0, -0.01f, 0), new Vector3(2.04f, 0.02f, 2.04f), white);   // Boden
            Box(new Vector3(0, 2.01f, 0), new Vector3(2.04f, 0.02f, 2.04f), white);    // Decke
            Box(new Vector3(0, 1f, 1.01f), new Vector3(2.04f, 2.04f, 0.02f), white);   // Rueckwand
            Box(new Vector3(-1.01f, 1f, 0), new Vector3(0.02f, 2.04f, 2.04f), red);    // links
            Box(new Vector3(1.01f, 1f, 0), new Vector3(0.02f, 2.04f, 2.04f), green);   // rechts
            Box(new Vector3(0.33f, 0.3f, -0.3f), new Vector3(0.6f, 0.6f, 0.6f), white, new Vector3(0, -17, 0));
            Box(new Vector3(-0.35f, 0.6f, 0.3f), new Vector3(0.6f, 1.2f, 0.6f), white, new Vector3(0, 17, 0));
            // Deckenlicht: flacher leuchtender Quader (Strahldichte wie im Original ~ (17, 12, 4))
            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Quad);   // einseitig, leuchtet nach unten
            Destroy(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(_root, false);
            lamp.transform.position = new Vector3(0, 1.999f, 0);
            lamp.transform.eulerAngles = new Vector3(-90f, 0, 0);
            lamp.transform.localScale = new Vector3(0.5f, 0.4f, 1f);
            lamp.GetComponent<MeshRenderer>().sharedMaterial = Mat(Color.black, 0f, 0f, new Color(17f, 12f, 4f));
            Look(new Vector3(0, 1f, -3.9f), new Vector3(0, 1f, 0), 38f);
        }

        // ------------------------------------------------------------------ Test 3: Metall/Rauheit
        private IEnumerator MetalRoughness()
        {
            Line("");
            Line("Test 3 – Metall/Rauheit (Reihen: Metall 0 → 1, Spalten: Glaette 0 → 1)");
            Clear();
            Box(new Vector3(0, -1.2f, 0), new Vector3(20f, 0.02f, 20f), Mat(new Color(0.35f, 0.35f, 0.35f)));
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 5; col++)
                Sphere(new Vector3(-2f + col, 1.6f - row * 1.05f, 0), 0.42f, Mat(new Color(0.9f, 0.62f, 0.35f), col / 4f, row / 2f));
            Sun(new Vector3(40, -30, 0), new Color(1f, 0.95f, 0.88f), 2f);
            Look(new Vector3(0, 0.6f, -6.8f), new Vector3(0, 0.45f, 0), 42f);
            Configure(c => { c.environment = PathTraceEnvironment.Verlauf; c.filterGlossy = 0.3f; });
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.55f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.6f, 0.6f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.18f, 0.16f);
            _pt.Recapture();
            yield return Converge();
            Check(_pt.Stats.NaNSamples == 0, $"keine ungueltigen Proben ({_pt.Stats.NaNSamples})");
            // Metall glatt zeigt die Umgebung (dunkler Boden unten, heller Himmel oben) – Kante oben heller als unten
            yield return Read(Around(new Vector3(2f, 1.6f + 0.3f, -0.3f), 4));
            float top = _region.Luminance;
            yield return Read(Around(new Vector3(2f, 1.6f - 0.3f, -0.3f), 4));
            float bottom = _region.Luminance;
            Line($"  glaette 1, dielektrisch: oben {top:0.000}, unten {bottom:0.000}");
            yield return Read(Around(new Vector3(-2f, -0.5f, -0.42f), 5));
            var roughMetal = _region;
            yield return Read(Around(new Vector3(-2f, 1.6f, -0.42f), 5));
            var roughDiel = _region;
            Line($"  rau: Metall {V(roughMetal.Mean)}, dielektrisch {V(roughDiel.Mean)}");
            Check(roughMetal.Luminance > 0.01f && roughDiel.Luminance > 0.01f, "rauhe Kugeln beleuchtet");
            yield return Save("03_metall_rauheit");
        }

        // ------------------------------------------------------------------ Test 4: Glas
        private IEnumerator Glass()
        {
            Line("");
            Line("Test 4 – Glas (Brechzahl 1,0 / 1,33 / 1,5 / 2,0, Faerbung nach Weglaenge)");
            Clear();
            // Karierter Hintergrund, damit Brechung sichtbar wird
            for (int i = 0; i < 12; i++)
            for (int j = 0; j < 6; j++)
                Box(new Vector3(-5.5f + i, 0.5f + j, 3f), new Vector3(1f, 1f, 0.1f), Mat((i + j) % 2 == 0 ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.1f, 0.12f, 0.2f)));
            Box(new Vector3(0, -0.01f, 0), new Vector3(14f, 0.02f, 8f), Mat(new Color(0.5f, 0.45f, 0.4f)));
            float[] iors = { 1.0f, 1.33f, 1.5f, 2.0f };
            GameObject[] spheres = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                spheres[i] = Sphere(new Vector3(-2.7f + i * 1.8f, 1.5f, 0), 0.7f, Mat(Color.white, 1f));
                var g = spheres[i].AddComponent<PathTraceSurface>();
                g.kind = PathTraceSurface.Kind.Glas;
                g.ior = iors[i];
                g.absorptionColor = i == 3 ? new Color(0.55f, 0.8f, 0.95f) : Color.white;
                g.absorptionDistance = 1f;
            }
            Sun(new Vector3(45, 20, 0), Color.white, 2f);
            Look(new Vector3(0, 1.6f, -6f), new Vector3(0, 1.5f, 0), 45f);
            Configure(c => { c.environment = PathTraceEnvironment.Gleichmaessig; c.uniformEnvironment = new Color(0.6f, 0.65f, 0.75f); c.maxBounces = 12; c.filterGlossy = 0.3f; });
            yield return Converge();
            Check(_pt.Stats.NaNSamples == 0, $"keine ungueltigen Proben ({_pt.Stats.NaNSamples})");
            yield return Read(Around(spheres[0].transform.position, 18));
            var withGlass = _region;
            yield return Save("04_glas");
            spheres[0].SetActive(false);
            _pt.Recapture();
            yield return Converge();
            yield return Read(Around(new Vector3(-2.7f, 1.5f, 0), 18));
            float diff = Mathf.Abs(withGlass.Luminance - _region.Luminance) / Mathf.Max(_region.Luminance, 1e-3f);
            Line($"  Brechzahl 1,0: mit Kugel {withGlass.Luminance:0.000}, ohne {_region.Luminance:0.000} (Abweichung {diff * 100:0.0} %)");
            Check(diff < 0.05f, "Kugel mit Brechzahl 1,0 ist unsichtbar (< 5 %)");
        }

        // ------------------------------------------------------------------ Test 5: kleines helles Licht (MIS)
        private IEnumerator SmallLight()
        {
            Line("");
            Line("Test 5 – kleines helles Licht: Licht-, BSDF- und MIS-Proben muessen dasselbe Mittel liefern; MIS am rauschaermsten");
            Clear();
            Box(new Vector3(0, -0.01f, 0), new Vector3(8f, 0.02f, 8f), Mat(new Color(0.25f, 0.25f, 0.25f), 0.85f));
            Box(new Vector3(0, 1.5f, 2.5f), new Vector3(8f, 3f, 0.02f), Mat(new Color(0.6f, 0.6f, 0.6f)));
            PointLight(new Vector3(0, 0.8f, 1.2f), new Color(1f, 0.85f, 0.7f), 3f, 0.08f);
            Look(new Vector3(0, 0.9f, -2.2f), new Vector3(0, 0.1f, 0.8f), 45f);
            Vector3 glossyPoint = new Vector3(0, 0f, -0.40f);    // Spiegelung des Lichts auf dem Boden (Spiegelpunkt Kamera↔Licht)
            Vector3 diffusePoint = new Vector3(-1.2f, 1.0f, 2.49f); // matte Wand
            var means = new List<(Vector3 g, Vector3 d, float eg, float ed)>();
            string[] names = { "MIS", "nur Licht", "nur BSDF" };
            for (int mode = 0; mode < 3; mode++)
            {
                int m = mode;
                Configure(c => { c.misMode = m; c.targetSamples = Mathf.Max(64, samples / 2); });
                yield return Converge();
                yield return Read(Around(glossyPoint, 10));
                var g = _region;
                yield return Read(Around(diffusePoint, 10));
                var d = _region;
                means.Add((g.Mean, d.Mean, g.RelError, d.RelError));
                Line($"  {names[m],-9}: Glanz {V(g.Mean)} Fehler {g.RelError * 100:0.0} % · matt {V(d.Mean)} Fehler {d.RelError * 100:0.0} %");
                if (m == 0) yield return Save("05_kleines_licht_mis");
            }
            float Lum(Vector3 v) => 0.2126f * v.x + 0.7152f * v.y + 0.0722f * v.z;
            float relDiff(float a, float b) => Mathf.Abs(a - b) / Mathf.Max(Mathf.Max(a, b), 1e-4f);
            Check(relDiff(Lum(means[0].g), Lum(means[1].g)) < 0.06f && relDiff(Lum(means[0].d), Lum(means[1].d)) < 0.04f, "MIS = nur Licht (Mittel, ±6 % / ±4 %)");
            Check(relDiff(Lum(means[0].g), Lum(means[2].g)) < 0.1f && relDiff(Lum(means[0].d), Lum(means[2].d)) < 0.15f, "MIS = nur BSDF (Mittel, ±10 % / ±15 %)");
            Check(means[0].eg <= Mathf.Min(means[1].eg, means[2].eg) * 1.35f, "Glanzpunkt: MIS nahe an der besten Strategie");
            Check(means[0].ed <= Mathf.Min(means[1].ed, means[2].ed) * 1.35f, "matte Flaeche: MIS nahe an der besten Strategie");
            Check(means[0].ed < means[2].ed * 0.8f, "MIS deutlich rauschaermer als reine BSDF-Proben auf matter Flaeche");
        }

        // ------------------------------------------------------------------ Test 6: Sonne + Himmel
        private IEnumerator SunSky()
        {
            Line("");
            Line("Test 6 – Sonne mit Winkelgroesse + Himmel (Schatten, Umgebungslicht)");
            Clear();
            Box(new Vector3(0, -0.01f, 0), new Vector3(40f, 0.02f, 40f), Mat(new Color(0.45f, 0.42f, 0.38f)));
            Box(new Vector3(0, 2f, 0), new Vector3(0.4f, 4f, 0.4f), Mat(new Color(0.5f, 0.5f, 0.5f)));
            Light sun = Sun(new Vector3(35, 30, 0), new Color(1f, 0.93f, 0.82f), 2.2f);
            RenderSettings.ambientSkyColor = new Color(0.4f, 0.55f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.65f, 0.65f, 0.7f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.2f);
            Look(new Vector3(8f, 4f, -4f), new Vector3(1f, 0.5f, 1f), 50f);
            Configure(c => { c.environment = PathTraceEnvironment.Verlauf; });
            yield return Converge();
            Vector3 toSun = -sun.transform.forward;
            // Schattenpunkt: vom Saeulenfuss weg von der Sonne
            Vector3 shadow = new Vector3(-toSun.x, 0, -toSun.z).normalized * 2.5f;
            yield return Read(Around(shadow, 6));
            var inShadow = _region;
            yield return Read(Around(new Vector3(3f, 0, -1.5f), 6));
            var lit = _region;
            Line($"  Schatten {V(inShadow.Mean)}, besonnt {V(lit.Mean)}");
            Check(lit.Luminance > inShadow.Luminance * 2.5f, "Schatten der Saeule deutlich dunkler als besonnter Boden");
            Check(inShadow.Luminance > 0.01f, "Schatten nicht schwarz (Himmelslicht)");
            Check(_pt.Stats.NaNSamples == 0, $"keine ungueltigen Proben ({_pt.Stats.NaNSamples})");
            yield return Save("06_sonne_himmel");
        }

        // ------------------------------------------------------------------ Test 7: Lagerfeuer bei Nacht
        private IEnumerator Fire()
        {
            Line("");
            Line("Test 7 – Lagerfeuer bei Nacht (Golden Test): Feuerlicht, Glut, Metallruestung");
            Clear();
            Box(new Vector3(0, -0.01f, 0), new Vector3(30f, 0.02f, 30f), Mat(new Color(0.3f, 0.27f, 0.23f)));
            Material wood = Mat(new Color(0.25f, 0.15f, 0.08f));
            Box(new Vector3(0, 0.08f, 0), new Vector3(0.9f, 0.12f, 0.12f), wood, new Vector3(0, 30, 0));
            Box(new Vector3(0, 0.08f, 0), new Vector3(0.9f, 0.12f, 0.12f), wood, new Vector3(0, -40, 0));
            Box(new Vector3(0, 0.05f, 0), new Vector3(0.35f, 0.06f, 0.35f), Mat(Color.black, 0f, 0f, new Color(4f, 1.2f, 0.25f)));   // Glut
            PointLight(new Vector3(0, 0.45f, 0), new Color(1f, 0.55f, 0.22f), 4f, 0.22f);
            GameObject armor = Sphere(new Vector3(1.2f, 0.6f, 0.4f), 0.45f, Mat(new Color(0.75f, 0.75f, 0.78f), 0.75f, 1f));
            Box(new Vector3(-1.5f, 0.9f, 0.8f), new Vector3(0.5f, 1.8f, 0.5f), Mat(new Color(0.55f, 0.5f, 0.45f)));
            Look(new Vector3(0.2f, 1.3f, -3.2f), new Vector3(0.3f, 0.35f, 0.3f), 50f);
            Configure(c => { c.environment = PathTraceEnvironment.Gleichmaessig; c.uniformEnvironment = new Color(0.02f, 0.025f, 0.05f); c.filterGlossy = 0.3f; });
            yield return Converge();
            yield return Read(Around(new Vector3(0.9f, 0f, -0.3f), 8));
            var near = _region;
            yield return Read(Around(new Vector3(3.5f, 0f, 2.5f), 8));
            var far = _region;
            yield return Read(Around(armor.transform.position + new Vector3(-0.3f, -0.1f, -0.3f), 4));
            var armorFacing = _region;
            Line($"  Boden nah {V(near.Mean)}, weit {V(far.Mean)}, Ruestung zum Feuer {V(armorFacing.Mean)}");
            Check(near.Luminance > far.Luminance * 3f, "Feuerlicht faellt mit der Entfernung ab");
            Check(near.Mean.x > near.Mean.z * 1.5f, "warmes Feuerlicht");
            Check(armorFacing.Luminance > 0.02f, "Metall spiegelt Feuer/Glut");
            Check(_pt.Stats.NaNSamples == 0, $"keine ungueltigen Proben ({_pt.Stats.NaNSamples})");
            yield return Save("07_lagerfeuer_nacht");
        }

        // ------------------------------------------------------------------ Reproduzierbarkeit (§54)
        private IEnumerator Reproducible()
        {
            Line("");
            Line("Reproduzierbarkeit – gleiche Szene, gleicher Seed, gleiche Probenzahl → gleiches Bild");
            Clear();
            BuildCornell();
            Configure(c => c.targetSamples = 32);
            yield return Converge();
            RectInt all = new RectInt(0, 0, _pt.Stats.Width, _pt.Stats.Height);
            yield return Read(all);
            var a = _region;
            _pt.ResetAccumulation();
            yield return Converge();
            yield return Read(all);
            var b = _region;
            Line($"  Lauf 1 {V(a.Mean)} · Lauf 2 {V(b.Mean)}");
            Check((a.Mean - b.Mean).sqrMagnitude < 1e-14f && a.Pixels == b.Pixels, "beide Laeufe identisch");
            _pt.settings.globalSeed = 7;
            _pt.ResetAccumulation();
            yield return Converge();
            yield return Read(all);
            Check((a.Mean - _region.Mean).sqrMagnitude > 0f, "anderer Seed → andere Proben");
        }

        // ------------------------------------------------------------------ Rechenzeit (§114)
        private IEnumerator Timing()
        {
            Line("");
            Line("Rechenzeit (Cornell-Box, diese Bildgroesse)");
            Clear();
            BuildCornell();
            foreach (PathTracePreset p in new[] { PathTracePreset.Preview, PathTracePreset.PhotoHigh, PathTracePreset.PhotoUltra })
            {
                Configure(c => { c.ApplyPreset(p); c.targetSamples = 0; c.adaptive = false; c.denoise = PathTraceDenoise.Aus; c.environment = PathTraceEnvironment.Aus; });
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 3f) yield return null;
                float sum = 0; int n = 0; float rays = 0;
                t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 2f)
                {
                    yield return null;
                    sum += _pt.Stats.GpuMs; n++;
                    rays = _pt.Stats.PixelsTracedLastFrame > 0 ? _pt.Stats.RaysLastFrame / (float)_pt.Stats.PixelsTracedLastFrame : 0;
                }
                float ms = n > 0 ? sum / n : 0;
                Line($"  {p,-10}: {_pt.settings.samplesPerFrame} Proben/Bild, {_pt.settings.maxBounces} Abpraller → GPU {ms:0.0} ms/Bild, {rays:0.0} Strahlen je Pixel, RTAS {_pt.Stats.RtasBytes / 1024} KB");
            }
            Check(_pt.Stats.GpuMs > 0f, "GPU-Zeit wird gemessen");
        }

        // ------------------------------------------------------------------ Entrauschen (§73–78)
        private Vector3[] _img;
        private IEnumerator Image(bool denoised)
        {
            bool ready = false;
            _img = null;
            _pt.ReadImage(denoised, (img, w, h) => { _img = img; ready = true; });
            float t0 = Time.realtimeSinceStartup;
            while (!ready && Time.realtimeSinceStartup - t0 < 10f) yield return null;
        }

        private static float Rmse(Vector3[] a, Vector3[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return float.MaxValue;
            double e = 0;
            for (int i = 0; i < a.Length; i++)
            {
                // im Tonemapping-Raum vergleichen (x/(1+x)), damit helle Lichter nicht alles dominieren
                Vector3 p = new Vector3(a[i].x / (1 + a[i].x), a[i].y / (1 + a[i].y), a[i].z / (1 + a[i].z));
                Vector3 q = new Vector3(b[i].x / (1 + b[i].x), b[i].y / (1 + b[i].y), b[i].z / (1 + b[i].z));
                e += (p - q).sqrMagnitude / 3.0;
            }
            return Mathf.Sqrt((float)(e / a.Length));
        }

        private IEnumerator DenoiseTest()
        {
            Line("");
            Line("Entrauschen – A-Trous auf albedo-bereinigtem Licht: weniger Fehler gegen die Referenz, Kanten bleiben");
            Clear();
            BuildCornell();
            Configure(c => { c.targetSamples = 2048; c.samplesPerFrame = 8; });
            yield return Converge(180f);
            yield return Image(false);
            Vector3[] reference = _img;
            // Leicht (Foto) bei 16 Proben, Stark (Vorschau) bei 2 Proben – jeweils gegen das rohe Bild gleicher Probenzahl
            foreach (var (mode, spp) in new[] { (PathTraceDenoise.Leicht, 16), (PathTraceDenoise.Stark, 2) })
            {
                Configure(c => { c.targetSamples = spp; c.samplesPerFrame = Mathf.Min(4, spp); c.denoise = PathTraceDenoise.Aus; });
                yield return Converge();
                yield return Image(false);
                float noisy = Rmse(_img, reference);
                yield return Save($"08_entrauschen_{spp}spp_roh");
                Configure(c => { c.targetSamples = spp; c.samplesPerFrame = Mathf.Min(4, spp); c.denoise = mode; });
                yield return Converge();
                yield return null; yield return null;
                yield return Image(true);
                float dn = Rmse(_img, reference);
                Line($"  {spp} Proben: roh {noisy:0.0000}, {mode} {dn:0.0000} (Fehler gegen 2048-Proben-Referenz)");
                Check(dn < noisy * 0.7f, $"{mode}: Fehler mindestens 30 % kleiner");
                yield return Save($"08_entrauschen_{spp}spp_" + mode.ToString().ToLowerInvariant());
            }
            // Bei vielen Proben darf der Entrauscher das Bild kaum veraendern
            Configure(c => { c.targetSamples = 1024; c.samplesPerFrame = 8; c.denoise = PathTraceDenoise.Leicht; });
            yield return Converge(120f);
            yield return null; yield return null;
            yield return Image(true);
            float late = Rmse(_img, reference);
            Line($"  1024 Proben + Leicht: Fehler {late:0.0000}");
            Check(late < 0.012f, "konvergiertes Bild bleibt praktisch unveraendert");
        }

        // ------------------------------------------------------------------ Adaptive Abtastung (§67–70, §120–121)
        private IEnumerator AdaptiveTest()
        {
            Line("");
            Line("Adaptive Abtastung – konvergierte Kacheln hoeren auf, Stopp bei Konvergenz statt fester Probenzahl");
            Clear();
            BuildCornell();
            Configure(c => { c.adaptive = true; c.adaptiveMinSamples = 64; c.convergenceTarget = 0.03f; c.targetSamples = 4096; c.samplesPerFrame = 8; });
            float t0 = Time.realtimeSinceStartup;
            float minShare = 1f;
            while (!_pt.Stats.Done && Time.realtimeSinceStartup - t0 < 120f)
            {
                yield return null;
                if (_pt.Stats.ActiveTilesValid) minShare = Mathf.Min(minShare, _pt.Stats.ActiveShare);
            }
            var st = _pt.Stats;
            Line($"  fertig: {st.Done} nach {Time.realtimeSinceStartup - t0:0.0} s, hoechste Probenzahl {st.Samples}, kleinster aktiver Anteil {minShare * 100:0} %");
            Check(minShare < 0.9f, "einfache Bereiche bekommen irgendwann keine Proben mehr");
            Check(st.Done, "Bild wird als fertig erkannt");
            yield return Read(Around(new Vector3(0f, 1.0f, 0.99f), 20));
            Line($"  Rueckwand: Fehler {_region.RelError * 100:0.0} %, Proben {_region.MinSamples}–{_region.MaxSamples}");
            Check(_region.RelError < 0.06f, "Rueckwand erreicht das Konvergenzziel (± Streuung)");
            yield return Save("09_adaptiv");
        }
    }
}
