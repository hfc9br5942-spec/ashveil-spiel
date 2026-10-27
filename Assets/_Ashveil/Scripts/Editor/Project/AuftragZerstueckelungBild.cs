using System.Collections.Generic;
using System.IO;
using Ashveil.Gameplay.Enemies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftrag „zerstueckelung_bild“ (22.10.2026, COMBAT-026a/b): Sichtprüfung der Gliedtrennung ohne Play-Modus und ohne Netz –
    /// ein menschlicher Gegner (Parameter: Prefab-Name, Standard Enemy_Deserteur) wird je Trennpunkt frisch aufgebaut, getrennt
    /// und von vorn und von der Seite fotografiert; das abgetrennte Teil wird zur Ansicht etwas zur Seite gelegt (im Spiel fällt
    /// es mit Physik). Bild: Logs/Ashveil/Zerstueckelung/Trennpunkte.png.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragZerstueckelungBild
    {
        static AuftragZerstueckelungBild()
        {
            Auftragsordner.Registrieren("zerstueckelung_bild", (a, fertig) => fertig(true, Erzeugen(string.IsNullOrEmpty(a.parameter) ? "Enemy_Deserteur" : a.parameter)), false, 120);
        }

        public static string Erzeugen(string prefabName)
        {
            string pfad = "Assets/_Ashveil/Prefabs/Enemies/" + prefabName + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pfad);
            if (prefab == null) return "Prefab fehlt: " + pfad;
            string ordner = Ashveil.Core.ProjectPaths.Logs("Zerstueckelung");
            Directory.CreateDirectory(ordner);
            if (prefab.GetComponentInChildren<EnemyBeastVisual>(true) != null) return Tier(prefab, ordner);
            var punkte = new[] { Zerstueckelung.Trennpunkt.Kopf, Zerstueckelung.Trennpunkt.OberarmL, Zerstueckelung.Trennpunkt.UnterarmR,
                                 Zerstueckelung.Trennpunkt.OberschenkelL, Zerstueckelung.Trennpunkt.OberschenkelR };
            const int w = 360, h = 480;
            var bild = new Texture2D(w * punkte.Length, h * 2, TextureFormat.RGB24, false);
            var aufraeumen = new List<Object>();
            // eigene Vorschauszene – die offene Szene bleibt unberührt (sonst gilt sie als geändert und andere Aufträge verweigern)
            Scene vorschau = EditorSceneManager.NewPreviewScene();
            var bericht = new System.Text.StringBuilder();
            Vector3 ort = new Vector3(0f, 2000f, 0f);
            var lichtGo = new GameObject("Zerstueckelung Licht");
            SceneManager.MoveGameObjectToScene(lichtGo, vorschau);
            var licht = lichtGo.AddComponent<Light>();
            licht.type = LightType.Directional;
            licht.intensity = 1.6f;
            lichtGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            aufraeumen.Add(lichtGo);
            var camGo = new GameObject("Zerstueckelung Kamera");
            SceneManager.MoveGameObjectToScene(camGo, vorschau);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = vorschau;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.57f, 0.6f);
            cam.fieldOfView = 32f;
            cam.enabled = false;
            aufraeumen.Add(camGo);
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();   // Aufwärmen: das erste Bild nach dem Laden der Shader kam blass heraus
            try
            {
                for (int i = 0; i < punkte.Length; i++)
                {
                    GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, vorschau);
                    g.transform.SetPositionAndRotation(ort, Quaternion.identity);
                    Animator anim = g.GetComponentInChildren<Animator>();
                    if (anim != null) { anim.Rebind(); anim.Update(0f); }
                    Vector3 dir = punkte[i].ToString().EndsWith("L") ? Vector3.left : Vector3.right;
                    bool ok = Zerstueckelung.Ausfuehren(anim, punkte[i], dir, g.transform);
                    Transform teil = g.transform.Find("Abgetrennt " + punkte[i]);
                    if (teil != null) { teil.position += dir * 0.45f + Vector3.up * 0.05f; foreach (Rigidbody rb in teil.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true; }
                    bericht.Append(punkte[i]).Append(ok ? " getrennt" : " NICHT getrennt").Append(teil != null ? ", Teil " + teil.childCount + " Objekte" : ", kein Teil").Append("; ");
                    for (int ansicht = 0; ansicht < 2; ansicht++)
                    {
                        Vector3 blick = ansicht == 0 ? Vector3.forward : Vector3.right;
                        cam.transform.position = ort + Vector3.up * 1.0f + blick * 4.2f;
                        cam.transform.LookAt(ort + Vector3.up * 0.95f);
                        cam.targetTexture = rt;
                        cam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        tex.Apply();
                        bild.SetPixels(i * w, (1 - ansicht) * h, w, h, tex.GetPixels());
                        Object.DestroyImmediate(tex);
                        RenderTexture.active = null;
                    }
                    Object.DestroyImmediate(g);
                }
                bild.Apply();
                string datei = Path.Combine(ordner, "Trennpunkte.png");
                File.WriteAllBytes(datei, bild.EncodeToPNG());
                return bericht + "Bild: " + datei;
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(bild);
                foreach (Object o in aufraeumen) if (o != null) Object.DestroyImmediate(o);
                EditorSceneManager.ClosePreviewScene(vorschau);
            }
        }

        /// <summary>Tier (starre Platzhalterteile): Kopf, Vorderbein, Schwanz – über die echte Trennfunktion (EnemyBeastVisual.TrenneTeil).</summary>
        private static string Tier(GameObject prefab, string ordner)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.MethodInfo trenne = typeof(EnemyBeastVisual).GetMethod("TrenneTeil", flags);
            System.Reflection.FieldInfo fHead = typeof(EnemyBeastVisual).GetField("head", flags), fTail = typeof(EnemyBeastVisual).GetField("tail", flags),
                fLegs = typeof(EnemyBeastVisual).GetField("legs", flags), fAb = typeof(EnemyBeastVisual).GetField("_ab", flags);
            if (trenne == null || fHead == null || fAb == null) return "TrenneTeil/Felder nicht gefunden";
            string[] namen = { "Kopf", "Vorderbein", "Schwanz" };
            const int w = 360, h = 360;
            Scene vorschau = EditorSceneManager.NewPreviewScene();
            var bild = new Texture2D(w * namen.Length, h, TextureFormat.RGB24, false);
            var lichtGo = new GameObject("Licht");
            SceneManager.MoveGameObjectToScene(lichtGo, vorschau);
            var licht = lichtGo.AddComponent<Light>();
            licht.type = LightType.Directional; licht.intensity = 1.6f;
            lichtGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var camGo = new GameObject("Kamera");
            SceneManager.MoveGameObjectToScene(camGo, vorschau);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = vorschau; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.57f, 0.6f); cam.fieldOfView = 30f; cam.enabled = false;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt; cam.Render();
            var bericht = new System.Text.StringBuilder();
            try
            {
                for (int i = 0; i < namen.Length; i++)
                {
                    GameObject g = Object.Instantiate(prefab);   // ohne Prefab-Verknüpfung – wie zur Laufzeit (Teile lassen sich umhängen)
                    SceneManager.MoveGameObjectToScene(g, vorschau);
                    g.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var tier = g.GetComponentInChildren<EnemyBeastVisual>(true);
                    var beine = fLegs != null ? (Transform[])fLegs.GetValue(tier) : null;
                    Transform ziel = i == 0 ? (Transform)fHead.GetValue(tier) : i == 2 ? (Transform)fTail.GetValue(tier) : beine != null && beine.Length > 0 ? beine[0] : null;
                    if (ziel != null) trenne.Invoke(tier, new object[] { ziel.position, Vector3.right });
                    var ab = (Transform)fAb.GetValue(tier);
                    if (ab != null) { ab.position += Vector3.right * 0.6f + Vector3.up * 0.1f; foreach (Rigidbody rb in ab.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true; }
                    bericht.Append(namen[i]).Append(ab != null ? " getrennt (" + ab.name + ")" : " NICHT getrennt").Append("; ");
                    var b = new Bounds(g.transform.position, Vector3.zero);
                    foreach (Renderer r in g.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                    cam.transform.position = b.center + new Vector3(0.35f, 0.25f, 1f).normalized * (b.extents.magnitude * 3.2f + 0.5f);
                    cam.transform.LookAt(b.center);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                    bild.SetPixels(i * w, 0, w, h, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                    RenderTexture.active = null;
                    Object.DestroyImmediate(g);
                }
                bild.Apply();
                string datei = Path.Combine(ordner, "Tier_" + prefab.name + ".png");
                File.WriteAllBytes(datei, bild.EncodeToPNG());
                return bericht + "Bild: " + datei;
            }
            finally
            {
                cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(bild);
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(lichtGo);
                EditorSceneManager.ClosePreviewScene(vorschau);
            }
        }
    }
}
