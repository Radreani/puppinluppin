using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(1)]
public class FPSCharacterController : MonoBehaviour, IKnockbackVelocityReceiver
{
    [Header("References")]
    [Tooltip("Camera for look pitch and for any camera-relative air movement. Auto-filled from children if empty.")]
    [SerializeField] Camera playerCamera;

    [Header("Look")]
    [Tooltip("Mouse look sensitivity scaler (Input System delta is scaled in FpsKeyBindingInput).")]
    [SerializeField] float mouseSensitivity = 2f;
    [Tooltip("Minimum look pitch in degrees (looking down).")]
    [SerializeField] float minPitch = -90f;
    [Tooltip("Maximum look pitch in degrees (looking up).")]
    [SerializeField] float maxPitch = 90f;
    [Tooltip("Lock and hide the cursor when play mode starts.")]
    [SerializeField] bool lockCursorOnPlay = true;

    [Header("Ground detection")]
    [Tooltip("Layers counted as ground for landing, jump reset, and grounded checks.")]
    [SerializeField] LayerMask groundLayer;
    [Tooltip("If enabled, grounded when the probe hits the mask OR CharacterController reports grounded. If disabled, both must be true.")]
    [SerializeField] bool preferLayerProbeOverCcGrounded = true;
    [Tooltip("Extra downward offset for the ground probe sphere (meters).")]
    [SerializeField, Min(0f)] float groundProbeSkin = 0.06f;

    [Header("Ground & debris skim")]
    [Tooltip("When the probe misses your Ground layer but the Character Controller still reports grounded (roofs, Default-layer debris), planar speed above this keeps you airborne so you do not \"land\" while skimming past.")]
    [SerializeField, Min(0f)] float ccGroundOnlyMaxPlanarSpeed = 14f;
    [Tooltip("In that same CC-only case, upward velocity above this (m/s) keeps you airborne (e.g. clearing a flat roof).")]
    [SerializeField, Min(0f)] float ccGroundOnlyMaxUpwardSpeed = 0.45f;
    [Tooltip("While moving at least this fast (m/s), step offset is temporarily reduced so the capsule does not auto-step up every shard of debris.")]
    [SerializeField, Min(0f)] float fastMoveReduceStepMinSpeed = 20f;
    [SerializeField, Min(0f)] float fastMoveStepOffset = 0.08f;

    [Header("Ground movement")]
    [Tooltip("Max horizontal speed while walking (m/s).")]
    [SerializeField] float groundMoveSpeed = 6f;
    [Tooltip("Acceleration toward walk speed when pressing move keys (m/s²).")]
    [SerializeField, Min(0f)] float groundAcceleration = 80f;
    [Tooltip("Deceleration when releasing move keys on the ground (m/s²).")]
    [SerializeField, Min(0f)] float groundDeceleration = 60f;

    [Header("Air movement")]
    [Tooltip("Max planar air-control speed when not air sprinting and not in post-jump flight (m/s).")]
    [SerializeField] float airMoveSpeed = 4f;
    [Tooltip("Acceleration toward air move target (m/s²).")]
    [SerializeField, Min(0f)] float airAcceleration = 40f;
    [Tooltip("Deceleration when releasing move keys in the air (m/s²).")]
    [SerializeField, Min(0f)] float airDeceleration = 25f;

    [Header("Dash")]
    [Tooltip("Max seconds between two taps of the same move key to register a dash.")]
    [SerializeField, Min(0.01f)] float dashDoubleTapWindow = 0.35f;
    [Tooltip("Seconds after a dash finishes before another dash can start.")]
    [SerializeField, Min(0f)] float dashCooldown = 0.75f;
    [Tooltip("Ground dash travel distance along the tapped direction (meters). Dash time = distance ÷ speed.")]
    [SerializeField, Min(0.01f)] float groundDashDistance = 2.16f;
    [Tooltip("Ground dash speed along the tapped direction (m/s).")]
    [SerializeField, Min(0.01f)] float groundDashSpeed = 18f;
    [Tooltip("Air dash travel distance along the tapped direction (meters). Dash time = distance ÷ speed.")]
    [SerializeField, Min(0.01f)] float airDashDistance = 2.4f;
    [Tooltip("Air dash speed along the tapped direction (m/s).")]
    [SerializeField, Min(0.01f)] float airDashSpeed = 20f;

