using UnityEngine;

/// <summary>
/// One runway per instance: spawn point, approach hold, and pad center for that jet only.
/// Place multiple pads in the scene for multiple jets (separate runways). <see cref="transform.forward"/> (XZ) = rollout / landing direction.
/// </summary>
[DisallowMultipleComponent]
public class EnemyJetRunwayPad : MonoBehaviour
{
    [Header("Spawn (this jet only)")]
    [Tooltip("Where the jet appears. If unset, uses this object’s position.")]
    [SerializeField] Transform spawnPoint;
    [SerializeField] GameObject jetPrefab;
    [SerializeField] bool spawnJetOnPlay = true;
    [SerializeField] LayerMask spawnGroundLayers = ~0;
    [SerializeField, Min(0f)] float spawnGroundClearance = 0.35f;

    [Header("Landing markers (this runway only)")]
    [Tooltip("Put this in the AIR (downwind / base turn). Final glide still aims at the runway on the ground.")]
    [SerializeField] Transform approachHoldPoint;
    [Tooltip("Touchdown footprint center (XZ) for “on pad”. If unset, uses this object’s position.")]
    [SerializeField] Transform resupplyCenterPoint;

    [Tooltip("Gizmo / rough pad size only. Actual resupply trigger uses Resupply Trigger Radius at the taxi stop.")]
    [SerializeField, Min(0.5f)] float horizontalArrivalRadius = 14f;
    [Tooltip("XZ distance from Taxi Stop (spawn when set) to count as “on the resupply point”. Needs to cover final taxi wobble; ~5–8 m typical.")]
    [SerializeField, Min(0.25f)] float resupplyTriggerRadius = 6f;
    [SerializeField, Min(0.5f)] float verticalArrivalSlack = 8f;

    public float HorizontalArrivalRadius => horizontalArrivalRadius;
    public float ResupplyTriggerRadius => resupplyTriggerRadius;
    public float VerticalArrivalSlack => verticalArrivalSlack;

    public Vector3 WorldCenter
    {
        get
        {
            Vector3 p = resupplyCenterPoint != null ? resupplyCenterPoint.position : transform.position;
            return p;
        }
    }

    public Vector3 ResupplyCenterXZ
    {
        get
        {
            Vector3 p = WorldCenter;
            return new Vector3(p.x, 0f, p.z);
        }
    }

    /// <summary>XZ the jet taxis toward on the ground (spawn / hangar cube). Uses spawn point when set, else pad center.</summary>
    public Vector3 TaxiStopXZ
    {
        get
        {
            if (spawnPoint != null)
            {
                Vector3 s = spawnPoint.position;
                return new Vector3(s.x, 0f, s.z);
            }

            return ResupplyCenterXZ;
        }
    }

    public Vector3 RunwayForwardPlanar
    {
        get
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }
    }

    void Start()
    {
        if (!spawnJetOnPlay || jetPrefab == null)
            return;

        if (!TryFindJetOnPrefab(jetPrefab, out _))
        {
            Debug.LogError(
                $"{nameof(EnemyJetRunwayPad)} on '{name}': {nameof(jetPrefab)} must contain an {nameof(EnemyJet)} (root recommended).",
                this);
            return;
        }

        SpawnJet();
    }

    static bool TryFindJetOnPrefab(GameObject prefab, out EnemyJet jet)
    {
        jet = prefab.GetComponentInChildren<EnemyJet>(true);
        return jet != null;
    }

    void SpawnJet()
    {
        Transform sp = spawnPoint != null ? spawnPoint : transform;
        Vector3 p = sp.position;
        if (spawnGroundLayers.value != 0
            && Physics.Raycast(p + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 260f, spawnGroundLayers, QueryTriggerInteraction.Ignore))
            p.y = hit.point.y + spawnGroundClearance;

        GameObject instance = Instantiate(jetPrefab, p, Quaternion.identity);
        if (!TryFindJetOnPrefab(instance, out EnemyJet jet))
        {
            Debug.LogError($"{nameof(EnemyJetRunwayPad)} on '{name}': spawned instance has no {nameof(EnemyJet)}.", this);
            Destroy(instance);
            return;
        }

        if (jet.transform != instance.transform)
            Debug.LogWarning(
                $"{nameof(EnemyJet)} is on '{jet.name}' but prefab root is '{instance.name}'. Put {nameof(EnemyJet)} on the root so flight moves the whole prefab.",
                jet);

        jet.BindRunwayPad(this);
        jet.AfterSnapFromRunwayPad(this);
    }

    public bool TryGetTouchdownOnGround(LayerMask groundLayers, out Vector3 touchdownWorld, float rayStartHeight = 120f)
    {
        touchdownWorld = WorldCenter;
        Vector3 o = WorldCenter + Vector3.up * rayStartHeight;
        if (Physics.Raycast(o, Vector3.down, out RaycastHit hit, rayStartHeight + 50f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            touchdownWorld = hit.point;
            return true;
        }

        return false;
    }

    public bool IsWithinRunwayFootprintXZ(Vector3 worldPosition)
    {
        Vector2 a = new Vector2(worldPosition.x, worldPosition.z);
        Vector2 b = new Vector2(TaxiStopXZ.x, TaxiStopXZ.z);
        return Vector2.Distance(a, b) <= resupplyTriggerRadius;
    }

    public bool TryGetApproachHoldWorld(LayerMask groundLayers, float heightAboveGround, out Vector3 world)
    {
        world = default;
        if (approachHoldPoint == null)
            return false;

        Vector3 pt = approachHoldPoint.position;
        if (groundLayers.value != 0
            && Physics.Raycast(pt + Vector3.up * 120f, Vector3.down, out RaycastHit hit, 280f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            float groundY = hit.point.y;
            // Transform clearly placed in the air: keep its Y. On/near ground: use height above terrain.
            if (pt.y >= groundY + 2.5f)
                world = pt;
            else
                world = new Vector3(pt.x, groundY + Mathf.Max(0.5f, heightAboveGround), pt.z);
        }
        else
            world = pt;

        return true;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 c = WorldCenter;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(c, horizontalArrivalRadius);
        Gizmos.DrawLine(c + Vector3.up * verticalArrivalSlack, c - Vector3.up * verticalArrivalSlack);
        Vector3 rw = RunwayForwardPlanar;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawRay(c + Vector3.up * 0.05f, rw * Mathf.Max(horizontalArrivalRadius, 8f));

        Vector3 stop = TaxiStopXZ;
        Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.9f);
        Gizmos.DrawWireSphere(stop + Vector3.up * 0.05f, resupplyTriggerRadius);
    }
#endif
}
