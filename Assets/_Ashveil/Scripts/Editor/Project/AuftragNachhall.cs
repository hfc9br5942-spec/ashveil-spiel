using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Ashveil.Core;
using Ashveil.Gameplay.Audio;
using SteamAudio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Vector3 = UnityEngine.Vector3;
using Scene = UnityEngine.SceneManagement.Scene;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Nachhall (Claude 14.10.2026, TECH-125, Schritt 3): vorberechneter Raumklang von Steam Audio in Höhlen und Hallen.
    ///
    /// Aufbau: Steam Audio rechnet den Nachhall „um den Hörer“ (listener-centric) aus vorher gebackenen Sonden. Der Effekt
    /// „Steam Audio Reverb“ gibt nur den Nachhall aus (Quelle: reverb_effect.cpp v4.8.1 – Ausgang wird geleert und nur mit dem
    /// Nachhall gefüllt), darum hängt er in einer eigenen Rückkehr-Gruppe:
    ///   Mischpult <see cref="Pfad"/>: Master › Welt (trocken, draußen) · Hoehle (trocken + Send 0 dB → Nachhall) ·
    ///   Nachhall (Receive → Steam Audio Reverb) · Betten (Wind, Grillen). Die KlangLandschaft schaltet die Weltquellen in
    ///   einer Höhle (<see cref="HoehlenVolumen"/>) auf „Hoehle“, draußen auf „Welt“ – draußen gibt es also keinen Nachhall.
    ///
    ///   „nachhall_mischpult“        prüft das Mischpult und meldet Gruppen, Effekte und Send-Ziel.
    ///   „nachhall_backen|Szene“      backt den Nachhall einer Szene (Pfad unter Assets, z. B. …/Regions/Westweiden_Zelle.unity):
    ///       1 je Höhlenvolumen der Szene: Bauteile unter seinem Eltern-Objekt als Steam-Audio-Geometrie (Fels) – das Gelände
    ///         NIE (würde 4 km Gelände mitnehmen) · 2 Szene exportieren (nur diese Geometrie) · 3 Sonden selbst setzen: Raster
    ///         3,5 m, Boden per Strahl nach unten (kein Gelände, Kopfhöhe frei ≥ 2,2 m), Sonde 1,6 m über dem Boden, Einfluss 4,5 m ·
    ///         4 Nachhall backen (Faltung, Ambisonics 1. Ordnung, 2 s) · 5 Wurzel „Nachhall (Steam Audio)“ AUS speichern – die
    ///         KlangLandschaft schaltet sie nur am Client ein (Server ohne Steam Audio, CORE-005).
    ///       Nach jedem Neubau der Szene (westweiden_zelle, kareth_v4_bauen) neu backen.
    /// Daten: <c>Audio/Nachhall/&lt;Szene&gt;_*.asset</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragNachhall
    {
        public const string Pfad = ProjectSetup.Root + "/Audio/Ashveil_Klang.mixer";
        public const string DatenOrdner = ProjectSetup.Root + "/Audio/Nachhall";
        public const string WurzelName = "Nachhall (Steam Audio)";
        private const float Raster = 3.5f, Hoehe = 1.6f, Einfluss = 4.5f, Kopf = 2.2f;

        static AuftragNachhall()
        {
            Auftragsordner.Registrieren("nachhall_mischpult", Mischpult, true, 60);
            Auftragsordner.Registrieren("nachhall_backen", Backen, false, 3600);
        }

        private const BindingFlags Alle = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // ------------------------------------------------------------------ Mischpult
        private static void Mischpult(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            var mix = AssetDatabase.LoadAssetAtPath<AudioMixer>(Pfad);
            if (mix == null) { fertig(false, "Mischpult fehlt: " + Pfad); return; }
            try { fertig(true, Beschreibung(mix)); }
            catch (Exception e) { fertig(false, "Mischpult nicht lesbar: " + e.Message); }
        }

        /// <summary>Gruppen, Effekte und Send-Ziele als Text (zur Prüfung; liest die internen Editor-Klassen).</summary>
        public static string Beschreibung(AudioMixer mix)
        {
            Assembly ed = typeof(Editor).Assembly;
            Type tGrp = ed.GetType("UnityEditor.Audio.AudioMixerGroupController");
            Type tEff = ed.GetType("UnityEditor.Audio.AudioMixerEffectController");
            return string.Join(", ", mix.FindMatchingGroups("").Select(g =>
            {
                var effs = (object[])tGrp.GetProperty("effects", Alle).GetValue(g);
                return g.name + "[" + string.Join("+", effs.Select(e =>
                {
                    string n = (string)tEff.GetProperty("effectName", Alle).GetValue(e);
                    object ziel = tEff.GetProperty("sendTarget", Alle)?.GetValue(e);
                    return ziel != null ? n + "→" + tEff.GetProperty("effectName", Alle).GetValue(ziel) : n;
                })) + "]";
            }));
        }

        // ------------------------------------------------------------------ Backen
        private static void Backen(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string pfad = (a.parameter ?? "").Trim();
            if (!pfad.EndsWith(".unity") || AssetDatabase.LoadAssetAtPath<SceneAsset>(pfad) == null)
            {
                fertig(false, "Szene nicht gefunden: „" + pfad + "“ (Parameter = Pfad unter Assets)");
                return;
            }
            var uhr = Stopwatch.StartNew();
            Scene szene = EditorSceneManager.OpenScene(pfad, OpenSceneMode.Single);
            SceneManager.SetActiveScene(szene);

            // alte Wurzel (Szenen-Objekt) ersetzen
            foreach (GameObject r in szene.GetRootGameObjects())
                if (r.name == WurzelName) UnityEngine.Object.DestroyImmediate(r);

            var volumina = szene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HoehlenVolumen>(true)).ToList();
            if (volumina.Count == 0) { fertig(true, "keine Höhlenvolumen in " + szene.name + " – nichts zu backen"); return; }

            // Einstellungen: Faltungs-Nachhall, 1. Ordnung, 2 s (Kreidegewölbe klingen lang)
            SteamAudioSettings st = SteamAudioSettings.Singleton;
            st.bakeConvolution = true; st.bakeParametric = false;
            st.bakingAmbisonicOrder = 1; st.realTimeAmbisonicOrder = Mathf.Max(1, st.realTimeAmbisonicOrder);
            st.bakingDuration = 2f; st.realTimeDuration = 2f;
            st.bakingCPUCoresPercentage = 75;
            EditorUtility.SetDirty(st);

            if (!AssetDatabase.IsValidFolder(DatenOrdner)) AssetDatabase.CreateFolder(ProjectSetup.Root + "/Audio", "Nachhall");

            var wurzel = new GameObject(WurzelName);
            SceneManager.MoveGameObjectToScene(wurzel, szene);

            // 1 Geometrie: Eltern der Höhlenvolumen, ohne Gelände
            SteamAudioMaterial fels = Resources.Load<SteamAudioMaterial>("Materials/Rock");
            var bauten = new HashSet<Transform>();
            foreach (HoehlenVolumen v in volumina)
            {
                Transform b = v.transform.parent;
                if (b == null || b.GetComponentInChildren<Terrain>(true) != null) continue;
                if (!bauten.Add(b)) continue;
                var geo = b.GetComponent<SteamAudioGeometry>() ?? b.gameObject.AddComponent<SteamAudioGeometry>();
                geo.exportAllChildren = true;
                geo.material = fels;
            }
            if (bauten.Count == 0) { Abbruch(wurzel, fertig, "kein Bauwerk zu den Höhlenvolumen gefunden"); return; }

            // 2 Export (nur Editor – Tag EditorOnly, kommt nicht ins Spiel)
            var geoGo = new GameObject("Geometrie (nur Editor)") { tag = "EditorOnly" };
            geoGo.transform.SetParent(wurzel.transform, false);
            var sm = geoGo.AddComponent<SteamAudioStaticMesh>();
            sm.asset = Daten(szene.name + "_Geometrie");
            sm.sceneNameWhenExported = szene.name;
            SteamAudioManager.ExportScene(szene, false);
            if (sm.asset.data == null || sm.asset.data.Length == 0) { Abbruch(wurzel, fertig, "Export ergab keine Geometrie"); return; }

            // 3 Sonden
            Physics.SyncTransforms();
            var batches = new List<SteamAudioProbeBatch>();
            int nSonden = 0;
            FieldInfo fSph = typeof(SteamAudioProbeBatch).GetField("mProbeSpheres", Alle);
            SteamAudioManager.Initialize(ManagerInitReason.GeneratingProbes);
            try
            {
                for (int i = 0; i < volumina.Count; i++)
                {
                    Bounds bb = volumina[i].Bereich;
                    List<Vector3> pts = Sondenpunkte(bb);
                    if (pts.Count == 0) continue;
                    var go = new GameObject("Nachhall: " + volumina[i].name.Replace("Höhlenvolumen: ", ""));
                    go.transform.SetParent(wurzel.transform, false);
                    go.transform.position = bb.center;
                    go.transform.localScale = bb.size;
                    var pb = go.AddComponent<SteamAudioProbeBatch>();
                    pb.placementStrategy = ProbeGenerationType.Centroid;
                    pb.asset = Daten(szene.name + "_Sonden_" + i);
                    var nat = new ProbeBatch(SteamAudioManager.Context);
                    var kugeln = pts.Select(p => new Sphere { center = Common.ConvertVector(p), radius = Einfluss }).ToArray();
                    foreach (Sphere k in kugeln) nat.AddProbe(k);
                    nat.Commit();
                    pb.probeDataSize = nat.Save(pb.asset);
                    nat.Release();
                    fSph.SetValue(pb, kugeln);
                    pb.ResetLayers();
                    EditorUtility.SetDirty(pb);
                    batches.Add(pb);
                    nSonden += kugeln.Length;
                }
            }
            finally
            {
                SteamAudioManager.ShutDown();
                if (SteamAudioManager.Singleton != null) UnityEngine.Object.DestroyImmediate(SteamAudioManager.Singleton.gameObject);
            }
            if (batches.Count == 0) { Abbruch(wurzel, fertig, "keine Sonden (kein Boden in den Höhlenvolumen gefunden)"); return; }
            SteamAudio.SerializedObject.FlushAllWrites();
            AssetDatabase.SaveAssets();

            // 4 Backen (eigener Hörer nur zum Backen)
            var hgo = new GameObject("Hörer zum Backen (nur Editor)") { tag = "EditorOnly" };
            hgo.transform.SetParent(wurzel.transform, false);
            var hoerer = hgo.AddComponent<SteamAudioListener>();
            hoerer.applyReverb = true;
            hoerer.reverbType = ReverbType.Baked;
            hoerer.useAllProbeBatches = false;
            hoerer.probeBatches = batches.ToArray();
            hoerer.BeginBake();
            if (!Baker.IsBakeActive()) { Abbruch(wurzel, fertig, "Backen startete nicht (siehe Protokoll)"); return; }

            string vorher = $"{volumina.Count} Höhlenvolumen, {bauten.Count} Bauwerk(e), {nSonden} Sonden in {batches.Count} Gruppen";
            EditorApplication.CallbackFunction warten = null;
            warten = () =>
            {
                if (Baker.IsBakeActive()) return;
                EditorApplication.update -= warten;
                BakedDataIdentifier id = hoerer.GetBakedDataIdentifier();
                int bytes = batches.Sum(b => b.GetSizeForLayer(id));
                int ohne = batches.Count(b => b.GetSizeForLayer(id) <= 0);
                foreach (var b in batches) EditorUtility.SetDirty(b);
                wurzel.AddComponent<NachhallWurzel>();
                wurzel.SetActive(false);                   // nur der Client schaltet ein (KlangLandschaft)
                EditorSceneManager.MarkSceneDirty(szene);
                EditorSceneManager.SaveScene(szene);
                AssetDatabase.SaveAssets();
                fertig(ohne == 0 && bytes > 0,
                    $"Nachhall {szene.name}: {vorher} · gebacken {bytes / 1024f:0} KB" + (ohne > 0 ? $" · {ohne} Gruppen OHNE Daten" : "") +
                    $" · Geometrie {sm.asset.data.Length / 1024f:0} KB (nur Editor) · {uhr.Elapsed.TotalSeconds:0} s");
            };
            EditorApplication.update += warten;
        }

        private static void Abbruch(GameObject wurzel, Action<bool, string> fertig, string grund)
        {
            if (wurzel != null) UnityEngine.Object.DestroyImmediate(wurzel);
            if (SteamAudioManager.Singleton != null) UnityEngine.Object.DestroyImmediate(SteamAudioManager.Singleton.gameObject);
            fertig(false, "Nachhall: " + grund);
        }

        private static SerializedData Daten(string name)
        {
            string p = DatenOrdner + "/" + name + ".asset";
            var d = AssetDatabase.LoadAssetAtPath<SerializedData>(p);
            if (d != null) return d;
            d = ScriptableObject.CreateInstance<SerializedData>();
            AssetDatabase.CreateAsset(d, p);
            return d;
        }

        /// <summary>Böden im Volumen: Raster, Strahlen nach unten aus mehreren Höhen; kein Gelände; Kopfhöhe frei.</summary>
        private static List<Vector3> Sondenpunkte(Bounds b)
        {
            var pts = new List<Vector3>();
            for (float x = b.min.x + Raster * 0.5f; x < b.max.x; x += Raster)
            for (float z = b.min.z + Raster * 0.5f; z < b.max.z; z += Raster)
            for (float y = b.max.y; y > b.min.y; y -= 2.5f)
            {
                if (!Physics.Raycast(new Vector3(x, y, z), Vector3.down, out RaycastHit h, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (h.collider is TerrainCollider || h.normal.y < 0.7f || !b.Contains(h.point)) continue;
                if (Physics.Raycast(h.point + Vector3.up * 0.1f, Vector3.up, Kopf, ~0, QueryTriggerInteraction.Ignore)) continue;
                Vector3 p = h.point + Vector3.up * Hoehe;
                if (pts.Any(q => (q - p).sqrMagnitude < 2f * 2f)) continue;
                pts.Add(p);
            }
            return pts;
        }
    }
}
