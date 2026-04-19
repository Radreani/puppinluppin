using UnityEngine;

/// <summary>
/// Implement on a <see cref="Collider"/>'s object (or parent) to receive damage from <see cref="ProjectileWeapon"/> hits and explosions.
/// </summary>
public interface IProjectileDamageReceiver
{
    void ReceiveProjectileDamage(float damage, Vector3 hitPoint, Vector3 hitNormal, Transform damageSourceRoot);
}
