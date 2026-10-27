using System.Collections.Generic;
using Ashveil.Data;

namespace Ashveil.Gameplay.Visuals
{
    public static class AppearanceDefaults
    {
        /// <summary>Erste Frisur/Bart-Option, die zu diesem Koerper passt (0 = keine).</summary>
        public static int FirstStyle(List<AppearanceCatalog.StyleEntry> list, AppearanceCatalog cat, int body)
        {
            for (int i = 1; i < list.Count; i++) if (cat.StyleAllowed(list, i, body)) return i;
            return 0;
        }
    }
}
