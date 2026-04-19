using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Configurable projectile weapon (single/shotgun, charge, homing, detonate, etc.). Fire key on <see cref="FPSCharacterController"/>.
/// </summary>
[DisallowMultipleComponent]
public class ProjectileWeapon : MonoBehaviour
{
    public enum SpreadMode
    {
        Single,
        RandomCone,
        FixedHorizontalFan,
        FixedRing
    }

    [Header("References")]
    [FormerlySerializedAs("characterController")]
    [SerializeField] FPSCharacterController player;
    [FormerlySerializedAs("defaultAimCamera")]
    [SerializeField] Camera aimCamera;
    [FormerlySerializedAs("defaultHandSpawn")]
    [SerializeField] Transform handSpawnPoint;
    [SerializeField] Transform offHandSpawnPoint;
    [SerializeField, Min(0f)] float spawnForwardOffset = 0.2f;
    [SerializeField] GameObject projectilePrefab;

    [Header("Projectile")]
    [SerializeField, Min(0f)] float projectileAcceleration = 1f;
    [SerializeField] ProjectileWeaponEffect.AccelerationCurve accelerationCurve = ProjectileWeaponEffect.AccelerationCurve.Linear;
    [SerializeField, Min(0.01f)] float accelerationCurveSharpness = 3f;
    [SerializeField, Min(0f)] float jerkPerSecond = 1.5f;
    [Tooltip("Muzzle speed in m/s. For arcing shots (gravity on), lower values (e.g. 12–28) read more like a lob; high values flatten the arc.")]
    [SerializeField, Min(0.01f)] float projectileMaxSpeed = 40f;
    [SerializeField, Min(0.01f)] float projectileLifetime = 3f;
    [SerializeField, Min(0.01f)] float projectileScale = 0.15f;
    [Tooltip("Uses real ballistics: one velocity vector, instant launch speed, downward acceleration each frame (no separate \"motor\" along aim). Off = straight-line style motion with optional speed ramp.")]
    [SerializeField] bool useProjectileGravity;
    [Tooltip("Downward acceleration in m/s² while gravity is on. Higher = heavier / faster drop (tighter arc). ~25 is mild; ~45–70 is very \"shotput\". Ignored when gravity is off.")]
    [SerializeField, Min(0f)] float projectileGravity = 32f;
    [SerializeField] bool bounceProjectiles;
    [SerializeField, Min(0)] int maxBounces = 2;
    [SerializeField, Range(0.01f, 1f)] float bounceVelocityDamping = 0.85f;

    [Header("Recoil (per volley)")]
    [SerializeField, Min(0f)] float maxRecoilHorizontalDegrees;
    [SerializeField, Min(0f)] float maxRecoilVerticalDegrees = 2f;

    [Header("Knockback")]
    [Tooltip("Impulse at hit (m/s velocity change). 0 = off.")]
    [SerializeField, Min(0f)] float impactKnockbackImpulse;
    [Tooltip("0 = only the collider struck; >0 = spherical push from impact point with falloff.")]
    [SerializeField, Min(0f)] float impactKnockbackRadius;
    [SerializeField, Min(0.1f)] float impactKnockbackFalloff = 1f;
    [SerializeField, Min(0f)] float explosionKnockbackImpulse;
    [SerializeField, Min(0f)] float explosionKnockbackRadius = 4f;
    [SerializeField, Min(0.1f)] float explosionKnockbackFalloff = 1f;
    [SerializeField] LayerMask knockbackLayers = ~0;

    [Header("Damage")]
    [Tooltip("Direct hit damage per pellet (0 = none). Crit and travel falloff apply before this is sent.")]
    [SerializeField, Min(0f)] float impactDamage;
    [SerializeField, Range(0f, 1f)] float critChance;
    [SerializeField, Min(1f)] float critDamageMultiplier = 2f;
    [Tooltip("1 = no change. Values above 1 multiply damage when the target is not grounded.")]
    [SerializeField, Min(0f)] float midairDamageMultiplier = 1f;
    [Tooltip("0 = no falloff. At this travel distance from spawn, damage reaches the min multiplier.")]
    [SerializeField, Min(0f)] float damageTravelFalloffDistance;
    [Tooltip("Damage multiplier at max travel distance (0–1).")]
    [SerializeField, Range(0f, 1f)] float damageTravelFalloffMinMultiplier = 0.35f;
    [SerializeField] LayerMask damageLayers = ~0;
    [SerializeField, Min(0f)] float explosionDamage;
    [SerializeField, Min(0f)] float explosionDamageRadius;
    [SerializeField, Min(0.1f)] float explosionDamageFalloff = 1f;

