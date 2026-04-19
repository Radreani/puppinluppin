using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Tank AI: independent turret aiming, hull-drive movement, health, and obstacle avoidance.
///
/// Hierarchy expected under the tank root:
///   turretRotate (empty) → rotates the whole turret/gun assembly horizontally
///     turret (mesh)
///     barrelTilt  (empty) → pitches the barrel up/down
///       barrel    (mesh)
///         muzzleFire (empty) → projectile spawn point
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class EnemyTank : MonoBehaviour, IProjectileDamageReceiver
{
    // ═══════════════════════════════════════════════════════════════════════
    //  HIERARCHY
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Hierarchy")]
    [SerializeField, FormerlySerializedAs("turretYawPivot")]
    Transform turretRotate;
    [SerializeField, FormerlySerializedAs("barrelPitchPivot")]
    Transform barrelTilt;
    [SerializeField] Transform muzzleFire;

    // ═══════════════════════════════════════════════════════════════════════
    //  HEALTH
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Health")]
    [SerializeField, Min(1f)] float maxHealth = 250f;
    [SerializeField] bool destroyOnDeath = true;
    [SerializeField, Min(0f)] float deathDestroyDelay = 2f;
    [Tooltip("Optional prefab spawned at the tank's position when it dies (explosion VFX, wreck, etc.).")]
    [SerializeField] GameObject deathVfxPrefab;

    // ═══════════════════════════════════════════════════════════════════════
    //  MOVEMENT
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Movement")]
    [Tooltip("Maximum forward drive speed (m/s).")]
    [SerializeField, Min(0f)] float forwardSpeed = 7f;
    [Tooltip("Forward speed used when repositioning away from the target (m/s). " +
             "The tank turns its hull away and drives forward at this speed.")]
    [SerializeField, Min(0f)] float retreatSpeed = 4f;
    [Tooltip("Degrees per second the hull can rotate about Y.")]
    [SerializeField, Min(0f)] float bodyTurnDegreesPerSecond = 55f;
    [Tooltip("Hull-forward dot-product threshold before forward thrust is applied while turning. " +
             "Below this the hull rotates in place; at or above, it drives.")]
    [SerializeField, Range(0f, 0.98f)] float driveAlignThreshold = 0.25f;
    [Tooltip("Downward acceleration applied when airborne (m/s²).")]
    [SerializeField] float gravity = -22f;

    // ═══════════════════════════════════════════════════════════════════════
    //  ENGAGEMENT RANGES
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Engagement Ranges")]
    [Tooltip("Beyond this distance the tank will not fire and will advance toward the target.")]
    [SerializeField, Min(1f)] float maxEngageRange = 80f;
    [Tooltip("Upper bound of the preferred firing band. Tank advances until inside this range.")]
    [SerializeField, Min(1f)] float optimalMaxRange = 45f;
    [Tooltip("Lower bound of the preferred firing band. Tank retreats if closer than this.")]
    [SerializeField, Min(1f)] float optimalMinRange = 25f;
    [Tooltip("Emergency minimum. If the target is closer than this the tank always retreats and " +
             "will not fire (point-blank is dangerous for tank shells).")]
    [SerializeField, Min(0f)] float minFireRange = 10f;
    [Tooltip("Dead-band around range thresholds to prevent oscillation at the boundary.")]
    [SerializeField, Min(0f)] float rangeHysteresis = 2.5f;

    // ═══════════════════════════════════════════════════════════════════════
    //  OBSTACLE AVOIDANCE
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Obstacle Avoidance")]
    [SerializeField] bool obstacleAvoidanceEnabled = true;
    [Tooltip("Layers to test for obstacles. If set to Nothing, the default raycast layers are used.")]
    [SerializeField] LayerMask obstacleAvoidanceMask;
    [Tooltip("Sphere probe radius (m). Set close to the tank's half-width so it doesn't thread gaps.")]
    [SerializeField, Min(0.1f)] float obstacleProbeRadius = 1.4f;
    [Tooltip("How far the probes reach ahead of the tank (m).")]
    [SerializeField, Min(0.5f)] float obstacleProbeDistance = 6f;
    [Tooltip("Probe origin height above transform.position.")]
    [SerializeField, Min(0f)] float obstacleProbeHeight = 1f;
    [Tooltip("Spread angle (°) of the left/right diagonal probes.")]
    [SerializeField, Range(10f, 80f)] float obstacleSideAngle = 40f;
    [Tooltip("How strongly obstacles steer the tank's movement (0 = none, 1 = full deflection).")]
    [SerializeField, Range(0f, 1f)] float obstacleAvoidanceStrength = 0.78f;

    // ═══════════════════════════════════════════════════════════════════════
    //  AIM
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Aim Speeds")]
    [SerializeField, Min(0.01f)] float turretYawDegreesPerSecond   = 95f;
    [SerializeField, Min(0.01f)] float barrelPitchDegreesPerSecond = 45f;

    [Header("Barrel Elevation Limits  (positive = above horizontal)")]
    [Tooltip("Maximum depression below the rest bore line (°).")]
    [SerializeField] float minBarrelPitchDegrees = -8f;
    [Tooltip("Maximum elevation above the rest bore line (°).")]
    [SerializeField] float maxBarrelPitchDegrees = 32f;
    [Tooltip("Leave at 1. Set to −1 only if auto-detection gives the wrong direction.")]
    [SerializeField] float barrelPitchEulerSign = 1f;

    [Header("Target & Aim")]
    [Tooltip("Height above the target root used as the base aim point (m).")]
    [SerializeField, Min(0f)]
    [FormerlySerializedAs("lineOfSightTargetHeight")]
    float aimHeightOffset = 1.2f;

    [Tooltip("Lead the target based on its current velocity.")]
    [SerializeField] bool aimPredictionEnabled = true;
    [Tooltip("Maximum lead time (s) for movement prediction.")]
    [SerializeField, Min(0f)] float aimPredictionMaxLeadTime = 0.65f;
    [Tooltip("Fraction of target vertical velocity used in the prediction (0 = horizontal only).")]
    [SerializeField, Range(0f, 1f)] float aimPredictionVerticalBlend = 0.35f;
    [Tooltip("Raise barrel angle to compensate for projectile gravity drop (ballistic formula).")]
    [SerializeField] bool gravityCompensationEnabled = true;

    // ═══════════════════════════════════════════════════════════════════════
    //  WEAPON
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Weapon")]
    [Tooltip("Weapon component. Auto-found in children if unset.")]
    [SerializeField] EnemyProjectileWeapon gun;
    [Tooltip("Bore must be within this many degrees of the aim point before firing.")]
    [SerializeField, Min(0f)] float maxFireAlignmentDegrees = 5f;

    // ═══════════════════════════════════════════════════════════════════════
    //  PRIVATE RUNTIME STATE
    // ═══════════════════════════════════════════════════════════════════════

    CharacterController    _controller;
    Transform              _target;
    FPSCharacterController _targetFps;
    CharacterController    _targetCC;

    // Health
    float _currentHealth;
    bool  _isDead;

    // Physics
    float _verticalVelocity;

    // Barrel pitch (all existing logic preserved)
    float _baseEulerX, _baseEulerY, _baseEulerZ;
    float _boreRestElevDeg;
    float _xToUpSign = 1f;
    float _smoothedIntuitiveAngle;
    Vector3 _boreDirTurretLocal;

    // Movement phase
    enum TankPhase { Holding, Advancing, Retreating }
    TankPhase _phase = TankPhase.Holding;

    // ═══════════════════════════════════════════════════════════════════════
    //  AWAKE / START
    // ═══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        _controller    = GetComponent<CharacterController>();
        _currentHealth = maxHealth;

        if (turretRotate == null) turretRotate = FindDeep(transform, "turretRotate");
        if (barrelTilt   == null) barrelTilt   = FindDeep(transform, "barrelTilt");
        if (muzzleFire   == null) muzzleFire   = FindDeep(transform, "muzzleFire");

        if (gun == null)
            gun = GetComponentInChildren<EnemyProjectileWeapon>(true);

        if (barrelTilt != null)
        {
            FixNonUniformScale(barrelTilt);
            Vector3 e = barrelTilt.localEulerAngles;
            _baseEulerX = e.x; _baseEulerY = e.y; _baseEulerZ = e.z;
            CacheBore();
            AutoDetectXSign();
        }

        if (gun != null && muzzleFire != null)
            gun.SetMuzzle(muzzleFire);
    }

    void Start() => ResolveTarget();

    // ═══════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ═══════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (_isDead) return;

        ResolveTarget();
        float dt   = Time.deltaTime;
        float dist = TargetPlanarDist();

        // ── Movement ──────────────────────────────────────────────────────
        UpdatePhase(dist);
        TickMovement(dist, dt);

        // ── Aim & fire ────────────────────────────────────────────────────
        if (_target == null) return;

        Vector3 predicted = ComputePredictedPos();
        Vector3 pitchAim  = ComputeGravityCompensatedAimPoint(predicted);

        if (turretRotate != null) ApplyYaw(predicted, dt);
        if (barrelTilt   != null) ApplyPitch(pitchAim, dt);
        if (gun          != null) TickWeapon(pitchAim, dist, dt);
    }

    public void SetTarget(Transform t) => _target = t;

    void ResolveTarget()
    {
        if (_target != null && _target.gameObject.activeInHierarchy) return;
        _target    = null;
        _targetFps = null;
        _targetCC  = null;
        var fps = FindFirstObjectByType<FPSCharacterController>();
        if (fps == null) return;
        _target    = fps.transform;
        _targetFps = fps;
        _targetCC  = fps.GetComponent<CharacterController>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HEALTH / DAMAGE
    // ═══════════════════════════════════════════════════════════════════════

    public float CurrentHealth    => _currentHealth;
    public float MaxHealth        => maxHealth;
    public float HealthNormalized => maxHealth > 0f ? _currentHealth / maxHealth : 0f;
    public bool  IsDead           => _isDead;

    public void ReceiveProjectileDamage(float damage, Vector3 hitPoint, Vector3 hitNormal, Transform damageSourceRoot)
    {
        if (_isDead || damage <= 0f) return;
        _currentHealth -= damage;
        if (_currentHealth <= 0f)
        {
            _currentHealth = 0f;
            Die();
        }
    }

    void Die()
    {
        _isDead = true;
        if (gun != null) gun.enabled = false;
        if (deathVfxPrefab != null)
            Instantiate(deathVfxPrefab, transform.position, transform.rotation);
        if (destroyOnDeath)
            Destroy(gameObject, deathDestroyDelay);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOVEMENT — PHASE
    // ═══════════════════════════════════════════════════════════════════════

    void UpdatePhase(float dist)
    {
        if (_target == null) { _phase = TankPhase.Holding; return; }

        // Point-blank always forces retreat regardless of current phase.
        if (dist < minFireRange) { _phase = TankPhase.Retreating; return; }

        // Hysteresis: only leave Holding if range error is large enough.
        switch (_phase)
        {
            case TankPhase.Holding:
                if (dist > optimalMaxRange + rangeHysteresis) _phase = TankPhase.Advancing;
                else if (dist < optimalMinRange - rangeHysteresis) _phase = TankPhase.Retreating;
                break;

            case TankPhase.Advancing:
                if (dist <= optimalMaxRange) _phase = TankPhase.Holding;
                break;

            case TankPhase.Retreating:
                if (dist >= optimalMinRange) _phase = TankPhase.Holding;
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOVEMENT — PHYSICS
    // ═══════════════════════════════════════════════════════════════════════

    void TickMovement(float dist, float dt)
    {
        // Gravity
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        else
            _verticalVelocity += gravity * dt;

        Vector3 planar = ComputePlanarMove(dt);
        planar = ApplyObstacleAvoidance(planar);
        _controller.Move((planar + Vector3.up * _verticalVelocity) * dt);
    }

    Vector3 ComputePlanarMove(float dt)
    {
        if (_target == null || _phase == TankPhase.Holding)
            return Vector3.zero;

        // ── Desired hull facing ────────────────────────────────────────────
        // Advancing → face target. Retreating → face away (so we drive forward away).
        Vector3 toTarget = _target.position - transform.position;
        toTarget.y = 0f;
        float d = toTarget.magnitude;
        if (d < 1e-4f) return Vector3.zero;
        Vector3 toTargetDir = toTarget / d;

        Vector3 desiredFacing = _phase == TankPhase.Advancing ? toTargetDir : -toTargetDir;

        // Rotate hull toward desired facing.
        Quaternion targetRot = Quaternion.LookRotation(desiredFacing, Vector3.up);
        transform.rotation   = Quaternion.RotateTowards(
            transform.rotation, targetRot, bodyTurnDegreesPerSecond * dt);

        // ── Drive ─────────────────────────────────────────────────────────
        float dot   = Vector3.Dot(transform.forward, desiredFacing);
        float speed = _phase == TankPhase.Advancing ? forwardSpeed : retreatSpeed;

        if (dot >= driveAlignThreshold)
            // Hull is facing the right way — drive forward at full speed.
            return transform.forward * speed;
        else
            // Still rotating — creep forward very slowly so we don't stall completely.
            return transform.forward * (speed * 0.15f);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OBSTACLE AVOIDANCE
    // ═══════════════════════════════════════════════════════════════════════

    Vector3 ApplyObstacleAvoidance(Vector3 planar)
    {
        if (!obstacleAvoidanceEnabled || planar.sqrMagnitude < 1e-6f)
            return planar;

        LayerMask mask = obstacleAvoidanceMask.value == 0
            ? Physics.DefaultRaycastLayers
            : obstacleAvoidanceMask;

        Vector3 origin = transform.position + Vector3.up * obstacleProbeHeight;
        Vector3 fwd    = planar.normalized;

        // Three probes: straight ahead, diagonal-left, diagonal-right.
        Vector3[] probeDirs =
        {
            fwd,
            Quaternion.AngleAxis(-obstacleSideAngle, Vector3.up) * fwd,
            Quaternion.AngleAxis( obstacleSideAngle, Vector3.up) * fwd,
        };

        Vector3 avoidance = Vector3.zero;

        for (int i = 0; i < probeDirs.Length; i++)
        {
            if (!Physics.SphereCast(
                    origin, obstacleProbeRadius, probeDirs[i],
                    out RaycastHit hit, obstacleProbeDistance, mask,
                    QueryTriggerInteraction.Ignore))
                continue;

            // Push laterally away from the obstacle; stronger when closer.
            float proximity  = 1f - Mathf.Clamp01(hit.distance / obstacleProbeDistance);
            // Which side of the probe direction is the obstacle?
            Vector3 right    = Vector3.Cross(Vector3.up, probeDirs[i]).normalized;
            float   side     = -Mathf.Sign(Vector3.Dot(right, hit.normal));
            avoidance       += right * (side * proximity);
        }

        if (avoidance.sqrMagnitude < 1e-8f) return planar;

        float   speed   = planar.magnitude;
        Vector3 steered = Vector3.Lerp(
            planar.normalized,
            (planar.normalized + avoidance).normalized,
            obstacleAvoidanceStrength);

        return steered.normalized * speed;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  WEAPON
    // ═══════════════════════════════════════════════════════════════════════

    void TickWeapon(Vector3 aimWorld, float dist, float dt)
    {
        // Range gate: won't fire if too close (point-blank) or too far.
        bool inRange = dist >= minFireRange && dist <= maxEngageRange;
        if (!inRange)
        {
            gun.TickCombat(dt, false, transform.forward);
            return;
        }

        Vector3 boreWorld = muzzleFire != null && barrelTilt != null
            ? (muzzleFire.position - barrelTilt.position).normalized
            : turretRotate != null
                ? turretRotate.TransformDirection(_boreDirTurretLocal).normalized
                : transform.forward;

        Vector3 toTarget = aimWorld - (muzzleFire != null ? muzzleFire.position : transform.position);
        if (toTarget.sqrMagnitude < 1e-8f) return;
        toTarget.Normalize();

        float alignDeg = Vector3.Angle(boreWorld, toTarget);
        gun.TickCombat(dt, alignDeg <= maxFireAlignmentDegrees, toTarget);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AIM — YAW
    // ═══════════════════════════════════════════════════════════════════════

    void ApplyYaw(Vector3 aimWorld, float dt)
    {
        Transform parent   = turretRotate.parent != null ? turretRotate.parent : transform;
        Vector3   toTarget = aimWorld - turretRotate.position;
        Vector3   flat     = Quaternion.Inverse(parent.rotation) * toTarget;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-8f) return;

        flat.Normalize();
        float targetYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;

        Vector3 euler = turretRotate.localEulerAngles;
        float   yaw   = euler.y > 180f ? euler.y - 360f : euler.y;
        float   next  = Mathf.MoveTowardsAngle(yaw, targetYaw, turretYawDegreesPerSecond * dt);
        turretRotate.localRotation = Quaternion.Euler(euler.x, next, euler.z);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AIM — PITCH
    // ═══════════════════════════════════════════════════════════════════════

    void ApplyPitch(Vector3 aimWorld, float dt)
    {
        Vector3 boreWorld = turretRotate != null
            ? turretRotate.TransformDirection(_boreDirTurretLocal).normalized
            : _boreDirTurretLocal;

        Vector3 aimDir = aimWorld - barrelTilt.position;
        if (aimDir.sqrMagnitude < 1e-8f) return;
        aimDir.Normalize();

        float intuitiveTarget = ElevDeg(aimDir) - ElevDeg(boreWorld);
        intuitiveTarget = Mathf.Clamp(intuitiveTarget, minBarrelPitchDegrees, maxBarrelPitchDegrees);

        _smoothedIntuitiveAngle = Mathf.MoveTowardsAngle(
            _smoothedIntuitiveAngle,
            intuitiveTarget,
            barrelPitchDegreesPerSecond * dt);

        float xOffset = _smoothedIntuitiveAngle * _xToUpSign * barrelPitchEulerSign;
        barrelTilt.localRotation = Quaternion.Euler(
            _baseEulerX + xOffset,
            _baseEulerY,
            _baseEulerZ);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AIM PREDICTION
    // ═══════════════════════════════════════════════════════════════════════

    Vector3 ComputePredictedPos()
    {
        Vector3 basePos = _target.position + Vector3.up * aimHeightOffset;
        if (!aimPredictionEnabled || _targetCC == null || gun == null)
            return basePos;

        Vector3 vel    = _targetCC.velocity;
        Vector3 origin = muzzleFire != null ? muzzleFire.position : transform.position;
        float   dist   = Vector3.Distance(origin, basePos);
        float   tof    = Mathf.Min(dist / Mathf.Max(0.01f, gun.ProjectileSpeed),
                                   aimPredictionMaxLeadTime);

        Vector3 planarVel = new Vector3(vel.x, 0f, vel.z);
        return basePos
             + planarVel * tof
             + Vector3.up * vel.y * aimPredictionVerticalBlend * tof;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BALLISTIC GRAVITY COMPENSATION
    // ═══════════════════════════════════════════════════════════════════════

    Vector3 ComputeGravityCompensatedAimPoint(Vector3 targetPos)
    {
        if (!gravityCompensationEnabled || gun == null || !gun.UseProjectileGravity)
            return targetPos;

        Vector3 origin = muzzleFire  != null ? muzzleFire.position
                       : barrelTilt  != null ? barrelTilt.position
                       : transform.position;

        Vector3 diff = targetPos - origin;
        float   d    = Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z);
        float   h    = diff.y;
        float   v    = gun.ProjectileSpeed;
        float   g    = gun.ProjectileGravityAcc;

        if (d < 0.01f || v < 0.01f || g < 0.01f) return targetPos;

        float v2   = v * v;
        float disc = v2 * v2 - g * (g * d * d + 2f * h * v2);

        float tanAngle = disc >= 0f
            ? (v2 - Mathf.Sqrt(disc)) / (g * d)
            : v2 / (g * d);

        float aimY = origin.y + d * tanAngle;
        return new Vector3(targetPos.x, aimY, targetPos.z);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    float TargetPlanarDist()
    {
        if (_target == null) return float.MaxValue;
        Vector3 delta = _target.position - transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    // ── Barrel initialization helpers ──────────────────────────────────────

    static void FixNonUniformScale(Transform pivot)
    {
        Vector3 s       = pivot.localScale;
        bool    uniform = Mathf.Approximately(s.x, s.y) && Mathf.Approximately(s.y, s.z);
        if (uniform) return;

        int n = pivot.childCount;
        var wPos   = new Vector3[n];
        var wRot   = new Quaternion[n];
        var wScale = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            Transform c = pivot.GetChild(i);
            wPos[i]   = c.position;
            wRot[i]   = c.rotation;
            wScale[i] = c.lossyScale;
        }

        pivot.localScale = Vector3.one;
        Vector3 pl = pivot.lossyScale;

        for (int i = 0; i < n; i++)
        {
            Transform c = pivot.GetChild(i);
            c.position  = wPos[i];
            c.rotation  = wRot[i];
            c.localScale = new Vector3(
                pl.x > 1e-6f ? wScale[i].x / pl.x : wScale[i].x,
                pl.y > 1e-6f ? wScale[i].y / pl.y : wScale[i].y,
                pl.z > 1e-6f ? wScale[i].z / pl.z : wScale[i].z);
        }
    }

    void CacheBore()
    {
        Vector3 boreWorld;
        if (muzzleFire != null)
        {
            Vector3 d = muzzleFire.position - barrelTilt.position;
            boreWorld = d.sqrMagnitude > 1e-8f ? d.normalized : barrelTilt.forward;
        }
        else
            boreWorld = barrelTilt.forward;

        _boreRestElevDeg  = ElevDeg(boreWorld);
        _boreDirTurretLocal = turretRotate != null
            ? turretRotate.InverseTransformDirection(boreWorld)
            : boreWorld;
    }

    void AutoDetectXSign()
    {
        if (muzzleFire == null) { _xToUpSign = 1f; return; }

        Quaternion saved = barrelTilt.localRotation;
        barrelTilt.localRotation = Quaternion.Euler(_baseEulerX + 10f, _baseEulerY, _baseEulerZ);

        Vector3 delta    = muzzleFire.position - barrelTilt.position;
        float   testElev = delta.sqrMagnitude > 1e-8f ? ElevDeg(delta.normalized) : _boreRestElevDeg;

        barrelTilt.localRotation = saved;
        _xToUpSign = testElev > _boreRestElevDeg + 0.01f ? 1f : -1f;
    }

    static float ElevDeg(Vector3 dir)
    {
        float h = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
        if (h < 1e-5f) return dir.y >= 0f ? 90f : -90f;
        return Mathf.Atan2(dir.y, h) * Mathf.Rad2Deg;
    }

    static Transform FindDeep(Transform root, string sought)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && string.Equals(t.name, sought, System.StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }
}
