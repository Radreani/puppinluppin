using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Air enemy: ground roll → climb → cruise / attack → return cruise to base → glide → taxi → resupply → repeat.
/// Yaw follows horizontal travel; optional visual pitch (local right / X) and roll/bank (local forward / Z) on top of prefab offset.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyJetMissileBarrage))]
public class EnemyJet : MonoBehaviour, IProjectileDamageReceiver
{
    public enum JetPhase
    {
        RunwayRoll,
        TakeoffClimb,
        Cruise,
        AirCombat,
        LandingApproach,
        Resupplying
    }

    enum StrikePhase
    {
        StrafePass,
        FlyOff,
        Reposition
    }

    enum CruiseIntent
    {
        Combat,
        ReturnToBase
    }

    [Header("References")]
    [Tooltip("This jet’s runway: spawn + pad + approach. One pad per jet; duplicate the pad object for more jets.")]
    [SerializeField] EnemyJetRunwayPad runwayPad;

    [Header("Missiles (assign here)")]
    [Tooltip("e.g. child empty at launch point; +Z should point out the tubes.")]
    [SerializeField] Transform missileMuzzle;
    [Tooltip("Prefab with ProjectileWeaponEffect on the root.")]
    [SerializeField] GameObject missileProjectilePrefab;

    EnemyJetMissileBarrage _missiles;

    [Header("Health")]
    [SerializeField, Min(1f)] float maxHealth = 120f;
    [SerializeField] bool destroyOnDeath = true;
    [SerializeField, Min(0f)] float deathDestroyDelay;

    [Header("Runway (ground roll)")]
    [Tooltip("0 = skip runway; spawn already flying uses Cruise / Air Combat.")]
    [SerializeField, Min(0f)] float requiredRunwayDistance = 45f;
    [SerializeField, Min(0.01f)] float groundRollSpeed = 12f;
    [SerializeField, Min(0.1f)] float groundStickRayLength = 8f;
    [SerializeField, Min(0.01f)] float groundClearance = 0.35f;
    [SerializeField] LayerMask groundLayers = ~0;

    [Header("Takeoff & climb")]
    [SerializeField] bool spawnAlreadyAirborne;
    [Tooltip("Horizontal speed while climbing after leaving the runway (m/s).")]
    [SerializeField, Min(0.01f)] float takeoffClimbSpeed = 22f;
    [Tooltip("Climb path angle from horizontal (degrees). Vertical speed = horizontal * tan(angle).")]
    [SerializeField, Range(2f, 45f)] float takeoffClimbAngleDegrees = 12f;
    [SerializeField, Min(1f)] float speedBlendTowardTarget = 24f;
    [Tooltip("While the short unground ray still hits, we apply runway forward + climb; no StickToGround snap.")]
    [SerializeField, Min(0.5f)] float takeoffUngroundRayDistance = 3.5f;

    [Header("Cruise")]
    [FormerlySerializedAs("airSpeed")]
    [Tooltip("Speed when not in an attack pass.")]
    [SerializeField, Min(0.01f)] float cruiseSpeed = 36f;

    [Header("Flight — cruise altitude & turning")]
    [FormerlySerializedAs("cruiseAltitudeAboveTarget")]
    [Tooltip("Preferred height above the target’s feet while fighting / cruising with a target.")]
    [SerializeField, Min(8f)] float cruiseAltitudeAboveTarget = 64f;
    [SerializeField, Min(0.5f)] float altitudeChangeSpeed = 24f;
    [Tooltip("Max yaw rate when turning in cruise (deg/s).")]
    [SerializeField, Min(1f)] float cruiseYawTurnRateDegrees = 64f;
    [Tooltip("Max yaw rate in combat maneuvering (deg/s).")]
    [SerializeField, Min(1f)] float combatYawTurnRateDegrees = 78f;
    [Tooltip("Approx. minimum horizontal turn radius at current speed (m). Caps turn rate as ω ≤ v/r.")]
    [SerializeField, Min(20f)] float minHorizontalTurnRadius = 48f;
    [SerializeField, Min(1f)] float groundHeadingTurnRateDegrees = 52f;

    [Header("No target (patrol)")]
    [SerializeField, Min(4f)] float idlePatrolHeightAboveGround = 52f;
    [SerializeField, Min(0.05f)] float idlePatrolSpeedFraction = 0.35f;

    [Header("Attack pass")]
    [Tooltip("Speed during strafe / break / rejoin legs.")]
    [SerializeField, Min(0.01f)] float attackPassSpeed = 52f;
    [Tooltip("Horizontal distance: begin / stay in strike pattern when inside this (m). Too-close widening runs inside AirCombat, not cruise orbit.")]
    [SerializeField, Min(30f)] float attackEngageOuterDistance = 132f;
    [Tooltip("Inside this horizontal distance, strafe/cruise bias outward (orbit) vs pure inbound. Should stay < min missile range.")]
    [SerializeField, Min(5f)] float attackTooCloseDistance = 18f;
    [Tooltip("Unit tangent of the current strafe leg (fixed for the leg). Lower = more boom‑and‑zoom inbound, less merry‑go‑round.")]
    [SerializeField, Min(0.2f)] float strafeTangentVsTowardBlend = 0.22f;
    [Tooltip("After this many seconds in a strafe leg without firing, blend heading hard onto the player to break stationary orbit stalls.")]
    [SerializeField, Min(0f)] float strafeInboundCommitAfterSeconds = 3.5f;
    [SerializeField, Min(0.25f)] float strafeInboundCommitRampSeconds = 8f;
    [SerializeField, Min(2f)] float strafePassMaxDuration = 24f;
    [SerializeField, Min(0.5f)] float flyOffSeconds = 3.2f;
    [SerializeField, Min(30f)] float flyOffExitHorizontalDistance = 178f;
    [Tooltip("Desired offset for the rejoin point vs target (XZ). Clamped at runtime between min missile range and max so the next pass can actually fire.")]
    [SerializeField, Min(40f)] float repositionStandoffDistance = 82f;
    [SerializeField, Min(15f)] float repositionArriveDistance = 58f;
    [SerializeField, Min(5f)] float repositionTimeoutSeconds = 30f;

    [Header("Engagement (missiles)")]
    [SerializeField] bool missileRangeUsesHorizontalDistance = true;
    [SerializeField, Min(10f)] float missileAttackMaxDistance = 124f;
    [SerializeField, Min(1f)] float missileAttackMinDistance = 26f;
    [SerializeField, Min(1f)] float missileFirePreferredDistance = 78f;
    [SerializeField, Min(0f)] float missileFirePreferredSlack;
    [Tooltip("Requires target & aim in front of flight path (not abeam) before firing.")]
    [SerializeField] bool missileRequireFacingCone = true;
    [Tooltip("Planar dot(flight dir, jet→target). Higher = narrower frontal firing arc (must point more at target).")]
    [SerializeField, Range(0.2f, 0.95f)] float missileMinTravelToTargetDot = 0.82f;
    [Tooltip("Planar dot(flight dir, aim). Higher = flight path must match muzzle bearing more closely.")]
    [SerializeField, Range(0.15f, 0.95f)] float missileMinTravelToAimDot = 0.88f;
    [Tooltip("Max angle between flight dir and aim on XZ (tight cone for head-on volleys).")]
    [SerializeField, Range(12f, 90f)] float missileMaxTravelToAimDegrees = 16f;
    [Tooltip("0–1: in missile range, blend strafe heading toward target to set up a forward pass.")]
    [SerializeField, Range(0f, 1f)] float missileNoseAlignBlendInRange = 0.94f;
    [SerializeField] bool requireLineOfSight = true;
    [SerializeField, Min(0.05f)] float losRayPadding = 0.12f;
    [SerializeField] bool aimPredictionEnabled = true;
    [SerializeField, Min(0.05f)] float aimPredictionMaxLeadTime = 0.65f;
    [SerializeField, Range(0f, 1f)] float aimPredictionVerticalBlend = 0.45f;

