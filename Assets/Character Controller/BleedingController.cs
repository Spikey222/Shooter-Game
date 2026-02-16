using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Drives bleeding based on limb/torso health: the more damaged a part is, the more it bleeds.
/// Part-specific multipliers (e.g. neck 3x) control relative bleed rate. Spawns blood sprites on the ground.
/// Add this to the same GameObject as ProceduralCharacterController or to a child of the character.
/// </summary>
public class BleedingController : MonoBehaviour
{
    public enum SpawnPositionMode
    {
        AtLimb,
        AtFeet,
        Blend
    }

    [System.Serializable]
    public struct LimbBleedSetting
    {
        public ProceduralCharacterController.LimbType limbType;
        [Range(0.1f, 10f)]
        public float bleedMultiplier;
    }

    [Header("Bleed Rate")]
    [Tooltip("Per-body-part bleed multipliers (e.g. Neck 3, Torso 1.2). Missing entries use 1.")]
    public List<LimbBleedSetting> limbBleedMultipliers = new List<LimbBleedSetting>();

    [Tooltip("Global scale for bleed intensity (drops per second at full intensity)")]
    [Range(0.1f, 5f)]
    public float baseBleedRate = 1f;

    [Tooltip("Accumulator value required to spawn one blood instance. Lower = more sprites spawn.")]
    [Range(0.1f, 3f)]
    public float bleedSpawnThreshold = 0.25f;

    [Tooltip("Limb does not bleed from blunt damage when health % is above this (0-1). Blunt bleeds only when health is at or below this (heavily bashed).")]
    [Range(0f, 1f)]
    public float heavyBashBleedThreshold = 0.3f;

    [Header("Blood Visual")]
    [Tooltip("If set, this prefab is instantiated for each spawn; otherwise use Blood Sprite Variants.")]
    public GameObject bloodPrefab;

    [Tooltip("Different blood sprites (drops, splats, pools). One random variant per spawn when not using prefab.")]
    public List<Sprite> bloodSpriteVariants = new List<Sprite>();

    [Tooltip("Spawn position: at limb, at feet (torso), or blend between")]
    public SpawnPositionMode spawnPositionMode = SpawnPositionMode.AtLimb;

    [Tooltip("Offset from spawn position (e.g. slight down for ground)")]
    public Vector2 spawnOffset = Vector2.zero;

    [Tooltip("Small random offset radius so drops don't stack exactly (0 = no offset)")]
    [Range(0f, 0.5f)]
    public float spawnRandomRadius = 0.04f;

    [Tooltip("Min/max scale for spawned blood (random in range)")]
    public Vector2 bloodScaleRange = new Vector2(0.8f, 1.2f);

    [Header("Sorting")]
    [Tooltip("Sorting layer name for blood when not using character reference (e.g. Default or Ground)")]
    public string bloodSortingLayerName = "Default";

    [Tooltip("Order in layer when not using character reference. Lower = behind.")]
    public int bloodSortingOrder = -100;

    [Tooltip("Offset below character sprites (negative = behind). Used when character has a SpriteRenderer; ensures blood uses same layer and renders under.")]
    public int bloodSortingOrderOffset = -20;

    [Header("Optional: Bleed Damage Over Time")]
    [Tooltip("If true, bleeding limbs take continuous damage")]
    public bool bleedDamageOverTime = false;

    [Tooltip("Damage per second at 100%% bleed intensity")]
    [Range(0f, 20f)]
    public float bleedDamagePerSecond = 2f;

    [Tooltip("Interval in seconds between DoT applications")]
    [Range(0.1f, 1f)]
    public float bleedDoTInterval = 0.5f;

    [Header("Optional: Blood Parent")]
    [Tooltip("Parent transform for spawned blood (keeps hierarchy clean). Leave empty for scene root.")]
    public Transform bloodParent;

    [Header("Bleed Status (for UI)")]
    [Tooltip("Intensity above this = Light bleeding (for body part info display)")]
    [Range(0f, 2f)]
    public float lightBleedThreshold = 0.1f;
    [Tooltip("Intensity above this = Heavy bleeding (for body part info display)")]
    [Range(0f, 2f)]
    public float heavyBleedThreshold = 0.5f;

    [Header("Blood Level (bleed-to-death)")]
    [Tooltip("Maximum blood level. Character dies when it reaches 0.")]
    [Range(10f, 200f)]
    public float maxBloodLevel = 100f;
    [Tooltip("Blood drained per second per unit of total bleed intensity across all limbs.")]
    [Range(0.5f, 20f)]
    public float bloodDrainPerIntensityPerSecond = 0.5f;

