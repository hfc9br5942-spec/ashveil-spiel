using System;
using System.Collections;
using System.Reflection;
using Ashveil.Rendering;
using Ashveil.World;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    public sealed class DuoIndoorScopeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _object;
        private Component _runner;
        private IEnumerator _run;
        private bool _oldOverride, _oldRoom;
        private DungeonSlot _oldSlot;
        private static readonly PropertyInfo Room = typeof(IndoorAtmosphere).GetProperty("IsIndoors");
        private static readonly PropertyInfo Slot = typeof(IndoorAtmosphere).GetProperty("CurrentSlot");

        private static void SetRoom(bool indoors) => Room.GetSetMethod(true).Invoke(null, new object[] { indoors });
        private void Call(string name) => _runner.GetType().GetMethod(name, Private).Invoke(_runner, null);
        private void BeginWindow()
        {
            var begin = _runner.GetType().GetMethod("BeginIndoorTest", Private);
            // Baseline adapter: exact original DayNight block entry, without network/time/render side effects.
            if (begin == null) AtmosphereController.IndoorOverride = true;
            else begin.Invoke(_runner, null);
        }
        private void EndWindow()
        {
            var end = _runner.GetType().GetMethod("RestoreIndoorTest", Private);
            // Baseline adapter: exact original normal block exit. Abort cases do not call this.
            if (end == null) AtmosphereController.IndoorOverride = false;
            else end.Invoke(_runner, null);
        }
        [SetUp]
        public void SetUp()
        {
            _oldOverride = AtmosphereController.IndoorOverride;
            _oldRoom = IndoorAtmosphere.IsIndoors;
            _oldSlot = IndoorAtmosphere.CurrentSlot;
            SetRoom(false);
            Slot.GetSetMethod(true).Invoke(null, new object[] { null });
            _object = new GameObject("Duo indoor test scope regression");
            _runner = _object.AddComponent(Type.GetType("Ashveil.Networking.DuoTestDirector, Ashveil.Networking.Testing", true));
        }
        [TearDown]
        public void TearDown()
        {
            (_run as IDisposable)?.Dispose();
            if (_object != null) UnityEngine.Object.DestroyImmediate(_object);
            SetRoom(_oldRoom);
            Slot.GetSetMethod(true).Invoke(null, new object[] { _oldSlot });
            AtmosphereController.IndoorOverride = _oldOverride;
        }
        private void BeginRun()
        {
            _run = (IEnumerator)_runner.GetType().GetMethod("Run", Private).Invoke(_runner, null);
            Assert.IsTrue(_run.MoveNext()); // No network suite, clock jump, lighting or report action.
        }

        [TestCase("normal", false)]
        [TestCase("dispose", false)]
        [TestCase("disable", false)]
        [TestCase("destroy", false)]
        [TestCase("normal", true)]
        [TestCase("dispose", true)]
        [TestCase("disable", true)]
        [TestCase("destroy", true)]
        public void Kurzes_Testfenster_restauriert_Vorwert_und_ist_idempotent(string exit, bool previous)
        {
            SetRoom(previous); // Also cover a host that really was indoors at entry.
            AtmosphereController.IndoorOverride = previous;
            BeginRun();
            BeginWindow();
            Assert.IsTrue(AtmosphereController.IndoorOverride);
            if (exit == "normal") EndWindow();
            else if (exit == "dispose") ((IDisposable)_run).Dispose();
            else Call(exit == "disable" ? "OnDisable" : "OnDestroy");
            Assert.AreEqual(previous, AtmosphereController.IndoorOverride);
            AtmosphereController.IndoorOverride = !previous;
            Call("OnDisable");
            Assert.AreEqual(!previous, AtmosphereController.IndoorOverride, "Repeated cleanup must not overwrite later values");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Echter_Raumwechsel_waehrend_Testfenster_bleibt_erhalten(bool startingIndoors)
        {
            SetRoom(startingIndoors);
            AtmosphereController.IndoorOverride = startingIndoors;
            BeginRun(); BeginWindow();
            // Model precisely the state/flag writes made by IndoorAtmosphere.Enter/Leave.
            SetRoom(!startingIndoors);
            AtmosphereController.IndoorOverride = !startingIndoors;
            EndWindow();
            Assert.AreEqual(!startingIndoors, AtmosphereController.IndoorOverride);
            Call("OnDisable");
            Assert.AreEqual(!startingIndoors, AtmosphereController.IndoorOverride);
        }

        [Test]
        public void Ohne_Testfenster_ueberschreibt_Run_Ende_keinen_Raumwechsel()
        {
            AtmosphereController.IndoorOverride = false;
            BeginRun();
            SetRoom(true);
            AtmosphereController.IndoorOverride = true;
            Call("OnDisable");
            Assert.IsTrue(AtmosphereController.IndoorOverride);
        }
    }
}
