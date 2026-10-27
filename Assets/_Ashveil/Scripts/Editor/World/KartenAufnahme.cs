using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Maps;
using Ashveil.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Karten r2 (#100, Max 08.10.2026 „Aufnahme von oben“): Minimap-Kacheln und Gebietskarte aus dem gebauten Kareth v4,
    /// Kamera senkrecht von oben, ohne Play. Zwei Aufträge für den Auftragsordner:
    ///   „karten_aufnahme“  Kacheln aus karten_unity.json (1 024 m, 512 px = 2 m je Pixel, zweifach überabgetastet) nach
    ///                      StreamingAssets/Ashveil/Karten/minimap/m_i_j.jpg und daraus die Gebietskarte (Rahmen des Gebiets,
    ///                      2 048 px) nach gebietskarten/kareth.jpg.
    ///   „karten_pruefen“   Daten, Szenenraster, Bilder und Größen prüfen; Prüfbild nach Logs/Ashveil/Karten.
    /// Aufnahme bei Mittagslicht über die Vorschau des Tageslaufs (Stil A), ohne Nebel, Vignette, Bloom und Gras, alle Bäume und
    /// Gebäude in voller Detailstufe, Schatten über die ganze Höhe. Die Szene bleibt unverändert: Sie wird vorher frisch geöffnet und
    /// danach von der Platte neu geladen; Projektwerte (Detailstufen, Schattenweite) werden zurückgesetzt.
    /// Die Bilder sind Ergebnis dieses Werkzeugs (ein neuer Lauf ersetzt sie); andere Dateien werden nicht berührt.
    /// </summary>
    public static class KartenAufnahme
    {
        public const string Ordner = "Assets/StreamingAssets/Ashveil/Karten";
        private const int Ueberabtastung = 2;
        private const float Mittag = 12f;
        private static readonly Color Leer = new Color(0.16f, 0.27f, 0.36f);   // wie „keine Kachel“ in der Minimap

        [InitializeOnLoadMethod]
        private static void Registrieren()
        {
            Auftragsordner.Registrieren("karten_aufnahme", (a, fertig) => fertig(Aufnehmen(out string m), m), true, 1800);
            Auftragsordner.Registrieren("karten_pruefen", (a, fertig) => fertig(Pruefen(out string m), m), false, 600);
        }

        [MenuItem("Ashveil/Karten/Kareth v4: Aufnahme von oben (Minimap, Gebietskarte)", priority = 600)]
        private static void AufnehmenMenue()
        {
            bool ok = Aufnehmen(out string m);
            if (ok) Debug.Log("[Karten] " + m);
            else EditorUtility.DisplayDialog("Ashveil – Karten", m, "OK");
        }

        [MenuItem("Ashveil/Karten/Kartendaten prüfen", priority = 601)]
        private static void PruefenMenue()
        {
            bool ok = Pruefen(out string m);
            Debug.Log("[Karten] " + m);
            if (!ok) EditorUtility.DisplayDialog("Ashveil – Karten", m, "OK");
        }

        private static string Voll => Path.GetFullPath(Ordner);

        // ================================================================== Aufnahme

        public static bool Aufnehmen(out string meldung)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { meldung = "Play läuft – keine Aufnahme"; return false; }
            if (!File.Exists(KarethV4Builder.ScenePath)) { meldung = "Kareth v4 ist noch nicht gebaut (Auftrag kareth_v4_bauen)"; return false; }
            if (EditorSceneManager.GetActiveScene().isDirty) { meldung = "ungespeicherte Änderungen in der offenen Szene – nichts aufgenommen"; return false; }
            string ordner = Voll;
            ZonenDienst d = ZonenDienst.Laden(ordner, out string fehler);
            if (d == null) { meldung = fehler; return false; }
            if (!d.LadeSzene(KarethV4Spiel.KartenSzenenRaster, out fehler)) { meldung = fehler; return false; }
            ZoneDaten zone = d.SzenenZone;
            // 13.10.2026: Weltzellen (Westweiden) mit eigenem Szenenraster – ihre Kacheln und ihre Gebietskarte entstehen im selben Lauf
            bool zelle = KarethV4Weidepass.Aktiv && d.LadeWeitereSzene(KarethV4Spiel.KartenWestweidenRaster, out _);
            var gebiete = new List<ZoneDaten> { zone };
            if (zelle) gebiete.Add(d.ZoneNachKey("westweiden"));
            int[] ij = d.Daten.minimap_kacheln_ij;
            float T = d.Daten.minimap_kachel_m > 0f ? d.Daten.minimap_kachel_m : 1024f;
            int px = d.Daten.minimap_kachel_px > 0 ? d.Daten.minimap_kachel_px : 512;
            if (ij == null || ij.Length < 2) { meldung = "karten_unity.json nennt keine Minimap-Kacheln"; return false; }
            if (zone.rahmen == null || zone.rahmen.Length < 4) { meldung = $"Gebiet {zone.name} ohne Kartenrahmen"; return false; }
            Vector2 versatz = KarethV4Spiel.KartenWeltversatz;

            EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();
            if (zelle) EditorSceneManager.OpenScene(WestweidenZelle.ScenePath, OpenSceneMode.Additive);
            Terrain[] gelaende = Terrain.activeTerrains;
            if (terrain == null) { meldung = "kein Gelände in Kareth v4"; return false; }
            DayNightCycle cycle = Object.FindFirstObjectByType<DayNightCycle>();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float schattenVorher = urp != null ? urp.shadowDistance : 0f;
            float lodVorher = QualitySettings.lodBias;
            bool vorschau = false;
            GameObject camGo = null, volGo = null;
            VolumeProfile profil = null;
            RenderTexture rt = null;
            Texture2D lese = null, bild = null;
            var t0 = DateTime.Now;
            long bytes = 0;
            try
            {
                // Licht: Mittag (Stil A über die Vorschau des Tageslaufs); ohne Tageslauf das gestaltete Licht der Szene
                if (cycle != null && (vorschau = cycle.PreviewBegin())) cycle.PreviewAt(Mittag);
                // Gelände: alles in voller Auflösung, kein Gras (gleichmäßig über die Kachel), keine Partikel, kein Nebel
                foreach (Terrain tg in gelaende)
                {
                    tg.treeDistance = 20000f;
                    tg.treeBillboardDistance = 20000f;
                    tg.treeMaximumFullLODCount = 1000000;
                    tg.basemapDistance = 20000f;
                    tg.detailObjectDistance = 0f;
                    tg.heightmapPixelError = 1f;
                }
                QualitySettings.lodBias = Mathf.Max(lodVorher, 50f);
                RenderSettings.fog = false;
                foreach (ParticleSystemRenderer pr in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None)) pr.enabled = false;
                // Nachbearbeitung: Stil A bleibt (Ton, Farbe), Vignette, Bloom, Tiefenschärfe und Bewegungsunschärfe aus
                volGo = new GameObject("Kartenaufnahme (Nachbearbeitung)") { hideFlags = HideFlags.HideAndDontSave };
                var vol = volGo.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.priority = 10000f;
                profil = ScriptableObject.CreateInstance<VolumeProfile>();
                profil.Add<Vignette>(true).intensity.Override(0f);
                profil.Add<Bloom>(true).intensity.Override(0f);
                profil.Add<DepthOfField>(true).mode.Override(DepthOfFieldMode.Off);
                profil.Add<MotionBlur>(true).intensity.Override(0f);
                profil.Add<ChromaticAberration>(true).intensity.Override(0f);
                profil.Add<FilmGrain>(true).intensity.Override(0f);
                profil.Add<LensDistortion>(true).intensity.Override(0f);
                vol.sharedProfile = profil;

                camGo = new GameObject("Kartenaufnahme (Kamera)") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = T * 0.5f;
                cam.aspect = 1f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Leer;
                cam.allowHDR = true;
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);    // Norden oben, Osten rechts
                int vorschauEbene = LayerMask.NameToLayer(Ashveil.Gameplay.Visuals.CharacterPreviewStage.LayerName);
                if (vorschauEbene >= 0) cam.cullingMask &= ~(1 << vorschauEbene);
                int ui = LayerMask.NameToLayer("UI");
                if (ui >= 0) cam.cullingMask &= ~(1 << ui);
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.renderShadows = true;
                int roh = px * Ueberabtastung;
                rt = new RenderTexture(roh, roh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                lese = new Texture2D(roh, roh, TextureFormat.RGB24, false);
                bild = new Texture2D(px, px, TextureFormat.RGB24, false);

                // Höchste Objekte je Kachel (Bäume, Türme, Felsen über dem Gelände): Kamera knapp darüber. Was ganz über dem Gelände
                // schwebt (Wolken- oder Himmelsschichten), bleibt für die Aufnahme aus – es würde die Karte verdecken.
                float oberkante = float.MinValue;
                foreach (Terrain tg in gelaende) oberkante = Mathf.Max(oberkante, tg.transform.position.y + tg.terrainData.size.y);
                var hoch = new List<Bounds>();
                int schwebend = 0;
                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                    Bounds b = r.bounds;
                    if (b.min.y > oberkante + 150f) { r.enabled = false; schwebend++; continue; }
                    if (b.size.x < 3000f && b.size.z < 3000f) hoch.Add(b);
                }
                if (schwebend > 0) Debug.Log($"[Karten] Aufnahme: {schwebend} schwebende Objekte über dem Gelände ausgeblendet.");

                Directory.CreateDirectory(Path.Combine(ordner, "minimap"));
                Directory.CreateDirectory(Path.Combine(ordner, "gebietskarten"));
                float mpp = T / px;
                // je Gebiet ein Mosaik in seinem Kartenrahmen (gleiche Auflösung wie die Kacheln)
                var mosaike = new List<(ZoneDaten z, float[] rahmen, int gw, int gh, Color32[] bild)>();
                foreach (ZoneDaten gz in gebiete)
                {
                    if (gz == null || gz.rahmen == null || gz.rahmen.Length < 4) continue;
                    float[] r = gz.rahmen;
                    int w = Mathf.RoundToInt((r[2] - r[0]) / mpp), h = Mathf.RoundToInt((r[3] - r[1]) / mpp);
                    var m = new Color32[w * h];
                    for (int q = 0; q < m.Length; q++) m[q] = Leer;
                    mosaike.Add((gz, r, w, h, m));
                }
                // Hecken der Westweiden: aus der Höhe sind die senkrechten Blattkarten kaum ein Pixel breit – die Linien werden aus
                // WW_Hecken.csv nachgezeichnet (dunkleres Grün auf dem Bild), damit die Heckenflur auf der Karte lesbar bleibt.
                List<Vector4> hecken = zelle ? HeckenStriche(versatz) : new List<Vector4>();
                int kacheln = 0;
                for (int k = 0; k + 1 < ij.Length; k += 2)
                {
                    int i = ij[k], j = ij[k + 1];
                    EditorUtility.DisplayProgressBar("Ashveil – Karten", $"Aufnahme von oben: Kachel {i}/{j}", (float)k / ij.Length);
                    Vector3 mitte = new Vector3((i + 0.5f) * T - versatz.x, 0f, (j + 0.5f) * T - versatz.y);
                    Hoehen(gelaende, hoch, mitte, T, out float hMin, out float hMax);
                    float y = hMax + 30f;
                    cam.transform.position = new Vector3(mitte.x, y, mitte.z);
                    cam.nearClipPlane = 0.5f;
                    cam.farClipPlane = y - hMin + 150f;
                    if (urp != null) urp.shadowDistance = Mathf.Max(schattenVorher, cam.farClipPlane);
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                    else cam.Render();
                    RenderTexture vorher = RenderTexture.active;
                    RenderTexture.active = rt;
                    lese.ReadPixels(new Rect(0, 0, roh, roh), 0, 0);
                    lese.Apply(false);
                    RenderTexture.active = vorher;
                    Color32[] klein = Verkleinern(lese.GetPixels32(), roh, Ueberabtastung);
                    if (hecken.Count > 0) HeckenZeichnen(klein, px, i * T, j * T, mpp, hecken);
                    bild.SetPixels32(klein);
                    bild.Apply(false);
                    byte[] jpg = bild.EncodeToJPG(88);
                    File.WriteAllBytes(Path.Combine(ordner, "minimap", $"m_{i}_{j}.jpg"), jpg);
                    bytes += jpg.Length;
                    kacheln++;
                    // in die Gebietskarten (gleiche Auflösung, Rahmen des Gebiets)
                    foreach (var mo in mosaike)
                    {
                        int ox = Mathf.RoundToInt((i * T - mo.rahmen[0]) / mpp), oz = Mathf.RoundToInt((j * T - mo.rahmen[1]) / mpp);
                        for (int yy = 0; yy < px; yy++)
                        {
                            int gy = oz + yy;
                            if (gy < 0 || gy >= mo.gh) continue;
                            for (int xx = 0; xx < px; xx++)
                            {
                                int gx = ox + xx;
                                if (gx < 0 || gx >= mo.gw) continue;
                                mo.bild[gy * mo.gw + gx] = klein[yy * px + xx];
                            }
                        }
                    }
                }
                var namen = new List<string>();
                foreach (var mo in mosaike)
                {
                    var gebiet = new Texture2D(mo.gw, mo.gh, TextureFormat.RGB24, false);
                    gebiet.SetPixels32(mo.bild);
                    gebiet.Apply(false);
                    byte[] gk = gebiet.EncodeToJPG(90);
                    Object.DestroyImmediate(gebiet);
                    File.WriteAllBytes(Path.Combine(ordner, "gebietskarten", mo.z.key + ".jpg"), gk);
                    bytes += gk.Length;
                    namen.Add($"{mo.z.name} ({mo.gw} × {mo.gh} px)");
                }
                meldung = $"Aufnahme von oben: {kacheln} Minimap-Kacheln ({px} px, {mpp:0.#} m je Pixel) und Gebietskarten {string.Join(", ", namen)}, " +
                          $"{bytes / 1048576f:0.0} MB, {(DateTime.Now - t0).TotalSeconds:0} s; Licht {(vorschau ? "12:00 (Tageslauf-Vorschau)" : "der Szene")} → {Ordner}";
            }
            catch (Exception e)
            {
                meldung = "Aufnahme fehlgeschlagen: " + e.GetType().Name + ": " + e.Message;
                Debug.LogException(e);
                return false;
            }
            finally
            {
                if (urp != null) urp.shadowDistance = schattenVorher;
                QualitySettings.lodBias = lodVorher;
                if (vorschau) cycle.PreviewEnd();
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (volGo != null) Object.DestroyImmediate(volGo);
                if (profil != null) Object.DestroyImmediate(profil);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (lese != null) Object.DestroyImmediate(lese);
                if (bild != null) Object.DestroyImmediate(bild);
                EditorUtility.ClearProgressBar();
                // Frisch von der Platte: verwirft alles, was die Aufnahme im Speicher verändert hat (Licht, Gelände, Partikel, Nebel)
                EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            }
            Debug.Log("[Karten] " + meldung);
            return true;
        }

        /// <summary>Niedrigster und höchster Punkt in der Kachel: Gelände (33 × 33 Proben) und alle Objekte darin.</summary>
        private static void Hoehen(Terrain[] gelaende, List<Bounds> objekte, Vector3 mitte, float T, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            float x0 = mitte.x - T * 0.5f, z0 = mitte.z - T * 0.5f;
            Vector3 tp = Vector3.zero, ts = Vector3.zero;
            foreach (Terrain terrain in gelaende)
            {
                tp = terrain.transform.position;
                ts = terrain.terrainData.size;
                if (tp.x > x0 + T || tp.x + ts.x < x0 || tp.z > z0 + T || tp.z + ts.z < z0) continue;   // Kachel außerhalb dieses Geländes
                for (int j = 0; j <= 32; j++)
                    for (int i = 0; i <= 32; i++)
                    {
                        float x = Mathf.Clamp(x0 + i * T / 32f, tp.x, tp.x + ts.x), z = Mathf.Clamp(z0 + j * T / 32f, tp.z, tp.z + ts.z);
                        float h = terrain.SampleHeight(new Vector3(x, 0f, z)) + tp.y;
                        min = Mathf.Min(min, h);
                        max = Mathf.Max(max, h);
                    }
            }
            if (min == float.MaxValue) { min = 0f; max = 0f; }
            foreach (Bounds b in objekte)
            {
                if (b.max.x < x0 || b.min.x > x0 + T || b.max.z < z0 || b.min.z > z0 + T) continue;
                max = Mathf.Max(max, Mathf.Min(b.max.y, tp.y + ts.y + 400f));
                min = Mathf.Min(min, Mathf.Max(b.min.y, tp.y - 200f));
            }
        }

        /// <summary>Kastenfilter n × n (Überabtastung → Kachelgröße).</summary>
        /// <summary>
        /// Heckenlinien der Westweiden in Welt-Kartenmetern (x1, z1, x2, z2): wie <see cref="WestweidenHecken"/> je Linie vom ersten zum
        /// letzten Punkt (kürzer als 4 m entfällt), Zellen-Koordinaten + Lage in Kareth + Weltversatz.
        /// </summary>
        private static List<Vector4> HeckenStriche(Vector2 versatz)
        {
            var res = new List<Vector4>();
            string pfad = Path.Combine(Path.GetDirectoryName(Application.dataPath), ProjectSetup.Root + "/Data/Westweiden/Entwurf/WW_Hecken.csv");
            if (!File.Exists(pfad)) return res;
            Vector3 u = KarethV4Weidepass.WwUrsprung;
            float ox = u.x + versatz.x, oz = u.z + versatz.y;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var linien = new Dictionary<int, (Vector2 a, Vector2 b)>();
            foreach (string line in File.ReadAllLines(pfad, Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3 || !int.TryParse(f[0], System.Globalization.NumberStyles.Integer, ci, out int id)) continue;
                if (!float.TryParse(f[1], System.Globalization.NumberStyles.Float, ci, out float x) ||
                    !float.TryParse(f[2], System.Globalization.NumberStyles.Float, ci, out float z)) continue;
                var p = new Vector2(x + ox, z + oz);
                linien[id] = linien.TryGetValue(id, out var l) ? (l.a, p) : (p, p);
            }
            foreach (var l in linien.Values)
                if (Vector2.Distance(l.a, l.b) >= 4f) res.Add(new Vector4(l.a.x, l.a.y, l.b.x, l.b.y));
            return res;
        }

        /// <summary>Heckenlinien in eine Kachel zeichnen (Zeile 0 = Süden): Kern ≈ 2,6 m breit, weicher Rand, Farbe = Bild abgedunkelt ins Heckengrün.</summary>
        private static void HeckenZeichnen(Color32[] bild, int px, float x0, float z0, float mpp, List<Vector4> striche)
        {
            const float Kern = 1.3f, Rand = 1.0f;   // Meter: halbe Heckenbreite, weicher Übergang
            var gruen = new Color(0.16f, 0.24f, 0.10f);
            float x1 = x0 + px * mpp, z1 = z0 + px * mpp, w = Kern + Rand;
            float[] deckung = null;
            foreach (Vector4 s in striche)
            {
                if (Mathf.Max(s.x, s.z) < x0 - w || Mathf.Min(s.x, s.z) > x1 + w || Mathf.Max(s.y, s.w) < z0 - w || Mathf.Min(s.y, s.w) > z1 + w) continue;
                if (deckung == null) deckung = new float[px * px];
                var a = new Vector2(s.x, s.y);
                var d = new Vector2(s.z - s.x, s.w - s.y);
                float ll = Mathf.Max(d.sqrMagnitude, 1e-4f);
                int ix0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(s.x, s.z) - w - x0) / mpp)), ix1 = Mathf.Min(px - 1, Mathf.CeilToInt((Mathf.Max(s.x, s.z) + w - x0) / mpp));
                int iz0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(s.y, s.w) - w - z0) / mpp)), iz1 = Mathf.Min(px - 1, Mathf.CeilToInt((Mathf.Max(s.y, s.w) + w - z0) / mpp));
                for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        var q = new Vector2(x0 + (ix + 0.5f) * mpp, z0 + (iz + 0.5f) * mpp);
                        float t = Mathf.Clamp01(Vector2.Dot(q - a, d) / ll);
                        float deck = 1f - Mathf.Clamp01((Vector2.Distance(q, a + d * t) - Kern) / Rand);
                        int n = iz * px + ix;
                        if (deck > deckung[n]) deckung[n] = deck;
                    }
            }
            if (deckung == null) return;
            for (int n = 0; n < deckung.Length; n++)
            {
                if (deckung[n] <= 0f) continue;
                Color c = bild[n];
                var ziel = new Color(c.r * 0.45f + gruen.r * 0.35f, c.g * 0.45f + gruen.g * 0.35f, c.b * 0.45f + gruen.b * 0.35f);
                bild[n] = Color.Lerp(c, ziel, deckung[n] * 0.85f);
            }
        }

        private static Color32[] Verkleinern(Color32[] gross, int seite, int n)
        {
            int k = seite / n;
            var r = new Color32[k * k];
            int nn = n * n;
            for (int y = 0; y < k; y++)
                for (int x = 0; x < k; x++)
                {
                    int sr = 0, sg = 0, sb = 0;
                    for (int dy = 0; dy < n; dy++)
                        for (int dx = 0; dx < n; dx++)
                        {
                            Color32 c = gross[(y * n + dy) * seite + x * n + dx];
                            sr += c.r; sg += c.g; sb += c.b;
                        }
                    r[y * k + x] = new Color32((byte)(sr / nn), (byte)(sg / nn), (byte)(sb / nn), 255);
                }
            return r;
        }

        // ================================================================== Prüfung

        public static bool Pruefen(out string meldung)
        {
            string ordner = Voll;
            var sb = new StringBuilder();
            int fehler = 0;
            void Fehler(string s) { fehler++; sb.Append(" · FEHLER ").Append(s); }

            ZonenDienst d = ZonenDienst.Laden(ordner, out string f);
            if (d == null) { meldung = "Kartendaten: " + f; return false; }
            sb.Append($"{d.Daten.zonen.Length} Gebiete, {d.AnzahlUnterzonen} Unterzonen (Stand {d.Daten.stand})");
            if (!d.LadeSzene(KarethV4Spiel.KartenSzenenRaster, out f)) { meldung = sb + " · FEHLER " + f; return false; }
            SzenenRasterDatei sz = d.Szene;
            ZoneDaten zone = d.SzenenZone;

            // Szenenraster: jede Zelle eine Unterzone des Szenengebiets
            var flaeche = new Dictionary<int, int>();
            int fremd = 0, leer = 0;
            for (int r = 0; r < sz.hoehe; r++)
                for (int c = 0; c < sz.breite; c++)
                {
                    float x = sz.x0 + (c + 0.5f) * sz.zelle_m, z = sz.z0 + (r + 0.5f) * sz.zelle_m;
                    UnterzoneDaten u = d.UnterzoneBei(x, z);
                    if (u == null) { leer++; continue; }
                    if (d.ZoneDerUnterzone(u.id) != zone) fremd++;
                    flaeche[u.id] = (flaeche.TryGetValue(u.id, out int n) ? n : 0) + 1;
                }
            sb.Append($" · Szenenraster {sz.name}: {sz.breite}×{sz.hoehe} je {sz.zelle_m:0} m, {flaeche.Count} Unterzonen");
            if (leer > 0) Fehler($"{leer} Zellen ohne Unterzone");
            if (fremd > 0) Fehler($"{fremd} Zellen mit Unterzonen eines anderen Gebiets");
            if (zone.unterzonen != null)
                foreach (UnterzoneDaten u in zone.unterzonen)
                    if (!flaeche.ContainsKey(u.id)) Fehler($"Unterzone {u.name} hat keine Fläche");
            if (d.UnterzoneNachId(sz.start_unterzone) == null) Fehler("Startunterzone fehlt");
            Vector2 v = KarethV4Spiel.KartenWeltversatz;
            if (!Mathf.Approximately(sz.weltversatz_x, v.x) || !Mathf.Approximately(sz.weltversatz_z, v.y))
                Fehler($"Weltversatz der Daten {sz.weltversatz_x:0}/{sz.weltversatz_z:0} ≠ Szene {v.x:0}/{v.y:0}");
            UnterzoneDaten amFeuer = d.UnterzoneBei(v.x, v.y);
            sb.Append($" · Wachtfeuer: {amFeuer?.name ?? "–"}");
            if (amFeuer == null || amFeuer.id != sz.start_unterzone) Fehler("das Wachtfeuer liegt nicht in der Startunterzone");

            // Orte des Gebiets
            if (zone.orte != null)
            {
                var ohne = zone.orte.Where(o => d.UnterzoneBei(o.x, o.z) == null).Select(o => o.name).ToList();
                sb.Append($" · {zone.orte.Length} Orte");
                if (ohne.Count > 0) Fehler("Orte außerhalb der Unterzonen: " + string.Join(", ", ohne));
            }

            // Bilder
            int[] ij = d.Daten.minimap_kacheln_ij ?? new int[0];
            int fehlend = 0;
            long groesse = 0;
            for (int k = 0; k + 1 < ij.Length; k += 2)
            {
                string p = Path.Combine(ordner, "minimap", $"m_{ij[k]}_{ij[k + 1]}.jpg");
                if (File.Exists(p)) groesse += new FileInfo(p).Length; else fehlend++;
            }
            sb.Append($" · Minimap {ij.Length / 2 - fehlend}/{ij.Length / 2} Kacheln ({groesse / 1048576f:0.0} MB)");
            if (fehlend > 0) Fehler($"{fehlend} Minimap-Kacheln fehlen (Auftrag karten_aufnahme)");
            string gk = Path.Combine(ordner, "gebietskarten", zone.key + ".jpg");
            Texture2D gebiet = null;
            if (!File.Exists(gk)) Fehler("Gebietskarte fehlt: " + gk);
            else
            {
                gebiet = new Texture2D(2, 2, TextureFormat.RGB24, false);
                gebiet.LoadImage(File.ReadAllBytes(gk));
                sb.Append($" · Gebietskarte {gebiet.width}×{gebiet.height}");
            }
            string welt = Path.Combine(ordner, "weltkarte_2048.jpg");
            if (!File.Exists(welt)) Fehler("Weltkarte fehlt");
            long gesamt = new DirectoryInfo(ordner).EnumerateFiles("*", SearchOption.AllDirectories).Where(fi => !fi.Name.EndsWith(".meta")).Sum(fi => fi.Length);
            sb.Append($" · zusammen {gesamt / 1048576f:0.0} MB");

            // Prüfbild: Gebietskarte mit Grenzen der Unterzonen, Namenspunkten und Orten
            if (gebiet != null)
            {
                try
                {
                    string bild = Pruefbild(d, zone, gebiet);
                    sb.Append(" · Prüfbild ").Append(bild);
                }
                catch (Exception e) { Fehler("Prüfbild: " + e.Message); }
                Object.DestroyImmediate(gebiet);
            }
            meldung = (fehler == 0 ? "Karten in Ordnung: " : $"{fehler} Fehler: ") + sb;
            return fehler == 0;
        }

        private static string Pruefbild(ZonenDienst d, ZoneDaten zone, Texture2D gebiet)
        {
            int w = gebiet.width, h = gebiet.height;
            Color32[] px = gebiet.GetPixels32();
            float[] r = zone.rahmen;
            float mx = (r[2] - r[0]) / w, mz = (r[3] - r[1]) / h;
            var id = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    id[y * w + x] = d.AufdeckungBei(r[0] + (x + 0.5f) * mx, r[1] + (y + 0.5f) * mz);
            var grenze = new Color32(20, 16, 10, 255);
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int k = y * w + x;
                    if (id[k] != id[k + 1] || id[k] != id[k + w]) { px[k] = grenze; px[k + 1] = grenze; px[k + w] = grenze; }
                }
            void Marke(float wx, float wz, Color32 c, int rad)
            {
                int cx = Mathf.RoundToInt((wx - r[0]) / mx), cy = Mathf.RoundToInt((wz - r[1]) / mz);
                for (int y = -rad; y <= rad; y++)
                    for (int x = -rad; x <= rad; x++)
                    {
                        if (x * x + y * y > rad * rad) continue;
                        int xx = cx + x, yy = cy + y;
                        if (xx >= 0 && yy >= 0 && xx < w && yy < h) px[yy * w + xx] = c;
                    }
            }
            if (zone.unterzonen != null)
                foreach (UnterzoneDaten u in zone.unterzonen)
                {
                    Vector2 np = d.Namenspunkt(u);
                    Marke(np.x, np.y, new Color32(255, 255, 255, 255), 9);
                    Marke(np.x, np.y, new Color32(30, 30, 30, 255), 5);
                }
            if (zone.orte != null)
                foreach (OrtDaten o in zone.orte) Marke(o.x, o.z, new Color32(240, 200, 90, 255), 8);
            if (zone.flugpunkte != null)
                foreach (PunktDaten fp in zone.flugpunkte) Marke(fp.x, fp.z, new Color32(140, 200, 250, 255), 8);
            var aus = new Texture2D(w, h, TextureFormat.RGB24, false);
            aus.SetPixels32(px);
            aus.Apply(false);
            string pfad = Ashveil.Core.ProjectPaths.Logs("Karten", $"Pruefbild_Gebietskarte_{zone.key}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(pfad));
            File.WriteAllBytes(pfad, aus.EncodeToPNG());
            Object.DestroyImmediate(aus);
            return pfad;
        }
    }
}