    [Header("Shooter knockback (per volley)")]
    [Tooltip("Pushes the firing player opposite to aim (m/s velocity change).")]
    [SerializeField, Min(0f)] float shooterKnockbackImpulse;

    [Header("Multi-projectile & spread")]
    [SerializeField, Min(1)] int pelletCount = 1;
    [SerializeField] SpreadMode spreadMode = SpreadMode.Single;
    [SerializeField, Range(0f, 1f)] float spreadTightness = 1f;
    [SerializeField, Min(0f)] float spreadHorizontalHalfAngleDegrees = 4f;
    [SerializeField, Min(0f)] float spreadVerticalHalfAngleDegrees = 4f;
    [SerializeField, Range(0f, 1f)] float pelletStatUniformity = 1f;
    [SerializeField] bool projectilesCollideWithEachOther;

    [Header("Explosion")]
    [SerializeField] bool explosionOnImpact = true;
    [SerializeField] GameObject explosionPrefab;
    [SerializeField, Min(0.01f)] float explosionInitialScale = 0.5f;
    [SerializeField, Min(0.01f)] float explosionMaxScale = 4f;
    [SerializeField, Min(0.01f)] float explosionGrowthSpeed = 8f;

    [Header("Modifiers — homing")]
    [SerializeField] bool homingEnabled;
    [SerializeField, Min(0f)] float homingStartDelay = 0.5f;
    [SerializeField, Min(0f)] float homingTurnSpeedDegreesPerSec = 180f;
    [SerializeField] bool homingCrosshairRaycast = true;
    [SerializeField, Min(0.1f)] float homingRayMaxDistance = 500f;
    [SerializeField] LayerMask homingHitLayers = ~0;

    [Header("Modifiers — inherit player velocity")]
    [SerializeField] bool inheritPlayerVelocity;

    [Header("Modifiers — rapid fire")]
    [SerializeField] bool rapidFireEnabled;
    [SerializeField, Min(0.01f)] float shotsPerSecond = 6f;
    [SerializeField] bool rapidFireBurstMode;
    [SerializeField, Min(1)] int burstShotCount = 3;
    [SerializeField, Min(0f)] float burstCooldown = 0.35f;

    [Header("Modifiers — alternating hands")]
    [SerializeField] bool alternatingHandsEnabled;

    [Header("Modifiers — charge")]
    [SerializeField] bool chargeEnabled;
    [SerializeField] bool stationaryCharge;
    [SerializeField, Min(0f)] float maxChargeTime = 2f;
    [SerializeField, Min(0f)] float chargeSizeGrowthPerSecond = 0.08f;
    [SerializeField, Min(1f)] float chargeMaxScaleMultiplier = 3f;
    [SerializeField, Min(0f)] float chargeExplosionScaleBonusPerSecond = 0.15f;
    [SerializeField, Min(1f)] float chargeMaxExplosionScaleMultiplier = 2.5f;
    [SerializeField, Min(0f)] float chargeSpeedBonusPerSecond = 12f;
    [SerializeField, Min(0f)] float chargeMaxSpeedBonus = 36f;

    [Header("Modifiers — detonate")]
    [SerializeField] bool detonateOnRepeatInput;
    [SerializeField] bool detonateAllActiveProjectiles = true;
    [SerializeField] bool detonateOnLifetimeEnd;

    [Header("Windup & cooldown")]
    [SerializeField, Min(0f)] float windupDuration;
    [SerializeField] bool stationaryWindup;
    [SerializeField] bool stationaryDuringRapidFire;
    [SerializeField] bool stationaryDuringHomingProjectiles;
    [SerializeField, Min(0f)] float cooldown = 0.5f;

