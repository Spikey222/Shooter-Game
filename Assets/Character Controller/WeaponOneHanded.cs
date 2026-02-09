using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.IO;

/// <summary>
/// One-handed melee weapon (e.g. knife) that uses keyframe-based animation for stabbing attacks.
/// Supports character-specific hand sprites to match different character colors/skins.
/// Sprites are requested from ProceduralCharacterController's sprite override system.
/// </summary>
public class WeaponOneHanded : Weapon
{
    [Header("One-Handed Weapon Settings")]
    [Tooltip("Damage detection radius from the weapon hand (center of overlap check)")]
    public float attackRange = 1.25f;

    [Tooltip("Damage multiplier for attacks")]
    public float damageMultiplier = 1.0f;

    [Header("Contact Damage - Torso Exception")]
    [Tooltip("When weapon hits torso, damage is randomly applied. High bias parts (Head, Neck, Torso):")]
    [Range(0f, 100f)] public float torsoContactHeadChance = 26f;
    [Range(0f, 100f)] public float torsoContactNeckChance = 26f;
    [Range(0f, 100f)] public float torsoContactTorsoChance = 32f;
    [Tooltip("Moderate-low bias parts (Forearms, Hands):")]
    [Range(0f, 10f)] public float torsoContactForearmChance = 2f;
    [Range(0f, 10f)] public float torsoContactHandChance = 1.5f;
    [Tooltip("Low bias parts (Thighs, Calves, Feet) - extreme low hit chance:")]
    [Range(0f, 10f)] public float torsoContactThighChance = 1.5f;
    [Range(0f, 10f)] public float torsoContactCalfChance = 1.5f;
    [Range(0f, 10f)] public float torsoContactFootChance = 1f;

    [Header("Sprite Settings")]
    [Tooltip("Sprite override index to use from character controller (leave at -1 to use character's selected index)")]
    public int spriteOverrideIndex = -1;

    // NOTE: The "Hand Sprites" category from EquippableItem base class is intentionally not used for one-handed weapons.
    // This weapon uses the character controller's sprite override system instead (see OnEquipped method).
    // The base class hand sprite fields will appear in Inspector but are ignored.

    [Header("Weapon Overlay")]
    [Tooltip("GameObject prefab for the weapon overlay (can include collision boxes, sprites, etc.). If set, this takes priority over weaponSprite.")]
    public GameObject knifeOverlayPrefab;

    [Tooltip("Sprite for the weapon to overlay under the right hand (used only if knifeOverlayPrefab is not set)")]
    public Sprite knifeSprite;

    [Tooltip("Local offset for weapon overlay relative to hand (X, Y)")]
    public Vector2 knifeSpriteOffset = Vector2.zero;

    [Tooltip("Local rotation for weapon overlay (Z rotation in degrees)")]
    public float knifeSpriteRotation = 0f;

    [Tooltip("Sorting order offset for weapon overlay (negative = below hand, positive = above hand). Only applies if using knifeSprite.")]
    public int knifeSortingOrderOffset = -1;

    [Header("Animation Settings")]
    [Tooltip("Keyframe index where damage window starts (0-based)")]
    public int damageKeyframeStartIndex = 1;

    [Tooltip("Keyframe index where damage window ends (0-based, inclusive). Set to same as start for single-frame damage.")]
    public int damageKeyframeEndIndex = 1;

    [Tooltip("Attack cooldown in seconds")]
    public float attackCooldown = 0.5f;

    [Header("Sound Effects")]
    [Tooltip("Sound to play when weapon hits a target")]
    public AudioClip stabSound;

    [Tooltip("Volume of the stab sound")]
    [Range(0f, 1f)]
    public float stabVolume = 1f;

    // Audio source for playing sounds
    private AudioSource audioSource;

    // Internal state
    private bool isOnCooldown = false;
    private float lastAttackTime = 0f;
    private HashSet<ProceduralCharacterController> hitTargetsThisAttack = new HashSet<ProceduralCharacterController>();
    private bool isInDamageWindow = false;

    // Weapon sprite overlay objects
    private GameObject rightHandKnifeOverlay;
    private GameObject leftHandKnifeOverlay;

