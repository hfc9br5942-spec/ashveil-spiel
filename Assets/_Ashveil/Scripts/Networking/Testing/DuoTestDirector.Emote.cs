using System.Collections;
using System.IO;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Visuals;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest M8.1 „Emotes“ mit drei echten Spielern: Maxmod (Editor-Host), Krisbot und spaeter Maltebot
    /// (kommt mitten in einen Tanz). Geprueft: alle sechs Emotes per Chatbefehl, Menue (J) und Actionbar, Einmal-Emotes
    /// enden von selbst, Tanzen/Sitzen laufen bis Bewegung oder Abbruch, andere sehen dasselbe (auch spaeter Hinzukommende
    /// an derselben Stelle), kein Emote im Kampf, Schaden beendet, Server lehnt Unbekanntes ab. Bilder: Logs/Ashveil/Emotes.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string EmoteFlag = "emotes";
        private const string EmoteA = "emote-a", EmoteB = "emote-b", EmoteC = "emote-c";
        private static bool _emoteMode;

        private IEnumerator EmoteSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(EmoteA)) != null, 30f);
            Check(a != null, "Maxmod (Editor-Host) angemeldet");
            if (a == null) yield break;
            yield return CaptureEmotes();
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, EmoteB);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(EmoteB)) != null && b.IsReady, 120f);
            Check(b != null, "Krisbot (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2.5f);
            var ne = b.GetComponent<NetworkEmote>();
            Check(ne != null && ne.Poser != null, "Krisbot hat Emotes (Netz + Darstellung)");
            if (ne == null) yield break;

            // ---------------------------------------------------------- Einmal-Emotes per Chat
            Line("");
            Line("[Chatbefehle – Einmal-Emotes]");
            foreach ((string cmd, EmoteId id) in new[] { ("/winken", EmoteId.Wave), ("/fistbump", EmoteId.Fistbump), ("/jubeln", EmoteId.Cheer), ("/lachen", EmoteId.Laugh) })
            {
                yield return Ask(b, "chat-line", cmd);
                bool sync = ne.Current == id;
                bool seen = ne.Poser != null && ne.Poser.Current == id;
                Check(_last.ok && sync && _last.data.Contains("poser=" + id) && seen, $"{cmd}: Server, Krisbot und Maxmod zeigen {id}");
                EmoteCatalog.TryGet(id, out EmoteInfo info);
                yield return new WaitForSecondsRealtime(info.Duration + 0.8f);
                Check(ne.Current == EmoteId.None && !ne.Poser.Active, $"{cmd} endet von selbst nach {info.Duration:0.0} s");
            }

            // ---------------------------------------------------------- Dauer-Emotes
            Line("");
            Line("[Tanzen und Sitzen]");
            yield return Ask(b, "chat-line", "/tanzen");
            Check(ne.Current == EmoteId.Dance, "/tanzen startet den Tanz");
            yield return new WaitForSecondsRealtime(5f);
            Check(ne.Current == EmoteId.Dance && ne.Poser.Current == EmoteId.Dance && ne.Poser.Elapsed > 4.5f, $"Der Tanz läuft endlos weiter (nach {ne.Poser.Elapsed:0.0} s)");

            // Maltebot kommt dazu, waehrend Krisbot tanzt
            StartClient(exe, EmoteC);
            NetworkInventory c = null;
            yield return WaitUntil(() => (c = OnlineInv(EmoteC)) != null && c.IsReady, 120f);
            Check(c != null, "Maltebot kommt mitten im Tanz dazu");
            if (c != null)
            {
                yield return new WaitForSecondsRealtime(2f);
                yield return Ask(c, "emote-of", b.ObjectId.ToString());
                float hostElapsed = ne.Poser.Elapsed;
                float seenElapsed = ParseAfter(_last.data, "elapsed=");
                Check(_last.ok && _last.data.Contains("poser=Dance") && Mathf.Abs(seenElapsed - hostElapsed) < 1.5f,
                    $"Maltebot sieht Krisbot tanzen – an derselben Stelle ({seenElapsed:0.0} s / Host {hostElapsed:0.0} s)");
            }

            yield return Ask(b, "walk", "0.6 1");
            yield return new WaitForSecondsRealtime(0.5f);
            Check(ne.Current == EmoteId.None && !ne.Poser.Active, "Bewegung beendet den Tanz (Server und Anzeige)");
            yield return Ask(b, "emote-state");
            Check(_last.data.Contains("poser=None"), "… auch bei Krisbot selbst");

            yield return Ask(b, "chat-line", "/sitzen");
            yield return new WaitForSecondsRealtime(3f);
            Check(ne.Current == EmoteId.Sit, "/sitzen: Krisbot bleibt sitzen");
            yield return Ask(b, "chat-line", "/aufstehen");
            yield return new WaitForSecondsRealtime(0.5f);
            Check(ne.Current == EmoteId.None, "/aufstehen beendet das Sitzen");

            // ---------------------------------------------------------- Menue und Actionbar
            Line("");
            Line("[Emote-Menü (J) und Actionbar]");
            yield return Ask(b, "emote-menu", "emote.cheer");
            Check(_last.ok && _last.data.Contains("rows=6") && ne.Current == EmoteId.Cheer, $"Menü mit 6 Emotes, Klick spielt „Jubeln“ ({Head(_last.data)})");
            yield return new WaitForSecondsRealtime(3f);
            yield return Ask(b, "bar-assign", "1 6 Emote emote.dance");
            Check(_last.ok, "„Tanzen“ auf die Leiste gelegt (B7)");
            yield return Ask(b, "bank-hold", "1");
            yield return Ask(b, "slot-press", "6");
            yield return new WaitForSecondsRealtime(0.4f);
            Check(ne.Current == EmoteId.Dance, "Taste B7 startet den Tanz");
            yield return Ask(b, "slot-press", "6");
            yield return Ask(b, "bank-hold", "0");
            yield return new WaitForSecondsRealtime(0.4f);
            Check(ne.Current == EmoteId.None, "Nochmal drücken beendet ihn");
            yield return Ask(b, "bar-assign", "1 7 Emote emote.gibt_es_nicht");
            Check(!_last.ok, "Unbekanntes Emote auf der Leiste wird abgelehnt");
            yield return Ask(b, "bar-clear", "1 6");

            // ---------------------------------------------------------- Kampf
            Line("");
            Line("[Kampf]");
            yield return Ask(b, "chat-line", "/tanzen");
            yield return new WaitForSecondsRealtime(1f);
            var hb = b.GetComponent<Health>();
            hb.ApplyDamage(new DamageInfo { Amount = 5f });
            yield return new WaitForSecondsRealtime(0.6f);
            Check(ne.Current == EmoteId.None, "Schaden beendet den Tanz");
            yield return Ask(b, "chat-line", "/winken");
            yield return new WaitForSecondsRealtime(0.4f);
            Check(ne.Current == EmoteId.None && ne.ServerLastReject.Contains("Kampf"), $"Im Kampf kein Emote: {ne.ServerLastReject}");
            yield return Ask(b, "emote-state");
            Check(_last.data.Contains("poser=None") && _last.data.Contains("Kampf"), "Krisbot bekommt die Meldung, die Figur bleibt ruhig");
            yield return new WaitForSecondsRealtime(NetworkEmote.CombatLock + 0.5f);
            yield return Ask(b, "chat-line", "/winken");
            Check(ne.Current == EmoteId.Wave, "Nach dem Kampf geht es wieder");
            Check(EmoteCatalog.TryGetByCommand("/tanz", out EmoteInfo t) && t.Id == EmoteId.Dance && !EmoteCatalog.TryGetByCommand("/fliegen", out _),
                "Befehle und Kurzformen werden erkannt, Unbekanntes nicht");
        }

        // ---------------------------------------------------------- Bilder

        private IEnumerator CaptureEmotes()
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            CharacterPreviewStage stage = CharacterPreviewStage.Ensure();
            string dir = Ashveil.Core.ProjectPaths.Logs("Emotes");
            Directory.CreateDirectory(dir);
            var gear = new ItemStack[InventoryModel.EquipCount];
            int shots = 0;
            for (int body = 0; body < 2 && cat != null; body++)
            {
                var look = new CharacterAppearanceData { body = body, hairColor = 1, hair = AppearanceDefaults.FirstStyle(cat.hairstyles, cat, body) };
                look.EnsureArrays();
                foreach (EmoteInfo e in EmoteCatalog.All)
                {
                    stage.Show(true);
                    stage.SetState(gear, look);
                    stage.SetZoom(0f, true);
                    stage.SetYaw(e.Id == EmoteId.Dance ? 60f : 25f);
                    stage.PlayEmote(e.Id);
                    float wait = e.Id == EmoteId.Sit ? 2.2f : e.Id == EmoteId.Dance ? 1.6f : e.Id == EmoteId.Fistbump ? 0.8f : 1.0f;
                    yield return new WaitForSeconds(wait);
                    stage.Capture(Path.Combine(dir, (body == 0 ? "mann_" : "frau_") + e.Command + ".png"));
                    stage.PlayEmote(EmoteId.None);
                    shots++;
                    yield return new WaitForSeconds(0.5f);
                }
            }
            stage.Show(false);
            Check(shots == 12, $"{shots} Bilder der Emotes gespeichert (Logs/Ashveil/Emotes)");
        }
    }
}
