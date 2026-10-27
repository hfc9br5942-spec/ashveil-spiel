using System;
using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.CameraSystem;
using Ashveil.Gameplay.Quests;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// Spielt Dialog-Szenen auf dem eigenen Rechner ab (Untertitel, Pausen, Ausloeser fuer Figuren und Tiere, kurzer
    /// Bildausschnitt). Der Server entscheidet, WANN eine Szene kommt (<see cref="StoryClient.SceneRequested"/>); was danach
    /// geschieht, bestimmt die Szene selbst (end=talk:… oeffnet das Gespraech) und der Server erfaehrt das Ende ("scene_done").
    /// Klick/Leertaste: naechste Zeile. Keine Zeitlupe, kein Kontrollverlust – man kann weiterlaufen.
    /// </summary>
    public static class StoryDirector
    {
        private static DialogueScene _scene;
        private static int _index = -1;
        private static float _left;
        private static bool _hooked;
        private static readonly Dictionary<string, Func<Vector3?>> Focus = new Dictionary<string, Func<Vector3?>>();

        public static bool Active => _scene != null;
        public static string SceneId => _scene != null ? _scene.Id : "";
        public static DialogueLine Line => _scene != null && _index >= 0 && _index < _scene.Lines.Count ? _scene.Lines[_index] : null;
        /// <summary>Zeilen-Nummer (Tests).</summary>
        public static int Index => _index;
        /// <summary>Letzte fertig gespielte Szene (Tests).</summary>
        public static string LastFinished { get; private set; } = "";

        /// <summary>Ausloeser aus der Szene (z. B. "foal_sniff", "breeder_approach").</summary>
        public static event Action<string> Cue;
        public static event Action<string> SceneStarted;
        public static event Action<string> SceneFinished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _scene = null;
            _index = -1;
            _hooked = false;
            Focus.Clear();
            LastFinished = "";
        }

        /// <summary>Einmalig vom Hauptschirm (UI) aufgerufen: auf Szenen-Bitten des Servers hoeren.</summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            StoryClient.SceneRequested += Play;
        }

        /// <summary>Kamerapunkt fuer "cam=name" (Figuren melden sich an, solange sie da sind).</summary>
        public static void RegisterFocus(string name, Func<Vector3?> where) => Focus[name] = where;
        public static void UnregisterFocus(string name) => Focus.Remove(name);

        public static void Play(string sceneId)
        {
            DialogueScene s = DialogueLibrary.Scene(sceneId);
            if (s == null)
            {
                Debug.LogWarning($"[Ashveil] Szene '{sceneId}' fehlt (Resources/Dialog).");
                StoryClient.Send("scene_done", sceneId);
                return;
            }
            if (_scene != null) Finish(false);
            _scene = s;
            _index = -1;
            SceneStarted?.Invoke(s.Id);
            Next();
        }

        /// <summary>Naechste Zeile (Klick/Leertaste) – Regie-Schritte laufen immer zu Ende.</summary>
        public static void Skip()
        {
            DialogueLine l = Line;
            if (l == null || string.IsNullOrEmpty(l.TextKey)) return;
            _left = Mathf.Min(_left, 0.05f);
        }

        /// <summary>Takt (vom Untertitel-Fenster jeden Frame).</summary>
        public static void Tick(float dt)
        {
            if (_scene == null) return;
            _left -= dt;
            if (_left <= 0f) Next();
        }

        private static void Next()
        {
            _index++;
            if (_scene == null) return;
            if (_index >= _scene.Lines.Count)
            {
                Finish(true);
                return;
            }
            DialogueLine l = _scene.Lines[_index];
            _left = DialogueLibrary.Duration(l);
            if (!string.IsNullOrEmpty(l.Cue)) Cue?.Invoke(l.Cue);
            if (!string.IsNullOrEmpty(l.Cam))
            {
                Vector3? p = null;
                if (Focus.TryGetValue(l.Cam, out Func<Vector3?> f)) p = f();
                else if (l.Cam.StartsWith("npc.", StringComparison.Ordinal))
                {
                    // cam=npc.<kennung>: Figur der Szene ohne eigene Anmeldung (Kareth: Lager-Szenen, WORLD-238)
                    NpcIdentity n = NpcIdentity.Find(l.Cam);
                    if (n != null) p = n.transform.position + Vector3.up * 1.4f;
                }
                Camera cam = Camera.main;
                MMOCameraController cc = cam != null ? cam.GetComponent<MMOCameraController>() : null;
                if (p.HasValue && cc != null) cc.FrameTowards(p.Value, Mathf.Min(3.5f, _left));
            }
        }

        private static void Finish(bool completed)
        {
            DialogueScene s = _scene;
            _scene = null;
            _index = -1;
            if (s == null) return;
            LastFinished = s.Id;
            SceneFinished?.Invoke(s.Id);
            if (!completed) return;
            StoryClient.Send("scene_done", s.Id);
            if (s.EndAction.StartsWith("talk:"))
            {
                string npc = s.EndAction.Substring(5);
                ProgressionClient.Current?.Talk(npc);
            }
        }

        /// <summary>Tests/Abbruch (Verbindungsende): Szene sofort beenden, ohne Folgeaktion.</summary>
        public static void Abort()
        {
            if (_scene != null) Finish(false);
        }
    }
}
