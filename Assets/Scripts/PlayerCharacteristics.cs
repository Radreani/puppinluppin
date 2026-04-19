using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Player-facing stats: health now; extend with stamina and other attributes later.
/// Receives projectile / explosion damage via <see cref="IProjectileDamageReceiver"/>.
/// Knockback uses <see cref="IKnockbackVelocityReceiver"/> on <see cref="FPSCharacterController"/>.
/// Optional ramming: damage and knockback on impact by speed, with a minimum speed gate and
/// a self velocity assist so fast impacts slow the player less than slow ones.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FPSCharacterController))]
[RequireComponent(typeof(CharacterController))]
public class PlayerCharacteristics : MonoBehaviour, IProjectileDamageReceiver
{
    [Header("Health")]
    [SerializeField, Min(1f)] float maxHealth = 100f;
    [SerializeField] bool destroyRootOnDeath;

    [Header("Ramming — damage & knockback on hit")]
    [SerializeField] bool rammingEnabled = true;
    [Tooltip("Below this speed (m/s), ramming does not apply damage, knockback, or self assist.")]
    [SerializeField, Min(0f)] float ramMinSpeed = 12f;
    [Tooltip("At this speed and above, ramming effects reach full strength (lerp from min speed).")]
    [SerializeField, Min(0.01f)] float ramFullSpeed = 45f;
    [SerializeField, Min(0f)] float ramBaseDamage;
    [Tooltip("Added damage per m/s above Ram Min Speed.")]
    [SerializeField, Min(0f)] float ramDamagePerSpeed = 1.2f;
    [Tooltip("Knockback / rigidbody impulse strength (scales with speed blend).")]
    [SerializeField, Min(0f)] float ramKnockbackImpulseScale = 0.35f;
    [SerializeField] LayerMask ramTargetLayers = ~0;
    [Tooltip("Skip ram when the surface normal is within this many degrees of world up (floors / gentle ramps).")]
    [SerializeField, Range(0f, 85f)] float ramSkipSurfaceAngleFromUp = 38f;
    [Tooltip("Seconds before the same collider can trigger ram again (avoids boost spam while sliding).")]
    [SerializeField, Min(0.01f)] float ramHitCooldown = 0.12f;

    [Header("Ramming — breach AOE (nearby chunks)")]
    [SerializeField] bool ramAoeEnabled = true;
    [Tooltip("Sphere radius base (m) plus speed-scaled term so fast rams soften the whole wall, not only the first collider.")]
    [SerializeField, Min(0f)] float ramAoeBaseRadius = 1.15f;
    [SerializeField, Min(0f)] float ramAoeRadiusPerSpeed = 0.026f;
    [Tooltip("Shifts the sphere center along travel direction so chunks ahead/in the wall take damage.")]
    [SerializeField] float ramAoeForwardAlongVelocity = 0.7f;
    [Tooltip("Other receivers in the sphere take this fraction of the primary ram damage (primary still gets full hit).")]
    [SerializeField, Range(0f, 1f)] float ramAoeDamageFraction = 0.45f;
    [Tooltip("Impulse applied to neighbors in the AOE as a fraction of the primary ram impulse.")]
    [SerializeField, Range(0f, 1f)] float ramAoeImpulseFraction = 0.4f;
    [Tooltip("Caps how often a full AOE burst runs (separate from per-collider hit cooldown).")]
    [SerializeField, Min(0.02f)] float ramAoeGlobalCooldown = 0.08f;

    [Header("Ramming — knockback rage")]
    [Tooltip("When the player receives or applies velocity impulses (knockback, self-boost), ram damage temporarily multiplies up to this value.")]
    [SerializeField] bool knockbackRageEnabled = true;
    [Tooltip("Maximum damage multiplier from accumulated knockback impulses.")]
    [SerializeField, Min(1f)] float knockbackRageMaxMultiplier = 2.5f;
    [Tooltip("Single impulse magnitude (m/s) needed to reach max multiplier (smaller impulses give proportionally less).")]
    [SerializeField, Min(1f)] float knockbackRageFullImpulse = 30f;
    [Tooltip("Seconds for the multiplier to fully decay back to 1× with no new impulses.")]
    [SerializeField, Min(0.1f)] float knockbackRageDecayTime = 3f;

