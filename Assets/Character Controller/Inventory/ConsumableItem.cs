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
    
    [Tooltip("Delay in seconds before this item can be used again. On each use, an audio clip plays and a radial fill (on the slot's green dot) fills in sync with this delay to show when it's ready.")]
    public float useTime = 1f;
    
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
    /// </summary>
    public bool Use(ProceduralCharacterController character)
    {
        if (character == null)
            return false;
        
        bool wasUsed = false;
        
        // Restore overall health (heal torso which affects overall health)
        if (healthRestore > 0f)
        {
            character.HealLimb(ProceduralCharacterController.LimbType.Torso, healthRestore);
            wasUsed = true;
        }
        
        // Restore limb health
        if (limbHealthRestore > 0f)
        {
            if (healAllLimbs)
            {
                character.HealAllLimbs(limbHealthRestore);
            }
            else
            {
                // Heal most damaged limbs
                ProceduralCharacterController.LimbType[] limbTypes = 
                {
                    ProceduralCharacterController.LimbType.Head,
                    ProceduralCharacterController.LimbType.Torso,
                    ProceduralCharacterController.LimbType.RightBicep,
                    ProceduralCharacterController.LimbType.LeftBicep,
                    ProceduralCharacterController.LimbType.RightForearm,
                    ProceduralCharacterController.LimbType.LeftForearm,
                    ProceduralCharacterController.LimbType.RightHand,
                    ProceduralCharacterController.LimbType.LeftHand
                };
                
                foreach (var limbType in limbTypes)
                {
                    var limb = character.GetLimb(limbType);
                    if (limb != null && limb.GetHealthPercentage() < 1f)
                    {
                        limb.Heal(limbHealthRestore);
                        wasUsed = true;
                        break; // Heal one limb at a time if not healing all
                    }
                }
            }
            wasUsed = true;
        }
        
        return wasUsed;
    }
    
    /// <summary>
    /// Use the consumable on a specific limb (e.g. after user selected a limb on the body outline).
    /// Applies healthRestore to that limb and optionally stops bleeding. Returns true if any effect was applied.
    /// </summary>
    public bool UseOnLimb(ProceduralCharacterController character, ProceduralCharacterController.LimbType limbType)
    {
        if (character == null)
            return false;
        
        bool wasUsed = false;
        
        if (healthRestore > 0f)
        {
            character.HealLimb(limbType, healthRestore);
            wasUsed = true;
        }
        
        if (limbHealthRestore > 0f)
        {
            character.HealLimb(limbType, limbHealthRestore);
            wasUsed = true;
        }
        
        if (stopsBleeding)
        {
            var bleedingController = character.GetComponent<BleedingController>();
            if (bleedingController != null)
            {
                bleedingController.StopBleeding(limbType);
                wasUsed = true;
            }
        }
        
        return wasUsed;
    }
}