    [Header("Resupply — landing pattern (runway = pad forward)")]
    [Tooltip("-1 = ammo only refills at the resupply zone. ≥ 0 = if no zone is assigned, refill after this many seconds while NeedsResupply (for testing / simple setups).")]
    [SerializeField, Min(-1f)] float resupplyWithoutPadSeconds = -1f;
    [SerializeField, Min(0.1f)] float resupplyDuration = 2.8f;
    [SerializeField, Min(5f)] float landingPatternDownwindDistance = 72f;
    [Tooltip("If true, computed approach hold (when no hold empty) is on +runway from touchdown; if false, −runway (typical downwind).")]
    [SerializeField] bool landingPatternApproachFromOppositeSide;
    [SerializeField, Min(3f)] float landingPatternHeightAboveTouchdown = 48f;
    [Tooltip("Glide slope angle (degrees) for final approach; mirrors takeoff climb.")]
    [SerializeField, Range(2f, 45f)] float landingGlideAngleDegrees = 12f;
    [Tooltip("Within this horizontal distance (m) of touchdown, altitude is clamped so the jet does not dive through the runway before wheels-down.")]
    [SerializeField, Min(4f)] float landingBeginFinalDescentHorizontal = 22f;
    [SerializeField, Min(2f)] float landingPatternCaptureRadius = 24f;
    [SerializeField, Min(2f)] float landingPatternVerticalTolerance = 14f;
    [SerializeField, Min(0.01f)] float landingApproachCruiseSpeed = 30f;
    [Tooltip("When returning to base in Cruise, switch to final glide after this close (XZ) to the hold point.")]
    [SerializeField, Min(8f)] float landingCruiseArriveRadius = 42f;
    [Tooltip("Turn-rate scale while flying ReturnToBase (1 = same as cruise).")]
    [SerializeField, Range(0.25f, 1f)] float returnToBaseTurnRateScale = 0.55f;
    [Tooltip("Glide slope when flying to approach hold (degrees from horizontal); avoids dropping straight down onto the hold.")]
    [SerializeField, Range(2f, 25f)] float returnToBaseGlideAngleDegrees = 11f;
    [Tooltip("After capturing the approach hold, steer along the runway axis (pad forward) toward touchdown instead of beelining in XZ — same heading as takeoff/spawn.")]
    [SerializeField] bool landingFinalHeadingAlongRunway = true;
    [Tooltip("When off the runway centerline (m), blend toward lateral closure so final approach recenters before wheels-down.")]
    [SerializeField, Min(2f)] float landingRunwayLateralBlendStartMeters = 5f;
    [SerializeField, Min(4f)] float landingRunwayLateralBlendFullMeters = 22f;

    [Header("Visual attitude (local pitch X, roll Z after yaw)")]
    [SerializeField] bool enableVisualPitchRoll = true;
    [SerializeField, Range(0f, 55f)] float maxBankDegrees = 44f;
    [SerializeField, Range(0f, 40f)] float maxPitchDegrees = 24f;
    [FormerlySerializedAs("bankPerYawDegreeTurned")]
    [Tooltip("Roll from turn rate: bank ≈ −(yaw °/s) × this. ~0.35–0.45 reads like a fighter.")]
    [SerializeField, Range(0.05f, 1.2f)] float bankFromTurnRateScale = 0.38f;
    [SerializeField, Range(0.1f, 2.5f)] float pitchFromClimbAngleScale = 0.85f;
    [SerializeField, Min(0.05f)] float bankSmoothSeconds = 0.28f;
    [Tooltip("Low-pass on vertical speed so pitch does not chase the player’s every jump.")]
    [SerializeField, Min(0.08f)] float attitudeVerticalSpeedSmoothTime = 0.55f;
    [SerializeField, Min(4f)] float attitudePitchMaxDegPerSecond = 22f;
    [SerializeField, Min(0.01f)] float landingFinalApproachMaxSpeed = 20f;
    [SerializeField, Min(0.01f)] float landingTouchdownSpeed = 11f;
    [SerializeField, Range(0.15f, 1f)] float landingRolloutSpeedFraction = 0.55f;
    [SerializeField, Min(1f)] float landingYawTurnRateDegrees = 38f;
    [Tooltip("Extra yaw smoothing during landing (seconds). Reduces snap when switching pattern → final.")]
    [SerializeField, Min(0f)] float landingYawSmoothTime = 0.1f;
    [SerializeField, Min(0f)] float landingHeadingSettleSeconds = 1.6f;

    Transform _target;
    float _health;
    JetPhase _phase;
    Vector3 _runwayForward;
    float _runwayDistanceAccumulated;
    float _currentSpeed;
    float _resupplyTimer;
    bool _landingCapturedPattern;
    bool _alive = true;

    /// <summary>Horizontal unit direction of travel (velocity on XZ). Never assigned abruptly — only integrated.</summary>
    Vector3 _horizontalTravelDir = Vector3.forward;

    StrikePhase _strikePhase = StrikePhase.StrafePass;
    Vector3 _strafeLegDesiredDir = Vector3.forward;
    Vector3 _repositionStagingWorld;
    float _strafeLegTimer;
    float _flyOffTimer;
    float _repositionTimer;
    int _passSide = 1;
    bool _hadTargetLastFrame;
    float _noPadResupplyTimer;
    float _alignYawVelocity;
    float _landingHeadingSettleTimer;
    CruiseIntent _cruiseIntent = CruiseIntent.Combat;

    /// <summary>Rotation with zero world yaw: preserves how the model sits on the runway (nose/canopy vs transform axes).</summary>
    Quaternion _parkedPitchRoll;

    float _prevPosY;
    float _verticalSpeed;
    float _lastWorldYawApplied;
    float _smoothedBank;
    float _smoothedPitch;
    float _bankVel;
    float _travelYawDeltaThisFrame;
    float _smoothedVerticalForPitch;
    float _pitchVerticalSmoothVel;

    public bool IsAlive => _alive && _health > 0f;
    public JetPhase Phase => _phase;

#if UNITY_EDITOR
    void OnValidate()
    {
        missileAttackMaxDistance = Mathf.Max(missileAttackMaxDistance, missileAttackMinDistance + 1f);
        missileFirePreferredDistance = Mathf.Clamp(missileFirePreferredDistance, missileAttackMinDistance + 0.5f, missileAttackMaxDistance - 0.5f);
        missileFirePreferredSlack = Mathf.Max(0f, missileFirePreferredSlack);
        repositionStandoffDistance = Mathf.Max(40f, repositionStandoffDistance);
        repositionStandoffDistance = Mathf.Min(repositionStandoffDistance, missileAttackMaxDistance * 0.92f);
        attackTooCloseDistance = Mathf.Min(attackTooCloseDistance, Mathf.Max(1f, missileAttackMinDistance - 0.5f));
        attackEngageOuterDistance = Mathf.Max(attackEngageOuterDistance, missileAttackMaxDistance + 2f);
        landingCruiseArriveRadius = Mathf.Max(8f, landingCruiseArriveRadius);
        landingRunwayLateralBlendFullMeters = Mathf.Max(landingRunwayLateralBlendStartMeters + 0.5f, landingRunwayLateralBlendFullMeters);
        repositionArriveDistance = Mathf.Min(repositionArriveDistance, missileAttackMaxDistance * 0.88f);
    }
#endif