    [Header("Sprint")]
    [Tooltip("Grounded + sprint: forward-only ground sprint (W/S along facing, A/D ignored). Airborne + sprint: after Air Sprint Windup (if any), air sprint — no gravity, W/S along camera forward; same tuning for falling or post-jump flight while sprint is held.")]
    [SerializeField] float sprintGroundMoveSpeed = 9f;
    [Tooltip("Ground sprint acceleration (m/s²).")]
    [SerializeField, Min(0f)] float sprintGroundAcceleration = 55f;
    [Tooltip("Air sprint target speed along camera forward (m/s). Used whenever sprint is held in the air, including while post-jump flying.")]
    [SerializeField] float sprintAirMoveSpeed = 7f;
    [Tooltip("Air sprint acceleration along look direction (m/s²).")]
    [SerializeField, Min(0f)] float sprintAirAcceleration = 35f;
    [Tooltip("When air sprinting with no W/S, velocity bleeds toward zero (m/s²).")]
    [SerializeField, Min(0f)] float sprintAirDeceleration = 28f;
    [Tooltip("While air sprinting with W or S held, vertical velocity is pushed toward 0 (m/s²) so you cancel fall and drive along your aim.")]
    [SerializeField, Min(0f)] float airSprintVerticalStabilization = 55f;
    [Tooltip("Air sprint only: hang motionless (planar + vertical velocity cleared) for this long before air sprint thrust applies. Ground sprint is unaffected. Use 0 to skip.")]
    [FormerlySerializedAs("airDashWindupDuration")]
    [SerializeField, Min(0f)] float airSprintWindupDuration = 0.15f;
    [Tooltip("While air sprinting (any airborne sprint), blends wish direction toward current 3D velocity for heavier turning. Also applies during post-jump flight if sprint is held.")]
    [FormerlySerializedAs("sprintFlyDirectionCommit")]
    [SerializeField, Range(0f, 1f)] float sprintAirDirectionCommit = 0.45f;
    [Tooltip("Minimum 3D speed (m/s) before air sprint direction commit applies.")]
    [FormerlySerializedAs("sprintFlyCommitMinSpeed")]
    [SerializeField, Min(0f)] float sprintAirCommitMinSpeed = 1f;
    [Tooltip("Multiplies horizontal mouse yaw while sprint is held (ground or air). Lower = harder to turn.")]
    [SerializeField, Range(0.05f, 1f)] float sprintYawSensitivityMultiplier = 0.4f;
    [Tooltip("Ground sprint only: blends desired direction toward current velocity (heavier turns). Strafe is already disabled while sprinting.")]
    [FormerlySerializedAs("sprintDirectionCommit")]
    [SerializeField, Range(0f, 1f)] float sprintGroundDirectionCommit = 0.55f;
    [Tooltip("Ground sprint only: minimum planar speed (m/s) before ground direction commit applies.")]
    [FormerlySerializedAs("sprintCommitMinPlanarSpeed")]
    [SerializeField, Min(0f)] float sprintGroundCommitMinPlanarSpeed = 1.5f;

    [Header("Crouch & slide")]
    [Tooltip("Max horizontal speed while crouch-walking on the ground (m/s).")]
    [SerializeField] float crouchGroundMoveSpeed = 3f;
    [Tooltip("Enter slide when grounded, crouched, and planar speed ≥ this (m/s).")]
    [SerializeField, Min(0f)] float slideEnterSpeedThreshold = 4.5f;
    [Tooltip("Exit slide to crouch-walk when planar speed &lt; this (m/s). Must be ≤ enter threshold.")]
    [SerializeField, Min(0f)] float slideExitSpeedThreshold = 2.25f;
    [Tooltip("Slide speed loss (m/s²).")]
    [SerializeField, Min(0f)] float slideDeceleration = 18f;
    [Tooltip("How much WASD can steer the slide (0 = almost none, 1 = very steerable).")]
    [SerializeField, Range(0f, 1f)] float slideSteerInfluence = 0.12f;
    [Tooltip("Mouse yaw multiplier while sliding (ground + crouch).")]
    [SerializeField, Range(0.05f, 1f)] float slideYawSensitivityMultiplier = 0.35f;
    [Tooltip("Blends slide steer input toward the current slide direction.")]
    [SerializeField, Range(0f, 1f)] float slideDirectionCommit = 0.7f;
    [Tooltip("Minimum planar speed (m/s) before slide direction commit applies.")]
    [SerializeField, Min(0f)] float slideCommitMinPlanarSpeed = 0.5f;

    [Header("Air descent")]
    [Tooltip("Extra downward acceleration (m/s²) while holding crouch in the air. Stacks with gravity except during post-jump flight or air sprint.")]
    [SerializeField, Min(0f)] float airCrouchDescendAcceleration = 45f;