    [Header("Ramming — how much the player keeps moving through obstacles")]
    [Tooltip("Extra velocity added along travel direction when ramming (0 = none). Scales with speed blend.")]
    [SerializeField, Min(0f)] float ramSelfVelocityBoostScale = 0.22f;
    [Tooltip("Cap on extra speed added per ram event (m/s).")]
    [SerializeField, Min(0f)] float ramSelfVelocityBoostMax = 14f;
    [Tooltip("Extra velocity from ramming is limited to once per this many seconds (stops huge boosts when grazing many pieces).")]
    [SerializeField, Min(0.05f)] float ramSelfBoostGlobalCooldown = 0.22f;

    [Header("Debug — play mode HUD")]
    [Tooltip("On-screen stats for tuning move speed vs ram damage (Game view).")]
    [FormerlySerializedAs("showSpeedDebug")]
    [SerializeField] bool showMovementRamHud;
    public enum DebugHudAnchor
    {
        [Tooltip("Best default: Game view zoom often crops the top-left of the image.")]
        BottomLeft = 0,
        BottomRight = 1,
        TopLeft = 2,
        TopRight = 3,
    }

    [SerializeField, Min(10f)] float debugHudFontSize = 15f;
    [SerializeField, Min(80f)] float debugHudWidth = 340f;
    [Tooltip("Inset from the screen safe area. Use ~28–48 if the Game view Scale is >1x (edges get cropped).")]
    [SerializeField, Min(0f)] float debugHudScreenInset = 36f;
    [SerializeField] DebugHudAnchor debugHudAnchor = DebugHudAnchor.BottomLeft;
    [Tooltip("Padding between panel border and text (fixes text clipped by the panel itself).")]
    [SerializeField, Min(4f)] float debugHudTextInset = 14f;

    float _currentHealth;
    FPSCharacterController _fps;
    CharacterController _characterController;
    readonly Dictionary<int, float> _lastRamTimeByColliderId = new Dictionary<int, float>(32);
    readonly Collider[] _aoeHits = new Collider[48];
    readonly HashSet<IProjectileDamageReceiver> _aoeReceiversSeen = new HashSet<IProjectileDamageReceiver>();
    float _lastRamSelfBoostTime = -999f;
    float _lastRamAoeTime = -999f;
    float _knockbackRageMultiplier = 1f;

    public float CurrentHealth => _currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthNormalized => maxHealth > 0f ? _currentHealth / maxHealth : 0f;
    public bool IsDead => _currentHealth <= 0f;

    public event Action<float, float> HealthChanged;
    public event Action Died;

    void Awake()
    {
        _currentHealth = maxHealth;
        _fps = GetComponent<FPSCharacterController>();
        _characterController = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (!knockbackRageEnabled || _knockbackRageMultiplier <= 1f)
            return;
        float decayRate = (knockbackRageMaxMultiplier - 1f) / Mathf.Max(knockbackRageDecayTime, 0.01f);
        _knockbackRageMultiplier = Mathf.Max(1f, _knockbackRageMultiplier - decayRate * Time.deltaTime);
    }

    /// <summary>
    /// Called by <see cref="FPSCharacterController"/> whenever a velocity impulse is applied to the player.
    /// Stacks the knockback rage multiplier so getting knocked around boosts ram damage.
    /// </summary>
    public void OnKnockbackReceived(float impulseMagnitude)
    {
        if (!knockbackRageEnabled || impulseMagnitude < 0.01f)
            return;
        float add = Mathf.InverseLerp(0f, knockbackRageFullImpulse, impulseMagnitude)
                    * (knockbackRageMaxMultiplier - 1f);
        _knockbackRageMultiplier = Mathf.Clamp(_knockbackRageMultiplier + add, 1f, knockbackRageMaxMultiplier);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!rammingEnabled || IsDead || _fps == null)
            return;
        if (hit.collider == null || hit.collider.isTrigger)
            return;
        if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
            return;
        if (((1 << hit.collider.gameObject.layer) & ramTargetLayers.value) == 0)
            return;
        if (Vector3.Angle(Vector3.up, hit.normal) < ramSkipSurfaceAngleFromUp)
            return;

