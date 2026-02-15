using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// Consumable items that can be used (e.g., bandages, food, medical supplies).
/// Extends Item with a Use() method that applies effects when consumed.
/// </summary>
[CreateAssetMenu(fileName = "New Consumable", menuName = "Inventory/Consumable Item")]
public class ConsumableItem : Item
{
    [Header("Consumable Settings")]
    [Tooltip("Amount of health restored when used (0 = no health restore)")]
    public float healthRestore = 0f;
    
    [Tooltip("Amount of health restored to all limbs when used")]
    public float limbHealthRestore = 0f;
    
    [Tooltip("Whether to heal all limbs equally or just the most damaged ones")]
    public bool healAllLimbs = true;
    
    [Tooltip("If true, using this item requires selecting a limb on the body outline first (click item, then click limb).")]
    public bool requiresLimbTarget = false;
    
    [Tooltip("When used on a limb, also stop bleeding on that limb (via BleedingController).")]
    public bool stopsBleeding = false;

    [Tooltip("Amount of blood level restored when used (0 = no blood restore). Applied globally; not gated by heavy-bash or laceration severity.")]
    public float bloodRestore = 0f;

    [Tooltip("When true, this consumable is stitches (or similar) and can restore health on heavy stab/slash wounds. When false, bandages only mitigate blood loss on heavy lacerations.")]
    public bool canHealHeavyLacerations = false;
    
    [Tooltip("Delay in seconds before this item can be used again. On each use, an audio clip plays and a radial fill (on the slot's green dot) fills in sync with this delay to show when it's ready.")]
    public float useTime = 1f;
    
    [Header("Consumable Audio")]
    [Tooltip("Sound played when this consumable is used. Assign here on the consumable asset. If empty, the default on Inventory UI is used.")]
    public AudioClip useSound;
    [Range(0f, 1f)]
    public float useSoundVolume = 1f;
    
    private void OnEnable()
    {
        // Set default values for consumables
        itemType = ItemType.Consumable;
        if (maxStackSize <= 1)
        {
            maxStackSize = 10; // Default stack size for consumables
        }
    }
    
    /// <summary>
    /// Use the consumable item on a character. Returns true if successfully used.
    /// Respects heavily bashed limbs and laceration severity (bandage vs stitches).
    /// </summary>
    public bool Use(ProceduralCharacterController character)
    {
        if (character == null)
            return false;

        var bleedingController = character.GetComponent<BleedingController>();
        bool wasUsed = false;

        // Blood restore is global; apply regardless of heavy-bash or laceration severity
        if (bloodRestore > 0f && bleedingController != null)
        {
            bleedingController.RestoreBlood(bloodRestore);
            wasUsed = true;
        }

        // Restore overall health (heal torso) — skip if torso is heavily bashed
        if (healthRestore > 0f)
        {
            if (bleedingController == null || !bleedingController.IsLimbHeavilyBashed(ProceduralCharacterController.LimbType.Torso))
            {
                character.HealLimb(ProceduralCharacterController.LimbType.Torso, healthRestore);
                wasUsed = true;
            }
        }

        // Restore limb health
        if (limbHealthRestore > 0f || (stopsBleeding && bleedingController != null))
        {
            var allLimbTypes = (ProceduralCharacterController.LimbType[])Enum.GetValues(typeof(ProceduralCharacterController.LimbType));

            if (healAllLimbs)
            {
                foreach (var limbType in allLimbTypes)
                {
                    if (bleedingController != null && bleedingController.IsLimbHeavilyBashed(limbType))
                        continue;
                    if (stopsBleeding && bleedingController != null)
                    {
                        bleedingController.StopBleeding(limbType);
                        wasUsed = true;
                    }
                    if (ApplyHealToLimbBySeverity(character, limbType, bleedingController))
                        wasUsed = true;
                }
            }
            else
            {
                // Heal most damaged eligible limb only
                ProceduralCharacterController.LimbType? worstLimb = null;
                float worstPercent = 1f;
                foreach (var limbType in allLimbTypes)
                {
                    if (!IsLimbEligibleForHeal(character, bleedingController, limbType, canHealHeavyLacerations))
                        continue;
                    float pct = GetLimbHealthPercent(character, limbType);
                    if (pct < 1f && pct < worstPercent)
                    {
                        worstPercent = pct;
                        worstLimb = limbType;
                    }
                }
                if (worstLimb.HasValue)
                {
                    if (stopsBleeding && bleedingController != null)
                        bleedingController.StopBleeding(worstLimb.Value);
                    if (ApplyHealToLimbBySeverity(character, worstLimb.Value, bleedingController))
                        wasUsed = true;
                }
            }
        }

        return wasUsed;
    }

