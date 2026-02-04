using UnityEngine;
using System.Collections.Generic;

public class Weapon : EquippableItem
{
    [System.Serializable]
    public class LimbHitProbability
    {
        [Tooltip("The limb type that can be hit")]
        public ProceduralCharacterController.LimbType limbType;
        
        [Tooltip("Chance to hit this limb (0-100%)")]
        [Range(0f, 100f)]
        public float hitChance = 0f;
    }
    
    [Header("Weapon Damage")]
    [Tooltip("Base damage dealt by this weapon")]
    public float baseDamage = 10f;
    
    [Header("Limb Hit Probabilities")]
    [Tooltip("List of limb hit probabilities that define where this weapon can hit")]
    public List<LimbHitProbability> hitProbabilities = new List<LimbHitProbability>();
    
    // Apply damage to a target character using this weapon's hit probabilities
    public float TakeDamageWithWeapon(ProceduralCharacterController target)
    {
        if (target == null)
            return 0f;
        
        return target.TakeDamageWithProbability(baseDamage, hitProbabilities);
    }
    
    // Normalize probabilities to ensure they sum to 100% (optional helper)
    public static void NormalizeProbabilities(List<LimbHitProbability> probabilities)
    {
        if (probabilities == null || probabilities.Count == 0)
            return;
        
        float total = 0f;
        foreach (var prob in probabilities)
        {
            total += prob.hitChance;
        }
        
        if (total > 0f && total != 100f)
        {
            float scale = 100f / total;
            foreach (var prob in probabilities)
            {
                prob.hitChance *= scale;
            }
        }
    }
    
    // Create default probabilities for a sniper rifle
    public static List<LimbHitProbability> CreateDefaultSniperProbabilities()
    {
        return new List<LimbHitProbability>
        {
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.Head, hitChance = 30f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.Torso, hitChance = 40f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.RightBicep, hitChance = 15f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.LeftBicep, hitChance = 15f }
        };
    }
    
    // Create default probabilities for a shotgun
    public static List<LimbHitProbability> CreateDefaultShotgunProbabilities()
    {
        return new List<LimbHitProbability>
        {
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.Head, hitChance = 5f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.Torso, hitChance = 60f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.RightBicep, hitChance = 10f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.RightForearm, hitChance = 10f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.RightHand, hitChance = 5f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.LeftBicep, hitChance = 10f }
        };
    }
    
    // Create default probabilities for an SMG
    public static List<LimbHitProbability> CreateDefaultSMGProbabilities()
    {
        return new List<LimbHitProbability>
        {
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.Head, hitChance = 10f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.Torso, hitChance = 50f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.RightBicep, hitChance = 8f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.RightForearm, hitChance = 6f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.RightHand, hitChance = 4f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.LeftBicep, hitChance = 8f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.LeftForearm, hitChance = 6f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.LeftHand, hitChance = 4f },
            new LimbHitProbability { limbType = ProceduralCharacterController.LimbType.Neck, hitChance = 4f }
        };
    }
}