    bool _inWindup;
    float _windupRemaining;
    float _cooldownEndTime;

    bool _fireFromPrimaryNext = true;
    float _nextRapidFireTime;
    int _burstShotsRemaining;
    float _burstCooldownUntil;

    ProjectileWeaponEffect _chargingInstance;

    readonly List<ProjectileWeaponEffect> _detonatable = new List<ProjectileWeaponEffect>();
    readonly List<ProjectileWeaponEffect> _homingActive = new List<ProjectileWeaponEffect>();
    readonly List<Collider> _projectilePeerColliders = new List<Collider>();
    ProjectileWeaponEffect _lastDetonatableProjectile;

    ProjectileWeaponEffect _windupOrb;
    Transform _windupSpawn;

    void Awake()
    {
        if (player == null)
        {
            player = GetComponent<FPSCharacterController>();
            if (player == null)
                player = GetComponentInParent<FPSCharacterController>();
        }
        if (aimCamera == null && player != null)
            aimCamera = player.AimCamera;
    }

    public void RegisterDetonatableProjectile(ProjectileWeaponEffect p)
    {
        if (p == null || _detonatable.Contains(p))
            return;
        _detonatable.Add(p);
        _lastDetonatableProjectile = p;
    }

    public void UnregisterDetonatableProjectile(ProjectileWeaponEffect p) => _detonatable.Remove(p);

    public void RegisterHomingProjectile(ProjectileWeaponEffect p)
    {
        if (p == null || _homingActive.Contains(p))
            return;
        _homingActive.Add(p);
    }

    public void UnregisterHomingProjectile(ProjectileWeaponEffect p) => _homingActive.Remove(p);

    public void RegisterProjectilePeerCollider(Collider col)
    {
        if (projectilesCollideWithEachOther || col == null)
            return;
        for (int i = 0; i < _projectilePeerColliders.Count; i++)
        {
            var c = _projectilePeerColliders[i];
            if (c != null)
                Physics.IgnoreCollision(col, c, true);
        }
        _projectilePeerColliders.Add(col);
    }

    public void UnregisterProjectilePeerCollider(Collider col)
    {
        if (col == null)
            return;
        _projectilePeerColliders.Remove(col);
    }

    public void RunFrame(bool firePressed, bool fireHeld)
    {
        if (player == null)
            return;

        PruneNullRefs();

        bool charging = _chargingInstance != null;
        bool homingAlive = HasHomingAlive();

        player.SetAbilityStationaryWindup(
            (stationaryWindup && _inWindup)
            || (stationaryCharge && charging)
            || (stationaryDuringRapidFire && rapidFireEnabled && fireHeld)
            || (stationaryDuringHomingProjectiles && homingAlive));

        float dt = Time.deltaTime;

        if (charging)
        {
            TryDetonateFromInput(firePressed);
            TickCharge(dt, fireHeld);
            return;
        }

        if (_inWindup)
        {
            _windupRemaining -= dt;
            if (_windupOrb != null)
                _windupOrb.RefreshWindupFacing(GetAimCamera());
            if (_windupRemaining <= 0f)
            {
                _inWindup = false;
                DestroyWindupOrb();
                Transform spawn = _windupSpawn != null ? _windupSpawn : handSpawnPoint;
                _windupSpawn = null;
                SpawnVolley(spawn, GetAimCamera(), projectileMaxSpeed, projectileScale, 1f);
                _cooldownEndTime = Time.time + cooldown;
            }
            return;
        }

        if (TryDetonateFromInput(firePressed))
            return;

        if (chargeEnabled && fireHeld && !rapidFireEnabled)
        {
            if (CanStartNewAction())
                StartCharge();
            return;
        }

        if (rapidFireEnabled && fireHeld)
        {
            TickRapidFire(dt);
            return;
        }

        if (Time.time < _cooldownEndTime)
            return;

        if (!firePressed)
            return;

        if (handSpawnPoint == null || projectilePrefab == null)
            return;

        if (windupDuration <= 0f)
        {
            SpawnOneVolley();
            _cooldownEndTime = Time.time + cooldown;
        }
        else
        {
            _inWindup = true;
            _windupRemaining = windupDuration;
            _windupSpawn = PickSpawnPoint();
            StartWindupVisual(_windupSpawn);
        }
    }

