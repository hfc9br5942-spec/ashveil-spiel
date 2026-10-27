using System.Collections.Generic;
using System.Reflection;
using Ashveil.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// 23.10.2026 (Kampfreaktionen, Paket C): reine Ableitung des Treffer-Ergebnisses (<see cref="TrefferReaktionen.Ableiten"/>) – jede
    /// Kombination, Vorrang, Netz-Rundweg (der reine Client kommt aus den übertragenen Feldern zum selben Ergebnis wie der Server),
    /// unbekannte Netzwerte, Darstellungs-Vorrang von Tod/Kontrolle; dazu der echte Weg durch Health.ApplyDamage (Ausweichen, Block,
    /// Aschenschild mit Testobjekten) und ein Gift-Tick aus DamageOverTime. Kein Parier-Ergebnis (Kanon 03.10.).
    /// Die Netzverteilung selbst prüft die Suite „spieltest mehrspieler4“ (KAMPF-REAKTION) mit echten Clients.
    /// </summary>
    public sealed class TrefferReaktionTests
    {
        private GameObject _g;

        [TearDown]
        public void TearDown()
        {
            if (_g != null) Object.DestroyImmediate(_g);
        }

        // ================================================================== reine Ableitung

        [Test]
        public void Ableiten_JedesErgebnis()
        {
            Assert.AreEqual(TrefferReaktion.Schaden, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 12f }));
            Assert.AreEqual(TrefferReaktion.Ausgewichen, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 0f, Dodged = true }));
            Assert.AreEqual(TrefferReaktion.BlockVoll, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 0f, Blocked = 20f }));
            Assert.AreEqual(TrefferReaktion.BlockTeil, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 8f, Blocked = 12f }));
            Assert.AreEqual(TrefferReaktion.AbsorbVoll, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 0f, Absorbed = 30f }));
            Assert.AreEqual(TrefferReaktion.AbsorbTeil, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 5f, Absorbed = 25f }));
            Assert.AreEqual(TrefferReaktion.Tick, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 4f, Periodisch = true }), "Gift/Brennen/Blutung");
            Assert.AreEqual(TrefferReaktion.Tick, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 35f, Environmental = true }), "Ertrinken");
            Assert.AreEqual(TrefferReaktion.Keine, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 0f }), "nichts angekommen, nichts gefangen");
            Assert.AreEqual(TrefferReaktion.Schaden, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 50f, IsCritical = true, Knockback = 2f }),
                "Wucht/Krit ändern das Ergebnis nicht – leicht/schwer wählt die Darstellung");
        }

        [Test]
        public void Ableiten_Vorrang()
        {
            // Ausweichen vor allem (ein ausgewichener Treffer trägt nie Block/Absorption, aber auch eine fehlerhafte Kombination bleibt eindeutig)
            Assert.AreEqual(TrefferReaktion.Ausgewichen, TrefferReaktionen.Ableiten(new DamageInfo { Dodged = true, Blocked = 5f, Absorbed = 5f, Periodisch = true }));
            // Tick vor Block/Absorption: periodische Ticks starten keine Schild- oder Schlagreaktion neu (absorbierte Menge bleibt im Feld)
            Assert.AreEqual(TrefferReaktion.Tick, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 0f, Absorbed = 6f, Periodisch = true }));
            // Block vor Absorption: Schild nimmt einen Teil, Aschenschild den Rest → kein Schaden → voller Block
            Assert.AreEqual(TrefferReaktion.BlockVoll, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 0f, Blocked = 10f, Absorbed = 7f }));
            Assert.AreEqual(TrefferReaktion.BlockTeil, TrefferReaktionen.Ableiten(new DamageInfo { Amount = 3f, Blocked = 10f, Absorbed = 7f }));
        }

        [Test]
        public void Netz_ClientLeitetDasselbeAbWieDerServer()
        {
            var server = new List<DamageInfo>
            {
                new DamageInfo { Amount = 12f },
                new DamageInfo { Dodged = true },
                new DamageInfo { Blocked = 20f },
                new DamageInfo { Amount = 8f, Blocked = 12f },
                new DamageInfo { Absorbed = 30f },
                new DamageInfo { Amount = 5f, Absorbed = 25f },
                new DamageInfo { Amount = 4f, Periodisch = true, WeaponId = "spell.x.dot" },
                new DamageInfo { Amount = 0f, Absorbed = 4f, Periodisch = true },
                new DamageInfo { Amount = 35f, Environmental = true, WeaponId = "umwelt.ertrinken" },
                new DamageInfo { Amount = 0f },
                new DamageInfo { Amount = 0f, Blocked = 10f, Absorbed = 7f },
            };
            foreach (DamageInfo s in server)
            {
                TrefferReaktion r = TrefferReaktionen.Ableiten(s);
                // so baut NetworkHealth.ObserversDamaged den Treffer nach: Menge, Block, Ausweichen einzeln; WeaponId/Periodisch/Umwelt nicht
                var c = new DamageInfo { Amount = s.Amount, Blocked = s.Blocked, Dodged = s.Dodged, IsCritical = s.IsCritical };
                TrefferReaktionen.UebernimmVomServer(ref c, (byte)r, s.Absorbed);
                Assert.AreEqual(r, c.Reaktion, "übernommen");
                Assert.AreEqual(r, TrefferReaktionen.Ableiten(c), $"Client leitet dasselbe ab (Server {r}, Menge {s.Amount}, Block {s.Blocked}, Absorb {s.Absorbed})");
                Assert.AreEqual(s.Absorbed, c.Absorbed, 1e-4f, "absorbierte Menge kommt an");
            }
        }

        [Test]
        public void Netz_WerteStabilUndUnbekanntWirdKeine()
        {
            // Netzwerte dürfen sich nie verschieben (Byte im Treffer-RPC)
            Assert.AreEqual(0, (int)TrefferReaktion.Keine);
            Assert.AreEqual(1, (int)TrefferReaktion.Schaden);
            Assert.AreEqual(2, (int)TrefferReaktion.Verfehlt);
            Assert.AreEqual(3, (int)TrefferReaktion.Ausgewichen);
            Assert.AreEqual(4, (int)TrefferReaktion.BlockVoll);
            Assert.AreEqual(5, (int)TrefferReaktion.BlockTeil);
            Assert.AreEqual(6, (int)TrefferReaktion.AbsorbVoll);
            Assert.AreEqual(7, (int)TrefferReaktion.AbsorbTeil);
            Assert.AreEqual(8, (int)TrefferReaktion.Tick);
            Assert.AreEqual(9, TrefferReaktionen.Anzahl);
            for (int i = 0; i < TrefferReaktionen.Anzahl; i++) Assert.AreEqual((TrefferReaktion)i, TrefferReaktionen.AusNetz((byte)i));
            Assert.AreEqual(TrefferReaktion.Keine, TrefferReaktionen.AusNetz(9));
            Assert.AreEqual(TrefferReaktion.Keine, TrefferReaktionen.AusNetz(255));
            var d = new DamageInfo();
            TrefferReaktionen.UebernimmVomServer(ref d, 200, -3f);
            Assert.AreEqual(TrefferReaktion.Keine, d.Reaktion);
            Assert.AreEqual(0f, d.Absorbed, "negative Menge wird 0");
            Assert.IsFalse(d.Periodisch);
            Assert.IsFalse(System.Enum.IsDefined(typeof(TrefferReaktion), "Pariert"), "kein Parier-Ergebnis (Kanon 03.10.)");
        }

        [Test]
        public void Darstellbar_TodUndKontrolleHabenVorrang()
        {
            for (int i = 0; i < TrefferReaktionen.Anzahl; i++)
            {
                var r = (TrefferReaktion)i;
                Assert.IsFalse(TrefferReaktionen.Darstellbar(r, true, false), $"{r}: Tod hat Vorrang");
                Assert.IsFalse(TrefferReaktionen.Darstellbar(r, false, true), $"{r}: Kontrolle hat Vorrang");
                bool soll = r != TrefferReaktion.Keine && r != TrefferReaktion.Verfehlt;
                Assert.AreEqual(soll, TrefferReaktionen.Darstellbar(r, false, false), $"{r} lebend ohne Kontrolle");
            }
        }

        // ================================================================== echter Weg durch Health.ApplyDamage

        private sealed class Ausweicher : IDodger
        {
            public bool TryDodge(in DamageInfo info) => true;
        }

        private sealed class Blocker : IDamageBlocker
        {
            public float Anteil;
            public float Block(ref DamageInfo info, float amount)
            {
                float b = Anteil >= 1f ? amount : Mathf.Floor(amount * Anteil);
                info.Blocked = b;
                return amount - b;
            }
        }

        private sealed class Absorber : IDamageAbsorber
        {
            public float Anteil;
            public float IncomingMultiplier => 1f;
            public float Absorb(ref DamageInfo info, float amount)
            {
                float a = Anteil >= 1f ? amount : Mathf.Floor(amount * Anteil);
                info.Absorbed += a;
                return amount - a;
            }
        }

        private static void Setze(Health h, string feld, object wert)
        {
            FieldInfo f = typeof(Health).GetField(feld, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "Health." + feld);
            f.SetValue(h, wert);
        }

        private Health Ziel(List<DamageInfo> mitschrift)
        {
            _g = new GameObject("Trefferziel (Test)");
            Health h = _g.AddComponent<Health>();
            h.Configure(1000f, false);
            Setze(h, "_dodger", null);
            Setze(h, "_blocker", null);
            Setze(h, "_mitigation", null);
            h.SetAbsorber(null);
            h.Damaged += info => mitschrift.Add(info);
            return h;
        }

        [Test]
        public void Health_SetztDasErgebnisAmEndgueltigenTreffer()
        {
            var m = new List<DamageInfo>();
            Health h = Ziel(m);

            h.ApplyDamage(new DamageInfo { Amount = 30f, Unblockable = true });
            Assert.AreEqual(TrefferReaktion.Schaden, m[m.Count - 1].Reaktion);

            Setze(h, "_dodger", new Ausweicher());
            h.ApplyDamage(new DamageInfo { Amount = 30f, Dodgeable = true });
            Assert.AreEqual(TrefferReaktion.Ausgewichen, m[m.Count - 1].Reaktion);
            Setze(h, "_dodger", null);

            Setze(h, "_blocker", new Blocker { Anteil = 1f });
            h.ApplyDamage(new DamageInfo { Amount = 40f });
            Assert.AreEqual(TrefferReaktion.BlockVoll, m[m.Count - 1].Reaktion);
            Setze(h, "_blocker", new Blocker { Anteil = 0.5f });
            h.ApplyDamage(new DamageInfo { Amount = 40f });
            Assert.AreEqual(TrefferReaktion.BlockTeil, m[m.Count - 1].Reaktion);
            Assert.AreEqual(20f, m[m.Count - 1].Amount, 1e-4f);
            Setze(h, "_blocker", null);

            h.SetAbsorber(new Absorber { Anteil = 1f });
            h.ApplyDamage(new DamageInfo { Amount = 40f, Unblockable = true });
            Assert.AreEqual(TrefferReaktion.AbsorbVoll, m[m.Count - 1].Reaktion);
            Assert.AreEqual(40f, m[m.Count - 1].Absorbed, 1e-4f);
            h.SetAbsorber(new Absorber { Anteil = 0.5f });
            h.ApplyDamage(new DamageInfo { Amount = 40f, Unblockable = true });
            Assert.AreEqual(TrefferReaktion.AbsorbTeil, m[m.Count - 1].Reaktion);
            h.SetAbsorber(null);

            Assert.AreEqual(6, m.Count, "je Treffer genau ein Damaged-Ereignis");
            // Ein tödlicher Treffer bleibt „Schaden“ – den Tod sieht die Darstellung am Leben (vor dem Ereignis gesetzt)
            float lebenImEreignis = -1f;
            h.Damaged += _ => lebenImEreignis = h.Current;
            h.ApplyDamage(new DamageInfo { Amount = 5000f, Unblockable = true });
            Assert.AreEqual(TrefferReaktion.Schaden, m[m.Count - 1].Reaktion);
            Assert.AreEqual(0f, lebenImEreignis, "Leben ist im Ereignis schon 0 → Darstellung gibt dem Tod Vorrang");
        }

        [Test]
        public void GiftTick_IstPeriodischUndTick()
        {
            var m = new List<DamageInfo>();
            Health h = Ziel(m);
            DamageOverTime dot = DamageOverTime.Apply(h, null, 6f, 5f, "enemy.test.poison");   // „.poison“: vorher an der Id nicht als Tick erkennbar
            Assert.IsNotNull(dot);
            FieldInfo tick = typeof(DamageOverTime).GetField("_tick", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo update = typeof(DamageOverTime).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(tick, "DamageOverTime._tick");
            Assert.IsNotNull(update, "DamageOverTime.Update");
            tick.SetValue(dot, 0f);
            update.Invoke(dot, null);
            Assert.AreEqual(1, m.Count, "ein Tick");
            Assert.IsTrue(m[0].Periodisch, "Kennung gesetzt");
            Assert.AreEqual(TrefferReaktion.Tick, m[0].Reaktion);
            Assert.AreEqual("enemy.test.poison", m[0].WeaponId);
        }
    }
}