    [Header("Jump, gravity & post-jump flight")]
    [Tooltip("Gravity acceleration (m/s²). Should be negative.")]
    [SerializeField] float gravity = -25f;
    [Tooltip("Jump apex height (m) from a standstill; impulse uses gravity.")]
    [SerializeField] float jumpHeight = 1.2f;
    [Tooltip("Allow a second jump before landing.")]
    [SerializeField] bool allowDoubleJump;
    [Tooltip("Allow a third jump before landing (implies double jump).")]
    [SerializeField] bool allowTripleJump;
    [Tooltip("Max time you can hold jump to stay in post-jump flight. Release jump early to fall sooner.")]
    [SerializeField, Min(0f)] float postJumpFlyDuration = 0.35f;
    [Tooltip("Max speed from WASD during post-jump flight without sprint (camera-relative 3D, full strafe).")]
    [SerializeField, Min(0f)] float flyMoveSpeed = 8f;
    [Tooltip("Acceleration during post-jump flight toward move target (m/s²).")]
    [SerializeField, Min(0f)] float flyAcceleration = 60f;
    [Tooltip("Horizontal deceleration when releasing WASD during post-jump flight (m/s²). Vertical unchanged.")]
    [SerializeField, Min(0f)] float flyDeceleration = 40f;

    [Header("Abilities")]
    [Tooltip("How fast planar velocity is reduced when an ability requests stationary windup (e.g. weapon charge).")]
    [SerializeField, Min(0f)] float abilityStationaryDeceleration = 120f;
    [Tooltip("How fast vertical velocity is pushed toward zero during stationary windup in the air.")]
    [SerializeField, Min(0f)] float abilityStationaryVerticalDeceleration = 120f;

    [Header("Weapon recoil smoothing")]
    [Tooltip("Higher = snappier recoil; lower = smoother kick over a few frames.")]
    [SerializeField, Min(0.02f)] float weaponRecoilSmoothTime = 0.16f;

    [Header("Speed — camera FOV")]
    [Tooltip("Scale camera FOV with player speed.")]
    [SerializeField] bool speedFovEnabled = true;
    [Tooltip("Resting field of view in degrees (used as the floor for FOV scaling).")]
    [SerializeField, Range(40f, 120f)] float baseFov = 70f;
    [Tooltip("Maximum additional degrees of FOV added at top speed.")]
    [SerializeField, Min(0f)] float speedFovMaxIncrease = 25f;
    [Tooltip("Speed (m/s) below which FOV stays at base value.")]
    [SerializeField, Min(0f)] float speedFovMinSpeed = 10f;
    [Tooltip("Speed (m/s) at which FOV reaches its maximum (base + max increase).")]
    [SerializeField, Min(0.01f)] float speedFovMaxSpeed = 50f;
    [Tooltip("SmoothDamp time for FOV transitions (seconds).")]
    [SerializeField, Min(0.01f)] float speedFovSmoothTime = 0.12f;

    [Header("Input — rebind with the Bind rows below")]
    [SerializeField] KeyCode forwardKey = KeyCode.W;
    [SerializeField] KeyCode backwardKey = KeyCode.S;
    [SerializeField] KeyCode leftKey = KeyCode.A;
    [SerializeField] KeyCode rightKey = KeyCode.D;
    [SerializeField] KeyCode jumpKey = KeyCode.Space;
    [SerializeField] KeyCode crouchKey = KeyCode.LeftControl;
    [SerializeField] KeyCode sprintKey = KeyCode.LeftShift;
    [Tooltip("Primary fire (used by ProjectileWeapon on this object). Rebind with the Bind row below.")]
    [SerializeField] KeyCode blastKey = KeyCode.Mouse0;

    CharacterController _controller;
    float _defaultStepOffset;
    float _pitch;
    float _verticalVelocity;
    Vector3 _horizontalVelocity;
    bool _grounded;
    int _jumpsRemaining;
    bool _leftGroundFromJump;
    float _timeSinceJumpImpulse;
    bool _isSliding;

    enum DashPhase
    {
        None,
        GroundActive,
        AirActive
    }

    enum AirSprintPhase
    {
        Idle,
        WindingUp,
        Active
    }

    DashPhase _dashPhase;
    float _dashCooldownUntil;
    float _dashPhaseTimeRemaining;
    Vector3 _dashPlanarDirection;
    float _lastTapTimeForward;
    float _lastTapTimeBack;
    float _lastTapTimeLeft;
    float _lastTapTimeRight;

    AirSprintPhase _airSprintPhase;
    float _airSprintWindupTimer;

    bool _abilityStationaryWindupActive;

    float _pendingRecoilPitchUp;
    float _pendingRecoilYaw;

    float _currentFov;
    float _fovSmoothVelocity;

