namespace Ashveil.Networking
{
    /// <summary>
    /// REQ-20261019-003 (19.10.2026): Server-seitige Freigabe fuer Test- und Entwicklerfunktionen (Teleport, Quest geben,
    /// Klasse/Stufe setzen, Uhrzeit, Herdentempo …). Bisher reichte ein Test-Profilname, den der Client selbst waehlt.
    /// Jetzt nur noch im Editor oder in Development-Builds (Spieltest); in fertigen Builds immer gesperrt.
    /// Das Speicherverhalten von Test-Profilen (CharacterStore.IsTestProfile) bleibt davon unberuehrt.
    /// </summary>
    public static class TestRechte
    {
        public static bool Erlaubt(string profile)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return !string.IsNullOrEmpty(profile) && CharacterStore.IsTestProfile(profile);
#else
            return false;
#endif
        }
    }
}
