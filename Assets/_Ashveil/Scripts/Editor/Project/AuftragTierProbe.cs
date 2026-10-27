using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Prüfbild der Herdentiere (Claude 14.10.2026, Lückenliste Punkt 6 „Tiere“): Auftrag „tier_probe“ baut in einer leeren,
    /// nicht gespeicherten Szene je ein Schaf, Rind und Pferd mit demselben Bau wie in den Westweiden und fotografiert sie von
    /// der Seite, schräg vorn und von hinten → Logs/Ashveil/Tiere/Tiere_*.png. Ändert keine Szene.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragTierProbe
    {
        static AuftragTierProbe() => Auftragsordner.Registrieren("tier_probe", Probe, false, 300);

        private static void Probe(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string vorher = SceneManager.GetActiveScene().path;
            Scene s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var boden = GameObject.CreatePrimitive(PrimitiveType.Plane);
                boden.transform.localScale = new Vector3(4f, 1f, 4f);
                boden.GetComponent<Renderer>().sharedMaterial = GreyboxKit.Mat("Probe_Boden", new Color(0.36f, 0.42f, 0.25f));
                var sonne = new GameObject("Sonne").AddComponent<Light>();
                sonne.type = LightType.Directional; sonne.intensity = 1.6f; sonne.shadows = LightShadows.Soft;
                sonne.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.52f);
                var herde = new GameObject("Herde").transform;
                SandboxSceneBuilder.WestweidenHerdentier(herde, "schaf", "Schaf", new Vector3(-3.2f, 0f, 0f), new Vector3(-3.2f, 0f, 0f), 0f, 90f, 1f, 2);
                SandboxSceneBuilder.WestweidenHerdentier(herde, "rind", "Rind", new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f), 0f, 90f, 1f, 4);
                SandboxSceneBuilder.WestweidenHerdentier(herde, "pferd", "Pferd", new Vector3(3.6f, 0f, 0f), new Vector3(3.6f, 0f, 0f), 0f, 90f, 1f, 5);
                // Glocken-Klangquellen sind für das Bild ohne Belang
                var cam = new GameObject("Kamera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.7f, 0.78f);
                cam.fieldOfView = 32f;
                string ordner = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Ashveil", "Tiere");
                Directory.CreateDirectory(ordner);
                (string n, Vector3 p, Vector3 z)[] blicke =
                {
                    ("Seite", new Vector3(0.3f, 1.4f, -11.5f), new Vector3(0.3f, 0.8f, 0f)),
                    ("Schraeg_vorn", new Vector3(9.5f, 2.4f, -7.5f), new Vector3(0.5f, 0.8f, 0f)),
                    ("Hinten", new Vector3(-9.5f, 2.0f, 6.5f), new Vector3(0.3f, 0.8f, 0f)),
                    ("Nah_Schaf", new Vector3(-1.2f, 1.0f, -2.6f), new Vector3(-3.2f, 0.55f, 0f))
                };
                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                cam.targetTexture = rt;
                foreach (var b in blicke)
                {
                    cam.transform.position = b.p; cam.transform.LookAt(b.z);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(ordner, "Tiere_" + b.n + ".png"), tex.EncodeToPNG());
                }
                // Haltung: Kopf tief (Grasen), Beine im Schritt – zeigt, ob sich die Haut an Hals und Schulter weich biegt
                var haut = herde.GetComponentsInChildren<SkinnedMeshRenderer>();
                var vorherB = new Bounds[haut.Length];
                var probe = new Mesh();
                for (int i = 0; i < haut.Length; i++) { haut[i].BakeMesh(probe, true); vorherB[i] = probe.bounds; }
                foreach (Transform t in herde.GetComponentsInChildren<Transform>())
                {
                    if (t.name == "Head") t.localRotation *= Quaternion.Euler(50f, 0f, 0f);
                    else if (t.name == "Bein VL") t.localRotation *= Quaternion.Euler(-30f, 0f, 0f);
                    else if (t.name == "Bein HR") t.localRotation *= Quaternion.Euler(28f, 0f, 0f);
                }
                string folgt = "";
                for (int i = 0; i < haut.Length; i++)
                {
                    haut[i].BakeMesh(probe, true);
                    float d = (probe.bounds.center - vorherB[i].center).magnitude + (probe.bounds.size - vorherB[i].size).magnitude;
                    folgt += (folgt.Length > 0 ? ", " : "") + haut[i].sharedMesh.name + (d > 0.01f ? " biegt" : " STARR") + $" ({d:0.00})";
                }
                UnityEngine.Object.DestroyImmediate(probe);
                // Im Editor rechnet die Grafikkarte die Haut für ein einzelnes Bild nicht neu – fürs Prüfbild die gebogene Haut backen
                var gebacken = new System.Collections.Generic.List<GameObject>();
                foreach (SkinnedMeshRenderer sk in haut)
                {
                    var m = new Mesh();
                    sk.BakeMesh(m, true);
                    var go = new GameObject("Haltung_" + sk.sharedMesh.name);
                    go.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    go.AddComponent<MeshRenderer>().sharedMaterial = sk.sharedMaterial;
                    sk.enabled = false;
                    gebacken.Add(go);
                }
                cam.transform.position = blicke[0].p; cam.transform.LookAt(blicke[0].z);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(ordner, "Tiere_Haltung.png"), tex.EncodeToPNG());
                foreach (GameObject go in gebacken) { UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(go); }
                RenderTexture.active = null; cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
                int tri = 0;
                foreach (MeshFilter mf in herde.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) tri += (int)(mf.sharedMesh.GetIndexCount(0) / 3);
                foreach (SkinnedMeshRenderer sk in herde.GetComponentsInChildren<SkinnedMeshRenderer>()) if (sk.sharedMesh != null) tri += (int)(sk.sharedMesh.GetIndexCount(0) / 3);
                AssetDatabase.SaveAssets();
                fertig(true, $"Tierbilder in {ordner} (Seite, Schräg vorn, Hinten, Nah Schaf, Haltung) · Netze unter {TierModelle.Ordner} · Dreiecke gesamt {tri} · Haltung: {folgt}");
            }
            catch (Exception e) { fertig(false, "Tierprobe: " + e.GetType().Name + ": " + e.Message); }
            finally
            {
                if (!string.IsNullOrEmpty(vorher)) EditorSceneManager.OpenScene(vorher, OpenSceneMode.Single);
            }
        }
    }
}
