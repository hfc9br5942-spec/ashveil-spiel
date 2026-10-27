using System;
using Ashveil.Networking;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Gleichzeitige Charakter-Erstellung: exklusive Reservierung, unveraenderte Zuordnung und Freigabe beim Abbruch.
    /// Ohne Server, Spielstaende oder Szenen; geprueft wird dieselbe Reservierung wie im ServerRpc.
    /// </summary>
    public sealed class NetworkInventoryCreationTests
    {
        private GameObject _firstObject, _secondObject;
        private NetworkInventory _first, _second;
        private string _profile, _otherProfile;

        [SetUp]
        public void SetUp()
        {
            _profile = "creator-reservation-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            _otherProfile = _profile + "-other";
            _firstObject = new GameObject("Creation_First");
            _firstObject.AddComponent<NetworkObject>();
            _first = _firstObject.AddComponent<NetworkInventory>();
            _secondObject = new GameObject("Creation_Second");
            _secondObject.AddComponent<NetworkObject>();
            _second = _secondObject.AddComponent<NetworkInventory>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_first != null) _first.ReleasePendingCreation();
            if (_second != null) _second.ReleasePendingCreation();
            if (_firstObject != null) UnityEngine.Object.DestroyImmediate(_firstObject);
            if (_secondObject != null) UnityEngine.Object.DestroyImmediate(_secondObject);
        }

        [Test]
        public void Neue_Verbindung_uebernimmt_die_offene_Erstellung_exklusiv()
        {
            Assert.IsTrue(_first.TryBeginCreation(_profile, "class.warden"));
            Assert.IsTrue(_second.TryBeginCreation(_profile, "class.ashstalker"));
            Assert.AreSame(_second, NetworkInventory.Pending(_profile));
            Assert.IsFalse(_first.OwnsPendingCreation);
            Assert.IsFalse(_first.AwaitingCreation);
            Assert.IsNull(_first.PendingProfile);
            Assert.IsTrue(_second.OwnsPendingCreation);
            _first.ReleasePendingCreation();
            Assert.AreSame(_second, NetworkInventory.Pending(_profile));
        }

        [Test]
        public void Erneute_Anmeldung_wechselt_ein_offenes_Profil_nicht()
        {
            Assert.IsTrue(_first.TryBeginCreation(_profile, "class.warden"));
            Assert.IsFalse(_first.TryBeginCreation(_profile, "class.ashstalker"));
            Assert.IsFalse(_first.TryBeginCreation(_otherProfile, "class.ashstalker"));
            Assert.AreEqual(_profile, _first.PendingProfile);
            Assert.AreSame(_first, NetworkInventory.Pending(_profile));
            Assert.IsNull(NetworkInventory.Pending(_otherProfile));
        }

        [Test]
        public void Verschiedene_Profile_koennen_parallel_erstellt_werden()
        {
            Assert.IsTrue(_first.TryBeginCreation(_profile, "class.warden"));
            Assert.IsTrue(_second.TryBeginCreation(_otherProfile, "class.ashstalker"));
            Assert.IsTrue(_first.OwnsPendingCreation);
            Assert.IsTrue(_second.OwnsPendingCreation);
        }

        [Test]
        public void Abbruch_gibt_das_Profil_fuer_eine_neue_Verbindung_frei()
        {
            Assert.IsTrue(_first.TryBeginCreation(_profile, "class.warden"));
            _first.ReleasePendingCreation();
            Assert.IsFalse(_first.AwaitingCreation);
            Assert.IsFalse(_first.OwnsPendingCreation);
            Assert.IsNull(_first.PendingProfile);
            Assert.IsNull(NetworkInventory.Pending(_profile));
            Assert.IsTrue(_second.TryBeginCreation(_profile, "class.ashstalker"));
            _first.ReleasePendingCreation();
            Assert.AreSame(_second, NetworkInventory.Pending(_profile));
            Assert.IsTrue(_second.OwnsPendingCreation);
        }

        [Test]
        public void Serverstopp_raeumt_eine_offene_Erstellung_auf()
        {
            Assert.IsTrue(_first.TryBeginCreation(_profile, "class.warden"));
            _first.OnStopServer();
            Assert.IsNull(NetworkInventory.Pending(_profile));
            Assert.IsFalse(_first.AwaitingCreation);
            Assert.IsTrue(_second.TryBeginCreation(_profile, "class.warden"));
        }

        [Test]
        public void Nach_Abbruch_kann_die_Verbindung_ein_anderes_Profil_erstellen()
        {
            Assert.IsTrue(_first.TryBeginCreation(_profile, "class.warden"));
            _first.ReleasePendingCreation();
            Assert.IsNull(NetworkInventory.Pending(_profile));
            Assert.IsTrue(_first.TryBeginCreation(_otherProfile, "class.warden"));
            Assert.AreSame(_first, NetworkInventory.Pending(_otherProfile));
            Assert.IsTrue(_first.OwnsPendingCreation);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("../profile")]
        public void Ungueltige_Profile_werden_nicht_reserviert(string profile)
        {
            Assert.IsFalse(_first.TryBeginCreation(profile, "class.warden"));
            Assert.IsFalse(_first.AwaitingCreation);
            Assert.IsNull(_first.PendingProfile);
        }
    }
}
