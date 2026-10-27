using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Weltverzeichnis (Large-World-Masterplan §9, §99): alle Regionen, ihre Zellen und Nachbarn, mit Versionsnummer.
    /// Die Welt endet nicht an einer festen Koordinate – sie besteht aus den Regionen, die hier stehen (§182).
    /// Aufbau/Pruefung: Menue Ashveil › Welt (Editor/World/WorldManifestBuilder.cs). Doku: Docs/World/.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Welt/Weltverzeichnis", fileName = "WorldManifest")]
    public sealed class WorldManifest : ScriptableObject
    {
        [Tooltip("Erhoehen, wenn sich Gelaende so aendert, dass gespeicherte Positionen ungueltig werden koennen.")]
        public int version = 1;
        public List<WorldRegionDefinition> regions = new List<WorldRegionDefinition>();

        public WorldRegionDefinition FindRegion(string regionId)
        {
            foreach (WorldRegionDefinition r in regions)
                if (r != null && r.regionId == regionId) return r;
            return null;
        }

        public WorldCellDefinition FindCell(string cellId)
        {
            foreach (WorldRegionDefinition r in regions)
            {
                if (r == null) continue;
                foreach (WorldCellDefinition c in r.cells)
                    if (c != null && c.cellId == cellId) return c;
            }
            return null;
        }

        /// <summary>Zelle, deren Grundflaeche den Punkt enthaelt (bei Ueberlappung die mit hoeherer Prioritaet).</summary>
        public WorldCellDefinition CellAt(Vector3 pos, out WorldRegionDefinition region)
        {
            region = null;
            WorldCellDefinition best = null;
            foreach (WorldRegionDefinition r in regions)
            {
                if (r == null) continue;
                foreach (WorldCellDefinition c in r.cells)
                    if (c != null && c.Contains(pos, 0f) && (best == null || c.streamingPriority > best.streamingPriority))
                    {
                        best = c;
                        region = r;
                    }
            }
            return best;
        }

        public IEnumerable<(WorldRegionDefinition region, WorldCellDefinition cell)> AllCells()
        {
            foreach (WorldRegionDefinition r in regions)
            {
                if (r == null) continue;
                foreach (WorldCellDefinition c in r.cells)
                    if (c != null) yield return (r, c);
            }
        }
    }

    /// <summary>Kleinste streambare Einheit (Masterplan §11): genau eine additive Szene – oder die Welt-Ebene selbst.</summary>
    [Serializable]
    public sealed class WorldCellDefinition
    {
        public string cellId = "";
        [Tooltip("Pfad der Szene (Assets/…unity). Leer bzw. alwaysLoaded = liegt in der Welt-Ebene.")] public string scenePath = "";
        [Tooltip("Grundflaeche x/z")] public Rect bounds;
        [Tooltip("Laden, wenn ein Spieler naeher als dieser Rand ist (m)")] public float loadMargin = 250f;
        [Tooltip("Entladen erst, wenn alle Spieler weiter weg sind (m) – groesser als loadMargin (Hysterese)")] public float unloadMargin = 400f;
        [Tooltip("Mindesthaltezeit nach dem letzten Bedarf (s)")] public float holdSeconds = 20f;
        public bool alwaysLoaded;
        public int streamingPriority;

        public string SceneName => string.IsNullOrEmpty(scenePath) ? "" : System.IO.Path.GetFileNameWithoutExtension(scenePath);

        /// <summary>Liegt der Punkt in der Grundflaeche plus Rand?</summary>
        public bool Contains(Vector3 pos, float margin) =>
            pos.x >= bounds.xMin - margin && pos.x <= bounds.xMax + margin && pos.z >= bounds.yMin - margin && pos.z <= bounds.yMax + margin;

        /// <summary>Abstand (x/z) vom Punkt zur Grundflaeche, 0 wenn innen.</summary>
        public float Distance(Vector3 pos)
        {
            float dx = Mathf.Max(bounds.xMin - pos.x, 0f, pos.x - bounds.xMax);
            float dz = Mathf.Max(bounds.yMin - pos.z, 0f, pos.z - bounds.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// Gespeicherte Position (Masterplan §47): Region, Zelle, Position relativ zum Region-Ursprung (heute = Welt-Nullpunkt,
    /// vorbereitet fuer Floating Origin) und Weltversion (§48) – stimmt die nicht, gilt der naechste sichere Anker.
    /// </summary>
    [Serializable]
    public struct WorldPosition
    {
        public string regionId;
        public string cellId;
        public Vector3 localPosition;
        public float yaw;
        public int worldVersion;

        public bool IsSet => !string.IsNullOrEmpty(cellId);
    }
}