    void StartWindupVisual(Transform attach)
    {
        DestroyWindupOrb();
        if (projectilePrefab == null || attach == null)
            return;

        var go = Instantiate(projectilePrefab, attach.position, Quaternion.identity);
        var fx = go.GetComponent<ProjectileWeaponEffect>();
        if (fx == null)
        {
            Destroy(go);
            return;
        }

        Transform owner = player != null ? player.transform : transform;
        fx.BeginWindupAttach(attach, owner, projectileScale);
        _windupOrb = fx;
    }

    void DestroyWindupOrb()
    {
        if (_windupOrb == null)
            return;
        Destroy(_windupOrb.gameObject);
        _windupOrb = null;
    }

    void PruneNullRefs()
    {
        for (int i = _detonatable.Count - 1; i >= 0; i--)
        {
            if (_detonatable[i] == null)
                _detonatable.RemoveAt(i);
        }
        for (int i = _homingActive.Count - 1; i >= 0; i--)
        {
            if (_homingActive[i] == null)
                _homingActive.RemoveAt(i);
        }
        for (int i = _projectilePeerColliders.Count - 1; i >= 0; i--)
        {
            if (_projectilePeerColliders[i] == null)
                _projectilePeerColliders.RemoveAt(i);
        }
    }

    bool HasHomingAlive()
    {
        for (int i = _homingActive.Count - 1; i >= 0; i--)
        {
            if (_homingActive[i] == null)
                _homingActive.RemoveAt(i);
            else
                return true;
        }
        return false;
    }

    bool CanStartNewAction() =>
        handSpawnPoint != null
        && projectilePrefab != null
        && Time.time >= _cooldownEndTime
        && !_inWindup
        && !BlocksNewShotForDetonateGate();

    bool BlocksNewShotForDetonateGate() =>
        detonateOnRepeatInput && !rapidFireEnabled && _detonatable.Count > 0;

    bool TryDetonateFromInput(bool firePressed)
    {
        if (!detonateOnRepeatInput || !firePressed || _detonatable.Count == 0)
            return false;

        if (detonateAllActiveProjectiles)
        {
            var snapshot = new List<ProjectileWeaponEffect>(_detonatable);
            foreach (var p in snapshot)
            {
                if (p != null)
                    p.Detonate();
            }
        }
        else if (_lastDetonatableProjectile != null)
            _lastDetonatableProjectile.Detonate();

        _cooldownEndTime = Time.time;
        return true;
    }

    void StartCharge()
    {
        Camera cam = GetAimCamera();
        if (cam == null)
            return;

        var go = Instantiate(projectilePrefab, handSpawnPoint.position, Quaternion.identity);
        var fx = go.GetComponent<ProjectileWeaponEffect>();
        if (fx == null)
        {
            Destroy(go);
            return;
        }

        Transform owner = player != null ? player.transform : transform;
        fx.SetOwnerForCharge(owner);
        fx.BeginCharge(
            handSpawnPoint,
            projectileScale,
            chargeSizeGrowthPerSecond,
            chargeMaxScaleMultiplier,
            maxChargeTime);

        _chargingInstance = fx;
    }

    void TickCharge(float dt, bool fireHeld)
    {
        if (_chargingInstance == null)
            return;

        _chargingInstance.TickCharge(dt);

        if (!fireHeld)
            ReleaseCharge();

        if (maxChargeTime > 0f && _chargingInstance.ChargeElapsed >= maxChargeTime - 1e-4f && fireHeld)
            ReleaseCharge();
    }

