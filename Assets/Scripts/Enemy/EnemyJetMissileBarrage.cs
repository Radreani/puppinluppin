using UnityEngine;

/// <summary>
/// Missile battery for <see cref="EnemyJet"/>: same <see cref="ProjectileWeaponEffect"/> pipeline as the player weapon, with optional homing, explosion on impact, and staggered barrage fire.
/// Ammo is only restored via <see cref="RefillAmmo"/> (at the jet’s <see cref="EnemyJetRunwayPad"/>), not timed reload.
/// </summary>
[DisallowMultipleComponent]
public class EnemyJetMissileBarrage : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Usually assigned from Enemy Jet → Missile Muzzle / Prefab.")]
    [SerializeField] Transform muzzle;
    [Tooltip("Usually assigned from Enemy Jet → Missile Projectile Prefab.")]
    [SerializeField] GameObject missilePrefab;
    [SerializeField] Transform ownerRoot;

    [Header("Magazine (resupply only)")]
    [SerializeField, Min(1)] int missileMagazineSize = 12;
    [SerializeField, Min(1)] int missilesPerBarrage = 4;
    [SerializeField, Min(0f)] float timeBetweenMissilesInBarrage = 0.12f;
    [SerializeField, Min(0f)] float barrageCooldown = 1.75f;

    [Header("Projectile")]
    [SerializeField, Min(0f)] float spawnForwardOffset = 0.35f;
    [SerializeField, Min(0.01f)] float projectileMaxSpeed = 48f;
    [SerializeField, Min(0f)] float projectileAcceleration = 1f;
    [SerializeField] ProjectileWeaponEffect.AccelerationCurve accelerationCurve = ProjectileWeaponEffect.AccelerationCurve.Linear;
    [SerializeField, Min(0.01f)] float accelerationCurveSharpness = 3f;
    [SerializeField, Min(0f)] float jerkPerSecond = 1.5f;
    [SerializeField, Min(0.01f)] float projectileLifetime = 8f;
    [SerializeField, Min(0.01f)] float projectileScale = 0.25f;
    [SerializeField] bool useProjectileGravity;
    [SerializeField, Min(0f)] float projectileGravity = 32f;
    [SerializeField] bool bounceProjectiles;
    [SerializeField, Min(0)] int maxBounces = 2;
    [SerializeField, Range(0.01f, 1f)] float bounceVelocityDamping = 0.85f;

    [Header("Spread (per missile)")]
    [SerializeField, Range(0f, 12f)] float aimSpreadHalfAngleDegrees = 2f;

    [Header("Damage")]
    [SerializeField, Min(0f)] float impactDamage = 18f;
    [SerializeField, Range(0f, 1f)] float critChance;
    [SerializeField, Min(1f)] float critDamageMultiplier = 2f;
    [SerializeField, Min(0f)] float midairDamageMultiplier = 1f;
    [SerializeField, Min(0f)] float damageTravelFalloffDistance;
    [SerializeField, Range(0f, 1f)] float damageTravelFalloffMinMultiplier = 0.5f;
    [SerializeField] LayerMask damageLayers = ~0;

    [Header("Knockback")]
    [SerializeField, Min(0f)] float impactKnockbackImpulse;
    [SerializeField, Min(0f)] float impactKnockbackRadius;
    [SerializeField, Min(0.1f)] float impactKnockbackFalloff = 1f;
    [SerializeField, Min(0f)] float explosionKnockbackImpulse;
    [SerializeField, Min(0f)] float explosionKnockbackRadius = 5f;
    [SerializeField, Min(0.1f)] float explosionKnockbackFalloff = 1f;
    [SerializeField] LayerMask knockbackLayers = ~0;

    [Header("Explosion on impact")]
    [SerializeField] bool explosionOnImpact = true;
    [SerializeField] GameObject explosionPrefab;
    [SerializeField, Min(0.01f)] float explosionInitialScale = 0.5f;
    [SerializeField, Min(0.01f)] float explosionMaxScale = 4f;
    [SerializeField, Min(0.01f)] float explosionGrowthSpeed = 8f;
    [SerializeField, Min(0f)] float explosionDamage = 40f;
    [SerializeField, Min(0f)] float explosionDamageRadius = 6f;
    [SerializeField, Min(0.1f)] float explosionDamageFalloff = 1f;

    [Header("Homing (optional)")]
    [Tooltip("Steers toward the player transform. Raycast-style homing needs a camera and is not used on this component.")]
    [SerializeField] bool homingEnabled;
    [SerializeField, Min(0f)] float homingStartDelay = 0.35f;
    [SerializeField, Min(0f)] float homingTurnSpeedDegreesPerSec = 220f;
    [SerializeField] LayerMask homingHitLayers = ~0;

    int _ammo;
    float _barrageCooldownUntil;
    int _burstRemaining;
    float _nextMissileTime;
    Vector3 _pendingAimDir;
    Transform _pendingHomingTarget;

    public Transform Muzzle => muzzle;
    public float ProjectileMaxSpeed => projectileMaxSpeed;
    public int Ammo => _ammo;
    public int MagazineSize => missileMagazineSize;
    public bool NeedsResupply => _ammo < missilesPerBarrage;
    public bool IsOutOfAmmo => _ammo <= 0;
    public bool BarrageInProgress => _burstRemaining > 0;

    /// <summary>Wired from <see cref="EnemyJet"/> so muzzle & prefab live in one place on the jet inspector.</summary>
    public void ApplyDriverRefs(Transform muzzleOverride, GameObject prefabOverride)
    {
        if (muzzleOverride != null)
            muzzle = muzzleOverride;
        if (prefabOverride != null)
            missilePrefab = prefabOverride;
    }

    void Awake()
    {
        if (muzzle == null)
            muzzle = transform;
        var jet = GetComponentInParent<EnemyJet>(true);
        if (jet != null)
            ownerRoot = jet.transform;
        else if (ownerRoot == null)
            ownerRoot = transform.root;
        _ammo = missileMagazineSize;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        missilesPerBarrage = Mathf.Max(1, missilesPerBarrage);
        missileMagazineSize = Mathf.Max(missilesPerBarrage, missileMagazineSize);
    }
