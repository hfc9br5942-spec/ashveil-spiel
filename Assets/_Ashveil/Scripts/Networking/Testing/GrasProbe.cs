#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ashveil.Core;
using Ashveil.Core.Events;
using Ashveil.Gameplay.Items;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.Networking
{
    /// <summary>
    /// Gras-Probe (09.10.2026 nachts): Die Flacker-Probe zeigte, dass das Terrain-Gras im fertigen Spiel schwarz ist, im Editor
    /// aber grün. Diese Probe grenzt im fertigen Spiel ein, woran es liegt: Sie schreibt Material, Shader, Textur und Schlüsselwörter
    /// des Grases ins Log und macht je ein Farbbild (960×540, Logs/Ashveil/Gras/) mit (1) unverändert, (2) ohne Screen-Space-
    /// Irradiance-Schlüsselwort, (3) weißer Textur, (4) weißer Farbe, (5) URP-Lit-Shader, (6) Schatten-Schlüsselwörter aus.
    /// Alle Änderungen gelten nur im Speicher des laufenden Spiels. Nur mit „-ashveil-gras-probe“ (Auftrag „gras_probe“).
    /// </summary>
    public sealed class GrasProbe : MonoBehaviour
    {
        public const string Argument = "-ashveil-gras-probe";

        private GameObject _spieler;
        private string _ordner;
        private RenderTexture _voll, _mittel;
        private readonly StringBuilder _bericht = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isEditor || ArgWert(Argument) == null) return;
            var go = new GameObject("Gras-Probe");
            DontDestroyOnLoad(go);
            go.AddComponent<GrasProbe>();
        }

        private void OnEnable() => EventBus<LocalPlayerSpawnedEvent>.Subscribe(OnSpieler);
        private void OnDisable() => EventBus<LocalPlayerSpawnedEvent>.Unsubscribe(OnSpieler);
        private void OnSpieler(LocalPlayerSpawnedEvent e) { if (e.Player != null) _spieler = e.Player; }

        private IEnumerator Start()
        {
            // 10.10.2026: je Startpunkt ein Unterordner, damit mehrere Läufe nebeneinander bleiben
            string argOrdner = ArgWert(Argument);
            _ordner = Path.Combine(Application.dataPath, "..", "Logs", "Ashveil", "Gras", string.IsNullOrEmpty(argOrdner) ? "Standard" : argOrdner.Replace(':', '_').Replace('.', 'p'));
            try { Directory.CreateDirectory(_ordner); } catch (Exception e) { Bericht("FEHLER – Ordner: " + e.Message); }
            Bericht($"ASHVEIL Gras-Probe · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {Screen.width}×{Screen.height} · {SystemInfo.graphicsDeviceType}");

            float bis = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < bis && !SpielerBereit()) yield return null;
            if (!SpielerBereit()) { Bericht("FEHLER – keine Spielfigur nach 150 s"); yield return Ende(); yield break; }
            yield return Warten(6f);

            var motion = _spieler.GetComponent<NetworkMotion>();
            Vector3 start = new Vector3(0f, 0f, 30f);
            float yaw = 0f;
            string arg = ArgWert(Argument);
            if (!string.IsNullOrEmpty(arg))
            {
                string[] w = arg.Split(':');
                if (w.Length >= 2 && float.TryParse(w[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    start = new Vector3(x, 0f, z);
                    if (w.Length > 2) float.TryParse(w[2], NumberStyles.Float, CultureInfo.InvariantCulture, out yaw);
                }
            }
            if (motion != null)
            {
                start.y = WorldGround.TrySample(start, out float h) ? h + 0.5f : 60f;
                motion.RequestTeleport(start, yaw);
                yield return Warten(4f);
            }

            // Gras-Material finden (Terrain-Details)
            Material gras = null;
            Texture tex = null;
            foreach (Terrain t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                DetailPrototype[] protos = t.terrainData.detailPrototypes;
                Bericht($"Terrain {t.name}: {protos.Length} Detail-Prototypen, Gras-Weite {t.detailObjectDistance:0} m, Dichte {t.detailObjectDensity:0.00}, Schatten {t.shadowCastingMode}");
                foreach (DetailPrototype p in protos)
                {
                    var r = p.prototype != null ? p.prototype.GetComponent<Renderer>() : null;
                    Material m = r != null ? r.sharedMaterial : null;
                    Bericht($"  Prototyp {(p.prototype != null ? p.prototype.name : "null")}: Mesh {p.usePrototypeMesh}, Modus {p.renderMode}, Instancing {p.useInstancing}, " +
                            $"Material {(m != null ? m.name : "keins")}, Shader {(m != null && m.shader != null ? m.shader.name : "keiner")}, " +
                            $"gesund {p.healthyColor}, trocken {p.dryColor}");
                    if (m != null)
                    {
                        Bericht($"    Shader unterstützt: {m.shader.isSupported}, Pässe {m.passCount}, Schlüsselwörter [{string.Join(" ", m.shaderKeywords)}], " +
                                $"Instancing {m.enableInstancing}, Farbe {(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "-")}");
                        Texture bm = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                        Bericht($"    Textur {(bm != null ? $"{bm.name} {bm.width}×{bm.height} {bm.dimension} {(bm is Texture2D t2 ? t2.format.ToString() : "")} Mip {bm.mipmapCount}" : "KEINE (null)")}");
                        if (gras == null && (m.name.Contains("Grass") || m.name.Contains("Gras"))) { gras = m; tex = bm; }
                    }
                }
            }
            var global = new List<string>();
            foreach (var k in Shader.enabledGlobalKeywords) global.Add(k.name);
            Bericht("Globale Schlüsselwörter: " + string.Join(" ", global));
            Bericht($"Sonne: {(RenderSettings.sun != null ? $"{RenderSettings.sun.name} an {RenderSettings.sun.enabled} Stärke {RenderSettings.sun.intensity:0.00} Farbe {RenderSettings.sun.color}" : "keine")}");

            _voll = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            _mittel = new RenderTexture(960, 540, 0, RenderTextureFormat.ARGB32);
            yield return Bild("1_unveraendert");

            if (gras == null)
            {
                Bericht("FEHLER – kein Gras-Material gefunden");
                yield return Ende();
                yield break;
            }

            // 2. ohne Screen-Space-Irradiance
            bool ssiAn = Shader.IsKeywordEnabled("_SCREEN_SPACE_IRRADIANCE");
            Bericht($"_SCREEN_SPACE_IRRADIANCE global an: {ssiAn}");
            Shader.DisableKeyword("_SCREEN_SPACE_IRRADIANCE");
            yield return Bild("2_ohne_ssi");
            if (ssiAn) Shader.EnableKeyword("_SCREEN_SPACE_IRRADIANCE");

            // 3. weiße Textur
            gras.SetTexture("_BaseMap", Texture2D.whiteTexture);
            yield return Bild("3_textur_weiss");
            gras.SetTexture("_BaseMap", tex);

            // 4. weiße Farbe
            Color farbe = gras.GetColor("_BaseColor");
            gras.SetColor("_BaseColor", Color.white);
            yield return Bild("4_farbe_weiss");
            gras.SetColor("_BaseColor", farbe);

            // 5. URP Lit
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader alt = gras.shader;
            Bericht("URP Lit im Build: " + (lit != null ? "ja" : "nein"));
            if (lit != null)
            {
                gras.shader = lit;
                yield return Bild("5_urp_lit");
                gras.shader = alt;
            }

            // 6. Schatten-Schlüsselwörter aus
            string[] sk = { "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN" };
            var war = new List<string>();
            foreach (string k in sk) if (Shader.IsKeywordEnabled(k)) { war.Add(k); Shader.DisableKeyword(k); }
            Bericht("Schatten-Schlüsselwörter waren an: " + string.Join(" ", war));
            yield return Bild("6_ohne_schatten_keywords");
            foreach (string k in war) Shader.EnableKeyword(k);

            // 7. Normale nach oben voll
            float nu = gras.GetFloat("_NormalUp");
            gras.SetFloat("_NormalUp", 1f);
            yield return Bild("7_normale_oben");
            gras.SetFloat("_NormalUp", nu);

            // 8. Wiesen-Pass-Zusätze aus (Wurzeldunkel 0, Farbvariation weiß) – prüft die Shader-Ergänzung vom 10.10.
            if (gras.HasProperty("_RootDark"))
            {
                float rd = gras.GetFloat("_RootDark"); Color vc = gras.GetColor("_VarColor");
                gras.SetFloat("_RootDark", 0f); gras.SetColor("_VarColor", Color.white);
                yield return Bild("8_ohne_wiesenpass");
                gras.SetFloat("_RootDark", rd); gras.SetColor("_VarColor", vc);
            }
            // 9. Alpha-Ausschnitt aus (Schlüsselwort) – prüft die Variante _ALPHATEST_ON
            bool clip = gras.IsKeywordEnabled("_ALPHATEST_ON");
            if (clip) { gras.DisableKeyword("_ALPHATEST_ON"); yield return Bild("9_ohne_alphatest"); gras.EnableKeyword("_ALPHATEST_ON"); }

            // 10.–14. (10.10. mittags, Grasfläche schwarz im Build): Eigenschaften einzeln ausschließen
            Material farn = null;
            foreach (Terrain t in Terrain.activeTerrains)
                foreach (DetailPrototype p in t.terrainData.detailPrototypes)
                {
                    var r = p.prototype != null ? p.prototype.GetComponent<Renderer>() : null;
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial != gras) farn = r.sharedMaterial;
                }
            if (farn != null)
            {
                var kopie = new Material(gras);
                gras.CopyPropertiesFromMaterial(farn);
                gras.SetTexture("_BaseMap", tex);
                Bericht($"  10: Eigenschaften von {farn.name} übernommen, Schlüsselwörter [{string.Join(" ", gras.shaderKeywords)}]");
                yield return Bild("10_wie_farn");
                gras.CopyPropertiesFromMaterial(kopie);
                gras.shaderKeywords = kopie.shaderKeywords;
                Destroy(kopie);
            }
            foreach (var (name, prop, wert) in new[] { ("11_normale_0", "_NormalUp", 0f), ("12_durchscheinen_0", "_Translucency", 0f), ("13_wind_0", "_WindStrength", 0f), ("14_cutoff_0", "_Cutoff", 0f) })
            {
                if (!gras.HasProperty(prop)) continue;
                float vorher = gras.GetFloat(prop); gras.SetFloat(prop, wert);
                yield return Bild(name);
                gras.SetFloat(prop, vorher);
            }

            // 17./18. GI-Sichten: nur GI (weiß) und Gültigkeit (grün gültig, rot Rückfall)
            var giInst = Ashveil.Rendering.GI.AshveilDynamicGI.Instance;
            if (giInst != null)
            {
                giInst.debugView = 1; yield return Bild("17_gi_nur");
                giInst.debugView = 2; yield return Bild("18_gi_gueltig");
                giInst.debugView = 0;
            }
            // 15. dynamisches GI aus (NaN aus der Irradiance-Textur?), 16. Detail-Instancing aus
            bool giLief = Ashveil.Rendering.GI.AshveilDynamicGI.IsRunning;
            Ashveil.Rendering.GI.AshveilDynamicGI.SetEnabled(false);
            yield return Bild("15_gi_aus");
            if (giLief) Ashveil.Rendering.GI.AshveilDynamicGI.SetEnabled(true);
            foreach (Terrain t in Terrain.activeTerrains)
            {
                DetailPrototype[] protos = t.terrainData.detailPrototypes;
                foreach (DetailPrototype p in protos) p.useInstancing = false;
                t.terrainData.detailPrototypes = protos; t.terrainData.RefreshPrototypes(); t.Flush();
            }
            yield return Warten(2f);
            yield return Bild("16_ohne_instancing");
            foreach (Terrain t in Terrain.activeTerrains)
            {
                DetailPrototype[] protos = t.terrainData.detailPrototypes;
                foreach (DetailPrototype p in protos) p.useInstancing = true;
                t.terrainData.detailPrototypes = protos; t.terrainData.RefreshPrototypes(); t.Flush();
            }

            Bericht("fertig");
            try { File.WriteAllText(Path.Combine(_ordner, "Bericht.txt"), _bericht.ToString()); } catch (Exception) { }
            yield return Ende();
        }

        private IEnumerator Bild(string name)
        {
            yield return Warten(1.5f);
            yield return new WaitForEndOfFrame();
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_voll);
                Graphics.Blit(_voll, _mittel);
                AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(_mittel, 0, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (req.hasError) { Bericht("FEHLER – Rücklesen " + name); yield break; }
                var daten = req.GetData<byte>();
                var t = new Texture2D(960, 540, TextureFormat.RGBA32, false);
                t.LoadRawTextureData(daten);
                t.Apply();
                // Bild ist bei Direct3D kopfüber: Zeilen tauschen
                Color32[] px = t.GetPixels32();
                var g = new Color32[px.Length];
                for (int y = 0; y < 540; y++) Array.Copy(px, y * 960, g, (539 - y) * 960, 960);
                t.SetPixels32(g);
                t.Apply();
                File.WriteAllBytes(Path.Combine(_ordner, name + ".png"), t.EncodeToPNG());
                // Mittlere Farbe der unteren Bildhälfte (Boden mit Gras) als Zahl
                long r = 0, gg = 0, b = 0; int n = 0; int dunkel = 0;
                for (int y = 300; y < 520; y++)
                for (int x = 0; x < 960; x++)
                {
                    Color32 c = g[y * 960 + x];
                    r += c.r; gg += c.g; b += c.b; n++;
                    if (c.r < 25 && c.g < 25 && c.b < 25) dunkel++;
                }
                Bericht($"Bild {name}: Mittel unten R {r / n} G {gg / n} B {b / n}, fast schwarze Pixel {100f * dunkel / n:0.0} %");
                Destroy(t);
            }
            catch (Exception e) { Bericht("FEHLER – Bild " + name + ": " + e.Message); }
        }

        private IEnumerator Ende()
        {
            if (_voll != null) _voll.Release();
            if (_mittel != null) _mittel.Release();
            yield return Warten(1f);
            Application.Quit();
        }

        private bool SpielerBereit() => _spieler != null && InventoryClient.Current != null && InventoryClient.Current.Ready;

        private static IEnumerator Warten(float s) { float bis = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < bis) yield return null; }

        private void Bericht(string zeile)
        {
            _bericht.AppendLine(zeile);
            Debug.Log("[Ashveil] Gras-Probe: " + zeile);
        }

        private static string ArgWert(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 >= args.Length) return string.Empty;
                string w = args[i + 1];
                return !w.StartsWith("-", StringComparison.Ordinal) || w.Contains(":") ? w : string.Empty;   // „-196:235:120“ ist ein Startpunkt
            }
            return null;
        }
    }
}
#endif
