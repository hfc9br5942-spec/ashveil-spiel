using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>Read-only inventory of loaded networking instances; never opens scenes or changes connections.</summary>
    [InitializeOnLoad]
    public static class NetzDiagnoseAuftrag
    {
        static NetzDiagnoseAuftrag() => Auftragsordner.Registrieren("netz_status", Status, false, 60);

        [Serializable] private sealed class Snapshot
        {
            public int processId;
            public bool editorPlaying, applicationPlaying;
            public List<Instance> managers = new List<Instance>();
            public List<Instance> transports = new List<Instance>();
        }

        [Serializable] private sealed class Instance
        {
            public string id, managerId, transportId;
            public int configuredPort;
            public string name, path, scene, scenePath, hideFlags, serverState, clientState, socketManagerState;
            public bool persistentAsset, sceneValid, sceneLoaded, active, enabled, serverStarted, clientStarted;
        }

        private static Instance Describe(Behaviour component)
        {
            var s = component.gameObject.scene;
            return new Instance
            {
                id = component.GetEntityId().ToString(), name = component.name,
                path = Hierarchy(component.transform), scene = s.name, scenePath = s.path,
                hideFlags = component.hideFlags + "/" + component.gameObject.hideFlags,
                persistentAsset = EditorUtility.IsPersistent(component), sceneValid = s.IsValid(), sceneLoaded = s.isLoaded,
                active = component.gameObject.activeInHierarchy, enabled = component.enabled
            };
        }

        private static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;

        private static void Status(Auftragsordner.Auftrag job, Action<bool, string> finish)
        {
            if (!string.IsNullOrWhiteSpace(job.parameter)) { finish(false, "netz_status erwartet keinen Parameter."); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            { finish(false, "Nur ausserhalb von Play diagnostizieren; keine Verbindung geaendert."); return; }
            var snapshot = new Snapshot
            {
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                editorPlaying = EditorApplication.isPlaying, applicationPlaying = Application.isPlaying
            };
            foreach (NetworkManager manager in Resources.FindObjectsOfTypeAll<NetworkManager>())
            {
                Instance entry = Describe(manager);
                entry.serverStarted = manager.ServerManager != null && manager.ServerManager.Started;
                entry.clientStarted = manager.ClientManager != null && manager.ClientManager.Started;
                var transport = manager.TransportManager != null ? manager.TransportManager.Transport : null;
                entry.transportId = transport != null ? transport.GetEntityId().ToString() : "";
                snapshot.managers.Add(entry);
            }
            foreach (Tugboat transport in Resources.FindObjectsOfTypeAll<Tugboat>())
            {
                Instance entry = Describe(transport);
                NetworkManager manager = transport.GetComponentInParent<NetworkManager>();
                entry.managerId = manager != null ? manager.GetEntityId().ToString() : "";
                entry.configuredPort = transport.GetPort(); // Configuration only; OS binding must be checked separately.
                entry.serverState = transport.GetConnectionState(true).ToString();
                entry.clientState = transport.GetConnectionState(false).ToString();
                // Read-only metadata from the installed package. Never call Stop/Dispose or write internal fields.
                var field = typeof(FishNet.Transporting.Tugboat.CommonSocket).GetField("NetManager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field == null) entry.socketManagerState = "unknown: field unavailable";
                else
                {
                    object socketManager = field.GetValue(transport.ServerSocket);
                    if (socketManager == null) entry.socketManagerState = "absent";
                    else
                    {
                        var running = socketManager.GetType().GetProperty("IsRunning");
                        var port = socketManager.GetType().GetProperty("LocalPort");
                        entry.socketManagerState = $"present, running={running?.GetValue(socketManager)}, localPort={port?.GetValue(socketManager)}";
                    }
                }
                snapshot.transports.Add(entry);
            }
            string directory = Ashveil.Core.ProjectPaths.Logs("NetzDiagnose");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            File.WriteAllText(file, JsonUtility.ToJson(snapshot, true));
            finish(true, $"Netzstatus PID{snapshot.processId}: {snapshot.managers.Count} Manager, {snapshot.transports.Count} Tugboats; {file}. Rein lesend, keine Verbindung geaendert.");
        }
    }
}
