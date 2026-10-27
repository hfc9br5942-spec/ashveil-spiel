namespace Ashveil.Networking
{
    /// <summary>
    /// Gemeinsame Regel der automatischen Tests: welche Log-Fehler gehoeren NICHT zum Spiel.
    /// Unity-Werkzeuge im Editor (z. B. der KI-Assistent mit seinem Relay-Prozess) melden gelegentlich eigene Fehler –
    /// die sollen keinen Spieltest rot faerben. Alles aus Ashveil-Code zaehlt weiterhin.
    /// </summary>
    public static class TestLog
    {
        public static bool IsEditorTooling(string message, string stackTrace)
        {
            string s = stackTrace ?? "";
            return s.Contains("com.unity.ai.") || s.Contains("Unity.AI.") || s.Contains("Unity.Relay.") ||
                   (message != null && message.StartsWith("[relay]"));
        }
    }
}
