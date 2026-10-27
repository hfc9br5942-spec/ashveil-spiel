using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Abilities;
using Ashveil.Gameplay.Items;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// P1 Klassenlehrer und Talente (Max 30.09.):
    ///   Lehrer – wie WoW Classic: neue Faehigkeiten und hoehere Raenge gegen Geld beim Lehrer der eigenen Klasse, ab der
    ///            Stufe des Rangs, immer der naechste Rang. Neu gelernte Faehigkeiten legen sich einmal auf die Leiste.
    ///   Talente – ab Stufe 10 ein Punkt je Stufe, drei Baeume, Reihe n braucht n × 5 Punkte im Baum. Zuruecksetzen beim
    ///            Lehrer gegen Geld (1 Silber, jedes weitere Mal 1 Silber mehr, hoechstens 10 Silber).
    /// </summary>
    public sealed partial class NetworkProgression
    {
        // ================================================================== Stufenaufstieg

        private void SpellsLevelUp(int level)
        {
            if (!Loaded) return;
            GrantStartSpells();
            PlaceNewSpells();
            SendSpellState(true);
            if (Owner != null && Owner.IsActive)
            {
                int trainable = CountTrainable(level);
                if (trainable > 0) TargetMessage(Owner, $"Neue Fähigkeiten beim Klassenlehrer: {trainable}.");
                if (level == TalentDefinition.FirstTalentLevel) TargetMessage(Owner, "Du hast deinen ersten Talentpunkt (Taste N).");
            }
        }

        private int CountTrainable(int level)
        {
            CharacterClassDefinition cls = ServerClass;
            if (cls == null || GameData.Database == null) return 0;
            int n = 0;
            foreach (GameDefinition g in GameData.Database.All)
            {
                if (!(g is SpellDefinition s) || s.classId != cls.Id) continue;
                SpellRank next = s.Rank(ServerSpellRank(s.Id) + 1);
                if (next != null && next.level == level) n++;
            }
            return n;
        }

        // ================================================================== Lehrer

        public void RequestTrain(string npcId, string spellId, int rank)
        {
            if (!IsOwner || !_cReady) return;
            ServerTrainRpc(npcId ?? "", spellId ?? "", rank);
        }

        [ServerRpc]
        private void ServerTrainRpc(string npcId, string spellId, int rank)
        {
            string why = ServerTrain(npcId, spellId, rank);
            if (why.Length > 0) Reject(why);
        }

        /// <summary>Server: beim Lehrer lernen (Gespraech, Klasse, Stufe, Rang-Reihenfolge, Geld). Leer = gelernt.</summary>
        public string ServerTrain(string npcId, string spellId, int rank)
        {
            if (!Loaded) return "Nicht bereit.";
            if (!InDialogQuiet(npcId, out NpcDefinition npc, out string dialogError)) return dialogError;
            CharacterClassDefinition cls = ServerClass;
            if (!npc.IsTrainer) return "Hier gibt es nichts zu lernen.";
            if (cls == null || npc.trainerClass != cls.Id) return $"{npc.Name} bildet nur {ClassName(npc.trainerClass)} aus.";
            SpellDefinition s = GameData.Get<SpellDefinition>(spellId);
            if (s == null || s.classId != cls.Id) return "Das kann deine Klasse nicht lernen.";
            int known = ServerSpellRank(s.Id);
            if (rank != known + 1) return rank <= known ? "Das kannst du schon." : "Lerne zuerst den Rang davor.";
            SpellRank r = s.Rank(rank);
            if (r == null) return "Diesen Rang gibt es nicht.";
            if (Model.Level < r.level) return $"Erst ab Stufe {r.level}.";
            if (D.money < r.trainCost) return "Nicht genug Geld.";
            D.money -= r.trainCost;
            SetSpellRank(s.Id, rank);
            PlaceNewSpells();
            _inv.ServerCommit();
            SendState(true);
            SendSpellState(true);
            if (Owner != null && Owner.IsActive)
            {
                if (r.trainCost > 0) TargetCoins(Owner);
                TargetMessage(Owner, s.RankCount > 1 ? $"Gelernt: {s.DisplayName} (Rang {rank})." : $"Gelernt: {s.DisplayName}.");
            }
            return "";
        }

        private bool InDialogQuiet(string npcId, out NpcDefinition npc, out string error)
        {
            npc = null;
            error = "";
            if (string.IsNullOrEmpty(npcId) || _dialogNpc != npcId) { error = "Kein Gespräch."; return false; }
            return InRange(npcId, out npc, out error);
        }

        private static string ClassName(string classId)
        {
            CharacterClassDefinition c = GameData.Class(classId);
            return c != null ? c.DisplayName : "eine andere Klasse";
        }

        private void SetSpellRank(string spellId, int rank)
        {
            foreach (SpellSave sp in D.spells)
                if (sp.id == spellId) { sp.rank = rank; return; }
            D.spells.Add(new SpellSave { id = spellId, rank = rank });
        }

        /// <summary>Test: Faehigkeit direkt lernen (ohne Lehrer, ohne Geld). Nur Test-Profile.</summary>
        public void ServerDebugLearn(string spellId, int rank)
        {
            if (!Loaded || !TestRechte.Erlaubt(_profile)) return;
            SpellDefinition s = GameData.Get<SpellDefinition>(spellId);
            if (s == null || ServerClass == null || s.classId != ServerClass.Id) return;
            SetSpellRank(s.Id, Mathf.Clamp(rank, 1, s.RankCount));
            PlaceNewSpells();
            _inv.ServerCommit();
            SendSpellState(true);
        }

        /// <summary>
        /// Test: Klasse wechseln (nur Test-Profile) – gelernte Faehigkeiten und Talente beginnen neu, die Ressource folgt der
        /// Klasse. Die Ausruestung setzt der Test selbst (ServerModel.Load).
        /// </summary>
        public void ServerDebugSwitchClass(CharacterClassDefinition cls, bool keepSpells = false)
        {
            if (!Loaded || cls == null || !TestRechte.Erlaubt(_profile)) return;
            Model.Class = cls;
            D.classId = cls.Id;
            if (!keepSpells) D.spells.Clear();
            D.talents.Clear();
            SpellsLogin();
            _inv.ServerCommit();
        }

        /// <summary>Test: alle gelernten Faehigkeiten vergessen (Startfaehigkeiten kommen sofort wieder).</summary>
        public void ServerDebugForgetSpells()
        {
            if (!Loaded || !TestRechte.Erlaubt(_profile)) return;
            D.spells.Clear();
            GrantStartSpells();
            _inv.ServerCommit();
            SendSpellState(true);
        }

        /// <summary>Test: alle Abklingzeiten und das Wirken zuruecksetzen.</summary>
        public void ServerDebugResetCooldowns()
        {
            _spellReady.Clear();
            _gcdUntil = -1f;
            CancelCast("", false);
        }

        // ================================================================== Talente

        public void RequestTalent(string talentId)
        {
            if (!IsOwner || !_cReady) return;
            ServerTalentRpc(talentId ?? "");
        }

        [ServerRpc]
        private void ServerTalentRpc(string talentId)
        {
            string why = ServerLearnTalent(talentId);
            if (why.Length > 0) Reject(why);
        }

        public int ServerTalentPointsSpent()
        {
            int n = 0;
            if (Loaded && D.talents != null) foreach (TalentSave t in D.talents) n += t.rank;
            return n;
        }

        public int ServerTalentPointsFree => Loaded ? Mathf.Max(0, TalentDefinition.PointsForLevel(Model.Level) - ServerTalentPointsSpent()) : 0;

        private int PointsInTree(int tree)
        {
            int n = 0;
            foreach (TalentSave t in D.talents)
            {
                TalentDefinition def = GameData.Get<TalentDefinition>(t.id);
                if (def != null && def.tree == tree) n += t.rank;
            }
            return n;
        }

        /// <summary>Server: einen Punkt in ein Talent setzen. Leer = in Ordnung, sonst der Grund.</summary>
        public string ServerLearnTalent(string talentId)
        {
            if (!Loaded) return "Nicht bereit.";
            TalentDefinition t = GameData.Get<TalentDefinition>(talentId);
            CharacterClassDefinition cls = ServerClass;
            if (t == null || cls == null || t.classId != cls.Id) return "Unbekanntes Talent.";
            if (ServerTalentPointsFree <= 0) return Model.Level < TalentDefinition.FirstTalentLevel ? $"Talente gibt es ab Stufe {TalentDefinition.FirstTalentLevel}." : "Keine Talentpunkte frei.";
            int rank = ServerTalentRank(t.Id);
            if (rank >= t.maxRank) return "Das Talent ist voll ausgebaut.";
            if (PointsInTree(t.tree) < t.tier * TalentDefinition.PointsPerTier)
                return $"Dafür brauchst du {t.tier * TalentDefinition.PointsPerTier} Punkte in diesem Baum.";
            bool found = false;
            foreach (TalentSave ts in D.talents) if (ts.id == t.Id) { ts.rank++; found = true; break; }
            if (!found) D.talents.Add(new TalentSave { id = t.Id, rank = 1 });
            RecomputeTalents();
            _inv.ServerCommit();
            SendState(true); // setzt auch die letzte Fehlermeldung des Clients zurueck
            SendSpellState(true);
            return "";
        }

        public void RequestTalentReset(string npcId)
        {
            if (!IsOwner || !_cReady) return;
            ServerTalentResetRpc(npcId ?? "");
        }

        [ServerRpc]
        private void ServerTalentResetRpc(string npcId)
        {
            string why = ServerResetTalents(npcId);
            if (why.Length > 0) Reject(why);
        }

        public static long TalentResetCost(int resets) => 100L * Mathf.Clamp(resets + 1, 1, 10);

        /// <summary>Server: Talente beim Lehrer der eigenen Klasse zuruecksetzen (gegen Geld).</summary>
        public string ServerResetTalents(string npcId)
        {
            if (!Loaded) return "Nicht bereit.";
            if (!InDialogQuiet(npcId, out NpcDefinition npc, out string dialogError)) return dialogError;
            CharacterClassDefinition cls = ServerClass;
            if (!npc.IsTrainer || cls == null || npc.trainerClass != cls.Id) return "Das kann nur dein Klassenlehrer.";
            if (ServerTalentPointsSpent() == 0) return "Du hast keine Talente gewählt.";
            long cost = TalentResetCost(D.talentResets);
            if (D.money < cost) return "Nicht genug Geld.";
            D.money -= cost;
            D.talents.Clear();
            D.talentResets++;
            RecomputeTalents();
            _inv.ServerCommit();
            SendState(true);
            SendSpellState(true);
            if (Owner != null && Owner.IsActive) { TargetCoins(Owner); TargetMessage(Owner, "Deine Talente sind zurückgesetzt."); }
            return "";
        }

        /// <summary>Mehr vergebene Punkte als erlaubt (Daten geaendert): alles zurueck, kostenlos.</summary>
        private void ValidateTalentPoints()
        {
            if (ServerTalentPointsSpent() > TalentDefinition.PointsForLevel(Model.Level)) D.talents.Clear();
        }

        private void RecomputeTalents()
        {
            _talents.Clear();
            if (D.talents != null)
                foreach (TalentSave t in D.talents) _talents.Add(GameData.Get<TalentDefinition>(t.id), t.rank);
            if (_sEq == null) _sEq = GetComponent<CharacterEquipment>();
            if (_sEq != null)
            {
                var copy = new TalentBonuses();
                if (D.talents != null) foreach (TalentSave t in D.talents) copy.Add(GameData.Get<TalentDefinition>(t.id), t.rank);
                _sEq.SetTalents(copy);
            }
        }
    }
}
