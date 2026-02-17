using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single global manager for blood spray. Subscribe to OnAnyDamageDealt and spawn flying blood + ground stains.
/// Add once to the scene (e.g. on an empty "GameManager" or "BloodManager" object).
/// </summary>
public class BloodSprayManager : MonoBehaviour
{
    public static BloodSprayManager Instance { get; private set; }

    [Header("Flying Blood")]
    [Tooltip("Prefabs for flying droplets. If empty, uses first available BleedingController.bloodPrefab.")]
    public List<GameObject> flyingBloodPrefabs = new List<GameObject>();

    [Tooltip("Flying droplets per unit of damage (scaled by damage type)")]
    [Range(0.5f, 5f)]
    public float dropletsPerDamageUnit = 2f;

    [Tooltip("Min/max initial droplet speed (higher = more violent spray)")]
    public Vector2 spraySpeedRange = new Vector2(10f, 24f);

    [Tooltip("Spread angle in degrees (wider = more chaotic)")]
    [Range(20f, 180f)]
    public float sprayConeAngle = 90f;

    [Tooltip("Chance (0-1) for backspatter - droplets that fly opposite or sideways")]
    [Range(0f, 0.4f)]
    public float backspatterChance = 0.15f;

    [Tooltip("Min/max droplets per hit")]
    public Vector2Int dropletCountRange = new Vector2Int(10, 30);

    [Tooltip("Deceleration (speed loss per second) – higher = shorter flight, stain lands sooner")]
    [Range(3f, 25f)]
    public float dropletDeceleration = 5f;

    [Tooltip("Upward bias on some droplets (adds +Y for dramatic arcs)")]
    [Range(0f, 0.6f)]
    public float upwardBias = 0.25f;

    [Tooltip("Min/max scale for flying droplets (wide range = varied spray)")]
    public Vector2 dropletScaleRange = new Vector2(0.2f, 0.6f);

    [Tooltip("When a flying droplet finishes, spawn a stain at its landing position")]
    public bool dropletsLeaveStains = true;

    [Header("Ground Stains")]
    [Tooltip("Prefabs for ground stains. If empty, uses flying blood prefabs or first character's bloodPrefab.")]
    public List<GameObject> bloodStainPrefabs = new List<GameObject>();

    [Tooltip("Min/max stain count per hit")]
    public Vector2Int stainCountRange = new Vector2Int(3, 8);

    [Tooltip("Delay before ground stains spawn (lets flying blood have air time first)")]
    [Range(0f, 1f)]
    public float stainSpawnDelay = 0.4f;

    [Tooltip("Min/max distance from hit along spray direction")]
    public Vector2 stainDistanceRange = new Vector2(0.3f, 1.2f);

    [Tooltip("Height above floor to start fake fall")]
    [Range(0.1f, 0.6f)]
    public float stainStartHeight = 0.3f;

    [Tooltip("Duration of stain fall animation")]
    [Range(0.2f, 0.8f)]
    public float stainFallDuration = 0.5f;

    [Range(-0.5f, 0f)]
    public float stainFloorOffset = -0.2f;

    [Header("Blunt Damage")]
    [Tooltip("Blunt only flings blood when limb health % is at or below this (badly crippled). Above = no spray.")]
    [Range(0f, 1f)]
    public float bluntBleedThreshold = 0.3f;

    [Header("Damage Scaling")]
    public float stabMultiplier = 1.5f;
    public float slashMultiplier = 1.5f;
    public float bluntMultiplier = 0.5f;
    public float genericMultiplier = 0.8f;

    [Range(1f, 2f)]
    public float criticalHitMultiplier = 1.5f;

    [Header("References")]
    [Tooltip("Parent for spawned blood. Null = scene root.")]
    public Transform bloodParent;

    [Header("Debug")]
    public bool enableDebugLogs;

    [Header("Sorting")]
    public string bloodSortingLayerName = "Default";
    public int bloodSortingOrder = -100;

