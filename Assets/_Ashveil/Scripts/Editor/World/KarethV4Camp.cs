using System.Collections.Generic;
using Ashveil.World;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Wachtfeuer v4 – „ein Ort, der zusammengehalten wird“ (Entwurf Claude, Plan: claude/kareth_v4/lager.py).
    /// 05.10. (Arbeitsregel „Sinn und Gelände“): Gebäude auf Fundamenten aus den Daten (boden/fuss/fundament) statt auf
    /// Tellern – Sockel bis zum tiefsten Geländepunkt, Hanghaus, Untergeschoss mit Kellertür, Stufen vor Türen, Schuppen
    /// am Hang auf Pfosten. Feuerrunde, Zelte, Übungsplatz, Holzstapel, Waschplatz, Furt, Steg, Brücke und Felsstirn als
    /// einzelne Spuren mit Sinn (Python: claude/kareth_v4/integration/ausstattung.py).
    /// Zwei Höfe am Bergfuß (unten y = 0, oben y = +3) mit Stützmauer und Treppen; Palisade auf Steinsockel folgt dem
    /// Gelände; Tore nach WORLD-075; Gebäude mit Steinsockel, Fachwerk und Schindeldach (prozedurale Greybox, später
    /// durch Modelle ersetzbar). Vorhandene Modelle (Lagerfeuer, Zelte, Kisten, Fässer, Stämme, Palisade) werden genutzt.
    /// </summary>
    public static class KarethV4Camp
    {
        private static Material _stone, _stoneDark, _wood, _woodDark, _plaster, _roof, _roofHall, _cloth1, _cloth2, _cloth3,
            _glow, _fire, _iron, _straw, _woodOld, _woodNew, _soot, _soil, _green, _moss, _linen, _rope, _water, _track;
        private static Transform _root;
        private static int _pieces;

        private static void Materials()
        {
            _stone = K.Mat("KV4 Stein", new Color(0.47f, 0.45f, 0.42f));
            _stoneDark = K.Mat("KV4 Stein dunkel", new Color(0.33f, 0.32f, 0.3f));
            _wood = K.Mat("KV4 Holz", new Color(0.45f, 0.33f, 0.22f));
            _woodDark = K.Mat("KV4 Holz dunkel", new Color(0.26f, 0.19f, 0.13f));
            _plaster = K.Mat("KV4 Lehmputz", new Color(0.72f, 0.64f, 0.5f));
            _roof = K.Mat("KV4 Schindeln", new Color(0.3f, 0.27f, 0.25f));
            _roofHall = K.Mat("KV4 Schindeln Halle", new Color(0.36f, 0.25f, 0.2f));
            _cloth1 = K.Mat("KV4 Tuch Rost", new Color(0.55f, 0.27f, 0.17f));
            _cloth2 = K.Mat("KV4 Tuch Ocker", new Color(0.68f, 0.55f, 0.3f));
            _cloth3 = K.Mat("KV4 Tuch Grau", new Color(0.5f, 0.5f, 0.47f));
            _glow = K.Mat("KV4 Fensterlicht", new Color(0.35f, 0.25f, 0.12f), 0.1f, new Color(1f, 0.55f, 0.2f) * 0.8f);
            _fire = K.Mat("KV4 Glut", new Color(0.4f, 0.12f, 0.03f), 0.1f, new Color(1f, 0.4f, 0.1f) * 2.5f);
            _iron = K.Mat("KV4 Eisen", new Color(0.2f, 0.2f, 0.21f), 0.35f);
            _straw = K.Mat("KV4 Stroh", new Color(0.66f, 0.58f, 0.36f));
            _woodOld = K.Mat("KV4 Holz alt (vergraut)", new Color(0.36f, 0.33f, 0.29f));
            _woodNew = K.Mat("KV4 Holz frisch", new Color(0.66f, 0.5f, 0.32f));
            _soot = K.Mat("KV4 Ruß", new Color(0.08f, 0.07f, 0.065f));
            _soil = K.Mat("KV4 Beeterde", new Color(0.24f, 0.17f, 0.11f));
            _green = K.Mat("KV4 Beetgrün", new Color(0.28f, 0.42f, 0.18f));
            _moss = K.Mat("KV4 Stein bemoost", new Color(0.36f, 0.4f, 0.3f));
            _linen = K.Mat("KV4 Leinen", new Color(0.82f, 0.78f, 0.68f));
            _rope = K.Mat("KV4 Seil", new Color(0.55f, 0.47f, 0.33f));
            _water = K.Mat("KV4 Bachwasser", new Color(0.24f, 0.36f, 0.4f), 0.85f);
            _track = K.Mat("KV4 Fahrspur", new Color(0.3f, 0.24f, 0.18f));
        }

        private static float G(float x, float z) => KarethV4Builder.Ground(x, z);
        private static Vector3 P(float x, float z, float dy = 0f) => new Vector3(x, G(x, z) + dy, z);

        public static string Build(KarethV4Plan plan, Transform parent)
        {
            Materials();
            _pieces = 0;
            KarethV4Lager l = plan.lager;
            _root = K.Group("Wachtfeuer v4 (Lager)", parent);
            int stakes = Walls(l);
            Gates(l);
            RetainingWall(l);
            Lookout(l.ausguck);
            foreach (KarethV4Gebaeude g in l.gebaeude) Building(g, l);
            foreach (KarethV4Bereich b in l.bereiche) Area(b, l);
            int traces = 0;
            if (l.spuren != null) foreach (KarethV4Spur sp in l.spuren) { Trace(sp, l); traces++; }
            var spawn = new GameObject("Spawn (Entwurf)");
            spawn.transform.SetParent(_root, false);
            spawn.transform.position = P(l.spawn[0], l.spawn[1], 0.05f);
            spawn.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            string result = $"{l.gebaeude.Length} Gebäude, {stakes} Wandteile, {l.tore.Length} Tore, {traces} Spuren";
            // Siedlungen außerhalb des Lagers (Untersteg …): gleiche Bausteine, Höhe aus dem Gelände
            if (plan.siedlungen != null)
                foreach (KarethV4Siedlung s in plan.siedlungen)
                {
                    _root = K.Group(s.name, parent);
                    if (s.gebaeude != null) foreach (KarethV4Gebaeude g in s.gebaeude) Building(g, null);
                    if (s.bereiche != null) foreach (KarethV4Bereich b in s.bereiche) Area(b, null);
                    if (s.spuren != null) foreach (KarethV4Spur sp in s.spuren) Trace(sp, null);
                    result += $"; {s.name}: {s.gebaeude?.Length ?? 0} Gebäude, {s.spuren?.Length ?? 0} Spuren";
                }
            // Idylle um Untersteg (WORLD-244, Max 10.10.2026: Feldmark, Heuwiesen, Wassermühle): Beidatei KarethV4_Idylle.json
            KarethV4IdylleDaten idy = KarethV4Idylle.Load();
            if (idy != null)
            {
                _root = K.Group(string.IsNullOrEmpty(idy.name) ? "Idylle Untersteg" : idy.name, parent);
                if (idy.gebaeude != null) foreach (KarethV4Gebaeude g in idy.gebaeude) Building(g, null);
                if (idy.spuren != null) foreach (KarethV4Spur sp in idy.spuren) Trace(sp, null);
                result += $"; Idylle: {idy.gebaeude?.Length ?? 0} Gebäude, {idy.spuren?.Length ?? 0} Spuren";
            }
            return result + $", {_pieces} Teile";
        }

        // ================================================================== Wände: Trockenmauer (alt), Palisade in drei Altern

        private static int Walls(KarethV4Lager l)
        {
            Transform t = K.Group("Mauern und Palisade (drei Bauphasen)", _root);
            var rng = new System.Random(4242);
            int n = 0;
            foreach (KarethV4Wand w in l.waende)
            {
                Transform part = K.Group($"{w.art} ({w.alter})", t);
                for (int i = 0; i + 3 < w.punkte.Length; i += 2)
                {
                    Vector2 a = new Vector2(w.punkte[i], w.punkte[i + 1]), b = new Vector2(w.punkte[i + 2], w.punkte[i + 3]), d = b - a;
                    float len = d.magnitude, heading = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                    const float piece = 2.35f;
                    int cnt = Mathf.Max(1, Mathf.RoundToInt(len / piece));
                    for (int k = 0; k < cnt; k++)
                    {
                        Vector2 c = a + d * ((k + 0.5f) / cnt);
                        if (NearGate(l, c, 1.0f) || Vector2.Distance(c, new Vector2(l.ausguck.x, l.ausguck.z)) < 3.4f) continue;
                        float seg = len / cnt + 0.1f;
                        // tiefster Punkt unter dem Wandstück (Enden und quer), damit am Hang nichts schwebt
                        Vector2 dn = d.normalized, qn = new Vector2(-dn.y, dn.x);
                        float y = Mathf.Min(Mathf.Min(G(c.x, c.y), Mathf.Min(G(c.x + dn.x * seg * 0.5f, c.y + dn.y * seg * 0.5f), G(c.x - dn.x * seg * 0.5f, c.y - dn.y * seg * 0.5f))),
                                            Mathf.Min(G(c.x + qn.x * 0.6f, c.y + qn.y * 0.6f), G(c.x - qn.x * 0.6f, c.y - qn.y * 0.6f))) - 0.05f;
                        if (w.art == "trockenmauer") DryStone(part, c, y, seg, heading, w.alter, rng);
                        else Stakes(part, c, y, seg, heading, w.alter, rng);
                        n++;
                    }
                    if (!NearGate(l, a, 0.6f)) K.Block("Eckpfosten", part, P(a.x, a.y, -0.3f), new Vector3(0.6f, 3.4f, 0.6f), w.alter == "neu" ? _woodNew : _woodDark);
                }
            }
            _pieces += n * 3;
            return n;
        }

        /// <summary>Trockenmauer aus Lesesteinen: unregelmäßige Lagen; „eingestürzt“ = Schuttkegel mit Brettern geflickt.</summary>
        private static void DryStone(Transform t, Vector2 c, float y, float seg, float heading, string alter, System.Random rng)
        {
            Quaternion rot = Quaternion.Euler(0f, heading - 90f, 0f);
            Vector3 basePos = new Vector3(c.x, y, c.y);
            if (alter == "eingestuerzt")
            {
                for (int i = 0; i < 7; i++)
                {
                    Vector3 o = rot * new Vector3(((float)rng.NextDouble() - 0.5f) * seg, 0f, ((float)rng.NextDouble() - 0.5f) * 2.4f);
                    float s = 0.5f + (float)rng.NextDouble() * 0.6f;
                    K.Box("Schutt", t, basePos + o + Vector3.up * s * 0.3f, new Vector3(s * 1.3f, s * 0.7f, s), rng.NextDouble() < 0.4 ? _moss : _stone,
                        new Vector3((float)rng.NextDouble() * 20f, (float)rng.NextDouble() * 180f, (float)rng.NextDouble() * 20f));
                }
                // Notflicken: Bretter an Pfosten (jemand hat die Lücke geschlossen, nicht neu gemauert)
                for (int s = -1; s <= 1; s += 2)
                    K.Block("Flickpfosten", t, basePos + rot * new Vector3(s * seg * 0.45f, -0.3f, 0.6f), new Vector3(0.2f, 2.4f, 0.2f), _woodOld);
                for (int r = 0; r < 4; r++)
                    K.Box("Flickbrett", t, basePos + rot * new Vector3(0f, 0.5f + r * 0.48f, 0.72f), new Vector3(seg, 0.38f, 0.06f), r % 2 == 0 ? _woodOld : _wood,
                        rot.eulerAngles + new Vector3(0f, 0f, (r - 1.5f) * 2.5f), true);
                return;
            }
            float layerY = -0.2f;
            for (int layer = 0; layer < 5; layer++)
            {
                float h = 0.42f + (float)rng.NextDouble() * 0.12f, x = -seg * 0.5f;
                while (x < seg * 0.5f)
                {
                    float w = Mathf.Min(0.5f + (float)rng.NextDouble() * 0.7f, seg * 0.5f - x + 0.05f);
                    Vector3 local = new Vector3(x + w * 0.5f, layerY + h * 0.5f, ((float)rng.NextDouble() - 0.5f) * 0.12f);
                    float depth = 1.2f - layer * 0.08f;
                    K.Box("Stein", t, basePos + rot * local, new Vector3(w, h, depth), layer == 0 || rng.NextDouble() < 0.25 ? _moss : _stone,
                        rot.eulerAngles + new Vector3(0f, ((float)rng.NextDouble() - 0.5f) * 6f, ((float)rng.NextDouble() - 0.5f) * 4f), layer == 0);
                    x += w + 0.04f;
                }
                layerY += h;
            }
            // Mauerkrone mit Grasnarbe
            K.Box("Krone", t, basePos + rot * new Vector3(0f, layerY + 0.12f, 0f), new Vector3(seg, 0.24f, 0.95f), _moss, rot.eulerAngles, false);
            // Kollision als ein Block (keine Lücken für die Spielfigur)
            var col = new GameObject("Mauerkollision");
            col.transform.SetParent(t, false);
            col.transform.position = basePos + Vector3.up * (layerY * 0.5f);
            col.transform.rotation = rot;
            col.AddComponent<BoxCollider>().size = new Vector3(seg, layerY + 0.4f, 1.2f);
        }

        /// <summary>Palisade: alt = vergraut und schief, mittel = Standard, neu = frisches helles Holz nach der Brandnacht.</summary>
        private static void Stakes(Transform t, Vector2 c, float y, float seg, float heading, string alter, System.Random rng)
        {
            Quaternion rot = Quaternion.Euler(0f, heading - 90f, 0f);
            Vector3 basePos = new Vector3(c.x, y, c.y);
            K.Box("Sockel", t, basePos + Vector3.up * 0.1f, new Vector3(seg + 0.1f, 0.9f, 1.1f), alter == "neu" ? _stone : _moss, rot.eulerAngles);
            if (alter == "mittel")
            {
                var stake = ArtModels.Place("SM_Palisade", t, basePos + Vector3.up * 0.5f, heading - 90f, 0.52f, ArtModels.ColliderKind.Box, true);
                if (stake != null) { stake.transform.localScale = new Vector3(0.52f, 1f, 1f); return; }
            }
            Material m = alter == "alt" ? _woodOld : alter == "neu" ? _woodNew : _woodDark;
            int count = Mathf.Max(4, Mathf.RoundToInt(seg / 0.3f));
            for (int i = 0; i < count; i++)
            {
                float x = -seg * 0.5f + (i + 0.5f) * seg / count;
                float h = (alter == "alt" ? 2.4f : 2.9f) + ((float)rng.NextDouble() - 0.5f) * (alter == "neu" ? 0.15f : 0.6f);
                float lean = alter == "alt" ? ((float)rng.NextDouble() - 0.3f) * 6f : 0f;
                K.Prim(PrimitiveType.Cylinder, "Pfahl", t, basePos + rot * new Vector3(x, 0.4f + h * 0.5f, 0f), new Vector3(0.28f, h * 0.5f, 0.28f),
                    rot.eulerAngles + new Vector3(lean, 0f, ((float)rng.NextDouble() - 0.5f) * (alter == "alt" ? 5f : 1f)), m, false);
            }
            K.Box("Querriegel", t, basePos + rot * new Vector3(0f, 1.6f, -0.2f), new Vector3(seg, 0.18f, 0.12f), m, rot.eulerAngles, false);
            var col = new GameObject("Palisadenkollision");
            col.transform.SetParent(t, false);
            col.transform.position = basePos + Vector3.up * 1.6f;
            col.transform.rotation = rot;
            col.AddComponent<BoxCollider>().size = new Vector3(seg, 3.2f, 0.5f);
        }

        private static bool NearGate(KarethV4Lager l, Vector2 c, float extra)
        {
            foreach (KarethV4Tor g in l.tore)
                if (Vector2.Distance(c, new Vector2(g.x, g.z)) < g.breite * 0.5f + extra) return true;
            return false;
        }

        /// <summary>Richtung der Wand am Tor (aus dem nächsten Wandabschnitt).</summary>
        private static float WallHeadingAt(KarethV4Lager l, Vector2 p)
        {
            float best = 1e9f, heading = 0f;
            foreach (KarethV4Wand w in l.waende)
                for (int i = 0; i + 3 < w.punkte.Length; i += 2)
                {
                    Vector2 a = new Vector2(w.punkte[i], w.punkte[i + 1]), b = new Vector2(w.punkte[i + 2], w.punkte[i + 3]), d = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
                    float dist = Vector2.Distance(p, a + d * t);
                    if (dist < best) { best = dist; heading = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; }
                }
            return heading;
        }

        // ================================================================== Tore

        private static void Gates(KarethV4Lager l)
        {
            Transform t = K.Group("Tore", _root);
            foreach (KarethV4Tor g in l.tore)
            {
                float heading = WallHeadingAt(l, new Vector2(g.x, g.z));
                float y = G(g.x, g.z);
                Transform gate = K.Group(g.name, t, new Vector3(g.x, y, g.z), new Vector3(0f, heading - 90f, 0f));
                float half = g.breite * 0.5f, h = g.tuerme ? 5.4f : 4.4f;
                if (g.art == "stein")
                {
                    // altes Steintor in der Trockenmauer: zwei Wangen, ein Sturzstein, ausgetretene Schwelle
                    for (int s = -1; s <= 1; s += 2)
                        K.Block("Torwange", gate, new Vector3(s * (half + 0.6f), -0.3f, 0f), new Vector3(1.2f, 3.3f, 1.4f), _moss);
                    K.Box("Sturzstein", gate, new Vector3(0f, 3.15f, 0f), new Vector3(g.breite + 2.6f, 0.6f, 1.3f), _stone);
                    K.Box("Schwelle", gate, new Vector3(0f, -0.08f, 0f), new Vector3(g.breite, 0.2f, 1.4f), _stoneDark, default, false);
                    _pieces += 4;
                    continue;
                }
                for (int s = -1; s <= 1; s += 2)
                {
                    K.Block("Torpfosten", gate, new Vector3(s * (half + 0.3f), -0.3f, 0f), new Vector3(0.6f, h, 0.6f), _woodDark);
                    K.Block("Sockelstein", gate, new Vector3(s * (half + 0.3f), -0.4f, 0f), new Vector3(1.3f, 1.2f, 1.3f), _stoneDark);
                    // offener Torflügel, nach innen geschwenkt
                    K.Box("Torflügel", gate, new Vector3(s * (half - 0.1f) * 0.55f + s * 0.4f, 1.5f, -half * 0.6f),
                        new Vector3(0.18f, 2.8f, half * 1.05f), _wood, new Vector3(0f, s * 18f, 0f));
                }
                K.Box("Querbalken", gate, new Vector3(0f, h - 0.6f, 0f), new Vector3(g.breite + 1.8f, 0.45f, 0.5f), _woodDark);
                K.Box("Torgiebel", gate, new Vector3(0f, h - 0.1f, 0f), new Vector3(g.breite + 2.2f, 0.18f, 1.6f), _roof, new Vector3(0f, 0f, 0f), false);
                K.PointLight("Torlaterne", gate, new Vector3(half + 0.8f, 3.2f, 0.6f), new Color(1f, 0.62f, 0.3f), 10f, 1.4f, true);
                if (!string.IsNullOrEmpty(g.schild))
                {
                    K.Box("Schild", gate, new Vector3(0f, h - 1.25f, 0.32f), new Vector3(3.0f, 0.75f, 0.1f), _wood, default, false);
                    K.Label(gate, new Vector3(0f, h + 1.2f, 0f), g.schild, "", WorldLabelStyle.Place, 70f, 1f);
                }
                if (g.tuerme)
                    for (int s = -1; s <= 1; s += 2) Tower(gate, new Vector3(s * (half + 3.2f), 0f, -1.6f), 6.2f);
                _pieces += 10;
            }
        }

        private static void Tower(Transform parent, Vector3 local, float h)
        {
            Transform t = K.Group("Wachturm", parent, local, Vector3.zero);
            const float half = 1.4f;
            K.Block("Steinfuß", t, new Vector3(0f, -0.6f, 0f), new Vector3(3.4f, 1.6f, 3.4f), _stone);
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
                K.Block("Pfosten", t, new Vector3(ix * half, 0.8f, iz * half), new Vector3(0.32f, h + 1.6f, 0.32f), _woodDark);
            K.Box("Plattform", t, new Vector3(0f, h, 0f), new Vector3(3.4f, 0.22f, 3.4f), _wood);
            for (int s = 0; s < 4; s++)
            {
                Quaternion r = Quaternion.Euler(0f, s * 90f, 0f);
                K.Box("Brüstung", t, r * new Vector3(0f, h + 0.55f, half + 0.2f), new Vector3(3.4f, 0.9f, 0.12f), _woodDark, new Vector3(0f, s * 90f, 0f), false);
            }
            Roof(t, new Vector3(0f, h + 2.2f, 0f), 3.9f, 3.9f, 0f, 1.3f, _roof);
            K.PointLight("Turmlaterne", t, new Vector3(0f, h + 1.4f, 0f), new Color(1f, 0.6f, 0.28f), 9f, 1.3f, true);
        }

        // ================================================================== Stützmauer und Treppen

        private static void RetainingWall(KarethV4Lager l)
        {
            Transform t = K.Group("Stützmauer zwischen den Höfen", _root);
            for (int i = 0; i + 3 < l.stuetzmauer.Length; i += 2)
            {
                Vector2 a = new Vector2(l.stuetzmauer[i], l.stuetzmauer[i + 1]);
                Vector2 b = new Vector2(l.stuetzmauer[i + 2], l.stuetzmauer[i + 3]);
                Vector2 mid = (a + b) * 0.5f, d = b - a;
                bool stair = false;
                foreach (float sx in l.treppen) if (Mathf.Abs(mid.x - sx) < 2.6f) stair = true;
                float heading = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                // Mauerkörper deckt die Geländestufe ab: Mitte 1,3 m bergseitig der Kante.
                // 05.10.: Der Unterhof ist natürlicher Boden (nicht überall y = 0) – Fuß und Treppe auf die echte Höhe.
                Vector2 c = mid + new Vector2(0f, -1.3f);
                float unten = Mathf.Min(G(mid.x, mid.y + 1.6f), Mathf.Min(G(a.x, a.y + 1.6f), G(b.x, b.y + 1.6f)));
                float oben = Mathf.Max(G(mid.x, mid.y - 2.6f), l.obenY);
                if (stair)
                {
                    // Treppe: Rampe (begehbar) + sichtbare Stufen, Höhe = echter Unterschied der Höfe
                    float hub = oben - unten;
                    float winkel = Mathf.Clamp(Mathf.Atan2(hub, 4.4f) * Mathf.Rad2Deg, 20f, 40f);
                    float laenge = hub / Mathf.Sin(winkel * Mathf.Deg2Rad);
                    Vector3 low = new Vector3(mid.x, unten, mid.y + 1.8f);
                    K.Ramp("Treppe", t, low, 180f, winkel, laenge, 3.0f, _stone);
                    int stufen = Mathf.Max(3, Mathf.RoundToInt(hub / 0.32f));
                    float tief = laenge * Mathf.Cos(winkel * Mathf.Deg2Rad) / stufen;
                    for (int s = 0; s < stufen; s++)
                        K.Box("Stufe", t, new Vector3(mid.x, unten + (s + 0.5f) * hub / stufen, mid.y + 1.8f - (s + 0.5f) * tief), new Vector3(3.0f, 0.12f, tief * 0.9f), _stoneDark, default, false);
                    for (int sd = -1; sd <= 1; sd += 2)
                        K.Block("Wange", t, new Vector3(mid.x + sd * 1.75f, unten - 0.3f, c.y), new Vector3(0.5f, hub + 1.4f, 3.4f), _stone);
                    continue;
                }
                K.Box("Mauer", t, new Vector3(c.x, (unten + oben) * 0.5f + 0.15f, c.y), new Vector3(d.magnitude + 0.15f, oben - unten + 1.3f, 2.8f), _stone,
                    new Vector3(0f, heading - 90f, 0f));
                // Sichtseite (Norden, unterer Hof): Steinlagen mit Versatz, ein jüngerer Flicken in hellerem Stein,
                // Wasserlöcher zum Ablaufen des Hangwassers, Moos am Fuß – die Mauer wurde gebaut und gepflegt.
                {
                    var rw = new System.Random(Mathf.RoundToInt(mid.x * 13f));
                    Quaternion rq = Quaternion.Euler(0f, heading - 90f, 0f);
                    Vector3 face = new Vector3(c.x, 0f, c.y) + rq * new Vector3(0f, 0f, 0f);
                    float len = d.magnitude, wallH = oben - unten + 0.9f;
                    Vector3 fwd = rq * Vector3.forward, right = rq * Vector3.right;
                    if (Vector3.Dot(fwd, Vector3.forward) < 0f) fwd = -fwd;      // Sichtseite zeigt nach Norden
                    bool patch = rw.NextDouble() < 0.35;
                    float patchX = R(rw, -len * 0.3f, len * 0.3f);
                    for (float yy = unten - 0.1f; yy < unten + wallH - 0.2f; yy += 0.5f)
                    {
                        float off = rw.NextDouble() < 0.5 ? 0f : 0.35f;
                        for (float xx = -len * 0.5f + off; xx < len * 0.5f - 0.2f; )
                        {
                            float sw = R(rw, 0.55f, 1.15f);
                            float cx = Mathf.Min(xx + sw * 0.5f, len * 0.5f - 0.2f);
                            bool inPatch = patch && Mathf.Abs(cx - patchX) < 1.4f && yy > unten + 0.6f;
                            K.Box(inPatch ? "Flickstein (jünger)" : "Mauerstein", t, face + right * cx + fwd * 1.42f + Vector3.up * (yy + 0.22f),
                                new Vector3(sw - 0.06f, 0.44f, 0.12f), inPatch ? _stone : (yy < unten + 0.4f ? _moss : _stoneDark),
                                new Vector3(0f, heading - 90f, 0f), false);
                            xx += sw;
                        }
                    }
                    for (float xx = -len * 0.5f + 1.5f; xx < len * 0.5f; xx += 4.5f)
                        K.Box("Wasserloch", t, face + right * xx + fwd * 1.5f + Vector3.up * (unten + 0.55f), new Vector3(0.22f, 0.22f, 0.06f), _soot, new Vector3(0f, heading - 90f, 0f), false);
                    _pieces += 30;
                }
                K.Box("Mauerkrone", t, new Vector3(c.x, oben + 0.55f, c.y + 1.0f), new Vector3(d.magnitude + 0.15f, 0.5f, 0.6f), _stoneDark,
                    new Vector3(0f, heading - 90f, 0f));
                _pieces += 2;
            }
        }

        // ================================================================== Ausguck (Teil der Mauer, auf der Felsnase)

        private static void Lookout(KarethV4Ausguck a)
        {
            Transform t = K.Group("Ausguck", _root, new Vector3(a.x, G(a.x, a.z), a.z), Vector3.zero);
            K.Block("Turmschaft", t, new Vector3(0f, -1.2f, 0f), new Vector3(5.2f, a.hoehe + 1.2f, 5.2f), _stone);
            K.Box("Wehrgang", t, new Vector3(0f, a.hoehe + 0.1f, 0f), new Vector3(6.4f, 0.3f, 6.4f), _wood);
            for (int s = 0; s < 4; s++)
            {
                Quaternion r = Quaternion.Euler(0f, s * 90f, 0f);
                K.Box("Brüstung", t, r * new Vector3(0f, a.hoehe + 0.75f, 3.1f), new Vector3(6.4f, 1.0f, 0.3f), _stone, new Vector3(0f, s * 90f, 0f), false);
            }
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
                K.Block("Dachpfosten", t, new Vector3(ix * 2.8f, a.hoehe + 0.2f, iz * 2.8f), new Vector3(0.28f, 2.6f, 0.28f), _woodDark);
            Roof(t, new Vector3(0f, a.hoehe + 2.8f, 0f), 7.2f, 7.2f, 0f, 2.0f, _roof);
            K.PointLight("Signalfeuer", t, new Vector3(0f, a.hoehe + 1.2f, 0f), new Color(1f, 0.55f, 0.22f), 16f, 2f, true);
        }

        // ================================================================== Gebäude (Fundament, Steinsockel, Fachwerk, Schindeldach)
        // Seit 05.10. aus den Daten (fundamente.py): boden = Fußboden (Oberkante Sockel), fuss = tiefster Geländepunkt unter
        // dem Grundriss, fundament = eben | hang | untergeschoss | fels. Die Gruppe steht auf boden − sockel. Sockel und
        // Pfeiler reichen bis 0,35 m unter das Gelände (nichts schwebt am Hang), Stufen und Rampen führen vom Boden vor der
        // Tür hinauf, die Talseite hoher Sockel bekommt eine Tür (Keller, Untergeschoss).

        /// <summary>Geländehöhe unter einem lokalen Punkt der Gruppe, als lokale Höhe.</summary>
        private static float GL(Transform b, float x, float z)
        {
            Vector3 w = b.TransformPoint(new Vector3(x, 0f, z));
            return G(w.x, w.z) - b.position.y;
        }

        /// <summary>Dünner Balken/Seil zwischen zwei lokalen Punkten.</summary>
        private static GameObject Seil(Transform parent, Vector3 a, Vector3 b, float dicke, Material m, string name = "Seil", bool collider = false)
        {
            Vector3 d = b - a;
            GameObject go = K.Box(name, parent, (a + b) * 0.5f, new Vector3(dicke, dicke, Mathf.Max(0.01f, d.magnitude)), m, default, collider);
            if (d.sqrMagnitude > 1e-6f) go.transform.localRotation = Quaternion.LookRotation(d);
            return go;
        }

        private static void Building(KarethV4Gebaeude g, KarethV4Lager l)
        {
            float w = g.w, d = g.d, h = g.wand, s = g.sockel;
            bool daten = !string.IsNullOrEmpty(g.fundament);
            float y;
            if (daten) y = g.boden - s;                                  // Fußboden = boden
            else if (l != null) y = Mathf.Min(g.oben ? l.obenY : l.untenY, G(g.x, g.z));
            else
            {
                // alte Daten: tiefste Ecke des Grundrisses, der Sockel gleicht den Hang aus
                float r1 = g.w * 0.5f, r2 = g.d * 0.5f;
                y = Mathf.Min(Mathf.Min(G(g.x - r1, g.z - r2), G(g.x + r1, g.z - r2)), Mathf.Min(G(g.x - r1, g.z + r2), G(g.x + r1, g.z + r2)));
                y = Mathf.Min(y, G(g.x, g.z)) - 0.2f;
            }
            // yaw im Plan = Blickrichtung der Vorderseite; die Tür liegt lokal bei −z, daher +180°
            Transform b = K.Group(g.name, _root, new Vector3(g.x, y, g.z), new Vector3(0f, g.yaw + 180f, 0f));
            // Unterkante aller Fundamente (lokal): 0,35 m unter dem tiefsten Geländepunkt
            float unten = Mathf.Min(-0.6f, (daten ? g.fuss : y - 0.4f) - 0.35f - y);
            switch (g.typ)
            {
                case "felshalle":
                    FelsHalle(b, w, d, s, h);
                    break;
                case "speicher":
                    // hoch belüftet: auf Steinpfeilern (jeder bis unter seinen eigenen Boden) mit Mäuseplatten, Lattenwände
                    for (int ix = -1; ix <= 1; ix++)
                    for (int iz = -1; iz <= 1; iz += 2)
                    {
                        float px = ix * (w * 0.5f - 0.4f), pz = iz * (d * 0.5f - 0.4f), yb = Mathf.Min(GL(b, px, pz) - 0.35f, s - 0.6f);
                        K.Block("Steinpfeiler", b, new Vector3(px, yb, pz), new Vector3(0.8f, s - 0.1f - yb, 0.8f), _stone);
                        K.Box("Mäuseplatte", b, new Vector3(px, s - 0.06f, pz), new Vector3(1.05f, 0.1f, 1.05f), _stoneDark, default, false);
                    }
                    K.Box("Boden", b, new Vector3(0f, s + 0.1f, 0f), new Vector3(w, 0.25f, d), _woodDark);
                    for (float x = -w * 0.5f + 0.3f; x <= w * 0.5f - 0.2f; x += 0.42f)
                    {
                        K.Box("Latte", b, new Vector3(x, s + h * 0.5f, d * 0.5f - 0.08f), new Vector3(0.22f, h - 0.4f, 0.1f), _wood, default, false);
                        K.Box("Latte", b, new Vector3(x, s + h * 0.5f, -d * 0.5f + 0.08f), new Vector3(0.22f, h - 0.4f, 0.1f), _wood, default, false);
                    }
                    K.Box("Kern", b, new Vector3(0f, s + h * 0.5f, 0f), new Vector3(w - 0.4f, h - 0.4f, d - 0.4f), _woodDark);
                    Frame(b, w, d, s, h);
                    Roof(b, new Vector3(0f, s + h, 0f), w + 1.0f, d + 1.0f, 0f, d * 0.42f, _roof);
                    // Zugang: Holztreppe (steil, ohne Setzstufen – Mäuse kommen nicht hinauf, wenn man sie wegzieht)
                    Zugang(b, -d * 0.5f, s + 0.22f, 1.2f, "treppe");
                    break;
                case "schuppen":
                    // ab 0,6 m freier Höhe am Hang auf Pfosten (Fundamente §3), sonst auf einem Steinsockel
                    bool pfosten = daten && g.boden - g.fuss > 0.6f;
                    if (pfosten) Pfostenbau(b, w, d, s);
                    else K.Block("Sockel", b, new Vector3(0f, unten, 0f), new Vector3(w + 0.3f, s - unten, d + 0.3f), _stone);
                    K.Box("Rückwand", b, new Vector3(0f, s + h * 0.5f, d * 0.5f - 0.15f), new Vector3(w, h, 0.3f), _wood);
                    K.Box("Seite L", b, new Vector3(-w * 0.5f + 0.15f, s + h * 0.5f, 0f), new Vector3(0.3f, h, d), _wood);
                    K.Box("Seite R", b, new Vector3(w * 0.5f - 0.15f, s + h * 0.5f, 0f), new Vector3(0.3f, h, d), _wood);
                    for (int i = -1; i <= 1; i += 2)
                        K.Block("Vorderpfosten", b, new Vector3(i * (w * 0.5f - 0.2f), s, -d * 0.5f + 0.2f), new Vector3(0.25f, h + 0.3f, 0.25f), _woodDark);
                    LeanRoof(b, s + h, w + 0.9f, d + 1.2f, _roof);
                    // Ladung an der Rückwand, Gang zur offenen Front bleibt frei
                    for (int i = 0; i < 3; i++)
                        ArtModels.Place(i % 2 == 0 ? "SM_Crate" : "SM_Barrel", b, new Vector3(-w * 0.3f + i * w * 0.3f, s, d * 0.5f - 0.9f), i * 25f, 1f, ArtModels.ColliderKind.Box);
                    // Karren und Schubkarren: Bohlenrampe statt Stufen
                    Zugang(b, pfosten ? -d * 0.5f - 0.05f : -d * 0.5f - 0.15f, s, Mathf.Min(2.6f, w - 1.2f), "rampe");
                    break;
                case "werkstatt":
                    // Schmiede: offene Halle auf Pfosten, Rückwand aus Stein, Esse mit Glut, Amboss
                    K.Block("Sockel", b, new Vector3(0f, unten, 0f), new Vector3(w + 0.3f, s - unten, d + 0.3f), _stone);
                    K.Box("Rückwand", b, new Vector3(0f, s + h * 0.5f, d * 0.5f - 0.3f), new Vector3(w, h, 0.6f), _stone);
                    for (int i = -1; i <= 1; i++)
                        K.Block("Pfosten", b, new Vector3(i * (w * 0.5f - 0.2f), s, -d * 0.5f + 0.2f), new Vector3(0.3f, h + 0.4f, 0.3f), _woodDark);
                    Roof(b, new Vector3(0f, s + h, 0f), w + 1.2f, d + 1.2f, 0f, d * 0.38f, _roof);
                    K.Block("Esse", b, new Vector3(w * 0.25f, s, d * 0.2f), new Vector3(1.8f, 1.0f, 1.6f), _stoneDark);
                    K.Box("Glut", b, new Vector3(w * 0.25f, s + 1.05f, d * 0.2f), new Vector3(1.0f, 0.15f, 0.9f), _fire, default, false);
                    K.Block("Kamin", b, new Vector3(w * 0.25f, s + 1.0f, d * 0.35f), new Vector3(0.9f, h + 2.4f, 0.9f), _stoneDark);
                    K.Particles("Rauch", b, new Vector3(w * 0.25f, s + h + 3.6f, d * 0.35f), new Color(0.3f, 0.3f, 0.32f, 0.35f), 1.4f, 4f, 1.1f, 6f, 8f, 0.25f);
                    K.PointLight("Esse", b, new Vector3(w * 0.25f, s + 1.6f, d * 0.1f), new Color(1f, 0.45f, 0.15f), 8f, 2.2f, true);
                    K.Block("Amboss", b, new Vector3(-w * 0.1f, s, -d * 0.1f), new Vector3(0.5f, 0.85f, 0.9f), _iron);
                    K.Block("Werkbank", b, new Vector3(-w * 0.32f, s, d * 0.25f), new Vector3(2.2f, 0.9f, 0.8f), _wood);
                    if (daten) Sockelseite(b, g, w, d, s);
                    // offene Front: Stufe über die ganze Breite
                    Zugang(b, -d * 0.5f - 0.15f, s, w - 0.6f, "stufen");
                    break;
                case "muehle":
                {
                    // Wassermühle (WORLD-244): Untergeschoss aus Bruchstein am Bach (Radseite lokal +x), darüber Fachwerk mit Lehmputz,
                    // steiles Schindeldach, Tür vorn (−z) mit Stufen, Mehlsäcke und ein ausgedienter Mühlstein vor der Tür. Das Rad
                    // steht als eigene Spur „muehlrad“ im Bach; hier nur die Wellbaum-Lagerung in der Wand.
                    K.Block("Steinsockel", b, new Vector3(0f, unten, 0f), new Vector3(w + 0.5f, s - unten, d + 0.5f), _stone);
                    K.Box("Untergeschoss", b, new Vector3(0f, s + 1.1f, 0f), new Vector3(w, 2.2f, d), _moss);
                    K.Box("Wände", b, new Vector3(0f, s + 2.2f + (h - 1.0f) * 0.5f, 0f), new Vector3(w - 0.1f, h - 1.0f, d - 0.1f), _plaster);
                    Frame(b, w - 0.1f, d - 0.1f, s + 2.2f, h - 1.0f);
                    K.Box("Tür", b, new Vector3(0f, s + 1.15f, -d * 0.5f - 0.06f), new Vector3(1.4f, 2.3f, 0.12f), _woodDark, default, false);
                    for (int i = -1; i <= 1; i += 2)
                        K.Box("Fenster", b, new Vector3(i * w * 0.28f, s + 2.2f + (h - 1.0f) * 0.55f, -d * 0.5f - 0.05f), new Vector3(0.8f, 0.7f, 0.08f), _glow, default, false);
                    K.Box("Fenster Radseite", b, new Vector3(w * 0.5f + 0.05f, s + 2.2f + (h - 1.0f) * 0.55f, 0f), new Vector3(0.08f, 0.7f, 0.8f), _glow, default, false);
                    Roof(b, new Vector3(0f, s + 2.2f + h - 1.0f, 0f), w + 1.2f, d + 1.4f, 0f, d * 0.5f, _roof);
                    K.Prim(PrimitiveType.Cylinder, "Wellbaum-Lager", b, new Vector3(w * 0.5f + 0.15f, s + 0.9f, 0f), new Vector3(0.7f, 0.25f, 0.7f), new Vector3(0f, 0f, 90f), _woodDark, false);
                    Zugang(b, -d * 0.5f - 0.2f, s, 1.8f, "stufen");
                    for (int i = 0; i < 4; i++)
                        K.Box("Mehlsack", b, new Vector3(-w * 0.32f + R(new System.Random(i + 7), -0.2f, 0.2f) + i * 0.55f, GL(b, -w * 0.3f + i * 0.55f, -d * 0.5f - 1.4f) + 0.32f, -d * 0.5f - 1.4f),
                            new Vector3(0.5f, 0.65f, 0.38f), _linen, new Vector3(0f, i * 23f, i == 3 ? 12f : 0f), false);
                    K.Prim(PrimitiveType.Cylinder, "alter Mühlstein", b, new Vector3(w * 0.28f, GL(b, w * 0.28f, -d * 0.5f - 1.2f) + 0.65f, -d * 0.5f - 1.2f),
                        new Vector3(1.3f, 0.16f, 1.3f), new Vector3(78f, 0f, 0f), _stoneDark);
                    _pieces += 16;
                    break;
                }
                default:      // haus, halle
                    bool hall = g.typ == "halle";
                    K.Block("Steinsockel", b, new Vector3(0f, unten, 0f), new Vector3(w + 0.4f, s - unten, d + 0.4f), _stone);
                    if (daten) Sockelseite(b, g, w, d, s);
                    K.Box("Wände", b, new Vector3(0f, s + h * 0.5f, 0f), new Vector3(w, h, d), _plaster);
                    Frame(b, w, d, s, h);
                    // Tür vorn (−z), Fenster mit warmem Licht
                    K.Box("Tür", b, new Vector3(0f, s + 1.15f, -d * 0.5f - 0.06f), new Vector3(hall ? 2.4f : 1.2f, 2.3f, 0.12f), _woodDark, default, false);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        K.Box("Fenster", b, new Vector3(i * w * 0.3f, s + h * 0.55f, -d * 0.5f - 0.05f), new Vector3(0.9f, 0.8f, 0.08f), _glow, default, false);
                        K.Box("Fenster", b, new Vector3(i * w * 0.3f, s + h * 0.55f, d * 0.5f + 0.05f), new Vector3(0.9f, 0.8f, 0.08f), _glow, default, false);
                    }
                    Roof(b, new Vector3(0f, s + h, 0f), w + 1.0f, d + 1.4f, 0f, d * (hall ? 0.48f : 0.42f), hall ? _roofHall : _roof);
                    Zugang(b, -d * 0.5f - 0.2f, s, hall ? 3.4f : 1.6f, "stufen");
                    if (hall)
                    {
                        // Vordach auf Pfosten (jeder auf seinem Boden), Banner
                        for (int i = -2; i <= 2; i++)
                        {
                            float px = i * w * 0.2f, pz = -d * 0.5f - 2.4f, yb = GL(b, px, pz) - 0.3f;
                            K.Block("Vordachpfosten", b, new Vector3(px, yb, pz), new Vector3(0.32f, s + h - 0.6f - yb, 0.32f), _woodDark);
                        }
                        K.Box("Vordach", b, new Vector3(0f, s + h - 0.3f, -d * 0.5f - 1.4f), new Vector3(w * 0.9f, 0.18f, 2.8f), _roofHall, new Vector3(-12f, 0f, 0f), false);
                        for (int i = -1; i <= 1; i += 2)
                            K.Box("Banner", b, new Vector3(i * 2.6f, s + 2.3f, -d * 0.5f - 0.1f), new Vector3(0.9f, 2.4f, 0.05f), _cloth1, default, false);
                        K.Block("Kamin", b, new Vector3(w * 0.32f, s + h - 1f, d * 0.2f), new Vector3(0.9f, d * 0.48f + 2.2f, 0.9f), _stoneDark);
                    }
                    else if (g.id == "kueche")
                    {
                        K.Block("Kamin", b, new Vector3(-w * 0.3f, s + h - 1f, 0f), new Vector3(0.8f, d * 0.42f + 2.2f, 0.8f), _stoneDark);
                        K.Particles("Rauch", b, new Vector3(-w * 0.3f, s + h + d * 0.42f + 1.6f, 0f), new Color(0.35f, 0.35f, 0.37f, 0.35f), 1.4f, 4f, 1.1f, 6f, 8f, 0.25f);
                    }
                    break;
            }
            _pieces += 12;
        }

        /// <summary>
        /// Zugang zur Front (lokal −z) vom Boden davor bis zum Fußboden: „stufen“ (Stein, Steigung ≤ 0,2 m), „rampe“
        /// (Bohlen, höchstens 1 : 4, für Karren) oder „treppe“ (steile Holztreppe, Speicher). zFront = lokale z der Kante,
        /// an der der Fußboden endet. Jede Stufe reicht bis unter ihr eigenes Gelände.
        /// </summary>
        private static void Zugang(Transform b, float zFront, float boden, float breite, string art)
        {
            float yg = GL(b, 0f, zFront - 0.7f);
            float hub = boden - yg;
            if (hub < 0.22f) return;
            if (art == "rampe")
            {
                float lauf = Mathf.Max(1.2f, hub * 4f);
                float yl = GL(b, 0f, zFront - lauf);
                float hub2 = boden - yl;
                if (hub2 < 0.1f) return;
                float ang = Mathf.Atan2(hub2, lauf) * Mathf.Rad2Deg, len = Mathf.Sqrt(hub2 * hub2 + lauf * lauf);
                K.Ramp("Bohlenrampe", b, new Vector3(0f, yl, zFront - lauf), 0f, ang, len, breite, _woodOld);
                for (int s2 = -1; s2 <= 1; s2 += 2)
                    Seil(b, new Vector3(s2 * breite * 0.5f, yl + 0.05f, zFront - lauf), new Vector3(s2 * breite * 0.5f, boden + 0.05f, zFront), 0.16f, _woodDark, "Rampenbalken");
                _pieces += 3;
                return;
            }
            if (art == "treppe")
            {
                float lauf = hub / Mathf.Tan(52f * Mathf.Deg2Rad);
                float yl = GL(b, 0f, zFront - lauf);
                float hub2 = boden - yl, len = Mathf.Sqrt(hub2 * hub2 + lauf * lauf);
                for (int s2 = -1; s2 <= 1; s2 += 2)
                    Seil(b, new Vector3(s2 * breite * 0.5f, yl, zFront - lauf), new Vector3(s2 * breite * 0.5f, boden, zFront), 0.12f, _woodDark, "Treppenwange");
                int n2 = Mathf.Max(2, Mathf.RoundToInt(hub2 / 0.26f));
                for (int k = 1; k < n2; k++)
                {
                    float u = k / (float)n2;
                    K.Box("Trittbrett", b, new Vector3(0f, yl + hub2 * u, zFront - lauf * (1f - u)), new Vector3(breite, 0.05f, 0.24f), _woodOld, default, false);
                }
                // begehbar: eine schräge Kollision
                var col = new GameObject("Treppe Kollision");
                col.transform.SetParent(b, false);
                col.transform.localPosition = new Vector3(0f, yl + hub2 * 0.5f, zFront - lauf * 0.5f);
                col.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(hub2, lauf) * Mathf.Rad2Deg, 0f, 0f);
                col.AddComponent<BoxCollider>().size = new Vector3(breite, 0.1f, len);
                _pieces += n2 + 2;
                return;
            }
            int n = Mathf.Max(2, Mathf.CeilToInt(hub / 0.2f));
            float auf = hub / n;
            for (int k = 1; k < n; k++)
            {
                float zc = zFront - (k - 0.5f) * 0.34f;
                float top = boden - k * auf;
                float yb = Mathf.Min(GL(b, -breite * 0.45f, zc), GL(b, breite * 0.45f, zc)) - 0.25f;
                if (top - yb < 0.08f) break;
                K.Block("Stufe", b, new Vector3(0f, yb, zc), new Vector3(breite, top - yb, 0.36f), _stoneDark);
                _pieces++;
            }
        }

        /// <summary>Schuppen am Hang auf Pfosten (ab 0,6 m freier Höhe): Pfähle auf Steinplatten, Schwellbalken, Bohlenboden.</summary>
        private static void Pfostenbau(Transform b, float w, float d, float s)
        {
            for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                if (ix == 0 && iz == 0) continue;
                float px = ix * (w * 0.5f - 0.15f), pz = iz * (d * 0.5f - 0.15f), yb = GL(b, px, pz);
                K.Box("Steinplatte", b, new Vector3(px, yb + 0.02f, pz), new Vector3(0.62f, 0.22f, 0.62f), _stone, default, false);
                K.Block("Pfahl", b, new Vector3(px, yb + 0.1f, pz), new Vector3(0.26f, Mathf.Max(0.2f, s - 0.32f - yb), 0.26f), _woodDark);
            }
            for (int iz = -1; iz <= 1; iz += 2)
                K.Box("Schwellbalken", b, new Vector3(0f, s - 0.32f, iz * (d * 0.5f - 0.15f)), new Vector3(w, 0.24f, 0.26f), _woodDark);
            for (int ix = -1; ix <= 1; ix += 2)
                K.Box("Schwellbalken", b, new Vector3(ix * (w * 0.5f - 0.15f), s - 0.32f, 0f), new Vector3(0.26f, 0.24f, d), _woodDark);
            K.Box("Bohlenboden", b, new Vector3(0f, s - 0.1f, 0f), new Vector3(w + 0.1f, 0.2f, d + 0.1f), _woodOld);
            _pieces += 14;
        }

        /// <summary>
        /// Talseite hoher Sockel: Hanghaus ab 1,85 m freier Höhe eine Kellertür, Haus mit Untergeschoss eine Tür (2 m), zwei
        /// Luken und ein Gurtgesims auf Fußbodenhöhe. Die Tür sitzt dort, wo das Gelände an der Wand am tiefsten ist.
        /// </summary>
        private static void Sockelseite(Transform b, KarethV4Gebaeude g, float w, float d, float s)
        {
            if (g.fundament != "hang" && g.fundament != "untergeschoss") return;
            bool ug = g.fundament == "untergeschoss";
            float best = 0f, rotY = 0f, laenge = 0f; Vector3 pos = Vector3.zero;
            for (int side = 0; side < 4; side++)
            {
                bool quer = side < 2;                                   // 0 vorn (−z), 1 hinten (+z), 2 links (−x), 3 rechts (+x)
                float half = quer ? w * 0.5f : d * 0.5f, off = (quer ? d * 0.5f : w * 0.5f) + 0.2f;
                float nx = quer ? 0f : (side == 2 ? -1f : 1f), nz = quer ? (side == 0 ? -1f : 1f) : 0f;
                for (float u = -half + 1.0f; u <= half - 1.0f + 1e-3f; u += 0.5f)
                {
                    float lx = quer ? u : nx * off, lz = quer ? nz * off : u;
                    float frei = s - GL(b, lx + nx * 0.6f, lz + nz * 0.6f);
                    if (frei > best) { best = frei; pos = new Vector3(lx, 0f, lz); rotY = side == 0 ? 180f : side == 1 ? 0f : side == 2 ? 270f : 90f; laenge = 2f * half; }
                }
            }
            float tuerH = ug ? 2.0f : 1.6f;
            if (best < tuerH + 0.25f) return;
            Quaternion q = Quaternion.Euler(0f, rotY, 0f);
            Vector3 n = q * Vector3.forward, quer2 = q * Vector3.right;
            float yg = GL(b, pos.x + n.x * 0.6f, pos.z + n.z * 0.6f);
            Vector3 tp = pos + n * 0.03f;
            K.Box(ug ? "Tür Untergeschoss" : "Kellertür", b, new Vector3(tp.x, yg + tuerH * 0.5f, tp.z), new Vector3(ug ? 1.4f : 1.0f, tuerH, 0.12f), _woodDark, new Vector3(0f, rotY, 0f), false);
            K.Box("Türsturz", b, new Vector3(tp.x, yg + tuerH + 0.1f, tp.z) + n * 0.02f, new Vector3(ug ? 1.9f : 1.4f, 0.2f, 0.16f), _stoneDark, new Vector3(0f, rotY, 0f), false);
            K.Prim(PrimitiveType.Cylinder, "ausgetretener Boden", b, new Vector3(pos.x + n.x * 1.2f, yg + 0.01f, pos.z + n.z * 1.2f), new Vector3(2.0f, 0.01f, 1.6f), new Vector3(0f, rotY, 0f), _soil, false);
            _pieces += 3;
            if (!ug) return;
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 lp = pos + quer2 * (i * Mathf.Min(laenge * 0.3f, 2.4f));
                float ygl = GL(b, lp.x + n.x * 0.6f, lp.z + n.z * 0.6f), ly = Mathf.Max(ygl + 1.25f, yg + 1.25f);
                if (ly + 0.25f < s - 0.25f && Mathf.Abs(Vector3.Dot(lp, quer2)) < laenge * 0.5f - 0.6f)
                    K.Box("Luke (vergittert)", b, new Vector3(lp.x, ly, lp.z) + n * 0.04f, new Vector3(0.6f, 0.4f, 0.08f), _soot, new Vector3(0f, rotY, 0f), false);
            }
            // Gurtgesims rundum auf Fußbodenhöhe: das Untergeschoss ist gemauert, darüber beginnt das Fachwerk
            K.Box("Gurtgesims", b, new Vector3(0f, s - 0.08f, -d * 0.5f - 0.28f), new Vector3(w + 0.56f, 0.16f, 0.16f), _stoneDark, default, false);
            K.Box("Gurtgesims", b, new Vector3(0f, s - 0.08f, d * 0.5f + 0.28f), new Vector3(w + 0.56f, 0.16f, 0.16f), _stoneDark, default, false);
            K.Box("Gurtgesims", b, new Vector3(-w * 0.5f - 0.28f, s - 0.08f, 0f), new Vector3(0.16f, 0.16f, d + 0.4f), _stoneDark, default, false);
            K.Box("Gurtgesims", b, new Vector3(w * 0.5f + 0.28f, s - 0.08f, 0f), new Vector3(0.16f, 0.16f, d + 0.4f), _stoneDark, default, false);
            _pieces += 6;
        }

        /// <summary>
        /// Halle des Hauptmanns, in die Felsstirn geschlagen (LORE-081): Holzfront 0,4 m tief in der Öffnung der behauenen
        /// Wand (Spur „felsstirn“), Stützbalken, Vordach, Stufen; dahinter nur Fels. Lokal −z = Front (zum Hof), +z = Fels.
        /// Der Rauch tritt aus einem Rauchloch oben im Fels aus.
        /// </summary>
        private static void FelsHalle(Transform b, float w, float d, float s, float h)
        {
            Zugang(b, -d * 0.5f - 0.15f, s, 6.5f, "stufen");
            K.Box("Schwelle", b, new Vector3(0f, s - 0.1f, -d * 0.5f + 0.05f), new Vector3(w - 1.2f, 0.2f, 0.4f), _stoneDark);
            // 13.10.2026 (Akt I „Das Register“): Die Halle ist begehbar – hinter dem offenen Tor liegen Torgang, Halle, Treppe und
            // Salzkeller im Fels (KarethV4Halle, Plan Data/KarethV4/KarethV4_Halle.json). Die dunkle Felsöffnung entfällt; die
            // Holzfront hat eine Toröffnung (2,6 × 3,0 m), die beiden Torflügel stehen nach innen offen.
            const float torB = 2.6f, torH = 3.0f;
            float frontB = w - 1f, seiteB = (frontB - torB) * 0.5f, zF = -d * 0.5f + 0.4f;
            for (int sx = -1; sx <= 1; sx += 2)
                K.Box("Holzfront", b, new Vector3(sx * (torB * 0.5f + seiteB * 0.5f), s + h * 0.45f, zF), new Vector3(seiteB, h * 0.9f, 0.35f), _wood);
            K.Box("Holzfront über dem Tor", b, new Vector3(0f, s + torH + (h * 0.9f - torH) * 0.5f, zF), new Vector3(torB, h * 0.9f - torH, 0.35f), _wood);
            for (int i = -3; i <= 3; i++)
                if (i != 0)   // der mittlere Ständer stünde im Tor
                    K.Box("Ständer", b, new Vector3(i * (w - 1.4f) / 6f, s + h * 0.45f, -d * 0.5f + 0.2f), new Vector3(0.32f, h * 0.9f, 0.3f), _woodDark, default, false);
            K.Box("Rähm", b, new Vector3(0f, s + h * 0.9f, -d * 0.5f + 0.15f), new Vector3(w - 1.0f, 0.35f, 0.4f), _woodDark, default, false);
            K.Box("Torsturz", b, new Vector3(0f, s + torH + 0.12f, -d * 0.5f + 0.18f), new Vector3(torB + 0.5f, 0.24f, 0.3f), _woodDark, default, false);
            for (int sx = -1; sx <= 1; sx += 2)
                K.Box("Torflügel (offen)", b, new Vector3(sx * (torB * 0.5f - 0.06f), s + torH * 0.5f, zF + 0.18f + 0.66f), new Vector3(0.1f, torH - 0.05f, 1.28f), _woodDark);
            for (int i = -1; i <= 1; i += 2)
            {
                K.Box("Fenster", b, new Vector3(i * w * 0.3f, s + h * 0.55f, -d * 0.5f + 0.17f), new Vector3(1.0f, 0.9f, 0.08f), _glow, default, false);
                K.Box("Banner", b, new Vector3(i * 2.4f, s + h * 0.62f, -d * 0.5f + 0.1f), new Vector3(0.9f, 2.6f, 0.05f), _cloth1, default, false);
            }
            // Vordach auf schweren Stützen (wie im Bergbau verkeilt), jede auf einem Steinfuß auf ihrem Boden
            for (int i = -2; i <= 2; i++)
            {
                float px = i * w * 0.2f, pz = -d * 0.5f - 3.0f, yb = GL(b, px, pz);
                K.Box("Steinfuß", b, new Vector3(px, yb + 0.1f, pz), new Vector3(0.7f, 0.4f, 0.7f), _stoneDark, default, false);
                K.Block("Stütze", b, new Vector3(px, yb + 0.25f, pz), new Vector3(0.4f, s + h * 0.8f - yb - 0.25f, 0.4f), _woodDark);
            }
            K.Box("Vordach", b, new Vector3(0f, s + h * 0.82f, -d * 0.5f - 1.6f), new Vector3(w * 0.95f, 0.2f, 3.6f), _roofHall, new Vector3(-10f, 0f, 0f), false);
            // 05.10. abends: Unter dem tiefen Vordach vor der Nordwand ist es auch am Tag dunkel – zwei Laternen an Armen
            // beiderseits des Tors brennen. Sie machen die Halle aus dem Hof lesbar (11 §X: Felsstirn sehr dunkel).
            // Ohne Schatten und ohne Flackern (keine Kosten je Bild auf der CPU).
            float zFront = -d * 0.5f + 0.22f;                 // Außenseite der Holzfront
            for (int i = -1; i <= 1; i += 2)
            {
                var lp = new Vector3(i * 1.8f, s + 3.25f, -d * 0.5f - 0.3f);
                K.Box("Laternenarm", b, new Vector3(lp.x, lp.y + 0.36f, (zFront + lp.z) * 0.5f), new Vector3(0.05f, 0.05f, Mathf.Abs(zFront - lp.z)), _iron, default, false);
                K.Box("Laternenhaken", b, lp + new Vector3(0f, 0.29f, 0f), new Vector3(0.04f, 0.14f, 0.04f), _iron, default, false);
                K.Box("Laterne", b, lp, new Vector3(0.24f, 0.36f, 0.24f), _glow, default, false);
                K.Box("Laternendach", b, lp + new Vector3(0f, 0.21f, 0f), new Vector3(0.32f, 0.07f, 0.32f), _iron, default, false);
                K.PointLight("Laternenschein (Halle)", b, lp + new Vector3(0f, -0.05f, -0.2f), new Color(1f, 0.64f, 0.34f), 9f, 1.5f, false, false);
            }
            // Rußfahne aus dem Rauchloch oben im Fels (auf der Felsoberfläche, nicht im Berg)
            Vector3 rl = new Vector3(w * 0.3f, 0f, d * 0.5f + 1.5f);
            float yr = GL(b, rl.x, rl.z);
            K.Box("Rauchloch", b, new Vector3(rl.x, yr + 0.05f, rl.z), new Vector3(1.0f, 0.14f, 1.0f), _soot, default, false);
            K.Particles("Rauch", b, new Vector3(rl.x, yr + 0.4f, rl.z), new Color(0.32f, 0.32f, 0.34f, 0.3f), 1.4f, 3f, 1.0f, 6f, 8f, 0.25f);
            _pieces += 24;
        }

        /// <summary>Fachwerk: Eckständer, Schwelle, Rähm und zwei Streben je Längsseite.</summary>
        private static void Frame(Transform b, float w, float d, float s, float h)
        {
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
                K.Box("Ständer", b, new Vector3(ix * (w * 0.5f + 0.02f), s + h * 0.5f, iz * (d * 0.5f + 0.02f)), new Vector3(0.26f, h, 0.26f), _woodDark, default, false);
            for (int iz = -1; iz <= 1; iz += 2)
            {
                float z = iz * (d * 0.5f + 0.04f);
                K.Box("Schwelle", b, new Vector3(0f, s + 0.12f, z), new Vector3(w, 0.22f, 0.12f), _woodDark, default, false);
                K.Box("Riegel", b, new Vector3(0f, s + h * 0.5f, z), new Vector3(w, 0.16f, 0.12f), _woodDark, default, false);
                K.Box("Rähm", b, new Vector3(0f, s + h - 0.1f, z), new Vector3(w, 0.22f, 0.12f), _woodDark, default, false);
                float len = Mathf.Sqrt(h * h * 0.25f + w * w * 0.04f);
                for (int i = -1; i <= 1; i += 2)
                    K.Box("Strebe", b, new Vector3(i * w * 0.36f, s + h * 0.25f, z), new Vector3(0.14f, len, 0.12f), _woodDark,
                        new Vector3(0f, 0f, i * Mathf.Atan2(w * 0.2f, h * 0.5f) * Mathf.Rad2Deg), false);
            }
        }

        /// <summary>Satteldach (First entlang x) aus zwei Dachflächen und zwei Giebeldreiecken.</summary>
        private static void Roof(Transform parent, Vector3 eave, float w, float d, float yaw, float rise, Material mat)
        {
            Transform r = K.Group("Dach", parent, eave, new Vector3(0f, yaw, 0f));
            float half = d * 0.5f;
            float slope = Mathf.Sqrt(half * half + rise * rise);
            float ang = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            for (int s = -1; s <= 1; s += 2)
                K.Box("Dachfläche", r, new Vector3(0f, rise * 0.5f, s * half * 0.5f), new Vector3(w, 0.16f, slope + 0.1f), mat,
                    new Vector3(s * ang, 0f, 0f));
            K.Box("First", r, new Vector3(0f, rise + 0.05f, 0f), new Vector3(w + 0.1f, 0.2f, 0.3f), _woodDark, default, false);
            for (int s = -1; s <= 1; s += 2)
                Gable(r, new Vector3(s * (w * 0.5f - 0.45f), 0f, 0f), d - 0.6f, rise - 0.15f, _plaster);
            _pieces += 5;
        }

        private static void LeanRoof(Transform parent, float y, float w, float d, Material mat)
        {
            float rise = d * 0.25f, len = Mathf.Sqrt(d * d + rise * rise), ang = Mathf.Atan2(rise, d) * Mathf.Rad2Deg;
            K.Box("Pultdach", parent, new Vector3(0f, y + rise * 0.5f, 0f), new Vector3(w, 0.16f, len), mat, new Vector3(ang, 0f, 0f));
        }

        /// <summary>Giebeldreieck als eigenes Netz (keine Lücke unter dem Dach).</summary>
        private static void Gable(Transform parent, Vector3 local, float width, float height, Material mat)
        {
            var go = new GameObject("Giebel");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var mesh = new Mesh { name = "Giebel" };
            float t = 0.2f, hw = width * 0.5f;
            var v = new[]
            {
                new Vector3(-t * 0.5f, 0f, -hw), new Vector3(-t * 0.5f, height, 0f), new Vector3(-t * 0.5f, 0f, hw),
                new Vector3(t * 0.5f, 0f, -hw), new Vector3(t * 0.5f, height, 0f), new Vector3(t * 0.5f, 0f, hw)
            };
            mesh.vertices = v;
            mesh.triangles = new[] { 0, 1, 2, 5, 4, 3, 0, 3, 4, 0, 4, 1, 1, 4, 5, 1, 5, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ================================================================== Spuren (Environmental Storytelling)
        // Jede Spur beantwortet „wer nutzt das, warum liegt es hier?“ (Plan lager.py, Storytelling §H, WORLD-205).

        private static GameObject Model(string name, Transform parent, Vector3 localPos, float yaw, float scale, ArtModels.ColliderKind col)
            => ArtModels.Has(name) ? ArtModels.Place(name, parent, localPos, yaw, scale, col) : null;

        private static Vector2 OnLine(float[] l, float u)
        {
            // u ∈ [0, 1] entlang der Polylinie (nach Länge)
            int n = l.Length / 2;
            float total = 0f;
            for (int i = 0; i + 1 < n; i++) total += Vector2.Distance(new Vector2(l[2 * i], l[2 * i + 1]), new Vector2(l[2 * i + 2], l[2 * i + 3]));
            float want = Mathf.Clamp01(u) * total;
            for (int i = 0; i + 1 < n; i++)
            {
                Vector2 a = new Vector2(l[2 * i], l[2 * i + 1]), b = new Vector2(l[2 * i + 2], l[2 * i + 3]);
                float seg = Vector2.Distance(a, b);
                if (want <= seg || i + 2 == n) return Vector2.Lerp(a, b, seg > 0f ? Mathf.Clamp01(want / seg) : 0f);
                want -= seg;
            }
            return new Vector2(l[0], l[1]);
        }

        private static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        private static void Trace(KarethV4Spur sp, KarethV4Lager l)
        {
            var rng = new System.Random(sp.typ.GetHashCode() ^ Mathf.RoundToInt(sp.x * 31f + sp.z * 17f));
            Transform t;
            switch (sp.typ)
            {
                case "ankunft":
                {
                    // Hier erscheinen die Neuankömmlinge (LORE-082). Ausgetretener Kreis, Laternenpfahl der Wache, Sitzstamm,
                    // Wegweiser zum Nordtor. Keine Magie zu sehen – nur, dass hier oft jemand wartet.
                    t = K.Group("Spur: Ankunftsstelle am Bergfuß", _root, P(sp.x, sp.z), Vector3.zero);
                    K.Prim(PrimitiveType.Cylinder, "ausgetretener Boden", t, new Vector3(0f, 0.01f, 0f), new Vector3(sp.r * 1.6f, 0.01f, sp.r * 1.6f), Vector3.zero, _soil, false);
                    for (int i = 0; i < 14; i++)
                    {
                        float a = i * 360f / 14f + R(rng, -6f, 6f);
                        K.Box("Kreisstein", t, new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * sp.r, 0.12f, Mathf.Cos(a * Mathf.Deg2Rad) * sp.r),
                            new Vector3(R(rng, 0.5f, 0.9f), 0.3f, R(rng, 0.4f, 0.7f)), rng.NextDouble() < 0.5 ? _moss : _stone, new Vector3(0f, a, 0f), false);
                    }
                    Vector3 post = new Vector3(sp.r + 1.5f, 0f, sp.r * 0.6f);
                    K.Block("Laternenpfahl", t, post, new Vector3(0.22f, 3.2f, 0.22f), _woodDark);
                    K.Box("Laternenarm", t, post + new Vector3(-0.4f, 3.0f, 0f), new Vector3(0.9f, 0.1f, 0.1f), _woodDark, default, false);
                    K.Box("Laterne", t, post + new Vector3(-0.8f, 2.7f, 0f), new Vector3(0.3f, 0.4f, 0.3f), _glow, default, false);
                    K.PointLight("Laternenschein", t, post + new Vector3(-0.8f, 2.6f, 0f), new Color(1f, 0.62f, 0.3f), 9f, 1.6f, false, false);
                    if (Model("SM_Log", t, post + new Vector3(1.4f, 0f, 0.5f), 20f, 0.7f, ArtModels.ColliderKind.Capsule) == null)
                        K.Prim(PrimitiveType.Cylinder, "Sitzstamm", t, post + new Vector3(1.4f, 0.3f, 0.5f), new Vector3(0.5f, 0.9f, 0.5f), new Vector3(90f, 20f, 0f), _wood);
                    K.Block("Wegweiser", t, new Vector3(sp.r + 0.5f, 0f, sp.r + 2f), new Vector3(0.16f, 2.0f, 0.16f), _woodOld);
                    K.Box("Wegweiser Brett", t, new Vector3(sp.r + 0.9f, 1.7f, sp.r + 2f), new Vector3(1.0f, 0.25f, 0.05f), _woodOld, new Vector3(0f, -10f, 6f), false);
                    // Decke und Wasserschlauch: die Wache bringt jedem etwas Warmes
                    K.Box("Decke (gefaltet)", t, post + new Vector3(1.2f, 0.62f, 0.4f), new Vector3(0.7f, 0.12f, 0.5f), _cloth2, new Vector3(0f, 25f, 0f), false);
                    _pieces += 24;
                    break;
                }
                case "steinmaennchen":
                {
                    // Jeder Neuankömmling legt am Weg zum Tor einen Stein dazu. Viele kleine Haufen, ältere bemoost (nahe der
                    // Ankunft dichter, weil die meisten gleich dort anfangen).
                    t = K.Group("Spur: Steinmännchen der Neuankömmlinge", _root);
                    // 11.10. (Max: „bei den Steinen am Camp ist ein Fehler passiert“): 140 Türme aus Quadern lasen sich als gestapelte
                    // Mauersteine über die ganze Wiese. Jetzt halb so viele, aus flachen gerundeten Steinen (Kugeln gestaucht), dichter am Pfad.
                    int anzahl = Mathf.Max(1, sp.anzahl / 2);
                    for (int i = 0; i < anzahl; i++)
                    {
                        float u = Mathf.Pow((float)rng.NextDouble(), 1.6f);
                        Vector2 c = OnLine(sp.linie, u);
                        float side = (rng.NextDouble() < 0.5 ? -1f : 1f) * R(rng, 1.2f, sp.breite * 0.4f + 0.8f);
                        Vector2 c2 = OnLine(sp.linie, Mathf.Min(1f, u + 0.01f)) - c;
                        Vector2 nrm = c2.sqrMagnitude > 1e-4f ? new Vector2(-c2.y, c2.x).normalized : Vector2.right;
                        Vector2 q = c + nrm * side;
                        Vector3 b = P(q.x, q.y);
                        int stones = 2 + rng.Next(3);
                        float y = 0f, s = R(rng, 0.38f, 0.5f);
                        bool old = rng.NextDouble() < 0.45;
                        for (int k = 0; k < stones; k++)
                        {
                            float ss = s * (1f - k * 0.2f);
                            K.Prim(PrimitiveType.Sphere, "Stein", t, b + new Vector3(R(rng, -0.04f, 0.04f), y + ss * 0.22f, R(rng, -0.04f, 0.04f)), new Vector3(ss, ss * 0.45f, ss * 0.85f),
                                new Vector3(R(rng, -8f, 8f), R(rng, 0f, 180f), R(rng, -8f, 8f)), old && k == 0 ? _moss : _stone, false);
                            y += ss * 0.4f;
                        }
                        _pieces += stones;
                    }
                    break;
                }
                case "zaehlbrett":
                {
                    // Veits Zählbrett neben der Registerstube: eine Kerbe je Neuankömmling, Fünferbündel, oben frische Kerben.
                    t = K.Group("Spur: Zählbrett am Register", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        K.Block("Pfosten", t, new Vector3(s2 * 0.75f, 0f, 0f), new Vector3(0.16f, 2.0f, 0.16f), _woodOld);
                    K.Box("Brett", t, new Vector3(0f, 1.25f, 0f), new Vector3(1.8f, 1.3f, 0.07f), _woodOld);
                    int perRow = 25;
                    for (int k = 0; k < sp.kerben; k++)
                    {
                        int row = k / perRow, col = k % perRow;
                        bool fifth = col % 5 == 4;
                        float x = -0.8f + (col - col / 5) * 0.075f + (col / 5) * 0.27f;
                        float yy = 1.75f - row * 0.32f;
                        if (fifth)
                            K.Box("Bündelstrich", t, new Vector3(x - 0.13f, yy, -0.045f), new Vector3(0.025f, 0.32f, 0.01f), _soot, new Vector3(0f, 0f, -60f), false);
                        else
                            K.Box("Kerbe", t, new Vector3(x, yy, -0.045f), new Vector3(0.022f, 0.22f, 0.01f), k >= sp.kerben - 6 ? _woodNew : _soot, default, false);
                    }
                    K.Box("Messer im Pfosten", t, new Vector3(0.75f, 1.6f, -0.12f), new Vector3(0.03f, 0.03f, 0.22f), _iron, new Vector3(20f, 0f, 0f), false);
                    _pieces += 6 + sp.kerben;
                    break;
                }
                case "geruest":
                {
                    // Gerüst an der Felsstirn: hier wird die Halle weiter in den Fels geschlagen (LORE-081). Steinsplitter darunter.
                    t = K.Group("Spur: Gerüst an der Felsstirn", _root, P(sp.x, sp.z), Vector3.zero);
                    int cols = 3, levels = 3;
                    for (int c = 0; c < cols; c++)
                        K.Block("Gerüststange", t, new Vector3(-sp.w * 0.5f + c * sp.w / (cols - 1), 0f, 1.6f), new Vector3(0.14f, sp.h, 0.14f), _woodOld);
                    for (int c = 0; c < cols; c++)
                        K.Block("Gerüststange hinten", t, new Vector3(-sp.w * 0.5f + c * sp.w / (cols - 1), 0f, 0.3f), new Vector3(0.14f, sp.h, 0.14f), _woodOld);
                    for (int v = 1; v <= levels; v++)
                    {
                        float y = v * sp.h / (levels + 0.4f);
                        K.Box("Riegel", t, new Vector3(0f, y, 1.6f), new Vector3(sp.w + 0.3f, 0.1f, 0.1f), _wood, default, false);
                        K.Box("Bohlen", t, new Vector3(0f, y + 0.08f, 0.95f), new Vector3(sp.w, 0.07f, 1.4f), _wood, new Vector3(0f, 0f, R(rng, -1f, 1f)));
                        K.Box("Kreuzband", t, new Vector3(0f, y - sp.h / (levels + 0.4f) * 0.5f, 1.65f), new Vector3(Mathf.Sqrt(sp.w * sp.w * 0.25f + 9f), 0.08f, 0.06f), _wood, new Vector3(0f, 0f, 35f * (v % 2 == 0 ? 1 : -1)), false);
                    }
                    K.Ramp("Leiter", t, new Vector3(sp.w * 0.5f + 0.6f, 0f, 2.6f), 180f, 72f, sp.h * 0.4f, 0.5f, _woodOld);
                    K.Box("Seilrolle", t, new Vector3(-sp.w * 0.5f, sp.h + 0.3f, 1.6f), new Vector3(0.35f, 0.35f, 0.15f), _woodDark, default, false);
                    K.Box("Seil", t, new Vector3(-sp.w * 0.5f, sp.h * 0.5f + 0.2f, 1.75f), new Vector3(0.03f, sp.h, 0.03f), _rope, default, false);
                    K.Box("Steinkorb", t, new Vector3(-sp.w * 0.5f, 0.3f, 1.9f), new Vector3(0.6f, 0.5f, 0.6f), _straw, default, false);
                    for (int i = 0; i < 9; i++)
                        K.Box("Steinsplitter", t, new Vector3(R(rng, -sp.w * 0.6f, sp.w * 0.6f), 0.08f, R(rng, 0.5f, 3.2f)), new Vector3(R(rng, 0.2f, 0.5f), 0.15f, R(rng, 0.2f, 0.4f)), _stone, new Vector3(0f, R(rng, 0f, 180f), 0f), false);
                    _pieces += 30;
                    break;
                }
                case "nischen":
                {
                    // Nischen in der Felsstirn in Bauzuständen: links fertig (Kerze, Gaben), rechts nur angerissen mit Werkzeug.
                    // Seit 05.10. gebaut mit der behauenen Felsstirn (Öffnungen in der Wand), siehe FelsStirn.
                    if (HatSpur(l, "felsstirn")) break;
                    t = K.Group("Spur: Nischen in der Felsstirn", _root, P(sp.x, sp.z), Vector3.zero);
                    for (int i = 0; i < sp.anzahl; i++)
                    {
                        float x = -i * 3.4f, stage = sp.anzahl > 1 ? 1f - i / (float)(sp.anzahl - 1) : 1f;   // 1 = fertig
                        float depth = 0.15f + 0.6f * stage;
                        Vector3 c = new Vector3(x, 1.4f, -depth * 0.5f + 0.25f);
                        K.Box("Nische", t, c, new Vector3(1.1f, 1.6f, depth), _soot, default, false);
                        if (stage > 0.6f)
                        {
                            K.Box("Sturzbalken", t, new Vector3(x, 2.3f, 0.3f), new Vector3(1.5f, 0.2f, 0.2f), _woodDark, default, false);
                            K.Box("Kerze", t, new Vector3(x - 0.25f, 0.75f, 0.1f), new Vector3(0.08f, 0.2f, 0.08f), _linen, default, false);
                            K.PointLight("Kerzenschein", t, new Vector3(x - 0.25f, 1.0f, 0.2f), new Color(1f, 0.6f, 0.28f), 3.5f, 0.8f, false, false);
                            K.Box("Gabe (Stein mit Zeichen)", t, new Vector3(x + 0.2f, 0.7f, 0.1f), new Vector3(0.2f, 0.12f, 0.15f), _stone, default, false);
                        }
                        else if (stage < 0.4f)
                        {
                            K.Box("Meißelriss", t, new Vector3(x, 1.4f, 0.27f), new Vector3(1.2f, 1.7f, 0.02f), _stoneDark, default, false);
                            K.Box("Meißel", t, new Vector3(x + 0.6f, 0.06f, 0.8f), new Vector3(0.04f, 0.04f, 0.3f), _iron, new Vector3(0f, 30f, 0f), false);
                            K.Box("Fäustel", t, new Vector3(x + 0.9f, 0.1f, 0.9f), new Vector3(0.18f, 0.14f, 0.12f), _iron, new Vector3(0f, 10f, 0f), false);
                            for (int k = 0; k < 6; k++)
                                K.Box("Splitter", t, new Vector3(x + R(rng, -0.6f, 0.6f), 0.06f, R(rng, 0.4f, 1.2f)), new Vector3(0.18f, 0.1f, 0.14f), _stone, new Vector3(0f, R(rng, 0f, 180f), 0f), false);
                        }
                    }
                    _pieces += sp.anzahl * 6;
                    break;
                }
                case "erweiterung":
                {
                    // Die Ostpalisade wird bis zum Bach erweitert: ein Teil steht schon (frisches Holz), danach Pfostenlöcher
                    // mit Schnur zwischen Pflöcken – es kommen weiter Menschen (WEITER).
                    t = K.Group("Spur: Erweiterung der Ostpalisade (im Bau)", _root);
                    int n = 0;
                    for (float u = 0f; u <= 1.0001f; u += 0.012f, n++)
                    {
                        Vector2 c = OnLine(sp.linie, u);
                        Vector3 b = P(c.x, c.y);
                        if (u <= sp.gesetzt)
                        {
                            float h = R(rng, 2.8f, 3.0f);
                            K.Prim(PrimitiveType.Cylinder, "neuer Pfahl", t, b + Vector3.up * (h * 0.5f - 0.2f), new Vector3(0.28f, h * 0.5f, 0.28f), new Vector3(0f, R(rng, 0f, 90f), 0f), _woodNew);
                        }
                        else if (n % 3 == 0)
                        {
                            K.Prim(PrimitiveType.Cylinder, "Pfostenloch", t, b + Vector3.up * 0.01f, new Vector3(0.5f, 0.01f, 0.5f), Vector3.zero, _soil, false);
                            K.Box("Pflock", t, b + new Vector3(0.4f, 0.25f, 0f), new Vector3(0.05f, 0.5f, 0.05f), _woodNew, default, false);
                        }
                    }
                    // Schnur entlang des ungebauten Rests
                    for (float u = sp.gesetzt; u < 1f; u += 0.1f)
                    {
                        Vector2 a = OnLine(sp.linie, u), b2 = OnLine(sp.linie, Mathf.Min(1f, u + 0.1f));
                        Vector3 A = P(a.x, a.y, 0.45f) + new Vector3(0.4f, 0f, 0f), B = P(b2.x, b2.y, 0.45f) + new Vector3(0.4f, 0f, 0f);
                        GameObject line = K.Box("Richtschnur", t, (A + B) * 0.5f, new Vector3(0.015f, 0.015f, Vector3.Distance(A, B)), _linen, default, false);
                        line.transform.rotation = Quaternion.LookRotation(B - A);
                    }
                    _pieces += n;
                    break;
                }
                case "pfahlstapel":
                {
                    t = K.Group("Spur: Pfähle für die Erweiterung", _root, P(sp.x, sp.z), new Vector3(0f, 20f, 0f));
                    int row = 0;
                    for (int count = 5; count > 0; count--, row++)
                        for (int i = 0; i < count; i++)
                            K.Prim(PrimitiveType.Cylinder, "Pfahl (geschält)", t, new Vector3((i - (count - 1) * 0.5f) * 0.32f, 0.16f + row * 0.28f, 0f),
                                new Vector3(0.28f, 1.6f, 0.28f), new Vector3(90f, 0f, 0f), _woodNew, row == 0);
                    K.Block("Hackklotz", t, new Vector3(2.0f, 0f, 1.2f), new Vector3(0.6f, 0.55f, 0.6f), _wood);
                    K.Box("Axt", t, new Vector3(2.0f, 0.75f, 1.2f), new Vector3(0.05f, 0.5f, 0.05f), _wood, new Vector3(0f, 0f, 25f), false);
                    for (int i = 0; i < 14; i++)
                        K.Box("Rindenspan", t, new Vector3(R(rng, -1.5f, 3f), 0.03f, R(rng, -1.5f, 2.5f)), new Vector3(R(rng, 0.2f, 0.5f), 0.03f, 0.08f), _woodDark, new Vector3(0f, R(rng, 0f, 180f), 0f), false);
                    _pieces += 34;
                    break;
                }
                case "karren":
                {
                    t = K.Group("Spur: Karren mit Pfählen (abgeladen halb)", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    K.Box("Ladefläche", t, new Vector3(0f, 0.85f, 0f), new Vector3(1.6f, 0.12f, 3.0f), _wood);
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        K.Box("Seitenbrett", t, new Vector3(s2 * 0.78f, 1.1f, 0f), new Vector3(0.06f, 0.4f, 3.0f), _wood, default, false);
                        K.Prim(PrimitiveType.Cylinder, "Rad", t, new Vector3(s2 * 0.95f, 0.6f, -0.2f), new Vector3(1.2f, 0.06f, 1.2f), new Vector3(0f, 0f, 90f), _woodDark);
                        K.Box("Deichsel", t, new Vector3(s2 * 0.4f, 0.6f, 2.4f), new Vector3(0.08f, 0.08f, 2.0f), _wood, new Vector3(14f, 0f, 0f), false);
                    }
                    for (int i = 0; i < 3; i++)
                        K.Prim(PrimitiveType.Cylinder, "Pfahl", t, new Vector3(-0.4f + i * 0.4f, 1.1f, 0f), new Vector3(0.26f, 1.6f, 0.26f), new Vector3(90f, 0f, 0f), _woodNew, false);
                    K.Box("Stützklotz", t, new Vector3(0f, 0.15f, 2.9f), new Vector3(0.4f, 0.3f, 0.3f), _woodDark);
                    _pieces += 12;
                    break;
                }
                case "brandstelle":
                {
                    // Brandnacht: vor dem neuen Abschnitt stehen noch die verkohlten Stümpfe der alten Pfähle.
                    t = K.Group("Spur: Brandstelle der alten Ostpalisade", _root);
                    for (float u = 0f; u <= 1.0001f; u += 0.035f)
                    {
                        Vector2 c = OnLine(sp.linie, u);
                        Vector3 b = P(c.x, c.y);
                        float h = R(rng, 0.25f, 1.1f);
                        K.Prim(PrimitiveType.Cylinder, "verkohlter Stumpf", t, b + Vector3.up * (h * 0.5f - 0.1f), new Vector3(0.3f, h * 0.5f, 0.3f), new Vector3(R(rng, -8f, 8f), 0f, R(rng, -8f, 8f)), _soot, false);
                        if (rng.NextDouble() < 0.5)
                            K.Prim(PrimitiveType.Cylinder, "Asche", t, b + Vector3.up * 0.01f, new Vector3(R(rng, 0.8f, 1.6f), 0.01f, R(rng, 0.8f, 1.6f)), Vector3.zero, _soot, false);
                    }
                    _pieces += 40;
                    break;
                }
                case "waesche":
                {
                    t = K.Group("Spur: Wäscheleine", _root);
                    Vector3 A = P(sp.linie[0], sp.linie[1]), B = P(sp.linie[2], sp.linie[3]);
                    K.Block("Leinenpfosten", t, A, new Vector3(0.14f, 2.3f, 0.14f), _woodOld);
                    K.Block("Leinenpfosten", t, B, new Vector3(0.14f, 2.3f, 0.14f), _woodOld);
                    Vector3 a2 = A + Vector3.up * 2.15f, b2 = B + Vector3.up * 2.15f;
                    float yaw = Mathf.Atan2(b2.x - a2.x, b2.z - a2.z) * Mathf.Rad2Deg;
                    Material[] cl = { _linen, _cloth2, _linen, _cloth3, _cloth1, _linen };
                    int count = 6;
                    for (int i = 0; i <= 8; i++)
                    {
                        float u0 = i / 8f, u1 = (i + 1) / 8f;
                        if (i == 8) break;
                        Vector3 p0 = Vector3.Lerp(a2, b2, u0) + Vector3.down * Mathf.Sin(u0 * Mathf.PI) * 0.25f;
                        Vector3 p1 = Vector3.Lerp(a2, b2, u1) + Vector3.down * Mathf.Sin(u1 * Mathf.PI) * 0.25f;
                        GameObject seg = K.Box("Leine", t, (p0 + p1) * 0.5f, new Vector3(0.02f, 0.02f, Vector3.Distance(p0, p1)), _rope, default, false);
                        seg.transform.rotation = Quaternion.LookRotation(p1 - p0);
                    }
                    for (int i = 0; i < count; i++)
                    {
                        float u = (i + 0.7f) / (count + 0.4f);
                        Vector3 p = Vector3.Lerp(a2, b2, u) + Vector3.down * (Mathf.Sin(u * Mathf.PI) * 0.25f);
                        float hgt = R(rng, 0.5f, 0.9f);
                        K.Box("Wäschestück", t, p + Vector3.down * (hgt * 0.5f), new Vector3(R(rng, 0.5f, 0.9f), hgt, 0.02f), cl[i % cl.Length], new Vector3(0f, yaw + 90f, R(rng, -3f, 3f)), false);
                    }
                    K.Box("Wäschekorb", t, A + new Vector3(0.6f, 0.2f, 0.3f), new Vector3(0.6f, 0.4f, 0.45f), _straw, default, false);
                    _pieces += 20;
                    break;
                }
                case "holzstapel":
                {
                    // Gespaltenes Brennholz an der Wand (lokal −z zeigt zur Wand), Schutzdach fällt von der Wand weg (Regen läuft
                    // vorn ab, nicht in den Stapel), hinten an der Wand höher; Hackklotz davor (Feuer, Küche, Schmiede).
                    t = K.Group("Spur: Holzstapel", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    float sw = sp.w > 1f ? sp.w : 3.8f;
                    int per = Mathf.Max(4, Mathf.RoundToInt((sw - 0.4f) / 0.4f));
                    for (int row = 0; row < 5; row++)
                        for (int i = 0; i < per; i++)
                            K.Box("Scheit", t, new Vector3(-sw * 0.5f + 0.3f + i * 0.4f + (row % 2) * 0.2f, 0.12f + row * 0.24f, -0.05f), new Vector3(0.22f, 0.22f, R(rng, 0.8f, 0.95f)),
                                rng.NextDouble() < 0.3 ? _woodNew : _wood, new Vector3(0f, 0f, R(rng, 0f, 45f)), false);
                    var col = new GameObject("Holzstapel Kollision");
                    col.transform.SetParent(t, false);
                    col.transform.localPosition = new Vector3(0f, 0.6f, -0.05f);
                    col.AddComponent<BoxCollider>().size = new Vector3(sw, 1.2f, 1.0f);
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        K.Block("Dachpfosten hinten", t, new Vector3(s2 * (sw * 0.5f + 0.1f), 0f, -0.55f), new Vector3(0.12f, 2.05f, 0.12f), _woodOld);
                        K.Block("Dachpfosten vorn", t, new Vector3(s2 * (sw * 0.5f + 0.1f), 0f, 0.55f), new Vector3(0.12f, 1.72f, 0.12f), _woodOld);
                    }
                    K.Box("Schutzdach", t, new Vector3(0f, 1.95f, 0f), new Vector3(sw + 0.6f, 0.08f, 1.65f), _roof, new Vector3(14f, 0f, 0f), false);
                    K.Block("Hackklotz", t, new Vector3(0.6f, 0f, 1.6f), new Vector3(0.55f, 0.5f, 0.55f), _woodDark);
                    K.Box("Axt im Klotz", t, new Vector3(0.6f, 0.75f, 1.6f), new Vector3(0.05f, 0.5f, 0.05f), _wood, new Vector3(0f, 30f, -20f), false);
                    for (int i = 0; i < 10; i++)
                        K.Box("Späne", t, new Vector3(0.6f + R(rng, -1f, 1f), 0.02f, 1.6f + R(rng, -1f, 1f)), new Vector3(0.12f, 0.02f, 0.05f), _woodNew, new Vector3(0f, R(rng, 0f, 180f), 0f), false);
                    _pieces += 5 * per + 16;
                    break;
                }
                case "garten":
                {
                    // Küchengarten hinter der Schmiede: Beete, Flechtzaun gegen Hühner, Gießeimer.
                    t = K.Group("Spur: Küchengarten", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    int beds = Mathf.Max(2, Mathf.RoundToInt(sp.w / 2.2f));
                    for (int i = 0; i < beds; i++)
                    {
                        float x = -sp.w * 0.5f + (i + 0.5f) * sp.w / beds;
                        K.Box("Beet", t, new Vector3(x, 0.12f, 0f), new Vector3(sp.w / beds - 0.6f, 0.24f, sp.d - 1.2f), _soil, default, false);
                        for (int k = 0; k < 5; k++)
                            K.Box("Pflanzen", t, new Vector3(x + R(rng, -0.3f, 0.3f), 0.38f, -sp.d * 0.5f + 1f + k * (sp.d - 2f) / 4f), new Vector3(0.45f, R(rng, 0.2f, 0.4f), 0.45f), _green, new Vector3(0f, R(rng, 0f, 90f), 0f), false);
                    }
                    for (int side = 0; side < 4; side++)
                    {
                        bool lng = side % 2 == 0;
                        float len = lng ? sp.w : sp.d;
                        Vector3 c = lng ? new Vector3(0f, 0.45f, (side == 0 ? -1 : 1) * sp.d * 0.5f) : new Vector3((side == 1 ? 1 : -1) * sp.w * 0.5f, 0.45f, 0f);
                        if (side == 0) len *= 0.7f;     // Durchgang
                        K.Box("Flechtzaun", t, c, lng ? new Vector3(len, 0.9f, 0.08f) : new Vector3(0.08f, 0.9f, len), _straw);
                    }
                    K.Prim(PrimitiveType.Cylinder, "Gießeimer", t, new Vector3(sp.w * 0.5f - 0.6f, 0.2f, -sp.d * 0.5f - 0.6f), new Vector3(0.35f, 0.2f, 0.35f), Vector3.zero, _wood, false);
                    _pieces += beds * 6 + 5;
                    break;
                }
                case "waschplatz":
                {
                    if (sp.h == 0f || sp.linie == null || sp.linie.Length < 4)
                    {
                        t = K.Group("Spur: Waschplatz am Bach", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                        for (int i = 0; i < 5; i++)
                            K.Box("Waschstein", t, new Vector3(-2f + i * 1.0f, 0.12f, R(rng, -0.2f, 0.2f)), new Vector3(0.9f, 0.25f, 0.8f), _stone, new Vector3(R(rng, -4f, 4f), R(rng, 0f, 40f), 0f));
                        K.Box("Waschbrett", t, new Vector3(0.3f, 0.4f, 0.4f), new Vector3(0.45f, 0.6f, 0.04f), _woodOld, new Vector3(-55f, 0f, 0f), false);
                        K.Box("Korb", t, new Vector3(-1.2f, 0.25f, 1.4f), new Vector3(0.6f, 0.5f, 0.45f), _straw, default, false);
                        K.Block("Bank", t, new Vector3(0f, 0f, 2.0f), new Vector3(2.2f, 0.45f, 0.4f), _woodOld);
                        _pieces += 10;
                        break;
                    }
                    // Waschplatz am Bach (neben dem Steg, Lagerseite): Steinpodest 0,35 m über dem Wasser (lokal +z zum Wasser),
                    // Stufen die Böschung hinauf bis zur Uferkante (gesetzt), Waschsteine im Wasser, oben eine Bank und der Korb.
                    t = K.Group("Spur: Waschplatz am Bach", _root, new Vector3(sp.x, sp.h, sp.z), new Vector3(0f, sp.yaw, 0f));
                    K.Box("Podest", t, new Vector3(0f, -0.3f, 0f), new Vector3(3.4f, 0.6f, 2.6f), _stone);
                    for (int i = 0; i < 4; i++)
                        K.Box("Waschstein (im Wasser)", t, new Vector3(-1.2f + i * 0.8f, -0.32f, 1.75f + R(rng, -0.1f, 0.1f)), new Vector3(0.7f, 0.3f, 0.6f), _moss, new Vector3(R(rng, -6f, 6f), R(rng, 0f, 40f), 0f));
                    K.Box("Waschbrett", t, new Vector3(0.4f, 0.25f, 1.1f), new Vector3(0.45f, 0.6f, 0.04f), _woodOld, new Vector3(-55f, 0f, 0f), false);
                    K.Box("Tuch", t, new Vector3(-0.6f, 0.02f, 0.9f), new Vector3(0.7f, 0.02f, 0.5f), _linen, default, false);
                    Vector3 oben = t.InverseTransformPoint(new Vector3(sp.linie[2], sp.gesetzt, sp.linie[3]));
                    float hub = oben.y, lauf = Mathf.Max(1f, -oben.z - 1.3f);
                    int n = Mathf.Max(2, Mathf.RoundToInt(hub / 0.22f));
                    for (int k = 1; k <= n; k++)
                    {
                        float zc = -1.3f - (k - 0.5f) * lauf / n, top = k * hub / n;
                        Vector3 wp = t.TransformPoint(new Vector3(0f, 0f, zc));
                        float yb = Mathf.Min(G(wp.x, wp.z) - sp.h - 0.25f, top - 0.2f);
                        K.Block("Stufe", t, new Vector3(0f, yb, zc), new Vector3(1.4f, top - yb, lauf / n + 0.02f), _stoneDark);
                        _pieces++;
                    }
                    K.Box("Korb mit Wäsche", t, new Vector3(1.3f, hub + 0.25f, oben.z - 0.6f), new Vector3(0.6f, 0.5f, 0.45f), _straw, default, false);
                    K.Block("Bank", t, new Vector3(-1.4f, hub, oben.z - 0.9f), new Vector3(2.0f, 0.45f, 0.4f), _woodOld);
                    _pieces += 10;
                    break;
                }
                case "stumpfguertel":
                {
                    // Stumpfgürtel: Holz für Palisade und Feuer kam aus der nächsten Umgebung. Darum fehlt der Wald rund ums Lager.
                    t = K.Group("Spur: Stumpfgürtel (gerodet)", _root);
                    for (int i = 0; i + 1 < sp.linie.Length; i += 2)
                    {
                        float x = sp.linie[i], z = sp.linie[i + 1];
                        Vector3 b = P(x, z);
                        float sc = R(rng, 0.7f, 1.3f);
                        if (Model("SM_Stump", t, b, R(rng, 0f, 360f), sc, ArtModels.ColliderKind.Capsule) == null)
                        {
                            float h = R(rng, 0.35f, 0.8f), r = R(rng, 0.45f, 0.75f);
                            K.Prim(PrimitiveType.Cylinder, "Stumpf", t, b + Vector3.up * (h * 0.5f - 0.1f), new Vector3(r, h * 0.5f, r), Vector3.zero, rng.NextDouble() < 0.5 ? _woodOld : _wood);
                            K.Prim(PrimitiveType.Cylinder, "Schnittfläche", t, b + Vector3.up * (h - 0.09f), new Vector3(r * 0.92f, 0.01f, r * 0.92f), new Vector3(R(rng, -10f, 10f), 0f, R(rng, -10f, 10f)), _woodNew, false);
                        }
                        _pieces += 2;
                    }
                    break;
                }
                case "friedhof":
                {
                    // Kleiner Friedhof unter dem Einzelbaum (der Baum steht am Nordrand, die Gräber südlich davon, nicht in seinen
                    // Wurzeln): alte Gräber mit Steinen, neuere mit Holzbrett. Niedrige Mauer, offen zum Lager (Osten).
                    t = K.Group("Spur: Friedhof unter dem Einzelbaum", _root, P(sp.x, sp.z), Vector3.zero);
                    for (int i = 0; i < 20; i++)
                    {
                        float a = i * 18f;
                        if (a > 60f && a < 110f) continue;      // Zugang nach Osten (zum Lager)
                        Vector3 p = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * sp.r, 0f, Mathf.Cos(a * Mathf.Deg2Rad) * sp.r);
                        p.y = GL(t, p.x, p.z) - 0.15f;
                        K.Block("Einfriedung", t, p, new Vector3(2.7f, 0.65f, 0.6f), _moss, a + 90f);
                    }
                    for (int i = 0; i < sp.anzahl; i++)
                    {
                        int rowI = i / 4, colI = i % 4;
                        Vector3 p = new Vector3(-3.6f + colI * 2.2f + R(rng, -0.2f, 0.2f), 0f, 1.4f - rowI * 2.6f);
                        p.y = GL(t, p.x, p.z);
                        bool oldGrave = i < sp.anzahl * 0.55f;
                        K.Box("Grabhügel", t, p + new Vector3(0f, 0.08f, -0.7f), new Vector3(0.9f, 0.18f, 1.8f), oldGrave ? _moss : _soil, new Vector3(0f, R(rng, -4f, 4f), 0f), false);
                        if (oldGrave)
                            K.Block("Grabstein", t, p + new Vector3(0f, -0.15f, 0.25f), new Vector3(0.55f, R(rng, 0.65f, 0.95f), 0.2f), _stone, R(rng, -8f, 8f));
                        else
                        {
                            K.Block("Grabbrett", t, p + new Vector3(0f, -0.15f, 0.25f), new Vector3(0.45f, 1.0f, 0.06f), _woodOld, R(rng, -4f, 4f));
                            if (i == sp.anzahl - 1)
                                K.Box("frische Blumen", t, p + new Vector3(0f, 0.22f, -0.3f), new Vector3(0.3f, 0.1f, 0.2f), _cloth2, default, false);
                        }
                    }
                    _pieces += 40;
                    break;
                }
                case "furt":
                {
                    if (sp.h == 0f && sp.gesetzt == 0f)
                    {
                        // alte Daten: Trittsteine für Fußgänger, daneben ein Bohlensteg
                        t = K.Group("Spur: Furt mit Steg", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                        for (int i = 0; i < 7; i++)
                            K.Box("Trittstein", t, new Vector3(R(rng, -0.25f, 0.25f), 0.05f, -4.5f + i * 1.5f), new Vector3(0.9f, 0.5f, 0.8f), _stone, new Vector3(0f, R(rng, 0f, 60f), 0f));
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                            for (int k = 0; k < 3; k++)
                                K.Block("Stegpfosten", t, new Vector3(3f + s2 * 0.6f, -0.6f, -4f + k * 4f), new Vector3(0.18f, 1.6f, 0.18f), _woodDark);
                        K.Box("Bohlen", t, new Vector3(3f, 0.95f, 0f), new Vector3(1.4f, 0.1f, 10f), _wood);
                        _pieces += 14;
                        break;
                    }
                    // Furt (Weg durch den Bach, yaw = Wegrichtung): die Sohle ist mit flachen Steinen gepflastert (Karren sinken nicht
                    // ein), daneben Trittsteine für trockene Füße, an beiden Ufern Furtstangen mit Pegelkerben (bei Hochwasser
                    // sieht man, ob man noch durchkommt).
                    float wid = sp.w > 0.5f ? sp.w : 3.6f;
                    t = K.Group("Spur: Furt", _root, new Vector3(sp.x, 0f, sp.z), new Vector3(0f, sp.yaw, 0f));
                    for (float u = -5.1f; u <= 5.1f; u += 0.85f)
                        for (float v = -wid * 0.5f + 0.4f; v <= wid * 0.5f - 0.3f; v += 0.85f)
                        {
                            if (rng.NextDouble() < 0.15) continue;
                            Vector3 lp = new Vector3(v + R(rng, -0.12f, 0.12f), 0f, u + R(rng, -0.12f, 0.12f));
                            Vector3 wp = t.TransformPoint(lp);
                            K.Box("Pflasterstein", t, new Vector3(lp.x, G(wp.x, wp.z) + 0.01f, lp.z), new Vector3(R(rng, 0.55f, 0.75f), 0.14f, R(rng, 0.5f, 0.7f)),
                                rng.NextDouble() < 0.3 ? _moss : _stone, new Vector3(R(rng, -3f, 3f), R(rng, 0f, 90f), R(rng, -3f, 3f)), false);
                            _pieces++;
                        }
                    for (float u = -2.6f; u <= 2.61f; u += 0.95f)
                    {
                        Vector3 lp = new Vector3(wid * 0.5f + 1.1f + R(rng, -0.15f, 0.15f), 0f, u);
                        Vector3 wp = t.TransformPoint(lp);
                        float gy = Mathf.Max(G(wp.x, wp.z), sp.gesetzt);
                        K.Box("Trittstein", t, new Vector3(lp.x, gy + 0.18f, lp.z), new Vector3(R(rng, 0.6f, 0.8f), 0.5f, R(rng, 0.55f, 0.7f)), _stone, new Vector3(R(rng, -4f, 4f), R(rng, 0f, 60f), R(rng, -4f, 4f)));
                        _pieces++;
                    }
                    for (int e = -1; e <= 1; e += 2)
                    {
                        Vector3 lp = new Vector3(-wid * 0.5f - 0.7f, 0f, e * 4.6f);
                        Vector3 wp = t.TransformPoint(lp);
                        float gy = G(wp.x, wp.z);
                        K.Block("Furtstange", t, new Vector3(lp.x, gy - 0.4f, lp.z), new Vector3(0.14f, 2.3f, 0.14f), _woodOld);
                        for (int k = 0; k < 3; k++)
                            K.Box("Pegelkerbe", t, new Vector3(lp.x, gy + 0.4f + k * 0.45f, lp.z) + Vector3.right * 0.075f, new Vector3(0.02f, 0.04f, 0.16f), _soot, default, false);
                        _pieces += 4;
                    }
                    break;
                }
                case "lesesteinwall":
                {
                    // Lesesteinwälle an den Terrassenkanten der Vorratswiesen: Steine, die über Generationen aus dem Acker gelesen wurden.
                    t = K.Group("Spur: Lesesteinwall", _root);
                    for (int i = 0; i + 3 < sp.linie.Length; i += 2)
                    {
                        Vector2 a = new Vector2(sp.linie[i], sp.linie[i + 1]), b = new Vector2(sp.linie[i + 2], sp.linie[i + 3]), d = b - a;
                        float len = d.magnitude;
                        if (len < 0.3f) continue;
                        float hd = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                        for (float u = R(rng, 0f, 1.5f); u < len; u += R(rng, 1.6f, 3.2f))
                        {
                            // locker, halb eingesunken und überwachsen: ein Rain aus Lesesteinen, keine Mauer
                            Vector2 c = a + d * (u / len);
                            K.Box("Lesestein", t, P(c.x, c.y, 0.02f), new Vector3(R(rng, 0.45f, 0.8f), R(rng, 0.25f, 0.45f), R(rng, 0.45f, 0.75f)), rng.NextDouble() < 0.6 ? _moss : _stone,
                                new Vector3(R(rng, -6f, 6f), hd + R(rng, -15f, 15f), R(rng, -6f, 6f)), false);
                            _pieces++;
                        }
                    }
                    break;
                }
                case "karrenrillen":
                {
                    // Muldentor: Seit Jahrhunderten fahren alle Karren hier durch – zwei ausgefahrene Rillen im Fels.
                    t = K.Group("Spur: Karrenrillen im Muldentor", _root);
                    for (int i = 0; i + 3 < sp.linie.Length; i += 2)
                    {
                        Vector2 a = new Vector2(sp.linie[i], sp.linie[i + 1]), b = new Vector2(sp.linie[i + 2], sp.linie[i + 3]), d = b - a;
                        if (d.sqrMagnitude < 0.01f) continue;
                        Vector2 n = new Vector2(-d.y, d.x).normalized;
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                        {
                            Vector2 pa = a + n * s2 * sp.breite * 0.5f, pb = b + n * s2 * sp.breite * 0.5f;
                            Vector3 A = P(pa.x, pa.y, 0.02f), B = P(pb.x, pb.y, 0.02f);
                            GameObject r = K.Box("Rille", t, (A + B) * 0.5f - Vector3.up * 0.012f, new Vector3(0.3f, 0.02f, Vector3.Distance(A, B) + 0.1f), _track, default, false);
                            r.transform.rotation = Quaternion.LookRotation(B - A);
                        }
                    }
                    _pieces += sp.linie.Length;
                    break;
                }
                case "prellstein":
                {
                    // An der engsten Stelle: Prellstein, von den Radnaben schwarz gerieben.
                    t = K.Group("Spur: Prellstein", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    K.Block("Prellstein", t, new Vector3(0f, -0.3f, 0f), new Vector3(0.7f, 1.2f, 0.9f), _stone, 0f);
                    K.Box("Reibspur", t, new Vector3(0f, 0.55f, 0f), new Vector3(0.74f, 0.25f, 0.94f), _soot, default, false);
                    _pieces += 2;
                    break;
                }
                case "schlagbaum":
                {
                    // Der Name sagt „Tor“, ein Tor gibt es nicht mehr: zwei Steinpfeiler, ein leerer Angelhaken, ein Stumpf
                    // des Baums im Gras. Warum, weiß eine Erzählerin in Untersteg [VORSCHLAG].
                    t = K.Group("Spur: Reste des Schlagbaums", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    float half = sp.breite * 0.5f;
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        K.Block("Pfeiler", t, new Vector3(s2 * half, -0.3f, 0f), new Vector3(0.8f, 2.2f, 0.8f), _moss);
                    K.Box("Angel (leer)", t, new Vector3(-half + 0.45f, 1.4f, 0f), new Vector3(0.12f, 0.35f, 0.12f), _iron, default, false);
                    K.Prim(PrimitiveType.Cylinder, "Baumstumpf (abgebrochen)", t, new Vector3(-half - 1.6f, 0.15f, 1.4f), new Vector3(0.25f, 1.6f, 0.25f), new Vector3(90f, 25f, 0f), _woodOld, false);
                    _pieces += 4;
                    break;
                }
                case "feldunterstand":
                {
                    // Feldunterstand: hier wird bei Regen gewartet und Werkzeug gelassen; Sense, Rechen, Wasserkrug.
                    t = K.Group("Spur: Feldunterstand", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    for (int ix = -1; ix <= 1; ix += 2)
                        for (int iz = -1; iz <= 1; iz += 2)
                            K.Block("Pfosten", t, new Vector3(ix * 1.6f, 0f, iz * 1.2f), new Vector3(0.18f, iz < 0 ? 2.2f : 2.6f, 0.18f), _woodOld);
                    K.Box("Strohdach", t, new Vector3(0f, 2.5f, 0f), new Vector3(3.8f, 0.18f, 3.2f), _straw, new Vector3(-9f, 0f, 0f), false);
                    K.Box("Rückwand (Flechtwerk)", t, new Vector3(0f, 1.1f, 1.25f), new Vector3(3.3f, 2.0f, 0.1f), _straw);
                    K.Box("Sense", t, new Vector3(-1.0f, 1.2f, 1.1f), new Vector3(0.05f, 1.6f, 0.05f), _wood, new Vector3(0f, 0f, 8f), false);
                    K.Box("Sensenblatt", t, new Vector3(-0.8f, 1.95f, 1.05f), new Vector3(0.6f, 0.06f, 0.03f), _iron, new Vector3(0f, 0f, -15f), false);
                    K.Box("Rechen", t, new Vector3(0.6f, 1.1f, 1.1f), new Vector3(0.05f, 1.7f, 0.05f), _wood, new Vector3(0f, 0f, -6f), false);
                    K.Block("Bank", t, new Vector3(0f, 0f, 0.6f), new Vector3(2.4f, 0.45f, 0.4f), _woodOld);
                    K.Prim(PrimitiveType.Cylinder, "Wasserkrug", t, new Vector3(0.9f, 0.6f, 0.6f), new Vector3(0.25f, 0.15f, 0.25f), Vector3.zero, _soil, false);
                    _pieces += 10;
                    break;
                }
                case "wuehlspuren":
                {
                    // Furchenwühler waren hier: aufgewühlte Erdhaufen vom Zaunbruch quer durch die Saatstreifen (Grund für die ersten
                    // Kämpfe). Mit Linie: entlang ihres Wegs, seitlich gestreut; sonst im Kreis.
                    t = K.Group("Spur: Wühlspuren der Furchenwühler", _root);
                    bool linie = sp.linie != null && sp.linie.Length >= 4;
                    for (int i = 0; i < sp.anzahl; i++)
                    {
                        float x, z;
                        if (linie)
                        {
                            Vector2 c = OnLine(sp.linie, (i + R(rng, 0.1f, 0.9f)) / sp.anzahl), c2 = OnLine(sp.linie, Mathf.Min(1f, (i + 1f) / sp.anzahl));
                            Vector2 dd = c2 - c, nr = dd.sqrMagnitude > 1e-4f ? new Vector2(-dd.y, dd.x).normalized : Vector2.right;
                            Vector2 q = c + nr * R(rng, -sp.r, sp.r);
                            x = q.x; z = q.y;
                        }
                        else
                        {
                            float a = R(rng, 0f, Mathf.PI * 2f), rr = sp.r * Mathf.Sqrt((float)rng.NextDouble());
                            x = sp.x + Mathf.Cos(a) * rr; z = sp.z + Mathf.Sin(a) * rr;
                        }
                        float s3 = R(rng, 0.5f, 1.1f);
                        K.Prim(PrimitiveType.Sphere, "Erdhaufen", t, P(x, z, -0.1f), new Vector3(s3 * 1.4f, s3 * 0.45f, s3), new Vector3(0f, R(rng, 0f, 180f), 0f), _soil, false);
                        _pieces++;
                    }
                    break;
                }
                case "zaunbruch":
                {
                    // Feldzaun gegen die Wühler: an einer Stelle umgedrückt, Latten liegen im Gras.
                    t = K.Group("Spur: Zaun, durchbrochen", _root);
                    int n = 0;
                    for (float u = 0f; u <= 1.0001f; u += 0.07f, n++)
                    {
                        Vector2 c = OnLine(sp.linie, u), c2 = OnLine(sp.linie, Mathf.Min(1f, u + 0.07f));
                        bool broken = sp.gesetzt > 0f ? Mathf.Abs(u - sp.gesetzt) < Mathf.Max(sp.r, 0.06f) : u > 0.4f && u < 0.62f;
                        float hd = Mathf.Atan2(c2.x - c.x, c2.y - c.y) * Mathf.Rad2Deg;
                        GameObject post = K.Block("Zaunpfahl", t, P(c.x, c.y, -0.2f), new Vector3(0.14f, 1.3f, 0.14f), _woodOld);
                        if (broken) post.transform.rotation = Quaternion.Euler(R(rng, 30f, 70f), hd, 0f);
                        if (!broken && u < 0.99f)
                        {
                            Vector3 A = P(c.x, c.y, 0.75f), B = P(c2.x, c2.y, 0.75f);
                            GameObject latte = K.Box("Latte", t, (A + B) * 0.5f, new Vector3(0.06f, 0.1f, Vector3.Distance(A, B)), _wood, default, false);
                            latte.transform.rotation = Quaternion.LookRotation(B - A);
                        }
                        else if (broken)
                            K.Box("Latte (im Gras)", t, P(c.x, c.y, 0.05f) + new Vector3(R(rng, -0.8f, 0.8f), 0f, R(rng, -0.8f, 0.8f)), new Vector3(0.06f, 0.06f, 1.4f), _wood, new Vector3(0f, R(rng, 0f, 180f), 0f), false);
                    }
                    _pieces += n * 2;
                    break;
                }
                case "steinhaufen":
                {
                    // Das Lesen hört nie auf: Haufen frisch gelesener Steine am oberen Feldrand, noch nicht verbaut.
                    t = K.Group("Spur: Lesesteinhaufen (noch nicht verbaut)", _root, P(sp.x, sp.z), Vector3.zero);
                    for (int hpile = 0; hpile < Mathf.Max(1, sp.anzahl); hpile++)
                    {
                        Vector3 c = new Vector3(hpile * 3.2f - 3f, 0f, R(rng, -1f, 1f));
                        for (int i = 0; i < 14; i++)
                        {
                            float s3 = R(rng, 0.3f, 0.55f);
                            K.Box("Lesestein", t, c + new Vector3(R(rng, -0.9f, 0.9f), R(rng, 0f, 0.7f), R(rng, -0.8f, 0.8f)), new Vector3(s3, s3 * 0.7f, s3 * 0.9f), _stone, new Vector3(R(rng, 0f, 40f), R(rng, 0f, 180f), R(rng, 0f, 40f)), false);
                        }
                    }
                    K.Box("Tragkorb", t, new Vector3(2.4f, 0.3f, 1.6f), new Vector3(0.55f, 0.6f, 0.45f), _straw, new Vector3(0f, 0f, 70f), false);
                    _pieces += 45;
                    break;
                }
                case "bruecke":
                {
                    // Untersteg: Brücke mit Steinwangen. An der Wange Hochwasserkerben mit Strichen – das höchste Wasser
                    // stand weit über dem heutigen Bach (WAS FEHLT: das Wasser von damals).
                    // Seit 05.10.: h = Fahrbahn aus dem Längsprofil der Straße, gesetzt = Bachbett; Widerlager auf beiden Ufern,
                    // ein Holzjoch in der Mitte bei mehr als 10 m Spannweite.
                    float len = sp.d, wid = sp.w;
                    Vector3 c0 = new Vector3(sp.x, 0f, sp.z);
                    Quaternion q = Quaternion.Euler(0f, sp.yaw, 0f);
                    Vector3 fw = q * Vector3.forward;
                    bool profil = sp.h != 0f || sp.gesetzt != 0f;
                    Vector3 endA = c0 - fw * (len * 0.5f), endB = c0 + fw * (len * 0.5f);
                    float deck = profil ? sp.h : Mathf.Max(G(endA.x, endA.z), G(endB.x, endB.z)) + 0.35f;
                    float bed = profil ? sp.gesetzt : G(c0.x, c0.z);
                    t = K.Group("Spur: Brücke über den Muldenbach (mit Hochwasserkerben)", _root, new Vector3(c0.x, deck, c0.z), new Vector3(0f, sp.yaw, 0f));
                    K.Box("Fahrbahn (Bohlen)", t, new Vector3(0f, -0.175f, 0f), new Vector3(wid, 0.35f, len), _wood);
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        K.Box("Längsträger", t, new Vector3(s2 * (wid * 0.5f - 0.5f), -0.55f, 0f), new Vector3(0.45f, 0.45f, len), _woodDark, default, false);
                        K.Box("Geländer", t, new Vector3(s2 * (wid * 0.5f - 0.1f), 1.0f, 0f), new Vector3(0.14f, 0.14f, len), _woodOld, default, false);
                        for (float zz = -len * 0.5f + 1f; zz <= len * 0.5f - 1f; zz += 2.4f)
                            K.Box("Geländerpfosten", t, new Vector3(s2 * (wid * 0.5f - 0.1f), 0.5f, zz), new Vector3(0.14f, 1.0f, 0.14f), _woodOld, default, false);
                    }
                    // Widerlager (Steinwangen) auf beiden Ufern: vom Boden darunter bis unter die Fahrbahn
                    float bodenA = 0f;
                    for (int e = -1; e <= 1; e += 2)
                    {
                        float zc = e * (len * 0.5f - 1.3f);
                        Vector3 wp = t.TransformPoint(new Vector3(0f, 0f, zc));
                        float yb = Mathf.Min(G(wp.x, wp.z) - deck - 0.8f, -1.2f);
                        if (e < 0) bodenA = yb;
                        K.Block("Steinwange", t, new Vector3(0f, yb, zc), new Vector3(wid + 1.2f, -0.35f - yb, 2.6f), _moss);
                    }
                    // Bei mehr als 10 m Spannweite ein gemauerter Mittelpfeiler im Bach (längs zur Strömung, mit Vorköpfen). Die
                    // Hochwasserkerben stehen an seiner Flanke (vom Ufer aus zu sehen), sonst an der Flanke des Widerlagers.
                    int nk = Mathf.Max(3, sp.kerben);
                    if (len - 2.4f > 10f)
                    {
                        float yb = bed - deck - 0.6f, hp = -0.78f - yb;
                        K.Block("Mittelpfeiler", t, new Vector3(0f, yb, 0f), new Vector3(wid + 0.6f, hp, 1.6f), _moss);
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                            K.Block("Vorkopf", t, new Vector3(s2 * (wid * 0.5f + 0.3f), yb, 0f), new Vector3(1.13f, Mathf.Min(hp, bed - deck + 2.6f - yb), 1.13f), _stone, 45f);
                        for (int k = 0; k < nk; k++)
                        {
                            float hy = bed - deck + 1.0f + k * (-0.8f - (bed - deck + 1.0f)) / (nk - 1) + R(rng, -0.1f, 0.1f);
                            K.Box("Hochwasserkerbe", t, new Vector3(R(rng, -1.6f, 1.6f), hy, -0.82f), new Vector3(R(rng, 0.4f, 0.9f), 0.05f, 0.03f), _soot, default, false);
                        }
                    }
                    else
                        for (int k = 0; k < nk; k++)
                        {
                            float hy = bodenA + 0.3f + k * (-0.6f - bodenA - 0.3f) / (nk - 1) + R(rng, -0.05f, 0.05f);
                            K.Box("Hochwasserkerbe", t, new Vector3(wid * 0.5f + 0.62f, hy, -len * 0.5f + 1.3f + R(rng, -0.6f, 0.6f)), new Vector3(0.03f, 0.05f, R(rng, 0.4f, 0.9f)), _soot, default, false);
                        }
                    _pieces += 34;
                    break;
                }
                case "bachwasser":
                {
                    // Wasser in den Bachbetten (vorher nur Täler ohne Wasser). Stücke von 5 m, je auf die Geländehöhe gesetzt.
                    var water = K.Mat("KV4 Bachwasser", new Color(0.24f, 0.36f, 0.4f), 0.85f);
                    t = K.Group("Spur: Bachwasser", _root);
                    for (int i = 0; i + 3 < sp.linie.Length; i += 2)
                    {
                        float ax = sp.linie[i], az = sp.linie[i + 1], bx = sp.linie[i + 2], bz = sp.linie[i + 3];
                        Vector3 A = new Vector3(ax, G(ax, az) + 0.22f, az), B = new Vector3(bx, G(bx, bz) + 0.22f, bz);
                        if ((B - A).sqrMagnitude < 0.01f) continue;
                        GameObject seg = K.Box("Wasser", t, (A + B) * 0.5f, new Vector3(sp.breite, 0.05f, Vector3.Distance(A, B) + 0.4f), water, default, false);
                        seg.transform.rotation = Quaternion.LookRotation(B - A);
                        _pieces++;
                    }
                    break;
                }
                case "alter_steg":
                {
                    // VORHER: der alte Fußgängersteg. Pfähle stehen noch im Wasser und am Ufer, schief; zwei Bohlen hängen, der
                    // Rest fehlt. Jeder Pfahl steht auf seinem eigenen Boden (Bachbett oder Ufer).
                    Vector3 c0 = P(sp.x, sp.z);
                    t = K.Group("Spur: Reste des alten Stegs", _root, c0, new Vector3(0f, sp.yaw, 0f));
                    for (int i = 0; i < 6; i++)
                    {
                        float zz = -sp.d * 0.5f + i * sp.d / 5f;
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                        {
                            if (rng.NextDouble() < 0.25) continue;
                            float hh = R(rng, 1.2f, 2.4f), gy = GL(t, s2 * 0.8f, zz);
                            K.Prim(PrimitiveType.Cylinder, "Stegpfahl (alt)", t, new Vector3(s2 * 0.8f, gy + hh * 0.5f - 0.6f, zz), new Vector3(0.22f, hh * 0.5f, 0.22f),
                                new Vector3(R(rng, -12f, 12f), 0f, R(rng, -12f, 12f)), _woodOld, false);
                        }
                    }
                    float ga = GL(t, 0.3f, -sp.d * 0.5f + 1.5f), gb = GL(t, -0.2f, sp.d * 0.5f - 1.8f);
                    K.Box("hängende Bohle", t, new Vector3(0.3f, ga + 0.5f, -sp.d * 0.5f + 1.5f), new Vector3(0.4f, 0.06f, 2.6f), _woodOld, new Vector3(18f, 0f, 6f), false);
                    K.Box("hängende Bohle", t, new Vector3(-0.2f, gb + 0.6f, sp.d * 0.5f - 1.8f), new Vector3(0.4f, 0.06f, 2.4f), _woodOld, new Vector3(-24f, 0f, -4f), false);
                    _pieces += 14;
                    break;
                }
                case "rohbau":
                {
                    // WEITER: Ein Haus wird gerichtet – Fachwerk ohne Wände, Dachsparren, Richtkranz auf dem First, Leiter.
                    Vector3 c0 = P(sp.x, sp.z);
                    t = K.Group("Spur: Haus im Bau (Richtkranz)", _root, c0, new Vector3(0f, sp.yaw, 0f));
                    float w = sp.w, d = sp.d, h = sp.h;
                    K.Block("Steinsockel (fertig)", t, new Vector3(0f, -0.5f, 0f), new Vector3(w + 0.3f, 0.9f, d + 0.3f), _stone);
                    for (int ix = -1; ix <= 1; ix++)
                        for (int iz = -1; iz <= 1; iz += 2)
                            K.Block("Ständer", t, new Vector3(ix * (w * 0.5f - 0.15f), 0.4f, iz * (d * 0.5f - 0.15f)), new Vector3(0.24f, h, 0.24f), _woodNew);
                    for (int iz = -1; iz <= 1; iz += 2)
                        K.Box("Rähm", t, new Vector3(0f, 0.4f + h, iz * (d * 0.5f - 0.15f)), new Vector3(w, 0.22f, 0.24f), _woodNew, default, false);
                    for (int ix = -2; ix <= 2; ix++)
                        for (int iz = -1; iz <= 1; iz += 2)
                            K.Box("Sparren", t, new Vector3(ix * w * 0.22f, 0.4f + h + d * 0.22f, iz * d * 0.25f), new Vector3(0.14f, 0.14f, d * 0.62f), _woodNew,
                                new Vector3(iz * 38f, 0f, 0f), false);
                    K.Box("First", t, new Vector3(0f, 0.4f + h + d * 0.42f, 0f), new Vector3(w, 0.18f, 0.18f), _woodNew, default, false);
                    K.Prim(PrimitiveType.Sphere, "Richtkranz", t, new Vector3(w * 0.4f, 0.4f + h + d * 0.42f + 0.4f, 0f), new Vector3(0.7f, 0.7f, 0.25f), Vector3.zero, _green, false);
                    K.Box("Band am Kranz", t, new Vector3(w * 0.4f, 0.4f + h + d * 0.42f - 0.2f, 0.05f), new Vector3(0.08f, 0.8f, 0.02f), _cloth1, default, false);
                    K.Ramp("Leiter", t, new Vector3(-w * 0.5f - 1.2f, 0f, -d * 0.5f - 0.4f), 90f, 70f, h + 0.6f, 0.5f, _woodOld);
                    _pieces += 30;
                    break;
                }
                case "achse":
                {
                    // Karrenhof: die gebrochene Achse liegt seit Jahren in der Ecke, ein Rad lehnt daneben – niemand räumt sie weg.
                    t = K.Group("Spur: gebrochene Achse", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    K.Prim(PrimitiveType.Cylinder, "Achse (Bruchstück)", t, new Vector3(0f, 0.12f, 0f), new Vector3(0.16f, 0.9f, 0.16f), new Vector3(0f, 0f, 90f), _woodOld, false);
                    K.Prim(PrimitiveType.Cylinder, "Achse (Splitter)", t, new Vector3(1.1f, 0.12f, 0.3f), new Vector3(0.14f, 0.35f, 0.14f), new Vector3(0f, 25f, 90f), _woodOld, false);
                    K.Prim(PrimitiveType.Cylinder, "Rad (angelehnt)", t, new Vector3(-0.6f, 0.58f, 0.6f), new Vector3(1.15f, 0.05f, 1.15f), new Vector3(75f, 0f, 0f), _woodDark, false);
                    K.Box("Gras über der Achse", t, new Vector3(0.2f, 0.25f, 0f), new Vector3(0.6f, 0.3f, 0.4f), _green, default, false);
                    _pieces += 4;
                    break;
                }
                // ---------------------------------------------------------- 05.10.: Ausstattung mit Sinn (ausstattung.py)
                case "sitz":
                {
                    // Sitz am Wachtfeuer, über Jahre zusammengetragen: tangential zum Feuer (yaw = Peilung vom Feuer, Längsachse
                    // lokal x), Lücken an den Trampelpfaden und im Rauch (NO). anzahl: 0 Stamm · 1 Bank · 2 Hackklotz des Feuerwächters.
                    t = K.Group("Spur: Sitz am Wachtfeuer", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    float len = sp.w > 0.3f ? sp.w : 2.0f;
                    if (sp.anzahl == 1)
                    {
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                            K.Prim(PrimitiveType.Cylinder, "Bankklotz", t, new Vector3(s2 * (len * 0.5f - 0.35f), 0.2f, 0f), new Vector3(0.42f, 0.22f, 0.42f), Vector3.zero, _wood);
                        K.Box("Sitzbohle", t, new Vector3(0f, 0.47f, 0f), new Vector3(len, 0.08f, 0.38f), _woodOld, new Vector3(0f, 0f, R(rng, -1f, 1f)));
                    }
                    else if (sp.anzahl == 2)
                    {
                        K.Prim(PrimitiveType.Cylinder, "Hackklotz (Sitz des Feuerwächters)", t, new Vector3(0f, 0.24f, 0f), new Vector3(0.62f, 0.25f, 0.62f), Vector3.zero, _wood);
                        K.Prim(PrimitiveType.Cylinder, "Schnittfläche", t, new Vector3(0f, 0.495f, 0f), new Vector3(0.56f, 0.006f, 0.56f), Vector3.zero, _woodNew, false);
                        K.Box("Schürhaken", t, new Vector3(0.45f, 0.45f, 0.25f), new Vector3(0.03f, 0.03f, 1.2f), _iron, new Vector3(-50f, 20f, 0f), false);
                    }
                    else
                    {
                        // Stamm: Modell (3 m, Achse lokal x) auf die Länge gestaucht, Dicke 0,85 → Sitzhöhe ≈ 0,48 m
                        GameObject lg = Model("SM_Log", t, Vector3.zero, 0f, 1f, ArtModels.ColliderKind.Box);
                        if (lg != null) lg.transform.localScale = new Vector3(len / 3f, 0.85f, 0.85f);
                        else K.Prim(PrimitiveType.Cylinder, "Sitzstamm", t, new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, len * 0.5f, 0.5f), new Vector3(0f, 0f, 90f), _wood);
                    }
                    _pieces += 3;
                    break;
                }
                case "feuerholz":
                {
                    // Feuerholz neben dem Hackklotz des Feuerwächters, quer zum Wind (nicht im Funkenflug), dazu ein Korb Reisig
                    t = K.Group("Spur: Feuerholz am Wachtfeuer", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    for (int row = 0; row < 3; row++)
                        for (int i = 0; i < 5 - row; i++)
                            K.Box("Scheit", t, new Vector3((i - (4 - row) * 0.5f) * 0.24f, 0.1f + row * 0.19f, 0f), new Vector3(0.18f, 0.18f, R(rng, 0.7f, 0.85f)),
                                rng.NextDouble() < 0.3 ? _woodNew : _wood, new Vector3(0f, R(rng, -4f, 4f), R(rng, 0f, 40f)), false);
                    K.Prim(PrimitiveType.Cylinder, "Reisigkorb", t, new Vector3(0.95f, 0.25f, 0.2f), new Vector3(0.55f, 0.25f, 0.55f), Vector3.zero, _straw, false);
                    for (int i = 0; i < 5; i++)
                        K.Box("Reisig", t, new Vector3(0.95f + R(rng, -0.15f, 0.15f), 0.52f, 0.2f + R(rng, -0.15f, 0.15f)), new Vector3(0.02f, 0.02f, R(rng, 0.5f, 0.8f)), _woodDark, new Vector3(R(rng, -20f, 20f), R(rng, 0f, 180f), 0f), false);
                    var col = new GameObject("Feuerholz Kollision");
                    col.transform.SetParent(t, false);
                    col.transform.localPosition = new Vector3(0.3f, 0.3f, 0f);
                    col.AddComponent<BoxCollider>().size = new Vector3(1.9f, 0.6f, 0.9f);
                    _pieces += 18;
                    break;
                }
                case "asche":
                {
                    // Asche und Kohlereste, vom Hauptwind (SW) zur Rauchseite (NO) verweht: ein dunkler Fächer über den Steinring
                    // hinaus, keine runde Scheibe.
                    t = K.Group("Spur: Asche (zur Rauchseite verweht)", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    for (int i = 0; i < 9; i++)
                    {
                        float u = R(rng, 0f, 1f);
                        K.Prim(PrimitiveType.Cylinder, "Ascheschleier", t, new Vector3(R(rng, -1f, 1f) * (0.3f + u * 0.8f), 0.006f + i * 0.0015f, u * sp.r * 1.4f),
                            new Vector3(R(rng, 0.6f, 1.2f) * (1f - u * 0.4f), 0.004f, R(rng, 0.8f, 1.4f)), new Vector3(0f, R(rng, -20f, 20f), 0f), _soot, false);
                    }
                    for (int i = 0; i < 4; i++)
                        K.Prim(PrimitiveType.Cylinder, "Kohlerest", t, new Vector3(R(rng, -0.5f, 0.5f), 0.04f, R(rng, 0.3f, 1.6f)), new Vector3(0.08f, 0.12f, 0.08f), new Vector3(0f, R(rng, 0f, 180f), 90f), _soot, false);
                    _pieces += 13;
                    break;
                }
                case "zelt":
                {
                    // Zelt für Neuankömmlinge ohne Platz im Schlafhaus: im Windschatten, Eingang (Modell lokal +z) nach NO – Rücken zum
                    // Hauptwind. Kein eigenes Feuer: gekocht wird am Wachtfeuer. Abgespannt, Bündel und Decke vor dem Eingang.
                    t = K.Group("Spur: Zelt", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    if (Model("SM_Tent", t, Vector3.zero, 0f, 1f, ArtModels.ColliderKind.Mesh) == null)
                        K.Box("Zelt", t, new Vector3(0f, 1f, 0f), new Vector3(3f, 2f, 3.6f), _cloth3);
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        for (int k = -1; k <= 1; k += 2)
                        {
                            Vector3 peg = new Vector3(s2 * 2.5f, 0f, k * 1.45f);
                            peg.y = GL(t, peg.x, peg.z);
                            K.Box("Hering", t, peg + Vector3.up * 0.08f, new Vector3(0.04f, 0.22f, 0.04f), _woodNew, new Vector3(0f, 0f, s2 * -20f), false);
                            Seil(t, new Vector3(s2 * 1.1f, 0.69f, k * 1.2f), peg + Vector3.up * 0.15f, 0.015f, _rope, "Abspannleine");
                        }
                    float gv = GL(t, 0.8f, 2.4f);
                    K.Box("Bündel", t, new Vector3(0.8f, gv + 0.18f, 2.4f), new Vector3(0.5f, 0.36f, 0.35f), _cloth2, new Vector3(0f, R(rng, 0f, 40f), 0f), false);
                    K.Box("Decke (zum Lüften)", t, new Vector3(-0.7f, GL(t, -0.7f, 2.5f) + 0.03f, 2.5f), new Vector3(1.1f, 0.04f, 0.8f), rng.NextDouble() < 0.5 ? _cloth1 : _linen, new Vector3(0f, R(rng, -20f, 20f), 0f), false);
                    _pieces += 12;
                    break;
                }
                case "zielscheibe":
                {
                    // Bogenbahn: Strohscheibe auf einem Dreibein, Front (lokal +z) zur Schusslinie, leicht zurückgelehnt; bemalte
                    // Ringe, drei Pfeile nahe der Mitte. Dahinter der Kugelfang (Strohballen) und die Palisade.
                    t = K.Group("Spur: Zielscheibe", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    const float hc = 1.35f;
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        K.Box("Bein", t, new Vector3(s2 * 0.55f, 0.85f, 0.12f), new Vector3(0.08f, 1.8f, 0.08f), _woodOld, new Vector3(-8f, 0f, s2 * 12f), false);
                    K.Box("Stützbein", t, new Vector3(0f, 0.8f, -0.55f), new Vector3(0.08f, 1.75f, 0.08f), _woodOld, new Vector3(28f, 0f, 0f), false);
                    Quaternion qs = Quaternion.Euler(80f, 0f, 0f);
                    Vector3 achse = qs * Vector3.up, mitte = new Vector3(0f, hc, 0.22f);
                    K.Prim(PrimitiveType.Cylinder, "Strohscheibe", t, mitte, new Vector3(1.25f, 0.1f, 1.25f), new Vector3(80f, 0f, 0f), _straw);
                    Material[] ringe = { _linen, _cloth1, _linen, _soot };
                    for (int k = 0; k < 4; k++)
                    {
                        float rr = 1.0f - k * 0.24f;
                        K.Prim(PrimitiveType.Cylinder, "Ring", t, mitte + achse * (0.101f + k * 0.003f), new Vector3(rr, 0.002f, rr), new Vector3(80f, 0f, 0f), ringe[k], false);
                    }
                    Vector3 rechts = Vector3.right, oben2 = qs * Vector3.forward * -1f;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 p0 = mitte + rechts * R(rng, -0.3f, 0.3f) + oben2 * R(rng, -0.3f, 0.3f) + achse * 0.1f;
                        Vector3 dir = (achse + new Vector3(R(rng, -0.12f, 0.12f), R(rng, -0.1f, 0.15f), 0f)).normalized;
                        Seil(t, p0 - dir * 0.08f, p0 + dir * 0.62f, 0.015f, _woodNew, "Pfeil");
                    }
                    _pieces += 12;
                    break;
                }
                case "strohballen":
                {
                    // Kugelfang hinter den Scheiben: Strohballen in zwei Lagen (Reihe lokal x), vorn verirrte Pfeile
                    t = K.Group("Spur: Strohballen (Kugelfang)", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    float len = sp.w > 0.5f ? sp.w : 6f;
                    int n = Mathf.Max(2, Mathf.RoundToInt(len / 1.12f));
                    for (int lage = 0; lage < 2; lage++)
                        for (int i = 0; i < n - lage; i++)
                        {
                            float x = (i - (n - lage - 1) * 0.5f) * 1.12f;
                            K.Box("Strohballen", t, new Vector3(x + R(rng, -0.04f, 0.04f), GL(t, x, 0f) + 0.25f + lage * 0.52f, R(rng, -0.05f, 0.05f)), new Vector3(1.05f, 0.52f, 0.6f), _straw,
                                new Vector3(0f, R(rng, -3f, 3f), 0f), lage == 0);
                        }
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p0 = new Vector3(R(rng, -len * 0.45f, len * 0.45f), R(rng, 0.3f, 0.9f), 0.31f);
                        Seil(t, p0 - Vector3.forward * 0.05f, p0 + new Vector3(R(rng, -0.1f, 0.1f), R(rng, -0.05f, 0.1f), 0.6f), 0.015f, _woodNew, "verirrter Pfeil");
                    }
                    _pieces += 2 * n + 4;
                    break;
                }
                case "schusslinie":
                {
                    // Schusslinie der Bogenbahn: ein Balken am Boden; am Südende ein Bogenständer, am Nordende ein Eimer mit Pfeilen
                    t = K.Group("Spur: Schusslinie", _root);
                    Vector3 A = P(sp.linie[0], sp.linie[1], 0.07f), B = P(sp.linie[2], sp.linie[3], 0.07f);
                    Seil(t, A, B, 0.14f, _woodOld, "Schusslinie (Balken)", true);
                    Vector3 dirL = (B - A); dirL.y = 0f; dirL.Normalize();
                    Vector3 hinten = new Vector3(-dirL.z, 0f, dirL.x);                 // links der Linie
                    if (Vector3.Dot(hinten, Vector3.right) > 0f) hinten = -hinten;    // hinter der Linie = von den Scheiben weg (Westen)
                    Vector3 bs = A - dirL * 1.3f + hinten * 0.8f; bs.y = G(bs.x, bs.z);
                    Transform st = K.Group("Bogenständer", t, bs, new Vector3(0f, Mathf.Atan2(dirL.x, dirL.z) * Mathf.Rad2Deg, 0f));
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        K.Block("Pfosten", st, new Vector3(0f, 0f, s2 * 0.7f), new Vector3(0.1f, 1.4f, 0.1f), _woodDark);
                    K.Box("Querholz", st, new Vector3(0f, 1.3f, 0f), new Vector3(0.08f, 0.08f, 1.6f), _woodDark, default, false);
                    for (int i = 0; i < 3; i++)
                        K.Box("Bogen", st, new Vector3(0.12f, 0.72f, -0.4f + i * 0.4f), new Vector3(0.04f, 1.4f, 0.05f), _wood, new Vector3(0f, 0f, -10f), false);
                    Vector3 em = B + dirL * 0.9f + hinten * 0.6f; em.y = G(em.x, em.z);
                    K.Prim(PrimitiveType.Cylinder, "Pfeileimer", t, em + Vector3.up * 0.25f, new Vector3(0.4f, 0.25f, 0.4f), Vector3.zero, _wood, false);
                    for (int i = 0; i < 7; i++)
                        Seil(t, em + new Vector3(R(rng, -0.12f, 0.12f), 0.15f, R(rng, -0.12f, 0.12f)), em + new Vector3(R(rng, -0.25f, 0.25f), 0.95f, R(rng, -0.25f, 0.25f)), 0.015f, _woodNew, "Pfeil");
                    _pieces += 16;
                    break;
                }
                case "uebungspuppe":
                {
                    // Übungspuppe: Pfahl, Strohleib, Querholz, Sackkopf; Front (lokal +z) zum Übenden, dort ist der Boden zertreten
                    t = K.Group("Spur: Übungspuppe", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    K.Block("Pfahl", t, new Vector3(0f, -0.4f, 0f), new Vector3(0.2f, 2.3f, 0.2f), _woodDark);
                    K.Box("Strohleib", t, new Vector3(0f, 1.3f, 0f), new Vector3(0.55f, 0.85f, 0.38f), _straw);
                    K.Box("Querholz (Arme)", t, new Vector3(0f, 1.6f, 0f), new Vector3(1.3f, 0.12f, 0.12f), _wood, new Vector3(0f, 0f, R(rng, -6f, 6f)), false);
                    K.Prim(PrimitiveType.Sphere, "Kopf (Sack)", t, new Vector3(0f, 2.0f, 0f), new Vector3(0.36f, 0.4f, 0.34f), Vector3.zero, _linen, false);
                    for (int i = 0; i < 3; i++)
                        K.Box("Hiebspur", t, new Vector3(R(rng, -0.15f, 0.15f), R(rng, 1.05f, 1.55f), 0.195f), new Vector3(R(rng, 0.2f, 0.35f), 0.035f, 0.01f), _soot, new Vector3(0f, 0f, R(rng, 20f, 45f)), false);
                    K.Prim(PrimitiveType.Cylinder, "zertretener Boden", t, new Vector3(0f, 0.008f, 0.8f), new Vector3(2.2f, 0.006f, 2.8f), Vector3.zero, _soil, false);
                    for (int i = 0; i < 6; i++)
                        K.Box("Strohhalme", t, new Vector3(R(rng, -0.8f, 0.8f), 0.02f, R(rng, -0.3f, 1.2f)), new Vector3(0.25f, 0.015f, 0.04f), _straw, new Vector3(0f, R(rng, 0f, 180f), 0f), false);
                    _pieces += 14;
                    break;
                }
                case "waffenstaender":
                {
                    // Waffenständer vor dem Übungsschuppen der Grundlehrer (Längsachse lokal z, parallel zur Schuppenfront):
                    // Holzschwerter und Stäbe, ein Übungsschild lehnt daneben
                    t = K.Group("Spur: Waffenständer", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        K.Block("Bock", t, new Vector3(0f, 0f, s2 * 1.1f), new Vector3(0.5f, 1.3f, 0.12f), _woodDark);
                    K.Box("Auflage oben", t, new Vector3(0f, 1.25f, 0f), new Vector3(0.12f, 0.1f, 2.4f), _wood, default, false);
                    K.Box("Auflage unten", t, new Vector3(0f, 0.25f, 0f), new Vector3(0.35f, 0.1f, 2.4f), _wood, default, false);
                    for (int i = 0; i < 6; i++)
                    {
                        bool stab = i % 3 == 2;
                        K.Box(stab ? "Übungsstab" : "Holzschwert", t, new Vector3(R(rng, -0.08f, 0.08f), stab ? 1.0f : 0.75f, -0.9f + i * 0.36f), new Vector3(stab ? 0.05f : 0.07f, stab ? 1.9f : 1.1f, 0.03f), _wood,
                            new Vector3(0f, 0f, R(rng, -12f, 12f)), false);
                    }
                    K.Prim(PrimitiveType.Cylinder, "Übungsschild", t, new Vector3(0.32f, 0.45f, 1.35f), new Vector3(0.7f, 0.03f, 0.7f), new Vector3(0f, 0f, 75f), _woodOld, false);
                    var col = new GameObject("Waffenständer Kollision");
                    col.transform.SetParent(t, false);
                    col.transform.localPosition = new Vector3(0f, 0.7f, 0f);
                    col.AddComponent<BoxCollider>().size = new Vector3(0.6f, 1.4f, 2.4f);
                    _pieces += 12;
                    break;
                }
                case "fass":
                {
                    // Fass neben dem Waffenständer; anzahl 1 = Übungswaffen ragen heraus
                    t = K.Group("Spur: Fass", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    if (Model("SM_Barrel", t, Vector3.zero, 0f, 1f, ArtModels.ColliderKind.Capsule) == null)
                        K.Prim(PrimitiveType.Cylinder, "Fass", t, new Vector3(0f, 0.5f, 0f), new Vector3(0.7f, 0.5f, 0.7f), Vector3.zero, _wood);
                    if (sp.anzahl == 1)
                        for (int i = 0; i < 5; i++)
                            K.Box(i % 2 == 0 ? "Übungsstab" : "Holzschwert", t, new Vector3(R(rng, -0.14f, 0.14f), 1.15f, R(rng, -0.14f, 0.14f)), new Vector3(0.05f, i % 2 == 0 ? 1.4f : 0.9f, 0.04f), _wood,
                                new Vector3(R(rng, -14f, 14f), R(rng, 0f, 180f), R(rng, -14f, 14f)), false);
                    _pieces += 6;
                    break;
                }
                case "hangmauer":
                {
                    // Stützmauer am Hangeinschnitt eines Hauses: Fuß unter der Sohle des Einschnitts (gesetzt), Krone auf dem Gelände
                    t = K.Group("Spur: Hangmauer", _root);
                    Vector3 A = new Vector3(sp.linie[0], 0f, sp.linie[1]), B = new Vector3(sp.linie[2], 0f, sp.linie[3]);
                    Vector3 dd = B - A;
                    if (dd.magnitude < 0.5f) break;
                    Vector3 c = (A + B) * 0.5f;
                    K.Box("Hangmauer", t, new Vector3(c.x, sp.gesetzt - 0.3f + (sp.h + 0.5f) * 0.5f, c.z), new Vector3(0.7f, sp.h + 0.5f, dd.magnitude), _stone,
                        new Vector3(0f, Mathf.Atan2(dd.x, dd.z) * Mathf.Rad2Deg, 0f));
                    _pieces++;
                    break;
                }
                case "steg":
                {
                    // Fußgängersteg am Bergsteig: Bohlen auf zwei Längsbalken (Oberkante = h aus dem Wegprofil), Pfahljoche, wo
                    // er mehr als 0,5 m über dem Boden liegt, Auflager aus Steinen an den Enden, ein Handlauf.
                    float len = sp.d, wid = sp.w > 0.5f ? sp.w : 1.4f;
                    t = K.Group("Spur: Steg am Bergsteig", _root, new Vector3(sp.x, sp.h, sp.z), new Vector3(0f, sp.yaw, 0f));
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        K.Box("Längsbalken", t, new Vector3(s2 * (wid * 0.5f - 0.12f), -0.19f, 0f), new Vector3(0.22f, 0.26f, len), _woodDark, default, false);
                    int nb = 0;
                    for (float z = -len * 0.5f + 0.15f; z < len * 0.5f; z += 0.32f, nb++)
                        K.Box("Bohle", t, new Vector3(R(rng, -0.03f, 0.03f), -0.03f, z), new Vector3(wid, 0.06f, 0.28f), _woodOld, new Vector3(0f, R(rng, -2f, 2f), 0f), false);
                    var col = new GameObject("Steg Kollision");
                    col.transform.SetParent(t, false);
                    col.transform.localPosition = new Vector3(0f, -0.15f, 0f);
                    col.AddComponent<BoxCollider>().size = new Vector3(wid, 0.3f, len);
                    for (float z = -len * 0.5f + 1.2f; z <= len * 0.5f - 1.2f; z += 3.2f)
                    {
                        float gy = GL(t, 0f, z);
                        if (gy > -0.8f) continue;
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                            K.Block("Jochpfahl", t, new Vector3(s2 * (wid * 0.5f - 0.12f), gy - 0.5f, z), new Vector3(0.2f, -0.32f - (gy - 0.5f), 0.2f), _woodDark);
                        K.Box("Jochholm", t, new Vector3(0f, -0.42f, z), new Vector3(wid + 0.3f, 0.18f, 0.22f), _woodDark, default, false);
                        nb += 3;
                    }
                    for (int e = -1; e <= 1; e += 2)
                    {
                        float zc = e * (len * 0.5f - 0.6f), gy = GL(t, 0f, zc);
                        K.Block("Auflager", t, new Vector3(0f, Mathf.Min(gy - 0.3f, -0.62f), zc), new Vector3(wid + 0.6f, -0.32f - Mathf.Min(gy - 0.3f, -0.62f), 1.2f), _stone);
                    }
                    // #29 Begehung (07.10.2026): Liegt das Ufer am Ende mehr als 0,35 m unter dem Deck, führt eine Bohlenrampe hinauf
                    // (flacher als 1 : 3; Regel „Sinn und Gelände“: Bohlenrampe für Karren höchstens 1 : 4). Vorher blieb die Figur am
                    // Ostende an einer Stufe von fast 1 m hängen (Rückweg von den Vorratswiesen ins Lager, K-A).
                    for (int e = -1; e <= 1; e += 2)
                    {
                        float z0 = e * len * 0.5f;
                        float gEnd = GL(t, 0f, z0 + e * 0.3f);
                        if (gEnd > -0.35f) continue;
                        float lr = Mathf.Clamp(-gEnd * 3.5f, 1.2f, 4.5f);
                        float gOut = Mathf.Min(GL(t, 0f, z0 + e * lr), -0.02f);
                        float lenR = Mathf.Sqrt(lr * lr + gOut * gOut);
                        float ang = e * Mathf.Atan2(-gOut, lr) * Mathf.Rad2Deg;
                        var mid = new Vector3(0f, gOut * 0.5f - 0.04f, z0 + e * lr * 0.5f);
                        K.Box("Bohlenrampe", t, mid, new Vector3(wid, 0.08f, lenR + 0.1f), _woodOld, new Vector3(ang, 0f, 0f));
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                            K.Box("Rampenbalken", t, mid + new Vector3(s2 * (wid * 0.5f - 0.12f), -0.13f, 0f), new Vector3(0.18f, 0.18f, lenR), _woodDark,
                                new Vector3(ang, 0f, 0f), false);
                        nb += 3;
                    }
                    for (float z = -len * 0.5f + 0.4f; z <= len * 0.5f - 0.39f; z += 2.4f)
                        K.Box("Handlaufpfosten", t, new Vector3(wid * 0.5f, 0.45f, z), new Vector3(0.1f, 0.9f, 0.1f), _woodOld, default, false);
                    K.Box("Handlauf", t, new Vector3(wid * 0.5f, 0.92f, 0f), new Vector3(0.08f, 0.08f, len - 0.6f), _woodOld, default, false);
                    _pieces += nb + 8;
                    break;
                }
                case "waschtrog":
                {
                    // Waschtrog am Dorfbrunnen (statt eines Waschplatzes 6 m über dem Wasser am Hang): Steintrog mit Wasser,
                    // Waschbrett, Korb mit Wäsche, Eimer
                    t = K.Group("Spur: Waschtrog am Brunnen", _root, P(sp.x, sp.z), new Vector3(0f, sp.yaw, 0f));
                    K.Block("Trogboden", t, new Vector3(0f, -0.25f, 0f), new Vector3(2.2f, 0.45f, 0.8f), _stone);
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        K.Box("Trogwand", t, new Vector3(0f, 0.475f, s2 * 0.325f), new Vector3(2.2f, 0.55f, 0.15f), _stone);
                        K.Box("Trogwand", t, new Vector3(s2 * 1.025f, 0.475f, 0f), new Vector3(0.15f, 0.55f, 0.5f), _stone);
                    }
                    K.Box("Wasser", t, new Vector3(0f, 0.62f, 0f), new Vector3(1.9f, 0.02f, 0.5f), _water, default, false);
                    K.Box("Waschbrett", t, new Vector3(0.55f, 0.8f, 0.05f), new Vector3(0.45f, 0.55f, 0.04f), _woodOld, new Vector3(-30f, 0f, 0f), false);
                    K.Box("Korb mit Wäsche", t, new Vector3(-1.6f, GL(t, -1.6f, 0.8f) + 0.25f, 0.8f), new Vector3(0.6f, 0.45f, 0.45f), _straw, default, false);
                    K.Prim(PrimitiveType.Cylinder, "Eimer", t, new Vector3(1.5f, GL(t, 1.5f, 0.7f) + 0.2f, 0.7f), new Vector3(0.32f, 0.2f, 0.32f), Vector3.zero, _wood, false);
                    _pieces += 9;
                    break;
                }
                case "felsstirn":
                    FelsStirn(sp, l);
                    break;
                case "muehlrad":
                {
                    // Unterschlächtiges Wasserrad (WORLD-244): Achse waagrecht in Richtung yaw (zum Mühlhaus), Nabe auf absoluter Höhe h,
                    // Radius r, Breite w. Zwei Felgen aus Bohlen, acht Speichen, Schaufeln; dreht sich langsam (SlowRotator, nur Bild –
                    // kein Netz, keine Kollision am drehenden Teil). Lager auf zwei Steinpfeilern im Bach.
                    t = K.Group("Spur: Mühlrad", _root, new Vector3(sp.x, sp.h, sp.z), new Vector3(0f, sp.yaw, 0f));
                    float r = sp.r > 0.5f ? sp.r : 2.6f, bw = sp.w > 0.3f ? sp.w : 1.2f;
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        float yb = G(t.TransformPoint(new Vector3(0f, 0f, s2 * (bw * 0.5f + 0.35f))).x, t.TransformPoint(new Vector3(0f, 0f, s2 * (bw * 0.5f + 0.35f))).z) - sp.h - 0.3f;
                        K.Block("Lagerpfeiler", t, new Vector3(0f, yb, s2 * (bw * 0.5f + 0.35f)), new Vector3(0.6f, -yb + 0.1f, 0.5f), _moss);
                    }
                    Transform rad = K.Group("Rad (dreht sich)", t, Vector3.zero, Vector3.zero);
                    const int seg = 18;
                    float bogen = 2f * Mathf.PI * r / seg;
                    for (int i = 0; i < seg; i++)
                    {
                        float a = i * 360f / seg;
                        Quaternion q = Quaternion.Euler(0f, 0f, a);
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                            K.Box("Felge", rad, q * new Vector3(0f, r, 0f) + new Vector3(0f, 0f, s2 * bw * 0.5f), new Vector3(bogen * 1.04f, 0.22f, 0.12f), _woodOld, new Vector3(0f, 0f, a), false);
                        K.Box("Schaufel", rad, q * new Vector3(0f, r + 0.28f, 0f), new Vector3(0.07f, 0.62f, bw), _wood, new Vector3(0f, 0f, a), false);
                    }
                    for (int i = 0; i < 8; i++)
                        for (int s2 = -1; s2 <= 1; s2 += 2)
                            K.Box("Speiche", rad, new Vector3(0f, 0f, s2 * bw * 0.5f), new Vector3(0.12f, r * 2f, 0.1f), _woodDark, new Vector3(0f, 0f, i * 22.5f), false);
                    K.Prim(PrimitiveType.Cylinder, "Nabe", rad, Vector3.zero, new Vector3(0.55f, bw * 0.5f + 0.1f, 0.55f), new Vector3(90f, 0f, 0f), _woodDark, false);
                    // Wellbaum vom Rad zum Haus (lokal −z = Richtung yaw + 180? Achse liegt auf lokal z): 3 m zum Haus hin
                    K.Prim(PrimitiveType.Cylinder, "Wellbaum", t, new Vector3(0f, 0f, -(bw * 0.5f + 1.6f)), new Vector3(0.32f, 1.6f, 0.32f), new Vector3(90f, 0f, 0f), _woodDark, false);
                    K.MakeDynamic(rad.gameObject);
                    var rot = rad.gameObject.AddComponent<SlowRotator>();
                    K.SetVector3(rot, "localAxis", Vector3.forward);
                    K.SetFloat(rot, "degreesPerSecond", -16f);
                    // Gischt, wo die Schaufeln ins Wasser tauchen
                    K.Particles("Spritzwasser", t, new Vector3(0f, -r + 0.3f, 0f), new Color(0.85f, 0.9f, 0.95f, 0.35f), 0.35f, 1.2f, 0.6f, 1.6f, 14f, 0.4f);
                    _pieces += seg * 3 + 20;
                    break;
                }
                case "wehr":
                {
                    // kleines Bohlenwehr quer im Bach oberhalb der Mühle: staut das Wasser vor dem Rad
                    t = K.Group("Spur: Wehr", _root, new Vector3(sp.x, sp.h, sp.z), new Vector3(0f, sp.yaw, 0f));
                    float bw = sp.w > 1f ? sp.w : 5f;
                    for (int i = 0; i < 3; i++)
                        K.Box("Wehrbohle", t, new Vector3(0f, 0.12f + i * 0.2f, 0f), new Vector3(bw, 0.18f, 0.12f), i == 2 ? _woodOld : _woodDark, new Vector3(0f, 0f, R(rng, -1.5f, 1.5f)), i < 2);
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        K.Block("Wehrpfahl", t, new Vector3(s2 * bw * 0.5f, -0.4f, 0f), new Vector3(0.22f, 1.4f, 0.22f), _woodDark);
                    _pieces += 5;
                    break;
                }
                case "feldzaun":
                {
                    // Feldzaun (Heuwiese): Pfosten alle ≈ 2,6 m auf dem Gelände, zwei Latten von Pfosten zu Pfosten
                    t = K.Group("Spur: Feldzaun", _root);
                    float len = 0f;
                    for (int i = 0; i + 3 < sp.linie.Length; i += 2) len += Vector2.Distance(new Vector2(sp.linie[i], sp.linie[i + 1]), new Vector2(sp.linie[i + 2], sp.linie[i + 3]));
                    int cnt = Mathf.Max(1, Mathf.RoundToInt(len / 2.6f));
                    Vector3 prev = Vector3.zero;
                    for (int k = 0; k <= cnt; k++)
                    {
                        Vector2 c = OnLine(sp.linie, k / (float)cnt);
                        Vector3 pp = P(c.x, c.y);
                        K.Block("Zaunpfosten", t, pp - Vector3.up * 0.3f, new Vector3(0.14f, 1.45f, 0.14f), _woodOld, R(rng, 0f, 90f));
                        if (k > 0)
                            for (int rr = 0; rr < 2; rr++)
                            {
                                Vector3 A = prev + Vector3.up * (0.5f + rr * 0.42f), B2 = pp + Vector3.up * (0.5f + rr * 0.42f);
                                GameObject latte = K.Box("Latte", t, (A + B2) * 0.5f, new Vector3(0.06f, 0.1f, Vector3.Distance(A, B2) + 0.1f), _wood, default, false);
                                latte.transform.rotation = Quaternion.LookRotation(B2 - A);
                            }
                        prev = pp;
                    }
                    _pieces += cnt * 3;
                    break;
                }
                case "heuschober":
                {
                    // Heuschober: Stange, darum das Heu gestapelt, unten breit, oben gerundet; etwas Heu liegt am Fuß
                    t = K.Group("Spur: Heuschober", _root, P(sp.x, sp.z, -0.15f), new Vector3(0f, sp.yaw, 0f));
                    float r = sp.r > 0.5f ? sp.r : 1.8f, hh = sp.h > 0.5f ? sp.h : 3f;
                    const int lagen = 6;
                    for (int i = 0; i < lagen; i++)
                    {
                        float u = i / (float)(lagen - 1);
                        float rad = r * Mathf.Sqrt(1f - 0.85f * u * u);
                        K.Prim(PrimitiveType.Cylinder, "Heu", t, new Vector3(R(rng, -0.05f, 0.05f), hh * (0.08f + 0.8f * u), R(rng, -0.05f, 0.05f)),
                            new Vector3(rad * 2f, hh / lagen * 0.62f, rad * 2f * R(rng, 0.92f, 1.04f)), new Vector3(R(rng, -3f, 3f), R(rng, 0f, 90f), R(rng, -3f, 3f)), _straw, i < 2);
                    }
                    K.Prim(PrimitiveType.Sphere, "Heukappe", t, new Vector3(0f, hh * 0.92f, 0f), new Vector3(r * 0.7f, r * 0.45f, r * 0.7f), Vector3.zero, _straw, false);
                    K.Block("Schoberstange", t, new Vector3(0f, hh * 0.9f, 0f), new Vector3(0.08f, 0.7f, 0.08f), _woodOld);
                    for (int i = 0; i < 5; i++)
                        K.Box("Heu am Boden", t, new Vector3(R(rng, -r, r) * 1.2f, 0.17f, R(rng, -r, r) * 1.2f), new Vector3(R(rng, 0.5f, 1.1f), 0.06f, R(rng, 0.3f, 0.7f)), _straw, new Vector3(0f, R(rng, 0f, 180f), 0f), false);
                    _pieces += lagen + 7;
                    break;
                }
                case "vogelscheuche":
                {
                    // Vogelscheuche im Kornfeld: Pfahl, Querholz, Sackkopf mit Hut, Leinenhemd
                    t = K.Group("Spur: Vogelscheuche", _root, P(sp.x, sp.z, -0.4f), new Vector3(0f, sp.yaw, 0f));
                    K.Block("Pfahl", t, Vector3.zero, new Vector3(0.1f, 2.3f, 0.1f), _woodOld);
                    K.Box("Querholz", t, new Vector3(0f, 1.75f, 0f), new Vector3(1.5f, 0.08f, 0.08f), _woodOld, new Vector3(0f, 0f, R(rng, -6f, 6f)), false);
                    K.Box("Hemd", t, new Vector3(0f, 1.45f, 0f), new Vector3(0.75f, 0.75f, 0.22f), _linen, new Vector3(0f, 0f, R(rng, -4f, 4f)), false);
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        K.Box("Ärmel", t, new Vector3(s2 * 0.55f, 1.7f, 0f), new Vector3(0.45f, 0.18f, 0.18f), _linen, new Vector3(0f, 0f, s2 * -12f), false);
                    K.Prim(PrimitiveType.Sphere, "Sackkopf", t, new Vector3(0f, 2.05f, 0f), new Vector3(0.36f, 0.4f, 0.34f), Vector3.zero, _straw, false);
                    K.Prim(PrimitiveType.Cylinder, "Hutkrempe", t, new Vector3(0f, 2.24f, 0f), new Vector3(0.62f, 0.02f, 0.62f), new Vector3(R(rng, -8f, 8f), 0f, 0f), _woodDark, false);
                    K.Prim(PrimitiveType.Cylinder, "Hut", t, new Vector3(0f, 2.33f, 0f), new Vector3(0.3f, 0.1f, 0.3f), Vector3.zero, _woodDark, false);
                    _pieces += 8;
                    break;
                }
                default:
                    Debug.LogWarning("[Ashveil] Kareth v4: unbekannte Spur " + sp.typ);
                    break;
            }
        }

        private static bool HatSpur(KarethV4Lager l, string typ)
        {
            if (l == null || l.spuren == null) return false;
            foreach (KarethV4Spur s in l.spuren) if (s.typ == typ) return true;
            return false;
        }

        // ================================================================== Felsstirn (behauen) mit Halle und Nischen

        /// <summary>
        /// Behauene Felsstirn hinter dem Oberhof (LORE-081): bis 9,5 m über dem Hof glatt geschlagen, darüber Naturfels
        /// (Felsbaukasten, geologie.py). Die Höhenkarte (2 m) macht aus der senkrechten Wand eine 2 m breite Rampe; die
        /// behauene Wand steht an deren Fuß und deckt sie. Öffnungen: die Halle (ihre Holzfront sitzt 0,4 m tief in der
        /// Öffnung) und die Nischen (fertig mit Kerze und Gaben → angerissen mit Werkzeug davor).
        /// linie = Tripel (x, z der Vorderkante, Tiefe in den Fels), West → Ost; der Hof liegt links (Norden).
        /// </summary>
        private static void FelsStirn(KarethV4Spur sp, KarethV4Lager l)
        {
            if (sp.linie == null || sp.linie.Length < 6) return;
            int n = sp.linie.Length / 3;
            var pts = new Vector2[n]; var tiefe = new float[n];
            for (int i = 0; i < n; i++) { pts[i] = new Vector2(sp.linie[3 * i], sp.linie[3 * i + 1]); tiefe[i] = sp.linie[3 * i + 2]; }
            float hoch = sp.h > 1f ? sp.h : 9.5f;
            Transform t = K.Group("Spur: Felsstirn (behauen, mit Halle und Nischen)", _root);
            // Öffnungen in Welt-x: (x0, x1, y0, y1)
            var oeff = new List<Vector4>();
            if (l != null)
                foreach (KarethV4Gebaeude g in l.gebaeude)
                    if (g.typ == "felshalle")
                    {
                        float y0 = g.boden - g.sockel;
                        oeff.Add(new Vector4(g.x - (g.w - 1f) * 0.5f, g.x + (g.w - 1f) * 0.5f, y0 - 3f, y0 + g.sockel + g.wand * 0.9f + 0.2f));
                    }
            var nischen = new List<Vector3>();     // x, z, Bauzustand (1 = fertig, 0 = angerissen)
            KarethV4Spur ni = null;
            if (l != null && l.spuren != null) foreach (KarethV4Spur s in l.spuren) if (s.typ == "nischen") ni = s;
            if (ni != null && ni.linie != null && ni.linie.Length >= 4)
            {
                float dir = Mathf.Sign(ni.linie[2] - ni.linie[0]);
                for (int i = 0; i < ni.anzahl; i++)
                {
                    float x = ni.linie[0] + dir * i * 3.4f;
                    nischen.Add(new Vector3(x, FrontAt(pts, x), ni.anzahl > 1 ? 1f - i / (float)(ni.anzahl - 1) : 1f));
                    float yy = HofAt(pts, x);
                    oeff.Add(new Vector4(x - 0.55f, x + 0.55f, yy + 0.6f, yy + 2.2f));
                }
            }
            // Wand: je Abschnitt senkrechte Streifen zwischen den Öffnungskanten, je Streifen die Höhen ohne Öffnungen
            var bau = new FelsKit.Behauen();
            for (int i = 0; i + 1 < n; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1], d = b - a;
                float L = d.magnitude;
                if (L < 0.05f || Mathf.Abs(d.x) < 0.01f) continue;
                Vector2 dn = d / L, nrm = new Vector2(-dn.y, dn.x);
                if (nrm.y < 0f) nrm = -nrm;
                float ya = HofAt(pts, a.x), yb = HofAt(pts, b.x);
                float unten = Mathf.Min(ya, yb) - 0.6f, oben = (ya + yb) * 0.5f + hoch;
                float tf = Mathf.Max(tiefe[i], tiefe[i + 1]);
                Quaternion rot = Quaternion.LookRotation(new Vector3(nrm.x, 0f, nrm.y));
                var cuts = new List<float> { 0f, L };
                foreach (Vector4 o in oeff)
                {
                    float u0 = (o.x - a.x) / dn.x, u1 = (o.y - a.x) / dn.x;
                    if (u0 > u1) { float tmp = u0; u0 = u1; u1 = tmp; }
                    if (u1 <= 0f || u0 >= L) continue;
                    cuts.Add(Mathf.Clamp(u0, 0f, L)); cuts.Add(Mathf.Clamp(u1, 0f, L));
                }
                cuts.Sort();
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    float u0 = cuts[k], u1 = cuts[k + 1];
                    if (u1 - u0 < 0.02f) continue;
                    float um = (u0 + u1) * 0.5f, xm = a.x + dn.x * um;
                    var ys = new List<Vector2> { new Vector2(unten, oben) };
                    foreach (Vector4 o in oeff)
                    {
                        if (xm <= Mathf.Min(o.x, o.y) || xm >= Mathf.Max(o.x, o.y)) continue;
                        var rest = new List<Vector2>();
                        foreach (Vector2 r in ys)
                        {
                            if (o.w <= r.x || o.z >= r.y) { rest.Add(r); continue; }
                            if (o.z > r.x) rest.Add(new Vector2(r.x, o.z));
                            if (o.w < r.y) rest.Add(new Vector2(o.w, r.y));
                        }
                        ys = rest;
                    }
                    Vector2 c2 = a + dn * um - nrm * (tf * 0.5f);
                    foreach (Vector2 r in ys)
                        if (r.y - r.x > 0.05f)
                            bau.Add(new Vector3(c2.x, (r.x + r.y) * 0.5f, c2.y), new Vector3(u1 - u0, r.y - r.x, tf), rot);
                }
            }
            bau.Bauen("Felsstirn behauen", t, FelsKit.GranitBehauen());
            // Nischen: Rückwand in der Tiefe des Bauzustands, fertige mit Sturzbalken, Kerze und Gabe, angerissene mit Werkzeug
            foreach (Vector3 nv in nischen)
            {
                float stage = nv.z, depth = 0.15f + 0.6f * stage, yy = HofAt(pts, nv.x);
                Vector2 nr = NormalAt(pts, nv.x);
                Transform nt = K.Group("Nische", t, new Vector3(nv.x, yy, nv.y), Quaternion.LookRotation(new Vector3(nr.x, 0f, nr.y)).eulerAngles);
                K.Box("Nischenrückwand", nt, new Vector3(0f, 1.4f, -depth - 0.05f), new Vector3(1.1f, 1.6f, 0.1f), _soot, default, false);
                if (stage > 0.6f)
                {
                    K.Box("Sturzbalken", nt, new Vector3(0f, 2.3f, 0.12f), new Vector3(1.5f, 0.2f, 0.2f), _woodDark, default, false);
                    K.Box("Kerze", nt, new Vector3(-0.25f, 0.7f, -depth * 0.5f), new Vector3(0.08f, 0.2f, 0.08f), _linen, default, false);
                    K.PointLight("Kerzenschein", nt, new Vector3(-0.25f, 0.95f, -depth * 0.5f + 0.1f), new Color(1f, 0.6f, 0.28f), 3.5f, 0.8f, false, false);
                    K.Box("Gabe (Stein mit Zeichen)", nt, new Vector3(0.2f, 0.66f, -depth * 0.5f), new Vector3(0.2f, 0.12f, 0.15f), _stone, default, false);
                }
                else if (stage < 0.4f)
                {
                    var rng = new System.Random(Mathf.RoundToInt(nv.x * 100f));
                    K.Box("Meißel", nt, new Vector3(0.6f, 0.05f, 0.8f), new Vector3(0.04f, 0.04f, 0.3f), _iron, new Vector3(0f, 30f, 0f), false);
                    K.Box("Fäustel", nt, new Vector3(0.9f, 0.09f, 0.9f), new Vector3(0.18f, 0.14f, 0.12f), _iron, new Vector3(0f, 10f, 0f), false);
                    for (int k = 0; k < 6; k++)
                        K.Box("Splitter", nt, new Vector3(R(rng, -0.6f, 0.6f), 0.06f, R(rng, 0.3f, 1.2f)), new Vector3(0.18f, 0.1f, 0.14f), _stone, new Vector3(0f, R(rng, 0f, 180f), 0f), false);
                }
                _pieces += 6;
            }
            _pieces += bau.Anzahl;
        }

        /// <summary>z der Vorderkante bei Welt-x (Polylinie West → Ost).</summary>
        private static float FrontAt(Vector2[] pts, float x)
        {
            if (x <= pts[0].x) return pts[0].y;
            for (int i = 0; i + 1 < pts.Length; i++)
                if (x <= pts[i + 1].x) return Mathf.Lerp(pts[i].y, pts[i + 1].y, (x - pts[i].x) / Mathf.Max(1e-4f, pts[i + 1].x - pts[i].x));
            return pts[pts.Length - 1].y;
        }

        private static Vector2 NormalAt(Vector2[] pts, float x)
        {
            int i = 0;
            while (i + 2 < pts.Length && x > pts[i + 1].x) i++;
            Vector2 d = (pts[i + 1] - pts[i]).normalized, nr = new Vector2(-d.y, d.x);
            return nr.y < 0f ? -nr : nr;
        }

        /// <summary>Hofhöhe 1,2 m vor der Wand.</summary>
        private static float HofAt(Vector2[] pts, float x)
        {
            Vector2 nr = NormalAt(pts, x);
            return G(x + nr.x * 1.2f, FrontAt(pts, x) + nr.y * 1.2f);
        }

        // ================================================================== Bereiche

        private static void Area(KarethV4Bereich a, KarethV4Lager l)
        {
            float y = G(a.x, a.z);
            Transform t = K.Group(a.id, _root, new Vector3(a.x, y, a.z), Vector3.zero);
            var rng = new System.Random(a.id.GetHashCode());
            switch (a.typ)
            {
                case "feuer":
                    // Das Modell hat seinen eigenen Steinring (10 Steine) – kein zweiter Kranz. Sitze, Feuerholz und die verwehte
                    // Asche sind eigene Spuren (sitz, feuerholz, asche).
                    if (ArtModels.Place("SM_Campfire", t, Vector3.zero, 0f, 1.25f, ArtModels.ColliderKind.Mesh) == null)
                    {
                        // Ersatz ohne Modell: unregelmäßiger, verrußter Ring
                        for (int i = 0; i < 11; i++)
                        {
                            float ang = i * 360f / 11f + R(rng, -9f, 9f), rr = R(rng, 1.55f, 1.85f);
                            K.Box("Ringstein", t, new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad) * rr, 0.12f, Mathf.Cos(ang * Mathf.Deg2Rad) * rr),
                                new Vector3(R(rng, 0.45f, 0.75f), R(rng, 0.28f, 0.42f), R(rng, 0.35f, 0.55f)), rng.NextDouble() < 0.5 ? _soot : _stoneDark,
                                new Vector3(R(rng, -8f, 8f), ang + R(rng, -15f, 15f), R(rng, -8f, 8f)));
                        }
                    }
                    K.Prim(PrimitiveType.Sphere, "Glut", t, new Vector3(0f, 0.25f, 0f), new Vector3(1.3f, 0.35f, 1.3f), Vector3.zero, _fire, false);
                    K.PointLight("Feuerschein", t, new Vector3(0f, 1.6f, 0f), new Color(1f, 0.55f, 0.22f), 22f, 4.5f, true, true);
                    K.Particles("Flammen", t, new Vector3(0f, 0.3f, 0f), new Color(1f, 0.55f, 0.2f, 0.9f), 0.8f, 30f, 1.4f, 1.0f, 12f, 0.4f);
                    K.Particles("Rauch", t, new Vector3(0f, 1.8f, 0f), new Color(0.35f, 0.35f, 0.37f, 0.45f), 1.8f, 6f, 1.2f, 7f, 8f, 0.35f);
                    break;
                case "baenke":
                    // (alte Daten) Stämme tangential: Achse lokal x → yaw = Peilung
                    for (int i = 0; i < 8; i++)
                    {
                        float ang = 22.5f + i * 45f;
                        Vector3 pos = new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad) * a.r, 0f, Mathf.Cos(ang * Mathf.Deg2Rad) * a.r);
                        if (ArtModels.Place("SM_Log", t, pos, ang, 0.9f, ArtModels.ColliderKind.Box) == null)
                            K.Prim(PrimitiveType.Cylinder, "Sitzstamm", t, pos + Vector3.up * 0.3f, new Vector3(0.6f, 1.2f, 0.6f), new Vector3(0f, ang, 90f), _wood);
                    }
                    break;
                case "markt":
                {
                    // Stände in einer Reihe quer zur Blickrichtung a.yaw: Theken (lokal −z) zum Platz bzw. zur Straße, Ware dahinter.
                    // Jeder Stand steht auf seinem eigenen Boden.
                    Material[] cloths = { _cloth1, _cloth2, _cloth3 };
                    int n = Mathf.Max(1, a.anzahl);
                    Quaternion qy = Quaternion.Euler(0f, a.yaw, 0f);
                    Vector3 quer = qy * Vector3.right, vor = qy * Vector3.forward;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 lp = quer * ((i - (n - 1) * 0.5f) * 5.0f) - vor * ((i % 2) * 0.7f);
                        float ly = G(a.x + lp.x, a.z + lp.z) - y;
                        Transform st = K.Group("Stand", t, new Vector3(lp.x, ly, lp.z), new Vector3(0f, a.yaw + 180f + R(rng, -5f, 5f), 0f));
                        for (int ix = -1; ix <= 1; ix += 2)
                        for (int iz = -1; iz <= 1; iz += 2)
                            K.Block("Pfosten", st, new Vector3(ix * 1.6f, GL(st, ix * 1.6f, iz * 1.1f) - 0.2f, iz * 1.1f), new Vector3(0.16f, iz < 0 ? 2.7f : 3.1f, 0.16f), _woodDark);
                        K.Box("Plane", st, new Vector3(0f, 2.75f, 0f), new Vector3(3.8f, 0.08f, 2.9f), cloths[i % 3], new Vector3(-8f, 0f, 0f), false);
                        K.Block("Theke", st, new Vector3(0f, GL(st, 0f, -1.0f) - 0.1f, -1.0f), new Vector3(3.0f, 1.1f, 0.7f), _wood);
                        ArtModels.Place("SM_Crate", st, new Vector3(1.2f, GL(st, 1.2f, 0.6f), 0.6f), 15f, 0.9f, ArtModels.ColliderKind.Box);
                        ArtModels.Place("SM_Barrel", st, new Vector3(-1.3f, GL(st, -1.3f, 0.7f), 0.7f), 0f, 1f, ArtModels.ColliderKind.Capsule);
                    }
                    break;
                }
                case "zelte":
                    // (alte Daten) – kein eigenes Feuer: gekocht wird am Wachtfeuer
                    for (int i = 0; i < Mathf.Max(1, a.anzahl); i++)
                    {
                        float ang = i * 120f + 20f;
                        Vector3 pos = new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad) * 5f, 0f, Mathf.Cos(ang * Mathf.Deg2Rad) * 5f);
                        if (ArtModels.Place("SM_Tent", t, pos, ang + 180f, 1f, ArtModels.ColliderKind.Mesh) == null)
                            K.Box("Zelt", t, pos + Vector3.up * 1f, new Vector3(3f, 2f, 3.6f), _cloth3, new Vector3(0f, ang, 0f));
                    }
                    break;
                case "training":
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 pos = new Vector3(-a.w * 0.35f + i * a.w * 0.23f, 0f, -a.d * 0.2f);
                        K.Block("Puppe Pfahl", t, pos, new Vector3(0.22f, 1.9f, 0.22f), _woodDark);
                        K.Box("Puppe Leib", t, pos + new Vector3(0f, 1.35f, 0f), new Vector3(0.55f, 0.8f, 0.35f), _straw);
                        K.Box("Puppe Arm", t, pos + new Vector3(0f, 1.6f, 0f), new Vector3(1.2f, 0.12f, 0.12f), _wood, default, false);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 pos = new Vector3(-a.w * 0.25f + i * a.w * 0.5f, 0f, a.d * 0.4f);
                        K.Prim(PrimitiveType.Cylinder, "Zielscheibe", t, pos + new Vector3(0f, 1.3f, 0f), new Vector3(1.2f, 0.06f, 1.2f), new Vector3(90f, 0f, 0f), _straw);
                        K.Block("Bock", t, pos + new Vector3(0f, 0f, 0.3f), new Vector3(0.2f, 1.0f, 0.2f), _woodDark);
                    }
                    K.Block("Waffenständer", t, new Vector3(a.w * 0.45f, 0f, 0f), new Vector3(0.5f, 1.6f, 2.4f), _wood);
                    break;
                case "tische":
                    for (int i = 0; i < Mathf.Max(1, a.anzahl); i++)
                    {
                        Vector3 pos = new Vector3(0f, 0f, i * 3.2f - 1.6f);
                        K.Block("Langtisch", t, pos, new Vector3(6.5f, 0.85f, 1.1f), _wood);
                        K.Block("Bank", t, pos + new Vector3(0f, 0f, 1.0f), new Vector3(6.2f, 0.45f, 0.4f), _woodDark);
                        K.Block("Bank", t, pos + new Vector3(0f, 0f, -1.0f), new Vector3(6.2f, 0.45f, 0.4f), _woodDark);
                    }
                    for (int i = -1; i <= 1; i += 2)
                        K.Block("Laubenpfosten", t, new Vector3(i * 3.6f, 0f, 2.6f), new Vector3(0.22f, 2.8f, 0.22f), _woodDark);
                    K.Box("Laubendach", t, new Vector3(0f, 2.9f, 0f), new Vector3(8.0f, 0.12f, 7.0f), _straw, new Vector3(-6f, 0f, 0f), false);
                    break;
                case "pferch":
                {
                    // Pferch: Gatter (lokal −z) zeigt nach a.yaw (zum Hof bzw. zum Weg); jeder Pfosten auf seinem Boden, Latten
                    // laufen von Pfosten zu Pfosten mit dem Gelände. Das Gattertor steht offen nach außen.
                    Transform pf = K.Group("Pferch", t, Vector3.zero, new Vector3(0f, a.yaw + 180f, 0f));
                    float hw = a.w * 0.5f, hd = a.d * 0.5f;
                    // Zaunläufe: vorn links und rechts vom Gatter (2,8 m), dann rechts, hinten, links
                    var laeufe = new List<(Vector2 von, Vector2 bis)>
                    {
                        (new Vector2(-hw, -hd), new Vector2(-1.4f, -hd)), (new Vector2(1.4f, -hd), new Vector2(hw, -hd)),
                        (new Vector2(hw, -hd), new Vector2(hw, hd)), (new Vector2(hw, hd), new Vector2(-hw, hd)), (new Vector2(-hw, hd), new Vector2(-hw, -hd)),
                    };
                    foreach (var (von, bis) in laeufe)
                    {
                        int cnt = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(von, bis) / 2.5f));
                        Vector3 prev = Vector3.zero;
                        for (int k = 0; k <= cnt; k++)
                        {
                            Vector2 q2 = Vector2.Lerp(von, bis, k / (float)cnt);
                            Vector3 lp = new Vector3(q2.x, GL(pf, q2.x, q2.y), q2.y);
                            K.Block("Zaunpfosten", pf, lp - Vector3.up * 0.3f, new Vector3(0.18f, 1.6f, 0.18f), _woodDark);
                            if (k > 0)
                                for (int r = 0; r < 2; r++)
                                    Seil(pf, prev + Vector3.up * (0.6f + r * 0.45f), lp + Vector3.up * (0.6f + r * 0.45f), 0.1f, _wood, "Zaunlatte", true);
                            prev = lp;
                        }
                    }
                    // Gatter: Torflügel offen nach außen, angeschlagen am Pfosten bei u = −1,4
                    float gy = GL(pf, -1.4f, -a.d * 0.5f);
                    Transform tor = K.Group("Gattertor (offen)", pf, new Vector3(-1.4f, gy, -a.d * 0.5f), new Vector3(0f, 70f, 0f));
                    for (int r = 0; r < 3; r++)
                        K.Box("Torlatte", tor, new Vector3(1.4f, 0.35f + r * 0.4f, 0f), new Vector3(2.7f, 0.1f, 0.06f), _woodOld, default, false);
                    K.Box("Torstrebe", tor, new Vector3(1.4f, 0.75f, 0.02f), new Vector3(2.9f, 0.09f, 0.05f), _woodOld, new Vector3(0f, 0f, 17f), false);
                    float ty = GL(pf, a.w * 0.25f, a.d * 0.25f);
                    K.Block("Tränke", pf, new Vector3(a.w * 0.25f, ty - 0.1f, a.d * 0.25f), new Vector3(2.2f, 0.6f, 0.6f), _wood);
                    break;
                }
                case "brunnen":
                    K.Prim(PrimitiveType.Cylinder, "Brunnenring", t, new Vector3(0f, 0.45f, 0f), new Vector3(2.0f, 0.45f, 2.0f), Vector3.zero, _stone);
                    for (int i = -1; i <= 1; i += 2)
                        K.Block("Brunnenpfosten", t, new Vector3(i * 1.0f, 0f, 0f), new Vector3(0.18f, 2.6f, 0.18f), _woodDark);
                    K.Box("Welle", t, new Vector3(0f, 2.1f, 0f), new Vector3(2.2f, 0.16f, 0.16f), _wood, default, false);
                    Roof(t, new Vector3(0f, 2.6f, 0f), 1.4f, 2.6f, 90f, 0.7f, _roof);
                    break;
                case "aushang":
                    for (int i = -1; i <= 1; i += 2)
                        K.Block("Pfosten", t, new Vector3(i * 0.9f, 0f, 0f), new Vector3(0.16f, 2.2f, 0.16f), _woodDark);
                    K.Box("Brett", t, new Vector3(0f, 1.5f, 0f), new Vector3(2.0f, 1.0f, 0.08f), _wood);
                    K.Box("Dächlein", t, new Vector3(0f, 2.2f, 0f), new Vector3(2.4f, 0.08f, 0.6f), _roof, new Vector3(-15f, 0f, 0f), false);
                    break;
            }
            // 05.10.: kein „Kleinkram-Ring“ aus Kisten und Fässern mehr um Markt, Übungsplatz und Tische – Ware steht an den
            // Ständen, Übungswaffen am Waffenständer (Arbeitsregel „Sinn und Gelände“).
            _pieces += 8;
        }
    }
}
