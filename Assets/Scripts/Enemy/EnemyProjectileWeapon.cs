using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Universal enemy projectile weapon. Drop onto any enemy unit to give it a configurable gun.
/// Supports burst/rapid fire, magazines, multi-pellet spread, ballistic/homing/gravity/bounce
/// projectiles, explosion VFX, impact + explosion damage, target knockback, and firing recoil
/// on the owner unit. Driven each frame by the owning AI via <see cref="TickCombat"/>.
/// </summary>
[DisallowMultipleComponent]
public class EnemyProjectileWeapon : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  REFERENCES
    // ═══════════════════════════════════════════════════════════════════════
    [Header("References")]
    [Tooltip("Spawn / bore origin. Auto-set by EnemyTank; leave blank to use this transform.")]
    [SerializeField] Transform muzzle;

    [SerializeField] GameObject projectilePrefab;

    [Tooltip("Root Transform used as projectile owner (its hierarchy ignores collisions). " +
             "Auto-resolved from the nearest parent EnemySoldier or EnemyTank if unset.")]
    [SerializeField] Transform ownerRoot;

    [Tooltip("Extra distance along the fire direction the projectile spawns past the muzzle, " +
             "to avoid clipping through the barrel geometry.")]
    [SerializeField, Min(0f), FormerlySerializedAs("spawnForwardOffset")]
    float muzzleForwardOffset = 0.2f;

    // ═══════════════════════════════════════════════════════════════════════
    //  FIRE RATE
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Fire Rate")]
    [Tooltip("Shots per second (within a burst or during continuous fire).")]
    [SerializeField, Min(0.01f)] float shotsPerSecond = 8f;

    [Tooltip("Minimum pause between individual shots (or volleys). Stacks on top of shotsPerSecond.")]
    [SerializeField, Min(0f)] float cooldownBetweenShots;

    [Tooltip("Fire in discrete bursts rather than continuously.")]
    [SerializeField] bool burstMode;
    [SerializeField, Min(1)] int burstShotCount = 3;
    [SerializeField, Min(0f)] float burstCooldown = 0.35f;

    // ═══════════════════════════════════════════════════════════════════════
    //  MAGAZINE
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Magazine")]
    [Tooltip("When off, ammo is infinite and reloading never happens.")]
    [SerializeField] bool useMagazineSystem = true;
    [SerializeField, Min(1)] int magazineSize = 24;
    [SerializeField, Min(0f)] float reloadDuration = 1.6f;

    // ═══════════════════════════════════════════════════════════════════════
    //  PROJECTILE
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Projectile")]
    [Tooltip("Launch speed in m/s.")]
    [SerializeField, Min(0.01f)] float projectileMaxSpeed = 40f;

    [Tooltip("How long (seconds) before the projectile self-destructs.")]
    [SerializeField, Min(0.01f)] float projectileLifetime = 3f;

    [Tooltip("Visual scale of the projectile mesh.")]
    [SerializeField, Min(0.01f)] float projectileScale = 0.15f;

    [Tooltip("Extra forward acceleration applied after launch (0 = constant speed).")]
    [SerializeField, Min(0f)] float projectileAcceleration = 1f;
    [SerializeField] ProjectileWeaponEffect.AccelerationCurve accelerationCurve
        = ProjectileWeaponEffect.AccelerationCurve.Linear;
    [SerializeField, Min(0.01f)] float accelerationCurveSharpness = 3f;
    [SerializeField, Min(0f)] float jerkPerSecond = 1.5f;

    [Tooltip("Apply real ballistic gravity drop to the projectile.")]
    [SerializeField] bool useProjectileGravity;
    [Tooltip("Downward acceleration (m/s²) when gravity is on. ~25 = gentle lob; ~60 = heavy drop.")]
    [SerializeField, Min(0f)] float projectileGravity = 32f;

    [Tooltip("Projectile bounces off surfaces instead of detonating.")]
    [SerializeField] bool bounceProjectiles;
    [SerializeField, Min(0)] int maxBounces = 2;
    [SerializeField, Range(0.01f, 1f)] float bounceVelocityDamping = 0.85f;

    // ═══════════════════════════════════════════════════════════════════════
    //  SPREAD & MULTI-PROJECTILE
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Spread & Multi-Projectile")]
    [Tooltip("Number of projectiles per volley (shotgun style when > 1).")]
    [SerializeField, Min(1)] int pelletCount = 1;

    [Tooltip("Random cone half-angle (°) applied when Spread Mode is Single. 0 = no spread.")]
    [SerializeField, Min(0f)] float aimSpreadHalfAngleDegrees;

    [SerializeField] ProjectileWeapon.SpreadMode spreadMode = ProjectileWeapon.SpreadMode.Single;

    [Tooltip("0 = maximum spread; 1 = tightest spread.")]
    [SerializeField, Range(0f, 1f)] float spreadTightness = 1f;
    [SerializeField, Min(0f)] float spreadHorizontalHalfAngleDegrees = 4f;
    [SerializeField, Min(0f)] float spreadVerticalHalfAngleDegrees = 4f;

    [Tooltip("1 = all pellets share identical stats. 0 = high variance in speed / scale / accel.")]
    [SerializeField, Range(0f, 1f)] float pelletStatUniformity = 1f;

    [Tooltip("Allow projectiles from the same volley to collide with each other.")]
    [SerializeField] bool projectilesCollideWithEachOther;

    // ═══════════════════════════════════════════════════════════════════════
    //  DAMAGE
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Damage")]
    [Tooltip("Damage dealt to whatever the projectile directly hits.")]
    [SerializeField, Min(0f)] float impactDamage;
    [SerializeField, Range(0f, 1f)] float critChance;
    [SerializeField, Min(1f)] float critDamageMultiplier = 2f;
    [Tooltip("Multiplier applied to impact damage when the target is airborne.")]
    [SerializeField, Min(0f)] float midairDamageMultiplier = 1f;
    [Tooltip("Travel distance (m) at which damage reaches the minimum multiplier. 0 = no falloff.")]
    [SerializeField, Min(0f)] float damageTravelFalloffDistance;
    [SerializeField, Range(0f, 1f)] float damageTravelFalloffMinMultiplier = 0.35f;
    [SerializeField] LayerMask damageLayers = ~0;

    [Space(4f)]
    [Tooltip("Damage dealt to all targets inside the explosion radius.")]
    [SerializeField, Min(0f)] float explosionDamage;
    [SerializeField, Min(0f)] float explosionDamageRadius;
    [SerializeField, Min(0.1f)] float explosionDamageFalloff = 1f;

    // ═══════════════════════════════════════════════════════════════════════
    //  KNOCKBACK — TARGET
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Knockback — Target")]
    [Tooltip("Velocity impulse (m/s) pushed into whatever the projectile hits directly.")]
    [SerializeField, Min(0f)] float impactKnockbackImpulse;
    [Tooltip("0 = only the hit collider; >0 = radial push within this sphere from impact point.")]
    [SerializeField, Min(0f)] float impactKnockbackRadius;
    [SerializeField, Min(0.1f)] float impactKnockbackFalloff = 1f;
    [Space(4f)]
    [SerializeField, Min(0f)] float explosionKnockbackImpulse;
    [SerializeField, Min(0f)] float explosionKnockbackRadius = 4f;
    [SerializeField, Min(0.1f)] float explosionKnockbackFalloff = 1f;
    [SerializeField] LayerMask knockbackLayers = ~0;

    // ═══════════════════════════════════════════════════════════════════════
    //  KNOCKBACK — SELF (FIRING RECOIL)
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Knockback — Self (Firing Recoil)")]
    [Tooltip("Each shot pushes the ownerRoot backwards by this velocity impulse (m/s). " +
             "Great for tank cannon kick. Requires ownerRoot to implement IKnockbackVelocityReceiver.")]
    [SerializeField, Min(0f)] float firingRecoilImpulse;

    [Tooltip("Additional upward component of the recoil impulse (m/s). Combined with horizontal recoil.")]
    [SerializeField, Min(0f)] float firingRecoilUpwardImpulse;

    // ═══════════════════════════════════════════════════════════════════════
    //  EXPLOSION VFX
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Explosion VFX")]
    [SerializeField] bool explosionOnImpact = true;
    [SerializeField] GameObject explosionPrefab;
    [SerializeField, Min(0.01f)] float explosionInitialScale = 0.5f;
    [SerializeField, Min(0.01f)] float explosionMaxScale = 4f;
    [SerializeField, Min(0.01f)] float explosionGrowthSpeed = 8f;

    // ═══════════════════════════════════════════════════════════════════════
    //  HOMING
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Homing")]
    [SerializeField] bool homingEnabled;
    [SerializeField, Min(0f)] float homingStartDelay = 0.5f;
    [SerializeField, Min(0f)] float homingTurnSpeedDegreesPerSec = 180f;
    [SerializeField, Min(0.1f)] float homingRayMaxDistance = 500f;
    [SerializeField] LayerMask homingHitLayers = ~0;

    // ═══════════════════════════════════════════════════════════════════════
    //  OWNER VELOCITY
    // ═══════════════════════════════════════════════════════════════════════
    [Header("Owner Velocity Inheritance")]
    [Tooltip("Add the owner's current velocity to the projectile's launch velocity.")]
    [SerializeField] bool inheritOwnerVelocity;

    // ── runtime ────────────────────────────────────────────────────────────
    float _nextShotTime;
    int   _burstShotsRemaining;
    float _burstCooldownUntil;
    float _cooldownEndTime;
    int   _ammoInMag;
    float _reloadEndTime;
    IKnockbackVelocityReceiver _ownerKnockback;

    // ── public API ─────────────────────────────────────────────────────────
    public Transform Muzzle              => muzzle;
    public int       AmmoInMagazine      => _ammoInMag;
    public float     ProjectileSpeed     => projectileMaxSpeed;
    public bool      IsReloading         => useMagazineSystem && _ammoInMag <= 0 && Time.time < _reloadEndTime;
    public bool      UseProjectileGravity => useProjectileGravity;
    public float     ProjectileGravityAcc => projectileGravity;

    /// <summary>Override the muzzle at runtime (called by <see cref="EnemyTank"/> to wire in its muzzleFire empty).</summary>
    public void SetMuzzle(Transform m) { if (m != null) muzzle = m; }

    // ── lifecycle ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (muzzle == null)
            muzzle = transform;

        if (ownerRoot == null)
        {
            var soldier = GetComponentInParent<EnemySoldier>(true);
            if (soldier != null)
                ownerRoot = soldier.transform;
            else
            {
                var tank = GetComponentInParent<EnemyTank>(true);
                if (tank != null)
                    ownerRoot = tank.transform;
            }
        }
        if (ownerRoot == null)
            ownerRoot = transform.root;

        _ownerKnockback = ownerRoot.GetComponent<IKnockbackVelocityReceiver>()
                       ?? ownerRoot.GetComponentInChildren<IKnockbackVelocityReceiver>(true);

        _ammoInMag = useMagazineSystem ? magazineSize : int.MaxValue;
    }

    // ── public fire API ────────────────────────────────────────────────────

    public bool CanFire()
    {
        FinishReloadIfDue();
        return projectilePrefab != null && muzzle != null
            && (!useMagazineSystem || _ammoInMag > 0);
    }

    /// <summary>One-line convenience wrapper (uses Time.deltaTime).</summary>
    public void TryFire(Vector3 worldDirection) =>
        TickCombat(Time.deltaTime, true, worldDirection, null);

    /// <summary>
    /// Primary AI entry point. Call every frame with current fire intent.
    /// <paramref name="wantFire"/> must be true for the gun to fire.
    /// </summary>
    public void TickCombat(float dt, bool wantFire, Vector3 worldAimDirection,
                           Transform homingFollowTarget = null)
    {
        if (!wantFire || !CanFire()) return;

        if (burstMode)
        {
            TickBurst(worldAimDirection, homingFollowTarget);
            return;
        }

        if (Time.time < _burstCooldownUntil) return;
        if (Time.time < _nextShotTime || Time.time < _cooldownEndTime) return;

        FireOneVolley(worldAimDirection, homingFollowTarget);
        _nextShotTime    = Time.time + 1f / Mathf.Max(0.01f, shotsPerSecond);
        _cooldownEndTime = Time.time + Mathf.Max(0f, cooldownBetweenShots);
    }

    // ── internal fire logic ────────────────────────────────────────────────

    void TickBurst(Vector3 worldAimDirection, Transform homingFollowTarget)
    {
        if (Time.time < _burstCooldownUntil) return;

        if (_burstShotsRemaining <= 0)
            _burstShotsRemaining = burstShotCount;

        if (Time.time >= _nextShotTime && _burstShotsRemaining > 0)
        {
            FireOneVolley(worldAimDirection, homingFollowTarget);
            _burstShotsRemaining--;
            _nextShotTime = Time.time + 1f / Mathf.Max(0.01f, shotsPerSecond);
            if (_burstShotsRemaining <= 0)
                _burstCooldownUntil = Time.time + burstCooldown;
        }
    }

    void FireOneVolley(Vector3 worldAimDirection, Transform homingFollowTarget)
    {
        Vector3 dir = worldAimDirection.sqrMagnitude > 1e-6f
            ? worldAimDirection.normalized
            : muzzle.forward;

        if (spreadMode == ProjectileWeapon.SpreadMode.Single && aimSpreadHalfAngleDegrees > 1e-4f)
        {
            float y = Random.Range(-aimSpreadHalfAngleDegrees, aimSpreadHalfAngleDegrees);
            float p = Random.Range(-aimSpreadHalfAngleDegrees, aimSpreadHalfAngleDegrees);
            dir = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(p, y, 0f) * Vector3.forward;
            if (dir.sqrMagnitude < 1e-6f) dir = muzzle.forward;
            else dir.Normalize();
        }

        Quaternion basis = Quaternion.LookRotation(dir, Vector3.up);

        if (useMagazineSystem)
        {
            _ammoInMag--;
            if (_ammoInMag <= 0)
                _reloadEndTime = Time.time + Mathf.Max(0f, reloadDuration);
        }

        ApplyFiringRecoil(dir);

        SpawnVolley(basis * Vector3.forward,
                    basis * Vector3.right,
                    basis * Vector3.up,
                    homingFollowTarget);
    }

    void ApplyFiringRecoil(Vector3 fireDir)
    {
        if (_ownerKnockback == null) return;
        if (firingRecoilImpulse <= 0f && firingRecoilUpwardImpulse <= 0f) return;

        Vector3 back = -fireDir;
        back.y = 0f;
        if (back.sqrMagnitude > 1e-6f) back.Normalize();

        _ownerKnockback.ApplyKnockbackVelocity(back * firingRecoilImpulse
                                              + Vector3.up * firingRecoilUpwardImpulse);
    }

    void SpawnVolley(Vector3 f, Vector3 r, Vector3 u, Transform homingTarget)
    {
        if (muzzle == null || projectilePrefab == null) return;

        Transform owner = ownerRoot != null ? ownerRoot : transform;
        int   count = Mathf.Max(1, pelletCount);
        var   dirs  = new Vector3[count];
        FillSpreadDirections(f, r, u, dirs);

        float explIni = explosionInitialScale;
        float explMax = explosionMaxScale;
        float unif    = Mathf.Clamp01(pelletStatUniformity);

        for (int i = 0; i < count; i++)
        {
            Vector3 shotDir  = dirs[i];
            Vector3 spawnPos = muzzle.position + shotDir * muzzleForwardOffset;

            float scaleMul = StatVariance(1f, unif);
            float speedMul = StatVariance(1f, unif);
            float accelMul = StatVariance(1f, unif);

            var go   = Instantiate(projectilePrefab, spawnPos, Quaternion.LookRotation(shotDir));
            var proj = go.GetComponent<ProjectileWeaponEffect>();
            if (proj == null)
            {
                Debug.LogWarning(
                    $"[{nameof(EnemyProjectileWeapon)}] '{name}': prefab '{projectilePrefab.name}' " +
                    $"has no {nameof(ProjectileWeaponEffect)} — shot discarded.", this);
                Destroy(go);
                continue;
            }

            var rt = BuildRuntime(homingTarget);
            proj.InitializeProjectile(
                owner,
                shotDir,
                Mathf.Max(0.01f, projectileMaxSpeed    * speedMul),
                Mathf.Max(0f,    projectileAcceleration * accelMul),
                projectileLifetime,
                Mathf.Max(0.01f, projectileScale        * scaleMul),
                explosionOnImpact,
                explosionPrefab,
                explIni * scaleMul,
                explMax * scaleMul,
                explosionGrowthSpeed,
                in rt);
        }
    }

    ProjectileWeaponEffect.ProjectileRuntime BuildRuntime(Transform homingTarget)
    {
        Vector3 inherited = Vector3.zero;
        if (inheritOwnerVelocity && ownerRoot != null)
        {
            var cc = ownerRoot.GetComponent<CharacterController>();
            if (cc != null) inherited = cc.velocity;
        }

        return new ProjectileWeaponEffect.ProjectileRuntime
        {
            AccelCurve                       = accelerationCurve,
            CurveSharpness                   = accelerationCurveSharpness,
            JerkPerSecond                    = jerkPerSecond,
            HomingEnabled                    = homingEnabled,
            HomingDelay                      = homingStartDelay,
            HomingTurnDegPerSec              = homingTurnSpeedDegreesPerSec,
            HomingCrosshairRaycast           = false,
            HomingRayMaxDistance             = homingRayMaxDistance,
            HomingHitLayers                  = homingHitLayers,
            AimCamera                        = null,
            HomingTarget                     = homingTarget,
            InheritPlayerVelocity            = inheritOwnerVelocity,
            InheritedVelocityWorld           = inherited,
            UseProjectileGravity             = useProjectileGravity,
            ProjectileGravity                = projectileGravity,
            BounceEnabled                    = bounceProjectiles,
            MaxBounces                       = maxBounces,
            BounceVelocityDamping            = bounceVelocityDamping,
            ImpactKnockbackImpulse           = impactKnockbackImpulse,
            ImpactKnockbackRadius            = impactKnockbackRadius,
            ImpactKnockbackFalloff           = impactKnockbackFalloff,
            ExplosionKnockbackImpulse        = explosionKnockbackImpulse,
            ExplosionKnockbackRadius         = explosionKnockbackRadius,
            ExplosionKnockbackFalloff        = explosionKnockbackFalloff,
            KnockbackLayers                  = knockbackLayers,
            Detonatable                      = false,
            DetonateOnLifetimeEnd            = false,
            SourceWeapon                     = null,
            SuppressPeerProjectileCollisions = !projectilesCollideWithEachOther,
            ImpactDamage                     = impactDamage,
            CritChance                       = critChance,
            CritDamageMultiplier             = critDamageMultiplier,
            MidairDamageMultiplier           = midairDamageMultiplier,
            DamageTravelFalloffDistance      = damageTravelFalloffDistance,
            DamageTravelFalloffMinMultiplier = damageTravelFalloffMinMultiplier,
            DamageLayers                     = damageLayers,
            ExplosionDamage                  = explosionDamage,
            ExplosionDamageRadius            = explosionDamageRadius,
            ExplosionDamageFalloff           = explosionDamageFalloff,
        };
    }

    // ── helpers ────────────────────────────────────────────────────────────

    void FinishReloadIfDue()
    {
        if (!useMagazineSystem || _ammoInMag > 0 || Time.time < _reloadEndTime) return;
        _ammoInMag = magazineSize;
    }

    static float StatVariance(float baseline, float uniformity)
    {
        if (uniformity >= 0.999f) return baseline;
        float spread = Mathf.Lerp(0.35f, 0f, uniformity);
        return baseline * Random.Range(1f - spread, 1f + spread);
    }

    void FillSpreadDirections(Vector3 f, Vector3 r, Vector3 u, Vector3[] dirs)
    {
        int n = dirs.Length;
        if (n == 1 || spreadMode == ProjectileWeapon.SpreadMode.Single)
        {
            for (int i = 0; i < n; i++) dirs[i] = f;
            return;
        }

        float tx   = Mathf.Clamp01(spreadTightness);
        float hMax = spreadHorizontalHalfAngleDegrees * tx;
        float vMax = spreadVerticalHalfAngleDegrees   * tx;

        switch (spreadMode)
        {
            case ProjectileWeapon.SpreadMode.RandomCone:
                for (int i = 0; i < n; i++)
                {
                    float y = Random.Range(-hMax, hMax);
                    float p = Random.Range(-vMax, vMax);
                    dirs[i] = (Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(p, y, 0f)) * Vector3.forward;
                    if (dirs[i].sqrMagnitude < 1e-6f) dirs[i] = f; else dirs[i].Normalize();
                }
                break;

            case ProjectileWeapon.SpreadMode.FixedHorizontalFan:
                for (int i = 0; i < n; i++)
                {
                    float t   = n == 1 ? 0f : (i / (float)(n - 1)) * 2f - 1f;
                    dirs[i] = (Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(0f, t * hMax, 0f)) * Vector3.forward;
                    dirs[i].Normalize();
                }
                break;

            case ProjectileWeapon.SpreadMode.FixedRing:
                for (int i = 0; i < n; i++)
                {
                    float ang = (360f / n) * i * Mathf.Deg2Rad;
                    dirs[i] = (f + (r * Mathf.Cos(ang) + u * Mathf.Sin(ang)) * Mathf.Tan(hMax * Mathf.Deg2Rad)).normalized;
                }
                break;

            default:
                for (int i = 0; i < n; i++) dirs[i] = f;
                break;
        }
    }
}
