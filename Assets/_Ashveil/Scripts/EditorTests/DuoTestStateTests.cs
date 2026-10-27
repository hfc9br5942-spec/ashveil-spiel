using System;
using System.Collections;
using System.Reflection;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashveil.EditorTests
{
    public sealed class DuoTestStateTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _object;
        private Component _runner;
        private IEnumerator _run;
        private bool _originalEnabled;
        private float _originalChance;

        [SetUp]
        public void SetUp()
        {
            _originalEnabled = CharacterEquipment.DodgeEnabled;
            _originalChance = EnemyBrain.DodgeChance;
            var type = Type.GetType("Ashveil.Networking.DuoTestDirector, Ashveil.Networking.Testing", true);
            _object = new GameObject("Duo state regression");
            _runner = _object.AddComponent(type);
        }

        [TearDown]
        public void TearDown()
        {
            // Also clean the intentionally failing baseline, whose runner has no lifecycle cleanup.
            if (_runner != null)
            {
                var handler = (Application.LogCallback)Delegate.CreateDelegate(typeof(Application.LogCallback), _runner,
                    _runner.GetType().GetMethod("OnLog", PrivateInstance));
                Application.logMessageReceived -= handler;
            }
            (_run as IDisposable)?.Dispose();
            if (_object != null) UnityEngine.Object.DestroyImmediate(_object);
            CharacterEquipment.DodgeEnabled = _originalEnabled;
            EnemyBrain.DodgeChance = _originalChance;
        }

        // Invoke the lifecycle handler without asking Unity's native SendMessage dispatch to run a non-ExecuteAlways behaviour in EditMode.
        private void DispatchLifecycle(string name) => _runner.GetType().GetMethod(name, PrivateInstance)?.Invoke(_runner, null);
        private void Begin(bool enabled, float chance)
        {
            CharacterEquipment.DodgeEnabled = enabled;
            EnemyBrain.DodgeChance = chance;
            _run = (IEnumerator)_runner.GetType().GetMethod("Run", PrivateInstance).Invoke(_runner, null);
            Assert.IsTrue(_run.MoveNext()); // The network suite/body is yielded, never executed by these EditMode tests.
            Assert.IsFalse(CharacterEquipment.DodgeEnabled);
            Assert.AreEqual(0f, EnemyBrain.DodgeChance);
            int before = (int)_runner.GetType().GetField("_logErrors", PrivateInstance).GetValue(_runner);
            LogAssert.Expect(LogType.Error, "DuoTestStateTests: aktives Abonnement");
            Debug.LogError("DuoTestStateTests: aktives Abonnement");
            Assert.AreEqual(before + 1, _runner.GetType().GetField("_logErrors", PrivateInstance).GetValue(_runner));
        }

        [TestCase("dispose", true, 0.05f)]
        [TestCase("disable", true, 0.05f)]
        [TestCase("destroy", true, 0.05f)]
        [TestCase("dispose", false, 0.37f)]
        [TestCase("disable", false, 0.37f)]
        [TestCase("destroy", false, 0.37f)]
        public void Beenden_stellt_exakte_Vorwerte_wieder_her_und_meldet_Log_ab(string exit, bool enabled, float chance)
        {
            Begin(enabled, chance);
            if (exit == "dispose") ((IDisposable)_run).Dispose();
            else DispatchLifecycle(exit == "disable" ? "OnDisable" : "OnDestroy");
            Assert.AreEqual(enabled, CharacterEquipment.DodgeEnabled);
            Assert.AreEqual(chance, EnemyBrain.DodgeChance);
            int before = (int)_runner.GetType().GetField("_logErrors", PrivateInstance).GetValue(_runner);
            LogAssert.Expect(LogType.Error, "DuoTestStateTests: Abonnement nach Cleanup");
            Debug.LogError("DuoTestStateTests: Abonnement nach Cleanup");
            Assert.AreEqual(before, _runner.GetType().GetField("_logErrors", PrivateInstance).GetValue(_runner));
            // Repeated teardown must not overwrite a later legitimate setting.
            CharacterEquipment.DodgeEnabled = !enabled;
            EnemyBrain.DodgeChance = 0.23f;
            DispatchLifecycle("OnDisable");
            Assert.AreEqual(!enabled, CharacterEquipment.DodgeEnabled);
            Assert.AreEqual(0.23f, EnemyBrain.DodgeChance);
        }

        [Test]
        public void Inaktiver_Runner_veraendert_keine_fremden_Kampfwerte()
        {
            CharacterEquipment.DodgeEnabled = false;
            EnemyBrain.DodgeChance = 0.29f;
            DispatchLifecycle("OnDisable");
            DispatchLifecycle("OnDestroy");
            Assert.IsFalse(CharacterEquipment.DodgeEnabled);
            Assert.AreEqual(0.29f, EnemyBrain.DodgeChance);
        }
    }
}
