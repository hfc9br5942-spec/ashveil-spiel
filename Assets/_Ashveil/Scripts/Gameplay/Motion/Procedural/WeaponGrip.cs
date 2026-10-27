using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>Vermessener Griff einer Waffe im Raum ihres Drehpunkts (Meter).</summary>
    public struct GripInfo
    {
        public bool Valid;
        /// <summary>Faustmitte der rechten Hand und Handrahmen (vorwaerts = Knoechelachse, oben = Handflaeche).</summary>
        public Vector3 Right;
        public Quaternion RightFrame;
        /// <summary>Zweite Hand am Griff/Schaft (Zweihaender, Stange) – Faustmitte, Rahmen wie rechts.</summary>
        public bool HasLeft;
        public Vector3 Left;
        /// <summary>Gewehr: Stuetzpunkt der Handflaeche unter dem Vorderschaft.</summary>
        public bool HasSupport;
        public Vector3 Support;
        /// <summary>Griff/Schaft von–bis entlang der Griffachse, Dicke (Radius).</summary>
        public float HandleMin, HandleMax, Radius;
        public Vector3 Axis;
        public string Note;
    }

    /// <summary>
    /// Misst Griff bzw. Schaft jeder Waffe aus ihrem Modell (Teile unter dem Drehpunkt) – statt eines festen Versatzes
    /// fuer alle Waffen. Daraus: wo die Faust sitzt (Zeigefinger am Parier, Hand nicht ueber das Griffende hinaus),
    /// wie die Hand ausgerichtet ist (Pistolengriff geneigt), wo die zweite Hand greift (unter der ersten,
    /// weiter oben am Schaft, unter dem Vorderschaft) und wie dick der Griff ist (Fingerschluss).
    /// </summary>
    public static class WeaponGrip
    {
        private struct Part
        {
            public Vector3 Center, A0, A1, A2;   // Mitte, Halbachsen (Pivot-Raum)
            public Vector3 Min, Max;
            public float Length => 2f * Mathf.Max(A0.magnitude, Mathf.Max(A1.magnitude, A2.magnitude));
            public Vector3 Long
            {
                get
                {
                    Vector3 a = A0;
                    if (A1.sqrMagnitude > a.sqrMagnitude) a = A1;
                    if (A2.sqrMagnitude > a.sqrMagnitude) a = A2;
                    return a;
                }
            }
        }

        private static readonly List<Part> Parts = new List<Part>();

        /// <param name="handWidth">Breite der Faust (Zeige- bis Kleinfinger, m).</param>
        public static GripInfo Measure(Transform pivot, GripStyle style, float handWidth)
        {
            var g = new GripInfo { RightFrame = Quaternion.identity, Axis = Vector3.forward };
            if (pivot == null || style == GripStyle.None || style == GripStyle.Unarmed) return g;
            Collect(pivot);
            if (Parts.Count == 0) return g;
            float half = Mathf.Clamp(handWidth * 0.5f + 0.01f, 0.035f, 0.06f);
            if (style == GripStyle.Pistol) return Pistol(g, half);
            if (style == GripStyle.LongGun) return LongGun(g, half);
            if (style == GripStyle.Bow) return Bow(g);
            return Melee(g, style, half);
        }

        private static void Collect(Transform pivot)
        {
            Parts.Clear();
            bool anyEquip = false;
            foreach (MeshRenderer r in pivot.GetComponentsInChildren<MeshRenderer>())
                if (r.enabled && r.gameObject.activeInHierarchy && r.name.StartsWith("Equip")) { anyEquip = true; break; }
            foreach (MeshFilter mf in pivot.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || !mf.gameObject.activeInHierarchy) continue;
                if (anyEquip && !mf.name.StartsWith("Equip")) continue; // Platzhalter ignorieren, wenn ein Modell da ist
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = pivot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var p = new Part
                {
                    Center = m.MultiplyPoint3x4(b.center),
                    A0 = m.MultiplyVector(new Vector3(b.extents.x, 0f, 0f)),
                    A1 = m.MultiplyVector(new Vector3(0f, b.extents.y, 0f)),
                    A2 = m.MultiplyVector(new Vector3(0f, 0f, b.extents.z)),
                };
                Vector3 e = new Vector3(Mathf.Abs(p.A0.x) + Mathf.Abs(p.A1.x) + Mathf.Abs(p.A2.x),
                    Mathf.Abs(p.A0.y) + Mathf.Abs(p.A1.y) + Mathf.Abs(p.A2.y),
                    Mathf.Abs(p.A0.z) + Mathf.Abs(p.A1.z) + Mathf.Abs(p.A2.z));
                p.Min = p.Center - e; p.Max = p.Center + e;
                Parts.Add(p);
            }
        }

        // ------------------------------------------------------------------ Klingen, Schaefte

        /// <summary>Querschnitt (groesste Ausdehnung quer zur Achse) aller Teile, die die Hoehe z umfassen.</summary>
        private static float Radius(float z)
        {
            float r = 0f;
            foreach (Part p in Parts)
            {
                if (z < p.Min.z || z > p.Max.z) continue;
                r = Mathf.Max(r, Mathf.Max(Mathf.Max(Mathf.Abs(p.Min.x), Mathf.Abs(p.Max.x)), Mathf.Max(Mathf.Abs(p.Min.y), Mathf.Abs(p.Max.y))));
            }
            return r;
        }

        private static GripInfo Melee(GripInfo g, GripStyle style, float half)
        {
            const float step = 0.005f, thin = 0.026f;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Part p in Parts) { lo = Mathf.Min(lo, p.Min.z); hi = Mathf.Max(hi, p.Max.z); }
            // Griffbereich um den Drehpunkt suchen (oder den naechsten duennen Abschnitt)
            float start = 0f;
            if (Radius(start) > thin || Radius(start) <= 0f)
            {
                float best = float.MaxValue;
                for (float z = lo; z <= hi; z += step)
                {
                    float r = Radius(z);
                    if (r > 0f && r <= thin && Mathf.Abs(z) < best) { best = Mathf.Abs(z); start = z; }
                }
                if (best == float.MaxValue) return g;
            }
            float a = start, b = start;
            while (a - step >= lo && Radius(a - step) <= thin && Radius(a - step) > 0f) a -= step;
            while (b + step <= hi && Radius(b + step) <= thin && Radius(b + step) > 0f) b += step;
            float radius = 0f;
            for (float z = a; z <= b; z += step) radius = Mathf.Max(radius, Radius(z));
            g.HandleMin = a; g.HandleMax = b; g.Radius = radius;
            float len = b - a;
            bool shaft = len > 0.3f;          // Stiel/Schaft (Keule, Axt, Stab) statt kurzem Heft
            float r0;
            switch (style)
            {
                case GripStyle.TwoHandBlade:
                    r0 = b - half - 0.004f;                                  // Zeigefinger am Parier
                    g.HasLeft = true;
                    g.Left = new Vector3(0f, 0f, Mathf.Max(a + half * 0.8f, r0 - 2f * half - 0.006f));
                    g.Note = "Zweihandgriff: rechts am Parier, links direkt darunter";
                    break;
                case GripStyle.TwoHandHeavy:
                    r0 = Mathf.Min(a + half + 0.03f, b - half);              // unten am Stiel, nicht ueber das Ende
                    g.HasLeft = true;
                    g.Left = new Vector3(0f, 0f, Mathf.Min(r0 + 0.3f, b - half - 0.02f));
                    g.Note = "Stiel: rechts unten, links 30 cm hoeher";
                    break;
                case GripStyle.Polearm:
                    r0 = Mathf.Min(a + 0.12f, b - half);
                    g.HasLeft = true;
                    g.Left = new Vector3(0f, 0f, Mathf.Min(r0 + 0.45f, b - half - 0.05f));
                    g.Note = "Schaft: rechts hinten, links vorn";
                    break;
                case GripStyle.Staff:
                    r0 = Mathf.Clamp(0.51f, a + half, b - half - 0.08f);
                    g.Note = "Stab: Hand auf Brusthoehe am Schaft";
                    break;
                default:
                    if (shaft) { r0 = a + half + 0.012f; g.Note = "Stiel: Faust am unteren Ende"; }
                    else if (len < 2f * half) { r0 = (a + b) * 0.5f + 0.004f; g.Note = "kurzes Heft: Faust mittig"; }
                    else { r0 = b - half - 0.004f; g.Note = "Heft: Zeigefinger am Parier"; }
                    break;
            }
            g.Radius = Mathf.Max(0.008f, Radius(r0));
            g.Right = new Vector3(0f, 0f, r0);
            g.RightFrame = Quaternion.identity; // Knoechel entlang +Z, Handflaeche +Y (Schneide zeigt in Handrichtung)
            g.Axis = Vector3.forward;
            g.Valid = true;
            return g;
        }

        // ------------------------------------------------------------------ Bogen

        /// <summary>
        /// Bogen: Griff = Mittelstueck am Drehpunkt, Wurfarme entlang +Y, Pfeilrichtung +Z. Die linke Faust umschliesst den
        /// Griff (Knoechel nach oben entlang der Wurfarme, Handflaeche nach rechts), die Pfeilauflage liegt knapp darueber.
        /// </summary>
        private static GripInfo Bow(GripInfo g)
        {
            float r = 0f;
            foreach (Part p in Parts)
                if (p.Min.y < 0.02f && p.Max.y > -0.02f && Mathf.Abs(p.Center.x) < 0.03f && Mathf.Abs(p.Center.z) < 0.05f)
                    r = Mathf.Max(r, Mathf.Max(Mathf.Abs(p.Max.z - p.Min.z), Mathf.Abs(p.Max.x - p.Min.x)) * 0.5f);
            g.Radius = Mathf.Clamp(r, 0.012f, 0.03f);
            g.Right = Vector3.zero;
            g.RightFrame = Quaternion.LookRotation(Vector3.up, Vector3.right);
            g.Axis = Vector3.up;
            g.HandleMin = -0.1f; g.HandleMax = 0.1f;
            g.Note = "Bogengriff: linke Faust am Mittelstueck";
            g.Valid = true;
            return g;
        }

        // ------------------------------------------------------------------ Feuerwaffen

        private static GripInfo Pistol(GripInfo g, float half)
        {
            // Griffstueck: das laengste Teil unterhalb der Laufebene nahe dem Drehpunkt
            int best = -1; float bestLen = 0f;
            for (int i = 0; i < Parts.Count; i++)
            {
                Part p = Parts[i];
                if (p.Center.y > -0.01f || p.Center.z > 0.1f || p.Center.z < -0.25f) continue;
                if (p.Length > bestLen) { bestLen = p.Length; best = i; }
            }
            if (best < 0) return g;
            Part gp = Parts[best];
            Vector3 axis = gp.Long.normalized;
            if (axis.y < 0f) axis = -axis;
            float hl = gp.Long.magnitude;
            Vector3 top = gp.Center + axis * hl;
            // Faust unter dem Rahmen: oberer Rand der Faust (Daumenbeuge) knapp unter dem Griffende
            g.Right = top - axis * (half + 0.006f);
            g.RightFrame = Quaternion.LookRotation(axis, Vector3.ProjectOnPlane(Vector3.left, axis).normalized);
            g.Axis = axis;
            g.HandleMin = -hl; g.HandleMax = hl;
            g.Radius = Mathf.Min(gp.A0.magnitude, Mathf.Min(gp.A1.magnitude, gp.A2.magnitude));
            g.Note = string.Format(System.Globalization.CultureInfo.InvariantCulture, "Pistolengriff {0:0}° geneigt", Vector3.Angle(axis, Vector3.up));
            g.Valid = true;
            return g;
        }

        private static GripInfo LongGun(GripInfo g, float half)
        {
            // Schaft: laengstes Teil hinter dem Abzug
            int stock = -1; float bestLen = 0f;
            for (int i = 0; i < Parts.Count; i++)
            {
                Part p = Parts[i];
                if (p.Center.z > -0.06f) continue;
                if (p.Length > bestLen) { bestLen = p.Length; stock = i; }
            }
            if (stock < 0) return Pistol(g, half);
            Part s = Parts[stock];
            Vector3 sa = s.Long.normalized;
            if (sa.z < 0f) sa = -sa;
            float hl = s.Long.magnitude;
            Vector3 front = s.Center + sa * hl;
            // Faust am Kolbenhals: vorderes Ende des Schafts, Zeigefinger reicht zum Abzug
            g.Right = front - sa * (half * 0.9f);
            Vector3 k = Vector3.ProjectOnPlane(Vector3.up, sa).normalized;
            g.RightFrame = Quaternion.LookRotation(k, Vector3.ProjectOnPlane(Vector3.left, k).normalized);
            g.Axis = sa;
            g.Radius = Mathf.Min(s.A0.magnitude, Mathf.Min(s.A1.magnitude, s.A2.magnitude));

            // Vorderschaft: das am tiefsten reichende Teil vor der Faust – Handflaeche liegt darunter an
            int fore = -1; float lowest = float.MaxValue;
            for (int i = 0; i < Parts.Count; i++)
            {
                Part p = Parts[i];
                if (i == stock || p.Max.z < g.Right.z + 0.15f || p.Center.z < g.Right.z + 0.05f) continue;
                if (p.Min.y < lowest) { lowest = p.Min.y; fore = i; }
            }
            if (fore >= 0)
            {
                Part f = Parts[fore];
                float z = Mathf.Clamp(g.Right.z + 0.27f, f.Min.z + 0.05f, f.Max.z - 0.06f);
                g.Support = new Vector3(0f, f.Min.y - 0.022f, z);
                g.HasSupport = true;
            }
            g.Note = g.HasSupport ? "Kolbenhals + Vorderschaft" : "Kolbenhals";
            g.Valid = true;
            return g;
        }
    }
}
