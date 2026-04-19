using UnityEngine;

/// <summary>
/// Projectile, charging orb, or explosion instance for <see cref="ProjectileWeapon"/>.
/// </summary>
[DisallowMultipleComponent]
public class ProjectileWeaponEffect : MonoBehaviour
{
    public enum AccelerationCurve
    {
        Linear,
        Exponential,
        Jerk,
        Logarithmic
    }

    enum Mode
    {
        None,
        Charging,
        WindupArm,
        Projectile,
        Explosion
    }

    [Header("Explosion only (visual prefab)")]
    [SerializeField, Min(0.01f)] float destroyDelayAfterMax = 0.35f;

    Mode _mode;

    Transform _chargeAttach;
    float _chargeBaseScale;
    float _chargeScalePerSecond;
    float _chargeMaxScaleMultiplier = 3f;
    float _chargeMaxDuration;
    float _chargeElapsed;

    Transform _ownerRoot;
    Vector3 _direction;
    float _maxSpeed;
    float _acceleration;
    float _lifetime;
    bool _explodeOnImpact;
    GameObject _explosionPrefab;
    float _explosionInitialScale;
    float _explosionMaxScale;
    float _explosionGrowthSpeed;
    float _currentSpeed;
    float _lifeRemaining;

    AccelerationCurve _accelCurve;
    float _curveSharpness;
    float _jerkPerSecond;
    float _jerkAccumulator;
    float _logTime;

    bool _homingEnabled;
    float _homingDelay;
    float _homingTurnDegPerSec;
    Camera _aimCamera;
    Transform _homingTarget;
    float _homingTimer;
    bool _homingCrosshairRaycast;
    float _homingRayMaxDistance;
    LayerMask _homingHitLayers;

    Vector3 _inheritedVelocity;
    bool _inheritVelocity;

    bool _useProjectileGravity;
    float _projectileGravityAccel;
    Vector3 _gravityVelocity;
    /// <summary>When gravity is on and homing is off, motion is one integrated world velocity (true ballistics).</summary>
    bool _ballisticMode;
    Vector3 _ballisticVelocity;

    bool _bounceEnabled;
    int _bounceRemaining;
    float _bounceDamping;

    float _impactKnockbackImpulse;
    float _impactKnockbackRadius;
    float _impactKnockbackFalloff;
    float _explosionKnockbackImpulse;
    float _explosionKnockbackRadius;
    float _explosionKnockbackFalloff;
    LayerMask _knockbackLayers;

    bool _detonatable;
    ProjectileWeapon _sourceWeapon;
    bool _detonateOnLifetimeEnd;
    bool _suppressPeerProjectileCollisions;
    Collider _projectileCollider;
    bool _registeredPeerCollider;

    Vector3 _spawnPosition;
    float _impactDamage;
    float _critChance;
    float _critDamageMultiplier;
    float _midairDamageMultiplier;
    float _damageTravelFalloffDistance;
    float _damageTravelFalloffMinMultiplier;
    LayerMask _damageLayers;

    float _explosionDamage;
    float _explosionDamageRadius;
    float _explosionDamageFalloff;
    bool _explosionDamageApplied;

    float _explosionFrom;
    float _explosionTo;
    float _explosionGrowSpeed;
    bool _explosionKnockbackApplied;

    bool _initialized;

    public float ChargeElapsed => _chargeElapsed;
    public float ChargeMaxDuration => _chargeMaxDuration;