    PlayerCharacteristics _playerChar;
    ProjectileWeapon _projectileWeapon;

    int MaxJumps => allowTripleJump ? 3 : (allowDoubleJump ? 2 : 1);

    /// <summary>Used by abilities (e.g. blast windup). When true, movement decelerates toward stationary before other movement runs.</summary>
    public void SetAbilityStationaryWindup(bool active) => _abilityStationaryWindupActive = active;

    public Camera AimCamera => playerCamera;

    /// <summary>Fire key for <see cref="ProjectileWeapon"/>; same Input System bridge as movement/jump.</summary>
    public KeyCode BlastKey => blastKey;

    /// <summary>Current world-space velocity (horizontal + vertical) for abilities (e.g. weapon inherit velocity).</summary>
    public Vector3 WorldVelocity => new Vector3(_horizontalVelocity.x, _verticalVelocity, _horizontalVelocity.z);

    /// <summary>Grounded state for gameplay (e.g. midair damage bonus).</summary>
    public bool IsGroundedForDamage => _grounded;

    /// <summary>
    /// Queues weapon recoil applied smoothly in <see cref="HandleLook"/>.
    /// <paramref name="kickUpDegrees"/> is positive for muzzle climb (camera looks slightly up).
    /// <paramref name="yawDegrees"/> positive turns view right.
    /// </summary>
    public void ApplyWeaponRecoil(float kickUpDegrees, float yawDegrees)
    {
        _pendingRecoilPitchUp += Mathf.Max(0f, kickUpDegrees);
        _pendingRecoilYaw += yawDegrees;
    }

    void ApplyPendingWeaponRecoil()
    {
        if (playerCamera == null)
            return;
        if (Mathf.Abs(_pendingRecoilPitchUp) < 1e-5f && Mathf.Abs(_pendingRecoilYaw) < 1e-5f)
            return;

        float dt = Time.deltaTime;
        float k = weaponRecoilSmoothTime > 1e-4f ? (1f - Mathf.Exp(-dt / weaponRecoilSmoothTime)) : 1f;
        k = Mathf.Sqrt(k);
        float dp = _pendingRecoilPitchUp * k;
        float dy = _pendingRecoilYaw * k;
        _pendingRecoilPitchUp -= dp;
        _pendingRecoilYaw -= dy;

        transform.Rotate(0f, dy, 0f);
        _pitch += dp;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        playerCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    /// <summary>Adds world-space velocity for knockback (CharacterController-friendly).</summary>
    public void ApplyKnockbackVelocity(Vector3 worldDeltaVelocity)
    {
        _horizontalVelocity += new Vector3(worldDeltaVelocity.x, 0f, worldDeltaVelocity.z);
        _verticalVelocity += worldDeltaVelocity.y;
        _playerChar?.OnKnockbackReceived(worldDeltaVelocity.magnitude);
    }

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (groundLayer.value == 0)
        {
            int groundByName = LayerMask.NameToLayer("Ground");
            if (groundByName >= 0)
                groundLayer = 1 << groundByName;
        }

        if (blastKey == KeyCode.None)
            blastKey = KeyCode.Mouse0;

        _projectileWeapon = GetComponentInChildren<ProjectileWeapon>(true);
        _playerChar = GetComponent<PlayerCharacteristics>();
        _defaultStepOffset = _controller.stepOffset;
    }

    void Start()
    {
        if (lockCursorOnPlay)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (playerCamera != null)
        {
            var e = playerCamera.transform.localEulerAngles;
            _pitch = NormalizePitch(e.x);
            if (speedFovEnabled)
            {
                _currentFov = baseFov;
                playerCamera.fieldOfView = _currentFov;
            }
        }
    }

    void Update()
    {
        _grounded = EvaluateGrounded();
        bool sprintHeld = FpsKeyBindingInput.IsPressed(sprintKey);
        bool crouchHeld = FpsKeyBindingInput.IsPressed(crouchKey);

        float yawMul = GetLookYawMultiplier(sprintHeld, crouchHeld);
        HandleLook(yawMul);
        _projectileWeapon?.RunFrame(
            FpsKeyBindingInput.WasPressedThisFrame(blastKey),
            FpsKeyBindingInput.IsPressed(blastKey));
        HandleMove();
        UpdateSpeedFov();
    }

    void UpdateSpeedFov()
    {
        if (!speedFovEnabled || playerCamera == null)
            return;
        float speed = WorldVelocity.magnitude;
        float t = Mathf.Clamp01(Mathf.InverseLerp(speedFovMinSpeed, speedFovMaxSpeed, speed));
        float targetFov = baseFov + t * speedFovMaxIncrease;
        _currentFov = Mathf.SmoothDamp(_currentFov, targetFov, ref _fovSmoothVelocity, speedFovSmoothTime);
        playerCamera.fieldOfView = _currentFov;
    }

