using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Sprechweise einer Figur (Vorgabe: Sprecherprofile). Dieselbe Figur kann mehrere haben (Panik, ruhig, muede).</summary>
    public sealed class SpeakerProfile
    {
        public string Id;
        /// <summary>Texttabellen-Schluessel des angezeigten Namens (leer = Gedanke/Beobachtung ohne Namen).</summary>
        public string NameKey;
        public Color Color = new Color(0.91f, 0.79f, 0.54f);
        /// <summary>Lesetempo (Zeichen je Sekunde) – bestimmt, wie lange eine Zeile stehen bleibt.</summary>
        public float Pace = 16f;
        /// <summary>broken (abgehackt, Panik) · calm · tired (langsam, Pausen) · thought (kursiv, ohne Namen).</summary>
        public string Style = "calm";

        public string DisplayName => string.IsNullOrEmpty(NameKey) ? "" : Loc.T(NameKey);
    }

    /// <summary>Eine Zeile oder ein Regie-Schritt einer Szene.</summary>
    public sealed class DialogueLine
    {
        public string Speaker;
        /// <summary>Texttabellen-Schluessel (leer = reiner Regie-Schritt).</summary>
        public string TextKey;
        public float Wait;
        public string Cue;
        public string Cam;

        public string Text => string.IsNullOrEmpty(TextKey) ? "" : Loc.T(TextKey);
    }

    public sealed class DialogueScene
    {
        public string Id;
        /// <summary>Was nach der Szene passiert: "talk:npc.*" (Gespraechsfenster oeffnen) oder leer.</summary>
        public string EndAction = "";
        public readonly List<DialogueLine> Lines = new List<DialogueLine>();
    }

    /// <summary>
    /// Dialog-Szenen aus Textdateien unter <c>Resources/Dialog/*.txt</c> (Format im Dateikopf). Texte selbst stehen in der
    /// Texttabelle (<see cref="Loc"/>) – Szenen bestimmen nur Reihenfolge, Sprecher, Pausen und Ausloeser.
    /// </summary>
    public static class DialogueLibrary
    {
        private static readonly Dictionary<string, SpeakerProfile> Speakers = new Dictionary<string, SpeakerProfile>();
        private static readonly Dictionary<string, DialogueScene> Scenes = new Dictionary<string, DialogueScene>();
        private static bool _loaded;

        public static int SceneCount { get { Ensure(); return Scenes.Count; } }

        public static DialogueScene Scene(string id)
        {
            Ensure();
            return id != null && Scenes.TryGetValue(id, out DialogueScene s) ? s : null;
        }

        public static SpeakerProfile Speaker(string id)
        {
            Ensure();
            return id != null && Speakers.TryGetValue(id, out SpeakerProfile p) ? p : null;
        }

        public static IEnumerable<DialogueScene> AllScenes { get { Ensure(); return Scenes.Values; } }

        public static void Reload() => _loaded = false;

        private static void Ensure()
        {
            if (_loaded) return;
            _loaded = true;
            Speakers.Clear();
            Scenes.Clear();
            foreach (TextAsset f in Resources.LoadAll<TextAsset>("Dialog")) Parse(f.text, f.name);
        }

        /// <summary>Szenen-Datei lesen (auch fuer Tests). Fehler werden gemeldet, nie geworfen.</summary>
        public static void Parse(string content, string source = "")
        {
            if (string.IsNullOrEmpty(content)) return;
            DialogueScene cur = null;
            int lineNo = 0;
            foreach (string raw in content.Split('\n'))
            {
                lineNo++;
                string t = raw.TrimEnd('\r').Trim();
                if (t.Length == 0 || t[0] == '#') continue;
                if (t.StartsWith("@speaker "))
                {
                    string[] parts = t.Substring(9).Split(' ');
                    var sp = new SpeakerProfile { Id = parts[0] };
                    for (int i = 1; i < parts.Length; i++)
                    {
                        int eq = parts[i].IndexOf('=');
                        if (eq <= 0) continue;
                        string k = parts[i].Substring(0, eq), v = parts[i].Substring(eq + 1);
                        switch (k)
                        {
                            case "name": sp.NameKey = v; break;
                            case "color": if (ColorUtility.TryParseHtmlString(v, out Color c)) sp.Color = c; break;
                            case "pace": sp.Pace = Num(v, 16f); break;
                            case "style": sp.Style = v; break;
                        }
                    }
                    Speakers[sp.Id] = sp;
                    continue;
                }
                if (t.StartsWith("@scene "))
                {
                    string[] parts = t.Substring(7).Split(' ');
                    cur = new DialogueScene { Id = parts[0] };
                    for (int i = 1; i < parts.Length; i++)
                        if (parts[i].StartsWith("end=")) cur.EndAction = parts[i].Substring(4);
                    Scenes[cur.Id] = cur;
                    continue;
                }
                if (t == "@end")
                {
                    cur = null;
                    continue;
                }
                if (cur == null)
                {
                    Debug.LogWarning($"[Ashveil] Dialog {source}:{lineNo}: Zeile ausserhalb einer Szene – {t}");
                    continue;
                }
                var line = new DialogueLine();
                string rest;
                if (t.StartsWith("~"))
                {
                    rest = t.Substring(1).Trim();
                }
                else
                {
                    int colon = t.IndexOf(':');
                    if (colon <= 0)
                    {
                        Debug.LogWarning($"[Ashveil] Dialog {source}:{lineNo}: erwartet 'sprecher: schluessel' – {t}");
                        continue;
                    }
                    line.Speaker = t.Substring(0, colon).Trim();
                    rest = t.Substring(colon + 1).Trim();
                    int sp = rest.IndexOf(' ');
                    line.TextKey = sp < 0 ? rest : rest.Substring(0, sp);
                    rest = sp < 0 ? "" : rest.Substring(sp + 1);
                    if (!Speakers.ContainsKey(line.Speaker))
                        Debug.LogWarning($"[Ashveil] Dialog {source}:{lineNo}: unbekannter Sprecher '{line.Speaker}'.");
                }
                foreach (string opt in rest.Split(' '))
                {
                    int eq = opt.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = opt.Substring(0, eq), v = opt.Substring(eq + 1);
                    if (k == "wait") line.Wait = Num(v, 0f);
                    else if (k == "cue") line.Cue = v;
                    else if (k == "cam") line.Cam = v;
                }
                cur.Lines.Add(line);
            }
        }

        private static float Num(string s, float fallback) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

        /// <summary>Wie lange eine Zeile stehen bleibt (s): Lesezeit nach Tempo des Sprechers, mindestens 1,6 s.</summary>
        public static float Duration(DialogueLine l)
        {
            if (string.IsNullOrEmpty(l.TextKey)) return l.Wait;
            SpeakerProfile sp = Speaker(l.Speaker);
            float pace = sp != null ? Mathf.Max(6f, sp.Pace) : 16f;
            float extra = sp != null && sp.Style == "tired" ? 0.8f : sp != null && sp.Style == "broken" ? 0.1f : 0.4f;
            return Mathf.Max(1.6f, l.Text.Length / pace + 1.0f + extra) + l.Wait;
        }
    }
}