    public struct ProjectileRuntime
    {
        public AccelerationCurve AccelCurve;
        public float CurveSharpness;
        public float JerkPerSecond;
        public bool HomingEnabled;
        public float HomingDelay;
        public float HomingTurnDegPerSec;
        public bool HomingCrosshairRaycast;
        public float HomingRayMaxDistance;
        public LayerMask HomingHitLayers;
        public Camera AimCamera;
        /// <summary>When homing is on and this is set, missiles steer toward this transform. Use when there is no player aim camera (e.g. AI).</summary>
        public Transform HomingTarget;
        public bool InheritPlayerVelocity;
        public Vector3 InheritedVelocityWorld;
        public bool UseProjectileGravity;
        public float ProjectileGravity;
        public bool BounceEnabled;
        public int MaxBounces;
        public float BounceVelocityDamping;
        public float ImpactKnockbackImpulse;
        public float ImpactKnockbackRadius;
        public float ImpactKnockbackFalloff;
        public float ExplosionKnockbackImpulse;
        public float ExplosionKnockbackRadius;
        public float ExplosionKnockbackFalloff;
        public LayerMask KnockbackLayers;
        public bool Detonatable;
        public bool DetonateOnLifetimeEnd;
        public ProjectileWeapon SourceWeapon;
        public bool SuppressPeerProjectileCollisions;
        public float ImpactDamage;
        [Range(0f, 1f)] public float CritChance;
        public float CritDamageMultiplier;
        public float MidairDamageMultiplier;
        [Tooltip("0 = no travel falloff. At this travel distance, damage reaches the min multiplier.")]
        public float DamageTravelFalloffDistance;
        [Tooltip("Damage multiplier at max travel (0–1).")]
        public float DamageTravelFalloffMinMultiplier;
        public LayerMask DamageLayers;
        public float ExplosionDamage;
        public float ExplosionDamageRadius;
        public float ExplosionDamageFalloff;

        public static ProjectileRuntime Default => new ProjectileRuntime
        {
            AccelCurve = AccelerationCurve.Linear,
            CurveSharpness = 3f,
            JerkPerSecond = 1.5f,
            HomingEnabled = false,
            HomingDelay = 0.5f,
            HomingTurnDegPerSec = 180f,
            HomingCrosshairRaycast = false,
            HomingRayMaxDistance = 500f,
            HomingHitLayers = ~0,
            AimCamera = null,
            HomingTarget = null,
            InheritPlayerVelocity = false,
            InheritedVelocityWorld = Vector3.zero,
            UseProjectileGravity = false,
            ProjectileGravity = 32f,
            BounceEnabled = false,
            MaxBounces = 0,
            BounceVelocityDamping = 0.85f,
            ImpactKnockbackImpulse = 0f,
            ImpactKnockbackRadius = 0f,
            ImpactKnockbackFalloff = 1f,
            ExplosionKnockbackImpulse = 0f,
            ExplosionKnockbackRadius = 0f,
            ExplosionKnockbackFalloff = 1f,
            KnockbackLayers = ~0,
            Detonatable = false,
            DetonateOnLifetimeEnd = false,
            SourceWeapon = null,
            SuppressPeerProjectileCollisions = false,
            ImpactDamage = 0f,
            CritChance = 0f,
            CritDamageMultiplier = 2f,
            MidairDamageMultiplier = 1f,
            DamageTravelFalloffDistance = 0f,
            DamageTravelFalloffMinMultiplier = 0.35f,
            DamageLayers = ~0,
            ExplosionDamage = 0f,
            ExplosionDamageRadius = 0f,
            ExplosionDamageFalloff = 1f
        };
    }