        Vector3 v = _fps.WorldVelocity;
        float speed = v.magnitude;
        if (speed < ramMinSpeed)
            return;

        int id = hit.collider.GetInstanceID();
        if (_lastRamTimeByColliderId.TryGetValue(id, out float lastTime) && Time.time - lastTime < ramHitCooldown)
            return;

        float blend = Mathf.InverseLerp(ramMinSpeed, ramFullSpeed, speed);
        blend = Mathf.Clamp01(blend);
        float overMin = Mathf.Max(0f, speed - ramMinSpeed);

        float rage = knockbackRageEnabled ? _knockbackRageMultiplier : 1f;
        float damage = (ramBaseDamage + overMin * ramDamagePerSpeed) * blend * rage;
        Vector3 dir = v.sqrMagnitude > 1e-6f ? v.normalized : transform.forward;
        Vector3 impulse = ramKnockbackImpulseScale > 0f && blend > 0f
            ? dir * (speed * ramKnockbackImpulseScale * blend)
            : Vector3.zero;

        IProjectileDamageReceiver primaryRecv = hit.collider.GetComponent<IProjectileDamageReceiver>()
            ?? hit.collider.GetComponentInParent<IProjectileDamageReceiver>();

        if (damage > 0f)
        {
            WeaponDamage.ApplyDirect(
                hit.collider,
                damage,
                hit.point,
                hit.normal,
                transform);
        }

        if (impulse.sqrMagnitude > 1e-8f)
            ApplyRamKnockbackToCollider(hit.collider, impulse, transform);

        TryRamAoeBurst(v, speed, blend, damage, impulse, hit.collider, primaryRecv);

        if (ramSelfVelocityBoostScale > 0f && blend > 0f && v.sqrMagnitude > 1e-6f
            && Time.time - _lastRamSelfBoostTime >= ramSelfBoostGlobalCooldown)
        {
            Vector3 boost = v.normalized * (speed * ramSelfVelocityBoostScale * blend);
            float mag = boost.magnitude;
            if (mag > ramSelfVelocityBoostMax)
                boost *= ramSelfVelocityBoostMax / Mathf.Max(mag, 1e-6f);
            _fps.ApplyKnockbackVelocity(boost);
            _lastRamSelfBoostTime = Time.time;
        }

