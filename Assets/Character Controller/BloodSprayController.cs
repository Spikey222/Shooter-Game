using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Subscribes to OnDamageDealt and spawns blood particle burst, ground stains (with fake fall), and optional character stain.
/// Add to the same GameObject as ProceduralCharacterController or a child (on the victim).
/// </summary>
public class BloodSprayController : MonoBehaviour
{
    [Header("Particle Burst")]
    [Tooltip("Prefab with ParticleSystem for flying blood. If null, particles are skipped (stains only).")]
    public GameObject bloodParticlePrefab;

    [Tooltip("Particles to emit per unit of damage (scaled by damage type multiplier)")]
    [Range(0.5f, 5f)]
    public float particlesPerDamageUnit = 2f;

    [Tooltip("Min/max initial particle speed")]
    public Vector2 spraySpeedRange = new Vector2(2f, 5f);

    [Tooltip("Spread angle in degrees (cone around spray direction)")]
    [Range(10f, 120f)]
    public float sprayConeAngle = 60f;

    [Tooltip("Min/max particles per burst (clamped)")]
    public Vector2Int particleCountRange = new Vector2Int(3, 25);

    [Header("Ground Stains")]
    [Tooltip("Blood sprites for stains. If empty, uses BleedingController.bloodSpriteVariants.")]
    public List<Sprite> bloodStainSprites = new List<Sprite>();

    [Tooltip("Min/max stain count per hit")]
    public Vector2Int stainCountRange = new Vector2Int(3, 8);

    [Tooltip("Min/max distance from hit along spray direction")]
    public Vector2 stainDistanceRange = new Vector2(0.2f, 1f);

    [Tooltip("Height above floor to start fake fall (stain spawns here, animates down)")]
    [Range(0f, 0.5f)]
    public float stainStartHeight = 0.15f;

    [Tooltip("Floor Y offset below spawn for stain target")]
    [Range(-0.5f, 0f)]
    public float stainFloorOffset = -0.2f;

    [Tooltip("Min/max scale for stain sprites")]
    public Vector2 stainScaleRange = new Vector2(0.6f, 1.2f);

    [Header("Damage Scaling")]
    [Tooltip("Multiplier per damage type (Stab/Slash = more spray)")]
    public float stabMultiplier = 1.5f;
    public float slashMultiplier = 1.5f;
    public float bluntMultiplier = 0.5f;
    public float genericMultiplier = 0.8f;

    [Tooltip("Extra multiplier for critical hits (head/neck)")]
    [Range(1f, 2f)]
    public float criticalHitMultiplier = 1.5f;

    [Header("Character Stain")]
    [Tooltip("Attach a small blood splat to the hit limb")]
    public bool attachStainToVictim = true;

    [Tooltip("Scale for character stain sprite")]
    [Range(0.2f, 1f)]
    public float characterStainScale = 0.4f;

    [Header("References")]
    [Tooltip("Parent for spawned blood (keeps hierarchy clean). If null, uses scene root.")]
    public Transform bloodParent;

    [Header("Debug")]
    [Tooltip("Log blood spray events to Console")]
    public bool enableDebugLogs;

    private ProceduralCharacterController characterController;
    private BleedingController bleedingController;
    private SpriteRenderer characterSortingReference;
    private string bloodSortingLayerName = "Default";
    private int bloodSortingOrder = -100;
    private int bloodSortingOrderOffset = -20;

    private void Awake()
    {
        characterController = GetComponent<ProceduralCharacterController>();
        if (characterController == null)
            characterController = GetComponentInParent<ProceduralCharacterController>();

        bleedingController = GetComponent<BleedingController>();
        if (bleedingController == null)
            bleedingController = GetComponentInParent<BleedingController>();

        CacheSortingReference();
    }

    private void OnEnable()
    {
        if (characterController != null)
        {
            characterController.OnDamageDealt += OnDamageDealt;
            if (enableDebugLogs)
                Debug.Log($"[BloodSpray] Subscribed to OnDamageDealt on {gameObject.name}");
        }
    }

    private void OnDisable()
    {
        if (characterController != null)
            characterController.OnDamageDealt -= OnDamageDealt;
    }