    public void BeginWindupAttach(Transform attachWorld, Transform ownerRoot, float uniformScale)
    {
        _mode = Mode.WindupArm;
        _ownerRoot = ownerRoot;
        _initialized = true;

        transform.SetParent(attachWorld, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * Mathf.Max(uniformScale, 0.01f);

        foreach (var col in GetComponents<SphereCollider>())
            col.enabled = false;
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
            rb.detectCollisions = false;
    }

    public void RefreshWindupFacing(Camera aimCamera)
    {
        if (_mode != Mode.WindupArm || aimCamera == null)
            return;
        transform.rotation = Quaternion.LookRotation(aimCamera.transform.forward);
    }

    public void BeginCharge(
        Transform attachWorld,
        float baseUniformScale,
        float scaleGrowthPerSecond,
        float maxScaleMultiplier,
        float maxChargeSeconds)
    {
        _mode = Mode.Charging;
        _chargeAttach = attachWorld;
        _chargeBaseScale = Mathf.Max(baseUniformScale, 0.01f);
        _chargeScalePerSecond = Mathf.Max(0f, scaleGrowthPerSecond);
        _chargeMaxScaleMultiplier = Mathf.Max(1f, maxScaleMultiplier);
        _chargeMaxDuration = Mathf.Max(0f, maxChargeSeconds);
        _chargeElapsed = 0f;
        _initialized = true;

        transform.SetParent(attachWorld, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * _chargeBaseScale;

        foreach (var col in GetComponents<SphereCollider>())
            col.enabled = false;
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
            rb.detectCollisions = false;
    }

    public void TickCharge(float dt)
    {
        if (_mode != Mode.Charging || !_initialized)
            return;

        _chargeElapsed += dt;
        if (_chargeMaxDuration > 0f)
            _chargeElapsed = Mathf.Min(_chargeElapsed, _chargeMaxDuration);

        float maxScale = _chargeBaseScale * _chargeMaxScaleMultiplier;
        float next = transform.localScale.x + _chargeScalePerSecond * dt;
        next = Mathf.Min(next, maxScale);
        transform.localScale = Vector3.one * Mathf.Max(next, _chargeBaseScale);
    }

    public void CancelChargeWithoutFire()
    {
        if (_mode != Mode.Charging)
            return;
        Destroy(gameObject);
    }

    public void EndChargeAndLaunch(
        Vector3 worldFireDirection,
        float maxSpeed,
        float acceleration,
        float lifetime,
        float uniformScale,
        bool explosionOnImpact,
        GameObject explosionPrefab,
        float explosionInitialScale,
        float explosionMaxScale,
        float explosionGrowthSpeed,
        in ProjectileRuntime runtime)
    {
        if (_mode != Mode.Charging)
            return;

        transform.SetParent(null, true);
        Vector3 dir = worldFireDirection.sqrMagnitude > 0.0001f ? worldFireDirection.normalized : Vector3.forward;
        transform.rotation = Quaternion.LookRotation(dir);

        foreach (var col in GetComponents<SphereCollider>())
            col.enabled = true;
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
            rb.detectCollisions = true;

        InitializeProjectile(
            _ownerRoot,
            dir,
            maxSpeed,
            acceleration,
            lifetime,
            uniformScale,
            explosionOnImpact,
            explosionPrefab,
            explosionInitialScale,
            explosionMaxScale,
            explosionGrowthSpeed,
            in runtime);
    }

    public void SetOwnerForCharge(Transform ownerRoot) => _ownerRoot = ownerRoot;

    public void InitializeProjectile(
        Transform ownerRoot,
        Vector3 worldDirection,
        float maxSpeed,
        float acceleration,
        float lifetime,
        float uniformScale,
        bool explosionOnImpact,
        GameObject explosionPrefab,
        float explosionInitialScale,
        float explosionMaxScale,
        float explosionGrowthSpeed,
        in ProjectileRuntime runtime)
    {
        _mode = Mode.Projectile;
        _ownerRoot = ownerRoot;
        _direction = worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : Vector3.forward;
        _maxSpeed = Mathf.Max(maxSpeed, 0.01f);
        _acceleration = acceleration;
        _lifetime = Mathf.Max(lifetime, 0.01f);
        _explodeOnImpact = explosionOnImpact;
        _explosionPrefab = explosionPrefab;
        _explosionInitialScale = explosionInitialScale;
        _explosionMaxScale = explosionMaxScale;
        _explosionGrowthSpeed = explosionGrowthSpeed;

        transform.localScale = Vector3.one * Mathf.Max(uniformScale, 0.01f);
        transform.rotation = Quaternion.LookRotation(_direction);

        _lifeRemaining = _lifetime;
        _jerkAccumulator = 0f;
        _logTime = 0f;
        _homingTimer = 0f;
        _gravityVelocity = Vector3.zero;
        _ballisticMode = false;
        _ballisticVelocity = Vector3.zero;

        _accelCurve = runtime.AccelCurve;
        _curveSharpness = Mathf.Max(0.01f, runtime.CurveSharpness);
        _jerkPerSecond = Mathf.Max(0f, runtime.JerkPerSecond);
        _homingEnabled = runtime.HomingEnabled;
        _homingDelay = Mathf.Max(0f, runtime.HomingDelay);
        _homingTurnDegPerSec = Mathf.Max(0f, runtime.HomingTurnDegPerSec);
        _aimCamera = runtime.AimCamera;
        _homingTarget = runtime.HomingTarget;
        _homingCrosshairRaycast = runtime.HomingCrosshairRaycast;
        _homingRayMaxDistance = Mathf.Max(0.1f, runtime.HomingRayMaxDistance);
        _homingHitLayers = runtime.HomingHitLayers;
        _inheritVelocity = runtime.InheritPlayerVelocity;
        _inheritedVelocity = runtime.InheritedVelocityWorld;

        _useProjectileGravity = runtime.UseProjectileGravity;
        _projectileGravityAccel = Mathf.Max(0f, runtime.ProjectileGravity);
        _ballisticMode = _useProjectileGravity && !_homingEnabled;
        _bounceEnabled = runtime.BounceEnabled;
        _bounceRemaining = Mathf.Max(0, runtime.MaxBounces);
        _bounceDamping = Mathf.Clamp01(runtime.BounceVelocityDamping);

        _impactKnockbackImpulse = Mathf.Max(0f, runtime.ImpactKnockbackImpulse);
        _impactKnockbackRadius = Mathf.Max(0f, runtime.ImpactKnockbackRadius);
        _impactKnockbackFalloff = Mathf.Max(0.01f, runtime.ImpactKnockbackFalloff);
        _explosionKnockbackImpulse = Mathf.Max(0f, runtime.ExplosionKnockbackImpulse);
        _explosionKnockbackRadius = Mathf.Max(0f, runtime.ExplosionKnockbackRadius);
        _explosionKnockbackFalloff = Mathf.Max(0.01f, runtime.ExplosionKnockbackFalloff);
        _knockbackLayers = runtime.KnockbackLayers;

        _detonatable = runtime.Detonatable;
        _sourceWeapon = runtime.SourceWeapon;
        _detonateOnLifetimeEnd = runtime.DetonateOnLifetimeEnd;
        _suppressPeerProjectileCollisions = runtime.SuppressPeerProjectileCollisions;

        _impactDamage = Mathf.Max(0f, runtime.ImpactDamage);
        _critChance = Mathf.Clamp01(runtime.CritChance);
        _critDamageMultiplier = Mathf.Max(1f, runtime.CritDamageMultiplier);
        _midairDamageMultiplier = Mathf.Max(0f, runtime.MidairDamageMultiplier);
        _damageTravelFalloffDistance = Mathf.Max(0f, runtime.DamageTravelFalloffDistance);
        _damageTravelFalloffMinMultiplier = Mathf.Clamp(runtime.DamageTravelFalloffMinMultiplier, 0f, 1f);
        _damageLayers = runtime.DamageLayers;
        _explosionDamage = Mathf.Max(0f, runtime.ExplosionDamage);
        _explosionDamageRadius = Mathf.Max(0f, runtime.ExplosionDamageRadius);
        _explosionDamageFalloff = Mathf.Max(0.01f, runtime.ExplosionDamageFalloff);

        _initialized = true;
        _spawnPosition = transform.position;

        if (_ballisticMode)
        {
            _ballisticVelocity = _direction * _maxSpeed;
            if (_inheritVelocity)
                _ballisticVelocity += _inheritedVelocity;
            _currentSpeed = _maxSpeed;
            _inheritVelocity = false;
            _inheritedVelocity = Vector3.zero;
        }
        else
            _currentSpeed = _useProjectileGravity ? _maxSpeed : 0f;

        var col = GetComponent<SphereCollider>();
        if (col == null)
            col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        _projectileCollider = col;
        if (_suppressPeerProjectileCollisions && _sourceWeapon != null)
        {
            _registeredPeerCollider = true;
            _sourceWeapon.RegisterProjectilePeerCollider(col);
        }

        if (_detonatable && _sourceWeapon != null)
            _sourceWeapon.RegisterDetonatableProjectile(this);
        if (_homingEnabled && _sourceWeapon != null)
            _sourceWeapon.RegisterHomingProjectile(this);
    }

    public void InitializeExplosion(float initialUniformScale, float maxUniformScale, float scaleGrowthPerSecond)
    {
        _mode = Mode.Explosion;
        _explosionFrom = Mathf.Max(0.01f, initialUniformScale);
        _explosionTo = Mathf.Max(_explosionFrom, maxUniformScale);
        _explosionGrowSpeed = Mathf.Max(scaleGrowthPerSecond, 0.01f);
        transform.localScale = Vector3.one * _explosionFrom;
        _initialized = true;
    }

    public void InitializeExplosionWithKnockback(
        float initialUniformScale,
        float maxUniformScale,
        float scaleGrowthPerSecond,
        float knockbackImpulse,
        float knockbackRadius,
        float knockbackFalloff,
        LayerMask knockbackMask,
        Transform excludeRoot,
        float explosionDamage = 0f,
        float explosionDamageRadius = 0f,
        float explosionDamageFalloff = 1f,
        LayerMask explosionDamageMask = default)
    {
        InitializeExplosion(initialUniformScale, maxUniformScale, scaleGrowthPerSecond);
        _explosionKnockbackImpulse = knockbackImpulse;
        _explosionKnockbackRadius = knockbackRadius;
        _explosionKnockbackFalloff = Mathf.Max(0.01f, knockbackFalloff);
        _knockbackLayers = knockbackMask;
        _ownerRoot = excludeRoot;
        _explosionDamage = explosionDamage;
        _explosionDamageRadius = explosionDamageRadius;
        _explosionDamageFalloff = Mathf.Max(0.01f, explosionDamageFalloff);
        if (explosionDamageMask.value != 0)
            _damageLayers = explosionDamageMask;
        if (knockbackImpulse > 0f && knockbackRadius > 0f)
            ApplyExplosionKnockbackPulse();
        if (explosionDamage > 0f && explosionDamageRadius > 0f)
            ApplyExplosionDamagePulse();
    }

    void ApplyExplosionKnockbackPulse()
    {
        if (_explosionKnockbackApplied || _explosionKnockbackImpulse <= 0f || _explosionKnockbackRadius <= 0f)
            return;
        _explosionKnockbackApplied = true;
        WeaponKnockback.ApplySpherical(
            transform.position,
            _explosionKnockbackRadius,
            _explosionKnockbackImpulse,
            _explosionKnockbackFalloff,
            _knockbackLayers,
            _ownerRoot);
    }

    void ApplyExplosionDamagePulse()
    {
        if (_explosionDamageApplied || _explosionDamage <= 0f || _explosionDamageRadius <= 0f)
            return;
        _explosionDamageApplied = true;
        WeaponDamage.ApplySpherical(
            transform.position,
            _explosionDamageRadius,
            _explosionDamage,
            _explosionDamageFalloff,
            _damageLayers,
            _ownerRoot);
    }

    void OnDestroy()
    {
        if (_detonatable && _sourceWeapon != null)
            _sourceWeapon.UnregisterDetonatableProjectile(this);
        if (_homingEnabled && _sourceWeapon != null)
            _sourceWeapon.UnregisterHomingProjectile(this);
        if (_registeredPeerCollider && _projectileCollider != null && _sourceWeapon != null)
            _sourceWeapon.UnregisterProjectilePeerCollider(_projectileCollider);
    }

    public void Detonate()
    {
        if (!_initialized || _mode != Mode.Projectile)
            return;

        SpawnExplosionVisual(requireImpactFlag: false);
        Destroy(gameObject);
    }

    void SpawnExplosionVisual(bool requireImpactFlag)
    {
        if (_explosionPrefab == null)
            return;
        if (requireImpactFlag && !_explodeOnImpact)
            return;

        Vector3 p = transform.position;
        var ex = Instantiate(_explosionPrefab, p, Quaternion.identity);
        var effect = ex.GetComponent<ProjectileWeaponEffect>();
        if (effect != null)
        {
            bool knock = _explosionKnockbackImpulse > 0f && _explosionKnockbackRadius > 0f;
            bool dmg = _explosionDamage > 0f && _explosionDamageRadius > 0f;
            if (knock || dmg)
            {
                effect.InitializeExplosionWithKnockback(
                    _explosionInitialScale,
                    _explosionMaxScale,
                    _explosionGrowthSpeed,
                    knock ? _explosionKnockbackImpulse : 0f,
                    knock ? _explosionKnockbackRadius : 0f,
                    _explosionKnockbackFalloff,
                    _knockbackLayers,
                    _ownerRoot,
                    dmg ? _explosionDamage : 0f,
                    dmg ? _explosionDamageRadius : 0f,
                    _explosionDamageFalloff,
                    _damageLayers);
            }
            else
                effect.InitializeExplosion(_explosionInitialScale, _explosionMaxScale, _explosionGrowthSpeed);
        }
        else
            ex.transform.localScale = Vector3.one * _explosionInitialScale;
    }

    void Update()
    {
        if (!_initialized)
            return;

        if (_mode == Mode.Charging || _mode == Mode.WindupArm)
            return;

        if (_mode == Mode.Projectile)
            TickProjectile();
        else if (_mode == Mode.Explosion)
            TickExplosion();
    }

    Vector3 GetHomingDesiredDirection()
    {
        if (_homingTarget != null)
        {
            Vector3 to = _homingTarget.position - transform.position;
            if (to.sqrMagnitude > 1e-6f)
                return to.normalized;
            return _direction;
        }

        if (_aimCamera == null)
            return _direction;

        if (_homingCrosshairRaycast)
        {
            var ray = new Ray(_aimCamera.transform.position, _aimCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, _homingRayMaxDistance, _homingHitLayers, QueryTriggerInteraction.Ignore))
                return (hit.point - transform.position).normalized;
        }

        return _aimCamera.transform.forward;
    }

    Vector3 GetWorldMoveVelocity()
    {
        if (_ballisticMode)
            return _ballisticVelocity;

        return _direction * _currentSpeed
            + (_inheritVelocity ? _inheritedVelocity : Vector3.zero)
            + (_useProjectileGravity ? _gravityVelocity : Vector3.zero);
    }

    void AbsorbVelocityIntoHeading(Vector3 worldVel)
    {
        if (worldVel.sqrMagnitude < 1e-6f)
            return;

        if (_ballisticMode)
        {
            _ballisticVelocity = worldVel;
            _direction = worldVel.normalized;
            _currentSpeed = worldVel.magnitude;
            return;
        }

        _direction = worldVel.normalized;
        _currentSpeed = worldVel.magnitude;
        _inheritedVelocity = Vector3.zero;
        _inheritVelocity = false;
        _gravityVelocity = Vector3.zero;
    }

    void TickProjectile()
    {
        float dt = Time.deltaTime;

        if (_ballisticMode)
        {
            _ballisticVelocity += Vector3.down * _projectileGravityAccel * dt;
            Vector3 moveVel = _ballisticVelocity;
            transform.position += moveVel * dt;
            if (moveVel.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(moveVel.normalized);

            _lifeRemaining -= dt;
            if (_lifeRemaining <= 0f)
            {
                if (_detonateOnLifetimeEnd)
                    Detonate();
                else
                    Destroy(gameObject);
            }
            return;
        }

        float accel01 = Mathf.Max(_acceleration, 0.0001f);
        if (_acceleration >= 1f)
            _currentSpeed = _maxSpeed;
        else
        {
            switch (_accelCurve)
            {
                case AccelerationCurve.Linear:
                    _currentSpeed = Mathf.MoveTowards(_currentSpeed, _maxSpeed, _maxSpeed * accel01 * dt);
                    break;
                case AccelerationCurve.Exponential:
                {
                    float k = _curveSharpness * accel01;
                    _currentSpeed = Mathf.Lerp(_currentSpeed, _maxSpeed, 1f - Mathf.Exp(-k * dt));
                    break;
                }
                case AccelerationCurve.Jerk:
                    _jerkAccumulator += _jerkPerSecond * dt;
                    float jerkMul = Mathf.Min(_jerkAccumulator, 5f);
                    _currentSpeed = Mathf.MoveTowards(_currentSpeed, _maxSpeed, _maxSpeed * accel01 * jerkMul * dt);
                    break;
                case AccelerationCurve.Logarithmic:
                    _logTime += dt;
                    float logFactor = Mathf.Log(1f + _curveSharpness * _logTime);
                    _currentSpeed = Mathf.MoveTowards(_currentSpeed, _maxSpeed, _maxSpeed * accel01 * logFactor * dt);
                    break;
            }
        }

        if (_homingEnabled && (_aimCamera != null || _homingTarget != null))
        {
            _homingTimer += dt;
            if (_homingTimer >= _homingDelay)
            {
                Vector3 desired = GetHomingDesiredDirection();
                float maxRad = _homingTurnDegPerSec * Mathf.Deg2Rad * dt;
                _direction = Vector3.RotateTowards(_direction, desired, maxRad, 0f);
            }
        }

        if (_useProjectileGravity)
            _gravityVelocity += Vector3.down * _projectileGravityAccel * dt;

        Vector3 moveVel2 = GetWorldMoveVelocity();
        transform.position += moveVel2 * dt;
        if (moveVel2.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(moveVel2.normalized);

        _lifeRemaining -= dt;
        if (_lifeRemaining <= 0f)
        {
            if (_detonateOnLifetimeEnd)
                Detonate();
            else
                Destroy(gameObject);
        }
    }

    void TickExplosion()
    {
        float s = transform.localScale.x;
        s = Mathf.MoveTowards(s, _explosionTo, _explosionGrowSpeed * Time.deltaTime);
        transform.localScale = Vector3.one * s;

        if (s >= _explosionTo - 0.001f)
        {
            Destroy(gameObject, destroyDelayAfterMax);
            enabled = false;
        }
    }

    bool TryBounceOff(Collider other)
    {
        if (!_bounceEnabled || _bounceRemaining <= 0)
            return false;

        Vector3 v = GetWorldMoveVelocity();
        if (v.sqrMagnitude < 1e-6f)
            return false;

        if (!TryGetImpactSurface(other, transform.position, v, out _, out Vector3 n))
            return false;

        v = Vector3.Reflect(v, n) * _bounceDamping;
        if (v.sqrMagnitude < 0.01f)
            return false;

        AbsorbVelocityIntoHeading(v);
        _bounceRemaining--;
        transform.position += n * 0.08f;
        return true;
    }

    float ComputeImpactDamage()
    {
        if (_impactDamage <= 0f)
            return 0f;

        float d = _impactDamage;
        if (_damageTravelFalloffDistance > 0.01f)
        {
            float traveled = Vector3.Distance(transform.position, _spawnPosition);
            float t = Mathf.Clamp01(traveled / _damageTravelFalloffDistance);
            d *= Mathf.Lerp(1f, _damageTravelFalloffMinMultiplier, t);
        }

        if (_critChance > 0f && Random.value < _critChance)
            d *= _critDamageMultiplier;

        return d;
    }

    void TryApplyImpactDamage(Collider hit, Vector3 hitPoint, Vector3 hitNormal)
    {
        float d = ComputeImpactDamage();
        if (d <= 0f)
            return;

        if (((1 << hit.gameObject.layer) & _damageLayers) == 0)
            return;

        var fps = hit.GetComponent<FPSCharacterController>() ?? hit.GetComponentInParent<FPSCharacterController>();
        if (fps != null && !fps.IsGroundedForDamage && _midairDamageMultiplier > 1f)
            d *= _midairDamageMultiplier;

        var recv = hit.GetComponent<IProjectileDamageReceiver>() ?? hit.GetComponentInParent<IProjectileDamageReceiver>();
        if (recv != null)
            recv.ReceiveProjectileDamage(d, hitPoint, hitNormal, _ownerRoot);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_initialized || _mode != Mode.Projectile)
            return;
        if (_ownerRoot != null && (other.transform == _ownerRoot || other.transform.IsChildOf(_ownerRoot)))
            return;
        if (_suppressPeerProjectileCollisions
            && TryGetEffectFromCollider(other, out var peer)
            && peer._suppressPeerProjectileCollisions
            && peer._mode == Mode.Projectile
            && _sourceWeapon != null
            && ReferenceEquals(peer._sourceWeapon, _sourceWeapon))
            return;

        if (TryBounceOff(other))
            return;

        Vector3 v = GetWorldMoveVelocity();
        TryGetImpactSurface(
            other,
            transform.position,
            v.sqrMagnitude > 1e-6f ? v : -transform.forward,
            out Vector3 hitPoint,
            out Vector3 n);

        TryApplyImpactDamage(other, hitPoint, n);

        if (_impactKnockbackImpulse > 0f)
        {
            if (_impactKnockbackRadius <= 0f)
                WeaponKnockback.ApplyDirect(other, (-n).normalized * _impactKnockbackImpulse, _ownerRoot);
            else
                WeaponKnockback.ApplySpherical(transform.position, _impactKnockbackRadius, _impactKnockbackImpulse, _impactKnockbackFalloff, _knockbackLayers, _ownerRoot);
        }

        SpawnExplosionVisual(requireImpactFlag: true);
        Destroy(gameObject);
    }

    static bool ColliderSupportsBuiltInClosestPoint(Collider c)
    {
        return c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex);
    }

    static bool TryImpactFromRaycast(Collider other, Vector3 projectilePos, Vector3 incomingNormalized, out Vector3 hitPoint, out Vector3 normal)
    {
        Vector3 inc = incomingNormalized.sqrMagnitude > 1e-6f ? incomingNormalized.normalized : Vector3.forward;
        const float back = 8f;
        const float maxDist = 32f;
        Ray ray = new Ray(projectilePos - inc * back, inc);
        var hits = Physics.RaycastAll(ray, maxDist, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        hitPoint = projectilePos;
        normal = Vector3.up;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != other)
                continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                hitPoint = hits[i].point;
                normal = hits[i].normal;
                found = true;
            }
        }