    float GetLookYawMultiplier(bool sprintHeld, bool crouchHeld)
    {
        if (_isSliding && crouchHeld && _grounded)
            return slideYawSensitivityMultiplier;
        if (sprintHeld)
            return sprintYawSensitivityMultiplier;
        return 1f;
    }

    void HandleLook(float yawSensitivityMultiplier)
    {
        if (playerCamera == null)
            return;

        ApplyPendingWeaponRecoil();

        Vector2 look = FpsKeyBindingInput.GetMouseLookDelta(mouseSensitivity);
        look.x *= yawSensitivityMultiplier;

        transform.Rotate(0f, look.x, 0f);

        _pitch -= look.y;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        playerCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    void HandleMove()
    {
        float dt = Time.deltaTime;

        if (_dashPhase == DashPhase.None)
            TryProcessDashDoubleTaps();
        if (_dashPhase != DashPhase.None)
            UpdateDashPhases(dt);

        if (_dashPhase != DashPhase.None)
        {
            Vector3 motion = new Vector3(_horizontalVelocity.x, _verticalVelocity, _horizontalVelocity.z);
            MoveWithStepTuning(motion, dt);
            return;
        }

        if (_abilityStationaryWindupActive)
        {
            ApplyAbilityStationaryWindupDecel(dt);
            Vector3 motionAbility = new Vector3(_horizontalVelocity.x, _verticalVelocity, _horizontalVelocity.z);
            MoveWithStepTuning(motionAbility, dt);
            return;
        }

        if (_grounded)
        {
            _jumpsRemaining = MaxJumps;
            if (_verticalVelocity < 0f)
                _verticalVelocity = -2f;
        }
        else if (_leftGroundFromJump)
            _timeSinceJumpImpulse += Time.deltaTime;

        bool jumpPressed = FpsKeyBindingInput.WasPressedThisFrame(jumpKey);
        bool crouchHeld = FpsKeyBindingInput.IsPressed(crouchKey);
        bool sprintHeld = FpsKeyBindingInput.IsPressed(sprintKey);

        float forward = (FpsKeyBindingInput.IsPressed(forwardKey) ? 1f : 0f) - (FpsKeyBindingInput.IsPressed(backwardKey) ? 1f : 0f);
        float strafe = (FpsKeyBindingInput.IsPressed(rightKey) ? 1f : 0f) - (FpsKeyBindingInput.IsPressed(leftKey) ? 1f : 0f);

        Vector3 input = new Vector3(strafe, 0f, forward);
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        if (jumpPressed && _jumpsRemaining > 0)
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _jumpsRemaining--;
            _leftGroundFromJump = true;
            _timeSinceJumpImpulse = 0f;
        }

        if (_grounded && !jumpPressed)
        {
            _leftGroundFromJump = false;
            _timeSinceJumpImpulse = 0f;
        }

        bool jumpHeld = FpsKeyBindingInput.IsPressed(jumpKey);
        bool inPostJumpFly = !_grounded
            && _leftGroundFromJump
            && postJumpFlyDuration > 0f
            && jumpHeld
            && _timeSinceJumpImpulse < postJumpFlyDuration;

        UpdateAirSprintWindup(dt, sprintHeld);

        bool airSprintReady = !_grounded && sprintHeld
            && (airSprintWindupDuration <= 0f || _airSprintPhase == AirSprintPhase.Active);

        if (inPostJumpFly)
        {
            if (sprintHeld && !airSprintReady)
            {
                // Windup: velocities cleared in UpdateAirSprintWindup
            }
            else
                ApplyPostJumpFlyMovement(input, crouchHeld, sprintHeld && airSprintReady);
        }
        else if (!_grounded && sprintHeld && airSprintReady)
            ApplyAirSprintMovement(forward, crouchHeld);
        else if (!_grounded && sprintHeld)
        {
            // Air sprint windup without post-jump fly
        }
        else
            ApplyStandardMovement(input, crouchHeld, sprintHeld);

        Vector3 motionNormal = new Vector3(_horizontalVelocity.x, _verticalVelocity, _horizontalVelocity.z);
        MoveWithStepTuning(motionNormal, dt);
    }

    void MoveWithStepTuning(Vector3 worldVelocity, float dt)
    {
        bool reduceStep = worldVelocity.magnitude >= fastMoveReduceStepMinSpeed;
        if (reduceStep)
            _controller.stepOffset = fastMoveStepOffset;
        try
        {
            _controller.Move(worldVelocity * dt);
        }
        finally
        {
            if (reduceStep)
                _controller.stepOffset = _defaultStepOffset;
        }
    }