    private void CacheSortingReference()
    {
        characterSortingReference = null;
        if (characterController != null && characterController.torso != null)
        {
            characterSortingReference = characterController.torso.GetComponentInChildren<SpriteRenderer>();
            if (characterSortingReference == null)
                characterSortingReference = characterController.torso.GetComponent<SpriteRenderer>();
        }
        if (characterSortingReference == null && characterController != null)
        {
            var limb = characterController.GetLimb(ProceduralCharacterController.LimbType.Torso);
            if (limb != null)
                characterSortingReference = limb.GetComponentInChildren<SpriteRenderer>();
        }
    }

    private void OnDamageDealt(Vector2 hitPosition, Vector2 attackDirection, float actualDamage,
        Weapon.DamageType damageType, ProceduralCharacterController.LimbType limbType, bool isCritical, Transform contactTransform)
    {
        if (enableDebugLogs)
            Debug.Log($"[BloodSpray] OnDamageDealt | {gameObject.name} | pos={hitPosition} dir={attackDirection} damage={actualDamage:F1} type={damageType} limb={limbType} critical={isCritical}");

        float mult = GetDamageTypeMultiplier(damageType);
        if (isCritical) mult *= criticalHitMultiplier;
        float intensity = actualDamage * mult;

        Vector2 sprayDir = attackDirection; // Blood sprays in slash direction
        if (sprayDir.sqrMagnitude < 0.01f)
            sprayDir = Vector2.right;

        SpawnParticleBurst(hitPosition, sprayDir, intensity);
        SpawnGroundStains(hitPosition, sprayDir, intensity);
        if (attachStainToVictim && contactTransform != null)
            SpawnCharacterStain(contactTransform);
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

    private void SpawnParticleBurst(Vector2 position, Vector2 direction, float intensity)
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(intensity * particlesPerDamageUnit), particleCountRange.x, particleCountRange.y);
        float speed = Mathf.Lerp(spraySpeedRange.x, spraySpeedRange.y, Mathf.Clamp01(intensity / 20f));

        if (enableDebugLogs)
            Debug.Log($"[BloodSpray] SpawnParticleBurst | count={count} speed={speed:F2} intensity={intensity:F1} prefab={bloodParticlePrefab != null}");

