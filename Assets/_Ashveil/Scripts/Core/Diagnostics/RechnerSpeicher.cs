using System;
using System.Runtime.InteropServices;

namespace Ashveil.Core.Diagnostics
{
    /// <summary>
    /// Arbeitsspeicher des ganzen Rechners (Windows, GlobalMemoryStatusEx). Für den Lasttest (CORE-005): Server und 50 Bots teilen
    /// sich einen Rechner – ist der Speicher voll, lagert Windows aus, und Hänger am Server sind dann ein Befund des Prüfstands,
    /// kein Serverbefund. Der Bericht nennt deshalb die Auslastung. Auf anderen Plattformen gibt <see cref="Lesen"/> false zurück.
    /// </summary>
    public static class RechnerSpeicher
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        /// <summary>Auslastung in Prozent sowie gesamter und freier physischer Speicher in MB. false, wenn nicht lesbar.</summary>
        public static bool Lesen(out int lastProzent, out long gesamtMb, out long freiMb)
        {
            lastProzent = 0;
            gesamtMb = 0;
            freiMb = 0;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (!GlobalMemoryStatusEx(ref m)) return false;
                lastProzent = (int)m.MemoryLoad;
                gesamtMb = (long)(m.TotalPhys >> 20);
                freiMb = (long)(m.AvailPhys >> 20);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>Kurztext für Berichte, z. B. „Auslastung 62 % (40 von 64 GB frei)“; „nicht lesbar“ ohne Windows.</summary>
        public static string Text()
        {
            if (!Lesen(out int last, out long gesamt, out long frei)) return "nicht lesbar";
            return $"Auslastung {last} % ({frei / 1024f:0.0} von {gesamt / 1024f:0.0} GB frei)";
        }
    }
}
