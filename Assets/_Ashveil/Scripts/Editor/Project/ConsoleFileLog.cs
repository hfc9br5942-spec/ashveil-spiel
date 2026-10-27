using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Schreibt Fehler, Warnungen und [Ashveil]-Meldungen des Editors zusaetzlich nach Logs/Ashveil/EditorKonsole.txt
    /// (lesbar ohne Unity-Fenster; wird bei jedem Neuladen der Skripte geleert, hoechstens ~2 MB).
    /// </summary>
    [InitializeOnLoad]
    public static class ConsoleFileLog
    {
        private static readonly string PathLog = Ashveil.Core.ProjectPaths.Logs("EditorKonsole.txt");
        private static long _written;

        static ConsoleFileLog()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathLog));
                File.WriteAllText(PathLog, "ASHVEIL Editor-Konsole ab " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n", new UTF8Encoding(false));
            }
            catch (Exception) { }
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished -= OnCompiled;
            UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished += OnCompiled;
        }

        /// <summary>Compiler-Meldungen (Fehler/Warnungen je Assembly) nach Logs/Ashveil/Kompilieren.txt.</summary>
        private static void OnCompiled(string assembly, UnityEditor.Compilation.CompilerMessage[] messages)
        {
            try
            {
                string p = Ashveil.Core.ProjectPaths.Logs("Kompilieren.txt");
                var sb = new StringBuilder();
                sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").Append(Path.GetFileName(assembly)).Append('\n');
                foreach (var m in messages)
                    if (m.type == UnityEditor.Compilation.CompilerMessageType.Error) sb.Append("  FEHLER ").Append(m.message).Append('\n');
                File.AppendAllText(p, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception) { }
        }

        private static string _lastMessage;
        private static int _repeats;

        private static void OnLog(string message, string stack, LogType type)
        {
            bool keep = type != LogType.Log || (message != null && message.StartsWith("[Ashveil]"));
            if (!keep) return;
            lock (PathLog)
            {
                // Gleiche Meldung in Serie (z. B. ein Fehler je Frame): nur zaehlen
                if (message == _lastMessage) { _repeats++; return; }
                string line = "";
                if (_repeats > 0) line += $"  (… {_repeats}× wiederholt)\n";
                _lastMessage = message;
                _repeats = 0;
                line += $"[{DateTime.Now:HH:mm:ss}] {type}: {message}\n";
                if (type == LogType.Exception || type == LogType.Error) line += Trim(stack) + "\n";
                try
                {
                    // Zu gross: neu beginnen statt verstummen
                    if (_written > 2_000_000)
                    {
                        File.WriteAllText(PathLog, "ASHVEIL Editor-Konsole (neu begonnen, Datei war voll) ab " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n", new UTF8Encoding(false));
                        _written = 0;
                    }
                    File.AppendAllText(PathLog, line, Encoding.UTF8);
                    _written += line.Length;
                }
                catch (Exception) { }
            }
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string[] lines = s.Split('\n');
            return string.Join("\n", lines, 0, Math.Min(lines.Length, 6));
        }
    }
}
