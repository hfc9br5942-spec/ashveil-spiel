using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Core.Services
{
    /// <summary>
    /// Minimale Service-Registry fuer globale Dienste (Persistenz, Chat, Gruppen, Netzwerk).
    /// Dienste werden ueber Interfaces registriert, damit eine In-Process-Version
    /// (Prototyp) spaeter gegen einen echten Server-Dienst getauscht werden kann,
    /// ohne Gameplay-Code anzufassen.
    ///
    /// Bewusst kein DI-Framework: Das Projekt soll fuer eine kleine Crew lesbar bleiben.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        public static void Register<TService>(TService service) where TService : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            Type key = typeof(TService);
            if (Services.ContainsKey(key))
                Debug.LogWarning($"[ServiceLocator] {key.Name} wird ueberschrieben.");
            Services[key] = service;
        }

        public static void Unregister<TService>(TService service) where TService : class
        {
            Type key = typeof(TService);
            if (Services.TryGetValue(key, out object current) && ReferenceEquals(current, service))
                Services.Remove(key);
        }

        public static bool TryGet<TService>(out TService service) where TService : class
        {
            if (Services.TryGetValue(typeof(TService), out object obj))
            {
                service = (TService)obj;
                return true;
            }
            service = null;
            return false;
        }

        public static TService Get<TService>() where TService : class
        {
            if (TryGet(out TService service)) return service;
            throw new InvalidOperationException($"[ServiceLocator] Kein Dienst fuer {typeof(TService).Name} registriert.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Services.Clear();
        }
    }
}