    private List<GameObject> cachedFlyingPrefabs;
    private List<GameObject> cachedStainPrefabs;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("BloodSprayManager");
        go.AddComponent<BloodSprayManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        ProceduralCharacterController.OnAnyDamageDealt += OnAnyDamageDealt;
        if (enableDebugLogs)
            Debug.Log("[BloodSprayManager] Subscribed to OnAnyDamageDealt (global)");
    }

    private void OnDisable()
    {
        ProceduralCharacterController.OnAnyDamageDealt -= OnAnyDamageDealt;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnAnyDamageDealt(ProceduralCharacterController victim, Vector2 hitPosition, Vector2 attackDirection,
        float actualDamage, Weapon.DamageType damageType, ProceduralCharacterController.LimbType limbType, bool isCritical)
    {
        if (enableDebugLogs)
            Debug.Log($"[BloodSprayManager] OnAnyDamageDealt | victim={victim?.name} pos={hitPosition} damage={actualDamage:F1} type={damageType} limb={limbType}");

        if (damageType == Weapon.DamageType.Blunt)
        {
            float healthPct = victim != null ? victim.GetLimbHealthPercentage(limbType) : 1f;
            if (healthPct > bluntBleedThreshold)
                return;
        }

        float mult = GetDamageTypeMultiplier(damageType);
        if (isCritical) mult *= criticalHitMultiplier;
        float intensity = actualDamage * mult;

        Vector2 sprayDir = attackDirection;
        if (sprayDir.sqrMagnitude < 0.01f)
            sprayDir = Vector2.right;

        CachePrefabs(victim);
        SpawnFlyingDroplets(hitPosition, sprayDir, intensity);
        StartCoroutine(SpawnGroundStainsDelayed(hitPosition, sprayDir, intensity));
    }

    private System.Collections.IEnumerator SpawnGroundStainsDelayed(Vector2 hitPosition, Vector2 sprayDirection, float intensity)
    {
        if (stainSpawnDelay > 0f)
            yield return new WaitForSeconds(stainSpawnDelay);
        SpawnGroundStains(hitPosition, sprayDirection, intensity);
    }

    private void CachePrefabs(ProceduralCharacterController victim)
    {
        var fromVictim = GetPrefabsFromVictim(victim);
        var fromScene = GetPrefabsFromAnyBleedingController();

        cachedFlyingPrefabs = flyingBloodPrefabs != null && flyingBloodPrefabs.Count > 0 ? flyingBloodPrefabs
            : fromVictim ?? fromScene;

        cachedStainPrefabs = bloodStainPrefabs != null && bloodStainPrefabs.Count > 0 ? bloodStainPrefabs
            : cachedFlyingPrefabs ?? fromVictim ?? fromScene;
    }

    private List<GameObject> GetPrefabsFromVictim(ProceduralCharacterController victim)
    {
        if (victim == null) return null;
        var bc = victim.GetComponent<BleedingController>();
        if (bc == null) bc = victim.GetComponentInChildren<BleedingController>();
        if (bc != null && bc.bloodPrefab != null)
        {
            var list = new List<GameObject> { bc.bloodPrefab };
            return list;
        }
        return null;
    }

    private List<GameObject> GetPrefabsFromAnyBleedingController()
    {
        var bc = FindFirstObjectByType<BleedingController>();
        if (bc != null && bc.bloodPrefab != null)
        {
            var list = new List<GameObject> { bc.bloodPrefab };
            return list;
        }
        return null;
    }

    private void ApplySortingToPrefab(GameObject go)
    {
        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            sr.sortingLayerName = bloodSortingLayerName;
            sr.sortingOrder = bloodSortingOrder;
        }
    }

    private float GetDamageTypeMultiplier(Weapon.DamageType damageType)
    {
        switch (damageType)
        {
            case Weapon.DamageType.Stab: return stabMultiplier;
            case Weapon.DamageType.Slash: return slashMultiplier;
            case Weapon.DamageType.Blunt: return bluntMultiplier;
            default: return genericMultiplier;
        }
    }

    private void SpawnFlyingDroplets(Vector2 position, Vector2 direction, float intensity)
    {
        if (cachedFlyingPrefabs == null || cachedFlyingPrefabs.Count == 0)
        {
            if (enableDebugLogs)
                Debug.LogWarning("[BloodSprayManager] No flying blood prefabs - assign flyingBloodPrefabs or ensure victims have BleedingController with bloodPrefab");
            return;
        }

        int count = Mathf.Clamp(Mathf.RoundToInt(intensity * dropletsPerDamageUnit), dropletCountRange.x, dropletCountRange.y);
        float halfCone = sprayConeAngle * 0.5f * Mathf.Deg2Rad;

        Transform parent = bloodParent;
        for (int i = 0; i < count; i++)
        {
            float angle;
            if (Random.value < backspatterChance)
            {
                angle = Mathf.Atan2(direction.y, direction.x) + (Random.value > 0.5f ? Mathf.PI : Mathf.PI * 0.5f * (Random.value > 0.5f ? 1f : -1f));
            }
            else
            {
                angle = Mathf.Atan2(direction.y, direction.x) + Random.Range(-halfCone, halfCone);
            }

            float speed = Random.Range(spraySpeedRange.x, spraySpeedRange.y) * Random.Range(0.8f, 1.4f);
            Vector2 vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            if (Random.value < upwardBias)
                vel.y += Random.Range(1f, 4f);

            GameObject prefab = cachedFlyingPrefabs[Random.Range(0, cachedFlyingPrefabs.Count)];
            if (prefab == null) continue;

            GameObject drop = Instantiate(prefab, parent);
            drop.name = "FlyingBloodDrop";
            drop.transform.position = new Vector3(position.x, position.y, 0f);
            drop.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float scale = Random.Range(dropletScaleRange.x, dropletScaleRange.y);
            drop.transform.localScale = new Vector3(scale, scale, 1f);
            ApplySortingToPrefab(drop);

            float decel = dropletDeceleration * Random.Range(0.85f, 1.15f);
            var onLand = dropletsLeaveStains ? (System.Action<Vector2>)(pos => SpawnStainAt(pos, scale)) : null;
            var fly = drop.GetComponent<FlyingBloodDrop>();
            if (fly == null) fly = drop.AddComponent<FlyingBloodDrop>();
            fly.Begin(vel, decel, onLand);
        }
    }

    /// <summary>
    /// Spawn a single stain at the given world position (e.g. where a flying droplet landed).
    /// </summary>
    public void SpawnStainAt(Vector2 worldPos, float dropletScale)
    {
        if (cachedStainPrefabs == null || cachedStainPrefabs.Count == 0) return;

        GameObject prefab = cachedStainPrefabs[Random.Range(0, cachedStainPrefabs.Count)];
        if (prefab == null) return;

        float stainScale = dropletScale * Random.Range(1.6f, 1.8f);
        Vector2 pos = worldPos;

        GameObject stain = Instantiate(prefab, bloodParent);
        stain.name = "BloodStain";
        stain.transform.position = new Vector3(pos.x, pos.y, 0f);
        stain.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        stain.transform.localScale = new Vector3(dropletScale, dropletScale, 1f);
        ApplySortingToPrefab(stain);

        var anim = stain.GetComponent<BloodDropFallAnimator>();
        if (anim == null) anim = stain.AddComponent<BloodDropFallAnimator>();
        anim.fallDuration = 0.15f;
        anim.Begin(pos, pos, dropletScale, stainScale);
    }

    private void SpawnGroundStains(Vector2 hitPosition, Vector2 sprayDirection, float intensity)
    {
        if (cachedStainPrefabs == null || cachedStainPrefabs.Count == 0)
        {
            if (enableDebugLogs)
                Debug.LogWarning("[BloodSprayManager] No stain prefabs");
            return;
        }

        int count = Mathf.Clamp(Mathf.RoundToInt(intensity * 0.5f), stainCountRange.x, stainCountRange.y);
        Transform parent = bloodParent;

        for (int i = 0; i < count; i++)
        {
            float dist = Random.Range(stainDistanceRange.x, stainDistanceRange.y);
            Vector2 perp = new Vector2(-sprayDirection.y, sprayDirection.x);
            Vector2 jitter = perp * Random.Range(-0.15f, 0.15f);
            Vector2 landing = hitPosition + sprayDirection * dist + jitter;
            Vector2 startPos = landing + Vector2.up * stainStartHeight;
            Vector2 targetPos = landing + Vector2.up * stainFloorOffset;

            GameObject prefab = cachedStainPrefabs[Random.Range(0, cachedStainPrefabs.Count)];
            if (prefab == null) continue;

            float scale = Random.Range(dropletScaleRange.x, dropletScaleRange.y);
            GameObject stain = Instantiate(prefab, parent);
            stain.name = "BloodStain";
            stain.transform.position = new Vector3(startPos.x, startPos.y, 0f);
            stain.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            stain.transform.localScale = new Vector3(scale, scale, 1f);
            ApplySortingToPrefab(stain);

            var anim = stain.GetComponent<BloodDropFallAnimator>();
            if (anim == null) anim = stain.AddComponent<BloodDropFallAnimator>();
            anim.fallDuration = stainFallDuration;
            anim.floorOffset = stainFloorOffset;
            anim.scaleMode = BloodDropFallAnimator.ScaleMode.SplatOnLand;
            anim.splatDuration = 0.08f;
            anim.Begin(startPos, targetPos);
        }
    }
}
