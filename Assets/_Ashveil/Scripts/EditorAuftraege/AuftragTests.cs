using System;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftrag „tests“ für den Auftragsordner: EditMode-Tests über die Test-Runner-Schnittstelle (wie das Fenster Test Runner).
    /// Parameter leer = alle EditMode-Tests; sonst Name oder Muster einer Gruppe, z. B. „SwimTests“.
    /// Eigenes Assembly, weil nur hier die Test-Runner-Bibliotheken eingebunden sind (Ashveil.Editor bleibt unverändert).
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragTests
    {
        static AuftragTests()
        {
            Auftragsordner.Registrieren("tests", Starten, true, 3600);
        }

        private static void Starten(Auftragsordner.Auftrag auftrag, Action<bool, string> fertig)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var rueckmeldung = new Rueckmeldung(api, fertig);
            api.RegisterCallbacks(rueckmeldung);
            var filter = new Filter { testMode = TestMode.EditMode };
            string gruppe = (auftrag.parameter ?? "").Trim();
            if (gruppe.Length > 0) filter.groupNames = new[] { gruppe };
            try
            {
                api.Execute(new ExecutionSettings(filter));
            }
            catch (Exception)
            {
                try { api.UnregisterCallbacks(rueckmeldung); } catch (Exception) { }
                throw;
            }
        }

        /// <summary>IErrorCallbacks: Bricht der Lauf mit einem Fehler ab, kommt kein RunFinished – dann sofort melden.</summary>
        private sealed class Rueckmeldung : ICallbacks, IErrorCallbacks
        {
            private readonly TestRunnerApi _api;
            private readonly Action<bool, string> _fertig;
            private readonly StringBuilder _fehler = new StringBuilder();
            private bool _gemeldet;

            public Rueckmeldung(TestRunnerApi api, Action<bool, string> fertig)
            {
                _api = api;
                _fertig = fertig;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (_gemeldet) return;
                _gemeldet = true;
                try { _api.UnregisterCallbacks(this); } catch (Exception) { }
                string text = $"{result.PassCount} bestanden, {result.FailCount} fehlgeschlagen, {result.SkipCount} übersprungen, " +
                              $"{result.InconclusiveCount} offen ({result.Duration:0.0} s)";
                if (_fehler.Length > 0) text += "\n" + _fehler;
                _fertig(result.FailCount == 0 && result.PassCount > 0, text);
            }

            public void OnError(string message)
            {
                if (_gemeldet) return;
                _gemeldet = true;
                try { _api.UnregisterCallbacks(this); } catch (Exception) { }
                _fertig(false, "Testlauf abgebrochen: " + message);
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren || result.TestStatus != TestStatus.Failed) return;
                if (_fehler.Length > 20000) return;
                _fehler.Append("FEHLER ").Append(result.FullName).Append(": ").Append(result.Message).Append('\n');
            }
        }
    }
}