    /// <summary>
    /// Fired when blood level changes. Parameters: current, max. Subscribe for UI updates.
    /// </summary>
    public event Action<float, float> OnBloodLevelChanged;

    /// <summary>
    /// True when character has died from blood loss (blood level reached 0).
    /// </summary>
    public bool IsDeadFromBloodLoss { get; private set; }

    private ProceduralCharacterController characterController;
    private Dictionary<ProceduralCharacterController.LimbType, float> bleedAccumulators = new Dictionary<ProceduralCharacterController.LimbType, float>();
    private float bleedDoTTimer;
    private SpriteRenderer characterSortingReference;
    private float currentBloodLevel;

    private void Awake()
    {
        characterController = GetComponent<ProceduralCharacterController>();
        if (characterController == null)
            characterController = GetComponentInParent<ProceduralCharacterController>();
        if (characterController == null)
            Debug.LogWarning("[BleedingController] No ProceduralCharacterController found.", this);
        else
            CacheCharacterSortingReference();

        if (limbBleedMultipliers == null || limbBleedMultipliers.Count == 0)
            ApplyDefaultBleedMultipliers();

        InitializeAccumulators();
        currentBloodLevel = maxBloodLevel;
    }

    private void Start()
    {
        OnBloodLevelChanged?.Invoke(currentBloodLevel, maxBloodLevel);
    }

    /// <summary>
    /// Current blood level (0 = dead from blood loss).
    /// </summary>
    public float GetCurrentBloodLevel() => currentBloodLevel;

    /// <summary>
    /// Blood level as 0-1 for UI (e.g. fill amount).
    /// </summary>
    public float GetBloodLevelPercent() => maxBloodLevel > 0f ? Mathf.Clamp01(currentBloodLevel / maxBloodLevel) : 1f;

    /// <summary>
    /// Restore blood level by the given amount (e.g. from consumables). Clamps to [0, maxBloodLevel].
    /// Does not revive if already dead from blood loss. Fires OnBloodLevelChanged for UI.
    /// </summary>
    public void RestoreBlood(float amount)
    {
        if (amount <= 0f) return;
        currentBloodLevel = Mathf.Clamp(currentBloodLevel + amount, 0f, maxBloodLevel);
        OnBloodLevelChanged?.Invoke(currentBloodLevel, maxBloodLevel);
    }

