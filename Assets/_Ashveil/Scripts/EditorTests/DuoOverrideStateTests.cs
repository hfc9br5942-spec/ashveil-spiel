using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Ashveil.Gameplay.Enemies;
using Ashveil.Networking;
using Ashveil.Rendering.Sky;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    public sealed class DuoOverrideStateTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo[] Fields =
        {
            Field(typeof(ColosseumInstanceHandler), "SeedOverride"),
            Field(typeof(ColosseumInstanceHandler), "TimeScaleOverride"),
            Field(typeof(ColosseumInstanceHandler), "StartDelayOverride"),
            Field(typeof(ColosseumInstanceHandler), "BellCountdownOverride"),
            Field(typeof(ColosseumInstanceHandler), "WipeDelayOverride"),
            Field(typeof(DungeonDirector), "ResetOverride"),
            Field(typeof(EnemyDirector), "TestIdleExtraTimeout"),
            Field(typeof(SkyDirector), "IgnoreLocal"),
            Field(typeof(SkyDirector), "ViewerOverride"),
            Field(typeof(ForemanEncounter), "FirstChargeOverride"),
            Field(typeof(SaltMotherEncounter), "FirstFloodOverride"),
            Field(typeof(SaltMotherEncounter), "FirstSpitOverride"),
            Field(typeof(HeartEncounter), "FirstEaterOverride"),
            Field(typeof(CaptainEncounter), "FirstBroadsideOverride")
        };

        private GameObject _object;
        private Component _runner;
        private IEnumerator _run;
        private object[] _original;
        private bool _originalDodge;
        private float _originalEnemyDodge;

        private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Static | BindingFlags.Public)
            ?? throw new MissingFieldException(type.FullName, name);
        private static object[] Read()
        {
            var values = new object[Fields.Length];
            for (int i = 0; i < Fields.Length; i++) values[i] = Fields[i].GetValue(null);
            return values;
        }
        private static void Write(object[] values)
        {
            for (int i = 0; i < Fields.Length; i++) Fields[i].SetValue(null, values[i]);
        }
        private static object[] Values(bool alternate, bool nullViewer) => new object[]
        {
            alternate ? 2468 : 1357,
            alternate ? 10f : 1.25f, alternate ? 2f : 12.5f, alternate ? 3f : 13.5f,
            alternate ? 4f : 14.5f, alternate ? 5f : 15.5f, alternate ? 6f : 16.5f,
            !alternate, nullViewer ? null : (object)new Vector3(12f, 34f, 56f),
            alternate ? 7f : 17.5f, alternate ? 8f : 18.5f, alternate ? 9f : 19.5f,
            alternate ? 11f : 20.5f, alternate ? 12f : 21.5f
        };
        private static void AssertValues(object[] expected)
        {
            var differences = new List<string>();
            for (int i = 0; i < Fields.Length; i++)
            {
                object actual = Fields[i].GetValue(null);
                if (!Equals(expected[i], actual))
                    differences.Add(Fields[i].DeclaringType.Name + "." + Fields[i].Name +
                        ": erwartet=" + (expected[i] ?? "null") + ", erhalten=" + (actual ?? "null"));
            }
            Assert.IsEmpty(differences, string.Join("\n", differences));
        }

        [SetUp]
        public void SetUp()
        {
            _original = Read();
            _originalDodge = Ashveil.Gameplay.Items.CharacterEquipment.DodgeEnabled;
            _originalEnemyDodge = EnemyBrain.DodgeChance;
            _object = new GameObject("Duo override regression");
            _runner = _object.AddComponent(Type.GetType("Ashveil.Networking.DuoTestDirector, Ashveil.Networking.Testing", true));
        }
        [TearDown]
        public void TearDown()
        {
            (_run as IDisposable)?.Dispose();
            if (_object != null) UnityEngine.Object.DestroyImmediate(_object);
            Write(_original);
            Ashveil.Gameplay.Items.CharacterEquipment.DodgeEnabled = _originalDodge;
            EnemyBrain.DodgeChance = _originalEnemyDodge;
        }
        private void Lifecycle(string name) => _runner.GetType().GetMethod(name, PrivateInstance).Invoke(_runner, null);

        [TestCase("dispose", false)]
        [TestCase("disable", false)]
        [TestCase("destroy", false)]
        [TestCase("dispose", true)]
        [TestCase("disable", true)]
        [TestCase("destroy", true)]
        public void Abbruch_restauriert_alle_Eintrittswerte_auch_nullable_Viewer(string exit, bool nullViewer)
        {
            object[] entry = Values(false, nullViewer);
            Write(entry);
            _run = (IEnumerator)_runner.GetType().GetMethod("Run", PrivateInstance).Invoke(_runner, null);
            Assert.IsTrue(_run.MoveNext()); // Suspend before any actual network suite executes.
            // Simulate the writes a nested suite leaves behind when it exits early or is cancelled.
            Write(Values(true, !nullViewer));
            if (exit == "dispose") ((IDisposable)_run).Dispose();
            else Lifecycle(exit == "disable" ? "OnDisable" : "OnDestroy");
            AssertValues(entry);
            object[] later = Values(true, !nullViewer);
            Write(later);
            Lifecycle("OnDisable");
            ((IDisposable)_run).Dispose();
            AssertValues(later); // An already-restored director cannot overwrite a later legitimate value.
        }

        [Test]
        public void Inaktiver_Director_veraendert_keine_Overrides()
        {
            object[] values = Values(false, false);
            Write(values);
            Lifecycle("OnDisable");
            Lifecycle("OnDestroy");
            AssertValues(values);
        }
    }
}