    void ReleaseCharge()
    {
        if (_chargingInstance == null)
            return;

        Camera cam = GetAimCamera();
        if (cam == null)
        {
            _chargingInstance.CancelChargeWithoutFire();
            _chargingInstance = null;
            _cooldownEndTime = Time.time + cooldown;
            return;
        }

        float baseScale = Mathf.Max(_chargingInstance.transform.localScale.x, projectileScale);
        float baseSpeed = projectileMaxSpeed + Mathf.Min(chargeMaxSpeedBonus, chargeSpeedBonusPerSecond * _chargingInstance.ChargeElapsed);
        float explMul = Mathf.Min(chargeMaxExplosionScaleMultiplier, 1f + chargeExplosionScaleBonusPerSecond * _chargingInstance.ChargeElapsed);

        Destroy(_chargingInstance.gameObject);
        _chargingInstance = null;

        SpawnVolley(handSpawnPoint, cam, baseSpeed, baseScale, explMul);

        _cooldownEndTime = Time.time + cooldown;
    }

    void TickRapidFire(float dt)
    {
        if (handSpawnPoint == null || projectilePrefab == null)
            return;

        if (Time.time < _burstCooldownUntil)
            return;

        if (rapidFireBurstMode)
        {
            if (_burstShotsRemaining <= 0)
                _burstShotsRemaining = burstShotCount;

            if (Time.time >= _nextRapidFireTime && _burstShotsRemaining > 0)
            {
                SpawnOneVolley();
                _burstShotsRemaining--;
                _nextRapidFireTime = Time.time + 1f / Mathf.Max(shotsPerSecond, 0.01f);
                if (_burstShotsRemaining <= 0)
                    _burstCooldownUntil = Time.time + burstCooldown;
            }
        }
        else
        {
            if (Time.time >= _nextRapidFireTime && Time.time >= _cooldownEndTime)
            {
                SpawnOneVolley();
                _nextRapidFireTime = Time.time + 1f / Mathf.Max(shotsPerSecond, 0.01f);
                _cooldownEndTime = Time.time;
            }
        }
    }

    Camera GetAimCamera() => aimCamera != null ? aimCamera : player != null ? player.AimCamera : null;

    void SpawnOneVolley() => SpawnVolley(PickSpawnPoint(), GetAimCamera(), projectileMaxSpeed, projectileScale, 1f);

    Transform PickSpawnPoint()
    {
        if (!alternatingHandsEnabled || offHandSpawnPoint == null)
            return handSpawnPoint;

        Transform t = _fireFromPrimaryNext ? handSpawnPoint : offHandSpawnPoint;
        _fireFromPrimaryNext = !_fireFromPrimaryNext;
        return t;
    }

    ProjectileWeaponEffect.ProjectileRuntime BuildRuntime()
    {
        Vector3 inherited = Vector3.zero;
        if (inheritPlayerVelocity && player != null)
            inherited = player.WorldVelocity;

        return new ProjectileWeaponEffect.ProjectileRuntime
        {
            AccelCurve = accelerationCurve,
            CurveSharpness = accelerationCurveSharpness,
            JerkPerSecond = jerkPerSecond,
            HomingEnabled = homingEnabled,
            HomingDelay = homingStartDelay,
            HomingTurnDegPerSec = homingTurnSpeedDegreesPerSec,
            HomingCrosshairRaycast = homingEnabled && homingCrosshairRaycast,
            HomingRayMaxDistance = homingRayMaxDistance,
            HomingHitLayers = homingHitLayers,
            AimCamera = GetAimCamera(),
            HomingTarget = null,
            InheritPlayerVelocity = inheritPlayerVelocity,
            InheritedVelocityWorld = inherited,
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
            Detonatable = detonateOnRepeatInput,
            DetonateOnLifetimeEnd = detonateOnLifetimeEnd,
            SourceWeapon = this,
            SuppressPeerProjectileCollisions = !projectilesCollideWithEachOther,
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
    }

    void ApplyShooterFeedback(Camera cam)
    {
        if (player == null || cam == null)
            return;

        if (maxRecoilHorizontalDegrees > 0f || maxRecoilVerticalDegrees > 0f)
        {
            float yaw = Random.Range(-maxRecoilHorizontalDegrees, maxRecoilHorizontalDegrees);
            float pitch = Random.Range(0f, maxRecoilVerticalDegrees);
            player.ApplyWeaponRecoil(pitch, yaw);
        }

        if (shooterKnockbackImpulse > 0f)
        {
            Vector3 back = -cam.transform.forward * shooterKnockbackImpulse;
            player.ApplyKnockbackVelocity(back);
        }
    }

    void SpawnVolley(Transform spawn, Camera cam, float baseMaxSpeed, float baseScale, float explosionScaleMultiplier)
    {
        if (spawn == null || projectilePrefab == null || cam == null)
            return;

        Transform owner = player != null ? player.transform : transform;
        int count = Mathf.Max(1, pelletCount);
        var dirs = new Vector3[count];
        FillSpreadDirections(cam, dirs);

        float explIni = explosionInitialScale * explosionScaleMultiplier;
        float explMax = explosionMaxScale * explosionScaleMultiplier;
        float u = Mathf.Clamp01(pelletStatUniformity);

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = dirs[i];
            Vector3 spawnPos = spawn.position + dir * spawnForwardOffset;

            float spMul = StatVariance(1f, u);
            float spdMul = StatVariance(1f, u);
            float accMul = StatVariance(1f, u);

            var go = Instantiate(projectilePrefab, spawnPos, Quaternion.LookRotation(dir));
            var proj = go.GetComponent<ProjectileWeaponEffect>();
            if (proj == null)
            {
                Destroy(go);
                continue;
            }

            var runtime = BuildRuntime();
            proj.InitializeProjectile(
                owner,
                dir,
                Mathf.Max(0.01f, baseMaxSpeed * spdMul),
                Mathf.Max(0f, projectileAcceleration * accMul),
                projectileLifetime,
                Mathf.Max(0.01f, baseScale * spMul),
                explosionOnImpact,
                explosionPrefab,
                explIni * spMul,
                explMax * spMul,
                explosionGrowthSpeed,
                in runtime);
        }

        ApplyShooterFeedback(cam);
    }

