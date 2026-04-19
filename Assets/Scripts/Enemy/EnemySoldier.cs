using UnityEngine;

/// <summary>
/// Capsule-style enemy with health, knockback, optional squad cohesion, and <see cref="EnemyProjectileWeapon"/>.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class EnemySoldier : MonoBehaviour, IProjectileDamageReceiver, IKnockbackVelocityReceiver
{
    [Header("References")]
    [SerializeField] EnemyProjectileWeapon gun;
    [Tooltip("Layers the LOS ray tests against. Include Default / environment layers for buildings. If set to Nothing while LOS is required, all default layers are tested.")]
    [SerializeField] LayerMask obstacleMask = ~0;

    [Header("Obstacle avoidance (buildings / narrow passages)")]
    [Tooltip("Sphere-cast probes that steer away from static geometry before CharacterController moves. Use a larger radius on wide units (e.g. tanks) so they do not thread tight alleys.")]
    [SerializeField] bool obstacleAvoidanceEnabled = true;
    [Tooltip("If Nothing, uses Obstacle Mask above. Otherwise probe only these layers (e.g. Building only).")]
    [SerializeField] LayerMask obstacleAvoidanceMask;
    [SerializeField, Min(0.05f)] float obstacleAvoidanceRadius = 0.55f;
    [SerializeField, Min(0.1f)] float obstacleAvoidanceDistance = 2.6f;
    [SerializeField, Range(0f, 1f)] float obstacleAvoidanceStrength = 0.72f;
    [Tooltip("Probe origin height above transform.position (feet).")]
    [SerializeField, Min(0.1f)] float obstacleProbeHeight = 0.85f;
    [SerializeField, Range(12f, 55f)] float obstacleSideCheckAngleDegrees = 34f;
    [SerializeField, Range(0.35f, 1f)] float obstacleSideRadiusScale = 0.88f;
    [Tooltip("Extra weight for OverlapSphere escape when the body is brushing or intersecting colliders.")]
    [SerializeField, Min(0f)] float obstacleEmbeddedPushStrength = 2.4f;

    [Header("Health")]
    [SerializeField, Min(1f)] float maxHealth = 40f;
    [SerializeField] bool destroyOnDeath = true;
    [SerializeField, Min(0f)] float deathDestroyDelay;

    [Header("Movement")]
    [SerializeField, Min(0.01f)] float moveSpeed = 3.2f;
    [SerializeField, Min(0.01f)] float fleeSpeed = 5.5f;
    [SerializeField, Min(0.01f)] float rotateSpeedDegrees = 420f;
    [SerializeField, Min(0.01f)] float gravity = -22f;

    [Header("Engagement")]
    [SerializeField, Min(0.5f)] float maxEngageDistance = 42f;
    [SerializeField, Min(0.1f)] float optimalMinDistance = 7f;
    [SerializeField, Min(0.1f)] float optimalMaxDistance = 14f;
    [SerializeField, Min(0.1f)] float panicDistance = 3.5f;
    [SerializeField, Min(0.1f)] float losRayPadding = 0.08f;
    [Tooltip("If off, the soldier may shoot whenever in range (no raycast).")]
    [SerializeField] bool requireLineOfSight;
    [Tooltip("When LOS is on: ray between these heights (feet + Y) instead of muzzle→camera.")]
    [SerializeField] bool lineOfSightUseTorsoRay = true;
    [SerializeField, Min(0.1f)] float lineOfSightOriginHeight = 1.1f;
    [SerializeField, Min(0.1f)] float lineOfSightTargetHeight = 1.2f;
    [Tooltip("Hits on these layers are skipped only if the surface faces mostly up (floor). Vertical walls on the same layer still block.")]
    [SerializeField] LayerMask lineOfSightPassThroughLayers;
    [Tooltip("Min dot(hit.normal, up) to treat a pass-through layer hit as floor (skip). Lower = stricter walls.")]
    [SerializeField, Range(0f, 1f)] float losPassThroughMinGroundUpDot = 0.55f;
    [Header("LOS — reposition when blocked")]
    [Tooltip("While in range but LOS blocked, strafe / step sideways to find a clear shot.")]
    [SerializeField] bool losSeekWhenBlocked = true;
    [SerializeField, Range(0f, 1f)] float losSeekBlend = 0.85f;
    [SerializeField, Min(0.1f)] float losSeekSampleOffset = 0.65f;
    [SerializeField, Min(0.5f)] float losSeekStrafeFlipTime = 2.2f;
    [Header("Aim prediction")]
    [SerializeField] bool aimPredictionEnabled = true;
    [SerializeField, Min(0.05f)] float aimPredictionMaxLeadTime = 0.55f;
    [SerializeField, Range(0f, 1f)] float aimPredictionVerticalBlend = 0.35f;
    [Tooltip("First hit tells the squad to scatter (if this soldier is in a squad).")]
    [SerializeField] bool requestSquadScatterOnFirstHit;

    CharacterController _controller;
    Transform _target;
    float _health;
    Vector3 _knockbackPlanar;
    float _knockbackVertical;
    EnemySquad _squad;
    Vector3 _formationLocalOffset;
    float _verticalVelocity;
    bool _scatterNotified;
    float _losSeekStrafeSign = 1f;
    float _losSeekStuckTimer;
    readonly Collider[] _obstacleOverlapBuffer = new Collider[24];

    public bool IsAlive => _health > 0f;
    public EnemySquad Squad => _squad;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _health = maxHealth;
        if (gun == null)
            gun = GetComponentInChildren<EnemyProjectileWeapon>(true);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        optimalMaxDistance = Mathf.Max(optimalMaxDistance, optimalMinDistance + 0.25f);
        optimalMinDistance = Mathf.Max(0.1f, Mathf.Min(optimalMinDistance, optimalMaxDistance - 0.25f));
        panicDistance = Mathf.Min(panicDistance, optimalMinDistance - 0.05f);
        panicDistance = Mathf.Max(0.1f, panicDistance);
        obstacleAvoidanceDistance = Mathf.Max(obstacleAvoidanceDistance, obstacleAvoidanceRadius + 0.15f);
    }