    /// <summary>
    /// Populate limb bleed multipliers with suggested defaults (Neck 3x, Head 1.5x, etc.). Call from Context menu or use Inspector.
    /// </summary>
    [ContextMenu("Apply Default Bleed Multipliers")]
    public void ApplyDefaultBleedMultipliers()
    {
        limbBleedMultipliers = new List<LimbBleedSetting>
        {
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.Neck, bleedMultiplier = 3f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.Head, bleedMultiplier = 1.5f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.Torso, bleedMultiplier = 1.2f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.RightBicep, bleedMultiplier = 1f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.RightForearm, bleedMultiplier = 1f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.RightHand, bleedMultiplier = 0.8f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.LeftBicep, bleedMultiplier = 1f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.LeftForearm, bleedMultiplier = 1f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.LeftHand, bleedMultiplier = 0.8f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.RightThigh, bleedMultiplier = 1f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.RightCalf, bleedMultiplier = 1f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.RightFoot, bleedMultiplier = 0.6f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.LeftThigh, bleedMultiplier = 1f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.LeftCalf, bleedMultiplier = 1f },
            new LimbBleedSetting { limbType = ProceduralCharacterController.LimbType.LeftFoot, bleedMultiplier = 0.6f }
        };
    }

    private void CacheCharacterSortingReference()
    {
        characterSortingReference = null;
        if (characterController == null) return;
        if (characterController.torso != null)
        {
            characterSortingReference = characterController.torso.GetComponentInChildren<SpriteRenderer>();
            if (characterSortingReference == null)
                characterSortingReference = characterController.torso.GetComponent<SpriteRenderer>();
        }
        if (characterSortingReference == null)
        {
            ProceduralLimb limb = characterController.GetLimb(ProceduralCharacterController.LimbType.Torso);
            if (limb != null)
                characterSortingReference = limb.GetComponentInChildren<SpriteRenderer>();
        }
    }

    private void InitializeAccumulators()
    {
        bleedAccumulators.Clear();
        foreach (ProceduralCharacterController.LimbType t in Enum.GetValues(typeof(ProceduralCharacterController.LimbType)))
            bleedAccumulators[t] = 0f;
    }

    private void LateUpdate()
    {
        if (characterController == null)
            return;

        if (IsDeadFromBloodLoss)
            return;

        float dt = Time.deltaTime;
        float totalBleedIntensity = 0f;

        foreach (ProceduralCharacterController.LimbType limbType in Enum.GetValues(typeof(ProceduralCharacterController.LimbType)))
        {
            float healthPercent = GetHealthPercentFor(limbType);
            if (!ShouldBleedFromLimbDamage(limbType, healthPercent))
            {
                bleedAccumulators[limbType] = 0f;
                continue;
            }
            float multiplier = GetBleedMultiplierFor(limbType);
            float intensity = (1f - healthPercent) * multiplier * baseBleedRate;

            if (intensity <= 0f)
            {
                bleedAccumulators[limbType] = 0f;
                continue;
            }

            bleedAccumulators[limbType] += intensity * dt;
            totalBleedIntensity += intensity;

            while (bleedAccumulators[limbType] >= bleedSpawnThreshold)
            {
                bleedAccumulators[limbType] -= bleedSpawnThreshold;
                Vector2 worldPos = GetSpawnPositionFor(limbType);
                SpawnBlood(worldPos, limbType);
            }
        }

        // Blood level drain: lose blood proportional to total bleed intensity
        if (totalBleedIntensity > 0f && currentBloodLevel > 0f)
        {
            float drain = totalBleedIntensity * bloodDrainPerIntensityPerSecond * dt;
            float previousBlood = currentBloodLevel;
            currentBloodLevel = Mathf.Max(0f, currentBloodLevel - drain);
            OnBloodLevelChanged?.Invoke(currentBloodLevel, maxBloodLevel);
            if (currentBloodLevel <= 0f)
            {
                IsDeadFromBloodLoss = true;
                characterController.TriggerDeathFromBloodLoss();
            }
        }

        if (bleedDamageOverTime && bleedDamagePerSecond > 0f)
        {
            bleedDoTTimer += dt;
            if (bleedDoTTimer >= bleedDoTInterval)
            {
                bleedDoTTimer = 0f;
                ApplyBleedDoT();
            }
        }
    }

    private float GetHealthPercentFor(ProceduralCharacterController.LimbType limbType)
    {
        if (limbType == ProceduralCharacterController.LimbType.Torso)
        {
            float max = characterController.GetTorsoMaxHealth();
            if (max <= 0f) return 1f;
            return characterController.GetTorsoHealth() / max;
        }
        return characterController.GetLimbHealthPercentage(limbType);
    }

    private Vector2 GetCharacterWorldPosition()
    {
        if (characterController == null)
            return Vector2.zero;
        if (characterController.torso != null)
            return characterController.torso.position;
        return characterController.transform.position;
    }

    private float GetBleedMultiplierFor(ProceduralCharacterController.LimbType limbType)
    {
        if (limbBleedMultipliers == null)
            return 1f;
        foreach (var s in limbBleedMultipliers)
        {
            if (s.limbType == limbType)
                return s.bleedMultiplier;
        }
        return 1f;
    }

    /// <summary>
    /// Returns normalized bleed intensity for a limb (0 = none, higher = more severe).
    /// Use lightBleedThreshold and heavyBleedThreshold to classify as Light/Heavy for UI display.
    /// </summary>
    public float GetBleedIntensityFor(ProceduralCharacterController.LimbType limbType)
    {
        if (characterController == null) return 0f;
        float healthPercent = GetHealthPercentFor(limbType);
        if (!ShouldBleedFromLimbDamage(limbType, healthPercent))
            return 0f;
        float multiplier = GetBleedMultiplierFor(limbType);
        float intensity = (1f - healthPercent) * multiplier * baseBleedRate;
        return intensity;
    }

    /// <summary>
    /// True when the limb is "heavily bashed": only Blunt (and optionally Generic) damage and health % at or below heavyBashBleedThreshold.
    /// Used by consumables to skip healing/stop-bleeding on such limbs.
    /// </summary>
    public bool IsLimbHeavilyBashed(ProceduralCharacterController.LimbType limb)
    {
        if (characterController == null) return false;
        float healthPercent = GetHealthPercentFor(limb);
        var types = characterController.GetDamageTypesForLimb(limb);
        bool hasStabOrSlash = types != null && types.Any(t => t == Weapon.DamageType.Stab || t == Weapon.DamageType.Slash);
        if (hasStabOrSlash) return false;
        return healthPercent <= heavyBashBleedThreshold;
    }

    private bool ShouldBleedFromLimbDamage(ProceduralCharacterController.LimbType limbType, float healthPercent)
    {
        if (characterController == null) return false;
        var damageTypesForLimb = characterController.GetDamageTypesForLimb(limbType);
        bool hasStabOrSlash = damageTypesForLimb != null && damageTypesForLimb.Any(t => t == Weapon.DamageType.Stab || t == Weapon.DamageType.Slash);
        if (hasStabOrSlash) return true;
        return healthPercent <= heavyBashBleedThreshold;
    }

    /// <summary>
    /// Stop bleeding on a specific limb (e.g. when a bandage is applied). Resets the bleed accumulator so no blood spawns until intensity is recomputed.
    /// </summary>
    public void StopBleeding(ProceduralCharacterController.LimbType limbType)
    {
        if (bleedAccumulators.ContainsKey(limbType))
            bleedAccumulators[limbType] = 0f;
    }

    private Vector2 GetSpawnPositionFor(ProceduralCharacterController.LimbType limbType)
    {
        // Use Rigidbody2D.position for physics-driven bodies to ensure we always get the current
        // world position as the character moves. transform.position can lag behind when reading
        // from Update; Rigidbody2D.position is the authoritative physics position.
        Vector2 limbPos;
        Vector2 feetPos;
        Vector2 characterWorldPos = GetCharacterWorldPosition();

        if (limbType == ProceduralCharacterController.LimbType.Torso)
        {
            limbPos = characterController.torso != null
                ? characterController.torso.position
                : characterWorldPos;
            feetPos = limbPos;
        }
        else
        {
            ProceduralLimb limb = characterController.GetLimb(limbType);
            if (limb != null)
            {
                var limbRb = limb.GetComponent<Rigidbody2D>();
                limbPos = limbRb != null ? limbRb.position : (Vector2)limb.transform.position;
            }
            else
                limbPos = characterWorldPos;
            feetPos = characterController.torso != null
                ? characterController.torso.position
                : characterWorldPos;
        }

        Vector2 pos;
        switch (spawnPositionMode)
        {
            case SpawnPositionMode.AtFeet:
                pos = feetPos;
                break;
            case SpawnPositionMode.Blend:
                pos = Vector2.Lerp(limbPos, feetPos, 0.5f);
                break;
            default:
                pos = limbPos;
                break;
        }

        pos += spawnOffset;
        if (spawnRandomRadius > 0f)
            pos += UnityEngine.Random.insideUnitCircle * spawnRandomRadius;
        return pos;
    }

    private void SpawnBlood(Vector2 worldPosition, ProceduralCharacterController.LimbType source)
    {
        float scaleToAdd = UnityEngine.Random.Range(bloodScaleRange.x, bloodScaleRange.y);
        Vector3 worldPos = new Vector3(worldPosition.x, worldPosition.y, 0f);

        if (bloodPrefab != null)
        {
            Quaternion rot = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            GameObject go = Instantiate(bloodPrefab, worldPos, rot, bloodParent);
            go.transform.position = worldPos;
            go.transform.rotation = rot;
            go.transform.localScale = new Vector3(scaleToAdd, scaleToAdd, 1f);
            ApplyBloodSorting(go);
            return;
        }

        if (bloodSpriteVariants == null || bloodSpriteVariants.Count == 0)
            return;

        Sprite sprite = bloodSpriteVariants[UnityEngine.Random.Range(0, bloodSpriteVariants.Count)];
        if (sprite == null)
            return;

        GameObject bloodGo = new GameObject("Blood");
        bloodGo.transform.position = worldPos;
        bloodGo.transform.SetParent(bloodParent, true);
        bloodGo.transform.rotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
        bloodGo.transform.localScale = new Vector3(scaleToAdd, scaleToAdd, 1f);

        SpriteRenderer renderer = bloodGo.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        ApplyBloodSorting(bloodGo);
    }

    private void ApplyBloodSorting(GameObject bloodObject)
    {
        SpriteRenderer[] renderers = bloodObject.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (SpriteRenderer sr in renderers)
        {
            if (characterSortingReference != null)
            {
                sr.sortingLayerID = characterSortingReference.sortingLayerID;
                sr.sortingOrder = characterSortingReference.sortingOrder + bloodSortingOrderOffset;
            }
            else
            {
                sr.sortingLayerName = bloodSortingLayerName;
                sr.sortingOrder = bloodSortingOrder;
            }
        }
    }

    private void ApplyBleedDoT()
    {
        foreach (ProceduralCharacterController.LimbType limbType in Enum.GetValues(typeof(ProceduralCharacterController.LimbType)))
        {
            float healthPercent = GetHealthPercentFor(limbType);
            if (!ShouldBleedFromLimbDamage(limbType, healthPercent))
                continue;
            float multiplier = GetBleedMultiplierFor(limbType);
            float intensity = (1f - healthPercent) * multiplier;
            if (intensity <= 0f)
                continue;

            float damage = bleedDamagePerSecond * bleedDoTInterval * intensity;
            if (damage > 0f)
                characterController.ApplyDamageToLimb(limbType, damage, Weapon.DamageType.Generic);
        }
    }
}