    void TryProcessDashDoubleTaps()
    {
        if (Time.time < _dashCooldownUntil)
            return;

        if (FpsKeyBindingInput.WasPressedThisFrame(forwardKey) && IsSecondTap(ref _lastTapTimeForward))
            TryBeginDash(PlanarDirection(transform.forward));
        else if (FpsKeyBindingInput.WasPressedThisFrame(backwardKey) && IsSecondTap(ref _lastTapTimeBack))
            TryBeginDash(PlanarDirection(-transform.forward));
        else if (FpsKeyBindingInput.WasPressedThisFrame(rightKey) && IsSecondTap(ref _lastTapTimeRight))
            TryBeginDash(PlanarDirection(transform.right));
        else if (FpsKeyBindingInput.WasPressedThisFrame(leftKey) && IsSecondTap(ref _lastTapTimeLeft))
            TryBeginDash(PlanarDirection(-transform.right));
    }

    bool IsSecondTap(ref float lastTapTime)
    {
        float now = Time.time;
        bool second = lastTapTime > 0f && (now - lastTapTime) <= dashDoubleTapWindow;
        lastTapTime = now;
        return second;
    }

    static Vector3 PlanarDirection(Vector3 world)
    {
        Vector3 p = Vector3.ProjectOnPlane(world, Vector3.up);
        return p.sqrMagnitude > 0.0001f ? p.normalized : Vector3.zero;
    }

    void TryBeginDash(Vector3 planarDir)
    {
        if (planarDir.sqrMagnitude < 0.0001f)
            return;

        _dashPlanarDirection = planarDir;

        if (_grounded)
        {
            _dashPhase = DashPhase.GroundActive;
            _dashPhaseTimeRemaining = DashTimeFromDistanceAndSpeed(groundDashDistance, groundDashSpeed);
        }
        else
        {
            _dashPhase = DashPhase.AirActive;
            _dashPhaseTimeRemaining = DashTimeFromDistanceAndSpeed(airDashDistance, airDashSpeed);
        }
    }

    static float DashTimeFromDistanceAndSpeed(float distance, float speed)
    {
        return Mathf.Max(0f, distance) / Mathf.Max(speed, 0.0001f);
    }

    void UpdateAirSprintWindup(float dt, bool sprintHeld)
    {
        if (_grounded || !sprintHeld)
        {
            _airSprintPhase = AirSprintPhase.Idle;
            return;
        }

        if (airSprintWindupDuration <= 0f)
        {
            _airSprintPhase = AirSprintPhase.Active;
            return;
        }

        if (_airSprintPhase == AirSprintPhase.Idle)
        {
            _airSprintPhase = AirSprintPhase.WindingUp;
            _airSprintWindupTimer = airSprintWindupDuration;
        }

        if (_airSprintPhase == AirSprintPhase.WindingUp)
        {
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _airSprintWindupTimer -= dt;
            if (_airSprintWindupTimer <= 0f)
                _airSprintPhase = AirSprintPhase.Active;
        }
    }

    void UpdateDashPhases(float dt)
    {
        switch (_dashPhase)
        {
            case DashPhase.GroundActive:
                _dashPhaseTimeRemaining -= dt;
                _horizontalVelocity = _dashPlanarDirection * groundDashSpeed;
                if (_grounded)
                {
                    if (_verticalVelocity < 0f)
                        _verticalVelocity = -2f;
                }
                else
                    _verticalVelocity += gravity * dt;

                if (_dashPhaseTimeRemaining <= 0f)
                    EndDash();
                break;

            case DashPhase.AirActive:
                _dashPhaseTimeRemaining -= dt;
                _horizontalVelocity = _dashPlanarDirection * airDashSpeed;
                _verticalVelocity = 0f;

                if (_dashPhaseTimeRemaining <= 0f)
                    EndDash();
                break;
        }
    }

    void EndDash()
    {
        _dashPhase = DashPhase.None;
        _dashCooldownUntil = Time.time + dashCooldown;
    }

