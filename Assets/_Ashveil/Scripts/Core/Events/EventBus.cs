using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Core.Events
{
    /// <summary>
    /// Typsicherer, allokationsarmer Event-Bus fuer entkoppelte Systeme.
    ///
    /// Regeln:
    /// - Events sind structs (keine GC-Allokation beim Senden).
    /// - Systeme, die sich kennen muessen (z. B. Motor und Animator desselben Charakters),
    ///   reden direkt miteinander. Der Bus ist fuer Querverbindungen gedacht
    ///   (Quest hoert auf "Gegner getoetet", Kamera hoert auf "Erschuetterung").
    /// - Wer abonniert, meldet sich in OnDisable/OnDestroy wieder ab.
    /// </summary>
    public static class EventBus<T> where T : struct
    {
        private static Action<T> _handlers;
        private static bool _registered;

        public static void Subscribe(Action<T> handler)
        {
            EnsureRegistered();
            _handlers -= handler; // doppelte Anmeldung verhindern
            _handlers += handler;
        }

        public static void Unsubscribe(Action<T> handler)
        {
            _handlers -= handler;
        }

        public static void Raise(in T evt)
        {
            Action<T> handlers = _handlers;
            if (handlers == null) return;

            // Einzelne fehlerhafte Empfaenger duerfen den Rest nicht blockieren.
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)d)(evt);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        internal static void Clear()
        {
            _handlers = null;
        }

        private static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            EventBusRegistry.Register(Clear);
        }
    }

    /// <summary>
    /// Setzt alle Busse beim Start des Play Mode zurueck. Noetig, wenn
    /// "Enter Play Mode Options" (ohne Domain Reload) aktiv sind.
    /// </summary>
    public static class EventBusRegistry
    {
        private static readonly List<Action> ClearActions = new List<Action>();

        internal static void Register(Action clear)
        {
            ClearActions.Add(clear);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAll()
        {
            for (int i = 0; i < ClearActions.Count; i++) ClearActions[i]();
        }
    }
}