#endif

    void Start()
    {
        ResolveTarget();
    }

    void Update()
    {
        if (!IsAlive)
            return;

        ResolveTarget();
        float dt = Time.deltaTime;

        ApplyGravity(dt);

        Vector3 origin = transform.position;
        Vector3 planarMove = Vector3.zero;

        if (_target != null)
        {
            Vector3 toTarget = _target.position - origin;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            Vector3 dirTo = dist > 1e-4f ? toTarget / dist : transform.forward;

            Quaternion look = Quaternion.LookRotation(dirTo, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                look,
                rotateSpeedDegrees * dt);

            if (dist > maxEngageDistance)
                planarMove = dirTo;
            else
                planarMove = ComputeCombatPlanar(dist, dirTo);
        }

        bool inEngageRange = _target != null && HorizontalDistanceToTarget() <= maxEngageDistance;
        bool losBlocked = requireLineOfSight && inEngageRange && !HasLineOfFire();
        TickLosSeekTimer(dt, losBlocked);

        if (losSeekWhenBlocked && losBlocked)
            planarMove = ApplyLosSeekToPlanar(planarMove);

        planarMove += ComputeFormationBias();
        planarMove = ApplyObstacleAvoidanceToPlanar(planarMove);
        planarMove = Vector3.ClampMagnitude(planarMove, 1f) * GetMaxSpeedForPlan(planarMove, _target);

        bool wantShoot = _target != null
            && gun != null
            && gun.CanFire()
            && inEngageRange
            && (!requireLineOfSight || HasLineOfFire());

        if (wantShoot)
            gun.TickCombat(dt, true, GetAimDirection());

        Vector3 delta = planarMove * dt + Vector3.up * (_verticalVelocity * dt);
        delta += new Vector3(_knockbackPlanar.x, _knockbackVertical, _knockbackPlanar.z) * dt;
        _controller.Move(delta);

        float groundMul = _controller.isGrounded ? 1f : 0.35f;
        _knockbackPlanar *= Mathf.Exp(-6f * dt * groundMul);
        _knockbackVertical *= Mathf.Exp(-4f * dt);
    }

    void TickLosSeekTimer(float dt, bool losBlocked)
    {
        if (!losBlocked)
        {
            _losSeekStuckTimer = 0f;
            return;
        }

        _losSeekStuckTimer += dt;
        if (_losSeekStuckTimer >= losSeekStrafeFlipTime)
        {
            _losSeekStuckTimer = 0f;
            _losSeekStrafeSign *= -1f;
        }
    }

    Vector3 ApplyLosSeekToPlanar(Vector3 combatPlanar)
    {
        Vector3 toP = _target.position - transform.position;
        toP.y = 0f;
        if (toP.sqrMagnitude < 1e-4f)
            return combatPlanar;

        Vector3 dirTo = toP.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, dirTo).normalized;

        bool centerClear = TestLineOfSightWithOriginOffset(Vector3.zero);
        bool leftClear = TestLineOfSightWithOriginOffset(-right * losSeekSampleOffset);
        bool rightClear = TestLineOfSightWithOriginOffset(right * losSeekSampleOffset);

        Vector3 seek = Vector3.zero;
        if (centerClear)
            seek = Vector3.zero;
        else if (leftClear && !rightClear)
            seek = -right;
        else if (rightClear && !leftClear)
            seek = right;
        else
            seek = right * _losSeekStrafeSign;

        if (seek.sqrMagnitude < 1e-6f)
            return combatPlanar;

        Vector3 blended = Vector3.Lerp(combatPlanar, seek, losSeekBlend);
        if (blended.sqrMagnitude < 1e-6f && combatPlanar.sqrMagnitude < 1e-6f)
            blended = seek;
        return blended;
    }

    float GetMaxSpeedForPlan(Vector3 planarDir, Transform target)
    {
        if (target == null)
            return moveSpeed;

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < panicDistance && planarDir.sqrMagnitude > 1e-6f)
        {
            Vector3 away = -to.normalized;
            if (Vector3.Dot(planarDir.normalized, away) > 0.25f)
                return fleeSpeed;
        }

        return moveSpeed;
    }

    Vector3 ComputeCombatPlanar(float planarDist, Vector3 dirToTarget)
    {
        if (planarDist > maxEngageDistance)
            return Vector3.zero;

        if (planarDist < panicDistance)
            return -dirToTarget;

        if (planarDist < optimalMinDistance)
            return -dirToTarget;

        if (planarDist > optimalMaxDistance)
            return dirToTarget;

        return Vector3.zero;
    }

    Vector3 ComputeFormationBias()
    {
        if (_squad == null || _squad.FormationInfluence <= 1e-4f)
            return Vector3.zero;

        Vector3 anchor = _squad.FormationAnchor;
        Vector3 slot = anchor + _squad.GetFormationRotation() * _formationLocalOffset;
        Vector3 toSlot = slot - transform.position;
        toSlot.y = 0f;
        float d = toSlot.magnitude;
        if (d < _squad.FormationArrivalSlack)
            return Vector3.zero;

        return toSlot.normalized * _squad.FormationInfluence;
    }

    Vector3 GetAimDirection()
    {
        Vector3 eye = gun != null && gun.Muzzle != null ? gun.Muzzle.position : transform.position + Vector3.up * 1.2f;
        Vector3 aim = GetPredictedAimWorld(eye);
        Vector3 d = aim - eye;
        if (d.sqrMagnitude < 1e-6f)
            d = transform.forward;
        return d.normalized;
    }

    Vector3 GetPredictedAimWorld(Vector3 fromMuzzle)
    {
        Vector3 raw = GetPlayerAimWorld();
        if (!aimPredictionEnabled || _target == null)
            return raw;

        Vector3 planarV = GetPlayerPlanarVelocity();
        float shotSpeed = gun != null ? gun.ProjectileSpeed : 32f;
        float dist = Vector3.Distance(fromMuzzle, raw);
        float t = Mathf.Clamp(dist / Mathf.Max(0.15f, shotSpeed), 0f, aimPredictionMaxLeadTime);

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
        if (fps != null)
        {
            Vector3 v = fps.WorldVelocity;
            return new Vector3(v.x, 0f, v.z);
        }

        return Vector3.zero;
    }

    /// <summary>Same idea as the player weapon: aim at the view/camera.</summary>
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

    float HorizontalDistanceToTarget()
    {
        if (_target == null)
            return float.MaxValue;
        Vector3 a = transform.position;
        Vector3 b = _target.position;
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    LayerMask GetLosQueryMask()
    {
        if (!requireLineOfSight)
            return obstacleMask;
        if (obstacleMask.value == 0)
            return Physics.DefaultRaycastLayers;
        return obstacleMask;
    }

    LayerMask GetObstacleAvoidanceMask()
    {
        if (obstacleAvoidanceMask.value != 0)
            return obstacleAvoidanceMask;
        if (obstacleMask.value != 0)
            return obstacleMask;
        return Physics.DefaultRaycastLayers;
    }

    bool IsOwnCollider(Collider c)
    {
        if (c == null)
            return false;
        Transform t = c.transform;
        return t == transform || t.IsChildOf(transform);
    }

    /// <summary>Biases planar steering away from building colliders so large units keep clearance and back out of tight gaps.</summary>
    Vector3 ApplyObstacleAvoidanceToPlanar(Vector3 planar)
    {
        if (!obstacleAvoidanceEnabled)
            return planar;

        LayerMask mask = GetObstacleAvoidanceMask();
        if (mask.value == 0)
            return planar;

        Vector3 origin = transform.position + Vector3.up * obstacleProbeHeight;
        float r = obstacleAvoidanceRadius;
        float dist = obstacleAvoidanceDistance;

        Vector3 flatMove = new Vector3(planar.x, 0f, planar.z);
        Vector3 forward = flatMove.sqrMagnitude > 1e-6f
            ? flatMove.normalized
            : new Vector3(transform.forward.x, 0f, transform.forward.z);
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 push = Vector3.zero;

        void AddNormalPush(in RaycastHit h, float weight = 1f)
        {
            if (IsOwnCollider(h.collider))
                return;
            Vector3 n = new Vector3(h.normal.x, 0f, h.normal.z);
            if (n.sqrMagnitude < 1e-5f)
                return;
            push += n.normalized * weight;
        }

        if (Physics.SphereCast(origin, r, forward, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
            AddNormalPush(hit);

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 dir = Quaternion.AngleAxis(obstacleSideCheckAngleDegrees * s, Vector3.up) * forward;
            if (Physics.SphereCast(origin, r * obstacleSideRadiusScale, dir, out hit, dist * 0.92f, mask, QueryTriggerInteraction.Ignore))
                AddNormalPush(hit, 0.85f);
        }

        int ov = Physics.OverlapSphereNonAlloc(origin, r * 0.99f, _obstacleOverlapBuffer, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < ov; i++)
        {
            Collider c = _obstacleOverlapBuffer[i];
            if (c == null || IsOwnCollider(c))
                continue;

            Vector3 cp = Physics.ClosestPoint(origin, c, c.transform.position, c.transform.rotation);
            Vector3 away = origin - cp;
            away.y = 0f;
            float d = away.magnitude;
            if (d < 1e-4f)
                continue;

            float penetration = Mathf.Clamp01(1f - d / Mathf.Max(0.05f, r));
            if (penetration > 0.02f)
                push += away.normalized * (penetration * obstacleEmbeddedPushStrength);
        }

        if (push.sqrMagnitude < 1e-8f)
            return planar;

        push = push.normalized;
        float mag = flatMove.magnitude;

        if (flatMove.sqrMagnitude < 1e-6f)
            return push;

        Vector3 dirIn = flatMove.normalized;
        Vector3 steer = (dirIn + push).normalized;
        Vector3 blended = Vector3.Slerp(dirIn, steer, obstacleAvoidanceStrength);
        return blended * Mathf.Max(mag, 0.2f);
    }

    bool HasLineOfFire() => TestLineOfSightWithOriginOffset(Vector3.zero);

    bool TestLineOfSightWithOriginOffset(Vector3 worldOriginOffsetXZ)
    {
        if (_target == null)
            return false;
        if (!lineOfSightUseTorsoRay && (gun == null || gun.Muzzle == null))
            return false;

        GetLosRayEndpoints(out Vector3 from, out Vector3 to);
        from += new Vector3(worldOriginOffsetXZ.x, 0f, worldOriginOffsetXZ.z);
        return TestLineOfSightSegment(from, to);
    }

    void GetLosRayEndpoints(out Vector3 from, out Vector3 to)
    {
        if (lineOfSightUseTorsoRay)
        {
            from = transform.position + Vector3.up * lineOfSightOriginHeight;
            to = _target.position + Vector3.up * lineOfSightTargetHeight;
        }
        else
        {
            from = gun.Muzzle.position;
            to = GetPlayerAimWorld();
        }
    }

    bool TestLineOfSightSegment(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 1e-3f)
            return true;

        dir /= dist;
        float maxDist = Mathf.Max(0f, dist - losRayPadding * 2f);
        Vector3 start = from + dir * losRayPadding;
        LayerMask mask = GetLosQueryMask();
        const int maxSteps = 12;

        for (int step = 0; step < maxSteps && maxDist > 0.002f; step++)
        {
            if (!Physics.Raycast(start, dir, out RaycastHit hit, maxDist, mask, QueryTriggerInteraction.Ignore))
                return true;

            Transform t = hit.collider.transform;
            if (IsPlayerHierarchy(t))
                return true;

            if (IsThisSoldierHierarchy(t))
            {
                float advance = hit.distance + 0.02f;
                start += dir * advance;
                maxDist -= advance;
                continue;
            }

            if (ShouldPassThroughHit(hit))
            {
                float advance = hit.distance + 0.02f;
                start += dir * advance;
                maxDist -= advance;
                continue;
            }

            return false;
        }

        return true;
    }

    bool ShouldPassThroughHit(RaycastHit hit)
    {
        if (lineOfSightPassThroughLayers.value == 0)
            return false;
        int layer = hit.collider.gameObject.layer;
        if (((1 << layer) & lineOfSightPassThroughLayers.value) == 0)
            return false;

        Vector3 n = hit.normal;
        if (n.sqrMagnitude < 1e-6f)
            return false;
        float upDot = Vector3.Dot(n.normalized, Vector3.up);
        return upDot >= losPassThroughMinGroundUpDot;
    }

    bool IsPlayerHierarchy(Transform t)
    {
        if (_target == null || t == null)
            return false;
        return t == _target || t.IsChildOf(_target);
    }

    bool IsThisSoldierHierarchy(Transform t) =>
        t != null && (t == transform || t.IsChildOf(transform));

    void ResolveTarget()
    {
        if (_target != null && _target.gameObject.activeInHierarchy)
            return;

        _target = null;
        var fps = FindFirstObjectByType<FPSCharacterController>();
        if (fps != null)
            _target = fps.transform;
    }

    public void SetTarget(Transform t) => _target = t;

    public void BindSquad(EnemySquad squad, Vector3 formationLocalOffset)
    {
        _squad = squad;
        _formationLocalOffset = formationLocalOffset;
    }

    void ApplyGravity(float dt)
    {
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        else
            _verticalVelocity += gravity * dt;
    }

    public void ReceiveProjectileDamage(float damage, Vector3 hitPoint, Vector3 hitNormal, Transform damageSourceRoot)
    {
        if (!IsAlive || damage <= 0f)
            return;

        if (requestSquadScatterOnFirstHit && !_scatterNotified && _squad != null)
        {
            _scatterNotified = true;
            _squad.SetScattered(true);
        }

        _health -= damage;
        if (_health <= 0f)
        {
            _health = 0f;
            OnDeath();
        }
    }

    public void ApplyKnockbackVelocity(Vector3 worldDeltaVelocity)
    {
        if (!IsAlive)
            return;

        _knockbackPlanar += new Vector3(worldDeltaVelocity.x, 0f, worldDeltaVelocity.z);
        _knockbackVertical += worldDeltaVelocity.y;
    }

    void OnDeath()
    {
        if (gun != null)
            gun.enabled = false;

        _squad?.NotifyMemberDied(this);

        if (destroyOnDeath)
            Destroy(gameObject, deathDestroyDelay);
    }
}
