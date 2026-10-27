using System.Collections;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest M6.6 „24-Slot-Actionbar“ mit zwei echten Spielern: Maxmod (Editor-Host) und Krisbot (Test-Client).
    /// Geprueft: Standardbelegung, Belegen/Tauschen/Verschieben/Leeren, Missbrauch wird abgelehnt, Ausloesen ueber die
    /// Tasten (Heiltrank, Hechtrolle, Waffe), Abklingzeit vom Server, Bank B (Halten und Umschalten), Tasten neu
    /// belegen ohne Doppelbelegung, Speichern der Tasten (Datei) und der Belegung (Charakter), alter Speicherstand.
    /// Start: "Ashveil &gt; Test &gt; Actionbar (2 Spieler)". Bericht: Logs/Ashveil/Ashveil_ActionbarTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string BarFlag = "actionbar";
        private const string BarA = "bar-a", BarB = "bar-b";
        private const string Potion = "item.healing_draught";
        private static bool _barMode;

        private static string Slots(NetworkInventory who)
        {
            var bar = who.GetComponent<NetworkActionBar>();
            var sb = new System.Text.StringBuilder();
            foreach (ActionBarEntry e in bar.ServerSlots)
                if (!e.IsEmpty) sb.Append(e.page == 0 ? 'A' : 'B').Append(e.slot + 1).Append('=').Append(e.referenceId).Append(',');
            return sb.ToString();
        }

        private static string At(NetworkInventory who, int page, int slot) =>
            who.GetComponent<NetworkActionBar>().ServerSlots[page * ActionBarLayout.SlotsPerPage + slot].referenceId ?? "";

        private IEnumerator ActionBarSuite()
        {
            // ---------------------------------------------------------- Anmeldung
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(BarA)) != null, 30f);
            Check(a != null, "Maxmod (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, BarB);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(BarB)) != null, 120f);
            Check(b != null, "Krisbot (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);
            var barB = b.GetComponent<NetworkActionBar>();

            // ---------------------------------------------------------- Grundstand
            Line("");
            Line("[Standardbelegung und Oberflaeche]");
            // P1: Die Hechtrolle nur fuer Klassen, die rollen; auf den freien Platz legt der Server die erste Klassenfaehigkeit
            bool rolls = b.ServerModel.Class != null && b.ServerModel.Class.canRoll;
            string a4 = rolls ? ActionBarLayout.Dodge : "spell.warden_heroic_strike";
            string expect = "A1=ability.auto_attack,A2=ability.weapon_special,A3=item.healing_draught,A4=" + a4 + ",B1=ability.swap_weapon,B2=ability.reload,";
            Check(barB != null && Slots(b) == expect, $"Neuer Charakter hat die Standardbelegung (Angriff, Waffenfaehigkeit, Heiltrank, {(rolls ? "Hechtrolle" : "Heldenhafter Schlag")}; Bank B: Wechseln, Nachladen) – {Slots(b)}");
            yield return Ask(b, "bar");
            Check(_last.ok && _last.data.StartsWith("bar=" + expect), $"Krisbot sieht dieselbe Belegung ({_last.data})");
            int abilities = GameData.Database.All.Count(d => d is AbilityDefinition);
            Check(abilities == 5, $"Fuenf Faehigkeiten fuer Leiste und Faehigkeitenbuch ({abilities})");
            yield return Ask(b, "bar-ui");
            Check(_last.ok && _last.data.Contains("slots=12") && _last.data.Contains("k0=1") && _last.data.Contains("k9=0") && _last.data.Contains("shown=0") && _last.data.Contains("book=" + (6 + b.ServerData.spells.Count)),
                $"Leiste mit 12 Plaetzen, Tasten 1 … 0, Bank A, Faehigkeitenbuch: 5 Faehigkeiten + Reittier-Zeile + {b.ServerData.spells.Count} Klassenfaehigkeit(en) ({_last.data})");
            Check(!_last.data.Contains("k10=;"), "Platz 11 zeigt seine Taste (deutsche Tastatur: ß)");
            Check(!_last.data.ToUpperInvariant().Contains("AKUT"), "Platz 12 zeigt ein Zeichen statt eines Tastennamens");

            // ---------------------------------------------------------- Belegen, Tauschen, Leeren
            Line("");
            Line("[Belegen, Tauschen, Verschieben, Leeren]");
            yield return Ask(b, "bar-assign", "1 4 Consumable " + Potion);
            Check(_last.ok && At(b, 1, 4) == Potion, "Heiltrank aus dem Rucksack auf B5 gelegt");
            Check(At(b, 0, 2) == Potion, "Derselbe Trank darf auf mehreren Plaetzen liegen (A3 und B5)");
            yield return Ask(b, "bar-swap", "0 0 0 1");
            Check(_last.ok && At(b, 0, 0) == ActionBarLayout.WeaponSpecial && At(b, 0, 1) == ActionBarLayout.AutoAttack, "A1 und A2 getauscht");
            yield return Ask(b, "bar-swap", "0 0 0 1");
            yield return Ask(b, "bar-swap", "0 3 0 7");
            Check(_last.ok && At(b, 0, 3) == "" && At(b, 0, 7) == a4, "A4 auf den leeren Platz A8 verschoben");
            yield return Ask(b, "bar-swap", "0 7 0 3");
            yield return Ask(b, "bar-clear", "1 1");
            Check(_last.ok && At(b, 1, 1) == "", "B2 geleert (nur die Verknuepfung)");
            yield return Ask(b, "bar-assign", "1 1 Ability " + ActionBarLayout.Reload);
            Check(_last.ok && At(b, 1, 1) == ActionBarLayout.Reload, "Nachladen wieder aus dem Faehigkeitenbuch auf B2");
            yield return Ask(b, "bar-assign", "0 11 Consumable item.dry_bread");
            Check(_last.ok && At(b, 0, 11) == "item.dry_bread", "Brot auf A12");
            yield return Ask(b, "bar-assign", "0 11 Ability " + ActionBarLayout.WeaponSpecial);
            Check(_last.ok && At(b, 0, 11) == ActionBarLayout.WeaponSpecial, "Platz ueberschreiben (Brot -> Waffenfaehigkeit)");
            yield return Ask(b, "bar-clear", "0 11");

            // ---------------------------------------------------------- Missbrauch
            Line("");
            Line("[Server prueft jede Aenderung]");
            string before = Slots(b);
            yield return Ask(b, "bar-assign", "0 8 Consumable item.bellringer");
            Check(!_last.ok && Slots(b) == before, $"Fremde Waffe (nicht im Rucksack, nicht benutzbar) abgelehnt: {Tail(_last.data)}");
            yield return Ask(b, "bar-assign", "0 8 Consumable item.strong_healing_draught");
            Check(!_last.ok && _last.data.Contains("nicht") && Slots(b) == before, $"Trank, den Krisbot nicht besitzt, abgelehnt: {Tail(_last.data)}");
            yield return Ask(b, "bar-assign", "5 3 Ability " + ActionBarLayout.Dodge);
            Check(!_last.ok && Slots(b) == before, $"Ungueltige Bank abgelehnt: {Tail(_last.data)}");
            yield return Ask(b, "bar-assign", "0 30 Ability " + ActionBarLayout.Dodge);
            Check(!_last.ok && Slots(b) == before, "Ungueltiger Platz abgelehnt");
            yield return Ask(b, "bar-assign", "0 8 Ability ability.gibt_es_nicht");
            Check(!_last.ok && Slots(b) == before, $"Unbekannte Faehigkeit abgelehnt: {Tail(_last.data)}");
            yield return Ask(b, "bar-assign", "0 8 Spell spell.feuerball");
            Check(!_last.ok && Slots(b) == before, $"Zauber gibt es noch nicht – abgelehnt: {Tail(_last.data)}");

            // ---------------------------------------------------------- Ausloesen
            Line("");
            Line("[Ausloesen ueber die Tasten]");
            var hb = b.GetComponent<Health>();
            hb.ApplyDamage(new DamageInfo { Amount = Mathf.Round(hb.Max * 0.5f) });
            yield return new WaitForSecondsRealtime(0.5f);
            float hp0 = hb.Current;
            int potions0 = CountOfBag(b, Potion);
            yield return Ask(b, "slot-press", "2");
            yield return new WaitForSecondsRealtime(1.5f);
            Check(_last.ok && _last.data.StartsWith("id=" + Potion) && hb.Current > hp0 && CountOfBag(b, Potion) == potions0 - 1,
                $"Taste 3 trinkt den Heiltrank (Leben {hp0:0} -> {hb.Current:0}, Traenke {potions0} -> {CountOfBag(b, Potion)})");
            Check(b.ServerCooldownRemaining(Potion) > 5f, $"Server sperrt den Trank ({b.ServerCooldownRemaining(Potion):0.0} s)");
            yield return Ask(b, "bar-cd", Potion);
            float cd = ParseAfter(_last.data, "cd=");
            Check(cd > 5f, $"Krisbot sieht die Abklingzeit auf der Leiste ({_last.data})");
            int potions1 = CountOfBag(b, Potion);
            yield return Ask(b, "slot-press", "2");
            Check(CountOfBag(b, Potion) == potions1, "Nochmal druecken waehrend der Abklingzeit: der Server lehnt ab, kein Trank verbraucht");

            if (!rolls) b.GetComponent<NetworkProgression>().ServerSetResource(50f); // Heldenhafter Schlag braucht Wut
            yield return Ask(b, "slot-press", "3");
            // Kampfumbau (Max 30.09.): die Ausweichrolle hat nur der Aschenjaeger; beim Eidwaechter liegt hier der Heldenhafte Schlag
            if (rolls)
                Check(_last.ok && _last.data.StartsWith("id=" + ActionBarLayout.Dodge) && ParseAfter(_last.data, "dodgecd=") > 0.5f,
                    $"Taste 4 loest die Hechtrolle aus ({_last.data})");
            else
            {
                yield return new WaitForSecondsRealtime(0.5f);
                var bprog = b.GetComponent<NetworkProgression>();
                Check(_last.data.StartsWith("id=spell.warden_heroic_strike") && bprog.ServerQueuedNextSwing == "spell.warden_heroic_strike",
                    $"Taste 4 legt den Heldenhaften Schlag auf den nächsten Schlag ({_last.data})");
                yield return Ask(b, "slot-press", "3"); // wieder aufheben
            }
            yield return Ask(b, "slot-press", "0");
            Check(_last.ok && _last.data.StartsWith("id=" + ActionBarLayout.AutoAttack), "Taste 1 loest den Angriff aus");
            yield return Ask(b, "stop", "");
            yield return AmmoSection(b);

            // ---------------------------------------------------------- Bank B
            Line("");
            Line("[Bank B: Halten und Umschalten]");
            yield return Ask(b, "bank-hold", "1");
            Check(_last.data == "page=1", "Wechseltaste gehalten: Bank B aktiv");
            yield return Ask(b, "bar-ui");
            Check(_last.data.Contains("shown=1"), "Die Leiste zeigt Bank B");
            yield return Ask(b, "slot-press", "4");
            Check(_last.ok && _last.data.StartsWith("id=" + Potion) && _last.data.Contains("page=1"), "Dieselbe Taste 5 loest jetzt B5 aus (Heiltrank)");
            yield return Ask(b, "bank-hold", "0");
            Check(_last.data == "page=0", "Losgelassen: wieder Bank A");
            yield return Ask(b, "slot-press", "4");
            Check(!_last.ok, "Taste 5 auf Bank A ist leer – nichts passiert");
            yield return Ask(b, "bindmode", "toggle");
            yield return Ask(b, "bank-toggle");
            Check(_last.data == "page=1", "Umschalt-Modus: einmal druecken = Bank B");
            yield return Ask(b, "bank-toggle");
            Check(_last.data == "page=0", "Nochmal druecken = Bank A");
            yield return Ask(b, "bindmode", "hold");

            // ---------------------------------------------------------- Tasten neu belegen
            Line("");
            Line("[Tasten frei belegen]");
            yield return Ask(b, "bind", "0 Key:Q");
            Check(_last.data.Contains("slots=Key:Q,Key:Digit2"), "Platz 1 auf Q gelegt");
            yield return Ask(b, "bar-ui");
            Check(_last.data.Contains("k0=Q"), $"Die Leiste zeigt „Q“ ({_last.data})");
            yield return Ask(b, "bind", "1 Key:Q");
            Check(_last.data.Contains("slots=,Key:Q,") && _last.data.Contains("before=-1"), "Q auf Platz 2: Platz 1 wird frei (keine Doppelbelegung)");
            yield return Ask(b, "bind-swap", "Mouse:Middle");
            Check(_last.data.Contains("swap=Mouse:Middle"), "Wechseltaste auf die mittlere Maustaste gelegt");
            yield return Ask(b, "bind-defaults");
            Check(_last.data.StartsWith("slots=Key:Digit1,Key:Digit2") && _last.data.Contains("swap=Key:LeftCtrl;toggle=0"), "Standard wiederhergestellt (1 … ´, Strg halten)");
            yield return Ask(b, "bind-roundtrip");
            Check(_last.ok, "Tasten, Wechseltaste und Modus werden gespeichert und wieder geladen (Einstellungsdatei)");

            // ---------------------------------------------------------- Speichern und Laden
            Line("");
            Line("[Speichern und Laden]");
            string saved = Slots(b);
            b.SaveNow();
            Check(b.ServerData.version == CharacterSaveData.CurrentVersion && b.ServerData.actionBar.Any(e => e.page == 1 && e.slot == 4 && e.referenceId == Potion),
                $"Belegung im Speicherstand (Version {b.ServerData.version})");
            yield return Ask(b, "reconnect");
            yield return WaitUntil(() => OnlineInv(BarB) == null || OnlineInv(BarB) != b, 10f);
            NetworkInventory b2 = null;
            yield return WaitUntil(() => (b2 = OnlineInv(BarB)) != null && b2 != b && b2.IsReady, 40f);
            Check(b2 != null, "Krisbot meldet sich neu an");
            if (b2 != null)
            {
                yield return new WaitForSecondsRealtime(1.5f);
                Check(Slots(b2) == saved, "Nach dem Neuanmelden: dieselbe Belegung auf dem Server");
                yield return Ask(b2, "bar");
                Check(_last.ok && _last.data.StartsWith("bar=" + saved), "… und bei Krisbot");
            }

            var old = new CharacterSaveData { version = 2, profileId = "bar-alt", name = "Alt", actionBar = null };
            CharacterStore.MigrateForTests(old);
            Check(old.version == CharacterSaveData.CurrentVersion && old.actionBar != null && old.actionBar.Count == ActionBarLayout.Defaults().Count,
                "Alter Speicherstand (Version 2) bekommt die Standardbelegung");
            var broken = new CharacterSaveData
            {
                version = 3, profileId = "bar-kaputt", name = "Kaputt",
                actionBar = new System.Collections.Generic.List<ActionBarEntry>
                {
                    new ActionBarEntry(ActionEntryType.Ability, ActionBarLayout.Dodge, 0, 0),
                    new ActionBarEntry(ActionEntryType.Consumable, Potion, 0, 0),
                    new ActionBarEntry(ActionEntryType.Ability, ActionBarLayout.Dodge, 7, 3),
                    new ActionBarEntry(ActionEntryType.Spell, "spell.x", 0, 5)
                }
            };
            CharacterStore.MigrateForTests(broken);
            Check(broken.actionBar.Count == 1, "Manipulierter Stand: doppelte Plaetze, falsche Bank und unbekannte Typen werden entfernt");
        }

        /// <summary>Munition zaehlt der Server: Schuss nimmt eine Kugel, leeres Magazin wird abgelehnt und der
        /// Client korrigiert, Nachladen fuellt auf – ausgeloest ueber die Leiste (B1 Wechseln, A1 Angriff, B2 Nachladen).</summary>
        private IEnumerator AmmoSection(NetworkInventory b)
        {
            Line("");
            Line("[Munition zaehlt der Server]");
            var combat = b.GetComponent<NetworkCombat>();
            var wu = b.GetComponent<Ashveil.Gameplay.Weapons.WeaponUser>();
            WeaponDefinition gun = wu != null ? wu.RangedWeapon : null;
            // Feuerwaffen ruhen: Magazine gibt es nur noch, wenn jemand eine alte Schusswaffe traegt (Pfeile prueft die Kampf-Pruefung)
            if (combat == null || gun == null || !gun.UsesAmmo) { Info("Übersprungen: keine Schusswaffe mit Magazin (Feuerwaffen ruhen)"); yield break; }
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Ask(b, "bank-hold", "1");
            yield return Ask(b, "slot-press", "0"); // B1 = Waffe wechseln
            yield return Ask(b, "bank-hold", "0");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Ask(b, "ammo");
            Check(_last.data.Contains("ranged=1"), $"B1 wechselt auf die Schusswaffe ({_last.data})");
            int full = gun.magazineSize;
            Check(combat.ServerAmmo(gun) == full && ParseAfter(_last.data, "ammo=") == full, $"Volles Magazin auf Server und Client ({full})");

            int rej0 = combat.ServerRejected;
            yield return Ask(b, "slot-press", "0"); // A1 = Angriff (ohne Ziel: ein Schuss)
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "ammo");
            Check(combat.ServerAmmo(gun) == full - 1 && ParseAfter(_last.data, "ammo=") == full - 1,
                $"Ein Schuss: Server zaehlt {combat.ServerAmmo(gun)}, Krisbot zeigt {ParseAfter(_last.data, "ammo="):0}");
            yield return Ask(b, "stop", "");

            // Server glaubt: leer (z. B. Client hat geschummelt oder verpasst) – der naechste Schuss muss scheitern
            if (full - 1 > 0)
            {
                combat.ServerSetAmmoForTest(gun, 0);
                rej0 = combat.ServerRejected;
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Ask(b, "slot-press", "0");
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Ask(b, "ammo");
                Check(combat.ServerRejected > rej0 && combat.ServerAmmo(gun) == 0, $"Schuss mit leerem Magazin (laut Server) abgelehnt");
                Check(ParseAfter(_last.data, "ammo=") == 0, $"Krisbot uebernimmt den Stand des Servers ({_last.data})");
                yield return Ask(b, "stop", "");
            }
            else Check(combat.ServerAmmo(gun) == 0, "Magazin nach dem Schuss leer");

            yield return Ask(b, "bank-hold", "1");
            yield return Ask(b, "slot-press", "1"); // B2 = Nachladen
            yield return Ask(b, "bank-hold", "0");
            yield return new WaitForSecondsRealtime(0.2f);
            rej0 = combat.ServerRejected;
            yield return Ask(b, "ammo");
            Check(_last.data.Contains("reloading=1") || ParseAfter(_last.data, "ammo=") == full, $"B2 laedt nach ({_last.data})");
            yield return new WaitForSecondsRealtime(gun.reloadTime + 0.8f);
            yield return Ask(b, "ammo");
            Check(combat.ServerAmmo(gun) == full && ParseAfter(_last.data, "ammo=") == full, $"Nach dem Nachladen voll: Server {combat.ServerAmmo(gun)}, Krisbot {ParseAfter(_last.data, "ammo="):0}");

            yield return Ask(b, "bank-hold", "1");
            yield return Ask(b, "slot-press", "0"); // zurueck zur Nahkampfwaffe
            yield return Ask(b, "bank-hold", "0");
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Ask(b, "ammo");
            Check(_last.data.Contains("ranged=0"), "Zurueck zur Nahkampfwaffe");
        }

        private static string Tail(string data)
        {
            int i = data.IndexOf("|| ", System.StringComparison.Ordinal);
            return i >= 0 ? data.Substring(i + 3) : data;
        }

        private static float ParseAfter(string data, string key)
        {
            int i = data.IndexOf(key, System.StringComparison.Ordinal);
            if (i < 0) return -1f;
            int j = data.IndexOf(';', i);
            string v = j < 0 ? data.Substring(i + key.Length) : data.Substring(i + key.Length, j - i - key.Length);
            return float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : -1f;
        }

        private static int CountOfBag(NetworkInventory inv, string id)
        {
            int n = 0;
            foreach (ItemStack st in inv.ServerModel.Bag) if (!st.IsEmpty && st.id == id) n += st.count;
            return n;
        }
    }
}
