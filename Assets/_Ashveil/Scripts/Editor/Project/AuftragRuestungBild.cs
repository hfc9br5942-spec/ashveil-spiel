using System.Collections.Generic;
using System.IO;
using Ashveil.Data;
using Ashveil.Gameplay.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftrag „ruestung_bild“ (22.10.2026, Max: Rüstung soll sitzen wie in WoW, Souls-Optik): Sichtprüfung der Ausrüstung ohne
    /// Play-Modus. Der Tripo-Mann (bzw. „frau“) bekommt die genannten Aussehen-Einträge über den echten Weg (AppearanceRig.Apply)
    /// und wird von vorn, von der Seite, von hinten und schräg fotografiert – in einer eigenen Vorschauszene.
    /// Parameter: „dented_helm,warden_pauldrons,frayed_gambeson“ (Schlüssel aus appearances.json), optional „frau:“ davor.
    /// Bild: Logs/Ashveil/Ruestung/Ruestung_&lt;erster Schlüssel&gt;.png.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragRuestungBild
    {
        static AuftragRuestungBild()
        {
            Auftragsordner.Registrieren("ruestung_bild", (a, fertig) => { string m = Erzeugen(a.parameter ?? "", out bool ok); fertig(ok, m); }, false, 120);
            // Aussehen-Katalog und -Einträge aus appearances.json neu bauen (z. B. nach neuen Anbauteilen) – ohne Szene
            Auftragsordner.Registrieren("aussehen_bauen", (a, fertig) => { AppearanceCatalog c = AppearanceContentBuilder.Build(); fertig(c != null, c != null ? "Aussehen-Katalog neu gebaut" : "Bau fehlgeschlagen"); }, true, 600);
        }

        public static string Erzeugen(string parameter, out bool ok)
        {
            ok = false;
            // „frau:“ = Frauenkörper, „nah:“ = Nahansicht Oberkörper (Schultern, Helm) – Reihenfolge egal
            bool frau = false, nah = false;
            for (bool weiter = true; weiter;)
            {
                weiter = false;
                if (parameter.StartsWith("frau:")) { frau = true; parameter = parameter.Substring(5); weiter = true; }
                if (parameter.StartsWith("nah:")) { nah = true; parameter = parameter.Substring(4); weiter = true; }
            }
            string[] keys = parameter.Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (keys.Length == 0) return "Parameter: Schlüssel aus appearances.json, z. B. dented_helm,warden_pauldrons";
            var koerper = AssetDatabase.LoadAssetAtPath<GameObject>(frau ? TripoKoerper.FemalePath : TripoKoerper.MalePath);
            if (koerper == null) return "Tripo-Körper fehlt";
            var alle = new Dictionary<string, AppearanceDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:AppearanceDefinition"))
            {
                var d = AssetDatabase.LoadAssetAtPath<AppearanceDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null && !string.IsNullOrEmpty(d.Id)) alle[d.Id.Replace("appearance.", "")] = d;
            }
            var defs = new AppearanceDefinition[CharacterAppearanceData.SlotCount];
            var fehlt = new List<string>();
            foreach (string k in keys)
            {
                if (k.StartsWith("probe_")) { AppearanceDefinition pd = Probe(k); if (pd != null) defs[(int)Platz(pd.slot)] = pd; else fehlt.Add(k); continue; }
                if (!alle.TryGetValue(k, out AppearanceDefinition d)) { fehlt.Add(k); continue; }
                defs[(int)Platz(d.slot)] = d;
            }
            Scene vorschau = EditorSceneManager.NewPreviewScene();
            GameObject inst = Object.Instantiate(koerper);
            SceneManager.MoveGameObjectToScene(inst, vorschau);
            var lichtGo = new GameObject("Licht"); SceneManager.MoveGameObjectToScene(lichtGo, vorschau);
            var licht = lichtGo.AddComponent<Light>(); licht.type = LightType.Directional; licht.intensity = 1.5f;
            lichtGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);   // von vorn-oben (vorher von hinten: Vorderansicht blass)
            var camGo = new GameObject("Kamera"); SceneManager.MoveGameObjectToScene(camGo, vorschau);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = vorschau; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f, 0.52f, 0.56f); cam.fieldOfView = 26f; cam.enabled = false;
            const int w = 420, h = 640;
            var rt = new RenderTexture(w, h, 24);
            var bild = new Texture2D(w * 4, h, TextureFormat.RGB24, false);
            try
            {
                var rig = inst.AddComponent<AppearanceRig>();
                rig.Init(inst.transform);
                if (!rig.Ready) return "AppearanceRig nicht bereit";
                rig.Apply(defs, new CharacterAppearanceData(), null);
                cam.targetTexture = rt; cam.Render();   // Aufwärmen
                var b = new Bounds(inst.transform.position, Vector3.zero);
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                Vector3[] blick = { Vector3.forward, Vector3.right, Vector3.back, new Vector3(0.7f, 0.15f, 0.7f) };
                for (int i = 0; i < 4; i++)
                {
                    Vector3 ziel = nah ? new Vector3(b.center.x, b.max.y - 0.4f, b.center.z) : b.center + Vector3.up * 0.15f;
                    cam.transform.position = ziel + blick[i].normalized * (nah ? 2.4f : 5.2f);
                    cam.transform.LookAt(ziel);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                    bild.SetPixels(i * w, 0, w, h, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                    RenderTexture.active = null;
                }
                bild.Apply();
                string ordner = Ashveil.Core.ProjectPaths.Logs("Ruestung");
                Directory.CreateDirectory(ordner);
                string datei = Path.Combine(ordner, "Ruestung_" + keys[0].Split(':')[0] + ".png");
                File.WriteAllBytes(datei, bild.EncodeToPNG());
                ok = true;
                string teile = "";
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>()) if (r.transform.parent != null && r.GetComponentInParent<Transform>().name.Length > 0 && (r.name.StartsWith("ANB_") || r.transform.parent.name.StartsWith("ANB_") || r.name.StartsWith("Equip Part") || r.name.StartsWith("Equip Band"))) teile += " " + r.name + " " + r.bounds.size.ToString("0.00") + "@" + r.bounds.center.ToString("0.00");
                return rig.Summary() + $" Größe={rig.SizeScale:0.000} Schulter={rig.BreitenScale:0.000}" + teile + (fehlt.Count > 0 ? " · fehlt: " + string.Join(",", fehlt) : "") + " · Bild: " + datei;
            }
            finally
            {
                cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(bild);
                Object.DestroyImmediate(inst); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lichtGo);
                EditorSceneManager.ClosePreviewScene(vorschau);
            }
        }

        /// <summary>
        /// Nur im Speicher: Anbauteil aus Art/Models/Ruestung zur Lageprüfung, ohne Daten zu ändern. „probe_helm:x/y/z“ (Drehung),
        /// „probe_schulter:x/y/z“ (gespiegelt an beiden Oberarmen), „probe_schnalle“.
        /// </summary>
        private static AppearanceDefinition Probe(string k)
        {
            string[] t = k.Split(':');
            Vector3 e = Vector3.zero;
            if (t.Length > 1) { string[] z = t[1].Split('/'); if (z.Length == 3) e = new Vector3(float.Parse(z[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(z[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(z[2], System.Globalization.CultureInfo.InvariantCulture)); }
            string modell; string knochen; EquipType platz; Vector3 off; bool spiegel = false; SurfaceKind art = SurfaceKind.Metal;
            switch (t[0])
            {
                case "probe_helm": modell = "ANB_Helm_Topf"; knochen = "Head"; platz = EquipType.Head; off = new Vector3(0f, 0.05f, 0.01f); break;
                case "probe_schulter": modell = "ANB_Schulter_Platte"; knochen = "upperarm_r"; platz = EquipType.Shoulders; off = Vector3.zero; spiegel = true; break;
                case "probe_leder": modell = "ANB_Schulter_Leder"; knochen = "upperarm_r"; platz = EquipType.Shoulders; off = Vector3.zero; spiegel = true; break;
                case "probe_knie": modell = "ANB_Kniekachel"; knochen = "calf_r"; platz = EquipType.Legs; off = new Vector3(0f, 0f, 0.05f); spiegel = true; break;
                case "probe_schnalle": modell = "ANB_Schnalle"; knochen = "pelvis"; platz = EquipType.Belt; off = new Vector3(0f, 0.08f, 0.13f); break;
                // 22.10.: modellierte Teile (Artlist/Tripo H3.1) – Drehung wie die Grundform-Einträge in appearances.json, wenn keine angegeben
                case "probe_helmgross": modell = "ANB_Helm_Gross"; knochen = "Head"; platz = EquipType.Head; off = new Vector3(0f, -0.075f, 0.005f); if (t.Length == 1) e = new Vector3(0f, 90f, 0f); break;
                case "probe_lederhart": modell = "ANB_Schulter_Lederhart"; knochen = "upperarm_r"; platz = EquipType.Shoulders; off = new Vector3(-0.04f, -0.015f, 0f); spiegel = true; if (t.Length == 1) e = new Vector3(0f, 180f, 0f); art = SurfaceKind.Leather; break;
                case "probe_kniefaecher": modell = "ANB_Kniekachel_Faecher"; knochen = "calf_r"; platz = EquipType.Legs; off = new Vector3(0f, 0.03f, 0.06f); spiegel = true; if (t.Length == 1) e = new Vector3(0f, 90f, 0f); break;
                default: return null;
            }
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Ashveil/Art/Models/Ruestung/" + modell + ".fbx");
            if (go == null) return null;
            var d = ScriptableObject.CreateInstance<AppearanceDefinition>();
            d.slot = platz;
            d.parts.Add(new RigidPart { bone = knochen, prefab = go, offset = off, euler = e, size = Vector3.one, surface = art, mirror = spiegel });
            return d;
        }

        private static EquipmentSlot Platz(EquipType t)
        {
            switch (t)
            {
                case EquipType.Head: return EquipmentSlot.Head;
                case EquipType.Shoulders: return EquipmentSlot.Shoulders;
                case EquipType.Chest: return EquipmentSlot.Chest;
                case EquipType.Hands: return EquipmentSlot.Hands;
                case EquipType.Legs: return EquipmentSlot.Legs;
                case EquipType.Feet: return EquipmentSlot.Feet;
                case EquipType.Belt: return EquipmentSlot.Belt;
                case EquipType.Cloak: return EquipmentSlot.Cloak;
                default: return EquipmentSlot.MainHand;
            }
        }
    }
}
