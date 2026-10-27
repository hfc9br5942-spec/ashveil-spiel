using System;
using Ashveil.Data;
using Ashveil.Gameplay.Quests;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Ausgeruht wie WoW Classic (Max 10.10.2026: „Ausgeruht-EP: Ja, wie Classic“), nur auf dem Server:
    /// - Im Gasthaus (<see cref="RestArea"/>) sammelt sich Ruhe: 5 % der Stufe je 8 Stunden, auch angemeldet, solange man dort steht.
    /// - Abgemeldet ausserhalb eines Gasthauses ein Viertel davon.
    /// - Hoechstens anderthalb Stufen. Jeder Sieg gibt so viel Zusatz-EP wie seine eigenen EP, bis die Ruhe aufgebraucht ist.
    ///   Auftraege und Entdeckungen werden nicht verdoppelt.
    /// Zeit ist die Serveruhr (Unix-Sekunden) – der Client kann nichts beeinflussen.
    /// </summary>
    public sealed partial class NetworkProgression
    {
        private bool _restInArea;
        private float _restTimer;

        /// <summary>Server: angesammelte Ruhe (Zusatz-EP).</summary>
        public int ServerRestedXp => Loaded ? D.restedXp : 0;
        /// <summary>Server: steht der Spieler in einem Gasthaus?</summary>
        public bool ServerInRestArea => _restInArea;

        private static long NowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>Beim Anmelden: Ruhe fuer die Zeit seit dem letzten Zeitstempel (Rate je nachdem, wo man sich abgemeldet hat).</summary>
        private void RestLogin()
        {
            if (!Loaded) return;
            long now = NowUnix;
            if (D.restStampUnix > 0 && now > D.restStampUnix)
            {
                int before = D.restedXp;
                D.restedXp = Progression.AccrueRested(D.restedXp, Model.Level, (now - D.restStampUnix) / 3600.0, D.restInRestArea);
                if (D.restedXp > before)
                    Debug.Log($"[Ashveil] '{_profile}' ist ausgeruht: +{D.restedXp - before} Ruhe ({D.restedXp} von {Progression.RestedCap(Model.Level)}, {(D.restInRestArea ? "Gasthaus" : "draussen")}).");
            }
            D.restedXp = Mathf.Clamp(D.restedXp, 0, Progression.RestedCap(Model.Level));
            D.restStampUnix = now;
            _restInArea = false;   // die Lage prueft das naechste RestUpdate (die Figur steht beim Anmelden evtl. noch nicht)
            _restTimer = 0f;
            SendState(true);
        }

        /// <summary>Jede Sekunde: Gasthaus betreten/verlassen, Ruhe im Gasthaus, Zeitstempel fuer das Abmelden.</summary>
        private void RestUpdate()
        {
            _restTimer -= Time.deltaTime;
            if (_restTimer > 0f) return;
            _restTimer = 1f;
            long now = NowUnix;
            bool inArea = RestArea.IsRestingAt(transform.position);
            int before = D.restedXp;
            if (_restInArea && D.restStampUnix > 0 && now > D.restStampUnix)
            {
                int accrued = Progression.AccrueRested(D.restedXp, Model.Level, (now - D.restStampUnix) / 3600.0, true);
                // Bruchteile sammeln: der Zeitstempel rueckt erst vor, wenn ein ganzer Punkt dazukam (oder man das Gasthaus verlaesst)
                if (accrued > D.restedXp || inArea != _restInArea || accrued >= Progression.RestedCap(Model.Level))
                {
                    D.restedXp = accrued;
                    D.restStampUnix = now;
                }
            }
            else D.restStampUnix = now;   // angemeldet draussen sammelt sich nichts (Classic)
            D.restInRestArea = inArea;
            if (inArea != _restInArea)
            {
                _restInArea = inArea;
                D.restStampUnix = now;
                if (inArea && Owner != null && Owner.IsActive) TargetMessage(Owner, "Gasthaus: Hier kommt ihr zur Ruhe.");
                SendState(false);
            }
            else if (D.restedXp != before) SendState(false);
        }

        /// <summary>Zusatz-EP aus Ruhe fuer einen Sieg (zieht sie von der Ruhe ab).</summary>
        private int RestConsume(int killXp)
        {
            if (!Loaded || D.restedXp <= 0 || killXp <= 0) return 0;
            int bonus = Progression.RestedBonus(killXp, D.restedXp);
            D.restedXp -= bonus;
            return bonus;
        }
    }
}