    /// <summary>
    /// Applies health restore to one limb respecting laceration severity. Returns true if any heal was applied.
    /// </summary>
    private bool ApplyHealToLimbBySeverity(ProceduralCharacterController character, ProceduralCharacterController.LimbType limbType, BleedingController bleedingController)
    {
        var damageTypes = character.GetDamageTypesForLimb(limbType);
        bool hasStabOrSlash = damageTypes != null && damageTypes.Any(t => t == Weapon.DamageType.Stab || t == Weapon.DamageType.Slash);

        bool shouldHeal = true;
        if (hasStabOrSlash)
        {
            int tier = character.GetLacerationSeverityTierForLimb(limbType);
            if (tier == 0) shouldHeal = true;
            else if (tier == 1) shouldHeal = UnityEngine.Random.value < 0.5f;
            else shouldHeal = canHealHeavyLacerations;
        }

        if (!shouldHeal) return false;
        bool didHeal = false;
        if (healthRestore > 0f) { character.HealLimb(limbType, healthRestore); didHeal = true; }
        if (limbHealthRestore > 0f) { character.HealLimb(limbType, limbHealthRestore); didHeal = true; }
        return didHeal;
    }
    
    /// <summary>
    /// Use the consumable on a specific limb (e.g. after user selected a limb on the body outline).
    /// Applies healthRestore to that limb and optionally stops bleeding. Returns true if any effect was applied.
    /// Respects heavily bashed (no effect) and laceration severity (bandage vs stitches).
    /// </summary>
    public bool UseOnLimb(ProceduralCharacterController character, ProceduralCharacterController.LimbType limbType)
    {
        if (character == null)
            return false;

        var bleedingController = character.GetComponent<BleedingController>();
        bool wasUsed = false;

        // Blood restore is global; apply regardless of heavy-bash or laceration severity
        if (bloodRestore > 0f && bleedingController != null)
        {
            bleedingController.RestoreBlood(bloodRestore);
            wasUsed = true;
        }

        // Heavily bashed limbs: bandages have no effect (heal/stop-bleed skipped; blood already restored above). Still consume item.
        if (bleedingController != null && bleedingController.IsLimbHeavilyBashed(limbType))
            return true;

        var damageTypes = character.GetDamageTypesForLimb(limbType);
        bool hasStabOrSlash = damageTypes != null && damageTypes.Any(t => t == Weapon.DamageType.Stab || t == Weapon.DamageType.Slash);

        if (stopsBleeding && bleedingController != null)
        {
            bleedingController.StopBleeding(limbType);
        }

        bool shouldHeal = true;
        if (hasStabOrSlash)
        {
            int tier = character.GetLacerationSeverityTierForLimb(limbType);
            if (tier == 0)
                shouldHeal = true;
            else if (tier == 1)
                shouldHeal = UnityEngine.Random.value < 0.5f;
            else
                shouldHeal = canHealHeavyLacerations;
        }

        if (shouldHeal && healthRestore > 0f)
        {
            character.HealLimb(limbType, healthRestore);
            wasUsed = true;
        }
        if (shouldHeal && limbHealthRestore > 0f)
        {
            character.HealLimb(limbType, limbHealthRestore);
            wasUsed = true;
        }
        if (stopsBleeding)
            wasUsed = true;

        return wasUsed;
    }

    private static bool IsLimbEligibleForHeal(ProceduralCharacterController character, BleedingController bleedingController,
        ProceduralCharacterController.LimbType limbType, bool canHealHeavy)
    {
        if (bleedingController != null && bleedingController.IsLimbHeavilyBashed(limbType))
            return false;
        var damageTypes = character.GetDamageTypesForLimb(limbType);
        bool hasStabOrSlash = damageTypes != null && damageTypes.Any(t => t == Weapon.DamageType.Stab || t == Weapon.DamageType.Slash);
        if (!hasStabOrSlash) return true;
        int tier = character.GetLacerationSeverityTierForLimb(limbType);
        if (tier == 2) return canHealHeavy;
        return true;
    }

    private static float GetLimbHealthPercent(ProceduralCharacterController character, ProceduralCharacterController.LimbType limbType)
    {
        if (limbType == ProceduralCharacterController.LimbType.Torso)
        {
            float max = character.GetTorsoMaxHealth();
            if (max <= 0f) return 1f;
            return character.GetTorsoHealth() / max;
        }
        return character.GetLimbHealthPercentage(limbType);
    }
}
