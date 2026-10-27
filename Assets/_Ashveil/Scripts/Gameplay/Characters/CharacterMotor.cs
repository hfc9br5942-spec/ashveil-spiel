using System;
using Ashveil.Core;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Bewegungsmotor fuer Spieler (und spaeter humanoide NPCs).
    ///
    /// Architektur:
    ///   MoveCommand --Simulate(cmd, dt)--> MotorState  (+ CharacterController bewegt den Koerper)
    ///
    /// Der Motor liest KEIN Input und KEINE Zeit selbst. Wer ihn aufruft, bestimmt Befehl und
    /// Zeitschritt. Heute ist das <see cref="PlayerCharacter"/> mit Time.deltaTime; mit dem
    /// Netzwerk-Layer (Milestone 3) ruft der Netzwerk-Tick denselben Code auf – auf dem Client
    /// zur Vorhersage, auf dem Server als Autoritaet. Mit <see cref="CaptureState"/> und
    /// <see cref="RestoreState"/> kann der Client auf einen Serverzustand zurueckgesetzt werden.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [SerializeField] private CharacterMotorSettings settings = new CharacterMotorSettings();

        private CharacterController _controller;
        private MotorState _state;

        // Bodenkontakt, gesammelt waehrend CharacterController.Move()
        private bool _hitGroundDuringMove;
        private Vector3 _groundNormalDuringMove = Vector3.up;
        private Vector3 _groundNormal = Vector3.up;

        /// <summary>Abgesprungen (fuer Animation/Sound).</summary>
        public event Action Jumped;
        /// <summary>Gelandet; Parameter = Aufprallgeschwindigkeit in m/s.</summary>
        public event Action<float> Landed;
        /// <summary>Ausweichrolle begonnen; Parameter = Richtung (Welt, horizontal).</summary>
        public event Action<Vector3> Dodged;

        public bool IsDodging => _state.DodgeTimer > 0f;
        /// <summary>Aktive Ausweichrolle erlaubt (Klassenfaehigkeit – Kampfumbau: nur der Aschenjaeger).</summary>
        public bool CanRoll { get; set; } = true;

        public CharacterMotorSettings Settings => settings;
        public MotorState State => _state;
        public CharacterController Controller => _controller;

        /// <summary>Staerke der Bewegungseingabe des letzten Simulate (0..1). 16.10.: Die Haltung unterscheidet damit echtes
        /// Anhalten (Taste los) von einem Tempoverlust mit gehaltener Taste (Treppe, Hang, Hindernis). Stellvertreter anderer
        /// Spieler kennen die Eingabe nicht – dort aus der Geschwindigkeit abgeleitet.</summary>
        public float InputMagnitude { get; private set; }

        public Vector3 HorizontalVelocity => new Vector3(_state.Velocity.x, 0f, _state.Velocity.z);
        public float HorizontalSpeed => HorizontalVelocity.magnitude;

        /// <summary>Geschwindigkeit relativ zur Blickrichtung (x = seitwaerts, z = vorwaerts).</summary>
        public Vector3 LocalVelocity => Quaternion.Euler(0f, -_state.Yaw, 0f) * HorizontalVelocity;

        public bool IsOnSteepSlope =>
            _state.Grounded && Vector3.Angle(_groundNormal, Vector3.up) > _controller.slopeLimit + 0.5f;

        /// <summary>Schwimmt (MOV-169, Welle 0).</summary>
        public bool IsSwimming => _state.Swimming;
        /// <summary>Kopf unter Wasser – die Atemleiste läuft ab.</summary>
        public bool IsUnderwater => _state.Underwater;
        /// <summary>Atem 0..1 (1 = voll).</summary>
        public float Breath01 => settings.breathSeconds > 0f ? Mathf.Clamp01(_state.Breath / settings.breathSeconds) : 1f;

        private void Awake()
        {
            if (settings == null) settings = new CharacterMotorSettings();
            settings.Migrate();
            _controller = GetComponent<CharacterController>();
            _state.Position = transform.position;
            _state.Yaw = transform.eulerAngles.y;
            _state.Stamina = settings.maxStamina;
            _state.Breath = settings.breathSeconds;
            _state.Grounded = true;
        }

        /// <summary>
        /// Fuehrt genau einen Simulationsschritt aus. Deterministisch bezueglich (Zustand, Befehl, dt)
        /// bis auf die Physik-Kollisionen der Umgebung, die Server und Client identisch haben.
        /// </summary>
        public void Simulate(in MoveCommand cmd, float dt)
        {
            if (dt <= 0f || !_controller.enabled) return;
            CharacterMotorSettings s = settings;

            // ---------- Blickrichtung ----------
            if (cmd.SteerWithCamera) _state.Yaw = cmd.DesiredYaw;
            else _state.Yaw += Mathf.Clamp(cmd.Turn, -1f, 1f) * (_state.Mounted ? s.mountedTurnSpeed : s.turnSpeed) * dt;
            _state.Yaw = Mathf.Repeat(_state.Yaw, 360f);
            Quaternion yawRotation = Quaternion.Euler(0f, _state.Yaw, 0f);

            // ---------- Eingabe normalisieren ----------
            var input = new Vector2(Mathf.Clamp(cmd.Strafe, -1f, 1f), Mathf.Clamp(cmd.Forward, -1f, 1f));
            if (input.sqrMagnitude > 1f) input.Normalize();
            bool hasInput = input.sqrMagnitude > 0.0001f;
            InputMagnitude = input.magnitude;
            bool movingBackward = input.y < -0.1f;

            // ---------- Wasser (MOV-169): tief genug → schwimmen statt gehen ----------
            bool inWater = WaterVolume.TrySurface(transform.position, out float surfaceY);
            float depth = inWater ? surfaceY - transform.position.y : -1f;
            bool swim = SwimRules.ShouldSwim(_state.Swimming, _state.Velocity.y, depth, s.swimEnterDepth, s.swimExitDepth);
            // An der Oberfläche wird die Sprungtaste zum Sprung (Classic): so kommt man auf Stege und steile Ufer.
            // Der normale Schritt springt ab; solange die Figur steigt, taucht sie nicht wieder ein (ShouldSwim).
            bool jumpOut = swim && _state.Swimming && cmd.Jump && SwimRules.CanJumpOut(depth, s.swimFloatDepth);
            if (swim && !jumpOut)
            {
                SimulateSwim(cmd, dt, yawRotation, input, hasInput, surfaceY, depth);
                return;
            }
            if (_state.Swimming)
            {
                // Aus dem Wasser heraus: wieder gehen. Wer schwimmt, steht – keine Landung melden (keine Landeanimation,
                // kein Landeschaden, kein falscher Sturzangriff). Den Boden findet der normale Schritt.
                _state.Swimming = false;
                _state.Underwater = false;
                _state.Drowning = false;
                _state.Grounded = true;
                _state.AirTime = 0f;
                _state.PeakFallSpeed = 0f;
                _groundNormal = Vector3.up;
                if (jumpOut) _state.CoyoteTimer = s.coyoteTime;
            }

            // ---------- Ausdauer & Sprint ----------
            if (_state.Exhausted && _state.Stamina >= s.staminaRecoverThreshold) _state.Exhausted = false;
            bool slowGait = cmd.Walk || cmd.EpicWalk;
            bool sprinting = !_state.Mounted && _state.DodgeTimer <= 0f && cmd.Sprint && !slowGait && input.y > 0.1f && !_state.Exhausted && _state.Stamina > 0f;
            if (sprinting)
            {
                _state.Stamina -= s.sprintStaminaPerSecond * (1f - _state.SprintSaving) * dt;
                _state.StaminaRegenDelay = s.staminaRegenDelay;
                if (_state.Stamina <= 0f)
                {
                    _state.Stamina = 0f;
                    _state.Exhausted = true;
                    sprinting = false;
                }
            }
            else if (_state.StaminaRegenDelay > 0f)
            {
                _state.StaminaRegenDelay -= dt;
            }
            else
            {
                _state.Stamina = Mathf.Min(s.maxStamina, _state.Stamina + s.staminaRegenPerSecond * dt);
            }
            _state.IsSprinting = sprinting;
            _state.IsWalking = cmd.Walk && !cmd.EpicWalk;
            _state.IsEpicWalking = cmd.EpicWalk && !_state.Mounted;
            _state.IsJogging = cmd.Jog && !slowGait && !sprinting && !_state.Mounted;

            // ---------- Ausweichrolle ----------
            if (_state.DodgeCooldown > 0f) _state.DodgeCooldown = Mathf.Max(0f, _state.DodgeCooldown - dt);
            bool dodgeStarted = false;
            float dodgeCost = s.dodgeStaminaCost * (1f - _state.DodgeSaving);
            if (cmd.Dodge && CanRoll && !_state.Mounted && _state.Grounded && _state.DodgeTimer <= 0f && _state.DodgeCooldown <= 0f &&
                _state.Stamina >= dodgeCost)
            {
                // Ohne Richtungseingabe: nach vorn hechten (Blickrichtung)
                Vector2 dirInput = hasInput ? input.normalized : new Vector2(0f, 1f);
                _state.DodgeDirection = (yawRotation * new Vector3(dirInput.x, 0f, dirInput.y)).normalized;
                _state.DodgeTimer = s.dodgeDuration;
                _state.DodgeCooldown = s.dodgeCooldown;
                _state.Stamina -= dodgeCost;
                _state.StaminaRegenDelay = s.staminaRegenDelay * (1f - _state.DodgeRecovery);
                dodgeStarted = true;
            }

            // ---------- Zielgeschwindigkeit ----------
            float speed = _state.IsEpicWalking ? s.epicWalkSpeed : cmd.Walk ? s.walkSpeed : sprinting ? s.sprintSpeed : _state.IsJogging ? s.jogSpeed : s.runSpeed;
            if (_state.Mounted) speed = cmd.Walk ? s.walkSpeed * s.mountedWalkFactor : s.runSpeed * (1f + _state.MountSpeed);

            // ---------- Reiserhythmus (nur zu Fuss, ausserhalb des Kampfes, beim echten Laufen – nie im Kampf) ----------
            float planar = HorizontalSpeed;
            bool travelling = !_state.Mounted && !_state.InCombat && _state.Grounded && hasInput && !slowGait && !_state.IsJogging && !movingBackward &&
                              _state.DodgeTimer <= 0f && planar >= s.runSpeed * 0.85f;
            if (_state.InCombat || _state.Mounted) _state.TravelMomentum = 0f;
            else if (travelling) _state.TravelMomentum = Mathf.Min(1f, _state.TravelMomentum + dt / Mathf.Max(0.1f, s.travelBuildSeconds));
            else if (!hasInput || planar < s.walkSpeed) _state.TravelMomentum = Mathf.Max(0f, _state.TravelMomentum - dt / Mathf.Max(0.1f, s.travelFadeSeconds));
            else _state.TravelMomentum = Mathf.Max(0f, _state.TravelMomentum - dt / Mathf.Max(0.1f, s.travelFadeSeconds * 2f));
            if (!slowGait && !_state.IsJogging && !movingBackward) speed *= 1f + _state.TravelMomentum * _state.TravelBonus;
            if (movingBackward) speed = Mathf.Min(speed, slowGait ? speed : s.backwardSpeed * (_state.Mounted ? s.mountedBackFactor : 1f));
            if (_state.LandingRecovery > 0f)
            {
                speed *= s.hardLandingSpeedMultiplier;
                _state.LandingRecovery = Mathf.Max(0f, _state.LandingRecovery - dt);
            }
            Vector3 targetVelocity = yawRotation * new Vector3(input.x, 0f, input.y) * speed;

            // Auf zu steilen Haengen gilt man nicht als "auf dem Boden": kein Springen, man rutscht.
            bool steep = _state.Grounded && Vector3.Angle(_groundNormal, Vector3.up) > _controller.slopeLimit + 0.5f;
            bool grounded = _state.Grounded && !steep;

            // ---------- Horizontal ----------
            Vector3 horizontal = HorizontalVelocity;
            if (_state.DashTimer > 0f)
            {
                horizontal = _state.DashVelocity;
                _state.DashTimer = Mathf.Max(0f, _state.DashTimer - dt);
                if (_state.DashTimer <= 0f) horizontal *= 0.2f; // am Ende abfangen statt weiterzurutschen
            }
            else if (_state.DodgeTimer > 0f)
            {
                // Explosiver Absprung, weiches Auslaufen: Tempo faellt linear von (1+k) auf (1-k) des Mittels.
                // k aus dodgeFrontLoad: Anteil der Strecke in der ersten Haelfte = 0,5 + k/4.
                float t = 1f - _state.DodgeTimer / Mathf.Max(0.01f, s.dodgeDuration);
                float avg = s.dodgeDistance / Mathf.Max(0.01f, s.dodgeDuration);
                float k = Mathf.Clamp((s.dodgeFrontLoad - 0.5f) * 4f, 0f, 0.95f);
                horizontal = _state.DodgeDirection * (avg * (1f + k - 2f * k * t));
                _state.DodgeTimer = Mathf.Max(0f, _state.DodgeTimer - dt);
            }
            else if (grounded)
            {
                float rate = _state.Mounted ? (hasInput ? s.mountedAcceleration : s.mountedDeceleration)
                                            : (hasInput ? s.groundAcceleration : s.groundDeceleration);
                horizontal = Vector3.MoveTowards(horizontal, targetVelocity, rate * dt);
            }
            else if (hasInput)
            {
                // In der Luft nur sanft lenken; ohne Eingabe bleibt der Schwung erhalten.
                horizontal = Vector3.MoveTowards(horizontal, targetVelocity, s.airAcceleration * dt);
            }

            // ---------- Vertikal ----------
            _state.JumpBufferTimer = cmd.Jump ? s.jumpBufferTime : Mathf.Max(0f, _state.JumpBufferTimer - dt);
            _state.CoyoteTimer = grounded ? s.coyoteTime : Mathf.Max(0f, _state.CoyoteTimer - dt);

            float vy = _state.Velocity.y;
            bool jumpedThisStep = false;
            bool lifted = false;
            if (_state.DashLift > 0f)
            {
                vy = _state.DashLift;
                _state.DashLift = 0f;
                lifted = true;
                _state.CoyoteTimer = 0f;
                _state.JumpBufferTimer = 0f;
            }
            if (cmd.Plunge && !grounded && !_state.IsPlunging)
            {
                _state.IsPlunging = true;
                horizontal *= 0.35f;
            }

            if (lifted)
            {
                // Schub nach oben (Rueckzug) – dieser Schritt hebt ab
            }
            else if (_state.IsPlunging && !grounded)
            {
                vy = -s.plungeSpeed;
            }
            else if (_state.JumpBufferTimer > 0f && _state.CoyoteTimer > 0f && _state.DodgeTimer <= 0f)
            {
                vy = s.JumpVelocity;
                _state.JumpBufferTimer = 0f;
                _state.CoyoteTimer = 0f;
                jumpedThisStep = true;
            }
            else if (grounded)
            {
                vy = -s.groundStickVelocity;
            }
            else
            {
                vy = Mathf.Max(vy - s.gravity * dt, -s.maxFallSpeed);
                // Auf zu steilem Hang kontrolliert rutschen statt immer schneller zu werden.
                if (steep) vy = Mathf.Max(vy, -s.maxSlideSpeed);
            }

            _state.Velocity = new Vector3(horizontal.x, vy, horizontal.z);

            // ---------- Bewegen ----------
            bool wasGrounded = _state.Grounded;
            _hitGroundDuringMove = false;
            _groundNormalDuringMove = Vector3.up;

            Vector3 beforeMove = transform.position;
            CollisionFlags flags = _controller.Move(_state.Velocity * dt);
            Vector3 afterMove = transform.position;
            bool nowGrounded = (flags & CollisionFlags.Below) != 0;

            // Beim Bergablaufen / ueber kleine Kanten am Boden kleben statt kurz abzuheben.
            if (!nowGrounded && wasGrounded && !jumpedThisStep && !lifted && vy <= 0f)
                nowGrounded = TrySnapToGround(s.groundSnapDistance);

            _groundNormal = _hitGroundDuringMove ? _groundNormalDuringMove : Vector3.up;

            // Kopf angestossen: Aufwaertsbewegung stoppen.
            if ((flags & CollisionFlags.Above) != 0 && _state.Velocity.y > 0f)
                _state.Velocity = new Vector3(_state.Velocity.x, 0f, _state.Velocity.z);

            // An Waenden: horizontale Geschwindigkeit an tatsaechliche Bewegung anpassen,
            // damit man nach dem Loslassen nicht "Restschwung" in die Wand hat.
            // Selbst aus diesem Schritt berechnet (nicht CharacterController.velocity: das haengt am
            // Frame-Takt und an Teleports/Korrekturen und kann riesige Werte liefern). Nur abbremsen, nie beschleunigen.
            if ((flags & CollisionFlags.Sides) != 0)
            {
                Vector3 actual = (afterMove - beforeMove) / dt;
                actual.y = 0f;
                float intended = new Vector2(_state.Velocity.x, _state.Velocity.z).magnitude;
                if (actual.magnitude > intended) actual = actual.normalized * intended;
                _state.Velocity = new Vector3(actual.x, _state.Velocity.y, actual.z);
            }

            // ---------- Landung / Luftzeit ----------
            if (!nowGrounded)
            {
                _state.AirTime += dt;
                _state.PeakFallSpeed = Mathf.Max(_state.PeakFallSpeed, -_state.Velocity.y);
            }
            else if (!wasGrounded)
            {
                float impact = Mathf.Max(_state.PeakFallSpeed, -vy);
                // Sturzangriff ist gewollt – keine "harte Landung" mit Verlangsamung.
                if (_state.IsPlunging) impact = Mathf.Min(impact, s.hardLandingSpeed - 0.01f);
                if (impact >= s.hardLandingSpeed) _state.LandingRecovery = s.hardLandingRecovery;
                _state.AirTime = 0f;
                _state.PeakFallSpeed = 0f;
                _state.Grounded = true;
                _state.IsPlunging = false;
                if (!SuppressEvents) Landed?.Invoke(impact);
            }

            _state.Grounded = nowGrounded;
            if (nowGrounded)
            {
                _state.AirTime = 0f;
                _state.PeakFallSpeed = 0f;
            }

            // An der Luft füllt sich der Atem wieder (MOV-169)
            _state.Breath = SwimRules.Breath(_state.Breath, false, dt, s.breathSeconds, s.breathRefillPerSecond);
            _state.Underwater = false;
            _state.Drowning = false;

            _state.Position = transform.position;
            transform.rotation = yawRotation;

            if (jumpedThisStep && !SuppressEvents) Jumped?.Invoke();
            if (dodgeStarted && !SuppressEvents) Dodged?.Invoke(_state.DodgeDirection);
        }

        /// <summary>
        /// Ein Schritt im Wasser (MOV-169, Welle 0): keine Schwerkraft; waagerecht mit Schwimmtempo, senkrecht mit Leertaste/X
        /// oder – mit Kamerasteuerung – dem Tauchwinkel. Über die Schwimmhöhe kommt man nicht hinaus (Kopf über Wasser).
        /// Kein Sprinten, Rollen, Ansturm oder Sturzangriff im Wasser; springen nur an der Oberfläche (aus dem Wasser, siehe
        /// <see cref="SwimRules.CanJumpOut"/>). Der Atem läuft ab, wenn der Kopf unter Wasser ist; den Ertrinkungsschaden
        /// verteilt der Server (NetworkMotion), ohne Netz die Spielfigur selbst (PlayerCharacter).
        /// </summary>
        private void SimulateSwim(in MoveCommand cmd, float dt, Quaternion yawRotation, Vector2 input, bool hasInput, float surfaceY, float depth)
        {
            CharacterMotorSettings s = settings;
            _state.Swimming = true;
            // Zustände, die es im Wasser nicht gibt
            _state.Grounded = false;
            _state.CoyoteTimer = 0f;
            _state.JumpBufferTimer = 0f;
            _state.AirTime = 0f;
            _state.PeakFallSpeed = 0f;
            _state.LandingRecovery = 0f;
            _state.IsPlunging = false;
            _state.IsSprinting = false;
            _state.IsJogging = false;
            _state.IsEpicWalking = false;
            _state.IsWalking = cmd.Walk && !cmd.EpicWalk;
            _state.DodgeTimer = 0f;
            _state.DashTimer = 0f;
            _state.DashLift = 0f;
            _state.TravelMomentum = 0f;
            if (_state.DodgeCooldown > 0f) _state.DodgeCooldown = Mathf.Max(0f, _state.DodgeCooldown - dt);

            // Ausdauer erholt sich wie beim Gehen
            if (_state.StaminaRegenDelay > 0f) _state.StaminaRegenDelay -= dt;
            else _state.Stamina = Mathf.Min(s.maxStamina, _state.Stamina + s.staminaRegenPerSecond * dt);
            if (_state.Exhausted && _state.Stamina >= s.staminaRecoverThreshold) _state.Exhausted = false;

            // Waagerecht: vorwärts teilt sich mit dem Tauchwinkel in waagerecht und senkrecht
            float dive = cmd.SteerWithCamera ? SwimRules.DivePitch(cmd.SwimPitch, s.diveDeadLow, s.diveDeadHigh) : 0f;
            float speed = input.y < -0.1f ? s.swimBackSpeed : s.swimSpeed;
            if (cmd.Walk) speed = Mathf.Min(speed, s.walkSpeed);
            float forward = input.y > 0f ? input.y * Mathf.Cos(dive * Mathf.Deg2Rad) : input.y;
            Vector3 target = yawRotation * new Vector3(input.x, 0f, forward) * speed;
            Vector3 horizontal = Vector3.MoveTowards(HorizontalVelocity, target, (hasInput ? s.swimAcceleration : s.swimDeceleration) * dt);

            // Senkrecht: Wasserwiderstand bremst Stürze ins Wasser, nie über die Schwimmhöhe
            float vyTarget = SwimRules.VerticalSpeed(cmd.SwimUp, cmd.SwimDown, dive, Mathf.Max(0f, input.y), depth, dt,
                s.swimVerticalSpeed, speed, s.swimFloatDepth);
            float vy = Mathf.MoveTowards(_state.Velocity.y, vyTarget, s.swimVerticalAcceleration * dt);
            vy = Mathf.Min(vy, (depth - s.swimFloatDepth) / dt);
            _state.Velocity = new Vector3(horizontal.x, vy, horizontal.z);

            // Bewegen (Grund und Ufer stoppen wie Wände)
            Vector3 beforeMove = transform.position;
            CollisionFlags flags = _controller.Move(_state.Velocity * dt);
            Vector3 afterMove = transform.position;
            if ((flags & CollisionFlags.Above) != 0 && _state.Velocity.y > 0f)
                _state.Velocity = new Vector3(_state.Velocity.x, 0f, _state.Velocity.z);
            if ((flags & CollisionFlags.Below) != 0 && _state.Velocity.y < 0f)
                _state.Velocity = new Vector3(_state.Velocity.x, 0f, _state.Velocity.z);
            if ((flags & CollisionFlags.Sides) != 0)
            {
                Vector3 actual = (afterMove - beforeMove) / dt;
                actual.y = 0f;
                float intended = new Vector2(_state.Velocity.x, _state.Velocity.z).magnitude;
                if (actual.magnitude > intended) actual = actual.normalized * intended;
                _state.Velocity = new Vector3(actual.x, _state.Velocity.y, actual.z);
            }

            // Atem
            bool headUnder = SwimRules.HeadUnder(surfaceY - transform.position.y, s.swimHeadHeight);
            _state.Underwater = headUnder;
            _state.Breath = SwimRules.Breath(_state.Breath, headUnder, dt, s.breathSeconds, s.breathRefillPerSecond);
            _state.Drowning = headUnder && _state.Breath <= 0f;

            _state.Position = transform.position;
            transform.rotation = yawRotation;
        }

        // ---------------------------------------------------------------- Netzwerk-Abbild

        /// <summary>
        /// Abbild eines fremden Spielers: der Motor simuliert nicht selbst, die Position kommt aus dem Netz.
        /// Er haelt nur den Zustand fuer Animation und Haltung aktuell.
        /// </summary>
        public bool IsProxy { get; private set; }

        /// <summary>Waehrend einer Netz-Korrektur werden Schritte wiederholt – dann keine Ereignisse (Klang, Animation).</summary>
        public bool SuppressEvents { get; set; }

        public void SetProxy(bool proxy) => IsProxy = proxy;

        /// <summary>Vom Netz empfangenen Bewegungszustand uebernehmen (nur Abbild).</summary>
        public void ApplyProxyState(Vector3 velocity, bool grounded, bool sprinting, bool walking, bool dodging, Vector3 dodgeDirection, bool plunging,
            bool jogging = false, bool epicWalking = false)
        {
            _state.IsJogging = jogging;
            _state.IsEpicWalking = epicWalking;
            _state.Position = transform.position;
            _state.Yaw = transform.eulerAngles.y;
            _state.Velocity = velocity;
            InputMagnitude = new Vector2(velocity.x, velocity.z).magnitude > 0.1f ? 1f : 0f;
            _state.Grounded = grounded;
            _state.IsSprinting = sprinting;
            _state.IsWalking = walking;
            _state.DodgeTimer = dodging ? 0.1f : 0f;
            _state.DodgeDirection = dodgeDirection;
            _state.IsPlunging = plunging;
            _state.LandingRecovery = 0f;
            // Schwimmen ergibt sich aus Lage und Wasserflächen (gleiche Szenendaten) – kein eigenes Netzbit nötig
            if (WaterVolume.TrySurface(transform.position, out float sy))
            {
                float d = sy - transform.position.y;
                _state.Swimming = SwimRules.ShouldSwim(_state.Swimming, velocity.y, d, settings.swimEnterDepth, settings.swimExitDepth);
                _state.Underwater = _state.Swimming && SwimRules.HeadUnder(d, settings.swimHeadHeight);
            }
            else
            {
                _state.Swimming = false;
                _state.Underwater = false;
            }
        }

        /// <summary>Ereignisse eines fremden Spielers ausloesen (Animation/Klang).</summary>
        public void RaiseProxyJumped() => Jumped?.Invoke();
        public void RaiseProxyLanded(float impact) => Landed?.Invoke(impact);
        public void RaiseProxyDodged(Vector3 direction) => Dodged?.Invoke(direction);

        /// <summary>Aktuellen Zustand fuer Netzwerk / Speichern abgreifen.</summary>
        public MotorState CaptureState()
        {
            _state.Position = transform.position;
            return _state;
        }

        /// <summary>Zustand hart setzen (Server-Korrektur, Teleport, Respawn).</summary>
        public void RestoreState(in MotorState state)
        {
            _state = state;
            bool wasEnabled = _controller.enabled;
            _controller.enabled = false; // sonst ueberschreibt der CharacterController die Position
            transform.SetPositionAndRotation(state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
            _controller.enabled = wasEnabled;
        }

        /// <summary>Wie oft der Motor versetzt wurde (Server: Teleports zaehlen nicht als Laufstrecke).</summary>
        public int TeleportCount { get; private set; }

        /// <summary>Server: Wirkungen der Praxis-Meisterschaften und Kampfzustand setzen (gehen mit dem Abgleich zum Besitzer).</summary>
        public void SetMastery(float travelBonus, float sprintSaving, float dodgeSaving, float dodgeRecovery)
        {
            _state.TravelBonus = Mathf.Clamp(travelBonus, 0f, 0.5f);
            _state.SprintSaving = Mathf.Clamp(sprintSaving, 0f, 0.9f);
            _state.DodgeSaving = Mathf.Clamp(dodgeSaving, 0f, 0.9f);
            _state.DodgeRecovery = Mathf.Clamp(dodgeRecovery, 0f, 0.9f);
        }

        public void SetInCombat(bool inCombat) => _state.InCombat = inCombat;

        /// <summary>Server (und Besitzer zur Vorhersage): auf- oder absitzen; speedBonus = Tempo-Aufschlag inkl. Bindung.</summary>
        public void SetMounted(bool mounted, float speedBonus)
        {
            _state.Mounted = mounted;
            _state.MountSpeed = mounted ? Mathf.Clamp(speedBonus, 0f, 1f) : 0f;
            if (mounted) _state.TravelMomentum = 0f;
        }

        public bool IsMounted => _state.Mounted;

        /// <summary>
        /// Server (P1): Schub fuer Ansturm/Rueckzug – waagerechte Geschwindigkeit fuer <paramref name="seconds"/>, optional
        /// ein Satz nach oben. Reist wie das Reittier mit dem Abgleich zum Besitzer.
        /// </summary>
        public void StartDash(Vector3 horizontalVelocity, float seconds, float lift)
        {
            horizontalVelocity.y = 0f;
            _state.DashVelocity = horizontalVelocity;
            _state.DashTimer = Mathf.Clamp(seconds, 0.05f, 2f);
            _state.DashLift = Mathf.Max(0f, lift);
            _state.DodgeTimer = 0f;
        }

        public bool IsDashing => _state.DashTimer > 0f;

        /// <summary>Teleport mit zurueckgesetzter Geschwindigkeit.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            MotorState s = _state;
            s.Position = position;
            s.Yaw = yaw;
            s.Velocity = Vector3.zero;
            s.Grounded = false;
            s.AirTime = 0f;
            s.PeakFallSpeed = 0f;
            s.LandingRecovery = 0f;
            s.IsPlunging = false;
            s.DodgeTimer = 0f;
            s.TravelMomentum = 0f;
            s.Swimming = false;
            s.Underwater = false;
            s.Drowning = false;
            s.Breath = settings.breathSeconds;
            TeleportCount++;
            RestoreState(s);
        }

        private bool TrySnapToGround(float maxDistance)
        {
            float radius = _controller.radius * 0.95f;
            Vector3 center = transform.TransformPoint(_controller.center);
            float halfToSphere = Mathf.Max(0f, _controller.height * 0.5f - _controller.radius);
            Vector3 origin = center + Vector3.down * halfToSphere;
            // Eigener Collider wird vom SphereCast ohnehin ignoriert (Start innerhalb der Kapsel);
            // zusaetzlich andere Spieler ausblenden, sofern der Spieler einen eigenen Layer hat.
            int mask = Physics.DefaultRaycastLayers;
            if (gameObject.layer != 0) mask &= ~(1 << gameObject.layer);

            if (!Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit,
                    maxDistance + _controller.skinWidth, mask, QueryTriggerInteraction.Ignore))
                return false;

            if (Vector3.Angle(hit.normal, Vector3.up) > _controller.slopeLimit) return false;

            float drop = hit.distance - _controller.skinWidth * 0.5f;
            if (drop > 0.001f) _controller.Move(Vector3.down * drop);
            _hitGroundDuringMove = true;
            _groundNormalDuringMove = hit.normal;
            return true;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Nur Kontakte unterhalb der Kapselmitte zaehlen als Boden.
            if (hit.moveDirection.y < -0.3f && hit.point.y < transform.position.y + _controller.radius)
            {
                _hitGroundDuringMove = true;
                _groundNormalDuringMove = hit.normal;
            }
        }

        private void OnValidate()
        {
            if (settings == null) settings = new CharacterMotorSettings();
            settings.Migrate();
        }
    }
}
