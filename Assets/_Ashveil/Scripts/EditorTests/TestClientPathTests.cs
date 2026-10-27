using System;
using System.IO;
using System.Reflection;
using Ashveil.Core;
using Ashveil.EditorTools;
using NUnit.Framework;

namespace Ashveil.EditorTests
{
    public sealed class TestClientPathTests
    {
        [TestCase("own", true)]
        [TestCase("case", true)]
        [TestCase("normalized", true)]
        [TestCase("release", false)]
        [TestCase("server", false)]
        [TestCase("other-project", false)]
        [TestCase("folder-prefix", false)]
        [TestCase("other-name", false)]
        [TestCase("relative", false)]
        [TestCase("null", false)]
        [TestCase("empty", false)]
        [TestCase("invalid", false)]
        [TestCase("whitespace", false)]
        public void Nur_exakter_normalisierter_Testclientpfad_wird_ausgewaehlt(string scenario, bool expected)
        {
            string own = ProjectPaths.Get("Builds", "TestClient", "Ashveil.exe");
            string candidate;
            switch (scenario)
            {
                case "own": candidate = own; break;
                case "case": candidate = own.ToUpperInvariant(); break;
                case "normalized": candidate = ProjectPaths.ProjectRoot + "/Builds/TestClient/../TestClient/Ashveil.exe"; break;
                case "release": candidate = ProjectPaths.Get("Builds", "ReleaseAudit", "case", "release", "Ashveil.exe"); break;
                case "server": candidate = ProjectPaths.Get("Builds", "Server", "Ashveil.exe"); break;
                case "other-project": candidate = Path.Combine(Path.GetTempPath(), "OtherAshveil", "Builds", "TestClient", "Ashveil.exe"); break;
                case "folder-prefix": candidate = ProjectPaths.Get("Builds", "TestClient2", "Ashveil.exe"); break;
                case "other-name": candidate = ProjectPaths.Get("Builds", "TestClient", "Ashveil-copy.exe"); break;
                case "relative": candidate = "Ashveil.exe"; break;
                case "null": candidate = null; break;
                case "empty": candidate = ""; break;
                case "invalid": candidate = "bad\0/Ashveil.exe"; break;
                default: candidate = "   "; break;
            }
            MethodInfo selector = typeof(NetworkSetupBuilder).GetMethod("IsTestClientExecutable", BindingFlags.Static | BindingFlags.NonPublic);
            bool selected;
            if (selector != null) selected = (bool)selector.Invoke(null, new object[] { candidate });
            else
            {
                // Original selection is only the process/executable name; no actual process is ever killed in this test.
                try { selected = string.Equals(Path.GetFileNameWithoutExtension(candidate), "Ashveil", StringComparison.OrdinalIgnoreCase); }
                catch (ArgumentException) { selected = false; }
            }
            Assert.AreEqual(expected, selected, scenario + ": " + candidate);
        }
    }
}
