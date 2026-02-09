using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Weapon that applies damage using limb hit probabilities (random limb selection). Use for guns, projectiles, etc.
/// WeaponOneHanded does not use this; it uses contact-based damage instead.
/// </summary>
public class RangedWeapon : Weapon
{
    [Header("Limb Hit Probabilities")]
    [Tooltip("List of limb hit probabilities that define where this weapon can hit")]
    public List<LimbHitProbability> hitProbabilities = new List<LimbHitProbability>();

    public override float TakeDamageWithWeapon(ProceduralCharacterController target)
    {
        if (target == null)
            return 0f;
        return target.TakeDamageWithProbability(baseDamage, hitProbabilities);
    }

    public static void NormalizeProbabilities(List<LimbHitProbability> probabilities)
    {
        if (probabilities == null || probabilities.Count == 0)
            return;

        float total = 0f;
        foreach (var prob in probabilities)
            total += prob.hitChance;

        if (total > 0f && total != 100f)
        {
            float scale = 100f / total;
            foreach (var prob in probabilities)
                prob.hitChance *= scale;
        }
    }

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
