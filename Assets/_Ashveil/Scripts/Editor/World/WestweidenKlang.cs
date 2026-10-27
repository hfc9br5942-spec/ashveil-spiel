using System.Collections.Generic;
using Ashveil.Gameplay.Audio;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Klangquellen setzen (Claude 14.10.2026, Klangteppich, TECH-125). Klänge: echte Aufnahmen, Freesound CC0
    /// (ART-119, Platzhalter bis eigene Aufnahmen), aufbereitet mit <c>Tools/claude/klang/aufbereiten_freesound.py</c>, unter
    /// <c>Art/ThirdParty/Audio/Westweiden/</c> (Herkunft je Datei: <c>LIZENZEN.md</c> daneben):
    ///   Mühlbach (Schleife alle 45 m am Wasserspiegel), Brandung (Schleife alle 150 m vor der Küste), Windmühlen (an der Nabe,
    ///   Holzmechanik im Mühlenkopf), Schaf- und Rinderglocken (an Herdentieren, <see cref="Glocke"/>), und ein
    ///   <see cref="KlangGebiet"/> über die ganze Zone: Lerchen am Tag, Grillen in der Nacht. Wind kommt von der KlangLandschaft.
    /// Zellen-Koordinaten (vor dem Verschieben an die Lage in Kareth).
    /// </summary>
    public static class WestweidenKlang
    {
        public const string Ordner = ProjectSetup.Root + "/Art/ThirdParty/Audio/Westweiden";

        public static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(Ordner + "/" + name + ".wav");

        private static AudioClip[] Clips(string praefix, int n)
        {
            var l = new List<AudioClip>();
            for (int i = 1; i <= n; i++) { AudioClip c = Clip(praefix + "_" + i); if (c != null) l.Add(c); }
            return l.ToArray();
        }

        /// <summary>Glocke an ein Herdentier hängen (Schaf hell, Rind tief). Gibt false zurück, wenn die Klänge fehlen.</summary>
        public static bool Glocke(Transform tier, bool rind)
        {
            AudioClip[] g = rind ? Clips("WW_Kuhglocke", 3) : Clips("WW_Schafglocke", 3);
            if (g.Length == 0) return false;
            var go = new GameObject("Glocke");
            go.transform.SetParent(tier, false);
            go.transform.localPosition = new Vector3(0f, rind ? 1.0f : 0.5f, 0.4f);
            var q = go.AddComponent<KlangQuelle>();
            q.Konfigurieren(g, false, rind ? 0.4f : 0.35f, 2f, rind ? 50f : 40f);
            q.pause = rind ? new Vector2(4f, 14f) : new Vector2(5f, 15f);   // Schafglocke = schon eine kleine Glockengruppe (4,5 s)
            q.grundTon = 1f;
            q.tonhoehe = rind ? 0.05f : 0.03f;
            return true;
        }

        public static string Bauen(Terrain t, float sx, float sz)
        {
            AudioClip bach = Clip("WW_Bach"), brand = Clip("WW_Brandung"), muehle = Clip("WW_Muehle");
            if (bach == null || brand == null || muehle == null) return "Klang: Klänge fehlen (Audio/Westweiden) · ";
            var root = new GameObject("Klang (Westweiden)").transform;
            int nb = 0, nk = 0, nm = 0;

            // Mühlbach: alle 45 m eine Quelle knapp über dem Wasser
            List<Vector3> b = WestweidenWasser.LetzterBach;
            float lauf = 999f;
            for (int i = 1; i < b.Count; i++)
            {
                lauf += Vector3.Distance(b[i - 1], b[i]);
                if (lauf < 45f) continue;
                lauf = 0f;
                Quelle(root, "Bach", b[i] + Vector3.up * 0.3f, new[] { bach }, true, 0.55f, 3f, 32f);
                nb++;
            }

            // Brandung: je 150 m Küste eine Quelle 15 m vor der Küstenlinie (erstes Land über 2 m von Westen her)
            Vector3 tp = t.transform.position;
            for (float z = 60f; z < sz - 60f; z += 150f)
            {
                float kx = -1f;
                for (float x = 0f; x < sx * 0.3f; x += 5f)
                    if (t.SampleHeight(new Vector3(x, 0f, z)) + tp.y > 2f) { kx = x; break; }
                if (kx < 0f) continue;
                Quelle(root, "Brandung", new Vector3(Mathf.Max(0f, kx - 15f), 0.5f, z), new[] { brand }, true, 0.9f, 25f, 260f);
                nk++;
            }

            // Windmühlen: an der Nabe (≈ 23 m), knarrende Holzmechanik als Schleife
            foreach (Transform tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!tr.name.StartsWith("Windmuehle ") || tr.parent == null) continue;
                var go = new GameObject("Klang Mühle");
                go.transform.SetParent(tr, false);
                go.transform.localPosition = new Vector3(0f, 23f, -2f);
                go.AddComponent<KlangQuelle>().Konfigurieren(new[] { muehle }, true, 0.8f, 8f, 140f);
                nm++;
            }

            // Klanggebiet über die ganze Zone: Lerchen am Tag, Grillen in der Nacht
            var g = new GameObject("Klanggebiet Westweiden");
            g.transform.SetParent(root, false);
            g.transform.position = new Vector3(sx * 0.5f, 200f, sz * 0.5f);
            g.AddComponent<KlangGebiet>().Konfigurieren(new Vector3(sx, 1200f, sz), Clips("WW_Lerche", 5), Clip("WW_Grillen_Nacht_Bett"));

            return $"Klang: {nb} am Bach, {nk} Brandung, {nm} Mühlen, Gebiet mit Lerchen/Grillen · ";
        }

        private static void Quelle(Transform root, string name, Vector3 p, AudioClip[] c, bool loop, float vol, float min, float max)
        {
            var go = new GameObject("Klang " + name);
            go.transform.SetParent(root, false);
            go.transform.position = p;
            go.AddComponent<KlangQuelle>().Konfigurieren(c, loop, vol, min, max);
        }
    }
}