    void ApplyPostJumpFlyMovement(Vector3 inputXZ, bool crouchHeld, bool sprintHeld)
    {
        Vector3 vel = new Vector3(_horizontalVelocity.x, _verticalVelocity, _horizontalVelocity.z);

        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 camForward = cam.forward;

        Vector3 wish = sprintHeld
            ? camForward * inputXZ.z
            : camForward * inputXZ.z + cam.right * inputXZ.x;

        float dt = Time.deltaTime;

        float cap = sprintHeld ? sprintAirMoveSpeed : flyMoveSpeed;
        float accel = sprintHeld ? sprintAirAcceleration : flyAcceleration;
        float decel = flyDeceleration;

        if (wish.sqrMagnitude > 0.0001f)
        {
            wish.Normalize();
            if (sprintHeld && vel.sqrMagnitude > sprintAirCommitMinSpeed * sprintAirCommitMinSpeed)
                wish = Vector3.Slerp(wish, vel.normalized, sprintAirDirectionCommit).normalized;
            Vector3 targetVel = wish * cap;
            vel = Vector3.MoveTowards(vel, targetVel, accel * dt);
            if (sprintHeld)
                vel.y = Mathf.MoveTowards(vel.y, 0f, airSprintVerticalStabilization * dt);
        }
        else
        {
            if (sprintHeld)
                vel = Vector3.MoveTowards(vel, Vector3.zero, sprintAirDeceleration * dt);
            else
            {
                Vector3 horiz = Vector3.ProjectOnPlane(vel, Vector3.up);
                horiz = Vector3.MoveTowards(horiz, Vector3.zero, decel * dt);
                vel = new Vector3(horiz.x, vel.y, horiz.z);
            }
        }

        if (crouchHeld)
            vel.y -= airCrouchDescendAcceleration * dt;

        _horizontalVelocity = new Vector3(vel.x, 0f, vel.z);
        _verticalVelocity = vel.y;
    }

    void ApplyAirSprintMovement(float forwardAxis, bool crouchHeld)
    {
        Vector3 vel = new Vector3(_horizontalVelocity.x, _verticalVelocity, _horizontalVelocity.z);
        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 camForward = cam.forward;
        float dt = Time.deltaTime;

        if (Mathf.Abs(forwardAxis) > 0.001f)
        {
            Vector3 targetDir = camForward.normalized * Mathf.Sign(forwardAxis);
            Vector3 targetVel = targetDir * sprintAirMoveSpeed;
            if (vel.sqrMagnitude > sprintAirCommitMinSpeed * sprintAirCommitMinSpeed)
                targetDir = Vector3.Slerp(targetDir, vel.normalized, sprintAirDirectionCommit).normalized;
            targetVel = targetDir * sprintAirMoveSpeed;
            vel = Vector3.MoveTowards(vel, targetVel, sprintAirAcceleration * dt);
            vel.y = Mathf.MoveTowards(vel.y, 0f, airSprintVerticalStabilization * dt);
        }
        else
            vel = Vector3.MoveTowards(vel, Vector3.zero, sprintAirDeceleration * dt);

        if (crouchHeld)
            vel.y -= airCrouchDescendAcceleration * dt;

        _horizontalVelocity = new Vector3(vel.x, 0f, vel.z);
        _verticalVelocity = vel.y;
    }

    void ApplyStandardMovement(Vector3 inputXZ, bool crouchHeld, bool sprintHeld)
    {
        Vector3 currentPlanar = Vector3.ProjectOnPlane(_horizontalVelocity, Vector3.up);
        float planarMag = currentPlanar.magnitude;

        UpdateSlideState(crouchHeld, planarMag);

        if (_grounded && _isSliding && crouchHeld)
        {
            ApplySlideMovement(inputXZ, ref currentPlanar);
            _horizontalVelocity = new Vector3(currentPlanar.x, 0f, currentPlanar.z);
            if (_verticalVelocity < 0f)
                _verticalVelocity = -2f;
            return;
        }

        Vector3 moveInput = inputXZ;
        if (sprintHeld && _grounded && !crouchHeld)
            moveInput = new Vector3(0f, 0f, inputXZ.z);

        Vector3 inputWorld = transform.TransformDirection(moveInput);

        float speed;
        float accel;
        float decel;

        if (_grounded)
        {
            decel = groundDeceleration;
            if (crouchHeld)
            {
                speed = crouchGroundMoveSpeed;
                accel = groundAcceleration;
            }
            else if (sprintHeld)
            {
                speed = sprintGroundMoveSpeed;
                accel = sprintGroundAcceleration;
            }
            else
            {
                speed = groundMoveSpeed;
                accel = groundAcceleration;
            }
        }
        else
        {
            decel = airDeceleration;
            speed = airMoveSpeed;
            accel = airAcceleration;
        }

        Vector3 wishDir = Vector3.ProjectOnPlane(inputWorld, Vector3.up);
        if (wishDir.sqrMagnitude > 0.0001f)
        {
            wishDir.Normalize();
            if (sprintHeld && _grounded && planarMag > sprintGroundCommitMinPlanarSpeed)
                wishDir = Vector3.Slerp(wishDir, currentPlanar.normalized, sprintGroundDirectionCommit).normalized;
        }

        Vector3 targetPlanar = wishDir * speed;
        if (moveInput.sqrMagnitude < 0.0001f)
            targetPlanar = Vector3.zero;

        if (targetPlanar.sqrMagnitude > 0.0001f)
            currentPlanar = Vector3.MoveTowards(currentPlanar, targetPlanar, accel * Time.deltaTime);
        else
            currentPlanar = Vector3.MoveTowards(currentPlanar, Vector3.zero, decel * Time.deltaTime);

        _horizontalVelocity = new Vector3(currentPlanar.x, 0f, currentPlanar.z);

        float dt = Time.deltaTime;
        _verticalVelocity += gravity * dt;

        if (!_grounded && crouchHeld)
            _verticalVelocity -= airCrouchDescendAcceleration * dt;
    }

