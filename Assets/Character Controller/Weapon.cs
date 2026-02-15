using UnityEngine;
using System.Collections.Generic;

public class Weapon : EquippableItem
{
    /// <summary>
    /// Damage type for weapon traits and armor mitigation (Stab, Blunt, Slash, Generic for bleed/environmental).
    /// </summary>
    public enum DamageType
    {
        Stab,
        Blunt,
        Slash,
        Generic
    }

    [Header("Weapon Damage")]
    [Tooltip("Base damage dealt by this weapon")]
    public float baseDamage = 10f;

    [Tooltip("Damage type for armor mitigation and part status wording (e.g. Stab, Blunt, Slash)")]
    public DamageType damageType = DamageType.Stab;

    // Shared type for limb hit probabilities (used by RangedWeapon and ProceduralCharacterController)
    [System.Serializable]
    public class LimbHitProbability
    {
        [Tooltip("The limb type that can be hit")]
        public ProceduralCharacterController.LimbType limbType;

        [Tooltip("Chance to hit this limb (0-100%)")]
        [Range(0f, 100f)]
        public float hitChance = 0f;
    }

    /// <summary>
    /// Apply damage to a target. Override in subclasses (e.g. RangedWeapon uses hit probabilities; WeaponOneHanded applies its own contact-based damage).
    /// </summary>
    public virtual float TakeDamageWithWeapon(ProceduralCharacterController target)
    {
        if (target == null)
            return 0f;
        return 0f;
    }
}
