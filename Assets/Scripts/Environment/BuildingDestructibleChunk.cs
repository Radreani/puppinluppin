using UnityEngine;

/// <summary>
/// Fractured building piece: projectile damage, optional impact damage, HP, then switches to dynamic physics.
/// Expects a <see cref="Rigidbody"/> (kinematic) and a collider; add <see cref="BuildingDestructibleRoot"/> on the parent empty.
/// </summary>
[DisallowMultipleComponent]
public class BuildingDestructibleChunk : MonoBehaviour, IProjectileDamageReceiver
{
    [Header("Health")]
    [SerializeField, Min(1f)] float maxHealth = 100f;

    [Header("Physics break")]
    [SerializeField] bool unparentWhenBroken = true;
    [SerializeField] bool retainWorldPoseWhenUnparenting = true;
    [Tooltip("Try to make mesh collider convex when breaking. If the mesh cannot cook, add a BoxCollider manually or use a simpler collider.")]
    [SerializeField] bool tryConvexMeshOnBreak = true;
    [SerializeField, Min(0f)] float breakImpulse = 0.35f;
    [SerializeField, Min(0f)] float breakTorque = 0.15f;
    [Tooltip("Extra gravity on this body only after it breaks (1 = project default, 1.4 ≈ 40% stronger fall).")]
    [SerializeField, Min(1f)] float brokenGravityMultiplier = 1.45f;
    [Tooltip("Air resistance while falling; keep at 0 for snappy motion unless you want drag.")]
    [SerializeField, Min(0f)] float brokenLinearDrag;
    [SerializeField, Min(0f)] float brokenAngularDrag = 0.02f;
    [SerializeField] RigidbodyInterpolation brokenInterpolation = RigidbodyInterpolation.Interpolate;
    [SerializeField] CollisionDetectionMode brokenCollisionDetection = CollisionDetectionMode.ContinuousDynamic;

    [Header("Impact damage (optional)")]
    [SerializeField] bool receiveCollisionDamage;
    [SerializeField, Min(0f)] float collisionDamageMinRelativeSpeed = 6f;
    [SerializeField, Min(0f)] float collisionDamagePerUnitSpeed = 2f;

    BuildingDestructibleRoot _root;
    Rigidbody _rigidbody;
    MeshCollider _meshCollider;
    float _health;
    bool _broken;

    public float CurrentHealth => _health;
    public float MaxHealth => maxHealth;
    public bool IsIntact => !_broken;

    public Bounds WorldStructuralBounds
    {
        get
        {
            var col = GetComponent<Collider>();
            if (col != null && col.enabled)
                return col.bounds;
            var r = GetComponentInChildren<Renderer>();
            return r != null ? r.bounds : new Bounds(transform.position, Vector3.zero);
        }
    }

    void Awake()
    {
        _health = maxHealth;
        _root = GetComponentInParent<BuildingDestructibleRoot>();

        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
            _rigidbody = gameObject.AddComponent<Rigidbody>();
        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.interpolation = RigidbodyInterpolation.None;
        _meshCollider = GetComponent<MeshCollider>();
    }

    void FixedUpdate()
    {
        if (!_broken || brokenGravityMultiplier <= 1.0001f)
            return;
        _rigidbody.AddForce(Physics.gravity * (brokenGravityMultiplier - 1f), ForceMode.Acceleration);
    }

    public void ReceiveProjectileDamage(float damage, Vector3 hitPoint, Vector3 hitNormal, Transform damageSourceRoot)
    {
        ApplyDamage(damage, hitPoint, hitNormal);
    }

    /// <summary>Damage from lost structural support (same HP pool as weapons).</summary>
    public void ApplyStructuralStress(float damage)
    {
        if (damage <= 0f)
            return;
        ApplyDamage(damage, transform.position, Vector3.up);
    }

    void ApplyDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (_broken || damage <= 0f)
            return;

        _health -= damage;
        if (_health > 0f)
            return;

        _health = 0f;
        Shatter(hitPoint, hitNormal);
    }

    void Shatter(Vector3 hitPoint, Vector3 hitNormal)
    {
        if (_broken)
            return;
        _broken = true;

        Bounds intactBounds = WorldStructuralBounds;
        _root?.NotifyChunkShattered(this, intactBounds);

        if (unparentWhenBroken)
            transform.SetParent(null, retainWorldPoseWhenUnparenting);

        if (_meshCollider != null && tryConvexMeshOnBreak && !_meshCollider.convex)
            _meshCollider.convex = true;

        _rigidbody.isKinematic = false;
        _rigidbody.useGravity = true;
        _rigidbody.linearDamping = brokenLinearDrag;
        _rigidbody.angularDamping = brokenAngularDrag;
        _rigidbody.collisionDetectionMode = brokenCollisionDetection;
        _rigidbody.interpolation = brokenInterpolation;

        Vector3 push = -hitNormal.normalized * breakImpulse + Random.insideUnitSphere * (breakImpulse * 0.35f);
        _rigidbody.AddForce(push, ForceMode.Impulse);
        _rigidbody.AddTorque(Random.insideUnitSphere * breakTorque, ForceMode.Impulse);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!receiveCollisionDamage || _broken)
            return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < collisionDamageMinRelativeSpeed)
            return;

        ContactPoint contact = collision.GetContact(0);
        float dmg = (speed - collisionDamageMinRelativeSpeed) * collisionDamagePerUnitSpeed;
        ApplyDamage(dmg, contact.point, contact.normal);
    }
}