    void Awake()
    {
        _health = maxHealth;
        _missiles = GetComponent<EnemyJetMissileBarrage>();

        Vector3 f = transform.forward;
        f.y = 0f;
        _runwayForward = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;

        if (spawnAlreadyAirborne || requiredRunwayDistance <= 0.01f)
        {
            _phase = JetPhase.Cruise;
            _currentSpeed = cruiseSpeed;
            _cruiseIntent = _missiles.NeedsResupply && runwayPad != null
                ? CruiseIntent.ReturnToBase
                : CruiseIntent.Combat;
        }
        else
        {
            _phase = JetPhase.RunwayRoll;
            _currentSpeed = groundRollSpeed;
        }

        ResetStrikePattern();
        InitHeadingFromTransform();
        CaptureParkedPitchRollBasis();
        _prevPosY = transform.position.y;
        _smoothedVerticalForPitch = 0f;
        _lastWorldYawApplied = Mathf.Atan2(transform.forward.x, transform.forward.z) * Mathf.Rad2Deg;
    }

    void Start()
    {
        if (_missiles != null)
            _missiles.ApplyDriverRefs(missileMuzzle, missileProjectilePrefab);
        ResolveTarget();
        InitHeadingFromTransform();
    }

    /// <summary>Called by <see cref="EnemyJetRunwayPad"/> after instantiating this prefab.</summary>
    public void BindRunwayPad(EnemyJetRunwayPad pad) => runwayPad = pad;

    /// <summary>Aligns runway heading and yaw after the root position is set (spawn).</summary>
    public void AfterSnapFromRunwayPad(EnemyJetRunwayPad pad)
    {
        if (pad == null)
            return;

        _runwayForward = pad.RunwayForwardPlanar;
        _horizontalTravelDir = _runwayForward;
        _runwayDistanceAccumulated = 0f;
        _cruiseIntent = CruiseIntent.Combat;
        float yawDeg = Mathf.Atan2(_runwayForward.x, _runwayForward.z) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(yawDeg, Vector3.up) * _parkedPitchRoll;
    }

    void CaptureParkedPitchRollBasis()
    {
        Vector3 flatFwd = transform.forward;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 1e-6f)
        {
            _parkedPitchRoll = transform.rotation;
            return;
        }

