using Ashveil.Data;
using Ashveil.Data.Motion;
using Ashveil.Gameplay.Audio;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// Bogen (wie WoW, selbst choreografiert): Bogen in der linken Faust, Pfeil aus dem Koecher ueber der rechten Schulter,
    /// auflegen, Bogen heben und Sehne bis unters Kinn spannen (Schultern seitlich zum Ziel), loesen – die Zughand schnellt
    /// nach hinten, die Sehne zurueck. Danach sofort den naechsten Pfeil holen. Die Sehne verformt sich mit der Zughand,
    /// der aufgelegte Pfeil liegt zwischen Sehne und Pfeilauflage.
    /// </summary>
    public sealed partial class CombatChoreographer
    {
        private enum BowSeg { Idle, Windup, Active, Recovery }

        private bool _bowActive, _nocked, _bowSpecial, _bowTimerRunning, _needNock;
        private float _raise, _draw, _bowTimer, _bw, _ba, _br, _fetchT = -1f, _releaseT = -1f, _recoverElapsed;
        private BowSeg _lastSeg = BowSeg.Idle;

        // Sichtbares Zubehoer
        private Transform _quiver;
        private LineRenderer _string;
        private Transform _nockArrow;
        private Renderer _staticString;
        private Transform _stringPivot;
        private Vector3 _tipTop = new Vector3(0f, 0.56f, -0.13f), _tipBottom = new Vector3(0f, -0.56f, -0.13f);
        private static Mesh _cylMesh;
        private static Material _leather;

        private void StartBowShot(AttackStep step, bool special)
        {
            _bw = Mathf.Max(0.1f, step.windup);
            _ba = Mathf.Max(0.03f, step.active);
            _br = Mathf.Max(0.2f, step.recovery);
            _bowTimer = 0f;
            _bowTimerRunning = true;
            _bowSpecial = special;
            _needNock = !_nocked;
            _releaseT = -1f;
            CurrentMove = special ? "Bogen: Gezielter Schuss" : "Bogen: Automatischer Schuss";
        }

        private void OnProjectileLaunched(Vector3 origin, Vector3 dir, bool targeted)
        {
            if (_hands == null || _hands.Style != GripStyle.Bow) return;
            // Loslassen: Pfeil ist unterwegs, Sehne schnellt zurueck; Zeitleiste fremder Spieler angleichen
            if (_releaseT >= 0f && !_nocked) return;
            _nocked = false;
            _releaseT = 0f;
            _draw = 0f;
            if (_bowTimerRunning && _bowTimer < _bw) _bowTimer = _bw;
        }

        private void StopBow()
        {
            _bowTimerRunning = false;
            _fetchT = -1f;
            _raise = _draw = 0f;
            _bowActive = false;
            HideBowExtras();
        }

        private BowSeg Segment(out float u, float dt)
        {
            u = 0f;
            if (_bowTimerRunning)
            {
                _bowTimer += dt;
                if (_bowTimer >= _bw + _ba + _br) _bowTimerRunning = false;
            }
            WeaponDefinition w = _weapons != null ? _weapons.ActiveWeapon : null;
            bool own = _weapons != null && _weapons.Phase != WeaponPhase.Idle && w != null && w.weaponClass == WeaponClass.Bow;
            if (own)
            {
                u = _weapons.PhaseProgress;
                switch (_weapons.Phase)
                {
                    case WeaponPhase.Windup: return BowSeg.Windup;
                    case WeaponPhase.Active: return BowSeg.Active;
                    case WeaponPhase.Recovery: return BowSeg.Recovery;
                    default: return BowSeg.Idle;
                }
            }
            if (!_bowTimerRunning) return BowSeg.Idle;
            float t = _bowTimer;
            if (t < _bw) { u = t / _bw; return BowSeg.Windup; }
            t -= _bw;
            if (t < _ba) { u = t / _ba; return BowSeg.Active; }
            u = (t - _ba) / _br;
            return BowSeg.Recovery;
        }

        /// <summary>true = Oberkoerper gehoert gerade dem Bogen.</summary>
        private bool UpdateBow(float dt, bool detailed)
        {
            bool bowStyle = _hands.Style == GripStyle.Bow;
            WeaponDefinition ranged = _weapons != null ? _weapons.RangedWeapon : null;
            EnsureQuiver(ranged != null && ranged.weaponClass == WeaponClass.Bow && detailed);
            Transform pivot = bowStyle ? _hands.Pivot : null;
            bool inHand = bowStyle && pivot != null && _hands.WeaponInHand && pivot.parent == _hands.WeaponArm.Hand;
            if (!inHand)
            {
                _bowActive = false;
                if (!bowStyle) { _nocked = false; _fetchT = -1f; }
                _raise = _draw = 0f;
                HideBowExtras();
                return false;
            }

            BowSeg seg = Segment(out float u, dt);
            if (seg != _lastSeg)
            {
                if (seg == BowSeg.Windup) _needNock = !_nocked;
                if (seg == BowSeg.Recovery) _recoverElapsed = 0f;
                // Loslassen ohne Netzmeldung (Zeitleiste, Lab): beim Uebergang in den Schuss
                if (seg == BowSeg.Active && _nocked && _releaseT < 0f) { _nocked = false; _releaseT = 0f; _draw = 0f; SfxPlayer.Play(SfxId.BowRelease, _hands.OtherArm.Center, 0.5f); }
                _lastSeg = seg;
            }
            if (seg == BowSeg.Recovery) _recoverElapsed += dt;
            if (_releaseT >= 0f) { _releaseT += dt; if (_releaseT > 0.5f) _releaseT = -1f; }
            MotionContext c = _orch != null ? _orch.Context : default;
            bool combat = c.Stance == MotionStance.Combat || seg != BowSeg.Idle;

            // Naechsten Pfeil holen: nach dem Loesen (kurz nachschauen), im Kampf, oder schnell zu Beginn des Zielens
            bool wantFetch = !_nocked && _fetchT < 0f && combat && seg != BowSeg.Active &&
                             (seg != BowSeg.Recovery || _recoverElapsed > 0.25f) && _releaseT < 0f || (!_nocked && _fetchT < 0f && seg == BowSeg.Windup);
            if (wantFetch) _fetchT = 0f;
            if (_fetchT >= 0f)
            {
                float speed = seg == BowSeg.Windup ? 1f / Mathf.Max(0.12f, 0.45f * EffectiveWindup()) : 1f / 0.85f;
                _fetchT += dt * speed;
                if (_fetchT >= 1f) { _fetchT = -1f; _nocked = true; SfxPlayer.Play(SfxId.WeaponDraw, _hands.OtherArm.Center, 0.25f); }
            }

            // Heben / Spannen
            float raiseT = 0f, drawT = 0f;
            bool fetching = _fetchT >= 0f;
            switch (seg)
            {
                case BowSeg.Windup:
                    if (fetching || !_nocked) { raiseT = 0.3f; drawT = 0f; }
                    else
                    {
                        raiseT = 1f;
                        float full = _bowSpecial ? 0.35f : 0.85f;
                        drawT = MotionMath.SmoothStep01(u / full);
                    }
                    break;
                case BowSeg.Active: raiseT = 1f; drawT = _nocked ? 1f : 0f; break;
                case BowSeg.Recovery: raiseT = _recoverElapsed < 0.3f ? 1f : 0f; break;
            }
            if (_releaseT >= 0f) drawT = 0f;
            _raise = MotionMath.Damp(_raise, raiseT, 0.07f, dt);
            _draw = drawT < _draw && _releaseT >= 0f ? 0f : MotionMath.Damp(_draw, drawT, 0.05f, dt);
            if (seg == BowSeg.Windup && drawT > 0.95f && _bowSpecial)
            {
                // Gezielter Schuss: gespannt halten – leichtes Zittern der Zughand
            }

            bool active = combat && (_raise > 0.02f || fetching || _nocked || _releaseT >= 0f || seg != BowSeg.Idle);
            _bowActive = active;
            if (!detailed) { UpdateBowExtras(pivot, false); return active && seg != BowSeg.Idle; }
            if (!active) { UpdateBowExtras(pivot, false); return false; }

            ArmRig bowArm = _hands.WeaponArm, hand = _hands.OtherArm;
            // Zielrichtung: zum Zielpunkt (bzw. geradeaus), Schultern seitlich dazu
            Vector3 chest = (_upperChest != null ? _upperChest : _chest).position;
            Vector3 aim = c.HasTarget && c.TargetPoint != Vector3.zero ? c.TargetPoint - chest : _f + _u * 0.05f;
            if (aim.sqrMagnitude < 0.01f) aim = _f;
            aim.Normalize();
            float pitch = Vector3.SignedAngle(Vector3.ProjectOnPlane(aim, _u), aim, Vector3.Cross(_u, aim));
            if (Mathf.Abs(pitch) > 35f) aim = Quaternion.AngleAxis(Mathf.Sign(pitch) * (Mathf.Abs(pitch) - 35f), Vector3.Cross(aim, _u).normalized) * aim;

            float r = MotionMath.SmoothStep01(_raise);
            float twist = 42f * r;
            Torso(twist, -2f * r, 0f, 1f);
            // Kopf zum Ziel: den restlichen Teil der Schulterdrehung im Hals zuruecknehmen
            if (_neck != null) Rot(_neck, -twist * 0.3f, 0f, 0f);

            // --- Bogenhand: Bereitschaft (tief, schraeg) → ausgestreckt zum Ziel
            WeaponStance ws = WeaponPoses.Guard(GripStyle.Bow, OffhandRole.Free);
            Vector3 gPos = FromBody(ws.right.pos);
            Vector3 gDir = M(ws.right.dir).normalized, gPalm = M(ws.right.palm).normalized;
            Quaternion gFrame = Quaternion.LookRotation(WeaponPoses.KnucklesOf(gDir, gPalm, true), gPalm);
            Vector3 bowUp = Vector3.ProjectOnPlane(Quaternion.AngleAxis(-10f, aim) * _u, aim).normalized;
            Vector3 bowPalm = Vector3.Cross(bowUp, aim).normalized;
            Vector3 aPos = bowArm.Upper.position + aim * bowArm.Reach * 0.93f;
            Quaternion aFrame = Quaternion.LookRotation(bowUp, bowPalm);
            bowArm.Place(Vector3.Lerp(gPos, aPos, r), Quaternion.Slerp(gFrame, aFrame, r), 0f, 0.5f, 1f, 45f);
            bowArm.ClampTwist(95f);
            bowArm.ClampWrist(45f, 1f);
            bowArm.Shape(HandShape.Grip, 1f, dt);

            // --- Zughand
            FindString(pivot);
            Vector3 nockRest = pivot.TransformPoint(new Vector3(0f, 0.06f, _tipTop.z));
            Vector3 fwd = pivot.forward;
            Vector3 head = _head != null ? _head.position : chest + _u * 0.35f;
            Vector3 anchor = head + Vector3.Cross(_u, aim).normalized * 0.06f - _u * 0.1f + aim * 0.03f;
            Vector3 hookPalm = -Vector3.Cross(bowUp, fwd).normalized;
            Quaternion hookFrame = Quaternion.LookRotation(WeaponPoses.KnucklesOf(fwd, hookPalm, false), hookPalm);
            HandShape shape = HandShape.Grip;
            Vector3 hp; Quaternion hr; float hw = 1f;
            if (fetching)
            {
                FetchPose(hand, nockRest, hookFrame, out hp, out hr, out shape);
            }
            else if (_releaseT >= 0f)
            {
                // Loesen: Zughand schnellt am Kinn vorbei nach hinten, Finger oeffnen sich
                float k = Mathf.Clamp01(_releaseT / 0.18f);
                hp = anchor - aim * (0.07f * k) + Vector3.Cross(_u, aim).normalized * (0.03f * k);
                hr = hookFrame;
                shape = HandShape.Open;
                hw = 1f - MotionMath.SmoothStep01((_releaseT - 0.3f) / 0.2f);
            }
            else if (_nocked)
            {
                float d = MotionMath.SmoothStep01(_draw);
                hp = Vector3.Lerp(nockRest, anchor, d) - _u * (0.03f * Mathf.Sin(d * Mathf.PI));
                hr = hookFrame;
            }
            else { hp = hand.Center; hr = hand.Frame; hw = 0f; }
            if (hw > 0.001f)
            {
                hand.Place(hp, hr, 0f, 0.5f, hw, 50f);
                hand.ClampTwist(100f);
                hand.ClampWrist(55f, 1f);
                hand.Shape(shape, 1f, dt);
            }
            UpdateBowExtras(pivot, true);
            return seg != BowSeg.Idle || fetching || _releaseT >= 0f || _raise > 0.05f;
        }

        private float EffectiveWindup() => _weapons != null && _weapons.Phase == WeaponPhase.Windup && _weapons.ActiveWeapon != null
            ? Mathf.Max(0.2f, _weapons.CurrentStep.windup) : _bw;

        /// <summary>Griff zum Koecher ueber der rechten Schulter, Pfeil fassen, zur Sehne fuehren.</summary>
        private void FetchPose(ArmRig hand, Vector3 nockRest, Quaternion hookFrame, out Vector3 pos, out Quaternion frame, out HandShape shape)
        {
            Vector3 q = QuiverTop();
            Vector3 shoulder = hand.Upper.position;
            Vector3 up = (q - shoulder).normalized;
            Vector3 palmQ = Vector3.ProjectOnPlane(_f, up).normalized;
            if (palmQ.sqrMagnitude < 1e-4f) palmQ = _f;
            Quaternion quiverFrame = Quaternion.LookRotation(WeaponPoses.KnucklesOf(up, palmQ, false), palmQ);
            float t = _fetchT;
            if (t < 0.45f)
            {
                float u = MotionMath.SmoothStep01(t / 0.45f);
                Vector3 p0 = hand.Center;
                pos = Vector3.Lerp(p0, q, u) + _u * (0.12f * Mathf.Sin(u * Mathf.PI));
                frame = Quaternion.Slerp(hand.Frame, quiverFrame, u);
                shape = u > 0.8f ? HandShape.Open : HandShape.LooseFist;
            }
            else if (t < 0.55f)
            {
                pos = q + up * (0.08f * (t - 0.45f) / 0.1f);
                frame = quiverFrame;
                shape = HandShape.Grip;
            }
            else
            {
                float u = MotionMath.SmoothStep01((t - 0.55f) / 0.45f);
                Vector3 from = q + up * 0.08f;
                // 21.10. nachts (Ziehfilm Bogen: Pfeil fuhr senkrecht vor/durch das Gesicht): Hand aussen um den Kopf herum nach vorn
                Vector3 ctrl = (from + nockRest) * 0.5f + _u * 0.05f + _f * 0.18f + _rt * 0.2f;
                pos = Bez(from, ctrl, nockRest, u);
                frame = Quaternion.Slerp(quiverFrame, hookFrame, u);
                shape = HandShape.Grip;
            }
        }

        // ================================================================== Zubehoer: Koecher, Sehne, aufgelegter Pfeil

        private Vector3 QuiverTop()
        {
            if (_quiver != null) return _quiver.position + _quiver.up * 0.21f;
            Transform b = _upperChest != null ? _upperChest : _chest;
            return b.position - _f * 0.16f + _rt * 0.16f + _u * 0.22f;
        }

        private void EnsureQuiver(bool want)
        {
            if (!want)
            {
                if (_quiver != null) _quiver.gameObject.SetActive(false);
                return;
            }
            Transform bone = _upperChest != null ? _upperChest : _chest;
            if (_quiver == null)
            {
                var go = new GameObject("Koecher (Bogen)");
                _quiver = go.transform;
                _quiver.SetParent(bone, false);
                Transform body = new GameObject("Koecher").transform;
                body.SetParent(_quiver, false);
                body.localScale = new Vector3(0.11f, 0.20f, 0.11f);   // 22.10.: 50 → 40 cm, unteres Ende stiess an Becken und Unterarm
                var mf = body.gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = CylinderMesh();
                var mr = body.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = LeatherMaterial();
                // Federn schauen oben heraus
                for (int i = 0; i < 5; i++)
                {
                    var a = new GameObject("Pfeil " + i).transform;
                    a.SetParent(_quiver, false);
                    float ang = i * 72f * Mathf.Deg2Rad;
                    // Spitze nach unten, Federn schauen oben heraus
                    a.localPosition = new Vector3(Mathf.Cos(ang) * 0.025f, -0.02f + (i % 2) * 0.03f, Mathf.Sin(ang) * 0.025f);
                    a.localRotation = Quaternion.LookRotation(-Vector3.up + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.06f, Vector3.forward);
                    a.gameObject.AddComponent<MeshFilter>().sharedMesh = ProjectileSystem.ArrowMesh(0.78f);
                    a.gameObject.AddComponent<MeshRenderer>().sharedMaterials = ProjectileSystem.ArrowMaterials();
                }
            }
            if (!_quiver.gameObject.activeSelf) _quiver.gameObject.SetActive(true);
            // Lage am Ruecken rechts, Oeffnung hinter der rechten Schulter (jedes Bild, damit Koerperwechsel passen)
            if (_quiver.parent != bone) _quiver.SetParent(bone, false);
            // 21.10. nachts (Haut-Pruefung: Koecher 5 cm im Ruecken und im rechten Oberarm des Tripo-Mannes): 6 cm weiter nach
            // hinten, weniger zur Schulter geneigt – die Oeffnung bleibt hinter der rechten Schulter erreichbar.
            Vector3 axis = (0.24f * _rt + 0.92f * _u - 0.26f * _f).normalized;
            _quiver.position = bone.position - _f * 0.26f + _rt * 0.04f + _u * 0.05f;   // 22.10.: 9 cm höher, 3 cm zur Mitte (Unterarm schwang hinein)
            _quiver.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(-_f, axis).normalized, axis);
            _quiver.localScale = Vector3.one / Mathf.Max(1e-4f, bone.lossyScale.x);
        }

        private static Mesh CylinderMesh()
        {
            if (_cylMesh != null) return _cylMesh;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _cylMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return _cylMesh;
        }

        private static Material LeatherMaterial()
        {
            if (_leather != null) return _leather;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            _leather = new Material(sh) { name = "Koecherleder", color = new Color(0.36f, 0.24f, 0.15f) };
            if (_leather.HasProperty("_Smoothness")) _leather.SetFloat("_Smoothness", 0.2f);
            return _leather;
        }

        /// <summary>Sehne im Modell finden (duennes, langes Teil entlang der Wurfarme) – Enden = Nocken.</summary>
        private void FindString(Transform pivot)
        {
            if (pivot == _stringPivot) return;
            _stringPivot = pivot;
            _staticString = null;
            float best = 0f;
            foreach (MeshFilter mf in pivot.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = pivot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Vector3 ex = m.MultiplyVector(new Vector3(b.extents.x, 0f, 0f)), ey = m.MultiplyVector(new Vector3(0f, b.extents.y, 0f)),
                        ez = m.MultiplyVector(new Vector3(0f, 0f, b.extents.z));
                Vector3 e = new Vector3(Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x), Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y),
                    Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z));
                if (e.x > 0.008f || e.z > 0.008f || e.y < 0.3f || e.y < best) continue;
                best = e.y;
                Vector3 c = m.MultiplyPoint3x4(b.center);
                _tipTop = c + Vector3.up * e.y;
                _tipBottom = c - Vector3.up * e.y;
                _staticString = mr;
            }
        }

        private void UpdateBowExtras(Transform pivot, bool show)
        {
            if (pivot == null) { HideBowExtras(); return; }
            FindString(pivot);
            if (_string == null)
            {
                var go = new GameObject("Bogensehne");
                go.transform.SetParent(transform, false);
                _string = go.AddComponent<LineRenderer>();
                _string.positionCount = 3;
                _string.useWorldSpace = true;
                _string.widthMultiplier = 0.006f;
                _string.numCapVertices = 1;
                _string.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh != null) _string.sharedMaterial = new Material(sh) { color = new Color(0.86f, 0.83f, 0.74f) };
            }
            if (_nockArrow == null)
            {
                var a = new GameObject("Aufgelegter Pfeil").transform;
                a.SetParent(transform, false);
                a.gameObject.AddComponent<MeshFilter>().sharedMesh = ProjectileSystem.ArrowMesh(0.78f);
                a.gameObject.AddComponent<MeshRenderer>().sharedMaterials = ProjectileSystem.ArrowMaterials();
                _nockArrow = a;
            }
            Vector3 top = pivot.TransformPoint(_tipTop), bottom = pivot.TransformPoint(_tipBottom);
            Vector3 mid = (top + bottom) * 0.5f;
            ArmRig hand = _hands.OtherArm;
            // Sehne haengt an der Zughand, solange sie sie haelt (aufgelegt/gespannt)
            Vector3 nock = mid;
            if (show && (_nocked || _draw > 0.01f) && _fetchT < 0f && _releaseT < 0f && hand.Valid)
            {
                Vector3 fc = hand.Center;
                if (Vector3.Distance(fc, mid) < 0.9f) nock = fc;
            }
            _string.gameObject.SetActive(true);
            _string.SetPosition(0, top);
            _string.SetPosition(1, nock);
            _string.SetPosition(2, bottom);
            if (_staticString != null) _staticString.enabled = false;

            bool arrow = show && (_nocked || (_fetchT >= 0.5f));
            _nockArrow.gameObject.SetActive(arrow);
            if (arrow)
            {
                Vector3 rest = pivot.TransformPoint(new Vector3(-0.018f, 0.06f, 0.01f));
                Vector3 tail, dir;
                if (_nocked) { tail = nock; dir = rest - nock; }
                else
                {
                    // noch in der Hand: Pfeil zeigt von der Faust in Bewegungsrichtung zur Sehne
                    tail = hand.Center;
                    // Spitze nach oben-vorn-aussen (weg vom Kopf), dann zur Pfeilauflage
                    Vector3 up0 = (_u + _rt * 0.35f + _f * 0.25f).normalized;
                    dir = Vector3.Slerp(up0, (rest - tail).normalized, MotionMath.SmoothStep01(Mathf.Clamp01((_fetchT - 0.5f) / 0.5f)));
                }
                if (dir.sqrMagnitude < 1e-6f) dir = pivot.forward;
                dir.Normalize();
                _nockArrow.SetPositionAndRotation(tail + dir * 0.37f, Quaternion.LookRotation(dir, _u));
                _nockArrow.localScale = Vector3.one / Mathf.Max(1e-4f, transform.lossyScale.x);
            }
        }

        private void HideBowExtras()
        {
            if (_string != null) _string.gameObject.SetActive(false);
            if (_nockArrow != null) _nockArrow.gameObject.SetActive(false);
            if (_staticString != null) _staticString.enabled = true;
        }
    }
}