    private void Awake()
    {
        // Ensure WaypointAnimation component exists (using inherited field from EquippableItem)
        if (waypointAnimation == null)
        {
            waypointAnimation = GetComponent<WaypointAnimation>();
            if (waypointAnimation == null)
            {
                waypointAnimation = gameObject.AddComponent<WaypointAnimation>();
            }
        }

        // Get character controller reference
        characterController = GetComponentInParent<ProceduralCharacterController>();

        // Get or add AudioSource for sound effects
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }
    }

    private void OnEnable()
    {
        // Ensure waypointAnimation is found before trying to stop it
        if (waypointAnimation == null)
        {
            waypointAnimation = GetComponent<WaypointAnimation>();
        }

        // Reset animation state when weapon is reactivated (after being deactivated on unequip)
        if (waypointAnimation != null)
        {
            waypointAnimation.Stop(); // This will clear any stale state
        }

        // Reset cooldown state
        isOnCooldown = false;
        lastAttackTime = 0f;

        // Reset damage window state
        isInDamageWindow = false;
        hitTargetsThisAttack.Clear();
    }

    private void OnDisable()
    {
        // Clean up event handlers to prevent stale callbacks
        if (waypointAnimation != null)
        {
            waypointAnimation.OnKeyframeReached -= OnKeyframeReached;
            waypointAnimation.OnAnimationComplete -= OnAnimationComplete;
            waypointAnimation.Stop(); // Stop any running animation
        }

        // Reset state
        isOnCooldown = false;
        isInDamageWindow = false;
        hitTargetsThisAttack.Clear();

        // Clear character controller reference to prevent stale references
        characterController = null;
    }

    private void Update()
    {
        // While the animation is playing and we're in the damage window, check for hits every frame.
        // Each target is only damaged once per animation; hitTargetsThisAttack enforces that.
        if (characterController != null && waypointAnimation != null && waypointAnimation.IsPlaying() && isInDamageWindow)
        {
            TryDealDamageThisFrame();
        }
    }

    /// <summary>
    /// One-handed weapon uses contact-based damage in TryDealDamageThisFrame; no probability-based damage.
    /// </summary>
    public override float TakeDamageWithWeapon(ProceduralCharacterController target)
    {
        return 0f;
    }

    /// <summary>
    /// Override Use() to trigger stabbing animation and deal damage
    /// </summary>
    public override void Use()
    {
        // Check cooldown
        if (isOnCooldown && Time.time - lastAttackTime < attackCooldown)
        {
            return;
        }

        if (characterController == null)
        {
            characterController = GetComponentInParent<ProceduralCharacterController>();
        }

        if (characterController == null)
        {
            Debug.LogWarning($"[WeaponOneHanded] No character controller found for {gameObject.name}", this);
            return;
        }
        // #region agent log
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"hypothesisId\":\"D\",\"location\":\"WeaponOneHanded.Use\",\"message\":\"Use before Play\",\"data\":{\"controllerName\":\"" + (characterController.gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"weaponName\":\"" + (gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\"},\"timestamp\":\"" + System.DateTime.UtcNow.ToString("o") + "\"}\n"); } catch { }
        // #endregion
        // Play animation
        if (waypointAnimation != null)
        {
            // CRITICAL: Ensure waypoint animation has the correct character controller reference
            waypointAnimation.SetCharacterController(characterController);

            // Set up keyframe event callback for damage
            waypointAnimation.OnKeyframeReached += OnKeyframeReached;
            waypointAnimation.OnAnimationComplete += OnAnimationComplete;

            waypointAnimation.Play();
        }
        else
        {
            // If no animation, deal damage immediately (one check)
            hitTargetsThisAttack.Clear();
            TryDealDamageThisFrame();
            isOnCooldown = false;
        }

        lastAttackTime = Time.time;
        isOnCooldown = true;
    }

    /// <summary>
    /// Called when a keyframe is reached during animation. Only opens/closes the damage window; actual hit checks run in Update.
    /// </summary>
    private void OnKeyframeReached(int keyframeIndex)
    {
        if (keyframeIndex == damageKeyframeStartIndex)
        {
            isInDamageWindow = true;
            hitTargetsThisAttack.Clear();
        }
        else if (keyframeIndex > damageKeyframeEndIndex)
        {
            isInDamageWindow = false;
        }
    }

    /// <summary>
    /// Called when animation completes
    /// </summary>
    private void OnAnimationComplete()
    {
        // Clean up event handlers
        if (waypointAnimation != null)
        {
            waypointAnimation.OnKeyframeReached -= OnKeyframeReached;
            waypointAnimation.OnAnimationComplete -= OnAnimationComplete;
        }

        // Reset damage window state
        isInDamageWindow = false;
        hitTargetsThisAttack.Clear();

        isOnCooldown = false;
    }

    /// <summary>
    /// Check for targets in range and deal damage once per target per animation. Called every frame while the damage window is active.
    /// Uses contact-based damage: damages the part the weapon makes contact with. Exception: if contact is torso, randomly damages Head/Neck/Torso (high bias) or Thighs/Calves/Feet (low bias).
    /// </summary>
    private void TryDealDamageThisFrame()
    {
        if (characterController == null)
            return;

        // Center the check on the hand that holds the weapon (visual right = leftHand in hierarchy)
        Vector2 checkCenter = characterController.leftHand != null
            ? characterController.leftHand.transform.position
            : (Vector2)characterController.torso.position;

        Collider2D[] hits = Physics2D.OverlapCircleAll(checkCenter, attackRange);

        // Group colliders by target, keeping the closest collider per target (primary contact)
        Dictionary<ProceduralCharacterController, Collider2D> targetToContactCollider = new Dictionary<ProceduralCharacterController, Collider2D>();
        foreach (Collider2D hit in hits)
        {
            ProceduralCharacterController target = hit.GetComponentInParent<ProceduralCharacterController>();
            if (target == null || target == characterController)
                continue;

            // Only count colliders that belong to this character (torso or limbs)
            if (target.GetLimbTypeForCollider(hit) == null)
                continue;

            float distSq = ((Vector2)hit.ClosestPoint(checkCenter) - checkCenter).sqrMagnitude;
            if (!targetToContactCollider.TryGetValue(target, out Collider2D existing) ||
                ((Vector2)existing.ClosestPoint(checkCenter) - checkCenter).sqrMagnitude > distSq)
            {
                targetToContactCollider[target] = hit;
            }
        }

        bool hitAnyTarget = false;
        foreach (var kvp in targetToContactCollider)
        {
            ProceduralCharacterController target = kvp.Key;
            Collider2D contactCollider = kvp.Value;
            if (hitTargetsThisAttack.Contains(target))
                continue;
            hitTargetsThisAttack.Add(target);
            hitAnyTarget = true;
            TakeKnifeDamageToTarget(target, contactCollider);
        }

        if (hitAnyTarget && stabSound != null && audioSource != null)
            audioSource.PlayOneShot(stabSound, stabVolume);
    }

    /// <summary>
    /// Apply damage to a target based on contact. Damages the contacted part directly, unless contact is torso—then uses weighted random (Head/Neck/Torso high bias, Thighs/Calves/Feet low bias).
    /// </summary>
    private void TakeKnifeDamageToTarget(ProceduralCharacterController target, Collider2D contactCollider)
    {
        if (target == null) return;

        float damage = baseDamage * damageMultiplier;
        var contactLimbType = target.GetLimbTypeForCollider(contactCollider);
        if (contactLimbType == null)
        {
            target.ApplyDamageToLimb(ProceduralCharacterController.LimbType.Torso, damage);
            return;
        }

        ProceduralCharacterController.LimbType limbToDamage;
        if (contactLimbType == ProceduralCharacterController.LimbType.Torso)
        {
            limbToDamage = target.SelectKnifeTorsoContactLimb(
                torsoContactHeadChance, torsoContactNeckChance, torsoContactTorsoChance,
                torsoContactForearmChance, torsoContactHandChance,
                torsoContactThighChance, torsoContactCalfChance, torsoContactFootChance);
        }
        else
        {
            limbToDamage = contactLimbType.Value;
        }

        target.ApplyDamageToLimb(limbToDamage, damage);
    }

    /// <summary>
    /// Called when weapon is equipped - applies character-specific sprites from ProceduralCharacterController
    /// </summary>
    public void OnEquipped(ProceduralCharacterController controller)
    {
        if (controller == null)
            return;

        characterController = controller;
        // #region agent log
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"hypothesisId\":\"D\",\"location\":\"WeaponOneHanded.OnEquipped\",\"message\":\"OnEquipped\",\"data\":{\"controllerName\":\"" + (controller.gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"weaponName\":\"" + (gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\"},\"timestamp\":\"" + System.DateTime.UtcNow.ToString("o") + "\"}\n"); } catch { }
        // #endregion
        // CRITICAL: Update WaypointAnimation's character controller reference
        // This ensures the animation targets the correct character's limbs
        if (waypointAnimation != null)
        {
            waypointAnimation.SetCharacterController(controller);
        }

        // Get sprite override from character controller
        // Use spriteOverrideIndex if set, otherwise use character's selected index
        ProceduralCharacterController.WeaponSpriteOverride spriteOverride = null;

        if (spriteOverrideIndex >= 0)
        {
            spriteOverride = controller.GetSpriteOverride(spriteOverrideIndex);
        }
        else
        {
            spriteOverride = controller.GetSelectedSpriteOverride();
        }

        if (spriteOverride != null)
        {
            // Apply sprites from character controller's selected override
            // Only apply if sprite exists (ignore null/empty sprites)
            // Note: Swapped to fix opposite sprite assignment issue
            if (spriteOverride.rightHandSprite != null && controller.leftHand != null)
            {
                controller.leftHand.SwapSprite(spriteOverride.rightHandSprite);
            }

            if (spriteOverride.leftHandSprite != null && controller.rightHand != null)
            {
                controller.rightHand.SwapSprite(spriteOverride.leftHandSprite);
            }
        }
        // Don't show warning if spriteOverride is null - it's optional

        // Create weapon sprite overlays under the hands
        CreateKnifeOverlays(controller);

        // Ensure waypoint animation is properly initialized and reset
        if (waypointAnimation == null)
        {
            waypointAnimation = GetComponent<WaypointAnimation>();
            if (waypointAnimation == null)
            {
                waypointAnimation = gameObject.AddComponent<WaypointAnimation>();
            }
        }

        // Reset animation state when re-equipping (in case weapon was deactivated/reactivated)
        if (waypointAnimation != null)
        {
            // Stop any playing animation and clear state
            waypointAnimation.Stop();

            // Set character controller reference again to ensure it's up to date
            waypointAnimation.SetCharacterController(controller);
        }

        // Initialize keyframe animation with character's limbs if not already set
        if (waypointAnimation != null && waypointAnimation.keyframes != null && waypointAnimation.keyframes.Count > 0)
        {
            // Check if keyframes need limb references
            foreach (var keyframe in waypointAnimation.keyframes)
            {
                foreach (var limbTarget in keyframe.limbTargets)
                {
                    if (limbTarget.targetLimb == null)
                    {
                        // Try to auto-assign based on common names (optional helper)
                        // This is a fallback - ideally limbs should be assigned in Inspector
                    }
                }
            }
        }
    }

    /// <summary>
    /// Create weapon overlays under the hands (either from prefab or sprite)
    /// </summary>
    private void CreateKnifeOverlays(ProceduralCharacterController controller)
    {
        // Check if we have either a prefab or sprite
        if (knifeOverlayPrefab == null && knifeSprite == null)
        {
            Debug.LogWarning($"[WeaponOneHanded] No weapon overlay prefab or sprite assigned. Overlay will not be created.", this);
            return;
        }

        // Clean up any existing overlays first
        DestroyKnifeOverlays();

        // Create overlay for the hand that visually holds the weapon
        // Note: Due to sprite swapping, the visual right hand is the leftHand GameObject
        // So we attach to leftHand to match the visual appearance
        if (controller.leftHand != null)
        {
            if (knifeOverlayPrefab != null)
            {
                // Use GameObject prefab (supports collision boxes, multiple components, etc.)
                rightHandKnifeOverlay = CreateKnifeOverlayFromPrefab(
                    controller.leftHand.transform,
                    "RightHandKnifeOverlay"
                );
            }
            else
            {
                // Fall back to sprite-based overlay
                SpriteRenderer handRenderer = GetHandSpriteRenderer(controller.leftHand);
                if (handRenderer != null)
                {
                    rightHandKnifeOverlay = CreateKnifeOverlayObject(
                        controller.leftHand.transform,
                        handRenderer,
                        "RightHandKnifeOverlay"
                    );
                }
            }
        }

        // Optionally create overlay for left hand if needed
        // (Usually only right hand holds the weapon, but you can enable this if needed)
    }

    /// <summary>
    /// Get the SpriteRenderer for a hand limb
    /// </summary>
    private SpriteRenderer GetHandSpriteRenderer(ProceduralLimb hand)
    {
        if (hand == null)
            return null;

        // Try to get from limbSprite first
        if (hand.limbSprite != null)
        {
            return hand.limbSprite;
        }

        // Fallback to getting SpriteRenderer from the limb GameObject
        return hand.GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// Create a weapon overlay GameObject from a prefab (supports collision boxes, etc.)
    /// </summary>
    private GameObject CreateKnifeOverlayFromPrefab(Transform handParent, string overlayName)
    {
        if (knifeOverlayPrefab == null)
            return null;

        // Instantiate the prefab
        GameObject overlayGO = Instantiate(knifeOverlayPrefab, handParent);
        overlayGO.name = overlayName;

        // Apply position offset
        overlayGO.transform.localPosition = new Vector3(knifeSpriteOffset.x, knifeSpriteOffset.y, 0f);

        // Apply rotation
        overlayGO.transform.localRotation = Quaternion.Euler(0f, 0f, knifeSpriteRotation);

        // Ensure scale is correct (preserve prefab scale)
        // Local scale is already set by Instantiate, but we can override if needed

        // Try to match sorting order if there's a SpriteRenderer in the prefab
        SpriteRenderer overlayRenderer = overlayGO.GetComponentInChildren<SpriteRenderer>();
        SpriteRenderer handRenderer = GetHandSpriteRenderer(handParent.GetComponent<ProceduralLimb>());

        if (overlayRenderer != null && handRenderer != null)
        {
            overlayRenderer.sortingLayerID = handRenderer.sortingLayerID;
            overlayRenderer.sortingOrder = handRenderer.sortingOrder + knifeSortingOrderOffset;
        }

        return overlayGO;
    }

    /// <summary>
    /// Create a weapon overlay GameObject as a child of the hand (sprite-based, legacy method)
    /// </summary>
    private GameObject CreateKnifeOverlayObject(Transform handParent, SpriteRenderer handRenderer, string overlayName)
    {
        GameObject overlayGO = new GameObject(overlayName);
        overlayGO.transform.SetParent(handParent, false);
        overlayGO.transform.localPosition = new Vector3(knifeSpriteOffset.x, knifeSpriteOffset.y, 0f);
        overlayGO.transform.localRotation = Quaternion.Euler(0f, 0f, knifeSpriteRotation);
        overlayGO.transform.localScale = Vector3.one;

        SpriteRenderer sr = overlayGO.AddComponent<SpriteRenderer>();
        sr.sprite = knifeSprite;

        // Match sorting layer and set sorting order below the hand
        if (handRenderer != null)
        {
            sr.sortingLayerID = handRenderer.sortingLayerID;
            sr.sortingOrder = handRenderer.sortingOrder + knifeSortingOrderOffset;
        }
        else
        {
            sr.sortingOrder = knifeSortingOrderOffset;
        }

        return overlayGO;
    }

    /// <summary>
    /// Destroy weapon overlay objects
    /// </summary>
    private void DestroyKnifeOverlays()
    {
        if (rightHandKnifeOverlay != null)
        {
            Destroy(rightHandKnifeOverlay);
            rightHandKnifeOverlay = null;
        }

        if (leftHandKnifeOverlay != null)
        {
            Destroy(leftHandKnifeOverlay);
            leftHandKnifeOverlay = null;
        }
    }

    /// <summary>
    /// Called when weapon is unequipped - restores default sprites and removes overlays
    /// </summary>
    public void OnUnequipped(ProceduralCharacterController controller)
    {
        if (controller == null)
            return;

        // Destroy weapon overlays
        DestroyKnifeOverlays();

        // Restore default sprites (passing null to SwapSprite should revert)
        if (controller.rightHand != null)
        {
            controller.rightHand.SwapSprite(null);
        }

        if (controller.leftHand != null)
        {
            controller.leftHand.SwapSprite(null);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (characterController == null)
            return;
        Vector3 center = characterController.leftHand != null
            ? characterController.leftHand.transform.position
            : (Vector3)characterController.torso.position;
        Gizmos.color = Color.red;
        float radius = attackRange;
        int segments = 32;
        float angleStep = 360f / segments;
        Vector3 prevPoint = center + new Vector3(radius, 0, 0);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }
}
