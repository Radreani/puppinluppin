using UnityEngine;

/// <summary>
/// Applied by <see cref="ProjectileWeaponEffect"/> knockback so non-rigidbody characters (e.g. CharacterController) receive impulses.
/// </summary>
public interface IKnockbackVelocityReceiver
{
    void ApplyKnockbackVelocity(Vector3 worldDeltaVelocity);
}
