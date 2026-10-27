using System;
using Ashveil.Data;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>Was die Oberflaeche am eigenen Aussehen aendern darf (nur Bitten – der Server entscheidet).</summary>
    public interface IAppearanceService
    {
        CharacterAppearanceData Current { get; }
        void RequestChange(CharacterAppearanceData wanted);
    }

    public static class AppearanceClient
    {
        public static IAppearanceService Current { get; private set; }
        public static event Action Changed;

        public static void Register(IAppearanceService s)
        {
            Current = s;
            Changed?.Invoke();
        }

        public static void Unregister(IAppearanceService s)
        {
            if (Current != s) return;
            Current = null;
            Changed?.Invoke();
        }

        public static void RaiseChanged() => Changed?.Invoke();
    }
}
