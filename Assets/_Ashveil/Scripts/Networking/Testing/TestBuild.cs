namespace Ashveil.Networking
{
    /// <summary>
    /// Testeinstiege sind im Editor, Entwicklungs-Build oder ausdruecklichen Testclient erlaubt.
    /// Typen und FishNet-Komponenten bleiben fuer stabile Szenen-/Prefabverweise erhalten.
    /// Serverrechte fuer Spielbefehle werden weiterhin getrennt durch TestRechte geprueft.
    /// </summary>
    public static class TestBuild
    {
        public const string Symbol = "ASHVEIL_TEST_CLIENT";

        public static bool Enabled
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ASHVEIL_TEST_CLIENT
                return true;
#else
                return false;
#endif
            }
        }
    }
}