    void ApplyAbilityStationaryWindupDecel(float dt)
    {
        Vector3 planar = Vector3.ProjectOnPlane(_horizontalVelocity, Vector3.up);
        planar = Vector3.MoveTowards(planar, Vector3.zero, abilityStationaryDeceleration * dt);
        _horizontalVelocity = new Vector3(planar.x, 0f, planar.z);
        _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, 0f, abilityStationaryVerticalDeceleration * dt);
        if (_grounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
    }

    void OnValidate()
    {
        if (slideExitSpeedThreshold > slideEnterSpeedThreshold)
            slideExitSpeedThreshold = slideEnterSpeedThreshold;
        groundDashSpeed = Mathf.Max(groundDashSpeed, 0.01f);
        airDashSpeed = Mathf.Max(airDashSpeed, 0.01f);
    }

    void UpdateSlideState(bool crouchHeld, float planarMag)
    {
        if (!_grounded || !crouchHeld)
        {
            _isSliding = false;
            return;
        }

        if (_isSliding)
        {
            if (planarMag < slideExitSpeedThreshold)
                _isSliding = false;
        }
        else if (planarMag >= slideEnterSpeedThreshold)
            _isSliding = true;
    }

    void ApplySlideMovement(Vector3 inputXZ, ref Vector3 currentPlanar)
    {
        float dt = Time.deltaTime;
        float mag = currentPlanar.magnitude;
        if (mag < 0.001f)
        {
            currentPlanar = Vector3.zero;
            return;
        }

        Vector3 dir = currentPlanar / mag;
        mag = Mathf.Max(0f, mag - slideDeceleration * dt);

        Vector3 inputWorld = transform.TransformDirection(inputXZ);
        Vector3 inputPlanar = Vector3.ProjectOnPlane(inputWorld, Vector3.up);
        if (inputPlanar.sqrMagnitude > 0.0001f)
        {
            inputPlanar.Normalize();
            if (mag > slideCommitMinPlanarSpeed)
                inputPlanar = Vector3.Slerp(inputPlanar, dir, slideDirectionCommit).normalized;
            float steer = slideSteerInfluence * 6f * dt;
            dir = Vector3.Slerp(dir, inputPlanar, Mathf.Clamp01(steer)).normalized;
        }

        currentPlanar = dir * mag;
    }

    bool EvaluateGrounded()
    {
        bool cc = _controller.isGrounded;

        if (groundLayer.value == 0)
            return cc;

        Vector3 worldCenter = transform.TransformPoint(_controller.center);
        float halfHeight = _controller.height * 0.5f;
        float bottomOffset = halfHeight - _controller.radius;
        Vector3 sphereCenter = worldCenter + Vector3.down * (bottomOffset + groundProbeSkin);
        float probeRadius = Mathf.Max(_controller.radius * 0.9f, 0.05f);
        bool layerHit = Physics.CheckSphere(
            sphereCenter,
            probeRadius,
            groundLayer,
            QueryTriggerInteraction.Ignore);

        if (preferLayerProbeOverCcGrounded)
        {
            if (layerHit)
                return true;
            if (!cc)
                return false;

            float planarSq = _horizontalVelocity.x * _horizontalVelocity.x + _horizontalVelocity.z * _horizontalVelocity.z;
            float maxPlanar = Mathf.Max(0f, ccGroundOnlyMaxPlanarSpeed);
            if (planarSq > maxPlanar * maxPlanar)
                return false;
            if (_verticalVelocity > ccGroundOnlyMaxUpwardSpeed)
                return false;
            return true;
        }

        return layerHit && cc;
    }

    static float NormalizePitch(float eulerX)
    {
        if (eulerX > 180f)
            eulerX -= 360f;
        return eulerX;
    }
}