        if (bloodParticlePrefab != null)
        {
            Transform parent = bloodParent; // null = scene root
            GameObject go = Instantiate(bloodParticlePrefab, new Vector3(position.x, position.y, 0f), Quaternion.identity, parent);
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                EmitParticlesInDirection(ps, direction, count, speed);
                Destroy(go, 1.5f);
            }
            return;
        }

        GameObject particleGo = new GameObject("BloodSpray");
        particleGo.transform.position = new Vector3(position.x, position.y, 0f);
        particleGo.transform.SetParent(bloodParent); // null = scene root (not parented to person)

        var psNew = particleGo.AddComponent<ParticleSystem>();
        var main = psNew.main;
        main.startLifetime = 0.5f;
        main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;

        var emitParams = new ParticleSystem.EmitParams();
        float halfCone = sprayConeAngle * 0.5f * Mathf.Deg2Rad;
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) + Random.Range(-halfCone, halfCone);
            Vector2 vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed * Random.Range(0.7f, 1.3f);
            emitParams.velocity = vel;
            emitParams.position = Vector3.zero;
            emitParams.startSize = Random.Range(0.03f, 0.08f);
            emitParams.startColor = new Color(0.7f, 0f, 0f, 0.9f);
            psNew.Emit(emitParams, 1);
        }

        Destroy(particleGo, 1.5f);
    }

    private void EmitParticlesInDirection(ParticleSystem ps, Vector2 direction, int count, float speed)
    {
        var emitParams = new ParticleSystem.EmitParams();
        float halfCone = sprayConeAngle * 0.5f * Mathf.Deg2Rad;
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) + Random.Range(-halfCone, halfCone);
            Vector2 vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed * Random.Range(0.7f, 1.3f);
            emitParams.velocity = vel;
            emitParams.position = Vector3.zero;
            emitParams.startSize = Random.Range(0.03f, 0.08f);
            emitParams.startColor = new Color(0.7f, 0f, 0f, 0.9f);
            ps.Emit(emitParams, 1);
        }
    }

    private void SpawnGroundStains(Vector2 hitPosition, Vector2 sprayDirection, float intensity)
    {
        var sprites = bloodStainSprites != null && bloodStainSprites.Count > 0
            ? bloodStainSprites
            : (bleedingController != null && bleedingController.bloodSpriteVariants != null && bleedingController.bloodSpriteVariants.Count > 0
                ? bleedingController.bloodSpriteVariants
                : null);

        if (sprites == null || sprites.Count == 0)
        {
            if (enableDebugLogs)
                Debug.LogWarning("[BloodSpray] SpawnGroundStains skipped: no blood sprites (bloodStainSprites or BleedingController.bloodSpriteVariants empty)");
            return;
        }

        int count = Mathf.Clamp(Mathf.RoundToInt(intensity * 0.5f), stainCountRange.x, stainCountRange.y);
        Transform parent = bloodParent; // null = scene root (not parented to person)

        if (enableDebugLogs)
            Debug.Log($"[BloodSpray] SpawnGroundStains | count={count} sprites={sprites.Count}");

        for (int i = 0; i < count; i++)
        {
            float dist = Random.Range(stainDistanceRange.x, stainDistanceRange.y);
            Vector2 perp = new Vector2(-sprayDirection.y, sprayDirection.x);
            Vector2 jitter = perp * Random.Range(-0.15f, 0.15f);
            Vector2 landingXZ = hitPosition + sprayDirection * dist + jitter;

            Vector2 startPos = landingXZ + Vector2.up * stainStartHeight;
            Vector2 targetPos = landingXZ + Vector2.up * stainFloorOffset;

            Sprite sprite = sprites[Random.Range(0, sprites.Count)];
            if (sprite == null) continue;

            float scale = Random.Range(stainScaleRange.x, stainScaleRange.y);
            GameObject stain = new GameObject("BloodStain");
            stain.transform.SetParent(parent, true);
            stain.transform.position = new Vector3(startPos.x, startPos.y, 0f);
            stain.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            stain.transform.localScale = new Vector3(scale, scale, 1f);

            var sr = stain.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            ApplyBloodSorting(stain);

            var anim = stain.AddComponent<BloodDropFallAnimator>();
            anim.fallDuration = 0.3f;
            anim.floorOffset = stainFloorOffset;
            anim.scaleMode = BloodDropFallAnimator.ScaleMode.SplatOnLand;
            anim.splatDuration = 0.08f;
            anim.Begin(startPos, targetPos);
        }
    }

    private void SpawnCharacterStain(Transform contactTransform)
    {
        var sprites = bloodStainSprites != null && bloodStainSprites.Count > 0
            ? bloodStainSprites
            : (bleedingController != null && bleedingController.bloodSpriteVariants != null && bleedingController.bloodSpriteVariants.Count > 0
                ? bleedingController.bloodSpriteVariants
                : null);

        if (sprites == null || sprites.Count == 0)
        {
            if (enableDebugLogs)
                Debug.LogWarning("[BloodSpray] SpawnCharacterStain skipped: no blood sprites");
            return;
        }

        if (enableDebugLogs)
            Debug.Log($"[BloodSpray] SpawnCharacterStain on {contactTransform.name}");

        Sprite sprite = sprites[Random.Range(0, sprites.Count)];
        if (sprite == null) return;

        GameObject stain = new GameObject("CharacterBloodStain");
        stain.transform.SetParent(contactTransform, false);
        stain.transform.localPosition = Random.insideUnitCircle * 0.05f;
        stain.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        stain.transform.localScale = new Vector3(characterStainScale, characterStainScale, 1f);

        var sr = stain.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (characterSortingReference != null)
        {
            sr.sortingLayerID = characterSortingReference.sortingLayerID;
            sr.sortingOrder = characterSortingReference.sortingOrder + 1;
        }
        else
        {
            sr.sortingLayerName = bloodSortingLayerName;
            sr.sortingOrder = bloodSortingOrder;
        }
    }

    private void ApplyBloodSorting(GameObject bloodObject)
    {
        foreach (var sr in bloodObject.GetComponentsInChildren<SpriteRenderer>(true))
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
}
