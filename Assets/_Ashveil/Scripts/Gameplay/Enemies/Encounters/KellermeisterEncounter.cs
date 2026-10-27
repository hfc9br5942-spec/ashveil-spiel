using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Westweiden, Kreidekeller (12.10.2026, Gebietsblatt v2 §7 Abschnitt 4): Kellermeister Kord in der Alten Zollkammer – offene
    /// Welt, keine Instanz. Mehlstaub ist der angekündigte Flächenangriff seiner Definition (Telegraph, heraustreten). Bei
    /// <see cref="rufBei"/> Leben ruft er einmal die Galerie-Schützen (Gebiete „nur gerufen“). Ansagen gehen an Spieler in der Nähe.
    /// Texte: VORSCHLAG Claude (einfache Sprache).
    /// </summary>
    public sealed class KellermeisterEncounter : EnemyEncounter
    {
        [Tooltip("Gebiete der Schützen, die er einmal ruft (summonOnly)")]
        [SerializeField] private EnemySpawnArea[] gerufen = new EnemySpawnArea[0];
        [Range(0.1f, 0.9f)] [SerializeField] private float rufBei = 0.5f;

        private bool _gerufen;

        public bool HatGerufen => _gerufen;

        protected override void OnFightStarted()
        {
            _gerufen = false;
            Announce($"{bossName}: „Das Korn ist verkauft. Ihr kommt zu spät.“");
        }

        protected override void OnFightTick(float dt)
        {
            if (_gerufen || Boss == null || Boss.HealthFraction > rufBei) return;
            _gerufen = true;
            int n = 0;
            foreach (EnemySpawnArea a in gerufen)
                if (Summon(a) != null) n++;
            if (n > 0) Announce($"{bossName} pfeift laut. Im Gang antworten Schritte.");
        }

        protected override void OnVictory() => Announce($"{bossName} fällt. Auf der Zollkiste liegt eine Liste mit Siegel.");

        protected override void OnWipe() => _gerufen = false;
    }
}
