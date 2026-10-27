using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kareth v4 – „Einladung beim Ankommen“ (WORLD-238, Max 10.10.2026): Wer das Wachtfeuer-Lager zum ersten Mal betritt,
    /// wird von der Köchin Wenna an der Kochstelle angesprochen (sie steht neben dem Südwesttor, durch das der Uferpfad von
    /// der Ankunft ins Lager führt). Einmal je Figur, Marke <see cref="FlagAnkunft"/> im Speicherstand; der Server entscheidet,
    /// der Client spielt die Szene (Resources/Dialog/kareth.txt, Szene <see cref="SzeneAnkunft"/>). Kein Zwang, kein Fenster:
    /// Untertitel wie bei den Geschichten des Pferdehofs, man kann weiterlaufen.
    /// Nur in Szenen mit dem Kennzeichen Kareth v4 (<see cref="KarethCatalog.SceneTag"/>); die Sandbox bleibt unberührt.
    /// </summary>
    public sealed partial class NetworkProgression
    {
        public const string FlagAnkunft = "story.kareth.ankunft";
        public const string SzeneAnkunft = "kareth_ankunft";
        /// <summary>Mitte des Lagers (Wachtfeuer) und Radius, ab dem die Einladung kommt (Palisade r ≈ 55 m, Südwesttor bei −46/−40).</summary>
        private static readonly Vector3 LagerMitte = new Vector3(0f, 0f, -4f);
        private const float LagerRadius = 50f;
        private float _karethTakt;

        private void KarethUpdate()
        {
            _karethTakt -= Time.deltaTime;
            if (_karethTakt > 0f) return;
            _karethTakt = 0.5f;
            if (D.HasFlag(FlagAnkunft) || !Ashveil.Gameplay.Quests.SceneQuestFilter.HasTag(KarethCatalog.SceneTag)) return;
            Vector3 p = transform.position;
            float dx = p.x - LagerMitte.x, dz = p.z - LagerMitte.z;
            if (dx * dx + dz * dz > LagerRadius * LagerRadius) return;
            StoryMilestones.Grant(D, FlagAnkunft);
            _inv.ServerCommit();
            PlayScene(SzeneAnkunft);
        }
    }
}