#endif

    public void RefillAmmo()
    {
        _ammo = missileMagazineSize;
        _burstRemaining = 0;
    }

    /// <summary>Starts a barrage if cooldown and ammo allow. Returns true if a new barrage was started.</summary>
    public bool TryBeginBarrage(Vector3 worldAimDirection, Transform homingTarget)
    {
        if (missilePrefab == null || muzzle == null)
            return false;
        if (_burstRemaining > 0)
            return false;
        if (Time.time < _barrageCooldownUntil)
            return false;
        if (_ammo < missilesPerBarrage)
            return false;

        Vector3 d = worldAimDirection.sqrMagnitude > 1e-6f ? worldAimDirection.normalized : transform.forward;
        _pendingAimDir = d;
        _pendingHomingTarget = homingTarget;
        _burstRemaining = missilesPerBarrage;
        _nextMissileTime = Time.time;
        return true;
    }

    /// <summary>Fire the next missile in an active barrage, if any. Call from Update.</summary>
    public void TickBarrage()
    {
        if (_burstRemaining <= 0)
            return;
        if (Time.time < _nextMissileTime)
            return;

        Vector3 dir = ApplySpread(_pendingAimDir);
        SpawnOneMissile(dir, _pendingHomingTarget);
        _ammo--;
        _burstRemaining--;
        _nextMissileTime = Time.time + Mathf.Max(0f, timeBetweenMissilesInBarrage);

        if (_burstRemaining <= 0)
            _barrageCooldownUntil = Time.time + Mathf.Max(0f, barrageCooldown);
    }

    Vector3 ApplySpread(Vector3 dir)
    {
        if (aimSpreadHalfAngleDegrees <= 1e-4f)
            return dir;
        float yaw = Random.Range(-aimSpreadHalfAngleDegrees, aimSpreadHalfAngleDegrees);
        float pitch = Random.Range(-aimSpreadHalfAngleDegrees, aimSpreadHalfAngleDegrees);
        Quaternion q = Quaternion.LookRotation(dir) * Quaternion.Euler(pitch, yaw, 0f);
        return q * Vector3.forward;
    }

    void SpawnOneMissile(Vector3 dir, Transform homingTarget)
    {
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : muzzle.forward.sqrMagnitude > 1e-6f ? muzzle.forward.normalized : Vector3.forward;

        Vector3 spawnPos = muzzle.position + dir * spawnForwardOffset;
        var go = Instantiate(missilePrefab, spawnPos, Quaternion.LookRotation(dir));
        var proj = go.GetComponent<ProjectileWeaponEffect>();
        if (proj == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyJetMissileBarrage)} on '{name}': missile prefab '{missilePrefab.name}' has no {nameof(ProjectileWeaponEffect)}.",
                this);
            Destroy(go);
            return;
        }

        var runtime = new ProjectileWeaponEffect.ProjectileRuntime
        {
            AccelCurve = accelerationCurve,
            CurveSharpness = accelerationCurveSharpness,
            JerkPerSecond = jerkPerSecond,
            HomingEnabled = homingEnabled,
            HomingDelay = homingStartDelay,
            HomingTurnDegPerSec = homingTurnSpeedDegreesPerSec,
            HomingCrosshairRaycast = false,
            HomingRayMaxDistance = 500f,
            HomingHitLayers = homingHitLayers,
            AimCamera = null,
            HomingTarget = homingEnabled ? homingTarget : null,
            InheritPlayerVelocity = false,
            InheritedVelocityWorld = Vector3.zero,
            UseProjectileGravity = useProjectileGravity,
            ProjectileGravity = projectileGravity,
            BounceEnabled = bounceProjectiles,
            MaxBounces = maxBounces,
            BounceVelocityDamping = bounceVelocityDamping,
            ImpactKnockbackImpulse = impactKnockbackImpulse,
            ImpactKnockbackRadius = impactKnockbackRadius,
            ImpactKnockbackFalloff = impactKnockbackFalloff,
            ExplosionKnockbackImpulse = explosionKnockbackImpulse,
            ExplosionKnockbackRadius = explosionKnockbackRadius,
            ExplosionKnockbackFalloff = explosionKnockbackFalloff,
            KnockbackLayers = knockbackLayers,
            Detonatable = false,
            DetonateOnLifetimeEnd = false,
            SourceWeapon = null,
            SuppressPeerProjectileCollisions = true,
            ImpactDamage = impactDamage,
            CritChance = critChance,
            CritDamageMultiplier = critDamageMultiplier,
            MidairDamageMultiplier = midairDamageMultiplier,
            DamageTravelFalloffDistance = damageTravelFalloffDistance,
            DamageTravelFalloffMinMultiplier = damageTravelFalloffMinMultiplier,
            DamageLayers = damageLayers,
            ExplosionDamage = explosionDamage,
            ExplosionDamageRadius = explosionDamageRadius,
            ExplosionDamageFalloff = explosionDamageFalloff
        };

        proj.InitializeProjectile(
            ownerRoot != null ? ownerRoot : transform,
            dir,
            projectileMaxSpeed,
            projectileAcceleration,
            projectileLifetime,
            projectileScale,
            explosionOnImpact,
            explosionPrefab,
            explosionInitialScale,
            explosionMaxScale,
            explosionGrowthSpeed,
            in runtime);

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }
}
