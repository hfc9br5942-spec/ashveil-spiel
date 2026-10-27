using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Core
{
    /// <summary>
    /// Bodenhoehe an einer Weltposition – ueber das Terrain, dessen Flaeche den Punkt enthaelt. Ersetzt
    /// <c>Terrain.activeTerrain</c>, das bei mehreren geladenen Gelaendekacheln (Kareth + Grenzgebirge) nur irgendeine
    /// Kachel liefert (Large-World-Architektur §1.3). Ausserhalb aller Kacheln: kein Ergebnis.
    /// </summary>
    public static class WorldGround
    {
        private static readonly List<Terrain> Scratch = new List<Terrain>();

        /// <summary>Terrain, dessen Grundflaeche (x/z) den Punkt enthaelt, oder null.</summary>
        public static Terrain TerrainAt(Vector3 pos)
        {
            Terrain[] all = Terrain.activeTerrains;
            for (int i = 0; i < all.Length; i++)
            {
                Terrain t = all[i];
                if (t == null || t.terrainData == null) continue;
                Vector3 o = t.transform.position;
                Vector3 s = t.terrainData.size;
                if (pos.x >= o.x && pos.x <= o.x + s.x && pos.z >= o.z && pos.z <= o.z + s.z) return t;
            }
            return null;
        }

        /// <summary>Gelaendehoehe (Welt-y) am Punkt; false, wenn dort kein Gelaende geladen ist.</summary>
        public static bool TrySample(Vector3 pos, out float height)
        {
            Terrain t = TerrainAt(pos);
            if (t == null)
            {
                height = 0f;
                return false;
            }
            height = t.SampleHeight(pos) + t.transform.position.y;
            return true;
        }

        /// <summary>Gelaendehoehe oder <paramref name="fallback"/>, wenn dort kein Gelaende geladen ist.</summary>
        public static float SampleHeight(Vector3 pos, float fallback) => TrySample(pos, out float h) ? h : fallback;

        /// <summary>Wie tief unter dem Gelaende eine Figur als "aus der Welt gefallen" gilt (m).</summary>
        public const float FallTiefe = 30f;

        /// <summary>
        /// Aus der Welt gefallen? Ueber geladenem Gelaende: mehr als <see cref="FallTiefe"/> unter dessen Oberflaeche (Westweiden
        /// 12.10.2026: die Regionen liegen auf verschiedenen Hoehen, die Kuestenebene bis −175 m – eine feste Grenze wie −60 m
        /// haette dort jeden Spieler zurueckgesetzt). Im Wasser gilt erst 200 m unter dem Spiegel. Wo das Gelaende hoeher liegt (Kareth), bleibt die feste Grenze
        /// <paramref name="absolut"/> massgeblich – dort aendert sich nichts; ohne Gelaende darunter (Instanzen) ebenso.
        /// </summary>
        public static bool UnterDerWelt(Vector3 pos, float absolut)
        {
            // Höhle (Kreidekeller, 12.10.2026): Kammern liegen tiefer als 30 m unter dem Gelände – dort gilt der Boden der Höhle
            if (HoehlenVolumen.Enthaelt(pos, out Bounds hoehle)) return pos.y < hoehle.min.y - 10f;
            // Im Wasser (auch über das Gelände hinaus, z. B. das offene Meer der Westweiden): erst tief unter dem Spiegel
            if (WaterVolume.TrySurface(pos, out float spiegel, 400f)) return pos.y < Mathf.Min(spiegel - 200f, absolut);
            return TrySample(pos, out float h) ? pos.y < Mathf.Min(h - FallTiefe, absolut) : pos.y < absolut;
        }

        /// <summary>Punkt auf den Boden gesetzt (y = Gelaende + lift); ohne Gelaende unveraendert.</summary>
        public static Vector3 OnGround(Vector3 pos, float lift = 0f) => TrySample(pos, out float h) ? new Vector3(pos.x, h + lift, pos.z) : pos;
    }

    /// <summary>
    /// Zustand der Weltzellen (Streaming) als Ereignisse – so koennen Gameplay-Systeme (Gegner, NPC-Wege) reagieren,
    /// ohne Szenen oder Netzwerk zu kennen. Gemeldet vom Streamer (Server und Client); die Welt-Ebene (Kareth) ist ab
    /// Start bereit.
    /// </summary>
    public static class WorldCells
    {
        private static readonly HashSet<string> Ready = new HashSet<string>();

        /// <summary>Zelle vollstaendig bereit (Gelaende, Kollision, NavMesh).</summary>
        public static event System.Action<string> CellReady;
        /// <summary>Zelle wird gleich entladen – Referenzen auf ihre Objekte loesen.</summary>
        public static event System.Action<string> CellUnloading;

        public static bool IsReady(string cellId) => Ready.Contains(cellId);

        /// <summary>Kurzfassung des Streamers fuer die Debug-Anzeige (Region, Zellen, Speicher); null ohne Streamer.</summary>
        public static string DebugInfo { get; set; }
        public static IReadOnlyCollection<string> ReadyCells => Ready;

        public static void RaiseReady(string cellId)
        {
            if (!Ready.Add(cellId)) return;
            CellReady?.Invoke(cellId);
        }

        public static void RaiseUnloading(string cellId)
        {
            if (!Ready.Remove(cellId)) return;
            CellUnloading?.Invoke(cellId);
        }

        /// <summary>Beim Spielstart/-ende (Editor ohne Domain-Reload) zuruecksetzen.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Ready.Clear();
            DebugInfo = null;
            CellReady = null;
            CellUnloading = null;
        }
    }
}
