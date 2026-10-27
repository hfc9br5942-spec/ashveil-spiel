using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    public sealed class DuoClientCleanupTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private readonly List<Process> _children = new List<Process>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Component> _runners = new List<Component>();
        private readonly List<IEnumerator> _runs = new List<IEnumerator>();

        private Component Director()
        {
            var obj = new GameObject("Owned test-client regression");
            _objects.Add(obj);
            var runner = obj.AddComponent(Type.GetType("Ashveil.Networking.DuoTestDirector, Ashveil.Networking.Testing", true));
            _runners.Add(runner);
            return runner;
        }
        private static List<Process> Clients(Component runner) =>
            (List<Process>)runner.GetType().GetField("Clients", Private).GetValue(runner);
        private void Begin(Component runner)
        {
            var run = (IEnumerator)runner.GetType().GetMethod("Run", Private).Invoke(runner, null);
            _runs.Add(run);
            Assert.IsTrue(run.MoveNext()); // Never traverse the actual network suite.
        }
        private static void Lifecycle(Component runner, string name) => runner.GetType().GetMethod(name, Private).Invoke(runner, null);
        private static bool WaitForOriginalExit(int pid, DateTime started)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 5000)
            {
                try
                {
                    using (Process observed = Process.GetProcessById(pid))
                        if (observed.HasExited || observed.StartTime != started) return true;
                }
                catch (ArgumentException) { return true; } // Original PID is no longer present.
                catch (InvalidOperationException) { return true; } // It exited during the observation.
                System.Threading.Thread.Sleep(20);
            }
            return false;
        }
        private Process Child(bool exited)
        {
            // Only this test's new local children are ever terminated; no process discovery by name/PID.
            var child = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60",
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            });
            Assert.IsNotNull(child);
            _children.Add(child);
            return child;
        }
        [TearDown]
        public void TearDown()
        {
            // Keep baseline failures isolated; remove only our own injected process objects before destroying directors.
            foreach (Component runner in _runners)
                if (runner != null) foreach (Process child in _children) Clients(runner).Remove(child);
            foreach (IEnumerator run in _runs) (run as IDisposable)?.Dispose();
            foreach (GameObject obj in _objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            foreach (Process child in _children)
            {
                try { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
                catch (InvalidOperationException) { } // Production cleanup may already have disposed its owned handle.
                finally { child.Dispose(); }
            }
            _runs.Clear(); _runners.Clear(); _objects.Clear(); _children.Clear();
        }

        [TestCase("dispose", false)]
        [TestCase("disable", false)]
        [TestCase("destroy", false)]
        [TestCase("dispose", true)]
        [TestCase("disable", true)]
        [TestCase("destroy", true)]
        public void Abbruch_beendet_eigenes_Kind_und_gibt_Handle_frei(string exit, bool alreadyExited)
        {
            Component runner = Director();
            Assert.AreEqual(0, Clients(runner).Count, "No existing owned clients may be consumed by this test");
            Begin(runner);
            Process child = Child(alreadyExited);
            // Observe only the identity of our new child; do not depend on Mono external-handle WaitForExit semantics.
            int pid = child.Id;
            DateTime started = child.StartTime;
            {
                if (alreadyExited)
                {
                    child.Kill();
                    Assert.IsTrue(WaitForOriginalExit(pid, started), "Own child must have exited before testing cleanup of an ended process");
                }
                Clients(runner).Add(child);
                if (exit == "dispose") ((IDisposable)_runs[0]).Dispose();
                else Lifecycle(runner, exit == "disable" ? "OnDisable" : "OnDestroy");
                Assert.IsTrue(WaitForOriginalExit(pid, started), "Owned child is still alive after director cleanup");
                Assert.AreEqual(0, Clients(runner).Count, "Completed client handles must be removed");
                Assert.Throws<InvalidOperationException>(() => { int ignored = child.Id; }, "Owned handle must be disposed");
                Lifecycle(runner, "OnDisable");
                Assert.AreEqual(0, Clients(runner).Count);
            }
        }

        [Test]
        public void Clientbesitz_ist_pro_Director_und_Abbruch_beruehrt_keinen_anderen()
        {
            Component first = Director(), second = Director();
            Assert.IsFalse(first.GetType().GetField("Clients", Private).IsStatic, "Clients cannot be shared by unrelated directors");
            Process child = Child(false), other = Child(false);
            int pid = child.Id, otherPid = other.Id;
            DateTime started = child.StartTime, otherStarted = other.StartTime;
            {
                Clients(first).Add(child); Clients(second).Add(other);
                Lifecycle(first, "OnDisable"); // Cleanup belongs to process ownership, independently of combat-state ownership.
                Assert.IsTrue(WaitForOriginalExit(pid, started));
                Assert.IsFalse(other.HasExited, "Another director's child was terminated");
                Assert.AreEqual(1, Clients(second).Count);
                Lifecycle(second, "OnDisable");
                Assert.IsTrue(WaitForOriginalExit(otherPid, otherStarted));
            }
        }

        [Test]
        public void Fehlgeschlagener_Start_registriert_keinen_Prozess()
        {
            Component runner = Director();
            Assert.AreEqual(0, Clients(runner).Count);
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "MissingClient.exe");
            var error = Assert.Throws<TargetInvocationException>(() => runner.GetType().GetMethod("StartClient", Private)
                .Invoke(runner, new object[] { missing, "cleanup-regression" }));
            Assert.IsInstanceOf<System.ComponentModel.Win32Exception>(error.InnerException);
            Assert.AreEqual(0, Clients(runner).Count);
        }
    }
}
