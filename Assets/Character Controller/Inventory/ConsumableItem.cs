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
    
    [Tooltip("Time in seconds it takes to use this item")]
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
}
