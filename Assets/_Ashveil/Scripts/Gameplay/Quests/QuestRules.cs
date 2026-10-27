using System;
using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Items;

namespace Ashveil.Gameplay.Quests
{
    /// <summary>Markierung ueber einem NPC – fuer jeden Spieler eigen.</summary>
    public enum QuestMarkerState
    {
        None,
        /// <summary>Grau "?" – Auftrag laeuft, hier abzugeben.</summary>
        InProgress,
        /// <summary>Gelbes "!" – neuer Auftrag.</summary>
        Available,
        /// <summary>Gelbes "?" – fertig, hier abgeben.</summary>
        Complete
    }

    /// <summary>
    /// Wer fragt: Volk, Klasse und Geschichtsmarken (fuer Klassen- und Geschichtsquests). Server: aus dem Speicherstand,
    /// Client: aus <see cref="StoryClient"/> und der eigenen Klasse.
    /// </summary>
    public sealed class QuestGate
    {
        public string Race;
        public CharacterClassDefinition Class;
        public Func<string, bool> HasFlag;

        /// <summary>Client: eigener Stand.</summary>
        public static QuestGate Local(CharacterClassDefinition cls) =>
            new QuestGate { Race = StoryClient.Race, Class = cls, HasFlag = StoryClient.HasFlag };
    }

    /// <summary>
    /// Regeln rund um Auftraege – dieselben auf Server (entscheidet) und Client (zeigt an).
    /// </summary>
    public static class QuestRules
    {
        public const int MaxActive = 20;

        /// <summary>null = darf annehmen, sonst Grund.</summary>
        public static string CanAccept(QuestDefinition q, int level, Func<string, bool> completed, Func<string, bool> active, int activeCount, QuestGate gate = null)
        {
            if (q == null) return "Unbekannter Auftrag.";
            if (active(q.Id)) return "Auftrag läuft bereits.";
            if (completed(q.Id)) return "Auftrag bereits erledigt.";
            string hidden = SceneQuestFilter.Hidden(q);   // Kareth v4: Q6 erst mit dem Salzsee (Welle 1); #28 nur in Kareth v4
            if (hidden != null) return hidden;
            foreach (QuestDefinition p in q.prerequisites)
                if (p != null && !completed(p.Id)) return "Noch nicht verfügbar.";
            string gateError = CheckGate(q, gate);
            if (gateError != null) return gateError;
            if (level < q.minLevel) return $"Benötigt Stufe {q.minLevel}.";
            if (activeCount >= MaxActive) return "Zu viele Aufträge.";
            return null;
        }

        /// <summary>
        /// Volk, Rolle, Klasse und Geschichtsmarken. Ohne Angaben zum Fragenden gilt ein Auftrag mit Bedingungen als nicht verfuegbar
        /// (lieber zu streng als falsch vergeben). Hat keine Hoechststufe – Klassenquests brechen auf hoher Stufe nicht.
        /// </summary>
        public static string CheckGate(QuestDefinition q, QuestGate gate)
        {
            if (q == null || !q.HasGate) return null;
            if (gate == null) return "Noch nicht verfügbar.";
            if (!string.IsNullOrEmpty(q.requiredRace) && gate.Race != q.requiredRace) return "Nicht für dein Volk.";
            if (!string.IsNullOrEmpty(q.requiredClass) && (gate.Class == null || gate.Class.Id != q.requiredClass)) return "Nicht für deine Klasse.";
            if (q.requiredRole != ClassRole.None && (gate.Class == null || (gate.Class.roles & q.requiredRole) == 0)) return "Nicht für deine Klasse.";
            if (q.requiredFlags != null)
                foreach (string f in q.requiredFlags)
                    if (!string.IsNullOrEmpty(f) && (gate.HasFlag == null || !gate.HasFlag(f))) return "Noch nicht verfügbar.";
            return null;
        }

        /// <summary>Sichtbar im Angebot des Gebers: Voraussetzungen erfuellt – Geschichtsquests nur, wenn ihre Szene es freigibt.</summary>
        public static bool Offered(QuestDefinition q, string npcId, int level, Func<string, bool> completed, Func<string, bool> active, QuestGate gate = null) =>
            q != null && q.giverNpc == npcId && (!q.offeredByStory || StoryClient.StoryOffer == q.Id) && CanAccept(q, level, completed, active, 0, gate) == null;

        /// <summary>Belohnungen, die fuer diese Klasse gelten (Ausruestung nur, wenn tragbar; Munition nur, wenn verschiessbar).</summary>
        public static List<QuestItemReward> ForClass(List<QuestItemReward> list, CharacterClassDefinition cls)
        {
            var result = new List<QuestItemReward>();
            if (list == null) return result;
            foreach (QuestItemReward r in list)
            {
                if (r == null || r.item == null) continue;
                if (r.item.IsEquippable && !InventoryModel.UsableByClass(r.item, cls)) continue;
                // 21.10. (AUFTRAEGE-02): Pfeile/Bolzen nur fuer Klassen mit Bogen bzw. Armbrust – der Aschenweber bekam sonst bis zu
                // 900 Pfeile (Q05, N02, N06, H3), unverkaeuflich und nicht wegzuwerfen. Server und Fenster nutzen beide diese Regel.
                if (r.item.IsAmmo && cls != null && !cls.CanUseWeapon(r.item.ammoType == AmmoType.Arrow ? WeaponClass.Bow : WeaponClass.Crossbow)) continue;
                result.Add(r);
            }
            return result;
        }

        public static QuestMarkerState MarkerFor(string npcId, IProgressionService p, CharacterClassDefinition cls)
        {
            if (p == null || !p.Ready || string.IsNullOrEmpty(npcId)) return QuestMarkerState.None;
            QuestMarkerState best = QuestMarkerState.None;
            foreach (QuestView v in p.Quests)
            {
                if (v.Def == null || v.Def.TurnInNpc != npcId) continue;
                QuestMarkerState s = v.Complete || TalkToHere(v, npcId) ? QuestMarkerState.Complete : QuestMarkerState.InProgress;
                if (s > best) best = s;
            }
            if (best == QuestMarkerState.Complete) return best;
            foreach (QuestDefinition q in GameData.Quests)
                if (!q.unmarked && Offered(q, npcId, p.Level, p.IsCompleted, p.IsActive, QuestGate.Local(cls))) return QuestMarkerState.Available;
            return best;
        }

        /// <summary>Einziges offenes Ziel ist "sprich mit diesem NPC" – Gespraech erledigt es.</summary>
        public static bool TalkToHere(QuestView v, string npcId)
        {
            if (v.Def == null) return false;
            for (int i = 0; i < v.Def.objectives.Count; i++)
            {
                QuestObjective o = v.Def.objectives[i];
                bool done = i < v.Progress.Length && v.Progress[i] >= o.count;
                if (done) continue;
                if (o.type != QuestObjectiveType.TalkTo || o.targetId != npcId) return false;
            }
            return true;
        }
    }
}
