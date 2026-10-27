using System.Collections;
using System.IO;
using Ashveil.Data;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Visuals;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest M7 „Charakter-Editor“ mit zwei echten Spielern: Maxmod (Editor-Host, hat einen Charakter) und ein
    /// neues Profil ohne Charakter (Test-Client "creator-b"). Geprueft: Editor oeffnet sich nur ohne Charakter, Figur
    /// unsichtbar und untaetig, alle Kategorien mit Vorschau (Gesichtsebenen, Augen, Kopfform, Frisur-/Bart-Formen),
    /// nur passende Optionen je Koerper, Zufall, Namensregeln (Client und Server), Name vergeben/reserviert,
    /// manipulierte Werte, Speichern, Mitspieler sieht dasselbe Aussehen, Neuanmeldung ohne Editor, alte Staende.
    /// Bilder aller Gesichter/Merkmale: Logs/Ashveil/Charakter/. Bericht: Logs/Ashveil/Ashveil_CreatorTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string CreatorFlag = "creator";
        private const string CreatorA = "creator-a", CreatorB = "creator-b";
        private static bool _creatorMode;

        private static int IndexOf(System.Collections.Generic.List<AppearanceCatalog.StyleEntry> list, string name, BodyVariantMask body)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].name == name && (list[i].bodies & body) != 0) return i;
            return -1;
        }

        // Testziel ist die Textur fuer die UV des tatsaechlichen Koerpers, nicht der alte Quaternius-Dateiname.
        private static string ExpectedEyeTexture(AppearanceCatalog cat, int body, int eye)
        {
            if (body < 0 || body >= cat.bodies.Count || eye < 0 || eye >= cat.eyeColors.Count) return "";
            GameObject model = cat.bodies[body].model;
            if (model != null)
                foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.name.ToLowerInvariant().EndsWith("_augen"))
                    {
                        Texture2D mpfb = Resources.Load<Texture2D>("Augen/T_Augen_MPFB_" + eye);
                        return mpfb != null ? mpfb.name : "";
                    }
            return cat.eyeColors[eye].texture != null ? cat.eyeColors[eye].texture.name : "";
        }

        private IEnumerator CreatorSuite()
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            Check(cat != null && cat.faces.Count >= 5 && cat.eyeColors.Count >= 6 && cat.features.Count >= 5 && cat.skinTones.Count >= 6 && cat.overlayTemplate != null,
                $"Katalog: {cat?.faces.Count} Gesichter, {cat?.skinTones.Count} Hauttöne, {cat?.eyeColors.Count} Augenfarben, {cat?.features.Count} Merkmale, {cat?.hairstyles.Count} Frisuren, {cat?.beards.Count} Bärte");
            if (cat == null) yield break;

            // ---------------------------------------------------------- Anmeldung
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(CreatorA)) != null, 30f);
            Check(a != null && CharacterStore.TryCast(CreatorA, out _, out _), "Maxmod (Editor-Host, feste Besetzung) meldet sich ohne Editor an");
            if (a == null) yield break;
            yield return CaptureCatalog();

            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, CreatorB);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = NetworkInventory.Pending(CreatorB)) != null, 120f);
            Line("");
            Line("[Neues Profil ohne Charakter]");
            Check(b != null, "Neues Profil: der Server wartet auf den Charakter-Editor");
            if (b == null) yield break;

            // Zwei echte Verbindungen fuer dasselbe noch nicht erstellte Profil: die neueste Anmeldung gewinnt.
            NetworkInventory firstCreator = b;
            StartClient(exe, CreatorB);
            NetworkInventory nextCreator = null;
            yield return WaitUntil(() => (nextCreator = NetworkInventory.Pending(CreatorB)) != null && nextCreator != firstCreator, 120f);
            Check(nextCreator != null && nextCreator != firstCreator, "Neue Verbindung uebernimmt dasselbe offene Erstellungsprofil");
            if (nextCreator == null || nextCreator == firstCreator) yield break;
            Check(firstCreator == null || !firstCreator.AwaitingCreation, "Alte Verbindung kann denselben Charakter nicht mehr erstellen");
            b = nextCreator;
            yield return new WaitForSecondsRealtime(2f);
            Check(!b.IsReady && OnlineInv(CreatorB) == null, "Noch kein Charakter geladen – Rucksack, Kampf und Aufträge ruhen");
            var np = b.GetComponent<NetworkPlayer>();
            var look = b.GetComponent<NetworkAppearance>();
            Check(np != null && np.DisplayName == "", "Kein Namensschild, solange der Charakter nicht erstellt ist");
            Check(look != null && look.Hidden, "Maxmod sieht die noch nicht erstellte Figur nicht");

            yield return Ask(b, "creator");
            string st = _last.data;
            Check(_last.ok && st.Contains("open=1") && st.Contains("pending=1") && st.Contains("blocked=1"), "Beim neuen Spieler öffnet sich der Charakter-Editor im Vollbild");
            int hairM = 0, beardM = 0;
            for (int i = 0; i < cat.hairstyles.Count; i++) if (cat.StyleAllowed(cat.hairstyles, i, 0)) hairM++;
            for (int i = 0; i < cat.beards.Count; i++) if (cat.StyleAllowed(cat.beards, i, 0)) beardM++;
            Check(st.Contains($"Body={cat.bodies.Count}") && st.Contains($"Face={cat.faces.Count}") && st.Contains($"Skin={cat.skinTones.Count}") &&
                  st.Contains($"Hair={hairM}") && st.Contains($"Eyes={cat.eyeColors.Count}") && st.Contains($"Beard={beardM}") && st.Contains($"Feature={cat.features.Count}"),
                $"Alle Kategorien mit ihren Varianten (Mann: {hairM} Frisuren, {beardM} Bärte)");
            // UI-204 (Spieltest r2, Max 09.10.2026): leeres Feld, Würfel schlägt vor, Tippen ersetzt den Vorschlag
            Check(st.Contains("name=;"), $"Das Namensfeld beginnt leer ({Between(st, "name=", ";")})");
            // EINSTIEG-01 (21.10.): einmal über den echten Tastaturpuffer tippen (Keyboard.onTextInput → RawInput) – vorher
            // holte der Chat (SocialUi, früher im Bild) die Zeichen ab und das Feld blieb leer; creator-type geht daran vorbei.
            yield return Ask(b, "creator-keys", "Maxim");
            Check(_last.ok && _last.data.StartsWith("Maxim;", System.StringComparison.Ordinal),
                $"Tippen über die Tastatur kommt im Namensfeld an, nicht im Chat ({_last.data})");
            yield return Ask(b, "creator-dice", "3");
            string vorschlag = _last.data.Split(';')[0];
            Check(_last.ok && vorschlag.Length >= 3 && _last.data.Contains("marked=1"), $"Der Würfel schlägt einen Namen vor und markiert ihn ({vorschlag})");
            yield return Ask(b, "creator-type", "Talwen");
            Check(_last.ok && _last.data.StartsWith("Talwen;", System.StringComparison.Ordinal) && _last.data.Contains("marked=0"),
                $"Tippen ersetzt den Vorschlag ({_last.data})");
            yield return Ask(b, "creator-type", "x1 z");
            Check(_last.ok && _last.data.StartsWith("Talwenxz;", System.StringComparison.Ordinal), $"Nur Buchstaben kommen ins Feld ({_last.data})");

            yield return Ask(b, "pos");
            string pos0 = _last.data;
            yield return Ask(b, "walk", "1.2 1");
            ParsePos(pos0, out Vector3 p0, out _, out _);
            ParsePos(_last.data, out Vector3 p1, out _, out _);
            Check(Vector3.Distance(p0, p1) < 0.3f, $"Figur bewegt sich im Editor nicht ({Vector3.Distance(p0, p1):0.00} m)");

            // ---------------------------------------------------------- Auswahl mit Vorschau
            Line("");
            Line("[Auswahl und Vorschau]");
            yield return Ask(b, "creator-set", "1 1");
            Check(_last.ok && _last.data.Contains("T_Face_kantig_male") && _last.data.Contains("head=1.04"), $"Gesicht „Kantig“: Gesichtsebene + breitere Kopfform ({Between(_last.data, "overlay=", ";")}, tris {Between(_last.data, "tris=", ";")})");
            Check(ParseAfter(_last.data, "tris=") > 50f, "Gesichtsebene liegt auf den Gesichts-Dreiecken des Körpers");
            yield return Ask(b, "creator-set", "5 4");
            string expectedBlue = ExpectedEyeTexture(cat, 0, 4);
            Check(_last.ok && expectedBlue.Length > 0 && Between(_last.data, "eyes=", ";") == expectedBlue,
                $"Augenfarbe „Blau“ in der Vorschau (erwartet {expectedBlue})");
            yield return Ask(b, "creator-set", "7 5");
            Check(_last.ok && _last.data.Contains("T_Feature_glutzeichen_male"), "Gesichtsmerkmal „Glutzeichen“");
            yield return Ask(b, "creator-set", "6 " + IndexOf(cat.beards, "Stoppeln", BodyVariantMask.Male));
            Check(_last.ok && _last.data.Contains("T_Beard_stoppeln_male"), "Bart „Stoppeln“ als gemalte Ebene");
            yield return Ask(b, "creator-set", "6 " + IndexOf(cat.beards, "Kinn- und Schnauzbart", BodyVariantMask.Male));
            Check(_last.ok && ParseAfter(_last.data, "beardParts=") >= 2f, $"Kinn- und Schnauzbart als eigene Formen ({Between(_last.data, "beardParts=", ";")} Teile)");
            yield return Ask(b, "creator-set", "3 " + IndexOf(cat.hairstyles, "Irokese", BodyVariantMask.Male));
            Check(_last.ok && ParseAfter(_last.data, "hairParts=") >= 1f && _last.data.Contains("hair=1"), "Frisur „Irokese“ (Grundform in Haarfarbe)");
            yield return Ask(b, "creator-set", "2 6");
            Check(_last.ok && _last.data.Contains("|s6|"), "Hautton „Tiefbraun“");

            yield return Ask(b, "creator-set", "0 1");
            Check(_last.ok && _last.data.Contains("|b1|") && _last.data.Contains("|d0|") && _last.data.Contains("T_Face_kantig_female") && _last.data.Contains("Beard=1"),
                "Frau: Bart entfällt, Gesichtsebenen für den weiblichen Körper, nur passende Frisuren");
            yield return Ask(b, "creator-set", "6 " + IndexOf(cat.beards, "Vollbart", BodyVariantMask.Male));
            Check(!_last.ok, "Ein Bart lässt sich für die Frau nicht wählen");
            yield return Ask(b, "creator-set", "3 " + IndexOf(cat.hairstyles, "Gescheitelt", BodyVariantMask.Male));
            Check(!_last.ok, "Eine Männerfrisur lässt sich für die Frau nicht wählen");

            yield return Ask(b, "creator-random", "7");
            string r1 = Between(_last.data, "look=", ";");
            yield return Ask(b, "creator-random", "8");
            string r2 = Between(_last.data, "look=", ";");
            Check(_last.ok && r1 != r2 && Between(_last.data, "name=", ";") == "Talwenxz",
                $"„Zufällig“ würfelt das Aussehen, der selbst getippte Name bleibt ({Between(_last.data, "name=", ";")})");

            // ---------------------------------------------------------- Klassenwahl (Max 30.09.)
            Line("");
            Line("[Klassenwahl]");
            yield return Ask(b, "creator");
            Check(_last.data.Contains("Class=4") && _last.data.Contains("class=class.warden"),
                $"Drei Klassen plus ausgegrauter Magier-Platz, Vorgabe Eidwächter ({Between(_last.data, "class=", ";")})");
            yield return Ask(b, "creator-set", "10 -1");
            Check(!_last.ok && _last.data.Contains("class=class.warden"), "„Magier – kommt später“ lässt sich nicht wählen");
            yield return Ask(b, "creator-set", "10 1");
            Check(_last.ok && _last.data.Contains("class=class.ashstalker"), "Aschenjäger gewählt");
            yield return Ask(b, "creator-set", "10 7");
            Check(!_last.ok && _last.data.Contains("class=class.ashstalker"), "Ungültige Klassen-Nummer wird ignoriert");

            // ---------------------------------------------------------- Namen
            Line("");
            Line("[Namen – Client und Server prüfen]");
            yield return Ask(b, "creator-name", "ab");
            yield return Ask(b, "creator-create");
            Check(!_last.ok && _last.data.Contains("mindestens"), $"Zu kurz: schon der Client lehnt ab ({Tail(_last.data)})");
            string valid = "1|b1|s0|h" + IndexOf(cat.hairstyles, "Lang", BodyVariantMask.Female) + "|c1|d0|a0|e0|m0|f0|y|t";
            yield return Ask(b, "creator-raw", "Maxmod " + valid);
            Check(!_last.ok && _last.data.Contains("vergeben"), $"„Maxmod“ gibt es schon – Server lehnt ab ({Head(_last.data)})");
            yield return Ask(b, "creator-raw", "Admin " + valid);
            Check(!_last.ok && _last.data.Contains("reserviert"), $"Reservierter Name abgelehnt ({Head(_last.data)})");
            yield return Ask(b, "creator-raw", "Ar1wen " + valid);
            Check(!_last.ok && _last.data.Contains("Buchstaben"), $"Ziffern im Namen abgelehnt ({Head(_last.data)})");
            yield return Ask(b, "creator-raw", "Zauberin " + valid + " class.mage");
            Check(!_last.ok && _last.data.Contains("Klasse") && NetworkInventory.Pending(CreatorB) == b, $"Manipulierter Client: Magier/erfundene Klasse lehnt der Server ab ({Head(_last.data)})");
            yield return Ask(b, "creator-raw", "Aaarwen " + valid);
            Check(!_last.ok && NetworkInventory.Pending(CreatorB) == b, "Dreifache Buchstaben abgelehnt – der Spieler bleibt im Editor");

            var bad = CharacterAppearanceData.Decode("1|b9|s99|h99|c99|d99|a99|e99|m99|f3|y2:200.200.200|t4=item.bellringer");
            cat.Sanitize(bad);
            Check(bad.body < cat.bodies.Count && bad.skin < cat.skinTones.Count && bad.face < cat.faces.Count && bad.eyeColor < cat.eyeColors.Count &&
                  bad.feature == 0 && bad.dyes[2].IsDefault, "Manipulierte Werte (Körper 9, Gesicht 99 …) werden auf gültige Optionen begrenzt");

            // ---------------------------------------------------------- Erstellen
            Line("");
            Line("[Charakter erstellen]");
            int pony = IndexOf(cat.hairstyles, "Pferdeschwanz", BodyVariantMask.Female);
            yield return Ask(b, "creator-set", "0 1");
            yield return Ask(b, "creator-set", "1 3");
            yield return Ask(b, "creator-set", "2 3");
            yield return Ask(b, "creator-set", "3 " + pony);
            yield return Ask(b, "creator-set", "4 4");
            yield return Ask(b, "creator-set", "5 3");
            yield return Ask(b, "creator-set", "7 3");
            string chosen = Between(_last.data, "look=", ";");
            yield return Ask(b, "creator-name", "  bRIgida ");
            yield return Ask(b, "creator-create");
            Check(_last.ok && _last.data.StartsWith("Brigida") && _last.data.Contains("open=0") && _last.data.Contains("blocked=0") && _last.data.Contains("captured=0"),
                $"Erstellt als „Brigida“ (Schreibweise vereinheitlicht), Editor schließt, Steuerung frei ({_last.data})");
            NetworkInventory b2 = null;
            yield return WaitUntil(() => (b2 = OnlineInv(CreatorB)) != null && b2.IsReady, 10f);
            Check(b2 != null && NetworkInventory.Pending(CreatorB) == null, "Der Charakter ist geladen und in der Welt");
            if (b2 == null) yield break;
            yield return new WaitForSecondsRealtime(1.5f);
            CharacterSaveData sd = b2.ServerData;
            Check(sd.name == "Brigida" && sd.version == CharacterSaveData.CurrentVersion && sd.appearance.body == 1 && sd.appearance.face == 3 &&
                  sd.appearance.skin == 3 && sd.appearance.hair == pony && sd.appearance.hairColor == 4 && sd.appearance.eyeColor == 3 && sd.appearance.feature == 3,
                $"Gespeichert: Name, Körper, Gesicht, Haut, Frisur, Haarfarbe, Augen, Merkmal (Version {sd.version})");
            Check(CharacterStore.Service.TryLoad(CreatorB, out CharacterSaveData disk) && disk.name == "Brigida" && disk.appearance.eyeColor == 3,
                "… auch in der Datei auf dem Server");
            Check(CharacterStore.ValidateNewName("brigida", "creator-x", out _) != null, "Der Name ist jetzt vergeben (auch in anderer Schreibweise)");
            int worn = 0;
            foreach (ItemStack st2 in b2.ServerModel.Equipment) if (!st2.IsEmpty) worn++;
            Check(worn >= 2, $"Startausrüstung der Klasse wird getragen ({worn} Teile)");
            Check(sd.classId == "class.ashstalker", $"Gewählte Klasse gespeichert ({sd.classId})");
            {
                ItemStack mh = b2.ServerModel.Equipment[(int)EquipmentSlot.MainHand], oh = b2.ServerModel.Equipment[(int)EquipmentSlot.OffHand],
                          rg = b2.ServerModel.Equipment[(int)EquipmentSlot.Ranged];
                Check(!mh.IsEmpty && !oh.IsEmpty && !rg.IsEmpty && mh.id.Contains("dagger") && oh.id.Contains("dagger") && rg.id.Contains("bow"),
                    $"Startausrüstung des Aschenjägers: zwei Dolche und Bogen ({mh.id}, {oh.id}, {rg.id})");
            }

            var look2 = b2.GetComponent<NetworkAppearance>();
            var rig = b2.GetComponent<EquipmentVisuals>()?.Rig;
            yield return new WaitForSecondsRealtime(0.5f);
            Check(look2 != null && !look2.Hidden && b2.GetComponent<NetworkPlayer>().DisplayName == "Brigida", "Maxmod sieht Brigida mit Namensschild");
            Check(rig != null && rig.OverlaySummary().Contains("T_Face_hager_female") && rig.OverlaySummary().Contains("T_Feature_sommersprossen_female") &&
                  rig.EyeTextureName == ExpectedEyeTexture(cat, sd.appearance.body, sd.appearance.eyeColor) && Mathf.Abs(rig.HeadScale.x - 0.95f) < 0.01f && rig.HairPartCount >= 1,
                $"… mit demselben Aussehen (Gesicht, Sommersprossen, grüne Augen, Kopfform, Pferdeschwanz: {rig?.OverlaySummary()})");
            yield return Ask(b2, "self-look");
            Check(_last.ok && _last.data.Contains("hidden=0") && _last.data.Contains("T_Feature_sommersprossen_female") && _last.data.Contains("name=Brigida") &&
                  Between(_last.data, "look=", ";") == chosen, $"Brigida sieht sich selbst genauso ({Between(_last.data, "look=", ";")})");
            yield return Ask(b2, "walk", "1 1");
            ParsePos(_last.data, out Vector3 p2, out _, out _);
            Check(Vector3.Distance(p1, p2) > 0.5f, $"Brigida kann laufen ({Vector3.Distance(p1, p2):0.0} m)");
            yield return CaptureBody(cat, sd.appearance, "Brigida");

            // ---------------------------------------------------------- Neu anmelden
            Line("");
            Line("[Neu anmelden]");
            b2.SaveNow();
            yield return Ask(b2, "reconnect");
            yield return WaitUntil(() => OnlineInv(CreatorB) == null || OnlineInv(CreatorB) != b2, 10f);
            NetworkInventory b3 = null;
            yield return WaitUntil(() => (b3 = OnlineInv(CreatorB)) != null && b3 != b2 && b3.IsReady, 40f);
            Check(b3 != null && NetworkInventory.Pending(CreatorB) == null, "Beim nächsten Anmelden kein Editor – der Charakter wird geladen");
            if (b3 != null)
            {
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Ask(b3, "creator");
                Check(_last.data.Contains("open=0") && _last.data.Contains("pending=0"), "Editor bleibt zu");
                yield return Ask(b3, "self-look");
                Check(_last.data.Contains("name=Brigida") && Between(_last.data, "look=", ";") == chosen, "Gleicher Name, gleiches Aussehen");
            }

            // ---------------------------------------------------------- Alte Staende und Regeln
            Line("");
            Line("[Alte Speicherstände und Regeln]");
            var oldLook = CharacterAppearanceData.Decode("1|b0|s1|h1|c2|d1|f0|y|t");
            Check(oldLook.face == 0 && oldLook.eyeColor == 0 && oldLook.feature == 0 && oldLook.skin == 1, "Aussehen von vor M7 bleibt gültig (Gesicht/Augen/Merkmal = Standard, Hautton „Dunkel“ bleibt)");
            var oldSave = new CharacterSaveData { version = 3, profileId = "alt-m6", name = "Kelwen", actionBar = ActionBarLayout.Defaults() };
            CharacterStore.MigrateForTests(oldSave);
            Check(oldSave.version == CharacterSaveData.CurrentVersion && oldSave.name == "Kelwen", "Speicherstand Version 3 wird ohne Verlust auf Version 4 gehoben");
            Check(!CharacterStore.NeedsCreation("maltebot") && !CharacterStore.NeedsCreation("duo-a") && CharacterStore.NeedsCreation("spieler-niegesehen01"),
                "Editor nur für echte neue Spieler (feste Besetzung und Test-Profile legen sich selbst an)");
        }

        private static string Between(string s, string key, string end)
        {
            int i = s.IndexOf(key, System.StringComparison.Ordinal);
            if (i < 0) return "";
            i += key.Length;
            int j = s.IndexOf(end, i, System.StringComparison.Ordinal);
            return j < 0 ? s.Substring(i) : s.Substring(i, j - i);
        }

        private static string Head(string s)
        {
            int i = s.IndexOf(';');
            return i < 0 ? s : s.Substring(0, i);
        }

        // ---------------------------------------------------------- Bilder fuer die Sichtpruefung

        private static string CaptureDir => Ashveil.Core.ProjectPaths.Logs("Charakter");

        /// <summary>Alle Gesichter, Merkmale, Augen, Frisuren und Baerte als Nahaufnahme (Host-Vorschau).</summary>
        private IEnumerator CaptureCatalog()
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            CharacterPreviewStage stage = CharacterPreviewStage.Ensure();
            Directory.CreateDirectory(CaptureDir);
            int shots = 0;
            var gear = new ItemStack[InventoryModel.EquipCount];
            for (int body = 0; body < cat.bodies.Count; body++)
            {
                var baseLook = new CharacterAppearanceData { body = body, hairColor = 1, hair = AppearanceDefaults.FirstStyle(cat.hairstyles, cat, body) };
                baseLook.EnsureArrays();
                string bk = body == 0 ? "mann" : "frau";
                for (int f = 0; f < cat.faces.Count; f++)
                {
                    var l = baseLook.Clone(); l.face = f;
                    yield return Shot(stage, gear, l, $"{bk}_gesicht_{f}", 1f, 0f); shots++;
                }
                for (int m = 1; m < cat.features.Count; m++)
                {
                    var l = baseLook.Clone(); l.feature = m;
                    yield return Shot(stage, gear, l, $"{bk}_merkmal_{m}", 1f, 0f); shots++;
                }
                for (int h = 0; h < cat.hairstyles.Count; h++)
                {
                    if (!cat.StyleAllowed(cat.hairstyles, h, body)) continue;
                    var l = baseLook.Clone(); l.hair = h;
                    yield return Shot(stage, gear, l, $"{bk}_frisur_{h}", 0.8f, 35f); shots++;
                    yield return Shot(stage, gear, l, $"{bk}_frisur_{h}_seite", 0.8f, 100f); shots++;
                }
                for (int bd = 1; bd < cat.beards.Count; bd++)
                {
                    if (!cat.StyleAllowed(cat.beards, bd, body)) continue;
                    var l = baseLook.Clone(); l.beard = bd;
                    yield return Shot(stage, gear, l, $"{bk}_bart_{bd}", 1f, 20f); shots++;
                }
                for (int s = 0; s < cat.skinTones.Count; s++)
                {
                    var l = baseLook.Clone(); l.skin = s;
                    yield return Shot(stage, gear, l, $"{bk}_haut_{s}", 0.55f, 0f); shots++;
                }
            }
            for (int e = 0; e < cat.eyeColors.Count; e++)
            {
                var l = new CharacterAppearanceData { body = 1, hairColor = 1, eyeColor = e, hair = AppearanceDefaults.FirstStyle(cat.hairstyles, cat, 1) };
                l.EnsureArrays();
                yield return Shot(stage, gear, l, $"augen_{e}", 1f, 0f); shots++;
            }
            stage.SetZoom(0f, true);
            stage.SetYaw(0f);
            stage.Show(false);
            Check(shots > 40 && File.Exists(Path.Combine(CaptureDir, "mann_gesicht_1.png")), $"{shots} Nahaufnahmen für die Sichtprüfung gespeichert (Logs/Ashveil/Charakter)");
        }

        private IEnumerator CaptureBody(AppearanceCatalog cat, CharacterAppearanceData look, string name)
        {
            CharacterPreviewStage stage = CharacterPreviewStage.Ensure();
            var gear = new ItemStack[InventoryModel.EquipCount];
            yield return Shot(stage, gear, look, name + "_gesicht", 1f, 15f);
            yield return Shot(stage, gear, look, name + "_figur", 0f, 25f);
            stage.SetZoom(0f, true);
            stage.Show(false);
        }

        private static IEnumerator Shot(CharacterPreviewStage stage, ItemStack[] gear, CharacterAppearanceData look, string file, float zoom, float yaw)
        {
            stage.SetState(gear, look);
            stage.SetYaw(yaw);
            stage.SetZoom(zoom, true);
            yield return null; // Kopfform (LateUpdate) und Skinning einen Takt laufen lassen
            yield return null;
            stage.Capture(Path.Combine(CaptureDir, file + ".png"));
        }
    }
}
