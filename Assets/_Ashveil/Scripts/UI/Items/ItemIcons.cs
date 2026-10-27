using System.Collections.Generic;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>
    /// Erzeugt Item-Symbole aus einfachen Formen (Kapseln, Kreise, Polygone) mit Kontur und Lichtverlauf –
    /// bis echte Symbole gezeichnet sind. Hat ein Item ein eigenes Sprite, wird dieses benutzt.
    /// </summary>
    public static class ItemIcons
    {
        private const int Size = 64;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        private static readonly Color Wood = new Color(0.42f, 0.28f, 0.16f);
        private static readonly Color Metal = new Color(0.72f, 0.72f, 0.74f);
        private static readonly Color Dark = new Color(0.16f, 0.14f, 0.13f);
        private static readonly Color Gold = new Color(0.86f, 0.68f, 0.3f);

        public static Sprite Get(ItemDefinition def)
        {
            if (def == null) return null;
            if (def.icon != null) return def.icon;
            string key = def.iconShape + "#" + ColorUtility.ToHtmlStringRGB(def.iconColor);
            if (Cache.TryGetValue(key, out Sprite s) && s != null) return s;
            s = Build(def.iconShape, def.iconColor);
            Cache[key] = s;
            return s;
        }

        /// <summary>Symbol nach Form und Farbe (Faehigkeiten ohne eigenes Bild).</summary>
        public static Sprite ForShape(IconShape shape, Color color)
        {
            string key = shape + "#" + ColorUtility.ToHtmlStringRGB(color);
            if (Cache.TryGetValue(key, out Sprite s) && s != null) return s;
            s = Build(shape, color);
            Cache[key] = s;
            return s;
        }

        private static Sprite _white;

        /// <summary>Weisses Quadrat – fuer gefuellte Bilder (Abklingrad), die ein Sprite brauchen.</summary>
        public static Sprite White
        {
            get
            {
                if (_white != null) return _white;
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color[16];
                for (int i = 0; i < px.Length; i++) px[i] = Color.white;
                t.SetPixels(px);
                t.Apply();
                _white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                return _white;
            }
        }

        // ================================================================== Zeichnen

        private sealed class Canvas
        {
            public readonly Color[] Px = new Color[Size * Size];

            // Signierte Distanzfunktionen in Pixeln; Farbe mit Kontur und Glanz
            public void Shape(System.Func<float, float, float> sdf, Color c, bool shade = true)
            {
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = sdf(x + 0.5f, y + 0.5f);
                    if (d > 1.5f) continue;
                    float fill = Mathf.Clamp01(0.5f - d);
                    float outline = Mathf.Clamp01(1.5f - Mathf.Abs(d + 1f));
                    Color col = c;
                    if (shade)
                    {
                        float light = 0.72f + 0.45f * (y / (float)Size) - 0.12f * (x / (float)Size);
                        col = new Color(c.r * light, c.g * light, c.b * light, 1f);
                        // Glanzkante oben links
                        if (d > -3f && d < -1.2f) col = Color.Lerp(col, Color.white, 0.18f * Mathf.Clamp01((y - x) / (float)Size + 0.5f));
                    }
                    col = Color.Lerp(col, new Color(0.03f, 0.03f, 0.03f, 1f), outline * 0.85f);
                    float a = Mathf.Max(fill, outline * Mathf.Clamp01(1.5f - d));
                    int i = y * Size + x;
                    Px[i] = Color.Lerp(Px[i], new Color(col.r, col.g, col.b, 1f), a);
                    Px[i].a = Mathf.Max(Px[i].a, a);
                }
            }

            public void Capsule(float ax, float ay, float bx, float by, float r, Color c) =>
                Shape((x, y) => SegDist(x, y, ax * Size, ay * Size, bx * Size, by * Size) - r * Size, c);

            public void Circle(float cx, float cy, float r, Color c) =>
                Shape((x, y) => Mathf.Sqrt(Sq(x - cx * Size) + Sq(y - cy * Size)) - r * Size, c);

            public void Ellipse(float cx, float cy, float rx, float ry, Color c) =>
                Shape((x, y) => (Mathf.Sqrt(Sq((x - cx * Size) / (rx * Size)) + Sq((y - cy * Size) / (ry * Size))) - 1f) * Mathf.Min(rx, ry) * Size, c);

            public void RingShape(float cx, float cy, float r, float thick, Color c) =>
                Shape((x, y) => Mathf.Abs(Mathf.Sqrt(Sq(x - cx * Size) + Sq(y - cy * Size)) - r * Size) - thick * Size, c);

            public void Box(float x0, float y0, float x1, float y1, Color c) =>
                Poly(c, x0, y0, x1, y0, x1, y1, x0, y1);

            public void Poly(Color c, params float[] p)
            {
                int n = p.Length / 2;
                var pts = new Vector2[n];
                for (int i = 0; i < n; i++) pts[i] = new Vector2(p[i * 2] * Size, p[i * 2 + 1] * Size);
                Shape((x, y) =>
                {
                    float d = float.MaxValue;
                    bool inside = false;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        d = Mathf.Min(d, SegDist(x, y, pts[j].x, pts[j].y, pts[i].x, pts[i].y));
                        if ((pts[i].y > y) != (pts[j].y > y) &&
                            x < (pts[j].x - pts[i].x) * (y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x) inside = !inside;
                    }
                    return inside ? -d : d;
                }, c);
            }
        }

        private static float Sq(float v) => v * v;

        private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / Mathf.Max(0.0001f, vx * vx + vy * vy));
            return Mathf.Sqrt(Sq(px - (ax + vx * t)) + Sq(py - (ay + vy * t)));
        }

        private static Sprite Build(IconShape shape, Color c)
        {
            var cv = new Canvas();
            Color dim = Color.Lerp(c, Dark, 0.45f);
            switch (shape)
            {
                case IconShape.Sword:
                    cv.Capsule(0.3f, 0.3f, 0.84f, 0.84f, 0.068f, c);
                    cv.Capsule(0.17f, 0.45f, 0.45f, 0.17f, 0.05f, Gold);
                    cv.Capsule(0.16f, 0.16f, 0.3f, 0.3f, 0.045f, Wood);
                    cv.Circle(0.13f, 0.13f, 0.06f, Gold);
                    break;
                case IconShape.Axe:
                    cv.Capsule(0.22f, 0.1f, 0.64f, 0.86f, 0.05f, Wood);
                    cv.Poly(c, 0.5f, 0.6f, 0.72f, 0.9f, 0.92f, 0.66f, 0.66f, 0.46f);
                    break;
                case IconShape.Dagger:
                    cv.Capsule(0.38f, 0.38f, 0.76f, 0.76f, 0.065f, c);
                    cv.Capsule(0.26f, 0.48f, 0.48f, 0.26f, 0.045f, Gold);
                    cv.Capsule(0.22f, 0.22f, 0.36f, 0.36f, 0.045f, Wood);
                    break;
                case IconShape.Mace:
                    cv.Capsule(0.16f, 0.16f, 0.6f, 0.6f, 0.05f, Wood);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        cv.Circle(0.68f + Mathf.Cos(a) * 0.14f, 0.68f + Mathf.Sin(a) * 0.14f, 0.04f, dim);
                    }
                    cv.Circle(0.68f, 0.68f, 0.14f, c);
                    break;
                case IconShape.Staff:
                    cv.Capsule(0.18f, 0.08f, 0.68f, 0.76f, 0.045f, Wood);
                    cv.RingShape(0.72f, 0.8f, 0.13f, 0.03f, Gold);
                    cv.Circle(0.72f, 0.8f, 0.09f, c);
                    break;
                case IconShape.Pistol:
                    cv.Poly(Wood, 0.32f, 0.6f, 0.48f, 0.6f, 0.34f, 0.2f, 0.16f, 0.26f);
                    cv.Box(0.26f, 0.54f, 0.9f, 0.7f, c);
                    cv.Circle(0.42f, 0.54f, 0.05f, Gold);
                    break;
                case IconShape.Rifle:
                    cv.Poly(Wood, 0.06f, 0.36f, 0.34f, 0.5f, 0.34f, 0.6f, 0.06f, 0.52f);
                    cv.Box(0.3f, 0.5f, 0.94f, 0.58f, c);
                    cv.Box(0.3f, 0.44f, 0.62f, 0.52f, Wood);
                    break;
                case IconShape.Bow:
                {
                    // Wurfarme als Kreisbogen (Mitte rechts unten), Sehne gerade, Griff in der Mitte
                    float px = 0f, py = 0f;
                    for (int i = 0; i <= 12; i++)
                    {
                        float a = Mathf.Lerp(98f, 172f, i / 12f) * Mathf.Deg2Rad;
                        float x = 0.8f + Mathf.Cos(a) * 0.62f, y = 0.2f + Mathf.Sin(a) * 0.62f;
                        float mid = Mathf.Abs(i - 6) / 6f;
                        if (i > 0) cv.Capsule(px, py, x, y, Mathf.Lerp(0.05f, 0.028f, mid), c);
                        px = x; py = y;
                    }
                    cv.Capsule(0.8f + Mathf.Cos(98f * Mathf.Deg2Rad) * 0.62f, 0.2f + Mathf.Sin(98f * Mathf.Deg2Rad) * 0.62f,
                               0.8f + Mathf.Cos(172f * Mathf.Deg2Rad) * 0.62f, 0.2f + Mathf.Sin(172f * Mathf.Deg2Rad) * 0.62f, 0.012f, new Color(0.92f, 0.9f, 0.82f));
                    float g = 135f * Mathf.Deg2Rad;
                    cv.Circle(0.8f + Mathf.Cos(g) * 0.62f, 0.2f + Mathf.Sin(g) * 0.62f, 0.06f, Wood);
                    break;
                }
                case IconShape.Arrow:
                    cv.Capsule(0.18f, 0.18f, 0.74f, 0.74f, 0.025f, Wood);
                    cv.Poly(c, 0.86f, 0.86f, 0.66f, 0.8f, 0.8f, 0.66f);
                    cv.Poly(new Color(0.9f, 0.86f, 0.76f), 0.12f, 0.12f, 0.3f, 0.2f, 0.26f, 0.26f, 0.2f, 0.3f);
                    cv.Capsule(0.3f, 0.12f, 0.12f, 0.3f, 0.02f, new Color(0.9f, 0.86f, 0.76f));
                    break;
                case IconShape.Quiver:
                    cv.Capsule(0.62f, 0.62f, 0.88f, 0.88f, 0.018f, Wood);
                    cv.Capsule(0.55f, 0.68f, 0.78f, 0.92f, 0.018f, Wood);
                    cv.Poly(new Color(0.9f, 0.86f, 0.76f), 0.84f, 0.92f, 0.94f, 0.86f, 0.9f, 0.82f);
                    cv.Poly(c, 0.2f, 0.38f, 0.38f, 0.2f, 0.72f, 0.54f, 0.54f, 0.72f);
                    cv.Poly(dim, 0.2f, 0.38f, 0.38f, 0.2f, 0.28f, 0.14f, 0.14f, 0.28f);
                    cv.Capsule(0.3f, 0.46f, 0.46f, 0.3f, 0.02f, Gold);
                    break;
                case IconShape.Bag:
                    cv.Circle(0.5f, 0.42f, 0.3f, c);
                    cv.Poly(c, 0.34f, 0.62f, 0.66f, 0.62f, 0.58f, 0.74f, 0.42f, 0.74f);
                    cv.Capsule(0.36f, 0.66f, 0.64f, 0.66f, 0.03f, Gold);
                    cv.Poly(dim, 0.4f, 0.74f, 0.6f, 0.74f, 0.66f, 0.84f, 0.34f, 0.84f);
                    cv.Circle(0.5f, 0.36f, 0.08f, dim);
                    break;
                case IconShape.Focus:
                    cv.RingShape(0.5f, 0.5f, 0.3f, 0.04f, Gold);
                    cv.Poly(c, 0.5f, 0.78f, 0.7f, 0.5f, 0.5f, 0.22f, 0.3f, 0.5f);
                    break;
                case IconShape.Helmet:
                    cv.Ellipse(0.5f, 0.5f, 0.3f, 0.32f, c);
                    cv.Box(0.18f, 0.3f, 0.82f, 0.38f, dim);
                    cv.Box(0.3f, 0.5f, 0.7f, 0.56f, Dark);
                    cv.Box(0.47f, 0.34f, 0.53f, 0.56f, Dark);
                    break;
                case IconShape.Shoulders:
                    cv.Ellipse(0.3f, 0.5f, 0.2f, 0.16f, c);
                    cv.Ellipse(0.7f, 0.5f, 0.2f, 0.16f, c);
                    cv.Box(0.3f, 0.36f, 0.7f, 0.46f, dim);
                    break;
                case IconShape.Chest:
                    cv.Poly(c, 0.2f, 0.8f, 0.38f, 0.86f, 0.5f, 0.76f, 0.62f, 0.86f, 0.8f, 0.8f, 0.72f, 0.14f, 0.28f, 0.14f);
                    cv.Box(0.3f, 0.36f, 0.7f, 0.42f, dim);
                    break;
                case IconShape.Gloves:
                    cv.Box(0.3f, 0.18f, 0.68f, 0.5f, c);
                    for (int i = 0; i < 4; i++) cv.Capsule(0.34f + i * 0.1f, 0.5f, 0.34f + i * 0.1f, 0.78f, 0.045f, c);
                    cv.Capsule(0.3f, 0.36f, 0.16f, 0.56f, 0.045f, c);
                    cv.Box(0.28f, 0.14f, 0.7f, 0.22f, dim);
                    break;
                case IconShape.Legs:
                    cv.Box(0.28f, 0.72f, 0.72f, 0.84f, dim);
                    cv.Poly(c, 0.28f, 0.74f, 0.5f, 0.74f, 0.48f, 0.14f, 0.3f, 0.14f);
                    cv.Poly(c, 0.5f, 0.74f, 0.72f, 0.74f, 0.7f, 0.14f, 0.52f, 0.14f);
                    break;
                case IconShape.Boots:
                    cv.Poly(c, 0.3f, 0.86f, 0.56f, 0.86f, 0.56f, 0.36f, 0.84f, 0.3f, 0.84f, 0.16f, 0.3f, 0.16f);
                    cv.Box(0.28f, 0.12f, 0.86f, 0.18f, Dark);
                    break;
                case IconShape.Belt:
                    cv.Box(0.08f, 0.42f, 0.92f, 0.58f, c);
                    cv.RingShape(0.5f, 0.5f, 0.12f, 0.03f, Gold);
                    break;
                case IconShape.Cloak:
                    cv.Poly(c, 0.36f, 0.78f, 0.64f, 0.78f, 0.86f, 0.12f, 0.14f, 0.12f);
                    cv.Circle(0.5f, 0.8f, 0.12f, dim);
                    break;
                case IconShape.Amulet:
                    cv.RingShape(0.5f, 0.64f, 0.24f, 0.028f, Gold);
                    cv.Poly(c, 0.5f, 0.46f, 0.68f, 0.26f, 0.5f, 0.06f, 0.32f, 0.26f);
                    break;
                case IconShape.Ring:
                    cv.RingShape(0.5f, 0.42f, 0.22f, 0.05f, Gold);
                    cv.Circle(0.5f, 0.66f, 0.1f, c);
                    break;
                case IconShape.Trinket:
                    cv.Poly(Gold, 0.5f, 0.88f, 0.8f, 0.5f, 0.5f, 0.12f, 0.2f, 0.5f);
                    cv.Circle(0.5f, 0.5f, 0.14f, c);
                    break;
                case IconShape.Potion:
                    cv.Circle(0.5f, 0.36f, 0.24f, c);
                    cv.Box(0.42f, 0.54f, 0.58f, 0.76f, new Color(0.7f, 0.75f, 0.78f));
                    cv.Box(0.4f, 0.74f, 0.6f, 0.86f, Wood);
                    break;
                case IconShape.Food:
                    cv.Ellipse(0.5f, 0.45f, 0.36f, 0.22f, c);
                    for (int i = 0; i < 3; i++) cv.Capsule(0.3f + i * 0.18f, 0.5f, 0.38f + i * 0.18f, 0.62f, 0.02f, dim);
                    break;
                case IconShape.Ore:
                    cv.Poly(Color.Lerp(c, new Color(0.35f, 0.33f, 0.32f), 0.6f), 0.14f, 0.3f, 0.32f, 0.78f, 0.72f, 0.84f, 0.88f, 0.44f, 0.6f, 0.14f);
                    cv.Poly(c, 0.4f, 0.46f, 0.5f, 0.62f, 0.6f, 0.46f, 0.5f, 0.36f);
                    cv.Circle(0.68f, 0.62f, 0.05f, c);
                    break;
                case IconShape.Hide:
                    cv.Poly(c, 0.2f, 0.84f, 0.5f, 0.74f, 0.8f, 0.86f, 0.74f, 0.5f, 0.86f, 0.16f, 0.5f, 0.26f, 0.14f, 0.14f, 0.26f, 0.5f);
                    break;
                case IconShape.Herb:
                    cv.Capsule(0.5f, 0.12f, 0.5f, 0.7f, 0.02f, dim);
                    cv.Ellipse(0.34f, 0.5f, 0.14f, 0.07f, c);
                    cv.Ellipse(0.66f, 0.6f, 0.14f, 0.07f, c);
                    cv.Ellipse(0.5f, 0.8f, 0.08f, 0.12f, c);
                    break;
                case IconShape.Cloth:
                    cv.Poly(c, 0.16f, 0.3f, 0.84f, 0.3f, 0.84f, 0.7f, 0.16f, 0.7f);
                    cv.Poly(dim, 0.16f, 0.5f, 0.84f, 0.5f, 0.84f, 0.56f, 0.16f, 0.56f);
                    break;
                case IconShape.Bone:
                    cv.Capsule(0.28f, 0.28f, 0.72f, 0.72f, 0.05f, c);
                    cv.Circle(0.22f, 0.3f, 0.07f, c);
                    cv.Circle(0.3f, 0.22f, 0.07f, c);
                    cv.Circle(0.7f, 0.78f, 0.07f, c);
                    cv.Circle(0.78f, 0.7f, 0.07f, c);
                    break;
                case IconShape.Scroll:
                    cv.Box(0.24f, 0.22f, 0.76f, 0.78f, c);
                    cv.Capsule(0.18f, 0.78f, 0.82f, 0.78f, 0.06f, dim);
                    cv.Capsule(0.18f, 0.22f, 0.82f, 0.22f, 0.06f, dim);
                    for (int i = 0; i < 4; i++) cv.Box(0.32f, 0.34f + i * 0.1f, 0.68f, 0.36f + i * 0.1f, Dark);
                    break;
                case IconShape.Letter:
                    cv.Box(0.14f, 0.28f, 0.86f, 0.72f, c);
                    cv.Poly(dim, 0.14f, 0.72f, 0.5f, 0.46f, 0.86f, 0.72f);
                    cv.Circle(0.5f, 0.46f, 0.08f, new Color(0.7f, 0.12f, 0.1f));
                    break;
                case IconShape.Coin:
                    cv.Circle(0.5f, 0.5f, 0.3f, c);
                    cv.RingShape(0.5f, 0.5f, 0.2f, 0.02f, dim);
                    break;
                case IconShape.Gem:
                    cv.Poly(c, 0.5f, 0.86f, 0.84f, 0.56f, 0.5f, 0.14f, 0.16f, 0.56f);
                    cv.Poly(Color.Lerp(c, Color.white, 0.35f), 0.5f, 0.86f, 0.64f, 0.56f, 0.5f, 0.5f, 0.36f, 0.56f);
                    break;
                case IconShape.Shield:
                    cv.Circle(0.5f, 0.5f, 0.36f, c);
                    cv.RingShape(0.5f, 0.5f, 0.35f, 0.035f, new Color(0.62f, 0.64f, 0.66f));
                    cv.Box(0.46f, 0.16f, 0.54f, 0.84f, new Color(0.55f, 0.16f, 0.12f));
                    cv.Circle(0.5f, 0.5f, 0.09f, new Color(0.62f, 0.64f, 0.66f));
                    break;
                case IconShape.Bullet:
                    for (int i = 0; i < 3; i++)
                    {
                        float x = 0.3f + i * 0.2f;
                        cv.Capsule(x, 0.2f, x, 0.66f, 0.06f, Gold);
                        cv.Circle(x, 0.7f, 0.06f, c);
                    }
                    break;
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Icon " + shape,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels(cv.Px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
