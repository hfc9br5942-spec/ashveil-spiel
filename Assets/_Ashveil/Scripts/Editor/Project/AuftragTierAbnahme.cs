using System;
using System.Collections.Generic;
using System.IO;
using Ashveil.Gameplay.Npcs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Creature Art Pass, Stufe 8 „Unity-Abnahme“ (ART-120): Auftrag „tier_abnahme|art“ stellt ein Tier wie in der Herde der Westweiden
    /// in eine leere, nicht gespeicherte Szene und fotografiert es mit Spielkamera-Werten (60° Sichtfeld, Augenhöhe 1,7 m):
    /// Abstand 2 / 5 / 15 / 30 m, Haltungen (Stand, Schritt, Trab, Grasen, Liegen), Licht (Mittag, Abend) und Hintergründe
    /// (Wiese, Himmel, dunkler Waldrand). Bilder: Logs/Ashveil/Tiere/Abnahme/&lt;art&gt;_*.png. Ändert keine Szene.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragTierAbnahme
    {
        static AuftragTierAbnahme() => Auftragsordner.Registrieren("tier_abnahme", Abnahme, false, 600);

        private static void Abnahme(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string art = string.IsNullOrEmpty(a.parameter) ? "pferd" : a.parameter.Trim();
            string vorher = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bilder = new List<string>();
            try
            {
                var boden = GameObject.CreatePrimitive(PrimitiveType.Plane);
                boden.transform.localScale = new Vector3(20f, 1f, 20f);
                var bodenMat = GreyboxKit.Mat("Abnahme_Wiese", new Color(0.30f, 0.38f, 0.20f));
                boden.GetComponent<Renderer>().sharedMaterial = bodenMat;
                var wald = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wald.transform.position = new Vector3(0f, 6f, 60f); wald.transform.localScale = new Vector3(200f, 12f, 2f);
                wald.GetComponent<Renderer>().sharedMaterial = GreyboxKit.Mat("Abnahme_Waldrand", new Color(0.10f, 0.14f, 0.09f));
                wald.SetActive(false);
                var sonne = new GameObject("Sonne").AddComponent<Light>();
                sonne.type = LightType.Directional; sonne.shadows = LightShadows.Soft;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                var herde = new GameObject("Herde").transform;
                SandboxSceneBuilder.WestweidenHerdentier(herde, art, "Tier", Vector3.zero, Vector3.zero, 0f, 90f, 1f, 5);
                var vis = herde.GetComponentInChildren<AnimalVisual>();
                var cam = new GameObject("Kamera").AddComponent<Camera>();
                cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 800f;
                string ordner = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Ashveil", "Tiere", "Abnahme");
                Directory.CreateDirectory(ordner);
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                cam.targetTexture = rt;
                Vector3 mitte = herde.GetComponentInChildren<Renderer>() != null ? herde.GetComponentInChildren<Renderer>().bounds.center : new Vector3(0, 1.1f, 0);
                void Licht(string welches)
                {
                    if (welches == "abend")
                    {
                        sonne.transform.rotation = Quaternion.Euler(9f, -60f, 0f); sonne.color = new Color(1f, 0.72f, 0.5f); sonne.intensity = 1.3f;
                        RenderSettings.ambientSkyColor = new Color(0.45f, 0.42f, 0.55f); RenderSettings.ambientEquatorColor = new Color(0.4f, 0.32f, 0.3f);
                        RenderSettings.ambientGroundColor = new Color(0.12f, 0.1f, 0.08f); cam.backgroundColor = new Color(0.86f, 0.62f, 0.45f);
                    }
                    else
                    {
                        sonne.transform.rotation = Quaternion.Euler(52f, -35f, 0f); sonne.color = new Color(1f, 0.97f, 0.9f); sonne.intensity = 1.5f;
                        RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.72f); RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.45f);
                        RenderSettings.ambientGroundColor = new Color(0.18f, 0.2f, 0.12f); cam.backgroundColor = new Color(0.62f, 0.72f, 0.84f);
                    }
                    cam.clearFlags = CameraClearFlags.SolidColor;
                }
                void Foto(string name, float abstand, float winkel, float hoehe = 1.7f)
                {
                    Vector3 richtung = Quaternion.Euler(0f, winkel, 0f) * Vector3.forward;
                    cam.transform.position = new Vector3(mitte.x, 0f, mitte.z) + richtung * abstand + Vector3.up * hoehe;
                    cam.transform.LookAt(mitte);
                    var temp = Gebacken(herde);
                    cam.Render();
                    foreach (var t in temp) { UnityEngine.Object.DestroyImmediate(t.Key.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(t.Key); t.Value.enabled = true; }
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
                    string p = Path.Combine(ordner, art + "_" + name + ".png");
                    File.WriteAllBytes(p, tex.EncodeToPNG()); bilder.Add(Path.GetFileName(p));
                }
                // Tier dreht sich mit dem Herdenbau zur Seite (+X): Kamera schräg vorn links = Winkel 50° um die y-Achse
                Licht("mittag");
                if (vis != null) vis.Vorschau(AnimalPose.Stand, 0f, 1f);
                Foto("02m_Stand", 2.6f, 40f, 1.6f);
                Foto("05m_Stand", 5f, 50f);
                Foto("15m_Stand", 15f, 55f);
                Foto("30m_Stand", 30f, 55f);
                if (vis != null)
                {
                    vis.Vorschau(AnimalPose.Walk, 1.4f, 0.9f); Foto("05m_Schritt", 5f, 0f, 1.4f);
                    vis.Vorschau(AnimalPose.Walk, 3.6f, 0.6f); Foto("05m_Trab", 5f, 0f, 1.4f);
                    vis.Vorschau(AnimalPose.Graze, 0f, 2.5f); Foto("05m_Grasen", 5f, 50f);
                    vis.Vorschau(AnimalPose.Lie, 0f, 3.0f); Foto("05m_Liegen", 5f, 50f);
                    vis.Vorschau(AnimalPose.Stand, 0f, 3.0f);
                }
                Licht("abend"); Foto("05m_Abend", 5f, 120f);
                Licht("mittag"); wald.SetActive(true); Foto("08m_Waldrand", 8f, 200f, 1.7f);
                cam.transform.position = new Vector3(mitte.x + 2.5f, 0.6f, mitte.z + 3.5f); cam.transform.LookAt(mitte + Vector3.up * 0.4f);
                wald.SetActive(false); Foto("04m_Himmel", 4f, 30f, 0.5f);
                cam.targetTexture = null; RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
                int tri = 0;
                foreach (SkinnedMeshRenderer sk in herde.GetComponentsInChildren<SkinnedMeshRenderer>()) if (sk.sharedMesh != null) tri += (int)(sk.sharedMesh.GetIndexCount(0) / 3);
                fertig(true, $"Abnahme {art}: {bilder.Count} Bilder in {ordner} · Dreiecke {tri} · Gelenke {(vis != null ? "ja" : "–")}");
            }
            catch (Exception e) { fertig(false, "Tierabnahme: " + e.GetType().Name + ": " + e.Message); }
            finally
            {
                if (!string.IsNullOrEmpty(vorher)) EditorSceneManager.OpenScene(vorher, OpenSceneMode.Single);
            }
        }

        /// <summary>Im Editor rechnet die Grafikkarte die Haut für ein einzelnes Bild nicht neu – gebogene Haut backen und zeigen.</summary>
        private static Dictionary<GameObject, SkinnedMeshRenderer> Gebacken(Transform wurzel)
        {
            var d = new Dictionary<GameObject, SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer sk in wurzel.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = new Mesh(); sk.BakeMesh(m, true);
                var go = new GameObject("Gebacken"); go.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = sk.sharedMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                sk.enabled = false; d[go] = sk;
            }
            return d;
        }
    }
}
