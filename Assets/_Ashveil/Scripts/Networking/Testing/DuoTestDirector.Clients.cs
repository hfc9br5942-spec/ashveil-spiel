using System;
using UnityEngine;

namespace Ashveil.Networking
{
    public sealed partial class DuoTestDirector
    {
        private void StopOwnedClients(bool final = false)
        {
            // Only Process objects returned by this director's starts; never discover or kill processes by name/PID.
            for (int i = Clients.Count - 1; i >= 0; i--)
            {
                var client = Clients[i];
                if (client == null) { Clients.RemoveAt(i); continue; }
                try
                {
                    if (!client.HasExited)
                    {
                        try { client.Kill(); }
                        catch (InvalidOperationException) { if (!client.HasExited) throw; } // Exited between check and Kill.
                    }
                    // Kill requests termination asynchronously. Never wait/block Unity's lifecycle callbacks.
                    client.Dispose();
                    Clients.RemoveAt(i);
                }
                catch (Exception exception)
                {
                    // Keep ownership on failure so a later lifecycle callback can retry; don't silently orphan it.
                    Debug.LogWarning("[Ashveil][Duo] Eigener Test-Client konnte nicht bereinigt werden: " + exception.Message);
                    if (final)
                    {
                        client.Dispose();
                        Clients.RemoveAt(i);
                    }
                }
            }
        }
    }
}
