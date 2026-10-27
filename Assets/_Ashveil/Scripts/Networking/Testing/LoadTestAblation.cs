using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// CORE-005 / NET-012 (02.10.2026): Wer kostet am Server wie viel? Im Spieler-Build gibt es keine Messpunkte je Skript,
    /// deshalb Ausschluss-Messung: alle Komponenten EINES Typs fuer kurze Zeit abschalten und die Rechenzeit des Servers
    /// mit und ohne sie vergleichen. Nur im Lasttest-Diagnoselauf (-ashveil-loadtest-ablation &lt;startsekunde&gt;).
    /// Netzwerk-Komponenten (FishNet) und Lasttest-Werkzeuge werden nie abgeschaltet. Das Spiel verhaelt sich waehrend
    /// der Messung kurz falsch (z. B. keine Animation) – nur fuer diese Diagnose zulaessig.
    /// </summary>
    public sealed class LoadTestAblation : MonoBehaviour
    {
        public static float LastWorkMs; // vom LoadTestServerMonitor je Bild gesetzt

        private const float OffSeconds = 4f, OnSeconds = 2f, Settle = 0.5f;
        private const int MinInstances = 8; // nur Typen, die mit der Spielerzahl wachsen

        private string _file;
        private float _startAt;
        private readonly StringBuilder _out = new StringBuilder();

        public static void Begin(string dir, float startAt)
        {
            var go = new GameObject("LoadTestAblation");
            DontDestroyOnLoad(go);
            var a = go.AddComponent<LoadTestAblation>();
            a._file = Path.Combine(dir, "server_ausschluss.txt");
            a._startAt = startAt;
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(_startAt);
            var types = Candidates();
            _out.AppendLine("Ausschluss-Messung (Server-Rechenzeit je Bild, Mittel): Typ · Anzahl · mit · ohne · Ersparnis");
            Write();
            foreach (var (type, list) in types)
            {
                // Grundlinie direkt davor (alle an)
                float on = 0f; int nOn = 0;
                float until = Time.realtimeSinceStartup + OnSeconds;
                while (Time.realtimeSinceStartup < until) { on += LastWorkMs; nOn++; yield return null; }

                var changed = new List<Behaviour>();
                foreach (Behaviour b in list)
                    if (b != null && b.enabled) { b.enabled = false; changed.Add(b); }
                float off = 0f; int nOff = 0;
                float settleEnd = Time.realtimeSinceStartup + Settle;
                until = Time.realtimeSinceStartup + OffSeconds;
                while (Time.realtimeSinceStartup < until)
                {
                    if (Time.realtimeSinceStartup > settleEnd) { off += LastWorkMs; nOff++; }
                    yield return null;
                }
                foreach (Behaviour b in changed) if (b != null) b.enabled = true;

                float mOn = nOn > 0 ? on / nOn : 0f, mOff = nOff > 0 ? off / nOff : 0f;
                _out.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} · {1} · {2:0.00} · {3:0.00} · {4:0.00}",
                    type.FullName, changed.Count, mOn, mOff, mOn - mOff));
                Write();
                yield return new WaitForSecondsRealtime(Settle);
            }
            _out.AppendLine("ENDE");
            Write();
        }

        private void Write()
        {
            try { File.WriteAllText(_file, _out.ToString()); } catch (IOException) { }
        }

        private static bool HasLoop(Type t)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                if (x.GetMethod("Update", f, null, Type.EmptyTypes, null) != null ||
                    x.GetMethod("LateUpdate", f, null, Type.EmptyTypes, null) != null ||
                    x.GetMethod("FixedUpdate", f, null, Type.EmptyTypes, null) != null)
                    return true;
            return false;
        }

        private static List<(Type, List<Behaviour>)> Candidates()
        {
            var map = new Dictionary<Type, List<Behaviour>>();
            foreach (Behaviour b in FindObjectsByType<Behaviour>(FindObjectsInactive.Exclude))
            {
                if (b == null || !b.enabled) continue;
                Type t = b.GetType();
                string ns = t.Namespace ?? "";
                if (ns.StartsWith("FishNet", StringComparison.Ordinal)) continue;             // Netzwerk nie anfassen
                if (t == typeof(LoadTestServerMonitor) || t == typeof(LoadTestAblation)) continue;
                if (typeof(FishNet.Object.NetworkBehaviour).IsAssignableFrom(t)) continue;   // eigene Netz-Komponenten auch nicht
                bool engine = t == typeof(Animator);
                if (!engine && !(b is MonoBehaviour && HasLoop(t))) continue;
                if (!map.TryGetValue(t, out var list)) map[t] = list = new List<Behaviour>();
                list.Add(b);
            }
            var result = new List<(Type, List<Behaviour>)>();
            foreach (var kv in map) if (kv.Value.Count >= MinInstances) result.Add((kv.Key, kv.Value));
            result.Sort((a, b) => b.Item2.Count.CompareTo(a.Item2.Count));
            return result;
        }
    }
}