    static float StatVariance(float baseline, float uniformity)
    {
        if (uniformity >= 0.999f)
            return baseline;
        float spread = Mathf.Lerp(0.35f, 0f, uniformity);
        return baseline * Random.Range(1f - spread, 1f + spread);
    }

    void FillSpreadDirections(Camera cam, Vector3[] dirs)
    {
        int n = dirs.Length;
        Vector3 f = cam.transform.forward;
        if (n == 1 || spreadMode == SpreadMode.Single)
        {
            for (int i = 0; i < n; i++)
                dirs[i] = f;
            return;
        }

        Vector3 r = cam.transform.right;
        Vector3 u = cam.transform.up;
        float tx = Mathf.Clamp01(spreadTightness);
        float hMax = spreadHorizontalHalfAngleDegrees * tx;
        float vMax = spreadVerticalHalfAngleDegrees * tx;

        switch (spreadMode)
        {
            case SpreadMode.RandomCone:
                for (int i = 0; i < n; i++)
                {
                    float yaw = Random.Range(-hMax, hMax);
                    float pitch = Random.Range(-vMax, vMax);
                    dirs[i] = (cam.transform.rotation * Quaternion.Euler(pitch, yaw, 0f)) * Vector3.forward;
                    if (dirs[i].sqrMagnitude < 1e-6f)
                        dirs[i] = f;
                    else
                        dirs[i].Normalize();
                }
                break;

            case SpreadMode.FixedHorizontalFan:
                if (n == 1)
                {
                    dirs[0] = f;
                    break;
                }
                for (int i = 0; i < n; i++)
                {
                    float t = n == 1 ? 0f : (i / (float)(n - 1)) * 2f - 1f;
                    float yaw = t * hMax;
                    dirs[i] = (cam.transform.rotation * Quaternion.Euler(0f, yaw, 0f)) * Vector3.forward;
                    dirs[i].Normalize();
                }
                break;

            case SpreadMode.FixedRing:
                for (int i = 0; i < n; i++)
                {
                    float ang = (360f / n) * i * Mathf.Deg2Rad;
                    float cos = Mathf.Cos(ang);
                    float sin = Mathf.Sin(ang);
                    Vector3 offset = (r * cos + u * sin) * Mathf.Tan(hMax * Mathf.Deg2Rad);
                    dirs[i] = (f + offset).normalized;
                }
                break;

            default:
                for (int i = 0; i < n; i++)
                    dirs[i] = f;
                break;
        }
    }
}