        if (!found && other.Raycast(ray, out RaycastHit hit2, maxDist))
        {
            hitPoint = hit2.point;
            normal = hit2.normal;
            found = true;
        }

        if (!found)
            return false;

        if (Vector3.Dot(normal, inc) > 0f)
            normal = -normal;
        return true;
    }

    static bool TryGetImpactSurface(Collider other, Vector3 projectilePos, Vector3 incomingVelocity, out Vector3 hitPoint, out Vector3 normal)
    {
        Vector3 inc = incomingVelocity.sqrMagnitude > 1e-6f ? incomingVelocity.normalized : Vector3.down;

        if (ColliderSupportsBuiltInClosestPoint(other))
        {
            hitPoint = other.ClosestPoint(projectilePos);
            normal = (projectilePos - hitPoint).normalized;
            if (normal.sqrMagnitude > 1e-4f)
            {
                if (Vector3.Dot(normal, inc) > 0f)
                    normal = -normal;
                return true;
            }
        }

        return TryImpactFromRaycast(other, projectilePos, inc, out hitPoint, out normal);
    }

    static bool TryGetEffectFromCollider(Collider c, out ProjectileWeaponEffect fx)
    {
        fx = c != null ? c.GetComponent<ProjectileWeaponEffect>() : null;
        if (fx == null && c != null && c.attachedRigidbody != null)
            fx = c.attachedRigidbody.GetComponent<ProjectileWeaponEffect>();
        return fx != null;
    }

    static class WeaponKnockback
    {
        public static void ApplyDirect(Collider c, Vector3 deltaV, Transform excludeRoot)
        {
            if (deltaV.sqrMagnitude < 1e-8f || c == null)
                return;
            if (excludeRoot != null && (c.transform == excludeRoot || c.transform.IsChildOf(excludeRoot)))
                return;

            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
                rb.AddForce(deltaV, ForceMode.VelocityChange);

            var kb = c.GetComponent<IKnockbackVelocityReceiver>()
                ?? c.GetComponentInParent<IKnockbackVelocityReceiver>();
            if (kb != null)
                kb.ApplyKnockbackVelocity(deltaV);
        }

        public static void ApplySpherical(
            Vector3 center,
            float radius,
            float impulseMagnitude,
            float falloffExponent,
            LayerMask mask,
            Transform excludeRoot)
        {
            if (impulseMagnitude <= 0f || radius <= 0f)
                return;

            var hits = Physics.OverlapSphere(center, radius, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (excludeRoot != null && (c.transform == excludeRoot || c.transform.IsChildOf(excludeRoot)))
                    continue;

                Vector3 to = c.bounds.center - center;
                float dist = to.magnitude;
                float t = 1f - Mathf.Clamp01(dist / Mathf.Max(radius, 0.01f));
                float mag = impulseMagnitude * Mathf.Pow(t, falloffExponent);
                Vector3 dir = to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.up;
                ApplyDirect(c, dir * mag, excludeRoot);
            }
        }
    }
}
