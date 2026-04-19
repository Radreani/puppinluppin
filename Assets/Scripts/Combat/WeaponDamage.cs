using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies radial projectile / explosion damage to <see cref="IProjectileDamageReceiver"/> on hit colliders.
/// </summary>
public static class WeaponDamage
{
    public static void ApplyDirect(
        Collider c,
        float damage,
        Vector3 hitPoint,
        Vector3 hitNormal,
        Transform damageSourceRoot)
    {
        if (damage <= 0f || c == null)
            return;

        var recv = c.GetComponent<IProjectileDamageReceiver>()
            ?? c.GetComponentInParent<IProjectileDamageReceiver>();
        if (recv == null)
            return;

        recv.ReceiveProjectileDamage(damage, hitPoint, hitNormal, damageSourceRoot);
    }

    public static void ApplySpherical(
        Vector3 center,
        float radius,
        float damageAtCenter,
        float falloffExponent,
        LayerMask mask,
        Transform excludeRoot)
    {
        if (damageAtCenter <= 0f || radius <= 0f)
            return;

        var seen = new HashSet<IProjectileDamageReceiver>();
        var hits = Physics.OverlapSphere(center, radius, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            if (excludeRoot != null && (c.transform == excludeRoot || c.transform.IsChildOf(excludeRoot)))
                continue;

            var recv = c.GetComponent<IProjectileDamageReceiver>()
                ?? c.GetComponentInParent<IProjectileDamageReceiver>();
            if (recv == null || !seen.Add(recv))
                continue;

            Vector3 sample = c.bounds.ClosestPoint(center);
            float dist = Vector3.Distance(center, sample);
            float t = 1f - Mathf.Clamp01(dist / Mathf.Max(radius, 0.01f));
            float mag = damageAtCenter * Mathf.Pow(t, falloffExponent);
            if (mag <= 0f)
                continue;

            Vector3 n = (sample - center).sqrMagnitude > 1e-6f
                ? (sample - center).normalized
                : Vector3.up;

            recv.ReceiveProjectileDamage(mag, sample, n, excludeRoot);
        }
    }
}