        _lastRamTimeByColliderId[id] = Time.time;
        if (_lastRamTimeByColliderId.Count > 128)
            _lastRamTimeByColliderId.Clear();
    }

    void TryRamAoeBurst(
        Vector3 velocity,
        float speed,
        float blend,
        float primaryDamage,
        Vector3 primaryImpulse,
        Collider primaryCollider,
        IProjectileDamageReceiver primaryRecv)
    {
        if (!ramAoeEnabled || primaryDamage <= 0f)
            return;
        if (Time.time - _lastRamAoeTime < ramAoeGlobalCooldown)
            return;
        if (_characterController == null)
            return;

        _lastRamAoeTime = Time.time;

        float radius = ramAoeBaseRadius + speed * ramAoeRadiusPerSpeed * blend;
        Vector3 dir = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : transform.forward;
        // Always project the full offset in travel direction (not blended) so backwards/knockback movement
        // correctly centers the burst behind the player into the surface being hit.
        Vector3 center = _characterController.bounds.center + dir * ramAoeForwardAlongVelocity;

        int count = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            _aoeHits,
            ramTargetLayers,
            QueryTriggerInteraction.Ignore);

        float neighborDamage = primaryDamage * ramAoeDamageFraction;
        Vector3 neighborImpulse = primaryImpulse * ramAoeImpulseFraction;
        _aoeReceiversSeen.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider c = _aoeHits[i];
            if (c == null || c.isTrigger || c == primaryCollider)
                continue;
            if (c.transform == transform || c.transform.IsChildOf(transform))
                continue;

            if (neighborDamage > 0f)
            {
                var recv = c.GetComponent<IProjectileDamageReceiver>()
                    ?? c.GetComponentInParent<IProjectileDamageReceiver>();
                if (recv != null && recv != primaryRecv && _aoeReceiversSeen.Add(recv))
                {
                    Vector3 pt = c.bounds.ClosestPoint(center);
                    Vector3 nrm = pt - center;
                    if (nrm.sqrMagnitude > 1e-6f)
                        nrm.Normalize();
                    else
                        nrm = dir;
                    recv.ReceiveProjectileDamage(neighborDamage, pt, nrm, transform);
                }
            }

            if (neighborImpulse.sqrMagnitude > 1e-8f)
                ApplyRamKnockbackToCollider(c, neighborImpulse, transform);
        }
    }

    static void ApplyRamKnockbackToCollider(Collider c, Vector3 deltaV, Transform excludeRoot)
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

    public void ReceiveProjectileDamage(float damage, Vector3 hitPoint, Vector3 hitNormal, Transform damageSourceRoot)
    {
        if (_currentHealth <= 0f || damage <= 0f)
            return;

        _currentHealth -= damage;
        if (_currentHealth < 0f)
            _currentHealth = 0f;

        HealthChanged?.Invoke(_currentHealth, maxHealth);

        if (_currentHealth <= 0f)
            Die();
    }

    /// <summary>Editor / pickups / regen — clamps to max.</summary>
    public void SetHealth(float value)
    {
        _currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        HealthChanged?.Invoke(_currentHealth, maxHealth);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || IsDead)
            return;
        SetHealth(_currentHealth + amount);
    }

    void Die()
    {
        Died?.Invoke();
        if (destroyRootOnDeath)
            Destroy(gameObject);
    }

    void OnGUI()
    {
        if (!showMovementRamHud || _fps == null || !Application.isPlaying)
            return;

        Rect safe = SafeAreaGuiPixels();
        float inset = Mathf.Max(0f, debugHudScreenInset);
        float maxW = Mathf.Max(100f, safe.width - inset * 2f);
        float panelW = Mathf.Clamp(debugHudWidth, 100f, maxW);

        Vector3 vel = _fps.WorldVelocity;
        float planar = new Vector3(vel.x, 0f, vel.z).magnitude;
        float speed3 = vel.magnitude;

        ComputeRamPreview(speed3, out float blend, out float damage, out float impulseMag, out float aoeRadius);

        var label = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(debugHudFontSize),
            richText = true,
            wordWrap = false,
            clipping = TextClipping.Overflow,
        };
        label.normal.textColor = new Color(0.94f, 0.94f, 0.9f);

        float ti = Mathf.Max(4f, debugHudTextInset);
        float line = Mathf.Max(17f, label.lineHeight + 2f);
        int rowCount = rammingEnabled ? (knockbackRageEnabled ? 7 : 6) : 3;
        float panelH = ti * 2f + rowCount * line;

        float maxH = Mathf.Max(60f, safe.height - inset * 2f);
        panelH = Mathf.Min(panelH, maxH);

        GetHudPanelOrigin(safe, panelW, panelH, inset, debugHudAnchor, out float panelX, out float panelY);
        var panel = new Rect(panelX, panelY, panelW, panelH);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.62f);
        GUI.Box(panel, "");
        GUI.color = prev;

        float textW = Mathf.Max(40f, panelW - ti * 2f);
        float y = panel.y + ti;
        void Row(string text)
        {
            var r = new Rect(panel.x + ti, y, textW, line + 2f);
            GUI.Label(r, text, label);
            y += line;
        }

        Row($"<b>Speed</b>  planar {planar:F1}  ·  3D {speed3:F1} m/s");
        Row($"Ram gate  min <color=#cccccc>{ramMinSpeed:F0}</color>  →  full <color=#cccccc>{ramFullSpeed:F0}</color> m/s");

        if (!rammingEnabled)
            Row("<color=#888>Ramming disabled</color>");
        else
        {
            string gate = speed3 < ramMinSpeed
                ? "<color=#ff8866>below min</color> (no ram)"
                : "<color=#8f8>eligible</color> (vertical hit)";
            Row($"Ram  blend {blend:F2}  ·  {gate}");
            Row($"Ram dmg (primary)  <b>{damage:F1}</b>");
            Row($"Ram impulse  {impulseMag:F1} m/s  (kb {ramKnockbackImpulseScale:F2})");
            if (ramAoeEnabled)
                Row($"Ram AOE r {aoeRadius:F2} m  (nb ×{ramAoeDamageFraction:F2} dmg)");
            else
                Row("<color=#888>Ram AOE off</color>");
            if (knockbackRageEnabled)
            {
                string rageCol = _knockbackRageMultiplier > 1.05f ? "#f8c" : "#888";
                Row($"KB rage  <color={rageCol}>×{_knockbackRageMultiplier:F2}</color>  (max ×{knockbackRageMaxMultiplier:F1}  decay {knockbackRageDecayTime:F1}s)");
            }
        }
    }

    static void GetHudPanelOrigin(
        Rect safe, float panelW, float panelH, float inset, DebugHudAnchor anchor,
        out float panelX, out float panelY)
    {
        float ix = Mathf.Max(inset, 8f);
        float minX = safe.xMin + ix;
        float maxX = safe.xMax - panelW - ix;
        float minY = safe.yMin + ix;
        float maxY = safe.yMax - panelH - ix;

        if (maxX < minX) maxX = minX;
        if (maxY < minY) maxY = minY;

        switch (anchor)
        {
            case DebugHudAnchor.BottomLeft:
                panelX = minX;
                panelY = maxY;
                break;
            case DebugHudAnchor.BottomRight:
                panelX = maxX;
                panelY = maxY;
                break;
            case DebugHudAnchor.TopLeft:
                panelX = minX;
                panelY = minY;
                break;
            default: // TopRight
                panelX = maxX;
                panelY = minY;
                break;
        }

        panelX = Mathf.Clamp(panelX, minX, maxX);
        panelY = Mathf.Clamp(panelY, minY, maxY);
    }

    /// <summary>Screen.safeArea uses bottom-left origin; IMGUI uses top-left.</summary>
    static Rect SafeAreaGuiPixels()
    {
        Rect s = Screen.safeArea;
        return new Rect(
            s.xMin,
            Screen.height - s.yMax,
            s.width,
            s.height);
    }

    /// <summary>
    /// Same math as <see cref="OnControllerColliderHit"/> for primary hit (wall assumed).
    /// </summary>
    void ComputeRamPreview(float speed, out float blend, out float primaryDamage, out float impulseMag, out float aoeRadius)
    {
        blend = 0f;
        primaryDamage = 0f;
        impulseMag = 0f;
        aoeRadius = 0f;

        if (!rammingEnabled || speed < ramMinSpeed)
            return;

        blend = Mathf.Clamp01(Mathf.InverseLerp(ramMinSpeed, ramFullSpeed, speed));
        float overMin = Mathf.Max(0f, speed - ramMinSpeed);
        float ragePreview = knockbackRageEnabled ? _knockbackRageMultiplier : 1f;
        primaryDamage = (ramBaseDamage + overMin * ramDamagePerSpeed) * blend * ragePreview;

        if (ramKnockbackImpulseScale > 0f && blend > 0f)
            impulseMag = speed * ramKnockbackImpulseScale * blend;

        if (ramAoeEnabled && primaryDamage > 0f)
            aoeRadius = ramAoeBaseRadius + speed * ramAoeRadiusPerSpeed * blend;
    }
}
