using System.Collections.Generic;
using System.Text;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// Wertet den Koerper-Mesh einmal aus (Bereich je Vertex aus dem staerksten Knochengewicht, Hoehe, geglaettete
    /// Normalen) und erzeugt daraus – geteilt fuer alle Figuren mit demselben Koerper –
    ///   · Kleidungsschalen: versetzte Kopien eines Bereichs mit denselben Knochengewichten (folgen jeder Animation)
    ///   · maskierte Koerper: Haut unter Kleidung entfernt (kein Durchstechen)
    /// Voraussetzung: das Koerpermodell ist lesbar (Import mit Read/Write – macht QuaterniusImport).
    /// </summary>
    public sealed class BodyShells
    {
        private static readonly Dictionary<Mesh, BodyShells> Bodies = new Dictionary<Mesh, BodyShells>();

        public readonly Mesh Source;
        private readonly Vector3[] _vertices;
        private readonly Vector3[] _normals;
        private readonly BodyRegion[] _region;
        private readonly float[] _height01;
        private readonly float _meshToMeters;
        private readonly Dictionary<string, Mesh> _shells = new Dictionary<string, Mesh>();
        private readonly Dictionary<string, Mesh> _masked = new Dictionary<string, Mesh>();
        private readonly int[] _bone;
        private readonly sbyte[] _boneSide;
        private readonly Vector3[] _bonePos, _boneAxis;
        private readonly Vector3 _meshUp, _meshFwd;
        private readonly Matrix4x4 _fromRoot;
        private readonly float _rootScale = 1f;
        private readonly Dictionary<string, int> _boneIndex = new Dictionary<string, int>();

        public bool Valid { get; }

        public static BodyShells For(SkinnedMeshRenderer smr, Transform characterRoot)
        {
            Mesh mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null) return null;
            if (Bodies.TryGetValue(mesh, out BodyShells b)) return b;
            b = new BodyShells(smr, characterRoot);
            Bodies[mesh] = b;
            return b;
        }

        private BodyShells(SkinnedMeshRenderer smr, Transform root)
        {
            Source = smr.sharedMesh;
            if (!Source.isReadable)
            {
                Debug.LogWarning($"[Ashveil] Koerpermodell '{Source.name}' ist nicht lesbar – keine Kleidungsschalen (Import: Read/Write aktivieren).");
                return;
            }
            _vertices = Source.vertices;
            int n = _vertices.Length;
            _region = new BodyRegion[n];
            _height01 = new float[n];

            // Bereich aus dem staerksten Knochen
            Transform[] bones = smr.bones;
            var boneRegion = new BodyRegion[bones.Length];
            for (int i = 0; i < bones.Length; i++) boneRegion[i] = RegionOfBone(bones[i] != null ? SkelettNamen.Kanonisch(bones[i].name) : "");
            BoneWeight[] w = Source.boneWeights;
            _bone = new int[n];
            for (int i = 0; i < n && i < w.Length; i++)
            {
                BoneWeight bw = w[i];
                int best = bw.boneIndex0;
                float bestW = bw.weight0;
                if (bw.weight1 > bestW) { best = bw.boneIndex1; bestW = bw.weight1; }
                if (bw.weight2 > bestW) { best = bw.boneIndex2; bestW = bw.weight2; }
                if (bw.weight3 > bestW) { best = bw.boneIndex3; }
                _region[i] = best >= 0 && best < boneRegion.Length ? boneRegion[best] : BodyRegion.Head;
                _bone[i] = best;
            }
            // Knochenlage und -achse im Netzraum (aus den Bindposen) fuer die Schalen-UV
            Matrix4x4[] bp = Source.bindposes;
            _bonePos = new Vector3[bones.Length];
            _boneAxis = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length && i < bp.Length; i++) _bonePos[i] = bp[i].inverse.MultiplyPoint3x4(Vector3.zero);
            for (int i = 0; i < bones.Length && i < bp.Length; i++)
            {
                int kind = -1, eltern = -1;
                for (int j = 0; j < bones.Length && j < bp.Length; j++)
                {
                    if (bones[j] == null || bones[i] == null) continue;
                    if (kind < 0 && bones[j].parent == bones[i]) kind = j;
                    if (bones[i].parent == bones[j]) eltern = j;
                }
                Vector3 ax = kind >= 0 ? _bonePos[kind] - _bonePos[i] : eltern >= 0 ? _bonePos[i] - _bonePos[eltern] : Vector3.up;
                _boneAxis[i] = ax.sqrMagnitude > 1e-10f ? ax.normalized : Vector3.up;
            }

            // Hoehe im Figurenraum (Mesh-Raum kann Z-oben sein) und Massstab Mesh -> Meter
            Matrix4x4 toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            float minY = float.MaxValue, maxY = float.MinValue;
            var ys = new float[n];
            for (int i = 0; i < n; i++)
            {
                ys[i] = toRoot.MultiplyPoint3x4(_vertices[i]).y;
                minY = Mathf.Min(minY, ys[i]);
                maxY = Mathf.Max(maxY, ys[i]);
            }
            float h = Mathf.Max(0.01f, maxY - minY);
            for (int i = 0; i < n; i++) _height01[i] = (ys[i] - minY) / h;
            _meshToMeters = toRoot.MultiplyVector(Vector3.up).magnitude * root.lossyScale.y;
            HeightMeters = h * root.lossyScale.y;
            Matrix4x4 fromRoot = toRoot.inverse;
            _meshUp = fromRoot.MultiplyVector(Vector3.up).normalized;
            _meshFwd = fromRoot.MultiplyVector(Vector3.forward).normalized;
            _fromRoot = fromRoot;
            _rootScale = Mathf.Max(1e-5f, root.lossyScale.y);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) _boneIndex[SkelettNamen.Kanonisch(bones[i].name)] = i;
            // 22.10.2026 (Codex Paket 2): Körperseite je Knochen (-1 links, +1 rechts, 0 Mitte) – ein einseitiges Band deckt nur Haut seiner Seite
            _boneSide = new sbyte[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) { string k = SkelettNamen.Kanonisch(bones[i].name); _boneSide[i] = (sbyte)(k.EndsWith("_l") ? -1 : k.EndsWith("_r") ? 1 : 0); }

            // Normalen ueber Naehte gemittelt (sonst reissen versetzte Schalen an UV-Kanten auf)
            Vector3[] nrm = Source.normals;
            _normals = new Vector3[n];
            var sum = new Dictionary<Vector3Int, Vector3>();
            for (int i = 0; i < n; i++)
            {
                Vector3Int k = Key(_vertices[i]);
                sum.TryGetValue(k, out Vector3 acc);
                sum[k] = acc + (i < nrm.Length ? nrm[i] : Vector3.up);
            }
            for (int i = 0; i < n; i++) _normals[i] = sum[Key(_vertices[i])].normalized;
            Valid = true;
        }

        public float HeightMeters { get; }

        private Vector3Int Key(Vector3 v) => new Vector3Int(Mathf.RoundToInt(v.x * 5000f), Mathf.RoundToInt(v.y * 5000f), Mathf.RoundToInt(v.z * 5000f));

        public static BodyRegion RegionOfBone(string b)
        {
            switch (b)
            {
                case "Head": case "neck_01": return BodyRegion.Head;
                case "spine_02": case "spine_03": case "clavicle_l": case "clavicle_r": return BodyRegion.Chest;
                case "spine_01": return BodyRegion.Belly;
                case "pelvis": case "root": return BodyRegion.Pelvis;
                case "upperarm_l": case "upperarm_r": return BodyRegion.UpperArms;
                case "lowerarm_l": case "lowerarm_r": return BodyRegion.Forearms;
                case "thigh_l": case "thigh_r": return BodyRegion.Thighs;
                case "calf_l": case "calf_r": return BodyRegion.Calves;
                case "foot_l": case "foot_r": case "ball_l": case "ball_r": case "ball_leaf_l": case "ball_leaf_r": return BodyRegion.Feet;
            }
            if (b.EndsWith("_l") || b.EndsWith("_r")) return BodyRegion.Hands; // Hand + Finger
            return BodyRegion.Head;
        }

        // 22.10.2026 (Max: Rüstung soll sitzen wie in WoW – dort ist sie als Textur auf den Körper gemalt): die Schalen bekommen
        // eine Zylinder-UV um ihr Körperglied – u läuft um den Arm/das Bein/den Rumpf herum (ganzzahlig oft je Umlauf, damit die
        // Textur nahtlos kachelt), v entlang des Gliedes in Metern / Kachelhöhe. So liegen Schienen, Kettenreihen und Nähte quer zum
        // Glied wie bei echter Rüstung, gleich auf jedem Körper (Tripo-UV des Körpers wird nicht gebraucht).
        public const float KachelHoehe = 0.3f;

        /// <summary>Wie oft die Rüstungstextur einmal um diesen Bereich läuft.</summary>
        public static float Umlauf(BodyRegion r)
        {
            switch (r)
            {
                case BodyRegion.Chest: case BodyRegion.Belly: case BodyRegion.Pelvis: return 4f;
                case BodyRegion.Thighs: case BodyRegion.Calves: case BodyRegion.UpperArms: case BodyRegion.Forearms: case BodyRegion.Head: return 2f;
                default: return 1f;
            }
        }

        private static bool Rumpf(BodyRegion r) => r == BodyRegion.Chest || r == BodyRegion.Belly || r == BodyRegion.Pelvis || r == BodyRegion.Head;

        private Vector2 ShellUv(int i)
        {
            int b = _bone[i];
            BodyRegion r = _region[i];
            if (b < 0 || b >= _bonePos.Length) return Vector2.zero;
            // Rumpf: Achse senkrecht durch den Knochen, v = Körperhöhe; Glieder: Achse entlang des Knochens, v = Abstand entlang
            Vector3 ax = Rumpf(r) ? _meshUp : _boneAxis[b];
            Vector3 d = _vertices[i] - _bonePos[b];
            Vector3 refDir = Vector3.ProjectOnPlane(_meshFwd, ax);
            if (refDir.sqrMagnitude < 1e-6f) refDir = Vector3.ProjectOnPlane(_meshUp, ax);
            Vector3 p = Vector3.ProjectOnPlane(d, ax);
            float ang = p.sqrMagnitude > 1e-12f ? Vector3.SignedAngle(refDir, p, ax) : 0f;
            float u = (ang / 360f + 0.5f) * Umlauf(r);
            float v = Rumpf(r) ? _height01[i] * HeightMeters / KachelHoehe : Vector3.Dot(d, ax) * _meshToMeters / KachelHoehe;
            return new Vector2(u, v);
        }

        private bool Covered(int v, ShellLayer s)
        {
            if ((_region[v] & s.regions) == 0) return false;
            if (!s.band) return _height01[v] >= s.yMin && _height01[v] <= s.yMax;
            Vector3 d = _vertices[v] - s.bandCenter;
            float along = Vector3.Dot(d, s.bandAxis) * _meshToMeters;
            if (Mathf.Abs(along) > s.bandHalf + BandRand) return false;   // etwas breiter – die Maske (Alpha-Schnitt) schneidet genau
            int bs = _bone[v];
            if (s.bandSide != 0 && bs >= 0 && bs < _boneSide.Length && _boneSide[bs] == -s.bandSide) return false;   // Haut der anderen Körperseite (Codex 22.10.: Oberschenkel nah beieinander)
            Vector3 p = Vector3.ProjectOnPlane(d, s.bandAxis);
            if (p.magnitude * _meshToMeters > s.bandRadius) return false;      // anderer Arm/anderes Bein auf derselben Linie
            if (s.angleHalf <= 0f) return true;
            Vector3 refDir = Vector3.ProjectOnPlane(_meshFwd, s.bandAxis);
            if (refDir.sqrMagnitude < 1e-6f) refDir = Vector3.ProjectOnPlane(_meshUp, s.bandAxis);
            float randWinkel = Mathf.Atan2(BandRand, Mathf.Max(0.02f, p.magnitude * _meshToMeters)) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(Vector3.SignedAngle(refDir, p, s.bandAxis), s.angleCenter)) <= s.angleHalf + randWinkel;
        }

        /// <summary>Zusätzliche Breite der Bandschale über das Band hinaus (m); die Maske T_Ruestung_Band schneidet bei UV 0,25/0,75.</summary>
        private const float BandRand = 0.03f;

        /// <summary>Masken-UV eines Bandes: v = Lage entlang der Achse (0,25..0,75 = im Band), u = Winkel um die Achse (Streifen) bzw. 0,5.</summary>
        private Vector2 BandUv(int v, ShellLayer s)
        {
            Vector3 d = _vertices[v] - s.bandCenter;
            float along = Vector3.Dot(d, s.bandAxis) * _meshToMeters;
            float y = 0.5f + 0.25f * along / Mathf.Max(1e-4f, s.bandHalf);
            if (s.angleHalf <= 0f) return new Vector2(0.5f, y);
            Vector3 p = Vector3.ProjectOnPlane(d, s.bandAxis);
            Vector3 refDir = Vector3.ProjectOnPlane(_meshFwd, s.bandAxis);
            if (refDir.sqrMagnitude < 1e-6f) refDir = Vector3.ProjectOnPlane(_meshUp, s.bandAxis);
            float a = Mathf.DeltaAngle(s.angleCenter, Vector3.SignedAngle(refDir, p, s.bandAxis));
            return new Vector2(0.5f + 0.25f * a / s.angleHalf, y);
        }

        /// <summary>
        /// 22.10.2026 (Codex Paket 2): Schlüssel der Hautmaske – nur was <see cref="Covered"/> wirklich auswertet. Normale Schalen: Region und
        /// Höhe (Abstand/Weite ändern die Abdeckung nicht); Bänder zusätzlich Lage, Achse, Breite, Radius, Winkel. Gleiche Abdeckung → eine Maske.
        /// </summary>
        private static string DeckKey(ShellLayer s) =>
            $"{(int)s.regions}:{s.yMin:0.000}:{s.yMax:0.000}" +
            (s.band ? $":B{s.bandCenter.x:0.0000},{s.bandCenter.y:0.0000},{s.bandCenter.z:0.0000}/{s.bandAxis.x:0.000},{s.bandAxis.y:0.000},{s.bandAxis.z:0.000}/{s.bandHalf:0.000}/{s.bandRadius:0.00}/{s.angleCenter:0.0}/{s.angleHalf:0.0}/{s.bandSide}" : "");

        private static string ShellKey(ShellLayer s) =>
            $"{(int)s.regions}:{s.yMin:0.000}:{s.yMax:0.000}:{s.offset:0.0000}:{s.flare:0.000}:{s.flareStart:0.000}" +
            (s.band ? $":B{s.bandCenter.x:0.0000},{s.bandCenter.y:0.0000},{s.bandCenter.z:0.0000}/{s.bandAxis.x:0.000},{s.bandAxis.y:0.000},{s.bandAxis.z:0.000}/{s.bandHalf:0.000}/{s.bandRadius:0.00}/{s.angleCenter:0.0}/{s.angleHalf:0.0}/{s.bandSide}" : "");

        /// <summary>
        /// 22.10.2026 (Max: „Kleidung und Ausrüstung so, wie sie in WoW an den Figuren anliegen“): ein starrer Ring (Saum, Manschette,
        /// Gürtel, Steppnaht) oder flacher Streifen (Knopfleiste, Riemen, Brustplatte) der Grundformen wird zum eng anliegenden Band
        /// am selben Skelett – wie die Geosets in WoW. Die Grundformen waren für den MPFB-Körper bemessen und schwebten bzw.
        /// stachen am Tripo-Körper. Andere Teile (Kuppeln, Kugeln, gedrehte Platten, Anbauteile) bleiben starr.
        /// </summary>
        public bool Band(RigidPart p, bool mirror, float offset, out ShellLayer band)
        {
            band = null;
            if (!Valid || p == null || p.prefab != null || p.bone == "Grip") return false;
            // 22.10. (2): auch Zylinder um ein Glied (Gürtel, Manschette, Kragen, Kettenkragen) sind Ringe – WoW-Geosets wie Gürtel/Stulpen
            bool ring = p.shape == ProcShape.Torus || p.shape == ProcShape.Cylinder, streifen = p.shape == ProcShape.Box;
            // 22.10. (3): Kuppel am Kopf mit offenem Bogen = Kapuze – liegt am Hinterkopf an, das Gesicht bleibt frei (WoW: Kapuze ist ein
            // eng anliegendes Kopf-Geoset, keine Kugel über dem Kopf)
            bool kapuze = p.shape == ProcShape.Dome;
            if (!ring && !streifen && !kapuze) return false;
            string name = mirror ? (p.bone.EndsWith("_l") ? p.bone.Substring(0, p.bone.Length - 2) + "_r" : p.bone.EndsWith("_r") ? p.bone.Substring(0, p.bone.Length - 2) + "_l" : p.bone) : p.bone;
            if (!_boneIndex.TryGetValue(SkelettNamen.Kanonisch(name), out int b) || b >= _bonePos.Length) return false;
            BodyRegion region = RegionOfBone(SkelettNamen.Kanonisch(name));
            if (kapuze && region != BodyRegion.Head) return false;
            Vector3 size = new Vector3(Mathf.Abs(p.size.x), Mathf.Abs(p.size.y), Mathf.Abs(p.size.z));
            Vector3 off = p.offset; if (mirror) off.x = -off.x;
            Vector3 euler = p.euler; if (mirror) euler = new Vector3(euler.x, -euler.y, -euler.z);
            Vector3 axis = Rumpf(region) ? _meshUp : _boneAxis[b];
            Vector3 center = _bonePos[b] + _fromRoot.MultiplyVector(off / _rootScale);
            float half, angleHalf = 0f, angleCenter = 0f;
            if (ring && Mathf.Min(size.x, size.z) < (Rumpf(region) ? 0.15f : 0.05f)) return false;   // kleiner Ring am Kopf (Kapuzenzipfel) war sonst eine Augenbinde
            if (kapuze)
            {
                offset += 0.015f;   // Stoff/Leder steht vom Kopf etwas ab (sonst wirkte die Kapuze wie eine Glatzenkappe)
                if (p.arc < 359f)
                {
                    half = Mathf.Max(0.05f, size.y * 0.6f);
                    center -= axis * (size.y * 0.1f / Mathf.Max(1e-5f, _meshToMeters));
                    angleHalf = Mathf.Clamp(p.arc * 0.5f, 5f, 175f); angleCenter = mirror ? -p.facing : p.facing;
                }
                else
                {
                    // geschlossene Kuppel = Kappe: nur der Oberkopf, das Gesicht bleibt frei
                    half = Mathf.Max(0.03f, size.y * 0.3f);
                    center += axis * (size.y * 0.3f / Mathf.Max(1e-5f, _meshToMeters));
                }
            }
            else if (ring)
            {
                // Ringachse (im Figurenraum: Ring liegt in x/z, Achse y) muss zur Gliedachse passen
                Vector3 ringAxis = _fromRoot.MultiplyVector(Quaternion.Euler(euler) * Vector3.up).normalized;
                if (Mathf.Abs(Vector3.Dot(ringAxis, axis)) < 0.8f) return false;
                half = p.shape == ProcShape.Torus ? Mathf.Max(0.008f, size.y * 0.75f) : Mathf.Max(0.008f, size.y * 0.5f);   // Zylinder: halbe Höhe
                // Teilzylinder (Schienbeinplatte, Kragenstück): Band nur über den Bogen, mittig in Blickrichtung des Teils (facing)
                if (p.shape == ProcShape.Cylinder && p.arc < 359f) { angleHalf = Mathf.Clamp(p.arc * 0.5f, 5f, 175f); angleCenter = mirror ? -p.facing : p.facing; }
            }
            else
            {
                // nur flache, ungedrehte Streifen nahe der Haut
                if (Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(0f, euler.x)), Mathf.Abs(Mathf.DeltaAngle(0f, euler.y)), Mathf.Abs(Mathf.DeltaAngle(0f, euler.z))) > 20f) return false;
                if (Mathf.Min(size.x, size.z) > 0.05f) return false;
                Vector3 radial = Vector3.ProjectOnPlane(center - _bonePos[b], axis);
                float r = radial.magnitude * _meshToMeters;
                if (r < 0.03f || r > 0.3f) return false;
                Vector3 refDir = Vector3.ProjectOnPlane(_meshFwd, axis);
                if (refDir.sqrMagnitude < 1e-6f) refDir = Vector3.ProjectOnPlane(_meshUp, axis);
                angleCenter = Vector3.SignedAngle(refDir, radial, axis);
                float breite = Mathf.Max(size.x, size.z);
                angleHalf = Mathf.Clamp(Mathf.Atan2(breite * 0.5f, r) * Mathf.Rad2Deg, 2f, 80f);
                half = Mathf.Max(0.008f, size.y * 0.5f);
                center = _bonePos[b] + axis * Vector3.Dot(center - _bonePos[b], axis);   // Bandmitte auf der Achse
            }
            band = new ShellLayer
            {
                regions = Rumpf(region) && region != BodyRegion.Head ? BodyRegion.Chest | BodyRegion.Belly | BodyRegion.Pelvis : region, offset = offset, surface = p.surface, channel = p.channel, hideBody = false,
                band = true, bandCenter = center, bandAxis = axis.normalized, bandHalf = half,
                bandRadius = Rumpf(region) ? 0.4f : 0.18f, angleCenter = angleCenter, angleHalf = angleHalf,
                bandSide = b < _boneSide.Length ? _boneSide[b] : (sbyte)0,
            };
            return true;
        }

        /// <summary>Verformbare Kleidungsschale (geteilt).</summary>
        public Mesh Shell(ShellLayer s)
        {
            if (!Valid) return null;
            string key = ShellKey(s);
            if (_shells.TryGetValue(key, out Mesh m) && m != null) return m;

            var tris = new List<int>();
            for (int sub = 0; sub < Source.subMeshCount; sub++)
            {
                int[] t = Source.GetTriangles(sub);
                for (int i = 0; i < t.Length; i += 3)
                    if (Covered(t[i], s) || Covered(t[i + 1], s) || Covered(t[i + 2], s))
                    {
                        tris.Add(t[i]); tris.Add(t[i + 1]); tris.Add(t[i + 2]);
                    }
            }
            var verts = new List<Vector3>(_vertices.Length + 256);
            var nrm = new List<Vector3>(_normals);
            var bw = new List<BoneWeight>(Source.boneWeights);
            var uv = new List<Vector2>(_vertices.Length + 256);
            float inv = 1f / Mathf.Max(1e-5f, _meshToMeters);
            for (int i = 0; i < _vertices.Length; i++)
            {
                float extra = s.flare > 0f ? Mathf.Max(0f, s.flareStart - _height01[i]) * HeightMeters * s.flare : 0f;
                verts.Add(_vertices[i] + _normals[i] * ((s.offset + extra) * inv));
                uv.Add(s.band ? BandUv(i, s) : ShellUv(i));
            }
            // Naht der Zylinder-UV: Dreiecke, die ueber den Umlauf springen, bekommen eigene Ecken mit u + Umlauf (Bänder: Masken-UV, keine Naht)
            for (int i = 0; i < (s.band ? 0 : tris.Count); i += 3)
            {
                float u0 = uv[tris[i]].x, u1 = uv[tris[i + 1]].x, u2 = uv[tris[i + 2]].x;
                float lo = Mathf.Min(u0, Mathf.Min(u1, u2)), hi = Mathf.Max(u0, Mathf.Max(u1, u2));
                float rep = Umlauf(_region[tris[i]]);
                if (hi - lo <= rep * 0.5f) continue;
                for (int c = 0; c < 3; c++)
                {
                    int vi = tris[i + c];
                    if (uv[vi].x >= rep * 0.5f) continue;
                    verts.Add(verts[vi]); nrm.Add(nrm[vi]); bw.Add(bw[vi]);
                    uv.Add(new Vector2(uv[vi].x + rep, uv[vi].y));
                    tris[i + c] = verts.Count - 1;
                }
            }
            m = new Mesh { name = "Shell " + key, hideFlags = HideFlags.DontSave };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(nrm);
            m.SetUVs(0, uv);
            m.boneWeights = bw.ToArray();
            m.bindposes = Source.bindposes;
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            _shells[key] = m;
            return m;
        }

        /// <summary>Koerper ohne die vollstaendig bedeckten Dreiecke (geteilt je Kombination).</summary>
        public Mesh Masked(List<ShellLayer> covering)
        {
            if (!Valid || covering == null || covering.Count == 0) return Source;
            var keys = new List<string>();
            var schon = new HashSet<string>();
            foreach (ShellLayer s in covering) if (s.hideBody && schon.Add(DeckKey(s))) keys.Add(DeckKey(s));   // gleiche Abdeckung nur einmal
            if (keys.Count == 0) return Source;
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string k in keys) sb.Append(k).Append(';');
            string key = sb.ToString();
            if (_masked.TryGetValue(key, out Mesh m) && m != null) return m;

            m = Object.Instantiate(Source);
            m.name = Source.name + " (bedeckt)";
            m.hideFlags = HideFlags.DontSave;
            for (int sub = 0; sub < Source.subMeshCount; sub++)
            {
                int[] t = Source.GetTriangles(sub);
                var keep = new List<int>(t.Length);
                for (int i = 0; i < t.Length; i += 3)
                {
                    bool hidden = false;
                    foreach (ShellLayer s in covering)
                    {
                        if (!s.hideBody) continue;
                        if (Covered(t[i], s) && Covered(t[i + 1], s) && Covered(t[i + 2], s)) { hidden = true; break; }
                    }
                    if (!hidden) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                }
                m.SetTriangles(keep, sub);
            }
            _masked[key] = m;
            return m;
        }
    }
}