        flatFwd.Normalize();
        float yawDeg = Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;
        _parkedPitchRoll = Quaternion.Inverse(Quaternion.AngleAxis(yawDeg, Vector3.up)) * transform.rotation;
    }

    void InitHeadingFromTransform()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        _horizontalTravelDir = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
    }

    void ResetStrikePattern()
    {
        _strafeLegTimer = 0f;
        _flyOffTimer = 0f;
        _repositionTimer = 0f;
        _strikePhase = StrikePhase.StrafePass;
    }

    /// <summary>World Y to hold when cruising / fighting. Uses player-relative height if targeted, otherwise ground + patrol clearance.</summary>
    float CruiseHeightWorld
    {
        get
        {
            if (_target != null)
                return _target.position.y + cruiseAltitudeAboveTarget;
            if (groundLayers.value != 0
                && Physics.Raycast(transform.position + Vector3.up * 120f, Vector3.down, out RaycastHit gh, 500f, groundLayers, QueryTriggerInteraction.Ignore))
                return gh.point.y + idlePatrolHeightAboveGround;
            return transform.position.y + cruiseAltitudeAboveTarget;
        }
    }

    void Update()
    {
        if (!IsAlive)
            return;

        ResolveTarget();
        float dt = Time.deltaTime;
        _travelYawDeltaThisFrame = 0f;

        if (_target != null && !_hadTargetLastFrame && (_phase == JetPhase.Cruise || _phase == JetPhase.AirCombat))
            EnterStrikeCycleFromCold();

        _hadTargetLastFrame = _target != null;

        if (_target == null)
        {
            _noPadResupplyTimer = 0f;
            _cruiseIntent = CruiseIntent.Combat;
            IdlePatrolAir(dt);
            _missiles?.TickBarrage();
            if (dt > 1e-5f)
                _verticalSpeed = (transform.position.y - _prevPosY) / dt;
            _smoothedVerticalForPitch = Mathf.SmoothDamp(
                _smoothedVerticalForPitch,
                _verticalSpeed,
                ref _pitchVerticalSmoothVel,
                attitudeVerticalSpeedSmoothTime,
                999f,
                dt);
            _prevPosY = transform.position.y;
            return;
        }

        TickNoPadResupply(dt);

        if (_missiles != null && !_missiles.NeedsResupply && _cruiseIntent == CruiseIntent.ReturnToBase && _phase == JetPhase.Cruise)
            _cruiseIntent = CruiseIntent.Combat;

        if (_missiles != null && runwayPad != null && _missiles.NeedsResupply
            && !_missiles.BarrageInProgress
            && _phase != JetPhase.Resupplying
            && _phase != JetPhase.LandingApproach
            && _phase != JetPhase.RunwayRoll
            && _phase != JetPhase.TakeoffClimb)
        {
            _cruiseIntent = CruiseIntent.ReturnToBase;
            if (_phase == JetPhase.AirCombat)
                ResetStrikePattern();
            _phase = JetPhase.Cruise;
            _landingCapturedPattern = false;
            _landingHeadingSettleTimer = 0f;
            _alignYawVelocity = 0f;
        }

        switch (_phase)
        {
            case JetPhase.RunwayRoll:
                TickRunwayRoll(dt);
                break;
            case JetPhase.TakeoffClimb:
                TickTakeoffClimb(dt);
                break;
            case JetPhase.Cruise:
                TickCruise(dt);
                break;
            case JetPhase.AirCombat:
                TickAirCombat(dt);
                break;
            case JetPhase.LandingApproach:
                TickLandingApproach(dt);
                break;
            case JetPhase.Resupplying:
                TickResupplying(dt);
                break;
        }

        _missiles?.TickBarrage();

        if (dt > 1e-5f)
            _verticalSpeed = (transform.position.y - _prevPosY) / dt;
        _smoothedVerticalForPitch = Mathf.SmoothDamp(
            _smoothedVerticalForPitch,
            _verticalSpeed,
            ref _pitchVerticalSmoothVel,
            attitudeVerticalSpeedSmoothTime,
            999f,
            dt);
        _prevPosY = transform.position.y;
    }

    void TickNoPadResupply(float dt)
    {
        if (resupplyWithoutPadSeconds < 0f || runwayPad != null || _missiles == null || !_missiles.NeedsResupply)
        {
            _noPadResupplyTimer = 0f;
            return;
        }

        _noPadResupplyTimer += dt;
        if (_noPadResupplyTimer >= resupplyWithoutPadSeconds)
        {
            _missiles.RefillAmmo();
            _noPadResupplyTimer = 0f;
            _cruiseIntent = CruiseIntent.Combat;
        }
    }

    void EnterStrikeCycleFromCold()
    {
        if (_cruiseIntent != CruiseIntent.Combat)
            return;

        ResetStrikePattern();
        if (HorizontalDistanceTo(transform.position, _target.position) <= attackEngageOuterDistance)
        {
            _phase = JetPhase.AirCombat;
            BeginStrafePass();
        }
    }

    void IdlePatrolAir(float dt)
    {
        float targetSp = cruiseSpeed * idlePatrolSpeedFraction;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSp, speedBlendTowardTarget * dt);

        Vector3 f = transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-4f)
            f = Vector3.forward;
        f.Normalize();

        float yawCap = EffectiveYawRateDegrees(cruiseYawTurnRateDegrees);
        IntegrateHorizontalTravelToward(f, yawCap, dt);

        float minY = transform.position.y;
        if (groundLayers.value != 0
            && Physics.Raycast(transform.position + Vector3.up * 80f, Vector3.down, out RaycastHit gh, 400f, groundLayers, QueryTriggerInteraction.Ignore))
            minY = Mathf.Max(minY, gh.point.y + idlePatrolHeightAboveGround);

        ApplyHorizontalMoveAndAltitude(dt, minY, cruiseYawTurnRateDegrees);
    }

    /// <summary>Turn-rate–limited blend toward a desired horizontal direction (unit XZ).</summary>
    void IntegrateHorizontalTravelToward(Vector3 desiredPlanarUnit, float maxYawDegreesPerSec, float dt)
    {
        desiredPlanarUnit.y = 0f;
        if (desiredPlanarUnit.sqrMagnitude < 1e-6f)
            return;
        desiredPlanarUnit.Normalize();

        _horizontalTravelDir.y = 0f;
        if (_horizontalTravelDir.sqrMagnitude < 1e-6f)
            _horizontalTravelDir = desiredPlanarUnit;
        else
            _horizontalTravelDir.Normalize();

        float yawBefore = Mathf.Atan2(_horizontalTravelDir.x, _horizontalTravelDir.z) * Mathf.Rad2Deg;

        float maxRad = Mathf.Deg2Rad * maxYawDegreesPerSec * dt;
        _horizontalTravelDir = Vector3.RotateTowards(_horizontalTravelDir, desiredPlanarUnit, maxRad, 0f);
        if (_horizontalTravelDir.sqrMagnitude < 1e-6f)
            _horizontalTravelDir = desiredPlanarUnit;

        float yawAfter = Mathf.Atan2(_horizontalTravelDir.x, _horizontalTravelDir.z) * Mathf.Rad2Deg;
        _travelYawDeltaThisFrame += Mathf.DeltaAngle(yawBefore, yawAfter);
    }

    float EffectiveYawRateDegrees(float configuredDegPerSec)
    {
        if (_currentSpeed < 0.5f)
            return configuredDegPerSec;
        float capFromRadius = (_currentSpeed / Mathf.Max(minHorizontalTurnRadius, 1f)) * Mathf.Rad2Deg;
        return Mathf.Min(configuredDegPerSec, capFromRadius);
    }

    void ApplyHorizontalMoveAndAltitude(float dt, float targetHeightWorld, float yawTurnRateDegrees)
    {
        Vector3 p = transform.position;
        Vector3 h = _horizontalTravelDir;
        h.y = 0f;
        if (h.sqrMagnitude > 1e-6f)
            h.Normalize();
        else
            h = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;

        p += h * (_currentSpeed * dt);
        p.y = Mathf.MoveTowards(p.y, targetHeightWorld, altitudeChangeSpeed * dt);
        transform.position = p;

        AlignGroundLevel(h, dt, yawTurnRateDegrees, 0f);
    }

    void TickRunwayRoll(float dt)
    {
        StickToGround();

        float targetSp = groundRollSpeed;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSp, speedBlendTowardTarget * dt);

        Vector3 steer = _runwayForward;
        Vector3 delta = steer * (_currentSpeed * dt);
        transform.position += delta;
        _runwayDistanceAccumulated += Vector3.Dot(new Vector3(delta.x, 0f, delta.z), _runwayForward);

        IntegrateHorizontalTravelToward(steer, groundHeadingTurnRateDegrees * 1.25f, dt);
        AlignGroundLevel(steer, dt);

        if (_runwayDistanceAccumulated >= requiredRunwayDistance)
            _phase = JetPhase.TakeoffClimb;
    }

    void TickTakeoffClimb(float dt)
    {
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, takeoffClimbSpeed, speedBlendTowardTarget * dt);

        float cruiseY = CruiseHeightWorld;
        float climbRad = takeoffClimbAngleDegrees * Mathf.Deg2Rad;
        Vector3 h = _runwayForward;
        h.y = 0f;
        if (h.sqrMagnitude < 1e-4f)
            h = Vector3.forward;
        h.Normalize();

        if (IsGroundedForTakeoff())
        {
            Vector3 climbDir = (h * Mathf.Cos(climbRad) + Vector3.up * Mathf.Sin(climbRad)).normalized;
            transform.position += climbDir * (_currentSpeed * dt);
            IntegrateHorizontalTravelToward(h, combatYawTurnRateDegrees, dt);
            AlignGroundLevel(h, dt, groundHeadingTurnRateDegrees * 1.1f);
            EnsureNotBelowRunwaySurface();
        }
        else
        {
            IntegrateHorizontalTravelToward(h, cruiseYawTurnRateDegrees, dt);

            Vector3 p = transform.position;
            p += _horizontalTravelDir * (_currentSpeed * Mathf.Cos(climbRad) * dt);
            p.y += _currentSpeed * Mathf.Sin(climbRad) * dt;
            transform.position = p;

            if (p.y >= cruiseY - 0.5f)
            {
                _phase = JetPhase.Cruise;
                ResetStrikePattern();
                _cruiseIntent = _missiles != null && _missiles.NeedsResupply
                    ? CruiseIntent.ReturnToBase
                    : CruiseIntent.Combat;
                return;
            }

            AlignGroundLevel(_horizontalTravelDir, dt, cruiseYawTurnRateDegrees);
        }
    }

    bool IsGroundedForTakeoff()
    {
        if (groundLayers.value == 0)
            return false;
        return Physics.Raycast(
            transform.position + Vector3.up * 0.25f,
            Vector3.down,
            takeoffUngroundRayDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore);
    }

    void EnsureNotBelowRunwaySurface()
    {
        if (groundLayers.value == 0)
            return;
        Vector3 o = transform.position + Vector3.up * 1.2f;
        if (!Physics.Raycast(o, Vector3.down, out RaycastHit hit, groundStickRayLength + 6f, groundLayers, QueryTriggerInteraction.Ignore))
            return;
        float minY = hit.point.y + groundClearance;
        if (transform.position.y >= minY)
            return;
        Vector3 p = transform.position;
        p.y = minY;
        transform.position = p;
    }

    void TickCruise(float dt)
    {
        if (_target == null)
            return;

        if (_cruiseIntent == CruiseIntent.ReturnToBase && runwayPad != null)
        {
            TickCruiseReturnToBase(dt);
            return;
        }

        _currentSpeed = Mathf.MoveTowards(_currentSpeed, cruiseSpeed, speedBlendTowardTarget * dt);
        float cruiseY = CruiseHeightWorld;
        float hDist = HorizontalDistanceTo(transform.position, _target.position);

        if (hDist <= attackEngageOuterDistance)
        {
            _phase = JetPhase.AirCombat;
            BeginStrafePass();
            return;
        }

        Vector3 toT = _target.position - transform.position;
        toT.y = 0f;
        Vector3 desired = toT.sqrMagnitude > 1e-4f ? toT.normalized : _horizontalTravelDir;

        if (hDist < attackTooCloseDistance)
        {
            Vector3 radialOut = transform.position - _target.position;
            radialOut.y = 0f;
            if (radialOut.sqrMagnitude < 1e-4f)
                radialOut = _horizontalTravelDir;
            radialOut.Normalize();
            Vector3 tang = Vector3.Cross(Vector3.up, radialOut).normalized * (_passSide >= 0 ? 1f : -1f);
            desired = (radialOut * 0.88f + tang * 0.12f).normalized;
        }

        float yawCap = EffectiveYawRateDegrees(cruiseYawTurnRateDegrees);
        IntegrateHorizontalTravelToward(desired, yawCap, dt);
        ApplyHorizontalMoveAndAltitude(dt, cruiseY, cruiseYawTurnRateDegrees);
    }

    void TickCruiseReturnToBase(float dt)
    {
        if (!runwayPad.TryGetTouchdownOnGround(groundLayers, out Vector3 touchdown))
            touchdown = runwayPad.WorldCenter;

        Vector3 runwayFwd = runwayPad.RunwayForwardPlanar;
        _runwayForward = runwayFwd;

        TryComputeLandingHoldWorld(touchdown, runwayFwd, out Vector3 holdWorld);

        _currentSpeed = Mathf.MoveTowards(_currentSpeed, landingApproachCruiseSpeed, speedBlendTowardTarget * dt);

        Vector3 toHold = holdWorld - transform.position;
        toHold.y = 0f;
        Vector3 desired = toHold.sqrMagnitude > 1e-4f ? toHold.normalized : runwayFwd;

        float yawCap = EffectiveYawRateDegrees(cruiseYawTurnRateDegrees * returnToBaseTurnRateScale);
        IntegrateHorizontalTravelToward(desired, yawCap, dt);

        Vector3 p = transform.position;
        Vector3 h = _horizontalTravelDir;
        h.y = 0f;
        if (h.sqrMagnitude > 1e-6f)
            h.Normalize();
        else
            h = new Vector3(runwayFwd.x, 0f, runwayFwd.z).normalized;

        float glideRad = Mathf.Clamp(returnToBaseGlideAngleDegrees, 2f, 25f) * Mathf.Deg2Rad;
        float cosG = Mathf.Cos(glideRad);
        float sinG = Mathf.Sin(glideRad);
        float horizSpeed = _currentSpeed * cosG;
        float hStep = horizSpeed * dt;
        p += h * hStep;

        float targetY = holdWorld.y;
        float distH = HorizontalDistanceTo(p, holdWorld);
        float altErr = targetY - p.y;
        float maxSink = _currentSpeed * sinG;
        float maxClimb = Mathf.Min(altitudeChangeSpeed, maxSink * 1.35f);

        // Intercept hold altitude using time-to-close on XZ — avoids freezing Y when |altErr| is tiny but we are still offset horizontally.
        if (Mathf.Abs(altErr) > 0.06f)
        {
            float closeTime = Mathf.Max(distH / Mathf.Max(horizSpeed, 0.22f), 0.4f);
            float vy = Mathf.Clamp(altErr / closeTime, -maxSink, maxClimb);
            p.y += vy * dt;
        }

        transform.position = p;

        AlignGroundLevel(h, dt, cruiseYawTurnRateDegrees * returnToBaseTurnRateScale);

        distH = HorizontalDistanceTo(transform.position, holdWorld);
        if (distH <= landingCruiseArriveRadius && Mathf.Abs(transform.position.y - targetY) <= landingPatternVerticalTolerance)
        {
            _phase = JetPhase.LandingApproach;
            _landingCapturedPattern = false;
            _landingHeadingSettleTimer = 0f;
            _runwayForward = runwayFwd;
        }
    }

    bool TryComputeLandingHoldWorld(Vector3 touchdown, Vector3 runwayFwd, out Vector3 holdWorld)
    {
        if (runwayPad.TryGetApproachHoldWorld(groundLayers, landingPatternHeightAboveTouchdown, out holdWorld))
            return true;

        float patternSign = landingPatternApproachFromOppositeSide ? 1f : -1f;
        holdWorld = touchdown + runwayFwd * (landingPatternDownwindDistance * patternSign)
            + Vector3.up * landingPatternHeightAboveTouchdown;
        return true;
    }

    void TickAirCombat(float dt)
    {
        if (_target == null)
            return;

        float cruiseY = CruiseHeightWorld;

        // Fly-off / rejoin must run even when horizontal distance exceeds missile max, or this branch was
        // previously taken every frame and the strike cycle never completed (no repeat volleys).
        if (_strikePhase == StrikePhase.FlyOff)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, attackPassSpeed, speedBlendTowardTarget * dt);
            TickFlyOff(dt, cruiseY);
            return;
        }

        if (_strikePhase == StrikePhase.Reposition)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, attackPassSpeed, speedBlendTowardTarget * dt);
            TickReposition(dt, cruiseY);
            return;
        }

        TickStrafePass(dt, cruiseY);
    }

    void BeginStrafePass()
    {
        if (_target == null)
            return;

        _strikePhase = StrikePhase.StrafePass;
        _strafeLegTimer = 0f;

        Vector3 p = _target.position;
        Vector3 radial = transform.position - p;
        radial.y = 0f;
        if (radial.sqrMagnitude < 1e-4f)
            radial = Quaternion.AngleAxis(30f * (_passSide >= 0 ? 1f : -1f), Vector3.up) * _runwayForward;
        radial.Normalize();

        Vector3 tangent = Vector3.Cross(Vector3.up, radial);
        if (_passSide < 0)
            tangent = -tangent;
        tangent.Normalize();

        Vector3 toward = -radial;
        float w = Mathf.Clamp01(strafeTangentVsTowardBlend);
        _strafeLegDesiredDir = (tangent * w + toward * (1f - w)).normalized;
    }

    void TickStrafePass(float dt, float cruiseY)
    {
        _strafeLegTimer += dt;

        float hDist = HorizontalDistanceTo(transform.position, _target.position);
        Vector3 desired = _strafeLegDesiredDir;

        // Never skip this tick: if we're too close for missiles or inside the "too close" ring, widen while
        // still advancing the strafe timer and calling TryFireMissiles (OpenRange used to return early and
        // blocked all further volleys after the first rejoin).
        if (hDist < missileAttackMinDistance || hDist < attackTooCloseDistance)
        {
            Vector3 radialOut = transform.position - _target.position;
            radialOut.y = 0f;
            if (radialOut.sqrMagnitude < 1e-4f)
                radialOut = _horizontalTravelDir;
            radialOut.Normalize();
            Vector3 tang = Vector3.Cross(Vector3.up, radialOut).normalized * (_passSide >= 0 ? 1f : -1f);
            desired = (radialOut * 0.82f + tang * 0.18f).normalized;
        }
        else if (hDist > missileAttackMaxDistance * 0.92f)
        {
            Vector3 toT = _target.position - transform.position;
            toT.y = 0f;
            if (toT.sqrMagnitude > 1e-4f)
            {
                toT.Normalize();
                float t = Mathf.InverseLerp(missileAttackMaxDistance * 0.92f, missileAttackMaxDistance * 1.6f, hDist);
                desired = Vector3.Slerp(desired, toT, Mathf.Clamp01(t * 0.75f + 0.2f)).normalized;
            }
        }
        else if (hDist >= missileAttackMinDistance && hDist <= missileAttackMaxDistance && missileNoseAlignBlendInRange > 0.01f)
        {
            Vector3 toT = _target.position - transform.position;
            toT.y = 0f;
            if (toT.sqrMagnitude > 1e-4f)
            {
                toT.Normalize();
                float blend = missileNoseAlignBlendInRange;
                if (missileRequireFacingCone)
                {
                    Vector3 aimProbe = GetMissileAimDirection();
                    if (!PassesMissileFacingCone(aimProbe))
                        blend = Mathf.Min(1f, blend + 0.42f);
                }
                desired = Vector3.Slerp(desired, toT, blend).normalized;
            }
        }

        if (strafeInboundCommitAfterSeconds > 0.01f
            && _strafeLegTimer >= strafeInboundCommitAfterSeconds
            && hDist <= missileAttackMaxDistance * 1.08f)
        {
            Vector3 toPlayer = _target.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 1e-4f)
            {
                toPlayer.Normalize();
                float u = Mathf.Clamp01(
                    (_strafeLegTimer - strafeInboundCommitAfterSeconds)
                    / Mathf.Max(0.25f, strafeInboundCommitRampSeconds));
                u = u * u * (3f - 2f * u);
                desired = Vector3.Slerp(desired, toPlayer, u * 0.94f).normalized;
            }
        }

        float yawCap = EffectiveYawRateDegrees(combatYawTurnRateDegrees);
        IntegrateHorizontalTravelToward(desired, yawCap, dt);

        float speedTarget = hDist < missileAttackMinDistance ? cruiseSpeed : attackPassSpeed;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, speedTarget, speedBlendTowardTarget * dt);

        ApplyHorizontalMoveAndAltitude(dt, cruiseY, combatYawTurnRateDegrees);

        if (TryFireMissiles())
        {
            BeginFlyOff();
            return;
        }

        if (_strafeLegTimer >= strafePassMaxDuration)
            BeginFlyOff();
    }

    void BeginFlyOff()
    {
        if (_target == null)
            return;

        _strikePhase = StrikePhase.FlyOff;
        _flyOffTimer = flyOffSeconds;
        _passSide *= -1;
    }

    void TickFlyOff(float dt, float cruiseY)
    {
        _flyOffTimer -= dt;

        Vector3 away = transform.position - _target.position;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-4f)
            away = _horizontalTravelDir;
        away.Normalize();

        float yawCap = EffectiveYawRateDegrees(combatYawTurnRateDegrees);
        IntegrateHorizontalTravelToward(away, yawCap, dt);
        ApplyHorizontalMoveAndAltitude(dt, cruiseY, combatYawTurnRateDegrees);

        float h = HorizontalDistanceTo(transform.position, _target.position);
        if (_flyOffTimer <= 0f || h >= flyOffExitHorizontalDistance)
            BeginReposition(cruiseY);
    }

    void BeginReposition(float cruiseY)
    {
        if (_target == null)
            return;

        _strikePhase = StrikePhase.Reposition;
        _repositionTimer = 0f;

        Vector3 p = _target.position;
        Vector3 outbound = transform.position - p;
        outbound.y = 0f;
        if (outbound.sqrMagnitude < 1e-4f)
            outbound = _horizontalTravelDir.sqrMagnitude > 1e-6f ? _horizontalTravelDir : transform.forward;
        outbound.y = 0f;
        outbound.Normalize();

        float sideAng = 50f * (_passSide >= 0 ? 1f : -1f) + Random.Range(-22f, 22f);
        Vector3 dir = Quaternion.AngleAxis(sideAng, Vector3.up) * outbound;

        float ring = RepositionRingRadius();
        _repositionStagingWorld = new Vector3(
            p.x + dir.x * ring,
            cruiseY,
            p.z + dir.z * ring);
    }

    void TickReposition(float dt, float cruiseY)
    {
        _repositionTimer += dt;
        _repositionStagingWorld.y = cruiseY;

        Vector3 to = _repositionStagingWorld - transform.position;
        to.y = 0f;
        Vector3 desired = to.sqrMagnitude > 1e-4f ? to.normalized : _horizontalTravelDir;

        float yawCap = EffectiveYawRateDegrees(combatYawTurnRateDegrees);
        IntegrateHorizontalTravelToward(desired, yawCap, dt);
        ApplyHorizontalMoveAndAltitude(dt, cruiseY, combatYawTurnRateDegrees);

        float h = HorizontalDistanceTo(transform.position, _repositionStagingWorld);
        float toPlayerH = HorizontalDistanceTo(transform.position, _target.position);
        float inMissileWindow = missileAttackMaxDistance * 0.9f;
        bool atStaging = h <= repositionArriveDistance;
        bool timedOut = _repositionTimer >= repositionTimeoutSeconds;
        // Starting strafe far outside missile range burns strafePassMaxDuration on geometry and reads as “lines up then breaks off”.
        if (timedOut || (atStaging && toPlayerH <= inMissileWindow))
            BeginStrafePass();
    }

    static float HorizontalDistanceTo(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>
    /// Planar steering for final approach: primary motion along ±runway toward touchdown, with a lateral blend
    /// to recenter on the strip (avoids landing sideways across the runway).
    /// </summary>
    static Vector3 RunwayAlignedFinalApproachDir(
        Vector3 jetWorld,
        Vector3 touchdownWorld,
        Vector3 runwayFwdPlanar,
        float lateralBlendStartMeters,
        float lateralBlendFullMeters)
    {
        Vector3 rw = runwayFwdPlanar;
        rw.y = 0f;
        if (rw.sqrMagnitude < 1e-6f)
            rw = Vector3.forward;
        rw.Normalize();

        Vector3 delta = jetWorld - touchdownWorld;
        delta.y = 0f;
        float along = Vector3.Dot(delta, rw);
        Vector3 lateral = delta - rw * along;
        lateral.y = 0f;
        float latMag = lateral.magnitude;
        Vector3 latDir = latMag > 1e-4f ? lateral.normalized : Vector3.zero;

        const float alongDeadMeters = 2.5f;
        Vector3 alongCourse;
        if (along > alongDeadMeters)
            alongCourse = -rw;
        else if (along < -alongDeadMeters)
            alongCourse = rw;
        else
        {
            if (latMag > 4f)
                alongCourse = -latDir;
            else if (along >= 0f)
                alongCourse = -rw;
            else
                alongCourse = rw;
        }

        float span = Mathf.Max(0.01f, lateralBlendFullMeters - lateralBlendStartMeters);
        float w = Mathf.Clamp01((latMag - lateralBlendStartMeters) / span);
        w = w * w * (3f - 2f * w);
        Vector3 towardCenterline = latMag > 1e-4f ? -latDir : alongCourse;
        return Vector3.Slerp(alongCourse, towardCenterline, w).normalized;
    }

    /// <summary>Staging radius from target: inside missile envelope so the strafe leg is not aborted as “too far” before we can close.</summary>
    float RepositionRingRadius()
    {
        float inner = missileAttackMinDistance * 1.25f;
        float outer = missileAttackMaxDistance * 0.9f;
        return Mathf.Clamp(repositionStandoffDistance, inner, outer);
    }

    void AlignGroundLevel(Vector3 planarForward, float dt) =>
        AlignGroundLevel(planarForward, dt, groundHeadingTurnRateDegrees, 0f);

    void AlignGroundLevel(Vector3 planarForward, float dt, float yawTurnRateDegrees) =>
        AlignGroundLevel(planarForward, dt, yawTurnRateDegrees, 0f);

    void AlignGroundLevel(Vector3 planarForward, float dt, float yawTurnRateDegrees, float yawSmoothTime)
    {
        planarForward.y = 0f;
        if (planarForward.sqrMagnitude < 1e-4f)
            return;
        planarForward.Normalize();

        float targetYaw = Mathf.Atan2(planarForward.x, planarForward.z) * Mathf.Rad2Deg;

        Vector3 curFlat = PlanarAttackReference();
        float currentYaw = Mathf.Atan2(curFlat.x, curFlat.z) * Mathf.Rad2Deg;

        float newYaw;
        if (yawSmoothTime > 1e-4f)
            newYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref _alignYawVelocity, yawSmoothTime, yawTurnRateDegrees, dt);
        else
        {
            _alignYawVelocity = 0f;
            newYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, yawTurnRateDegrees * dt);
        }

        ApplyYawWithPitchRoll(newYaw, dt);
    }

    /// <summary>World yaw, then local roll about forward (Z) and pitch about right (X) — matches jet root Z=fwd, Y=up, X=right.</summary>
    void ApplyYawWithPitchRoll(float worldYawDeg, float dt)
    {
        if (!enableVisualPitchRoll || ShouldFlattenAttitude())
        {
            transform.rotation = Quaternion.AngleAxis(worldYawDeg, Vector3.up) * _parkedPitchRoll;
            _smoothedBank = Mathf.MoveTowards(_smoothedBank, 0f, 120f * dt);
            _smoothedPitch = Mathf.MoveTowards(_smoothedPitch, 0f, 120f * dt);
            _lastWorldYawApplied = worldYawDeg;
            return;
        }

        float turnRateDegPerSec = _travelYawDeltaThisFrame / Mathf.Max(dt, 1e-5f);
        float targetBank = Mathf.Clamp(-turnRateDegPerSec * bankFromTurnRateScale, -maxBankDegrees, maxBankDegrees);
        _smoothedBank = Mathf.SmoothDampAngle(_smoothedBank, targetBank, ref _bankVel, bankSmoothSeconds, 720f, dt);

        float horizSp = Mathf.Max(0.8f, _currentSpeed);
        float climbDeg = Mathf.Atan2(_smoothedVerticalForPitch, horizSp) * Mathf.Rad2Deg;
        float targetPitch = Mathf.Clamp(-climbDeg * pitchFromClimbAngleScale, -maxPitchDegrees, maxPitchDegrees);
        _smoothedPitch = Mathf.MoveTowardsAngle(_smoothedPitch, targetPitch, attitudePitchMaxDegPerSecond * dt);

        Quaternion qYaw = Quaternion.AngleAxis(worldYawDeg, Vector3.up);
        Vector3 flatFwd = qYaw * Vector3.forward;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 1e-6f)
            flatFwd = Vector3.forward;
        flatFwd.Normalize();
        Vector3 flatRight = Vector3.Cross(Vector3.up, flatFwd).normalized;

        Quaternion qRoll = Quaternion.AngleAxis(_smoothedBank, flatFwd);
        Quaternion qPitch = Quaternion.AngleAxis(_smoothedPitch, flatRight);
        transform.rotation = qYaw * qRoll * qPitch * _parkedPitchRoll;
        _lastWorldYawApplied = worldYawDeg;
    }

    bool ShouldFlattenAttitude()
    {
        if (_phase == JetPhase.RunwayRoll || _phase == JetPhase.Resupplying)
            return true;
        if (_phase == JetPhase.LandingApproach && IsGrounded())
            return true;
        if (_phase == JetPhase.TakeoffClimb && IsGroundedForTakeoff())
            return true;
        return false;
    }

    void TickLandingApproach(float dt)
    {
        if (runwayPad == null)
        {
            _phase = JetPhase.Cruise;
            ResetStrikePattern();
            return;
        }

        if (!runwayPad.TryGetTouchdownOnGround(groundLayers, out Vector3 touchdown))
            touchdown = runwayPad.WorldCenter;

        Vector3 runwayFwd = runwayPad.RunwayForwardPlanar;
        _runwayForward = runwayFwd;

        if (IsGrounded())
        {
            _alignYawVelocity = 0f;
            if (runwayPad.IsWithinRunwayFootprintXZ(transform.position))
            {
                _phase = JetPhase.Resupplying;
                _resupplyTimer = 0f;
                _currentSpeed = 0f;
                return;
            }

            TickRunwayTaxiToResupply(dt, runwayFwd);
            return;
        }

        TryComputeLandingHoldWorld(touchdown, runwayFwd, out Vector3 pattern);

        Vector3 deltaPattern = pattern - transform.position;
        Vector2 horFromPattern = new Vector2(deltaPattern.x, deltaPattern.z);
        if (!_landingCapturedPattern
            && horFromPattern.magnitude < landingPatternCaptureRadius
            && Mathf.Abs(deltaPattern.y) < landingPatternVerticalTolerance)
        {
            _landingCapturedPattern = true;
            _landingHeadingSettleTimer = 0f;
            _alignYawVelocity = 0f;
        }

        float altAboveRunway = Mathf.Max(0f, transform.position.y - touchdown.y);

        float landYawRate = landingYawTurnRateDegrees;
        if (_landingCapturedPattern && _landingHeadingSettleTimer < landingHeadingSettleSeconds)
        {
            _landingHeadingSettleTimer += dt;
            landYawRate *= Mathf.Lerp(0.32f, 1f, Mathf.Clamp01(_landingHeadingSettleTimer / landingHeadingSettleSeconds));
        }

        float targetSp;
        if (!_landingCapturedPattern)
            targetSp = landingApproachCruiseSpeed;
        else if (altAboveRunway > 22f)
            targetSp = landingFinalApproachMaxSpeed;
        else if (altAboveRunway > 6f)
            targetSp = Mathf.Lerp(landingTouchdownSpeed, landingFinalApproachMaxSpeed, (altAboveRunway - 6f) / 16f);
        else
            targetSp = landingTouchdownSpeed;

        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSp, speedBlendTowardTarget * dt);

        float glideRad = Mathf.Clamp(landingGlideAngleDegrees, 2f, 45f) * Mathf.Deg2Rad;
        float cosG = Mathf.Cos(glideRad);
        float sinG = Mathf.Sin(glideRad);

        // Air path: mirror takeoff — fixed glide angle γ. To the hold we beeline; after pattern capture, follow the runway axis
        // toward touchdown (same orientation as rollout/takeoff) instead of a direct XZ cut across the strip.
        Vector3 hDir;
        if (landingFinalHeadingAlongRunway && _landingCapturedPattern)
        {
            hDir = RunwayAlignedFinalApproachDir(
                transform.position,
                touchdown,
                runwayFwd,
                landingRunwayLateralBlendStartMeters,
                landingRunwayLateralBlendFullMeters);
        }
        else
        {
            Vector3 aim = !_landingCapturedPattern ? pattern : touchdown + Vector3.up * groundClearance;
            Vector3 toAimFlat = aim - transform.position;
            toAimFlat.y = 0f;
            hDir = toAimFlat.sqrMagnitude > 1e-4f
                ? toAimFlat.normalized
                : new Vector3(runwayFwd.x, 0f, runwayFwd.z).normalized;
        }

        IntegrateHorizontalTravelToward(hDir, landYawRate, dt);

        Vector3 p = transform.position;
        Vector3 h = _horizontalTravelDir;
        h.y = 0f;
        if (h.sqrMagnitude < 1e-6f)
            h = hDir;
        else
            h.Normalize();

        float hStep = _currentSpeed * cosG * dt;
        float vSink = _currentSpeed * sinG * dt;
        p += h * hStep;
        p.y -= vSink;

        if (!_landingCapturedPattern && p.y < pattern.y - 0.2f)
            p.y += Mathf.Min(altitudeChangeSpeed * dt, pattern.y - p.y);

        float hDistTd = HorizontalDistanceTo(p, touchdown);
        if (hDistTd < landingBeginFinalDescentHorizontal)
            p.y = Mathf.Max(p.y, touchdown.y + groundClearance * 0.85f);

        transform.position = p;

        AlignGroundLevel(h, dt, landYawRate, landingYawSmoothTime);
    }

    void TickRunwayTaxiToResupply(float dt, Vector3 runwayFwd)
    {
        StickToGround();
        float targetSp = groundRollSpeed * landingRolloutSpeedFraction;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSp, speedBlendTowardTarget * dt);

        Vector3 rw = runwayFwd;
        rw.y = 0f;
        if (rw.sqrMagnitude < 1e-6f)
            rw = Vector3.forward;
        rw.Normalize();

        Vector3 pos = transform.position;
        Vector3 center = runwayPad.TaxiStopXZ;
        Vector3 toC = new Vector3(center.x - pos.x, 0f, center.z - pos.z);
        float distStop = toC.magnitude;

        // Drive toward spawn / taxi stop on XZ (±runway-only steering never reached offset pads).
        Vector3 desired = distStop > 0.12f
            ? new Vector3(toC.x / distStop, 0f, toC.z / distStop)
            : rw;

        IntegrateHorizontalTravelToward(desired, groundHeadingTurnRateDegrees, dt);
        transform.position += _horizontalTravelDir * (_currentSpeed * dt);
        AlignGroundLevel(_horizontalTravelDir, dt, groundHeadingTurnRateDegrees);
        StickToGround();
    }

    void TickResupplying(float dt)
    {
        if (runwayPad != null)
            _runwayForward = runwayPad.RunwayForwardPlanar;

        _currentSpeed = 0f;
        StickToGround();
        AlignGroundLevel(_runwayForward, dt);

        _resupplyTimer += dt;
        if (_resupplyTimer >= resupplyDuration)
        {
            _missiles?.RefillAmmo();
            _runwayDistanceAccumulated = 0f;
            _resupplyTimer = 0f;
            _landingCapturedPattern = false;
            _cruiseIntent = CruiseIntent.Combat;
            _phase = requiredRunwayDistance > 0.01f ? JetPhase.RunwayRoll : JetPhase.TakeoffClimb;
            _currentSpeed = requiredRunwayDistance > 0.01f ? groundRollSpeed : takeoffClimbSpeed;
            ResetStrikePattern();
        }
    }

    void StickToGround()
    {
        Vector3 o = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(o, Vector3.down, out RaycastHit hit, groundStickRayLength, groundLayers, QueryTriggerInteraction.Ignore))
        {
            Vector3 p = transform.position;
            p.y = hit.point.y + groundClearance;
            transform.position = p;
        }
    }

    bool IsGrounded()
    {
        if (groundLayers.value == 0)
            return false;
        return Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, groundStickRayLength * 0.5f, groundLayers, QueryTriggerInteraction.Ignore);
    }

    bool TryFireMissiles()
    {
        if (_missiles == null || _target == null)
            return false;
        if (_missiles.BarrageInProgress)
            return false;
        if (_missiles.NeedsResupply)
            return false;

        float dist = missileRangeUsesHorizontalDistance
            ? HorizontalDistanceTo(transform.position, _target.position)
            : Vector3.Distance(transform.position, _target.position);
        if (dist > missileAttackMaxDistance || dist < missileAttackMinDistance)
            return false;

        if (missileFirePreferredSlack > 0.01f
            && Mathf.Abs(dist - missileFirePreferredDistance) > missileFirePreferredSlack)
            return false;

        Vector3 aim = GetMissileAimDirection();

        if (missileRequireFacingCone && !PassesMissileFacingCone(aim))
            return false;

        if (requireLineOfSight && !HasMissileLineOfFire(aim))
            return false;

        return _missiles.TryBeginBarrage(aim, _target);
    }

    /// <summary>Planar direction the jet is actually flying (authoritative for “in front” even if mesh/import axes differ).</summary>
    Vector3 PlanarAttackReference()
    {
        Vector3 h = _horizontalTravelDir;
        h.y = 0f;
        if (h.sqrMagnitude > 1e-6f)
            return h.normalized;

        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
    }

    /// <summary>
    /// Planar cone between <b>flight direction</b> and <b>aim</b> (same as muzzle→predicted on XZ).
    /// Pure orbit strafe is ~90° between those vectors; a tight cone + dot&gt;0 blocks all follow-up volleys.
    /// </summary>
    bool PassesMissileFacingCone(Vector3 aimWorldDir)
    {
        if (_target == null)
            return false;

        Vector3 flatTravel = PlanarAttackReference();

        Vector3 toT = _target.position - transform.position;
        toT.y = 0f;
        if (toT.sqrMagnitude < 1e-6f)
            return false;
        Vector3 flatToTarget = toT.normalized;
        if (Vector3.Dot(flatTravel, flatToTarget) < missileMinTravelToTargetDot)
            return false;

        Vector3 flatAim = aimWorldDir;
        flatAim.y = 0f;
        // Steep top-down aim: planar projection vanishes; use target bearing so cone still works.
        if (flatAim.sqrMagnitude < 1e-5f)
            flatAim = flatToTarget;
        else
            flatAim.Normalize();

        if (Vector3.Dot(flatTravel, flatAim) < missileMinTravelToAimDot)
            return false;

        return Vector3.Angle(flatTravel, flatAim) <= missileMaxTravelToAimDegrees;
    }

    Vector3 GetMissileAimDirection()
    {
        Transform m = _missiles.Muzzle;
        Vector3 from = m != null ? m.position : transform.position + transform.forward * 2f;
        Vector3 aimWorld = GetPredictedAimWorld(from);
        Vector3 d = aimWorld - from;
        if (d.sqrMagnitude < 1e-6f)
        {
            Vector3 h = _horizontalTravelDir;
            h.y = 0f;
            d = h.sqrMagnitude > 1e-6f ? h.normalized : transform.forward;
        }
        return d.normalized;
    }

    Vector3 GetPredictedAimWorld(Vector3 fromMuzzle)
    {
        Vector3 raw = GetPlayerAimWorld();
        if (!aimPredictionEnabled || _target == null)
            return raw;

        Vector3 planarV = GetPlayerPlanarVelocity();
        float shotSpeed = _missiles != null ? Mathf.Max(8f, _missiles.ProjectileMaxSpeed) : 40f;
        float dist = Vector3.Distance(fromMuzzle, raw);
        float t = Mathf.Clamp(dist / shotSpeed, 0f, aimPredictionMaxLeadTime);
        Vector3 lead = raw + new Vector3(planarV.x, 0f, planarV.z) * t;

        var fps = _target.GetComponent<FPSCharacterController>();
        if (fps != null && aimPredictionVerticalBlend > 1e-4f)
            lead.y += fps.WorldVelocity.y * t * aimPredictionVerticalBlend;

        return lead;
    }

    Vector3 GetPlayerPlanarVelocity()
    {
        if (_target == null)
            return Vector3.zero;
        var fps = _target.GetComponent<FPSCharacterController>();
        if (fps == null)
            return Vector3.zero;
        Vector3 v = fps.WorldVelocity;
        return new Vector3(v.x, 0f, v.z);
    }

    Vector3 GetPlayerAimWorld()
    {
        if (_target == null)
            return transform.position + transform.forward;

        var fps = _target.GetComponent<FPSCharacterController>();
        if (fps != null && fps.AimCamera != null)
            return fps.AimCamera.transform.position;

        if (Camera.main != null)
            return Camera.main.transform.position;

        return _target.position + Vector3.up * 1.6f;
    }

    bool HasMissileLineOfFire(Vector3 aimDirNormalized)
    {
        Transform m = _missiles.Muzzle;
        Vector3 from = m != null ? m.position : transform.position;
        float maxDist = Mathf.Max(missileAttackMaxDistance, Vector3.Distance(from, _target.position) + 2f);
        Vector3 dir = aimDirNormalized.sqrMagnitude > 1e-6f ? aimDirNormalized.normalized : transform.forward;
        Vector3 start = from + dir * losRayPadding;
        float rayLen = Mathf.Max(0f, maxDist - losRayPadding * 2f);

        var hits = Physics.RaycastAll(start, dir, rayLen, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return true;

        System.Array.Sort(hits, static (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (IsPlayerHierarchy(hit.collider.transform))
                return true;
            if (groundLayers.value != 0 && ((1 << hit.collider.gameObject.layer) & groundLayers.value) != 0)
                continue;
            return false;
        }

        return true;
    }

    bool IsPlayerHierarchy(Transform t)
    {
        if (_target == null || t == null)
            return false;
        return t == _target || t.IsChildOf(_target);
    }

    void ResolveTarget()
    {
        if (_target != null && _target.gameObject.activeInHierarchy)
            return;

        _target = null;
        var fps = FindFirstObjectByType<FPSCharacterController>();
        if (fps != null)
            _target = fps.transform;
    }

    public void ReceiveProjectileDamage(float damage, Vector3 hitPoint, Vector3 hitNormal, Transform damageSourceRoot)
    {
        if (!IsAlive || damage <= 0f)
            return;

        _health -= damage;
        if (_health <= 0f)
        {
            _health = 0f;
            OnDeath();
        }
    }

    void OnDeath()
    {
        _alive = false;
        if (_missiles != null)
            _missiles.enabled = false;

        if (destroyOnDeath)
            Destroy(gameObject, deathDestroyDelay);
    }
}
