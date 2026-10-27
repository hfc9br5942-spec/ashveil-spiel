using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4, Welle 0 (Map Pass 05.10., Teil 4, von Max freigegeben): Schwellen-Bauwerke an den sechs Ausgängen.
    /// In Welle 0 sind alle Ausgänge sichtbar geschlossen – nur durch Gelände oder Bauwerk, keine unsichtbare Wand (WORLD-114).
    /// Greybox aus einfachen Formen; Lage, Richtung und Art kommen aus den Entwurfsdaten (plan.schwellen, integration.py
    /// Stufe 8, welle0.py). Richtung (yaw) zeigt vom Tal nach außen. Später ersetzt Kunst die Formen, die Lage bleibt.
    /// </summary>
    public static class KarethV4Schwellen
    {
        private static Material _stone, _stoneDark, _wood, _woodOld, _iron, _sign;

        public static string Build(KarethV4Plan p, Transform parent)
        {
            if (p.schwellen == null || p.schwellen.Length == 0) return "keine Schwellen in den Daten";
            _stone = K.Mat("KV4 Schwelle Stein", new Color(0.46f, 0.44f, 0.41f));
            _stoneDark = K.Mat("KV4 Schwelle Stein dunkel", new Color(0.3f, 0.29f, 0.28f));
            _wood = K.Mat("KV4 Schwelle Holz", new Color(0.36f, 0.26f, 0.17f));
            _woodOld = K.Mat("KV4 Schwelle Holz alt", new Color(0.27f, 0.23f, 0.19f));
            _iron = K.Mat("KV4 Schwelle Eisen", new Color(0.2f, 0.2f, 0.21f));
            _sign = K.Mat("KV4 Schwelle Schild", new Color(0.62f, 0.55f, 0.42f));
            Transform root = K.Group("Schwellen an den Ausgängen (Welle 0 geschlossen)", parent);
            int n = 0;
            foreach (KarethV4Schwelle s in p.schwellen)
            {
                if (s == null) continue;
                float y = KarethV4Builder.Ground(s.x, s.z);
                Transform g = K.Group($"Schwelle {s.typ} ({s.weg})", root, new Vector3(s.x, y, s.z), new Vector3(0f, s.yaw, 0f));
                float w = Mathf.Max(s.breite, 2.2f);
                switch (s.typ)
                {
                    case "dammbruch": Dammbruch(g, s, w); break;
                    case "granitstufen_steinschlag": Steinschlag(g, w); break;
                    case "zollturm_bergsturz": Zollturm(g, w); break;
                    case "furt_wegwacht_tor": Tor(g, w, 3.2f, true); break;
                    case "weidegatter_hangrutsch":
                        // Westweiden angeschlossen (12.10.2026): Gatter offen, der Hangrutsch ist geräumt – dahinter die Klamm
                        if (KarethV4Weidepass.Aktiv) KarethV4Weidepass.OffenesGatter(g, w, _wood, _woodOld, Ground);
                        else Weidegatter(g, w);
                        break;
                    case "bohlensteg_luecke": Bohlensteg(g, w); break;
                    default: Debug.LogWarning("[Ashveil] Kareth v4: unbekannte Schwelle " + s.typ); continue;
                }
                n++;
            }
            return $"{n} Schwellen";
        }

        // Lokale Achsen: +z = nach außen (Wegrichtung), +x = quer zum Weg. Boden lokal y = 0 (Mitte der Schwelle).

        private static float Ground(Transform g, float lx, float lz)
        {
            Vector3 wpos = g.TransformPoint(new Vector3(lx, 0f, lz));
            return KarethV4Builder.Ground(wpos.x, wpos.z) - g.position.y;
        }

        private static void Pfosten(Transform g, string name, float lx, float lz, float h, float r, Material m, float tilt = 0f)
        {
            float y0 = Ground(g, lx, lz);
            K.Prim(PrimitiveType.Cylinder, name, g, new Vector3(lx, y0 + h * 0.5f - 0.3f, lz), new Vector3(r * 2f, h * 0.5f, r * 2f), new Vector3(tilt, 0f, tilt * 0.5f), m);
        }

        private static void Brocken(Transform g, string name, float lx, float lz, float size, int seed)
        {
            var rnd = new System.Random(seed);
            float y0 = Ground(g, lx, lz);
            var scale = new Vector3(size * (0.8f + 0.5f * (float)rnd.NextDouble()), size * (0.55f + 0.35f * (float)rnd.NextDouble()), size * (0.8f + 0.5f * (float)rnd.NextDouble()));
            var euler = new Vector3((float)rnd.NextDouble() * 25f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 25f);
            K.Prim(PrimitiveType.Cube, name, g, new Vector3(lx, y0 + scale.y * 0.3f, lz), scale, euler, rnd.NextDouble() < 0.5 ? _stone : _stoneDark);
        }

        private static void Schild(Transform g, float lx, float lz, float yaw)
        {
            float y0 = Ground(g, lx, lz);
            Transform t = K.Group("Schild", g, new Vector3(lx, y0, lz), new Vector3(0f, yaw, 0f));
            K.Prim(PrimitiveType.Cube, "Schildpfahl", t, new Vector3(0f, 0.9f, 0f), new Vector3(0.12f, 1.8f, 0.12f), Vector3.zero, _wood);
            K.Prim(PrimitiveType.Cube, "Schildtafel", t, new Vector3(0f, 1.55f, 0.08f), new Vector3(0.9f, 0.55f, 0.05f), Vector3.zero, _sign, false);
        }

        /// <summary>Salzdamm: unterspülter Abschnitt. Gebrochene Pfähle an beiden Kanten, Bohlen und Steine im Wasser.
        /// Davor wartet Salzfuhrmann Oswald mit dem Karren (NPC kommt mit den Inhalten).</summary>
        private static void Dammbruch(Transform g, KarethV4Schwelle s, float w)
        {
            float half = Mathf.Max(s.breite, 10f) * 0.5f;
            foreach (float side in new[] { -1f, 1f })
                for (int i = 0; i < 3; i++)
                {
                    float lx = (-2.4f + 2.4f * i) * 1.1f;
                    float lz = side * (half + 0.6f + (i % 2) * 0.5f);
                    Pfosten(g, "Dammpfahl gebrochen", lx, lz, 1.2f + 0.5f * (i % 2), 0.16f, _woodOld, 12f * side);
                }
            Transform wasser = K.Group("Dammbruch (Wasser)", g);
            for (int i = 0; i < 4; i++)
                K.Prim(PrimitiveType.Cube, "Treibholz", wasser, new Vector3(-1.5f + i * 1.3f, s.h - g.position.y - 1.15f, -2f + i * 1.4f),
                    new Vector3(0.28f, 0.12f, 3.2f), new Vector3(0f, 25f * i - 30f, 6f * i), _woodOld, false);
            for (int i = 0; i < 5; i++) Brocken(g, "Dammstein", -3f + i * 1.5f, (i % 2 == 0 ? -1f : 1f) * (half - 1.2f), 1.1f, 700 + i);
            Schild(g, w * 0.5f + 1.2f, -half - 3.5f, 180f);
        }

        /// <summary>Nordpass: Granitstufen mit Eisenringen; Steinschlag verschüttet den Weg. Räumtrupp-Werkzeug am Rand.</summary>
        private static void Steinschlag(Transform g, float w)
        {
            for (int i = 0; i < 9; i++)
            {
                float lx = -w * 0.9f + i * (w * 1.8f / 8f);
                Brocken(g, "Felssturz", lx, (i % 3) * 1.1f - 1f, 2.2f + (i % 2) * 0.9f, 900 + i);
            }
            for (int i = 0; i < 3; i++) Brocken(g, "Felssturz groß", -w * 0.5f + i * w * 0.5f, 2.2f, 3.4f, 950 + i);
            foreach (float side in new[] { -1f, 1f })
            {
                float y0 = Ground(g, side * (w * 0.5f + 0.4f), -4f);
                K.Prim(PrimitiveType.Cylinder, "Eisenring", g, new Vector3(side * (w * 0.5f + 0.4f), y0 + 0.5f, -4f), new Vector3(0.45f, 0.03f, 0.45f), new Vector3(0f, 0f, 90f), _iron, false);
            }
            Transform werk = K.Group("Räumtrupp", g);
            K.Prim(PrimitiveType.Cube, "Werkzeugkiste", werk, new Vector3(w * 0.5f + 1.8f, Ground(g, w * 0.5f + 1.8f, -5f) + 0.3f, -5f), new Vector3(1.2f, 0.6f, 0.7f), new Vector3(0f, 15f, 0f), _wood);
            Schild(g, -w * 0.5f - 1.5f, -6f, 180f);
        }

        /// <summary>Salzstraße Ost: Zollturm am Rippendurchlass; ein Bergsturz liegt quer über der Straße.</summary>
        private static void Zollturm(Transform g, float w)
        {
            float tx = w * 0.5f + 3.5f, tz = -6f, y0 = Ground(g, tx, tz);
            Transform t = K.Group("Zollturm", g, new Vector3(tx, y0, tz), Vector3.zero);
            K.Prim(PrimitiveType.Cube, "Turmsockel", t, new Vector3(0f, 1.5f, 0f), new Vector3(4.2f, 3.6f, 4.2f), Vector3.zero, _stone);
            K.Prim(PrimitiveType.Cube, "Turm", t, new Vector3(0f, 5.5f, 0f), new Vector3(3.6f, 4.6f, 3.6f), Vector3.zero, _wood);
            K.Prim(PrimitiveType.Cube, "Turmdach", t, new Vector3(0f, 8.3f, 0f), new Vector3(4.4f, 0.4f, 4.4f), Vector3.zero, _woodOld);
            for (int i = 0; i < 11; i++)
            {
                float lx = -w * 1.1f + i * (w * 2.2f / 10f);
                Brocken(g, "Bergsturz", lx, 1.5f + (i % 3) * 1.4f, 2.6f + (i % 4) * 0.7f, 1100 + i);
            }
            Schild(g, -w * 0.5f - 1.4f, -3f, 180f);
        }

        /// <summary>Südweg: Furt und Wegwacht-Tor, verschlossen (Torflügel mit Collider, Pfosten aus Stein).</summary>
        private static void Tor(Transform g, float w, float h, bool turm)
        {
            float half = w * 0.5f + 1.0f;
            foreach (float side in new[] { -1f, 1f })
            {
                float y0 = Ground(g, side * half, 0f);
                K.Prim(PrimitiveType.Cube, "Torpfeiler", g, new Vector3(side * half, y0 + h * 0.5f, 0f), new Vector3(1.1f, h + 0.6f, 1.1f), Vector3.zero, _stone);
                K.Prim(PrimitiveType.Cube, "Torflügel", g, new Vector3(side * half * 0.5f, y0 + h * 0.45f, 0f), new Vector3(half - 0.5f, h * 0.85f, 0.18f), Vector3.zero, _wood);
            }
            K.Prim(PrimitiveType.Cube, "Riegel", g, new Vector3(0f, Ground(g, 0f, 0f) + h * 0.5f, -0.15f), new Vector3(w + 1.2f, 0.18f, 0.12f), Vector3.zero, _iron, false);
            if (turm)
            {
                float y0 = Ground(g, half + 3.0f, 2.5f);
                K.Prim(PrimitiveType.Cube, "Wachstube", g, new Vector3(half + 3.0f, y0 + 1.6f, 2.5f), new Vector3(3.4f, 3.2f, 3.4f), Vector3.zero, _stoneDark);
            }
            Schild(g, -half - 1.2f, -2f, 180f);
        }

        /// <summary>Weidensteig: hohes Weidegatter am Pass (2 m – deutlich höher als ein Sprung, 1,48 m), dahinter ein abgerutschter Hang.</summary>
        private static void Weidegatter(Transform g, float w)
        {
            float half = w * 0.5f + 0.8f;
            Pfosten(g, "Gatterpfosten", -half, 0f, 2.6f, 0.12f, _wood);
            Pfosten(g, "Gatterpfosten", half, 0f, 2.6f, 0.12f, _wood);
            float y0 = Ground(g, 0f, 0f);
            for (int i = 0; i < 5; i++)
                K.Prim(PrimitiveType.Cube, "Gatterlatte", g, new Vector3(0f, y0 + 0.35f + i * 0.4f, 0f), new Vector3(half * 2f, 0.1f, 0.06f), Vector3.zero, _wood);
            for (int i = 0; i < 8; i++)
                Brocken(g, "Hangrutsch", -w + i * (w * 2f / 7f), 3f + (i % 3) * 1.3f, 1.8f + (i % 2) * 0.8f, 1300 + i);
        }

        /// <summary>Packweg (NW): Bohlensteg über das Moor, in der Mitte fehlen Bohlen über tiefem Wasser
        /// (Lücke 6,4 m – weiter als ein Sprung mit Anlauf, ≈ 5,2 m).</summary>
        private static void Bohlensteg(Transform g, float w)
        {
            float y0 = Ground(g, 0f, 0f);
            float deck = Mathf.Max(y0, -21.7f - g.position.y) + 0.35f;          // knapp über dem Moorwasser
            for (int i = -9; i <= 9; i++)
            {
                if (i >= -3 && i <= 3) continue;                               // Lücke: fehlende Bohlen
                K.Prim(PrimitiveType.Cube, "Bohle", g, new Vector3(0f, deck, i * 0.9f), new Vector3(w * 0.9f, 0.1f, 0.8f), new Vector3(0f, 0f, (i % 3) * 1.5f), _woodOld);
            }
            foreach (float lz in new[] { -7.2f, -4.5f, 4.5f, 7.2f })
                foreach (float side in new[] { -1f, 1f })
                    Pfosten(g, "Stegpfahl", side * w * 0.45f, lz, Mathf.Max(0.8f, deck - Ground(g, side * w * 0.45f, lz) + 0.6f), 0.08f, _woodOld);
            Schild(g, -w * 0.5f - 1.0f, -9.5f, 180f);
        }
    }
}
