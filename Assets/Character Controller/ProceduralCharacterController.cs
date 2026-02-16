using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.IO;

[RequireComponent(typeof(Rigidbody2D))]
public class ProceduralCharacterController : MonoBehaviour
{
    // Enum to categorize limb types for UI and gameplay purposes
    public enum LimbType
    {
        Head,
        Neck,
        Torso,
        RightBicep,
        RightForearm,
        RightHand,
        LeftBicep,
        LeftForearm,
        LeftHand,
        // Leg support (for future implementation)
        RightThigh,
        RightCalf,
        RightFoot,
        LeftThigh,
        LeftCalf,
        LeftFoot
    }
    
    // Dictionary to map limbs to their types
    private Dictionary<LimbType, ProceduralLimb> limbMap = new Dictionary<LimbType, ProceduralLimb>();

    // Last damage type per limb (for body-part UI wording, e.g. "badly slashed")
    private Dictionary<LimbType, Weapon.DamageType> lastDamageTypeByLimb = new Dictionary<LimbType, Weapon.DamageType>();
    // All damage types that have affected each limb (for UI: show Stab, Slash, Blunt etc.)
    private Dictionary<LimbType, HashSet<Weapon.DamageType>> damageTypesByLimb = new Dictionary<LimbType, HashSet<Weapon.DamageType>>();
    // Accumulated blunt damage per limb (increases vulnerability to future damage)
    private Dictionary<LimbType, float> bluntTraumaByLimb = new Dictionary<LimbType, float>();
    // Accumulated pain per limb (increases with damage, optional decay)
    private Dictionary<LimbType, float> painByLimb = new Dictionary<LimbType, float>();

    [Header("Blunt Trauma (vulnerability)")]
    [Tooltip("Extra damage multiplier per 'limb max health' of blunt trauma (e.g. 0.5 = 50%% more damage when trauma equals max health)")]
    [Range(0f, 2f)]
    public float bluntTraumaVulnerabilityScale = 0.5f;
    [Tooltip("Cap on vulnerability increase (e.g. 0.5 = at most 50%% more damage, multiplier 1.5)")]
    [Range(0f, 1f)]
    public float bluntTraumaVulnerabilityCap = 0.5f;

    [Header("Laceration severity (for bandage/stitches)")]
    [Tooltip("Health %% above this = light severity (bandages fully heal).")]
    [Range(0f, 1f)]
    public float lacerationLightThreshold = 0.66f;
    [Tooltip("Health %% below this = heavy severity (need stitches to heal). Between light and heavy = medium.")]
    [Range(0f, 1f)]
    public float lacerationHeavyThreshold = 0.33f;
    
    [Header("Pain")]
    [Tooltip("Pain added per point of damage taken (0 = no pain)")]
    public float painPerDamage = 1f;
    [Tooltip("Max pain value per limb (displayed as 100% when at or above this)")]
    public float maxPainPerLimb = 100f;
    [Tooltip("Pain decay per second (0 = no decay)")]
    public float painDecayPerSecond = 2f;
    
    // Event for when any limb's health changes
    public event Action<LimbType, float, float> OnLimbHealthChanged; // limbType, current, max
    [Header("Character Settings")]
    [Tooltip("Team ID for the character (0 = neutral, 1 = team 1, 2 = team 2, etc.)")]
    public int teamId = 0;
    
    [Tooltip("Whether this character is in spectator mode (no movement control)")]
    public bool spectatorMode = false;
    
    [Tooltip("Whether to automatically check for spectator mode")]
    public bool autoCheckSpectatorMode = true;
    
    [Header("Debug")]
    [Tooltip("Log damage to Console (character name, body part, amount)")]
    public bool logDamageToConsole = false;
    
    // Used to ignore attack only in the frame we were possessed (avoids attacking on the same click that selected this character)
    private bool ignoreNextAttackInput = false;
    private int possessedFrame = -1;
    
    [Header("Health Settings")]
    [Tooltip("Maximum health for the character")]
    public float maxHealth = 100f;
    
    [Tooltip("Current health of the character")]
    [SerializeField] private float currentHealth;
    
    [Tooltip("Maximum health for the torso/chest")]
    public float torsoMaxHealth = 100f;
    
    [Tooltip("Current health of the torso/chest")]
    [SerializeField] private float torsoHealth;
    
    // Track previous torso health value to detect inspector changes at runtime
    private float previousTorsoHealth = -1f;
    
    // Event for when character's overall health changes
    public event Action<float, float> OnHealthChanged; // current, max

    /// <summary>
    /// Fired when the character dies (e.g. from blood loss). SpectatorController should release control.
    /// </summary>
    public event Action OnDeath;

    /// <summary>
    /// True when the character has died (e.g. from blood loss). Character is disabled and cannot be controlled.
    /// </summary>
    public bool IsDead { get; private set; }

    /// <summary>
    /// Fired when damage is actually dealt and hit context is provided. Args: hitPosition, attackDirection (normalized), actualDamage, damageType, limb, isCritical.
    /// Blood system subscribes to drive directional spray.
    /// </summary>
    public event Action<Vector2, Vector2, float, Weapon.DamageType, LimbType, bool, Transform> OnDamageDealt;

    [Tooltip("Movement speed of the character")]
    public float moveSpeed = 5f;
    
    [Tooltip("Rotation speed when turning")]
    public float rotationSpeed = 3f;
    
    [Tooltip("Acceleration for smoother movement")]
    public float acceleration = 20f;
    
    [Tooltip("Deceleration when stopping")]
    public float deceleration = 30f;
    
    [Tooltip("Velocity threshold to completely stop movement")]
    public float stopThreshold = 0.1f;
    
    [Header("Physics Settings")]
    [Tooltip("Torso rigidbody mass")]
    public float torsoMass = 3f;
    
    [Tooltip("Limb rigidbody mass")]
    public float limbMass = 0.8f;
    
    [Tooltip("Linear drag for all rigidbodies")]
    public float linearDrag = 1f;
    
    [Tooltip("Angular drag for all rigidbodies")]
    public float angularDrag = 0.5f;
    
    [Header("Body Parts")]
    [Tooltip("Reference to the torso rigidbody")]
    public Rigidbody2D torso;
    
    [Tooltip("Reference to the head limb")]
    public ProceduralLimb head;
    
    [Tooltip("Reference to the right arm")]
    public ProceduralLimb rightArm;
    
    [Tooltip("Reference to the right forearm")]
    public ProceduralLimb rightForearm;
    
    [Tooltip("Reference to the right hand")]
    public ProceduralLimb rightHand;
    
    [Tooltip("Reference to the left arm")]
    public ProceduralLimb leftArm;
    
    [Tooltip("Reference to the left forearm")]
    public ProceduralLimb leftForearm;
    
    [Tooltip("Reference to the left hand")]
    public ProceduralLimb leftHand;
    
    [Tooltip("Reference to the neck limb (can also be found dynamically)")]
    public ProceduralLimb neck;
    
    [Tooltip("Reference to the right thigh")]
    public ProceduralLimb rightThigh;
    
    [Tooltip("Reference to the right calf")]
    public ProceduralLimb rightCalf;
    
    [Tooltip("Reference to the right foot")]
    public ProceduralLimb rightFoot;
    
    [Tooltip("Reference to the left thigh")]
    public ProceduralLimb leftThigh;
    
    [Tooltip("Reference to the left calf")]
    public ProceduralLimb leftCalf;
    
    [Tooltip("Reference to the left foot")]
    public ProceduralLimb leftFoot;
    
    // Input is now handled directly via keyboard
    

    [Header("Arm Settings")]
    [Tooltip("Idle sway amount for arms")]
    public float armIdleSwayAmount = 5f;
    
    [Tooltip("Idle sway speed for arms")]
    public float armIdleSwaySpeed = 0.5f;
    
    [Tooltip("Base angle for right arm when idle")]
    public float rightArmBaseAngle = 60f;
    
    [Tooltip("Base angle for left arm when idle")]
    public float leftArmBaseAngle = -60f;
    
    [Tooltip("Base angle for right forearm when idle")]
    public float rightForearmBaseAngle = 60f;
    
    [Tooltip("Base angle for left forearm when idle")]
    public float leftForearmBaseAngle = -60f;
    
    [Header("Walking Animation")]
    [Tooltip("Whether to use walking animation when moving")]
    public bool useWalkingAnimation = true;
    
    [Tooltip("Base angle for arms when walking")]
    public float walkingArmAngle = 35f;
    
    [Tooltip("Base angle for forearms when walking")]
    public float walkingForearmAngle = 75f;
    
    [Tooltip("Arm swing amount when walking")]
    public float walkingSwingAmount = 40f;
    
    [Tooltip("Forearm swing amount when walking (should be less than arm swing)")]
    public float walkingForearmSwingAmount = 10f;
    
    [Tooltip("Arm swing speed multiplier based on movement speed")]
    public float walkingSwingSpeedMultiplier = 0.5f;
    
    [Tooltip("Minimum movement magnitude to trigger walking animation")]
    public float walkingThreshold = 0.1f;
    
    [Tooltip("Scale torso rotation during weapon/attack animation (0 = lock rotation, 1 = full). Reduces joint strain when moving in certain directions.")]
    [Range(0f, 1f)]
    public float torsoRotationScaleDuringAttack = 0.4f;
    
    [Header("Hand Settings")]
    [Tooltip("Base angle for right hand relative to forearm")]
    public float rightHandBaseAngle = 0f;
    
    [Tooltip("Base angle for left hand relative to forearm")]
    public float leftHandBaseAngle = 0f;
    
    [Header("Joint Stiffness")]
    [Tooltip("Motor force for arm joints")]
    public float armJointForce = 100f;
    
    [Tooltip("Motor force for hand joints")]
    public float handJointForce = 150f;
    
    [Header("Head Control")]
    [Tooltip("Head rotation speed")]
    public float headRotationSpeed = 15f;
    
    [Tooltip("Maximum angle difference between head and torso")]
    public float maxHeadAngleDifference = 45f;
    
    [Header("Camera Settings")]
    [Tooltip("Whether to attach the camera to the player")]
    public bool attachCamera = false;
    
    [Tooltip("Camera follow speed")]
    public float cameraFollowSpeed = 5f;
    
    [Tooltip("Camera follow speed multiplier when in overview/spectator mode")]
    public float overviewCameraSpeedMultiplier = 2f;
    
    [Header("Camera Settings")]
    [Tooltip("Enable camera zoom with mouse wheel")]
    public bool enableCameraZoom = false;
    
    [Tooltip("Camera zoom speed")]
    public float cameraZoomSpeed = 2f;
    
    [Tooltip("Minimum camera orthographic size")]
    public float minZoom = 2f;
    
    [Tooltip("Maximum camera orthographic size")]
    public float maxZoom = 10f;
    
    // Runtime variables
    private Vector2 moveInput;
    private Vector2 aimInput;
    private Vector2 currentVelocity;
    private Vector2 targetVelocity;
    private Camera mainCamera;
    private float idleTime = 0f;
    
    // Currently equipped item
    private EquippableItem equippedItem;
    
    [Header("Inventory")]
    [Tooltip("Reference to the inventory component (auto-assigned if not set)")]
    public Inventory inventory;

    [Header("Clothing")]
    [Tooltip("Reference to the clothing controller (auto-assigned if not set)")]
    public ClothingController clothingController;
    
    // Per-character weapon instance tracking - prevents sharing weapons between characters
    private Dictionary<Item, Weapon> ownedWeaponInstances = new Dictionary<Item, Weapon>();
    
    [System.Serializable]
    public class WeaponSpriteOverride
    {
        [Tooltip("Name/identifier for this sprite override set (e.g., 'White Character', 'Black Character')")]
        public string overrideName = "Default";
        
        [Tooltip("Right hand sprite when holding weapons")]
        public Sprite rightHandSprite;
        
        [Tooltip("Left hand sprite when holding weapons")]
        public Sprite leftHandSprite;
    }
    
    [Header("Weapon Sprite Overrides")]
    [Tooltip("List of sprite override sets for different character appearances")]
    public List<WeaponSpriteOverride> weaponSpriteOverrides = new List<WeaponSpriteOverride>();
    
    [Tooltip("Currently selected sprite override index (0 = first in list)")]
    public int selectedSpriteOverrideIndex = 0;
    
    private void Awake()
    {
        // Initialize components
        if (torso == null)
        {
            torso = GetComponent<Rigidbody2D>();
        }
        
        // Initialize inventory
        if (inventory == null)
        {
            inventory = GetComponent<Inventory>();
            if (inventory == null)
            {
                inventory = gameObject.AddComponent<Inventory>();
            }
        }

        // Initialize clothing controller
        if (clothingController == null)
        {
            clothingController = GetComponent<ClothingController>();
            if (clothingController == null)
            {
                clothingController = gameObject.AddComponent<ClothingController>();
            }
        }
        
        // Configure torso physics
        if (torso != null)
        {
            torso.mass = torsoMass;
            torso.linearDamping = linearDrag;
            torso.angularDamping = angularDrag;
            torso.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            torso.interpolation = RigidbodyInterpolation2D.Interpolate;
            torso.gravityScale = 0f; // No gravity in top-down view
        }
        
        // Get main camera
        mainCamera = Camera.main;
        
        // Configure joint stiffness
        ConfigureJointStiffness();
        
        // Configure limb colliders
        ConfigureLimbColliders();
        
        // Initialize health
        currentHealth = maxHealth;
        
        // Initialize torso health
        torsoHealth = torsoMaxHealth;
        previousTorsoHealth = torsoHealth;
        
        // Initialize limb map and subscribe to health change events
        InitializeLimbMap();
    }
    
    // OnEnable and OnDisable methods removed as part of possession system cleanup
    
    // Detect inspector changes (runs in edit mode and play mode when inspector values change)
    private void OnValidate()
    {
        // Fire event if torso health changed (only in play mode, to avoid spamming in edit mode)
        if (Application.isPlaying && previousTorsoHealth >= 0f && torsoHealth != previousTorsoHealth)
        {
            torsoHealth = Mathf.Clamp(torsoHealth, 0f, torsoMaxHealth);
            OnLimbHealthChanged?.Invoke(LimbType.Torso, torsoHealth, torsoMaxHealth);
            previousTorsoHealth = torsoHealth;
            UpdateOverallHealth();
        }
    }
    
    private void Update()
    {
        // Check for runtime inspector changes to torso health (since OnValidate might not always fire in play mode)
        if (previousTorsoHealth >= 0f && torsoHealth != previousTorsoHealth)
        {
            torsoHealth = Mathf.Clamp(torsoHealth, 0f, torsoMaxHealth);
            OnLimbHealthChanged?.Invoke(LimbType.Torso, torsoHealth, torsoMaxHealth);
            previousTorsoHealth = torsoHealth;
            UpdateOverallHealth();
        }
        
        // Auto check for spectator mode if enabled
        if (autoCheckSpectatorMode)
        {
            CheckSpectatorMode();
        }
        
        
        // Reset aim input if in spectator mode
        if (spectatorMode)
        {
            aimInput = Vector2.zero;
        }
        // Get mouse position for aiming - only update when inventory is closed (keeps last angle when open to prevent spinning)
        else if (!InventoryUI.IsInventoryOpen && mainCamera != null && Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector2 worldPos = mainCamera.ScreenToWorldPoint(mousePos);
            aimInput = (worldPos - (Vector2)transform.position).normalized;
        }
        // When inventory is open: aimInput keeps its last value so torso stays locked to that angle (no uncontrolled spins)
        
        // Handle keyboard input for WASD movement (will be ignored if in overview mode)
        HandleKeyboardInput();
        
        // Update head rotation to follow mouse (will be skipped if in overview mode)
        UpdateHeadRotation();
        
        // Always update idle animation regardless of control state
        UpdateIdleAnimation();
        
        // Update arm positions for equipped item
        if (equippedItem != null)
        {
            UpdateEquippedItemPosition();
        }
        
        // Update camera position and zoom if attached
        UpdateCamera();
        
        // Decay pain over time
        if (painDecayPerSecond > 0f && painByLimb.Count > 0)
        {
            var limbs = painByLimb.Keys.ToList();
            foreach (LimbType limb in limbs)
            {
                float p = painByLimb[limb] - painDecayPerSecond * Time.deltaTime;
                if (p <= 0f)
                    painByLimb.Remove(limb);
                else
                    painByLimb[limb] = p;
            }
        }
    }
    
    private void FixedUpdate()
    {
        // Skip all movement and rotation if in spectator mode
        if (spectatorMode)
        {
            // Ensure the character doesn't move in spectator mode
            if (torso != null)
            {
                torso.linearVelocity = Vector2.zero;
            }
            return;
        }
        
        // Calculate target velocity based on input
        targetVelocity = moveInput * moveSpeed;
        
        // Smoothly interpolate current velocity towards target
        if (targetVelocity.magnitude > 0.1f)
        {
            currentVelocity = Vector2.Lerp(currentVelocity, targetVelocity, acceleration * Time.fixedDeltaTime);
        }
        else
        {
            // Apply stronger deceleration when stopping
            currentVelocity = Vector2.Lerp(currentVelocity, Vector2.zero, deceleration * Time.fixedDeltaTime);
            
            // Force stop if velocity is below threshold
            if (currentVelocity.magnitude < stopThreshold)
            {
                currentVelocity = Vector2.zero;
            }
        }
        
        // Apply movement to torso
        if (torso != null)
        {
            torso.linearVelocity = currentVelocity;
        }
        
        // Rotate torso towards aim direction (slower than head)
        // When aimInput is valid, lock to that angle and zero angular velocity to prevent uncontrolled spinning from collisions
        if (aimInput.magnitude > 0.1f)
        {
            float targetAngle = Mathf.Atan2(aimInput.y, aimInput.x) * Mathf.Rad2Deg;
            
            if (torso != null)
            {
                // Zero angular velocity so collisions don't cause runaway spinning
                torso.angularVelocity = 0f;
                // During weapon animation, reduce rotation so limbs aren't yanked by torso spin
                float effectiveRotationSpeed = rotationSpeed;
                if (equippedItem != null && equippedItem.waypointAnimation != null && equippedItem.waypointAnimation.IsPlaying())
                    effectiveRotationSpeed *= torsoRotationScaleDuringAttack;
                // Smoothly rotate towards target angle
                float currentAngle = torso.rotation;
                float newAngle = Mathf.LerpAngle(currentAngle, targetAngle, effectiveRotationSpeed * Time.fixedDeltaTime);
                torso.MoveRotation(newAngle);
            }
        }
    }
    
    // Handle keyboard input for WASD movement
    private void HandleKeyboardInput()
    {
        // Reset move input each frame to ensure it stops when keys are released
        moveInput = Vector2.zero;
        
        // Skip input handling if in spectator mode
        if (spectatorMode)
        {
            return;
        }
        
        // WASD movement
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) moveInput.y += 1;
            if (Keyboard.current.sKey.isPressed) moveInput.y -= 1;
            if (Keyboard.current.aKey.isPressed) moveInput.x -= 1;
            if (Keyboard.current.dKey.isPressed) moveInput.x += 1;
            
            // Normalize to prevent diagonal movement being faster
            if (moveInput.magnitude > 1)
            {
                moveInput.Normalize();
            }
            
            // Attack/Use input (Left Mouse Button or Space) - blocked when inventory is open
            if (!InventoryUI.IsInventoryOpen &&
                ((Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) || 
                Keyboard.current.spaceKey.wasPressedThisFrame))
            {
                // Clear stale ignore flag if we're past the possessed frame (script order can make us miss the possession click)
                if (ignoreNextAttackInput && Time.frameCount > possessedFrame)
                {
                    ignoreNextAttackInput = false;
                    possessedFrame = -1;
                }
                // Ignore attack only in the exact frame we were possessed (same click that selected this character)
                if (ignoreNextAttackInput && Time.frameCount == possessedFrame)
                {
                    ignoreNextAttackInput = false;
                    possessedFrame = -1;
                }
                else if (equippedItem != null)
                {
                    equippedItem.Use();
                }
            }
        }
    }
    
    // Update animation for arms (idle or walking)
    private void UpdateIdleAnimation()
    {
        // Update idle time
        idleTime += Time.deltaTime;
        
        // Check if we should use walking animation - use CURRENT velocity, not input
        // Always allow idle animations, even in overview mode
        bool isWalking = useWalkingAnimation && currentVelocity.magnitude > walkingThreshold;
        
        // Don't animate if holding an item AND waypoint animation is currently playing
        if (equippedItem != null)
        {
            // Check if waypoint animation is playing - if so, don't override with idle animation
            if (equippedItem.waypointAnimation != null && equippedItem.waypointAnimation.IsPlaying())
            {
                return;
            }
            // Otherwise, allow idle animation to play (waypoint animation is not playing or doesn't exist)
            // This allows idle animation to resume after waypoint animation completes
        }
        
        if (isWalking)
        {
            // Calculate walking animation parameters based on actual velocity
            float walkingSpeed = currentVelocity.magnitude * walkingSwingSpeedMultiplier;
            float walkingTime = Time.time * walkingSpeed;
            
            // RUNNING ANIMATION: Arms in true opposition (when one is forward, other is backward)
            // Right arm starts forward when sin is positive, left arm starts backward
            
            // Calculate the swing cycle (0 to 1 to 0 to -1 to 0)
            float swingCycle = Mathf.Sin(walkingTime);
            
            // Right arm: Forward when positive, backward when negative
            float rightArmPosition = walkingArmAngle + (swingCycle * walkingSwingAmount);
            
            // Left arm: Backward when right is forward (opposite phase)
            float leftArmPosition = walkingArmAngle - (swingCycle * walkingSwingAmount);
            
            // Forearms follow the same pattern but with reduced swing and appropriate angles
            float rightForearmPosition = walkingForearmAngle + (swingCycle * walkingForearmSwingAmount * 0.5f);
            float leftForearmPosition = walkingForearmAngle - (swingCycle * walkingForearmSwingAmount * 0.5f);
            
            // Apply walking animation to arms
            // Right arm chain
            if (rightArm != null)
            {
                rightArm.SetTargetAngle(rightArmPosition);
            }
            
            if (rightForearm != null)
            {
                rightForearm.SetTargetAngle(rightForearmPosition);
            }
            
            if (rightHand != null)
            {
                rightHand.SetTargetAngle(rightHandBaseAngle);
            }
            
            // Left arm chain
            if (leftArm != null)
            {
                leftArm.SetTargetAngle(-leftArmPosition); // Mirror for left side
            }
            
            if (leftForearm != null)
            {
                leftForearm.SetTargetAngle(-leftForearmPosition); // Mirror for left side
            }
            
            if (leftHand != null)
            {
                leftHand.SetTargetAngle(0f); // Zero to align with forearm (avoids lower limit capping)
            }
        }
        else
        {
            // Calculate idle sway based on sine wave
            float rightArmSway = Mathf.Sin(idleTime * armIdleSwaySpeed) * armIdleSwayAmount;
            float leftArmSway = Mathf.Sin(idleTime * armIdleSwaySpeed + Mathf.PI) * armIdleSwayAmount; // Offset by PI for opposite movement
            
            // Apply idle animation
            // Right arm chain
            if (rightArm != null)
            {
                rightArm.SetTargetAngle(rightArmBaseAngle + rightArmSway);
            }
            
            if (rightForearm != null)
            {
                rightForearm.SetTargetAngle(rightForearmBaseAngle + rightArmSway);
            }
            
            if (rightHand != null)
            {
                rightHand.SetTargetAngle(rightHandBaseAngle);
            }
            
            // Left arm chain
            if (leftArm != null)
            {
                leftArm.SetTargetAngle(leftArmBaseAngle + leftArmSway);
            }
            
            if (leftForearm != null)
            {
                leftForearm.SetTargetAngle(leftForearmBaseAngle + leftArmSway);
            }
            
            if (leftHand != null)
            {
                leftHand.SetTargetAngle(0f); // Zero to align with forearm (avoids lower limit capping)
            }
        }
    }
    
    // Update camera position and zoom if attached
    private void UpdateCamera()
    {
        // Skip camera updates completely if not attached - let SpectatorController handle it
        if (mainCamera == null || !attachCamera)
        {
            return;
        }
            
        // Update camera position if attached
        if (attachCamera)
        {
            // Get current camera position
            Vector3 cameraPosition = mainCamera.transform.position;
            
            // Calculate target position (keep z the same)
            Vector3 targetPosition = new Vector3(transform.position.x, transform.position.y, cameraPosition.z);
            
            // Determine camera speed based on mode
            float currentCameraSpeed = spectatorMode ? 
                cameraFollowSpeed * overviewCameraSpeedMultiplier : cameraFollowSpeed;
            
            // Smoothly move camera
            mainCamera.transform.position = Vector3.Lerp(cameraPosition, targetPosition, Time.deltaTime * currentCameraSpeed);
        }
        
        // Handle camera zoom with mouse wheel
        if (enableCameraZoom)
        {
            float scrollDelta = Mouse.current.scroll.ReadValue().y;
            if (scrollDelta != 0)
            {
                // Get the camera's orthographic size
                Camera camera = mainCamera.GetComponent<Camera>();
                if (camera != null && camera.orthographic)
                {
                    // Adjust zoom based on scroll direction
                    float newSize = camera.orthographicSize - (scrollDelta * cameraZoomSpeed * 0.01f);
                    
                    // Clamp to min/max zoom levels
                    camera.orthographicSize = Mathf.Clamp(newSize, minZoom, maxZoom);
                }
            }
        }
    }
    
    // Configure joint stiffness for all limbs
    private void ConfigureJointStiffness()
    {
        // Configure arm joints
        ConfigureLimbJoint(rightArm, armJointForce);
        ConfigureLimbJoint(leftArm, armJointForce);
        ConfigureLimbJoint(rightForearm, armJointForce);
        ConfigureLimbJoint(leftForearm, armJointForce);
        
        // Configure hand joints with higher stiffness
        ConfigureLimbJoint(rightHand, handJointForce);
        ConfigureLimbJoint(leftHand, handJointForce);
    }
    
    // Configure a single limb joint
    private void ConfigureLimbJoint(ProceduralLimb limb, float motorForce)
    {
        if (limb == null) return;
        
        HingeJoint2D joint = limb.GetComponent<HingeJoint2D>();
        if (joint != null)
        {
            // Enable motor
            joint.useMotor = true;
            
            // Set motor force
            JointMotor2D motor = joint.motor;
            motor.maxMotorTorque = motorForce;
            joint.motor = motor;
            
            // Ensure collision is disabled
            joint.enableCollision = false;
        }
    }
    
    // Update head rotation to follow mouse (faster than torso)
    private void UpdateHeadRotation()
    {
        // Skip head rotation if in spectator mode or if no head or no aim input
        if (spectatorMode || head == null || aimInput.magnitude < 0.1f) return;
        
        // Calculate target angle for head
        float targetHeadAngle = Mathf.Atan2(aimInput.y, aimInput.x) * Mathf.Rad2Deg;
        
        // Get current torso angle
        float torsoAngle = torso != null ? torso.rotation : 0f;
        
        // Calculate angle difference between target and torso
        float angleDifference = Mathf.DeltaAngle(torsoAngle, targetHeadAngle);
        
        // Clamp the difference to max allowed head rotation
        angleDifference = Mathf.Clamp(angleDifference, -maxHeadAngleDifference, maxHeadAngleDifference);
        
        // Calculate final head angle - fixed to look at cursor
        float finalHeadAngle = -angleDifference; // Negative to fix the reversed rotation
        
        // Set head target angle
        head.SetTargetAngle(finalHeadAngle);
    }
    
    // Equip an item
    public void EquipItem(EquippableItem item)
    {
        // Unequip current item if any
        if (equippedItem != null)
        {
            UnequipItem();
        }
        
        // Set new equipped item
        equippedItem = item;
        
        if (item != null)
        {
            // Parent item to torso
            item.transform.SetParent(torso.transform);
            // #region agent log
            try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"hypothesisId\":\"C\",\"location\":\"EquipItem\",\"message\":\"after SetParent\",\"data\":{\"characterName\":\"" + (gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"itemName\":\"" + (item.gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"itemParentNow\":\"" + (item.transform.parent != null ? item.transform.parent.name : "null").Replace("\\","\\\\").Replace("\"","\\\"") + "\"},\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}\n"); } catch { }
            // #endregion
            // Set initial position
            UpdateEquippedItemPosition();
            
            // Handle one-handed weapon equipping (character-specific sprites)
            if (item is WeaponOneHanded oneHanded)
            {
                oneHanded.OnEquipped(this);
            }
            else
            {
                // Swap hand sprites if needed (generic items)
                if (item.rightHandSprite != null && rightHand != null)
                {
                    rightHand.SwapSprite(item.rightHandSprite);
                }
                
                if (item.leftHandSprite != null && leftHand != null)
                {
                    leftHand.SwapSprite(item.leftHandSprite);
                }
            }
        }
    }
    
    // Unequip current item
    public void UnequipItem()
    {
        UnequipItemToInventory();
    }
    
    /// <summary>
    /// Unequip current item and optionally add it to inventory
    /// </summary>
    public void UnequipItemToInventory(bool addToInventory = true)
    {
        if (equippedItem != null)
        {
            EquippableItem itemToUnequip = equippedItem;
            Item itemData = itemToUnequip.itemData;
            
            // Handle one-handed weapon unequipping (character-specific sprites)
            if (itemToUnequip is WeaponOneHanded oneHandedUnequip)
            {
                oneHandedUnequip.OnUnequipped(this);
            }
            else
            {
                // Reset hand sprites (generic items)
                if (rightHand != null)
                {
                    rightHand.SwapSprite(null); // Revert to default sprite
                }
                
                if (leftHand != null)
                {
                    leftHand.SwapSprite(null); // Revert to default sprite
                }
            }
            
            // Unparent item
            itemToUnequip.transform.SetParent(null);
            equippedItem = null;
            
            // Remove from owned weapon instances (weapon no longer equipped)
            if (itemData != null)
            {
                ownedWeaponInstances.Remove(itemData);
            }
            
            // Add to inventory if requested and item has itemData
            if (addToInventory && inventory != null && itemData != null)
            {
                inventory.AddItem(itemData, 1);
            }
            
            // Prefab-spawned weapons: destroy (will re-instantiate from Item.weaponPrefab on next equip)
            // Scene-based weapons or non-inventory items: deactivate for reuse
            if (itemData != null && itemData.weaponPrefab != null)
            {
                Destroy(itemToUnequip.gameObject);
            }
            else
            {
                itemToUnequip.gameObject.SetActive(false);
            }
        }
    }
    
    // Update equipped item position based on hand positions
    private void UpdateEquippedItemPosition()
    {
        if (equippedItem == null)
            return;
            
        // Position the item based on its configuration
        equippedItem.UpdateHandPositions(rightHand, leftHand);
    }
    
    /// <summary>
    /// Equip an item from inventory by index (toggles if already equipped)
    /// </summary>
    public bool EquipItemFromInventory(int slotIndex)
    {
        if (inventory == null)
            return false;
        
        InventoryItem invItem = inventory.GetItemAt(slotIndex);
        if (invItem == null || invItem.IsEmpty())
            return false;
        
        // Only weapons can be equipped this way for now
        if (invItem.item.itemType != Item.ItemType.Weapon)
            return false;
        
        // Check if this weapon is already equipped - if so, unequip it (toggle behavior)
        if (equippedItem != null && equippedItem.itemData == invItem.item)
        {
            UnequipItemToInventory(false); // Don't add back to inventory since it's already there
            return true;
        }
        
        // Try to find or create the weapon GameObject
        Weapon weapon = FindWeaponForItem(invItem.item);
        if (weapon == null)
        {
            Debug.LogWarning($"Could not find or create weapon GameObject for item: {invItem.item.itemName}");
            return false;
        }
        
        // Unequip current item to inventory
        if (equippedItem != null)
        {
            UnequipItemToInventory(false); // Don't add back to inventory since it's already there
        }
        
        // Equip the new weapon
        EquipItem(weapon);
        
        // Don't remove from inventory - keep it there like clothing items
        // This allows the equipped indicator to show and item to remain visible
        
        return true;
    }
    
    /// <summary>
    /// Equip a weapon by Item ScriptableObject reference
    /// </summary>
    public bool EquipItem(Item itemData)
    {
        if (itemData == null || itemData.itemType != Item.ItemType.Weapon)
            return false;
        
        if (inventory == null || !inventory.HasItem(itemData))
            return false;
        
        Weapon weapon = FindWeaponForItem(itemData);
        if (weapon == null)
            return false;
        
        // Unequip current item
        if (equippedItem != null)
        {
            UnequipItemToInventory(false); // Don't add back to inventory since it's already there
        }
        
        // Equip the weapon
        EquipItem(weapon);
        
        // Don't remove from inventory - keep it there like clothing items
        // This allows the equipped indicator to show and item to remain visible
        
        return true;
    }
    
    /// <summary>
    /// Check if a specific Item is currently equipped
    /// </summary>
    public bool IsEquipped(Item item)
    {
        if (item == null || equippedItem == null)
            return false;
        
        // Check if the equipped item's itemData matches
        return equippedItem.itemData == item;
    }
    
    /// <summary>
    /// Find or create a weapon GameObject for an Item ScriptableObject.
    /// Each character gets their own weapon instance to prevent shared state issues.
    /// </summary>
    private Weapon FindWeaponForItem(Item itemData)
    {
        if (itemData == null || itemData.itemType != Item.ItemType.Weapon)
            return null;
        
        // #region agent log
        string callerName = gameObject.name;
        // #endregion
        
        // Check if this character already owns a weapon instance for this item
        if (ownedWeaponInstances.TryGetValue(itemData, out Weapon ownedWeapon))
        {
            if (ownedWeapon != null)
            {
                // #region agent log
                try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"hypothesisId\":\"B\",\"location\":\"FindWeaponForItem\",\"message\":\"return owned\",\"data\":{\"callerName\":\"" + (callerName ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"weaponName\":\"" + (ownedWeapon.gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"weaponParent\":\"" + (ownedWeapon.transform.parent != null ? ownedWeapon.transform.parent.name : "null").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"cloned\":false},\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}\n"); } catch { }
                // #endregion
                ownedWeapon.gameObject.SetActive(true);
                return ownedWeapon;
            }
            else
            {
                // Clean up null reference
                ownedWeaponInstances.Remove(itemData);
            }
        }
        
        // Prefer instantiating from Item's weaponPrefab (project asset) - no scene object required
        if (itemData.weaponPrefab != null)
        {
            Weapon prefabWeapon = itemData.weaponPrefab.GetComponent<Weapon>();
            if (prefabWeapon != null)
            {
                GameObject newWeaponGO = Instantiate(itemData.weaponPrefab);
                newWeaponGO.name = $"{itemData.weaponPrefab.name}_{gameObject.name}";
                
                Weapon newWeapon = newWeaponGO.GetComponent<Weapon>();
                if (newWeapon != null)
                {
                    if (newWeapon.itemData == null)
                        newWeapon.itemData = itemData;
                    
                    ownedWeaponInstances[itemData] = newWeapon;
                    newWeaponGO.SetActive(true);
                    return newWeapon;
                }
            }
            else
            {
                Debug.LogWarning($"Item {itemData.itemName}'s weaponPrefab has no Weapon component.");
            }
        }
        
        // Fallback: find a template weapon in the scene to clone (legacy - requires scene object)
        Weapon templateWeapon = null;
        Weapon[] allWeapons = FindObjectsByType<Weapon>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var weapon in allWeapons)
        {
            // Check if weapon has matching itemData (from base EquippableItem class)
            if (weapon.itemData == itemData)
            {
                // Check if this weapon is already owned by another character
                bool isOwned = false;
                ProceduralCharacterController[] allCharacters = FindObjectsByType<ProceduralCharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var character in allCharacters)
                {
                    if (character != this && character.ownedWeaponInstances.TryGetValue(itemData, out Weapon charWeapon) && charWeapon == weapon)
                    {
                        isOwned = true;
                        break;
                    }
                }
                
                // If not owned by another character, use this as our instance
                if (!isOwned)
                {
                    // #region agent log
                    try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"hypothesisId\":\"B\",\"location\":\"FindWeaponForItem\",\"message\":\"claim unowned\",\"data\":{\"callerName\":\"" + (callerName ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"weaponName\":\"" + (weapon.gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"weaponParent\":\"" + (weapon.transform.parent != null ? weapon.transform.parent.name : "null").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"cloned\":false},\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}\n"); } catch { }
                    // #endregion
                    ownedWeaponInstances[itemData] = weapon;
                    weapon.gameObject.SetActive(true);
                    return weapon;
                }
                
                // Keep track of template for cloning if needed
                if (templateWeapon == null)
                {
                    templateWeapon = weapon;
                }
            }
        }
        
        // If all instances are owned, create a new one by cloning
        if (templateWeapon != null)
        {
            GameObject newWeaponGO = Instantiate(templateWeapon.gameObject);
            newWeaponGO.name = $"{templateWeapon.gameObject.name}_{gameObject.name}";
            
            Weapon newWeapon = newWeaponGO.GetComponent<Weapon>();
            if (newWeapon != null)
            {
                // #region agent log
                try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"hypothesisId\":\"A\",\"location\":\"FindWeaponForItem\",\"message\":\"clone\",\"data\":{\"callerName\":\"" + (callerName ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"weaponName\":\"" + (newWeaponGO.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"templateParent\":\"" + (templateWeapon.transform.parent != null ? templateWeapon.transform.parent.name : "null").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"cloned\":true},\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}\n"); } catch { }
                // #endregion
                ownedWeaponInstances[itemData] = newWeapon;
                newWeaponGO.SetActive(true);
                return newWeapon;
            }
        }
        
        // If not found, you'd normally instantiate from a prefab or pool
        Debug.LogWarning($"No weapon GameObject found for item: {itemData.itemName}. You may need to set up item prefab spawning.");
        return null;
    }
    
    /// <summary>
    /// Clean up owned weapon instances when character is destroyed or disabled
    /// </summary>
    private void CleanupOwnedWeapons()
    {
        foreach (var kvp in ownedWeaponInstances)
        {
            if (kvp.Value != null)
            {
                // Prefab-spawned: destroy. Scene-based: deactivate for reuse.
                if (kvp.Key != null && kvp.Key.weaponPrefab != null)
                    Destroy(kvp.Value.gameObject);
                else
                    kvp.Value.gameObject.SetActive(false);
            }
        }
        ownedWeaponInstances.Clear();
    }
    
    /// <summary>
    /// Pick up an item from the world and add it to inventory
    /// </summary>
    public bool PickupItem(ItemPickup pickup)
    {
        if (pickup == null || inventory == null)
            return false;
        
        if (pickup.item == null)
            return false;
        
        // Try to add item to inventory
        bool added = inventory.AddItem(pickup.item, pickup.quantity);
        
        if (added)
        {
            // Notify pickup that it was collected
            pickup.OnPickedUp();
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Use a consumable item from inventory. When slotIndex >= 0, removes from that slot (so the selected stack is used); otherwise removes from first stack of that item.
    /// </summary>
    public bool UseConsumable(Item item, int quantity = 1, int slotIndex = -1)
    {
        if (inventory == null || item == null)
            return false;
        
        if (item.itemType != Item.ItemType.Consumable)
            return false;
        
        if (slotIndex >= 0)
        {
            InventoryItem invItem = inventory.GetItemAt(slotIndex);
            if (invItem == null || invItem.IsEmpty() || invItem.item != item || invItem.quantity < quantity)
                return false;
        }
        else if (!inventory.HasItem(item, quantity))
            return false;
        
        bool wasUsed = false;
        if (item is ConsumableItem consumable)
        {
            for (int i = 0; i < quantity; i++)
            {
                if (consumable.Use(this))
                {
                    wasUsed = true;
                    if (slotIndex >= 0)
                        inventory.RemoveItemAt(slotIndex, 1);
                    else
                        inventory.RemoveItem(item, 1);
                }
                else
                    break;
            }
        }
        
        return wasUsed;
    }
    
    /// <summary>
    /// Use a consumable item on a specific limb. When slotIndex >= 0, removes from that slot (the selected stack); otherwise from first stack.
    /// </summary>
    public bool UseConsumableOnLimb(Item item, LimbType limbType, int quantity = 1, int slotIndex = -1)
    {
        if (inventory == null || item == null)
            return false;
        if (item.itemType != Item.ItemType.Consumable)
            return false;
        if (slotIndex >= 0)
        {
            InventoryItem invItem = inventory.GetItemAt(slotIndex);
            if (invItem == null || invItem.IsEmpty() || invItem.item != item || invItem.quantity < quantity)
                return false;
        }
        else if (!inventory.HasItem(item, quantity))
            return false;
        
        if (item is ConsumableItem consumable && consumable.UseOnLimb(this, limbType))
        {
            if (slotIndex >= 0)
                inventory.RemoveItemAt(slotIndex, 1);
            else
                inventory.RemoveItem(item, 1);
            return true;
        }
        return false;
    }
    
    // Possession-related method removed
    
    // Apply recoil force to arms
    public void ApplyRecoil(Vector2 recoilForce)
    {
        // Apply recoil to arms
        if (rightArm != null)
        {
            rightArm.ApplyImpulse(recoilForce);
        }
        
        if (rightForearm != null)
        {
            rightForearm.ApplyImpulse(recoilForce * 0.7f);
        }
        
        if (leftArm != null)
        {
            leftArm.ApplyImpulse(recoilForce * 0.5f);
        }
        
        if (leftForearm != null)
        {
            leftForearm.ApplyImpulse(recoilForce * 0.3f);
        }
    }
    
    // Set spectator mode
    public void SetSpectatorMode(bool enabled)
    {
        spectatorMode = enabled;
        
        // Lock/unlock torso Z rotation based on spectator mode
        if (torso != null)
        {
            if (enabled)
            {
                // Lock Z rotation in spectator mode to prevent drift
                torso.constraints = RigidbodyConstraints2D.FreezeRotation;
                
                // Reset movement input and velocity when entering spectator mode
                // This prevents the character from continuing to walk after releasing control
                moveInput = Vector2.zero;
                targetVelocity = Vector2.zero;
                currentVelocity = Vector2.zero;
                
                // Also reset the rigidbody velocity
                torso.linearVelocity = Vector2.zero;
                
                // Stop any ongoing weapon animations when entering spectator mode
                if (equippedItem != null && equippedItem.waypointAnimation != null)
                {
                    equippedItem.waypointAnimation.Stop();
                }
            }
            else
            {
                // Allow rotation when player controlled
                torso.constraints = RigidbodyConstraints2D.None;
                
                // When regaining control, ignore attack only this frame so the click that possessed us doesn't swing the weapon
                ignoreNextAttackInput = true;
                possessedFrame = Time.frameCount;
                
                // When regaining control, ensure weapon animation is in clean state
                if (equippedItem != null && equippedItem.waypointAnimation != null)
                {
                    equippedItem.waypointAnimation.Stop();
                }
            }
        }
        
        // Reset head rotation when entering spectator mode
        if (enabled && head != null)
        {
            // Use smooth rotation to zero
            head.SmoothRotateToZero();
        }
    }
    
    // Check if spectator mode should be enabled or disabled
    private void CheckSpectatorMode()
    {
        if (!autoCheckSpectatorMode)
            return;
            
        bool shouldBeInSpectatorMode = true;
        
        // Find all spectator controllers in the scene
        SpectatorController[] spectators = GameObject.FindObjectsByType<SpectatorController>(FindObjectsSortMode.None);
        
        // Check if any spectator is controlling this character
        foreach (SpectatorController spectator in spectators)
        {
            if (spectator.IsControllingCharacter(this))
            {
                // If a spectator is controlling this character, it should NOT be in spectator mode
                // This gives the player direct control
                shouldBeInSpectatorMode = false;
                break;
            }
        }
        
        // Update spectator mode if needed
        if (shouldBeInSpectatorMode != spectatorMode)
        {
            SetSpectatorMode(shouldBeInSpectatorMode);
        }
    }
    
    // Configure limb colliders and prevent self-collisions
    private void ConfigureLimbColliders()
    {
        // Get all limbs
        List<ProceduralLimb> limbs = new List<ProceduralLimb>();
        
        // Add all limbs to the list if they exist
        if (head != null) limbs.Add(head);
        if (rightArm != null) limbs.Add(rightArm);
        if (rightForearm != null) limbs.Add(rightForearm);
        if (rightHand != null) limbs.Add(rightHand);
        if (leftArm != null) limbs.Add(leftArm);
        if (leftForearm != null) limbs.Add(leftForearm);
        if (leftHand != null) limbs.Add(leftHand);
        
        // Get torso collider
        Collider2D torsoCollider = torso?.GetComponent<Collider2D>();
        
        // Ensure each limb has a collider
        foreach (ProceduralLimb limb in limbs)
        {
            // Get or add a collider to the limb
            Collider2D limbCollider = limb.GetComponent<Collider2D>();
            if (limbCollider == null)
            {
                // Add a box collider if none exists
                limbCollider = limb.gameObject.AddComponent<BoxCollider2D>();
                
                // Size the collider to match the sprite
                SpriteRenderer spriteRenderer = limb.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null && limbCollider is BoxCollider2D boxCollider)
                {
                    // Make the collider slightly smaller than the sprite
                    boxCollider.size = new Vector2(
                        spriteRenderer.bounds.size.x * 0.8f,
                        spriteRenderer.bounds.size.y * 0.8f
                    );
                }
            }
            
            // Ensure the collider is enabled
            limbCollider.enabled = true;
            
            // Ignore collision with torso
            if (torsoCollider != null)
            {
                Physics2D.IgnoreCollision(limbCollider, torsoCollider);
            }
            
            // Ignore collisions with other limbs
            foreach (ProceduralLimb otherLimb in limbs)
            {
                if (limb != otherLimb)
                {
                    Collider2D otherCollider = otherLimb.GetComponent<Collider2D>();
                    if (otherCollider != null)
                    {
                        Physics2D.IgnoreCollision(limbCollider, otherCollider);
                    }
                }
            }
        }
    }
    
    // Initialize limb map and subscribe to health change events
    private void InitializeLimbMap()
    {
        // Clear existing map
        limbMap.Clear();
        
        // Add all limbs to the map if they exist
        if (head != null) {
            limbMap[LimbType.Head] = head;
            head.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.Head, current, max);
        }
        
        // Add neck to the map if it exists (check public field first, then try dynamic finding)
        if (neck == null)
        {
            neck = transform.Find("Neck")?.GetComponent<ProceduralLimb>();
        }
        if (neck != null) {
            limbMap[LimbType.Neck] = neck;
            neck.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.Neck, current, max);
        }
        
        if (rightArm != null) {
            limbMap[LimbType.RightBicep] = rightArm;
            rightArm.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.RightBicep, current, max);
        }
        
        if (rightForearm != null) {
            limbMap[LimbType.RightForearm] = rightForearm;
            rightForearm.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.RightForearm, current, max);
        }
        
        if (rightHand != null) {
            limbMap[LimbType.RightHand] = rightHand;
            rightHand.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.RightHand, current, max);
        }
        
        if (leftArm != null) {
            limbMap[LimbType.LeftBicep] = leftArm;
            leftArm.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.LeftBicep, current, max);
        }
        
        if (leftForearm != null) {
            limbMap[LimbType.LeftForearm] = leftForearm;
            leftForearm.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.LeftForearm, current, max);
        }
        
        if (leftHand != null) {
            limbMap[LimbType.LeftHand] = leftHand;
            leftHand.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.LeftHand, current, max);
        }
        
        // Add legs to the map if they exist
        if (rightThigh != null) {
            limbMap[LimbType.RightThigh] = rightThigh;
            rightThigh.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.RightThigh, current, max);
        }
        
        if (rightCalf != null) {
            limbMap[LimbType.RightCalf] = rightCalf;
            rightCalf.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.RightCalf, current, max);
        }
        
        if (rightFoot != null) {
            limbMap[LimbType.RightFoot] = rightFoot;
            rightFoot.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.RightFoot, current, max);
        }
        
        if (leftThigh != null) {
            limbMap[LimbType.LeftThigh] = leftThigh;
            leftThigh.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.LeftThigh, current, max);
        }
        
        if (leftCalf != null) {
            limbMap[LimbType.LeftCalf] = leftCalf;
            leftCalf.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.LeftCalf, current, max);
        }
        
        if (leftFoot != null) {
            limbMap[LimbType.LeftFoot] = leftFoot;
            leftFoot.OnHealthChanged += (current, max) => OnLimbHealthChanged?.Invoke(LimbType.LeftFoot, current, max);
        }
        
        // Subscribe to limb health changes to update overall health
        foreach (var limb in limbMap.Values)
        {
            if (limb.affectsCharacterHealth)
            {
                limb.OnHealthChanged += (current, max) => UpdateOverallHealth();
            }
            limb.SetCharacterController(this);
        }
    }
    
    
    
    // Update overall health based on limb health and torso health
    private void UpdateOverallHealth()
    {
        // Calculate overall health as average of all limbs that affect character health, plus torso
        float totalHealth = 0f;
        int count = 0;
        
        // Add torso health to the calculation (torso always affects overall health)
        float torsoHealthPercentage = torsoMaxHealth > 0 ? torsoHealth / torsoMaxHealth : 1f;
        totalHealth += torsoHealthPercentage;
        count++;
        
        // Add limb health percentages
        foreach (var limb in limbMap.Values)
        {
            if (limb.affectsCharacterHealth)
            {
                totalHealth += limb.GetHealthPercentage();
                count++;
            }
        }
        
        // Calculate average health percentage (including torso)
        float healthPercentage = count > 0 ? totalHealth / count : 1f;
        
        // Update current health
        float previousHealth = currentHealth;
        currentHealth = healthPercentage * maxHealth;
        
        // Invoke health changed event if health changed
        if (previousHealth != currentHealth)
        {
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }
    }
    
    // Apply damage to a specific limb (with optional damage type for armor mitigation and part status)
    public float ApplyDamageToLimb(LimbType limbType, float amount, Weapon.DamageType damageType = Weapon.DamageType.Generic)
    {
        return ApplyDamageToLimb(limbType, amount, damageType, null);
    }

    /// <summary>
    /// Apply damage with optional hit context (position, direction, critical). When provided and actual damage > 0, fires OnDamageDealt for blood/spray.
    /// </summary>
    public float ApplyDamageToLimb(LimbType limbType, float amount, Weapon.DamageType damageType, HitContext? hitContext)
    {
        // Record last damage type for this limb (for body-part UI wording)
        lastDamageTypeByLimb[limbType] = damageType;
        // Track all damage types that have affected this limb
        if (!damageTypesByLimb.TryGetValue(limbType, out HashSet<Weapon.DamageType> set))
        {
            set = new HashSet<Weapon.DamageType>();
            damageTypesByLimb[limbType] = set;
        }
        set.Add(damageType);

        // Blunt trauma: bashed limbs take more damage from all sources
        amount *= GetBluntTraumaVulnerabilityMultiplier(limbType);

        // Apply clothing mitigation (negative = more damage)
        if (clothingController != null)
        {
            float mitigation = clothingController.GetDamageMitigationForLimb(limbType, damageType);
            float mult = Mathf.Clamp(1f - mitigation, 0.01f, 2f);
            amount *= mult;
        }

        float actualDamage = 0f;
        float currentHealthAfter = 0f;
        float maxHealthForPart = 0f;
        
        // Special handling for Torso since it's not a ProceduralLimb
        if (limbType == LimbType.Torso)
        {
            float oldTorsoHealth = torsoHealth;
            torsoHealth = Mathf.Max(0f, torsoHealth - amount);
            previousTorsoHealth = torsoHealth; // Update tracking variable for inspector change detection
            actualDamage = oldTorsoHealth - torsoHealth;
            currentHealthAfter = torsoHealth;
            maxHealthForPart = torsoMaxHealth;
            
            if (damageType == Weapon.DamageType.Blunt && actualDamage > 0f)
            {
                if (!bluntTraumaByLimb.ContainsKey(limbType)) bluntTraumaByLimb[limbType] = 0f;
                bluntTraumaByLimb[limbType] += actualDamage;
            }
            
            // Fire Torso health changed event
            OnLimbHealthChanged?.Invoke(LimbType.Torso, torsoHealth, torsoMaxHealth);
            
            // Update overall character health (torso affects overall health)
            UpdateOverallHealth();
            
            if (actualDamage > 0f)
            {
                AddPainToLimb(limbType, actualDamage);
                if (hitContext.HasValue && hitContext.Value.HasValidDirection)
                    OnDamageDealt?.Invoke(hitContext.Value.worldPosition, hitContext.Value.attackDirection, actualDamage, damageType, limbType, hitContext.Value.isCritical, hitContext.Value.contactTransform);
                if (logDamageToConsole)
                    Debug.Log($"[Damage] {gameObject.name} | {limbType}: {actualDamage:F1} (current: {currentHealthAfter:F0}/{maxHealthForPart:F0})");
            }
            return actualDamage;
        }
        
        // Handle normal limb damage
        if (limbMap.TryGetValue(limbType, out ProceduralLimb limb))
        {
            actualDamage = limb.TakeDamage(amount);
            if (damageType == Weapon.DamageType.Blunt && actualDamage > 0f)
            {
                if (!bluntTraumaByLimb.ContainsKey(limbType)) bluntTraumaByLimb[limbType] = 0f;
                bluntTraumaByLimb[limbType] += actualDamage;
            }
            if (actualDamage > 0f)
            {
                AddPainToLimb(limbType, actualDamage);
                if (hitContext.HasValue && hitContext.Value.HasValidDirection)
                    OnDamageDealt?.Invoke(hitContext.Value.worldPosition, hitContext.Value.attackDirection, actualDamage, damageType, limbType, hitContext.Value.isCritical, hitContext.Value.contactTransform);
            }
            if (logDamageToConsole && actualDamage > 0f)
            {
                float current = limb.GetCurrentHealth();
                float max = limb.maxHealth;
                Debug.Log($"[Damage] {gameObject.name} | {limbType}: {actualDamage:F1} (current: {current:F0}/{max:F0})");
            }
            return actualDamage;
        }
        return 0f;
    }

    private float GetBluntTraumaVulnerabilityMultiplier(LimbType limbType)
    {
        float maxHealth = limbType == LimbType.Torso ? GetTorsoMaxHealth() : (GetLimb(limbType) != null ? GetLimb(limbType).maxHealth : 1f);
        if (maxHealth < 1f) maxHealth = 1f;
        float trauma = bluntTraumaByLimb.TryGetValue(limbType, out float t) ? t : 0f;
        float normalized = trauma / maxHealth;
        float bonus = Mathf.Min(normalized * bluntTraumaVulnerabilityScale, bluntTraumaVulnerabilityCap);
        return 1f + bonus;
    }

    /// <summary>
    /// Laceration severity tier from limb health: 0 = light, 1 = medium, 2 = heavy. Used by consumables (bandage vs stitches).
    /// </summary>
    public int GetLacerationSeverityTierForLimb(LimbType limb)
    {
        float healthPercent = limb == LimbType.Torso
            ? (GetTorsoMaxHealth() > 0f ? GetTorsoHealth() / GetTorsoMaxHealth() : 1f)
            : GetLimbHealthPercentage(limb);
        if (healthPercent > lacerationLightThreshold) return 0;
        if (healthPercent > lacerationHeavyThreshold) return 1;
        return 2;
    }

    /// <summary>
    /// Get the last damage type that hit this limb (for UI wording, e.g. "badly slashed"). Returns null if never hit.
    /// </summary>
    public Weapon.DamageType? GetLastDamageTypeForLimb(LimbType limb)
    {
        return lastDamageTypeByLimb.TryGetValue(limb, out Weapon.DamageType type) ? (Weapon.DamageType?)type : null;
    }

    /// <summary>
    /// Get all damage types that have affected this limb (Stab, Slash, Blunt; excludes Generic). For UI condition list.
    /// </summary>
    public IReadOnlyCollection<Weapon.DamageType> GetDamageTypesForLimb(LimbType limb)
    {
        if (!damageTypesByLimb.TryGetValue(limb, out HashSet<Weapon.DamageType> set))
            return Array.Empty<Weapon.DamageType>();
        return set.Where(t => t != Weapon.DamageType.Generic).ToList();
    }

    /// <summary>
    /// Get current pain value for this limb (0 to maxPainPerLimb).
    /// </summary>
    public float GetPainForLimb(LimbType limb)
    {
        return painByLimb.TryGetValue(limb, out float p) ? Mathf.Min(p, maxPainPerLimb) : 0f;
    }

    /// <summary>
    /// Get pain as 0–1 for UI (e.g. pain meter).
    /// </summary>
    public float GetPainPercentForLimb(LimbType limb)
    {
        if (maxPainPerLimb <= 0f) return 0f;
        return Mathf.Clamp01(GetPainForLimb(limb) / maxPainPerLimb);
    }

    private void AddPainToLimb(LimbType limbType, float actualDamage)
    {
        float add = actualDamage * painPerDamage;
        painByLimb.TryGetValue(limbType, out float current);
        painByLimb[limbType] = Mathf.Min(current + add, maxPainPerLimb * 1.5f); // allow slight overcap for display
    }
    
    // Apply damage to all limbs
    public void ApplyDamageToAllLimbs(float amount)
    {
        foreach (var limb in limbMap.Values)
        {
            limb.TakeDamage(amount);
        }
    }
    
    // Heal a specific limb (or torso; does not directly heal overall/total HP)
    public float HealLimb(LimbType limbType, float amount)
    {
        // Special handling for Torso since it's not a ProceduralLimb
        if (limbType == LimbType.Torso)
        {
            float oldTorsoHealth = torsoHealth;
            torsoHealth = Mathf.Min(torsoMaxHealth, torsoHealth + amount);
            previousTorsoHealth = torsoHealth;
            float actualHealed = torsoHealth - oldTorsoHealth;
            OnLimbHealthChanged?.Invoke(LimbType.Torso, torsoHealth, torsoMaxHealth);
            UpdateOverallHealth();
            return actualHealed;
        }
        if (limbMap.TryGetValue(limbType, out ProceduralLimb limb))
        {
            return limb.Heal(amount);
        }
        return 0f;
    }
    
    // Heal all limbs and torso (does not directly heal overall/total HP)
    public void HealAllLimbs(float amount)
    {
        foreach (var limb in limbMap.Values)
        {
            limb.Heal(amount);
        }
        // Heal torso separately (torso is not in limbMap)
        float oldTorsoHealth = torsoHealth;
        torsoHealth = Mathf.Min(torsoMaxHealth, torsoHealth + amount);
        previousTorsoHealth = torsoHealth;
        if (torsoHealth != oldTorsoHealth)
        {
            OnLimbHealthChanged?.Invoke(LimbType.Torso, torsoHealth, torsoMaxHealth);
            UpdateOverallHealth();
        }
    }
    
    // Reset all limb health to maximum
    public void ResetAllLimbHealth()
    {
        foreach (var limb in limbMap.Values)
        {
            limb.ResetHealth();
        }
    }
    
    // Get a limb by type
    public ProceduralLimb GetLimb(LimbType limbType)
    {
        if (limbMap.TryGetValue(limbType, out ProceduralLimb limb))
        {
            return limb;
        }
        return null;
    }

    /// <summary>
    /// Get the transform to use for attaching blood/stains to a limb (or torso). Used by blood spray to overlay stains on the victim.
    /// </summary>
    public Transform GetTransformForLimb(LimbType limbType)
    {
        if (limbType == LimbType.Torso && torso != null)
            return torso.transform;
        var limb = GetLimb(limbType);
        return limb != null ? limb.transform : (torso != null ? torso.transform : transform);
    }
    
    /// <summary>
    /// Order to check limbs: most specific (end of chain) first, so hand/forearm hit returns
    /// Hand/Forearm, not the parent Bicep. Arm chain: Bicep -> Forearm -> Hand.
    /// </summary>
    private static readonly LimbType[] LimbSpecificityOrder = new LimbType[]
    {
        LimbType.RightHand, LimbType.LeftHand,
        LimbType.RightForearm, LimbType.LeftForearm,
        LimbType.RightBicep, LimbType.LeftBicep,
        LimbType.RightFoot, LimbType.LeftFoot,
        LimbType.RightCalf, LimbType.LeftCalf,
        LimbType.RightThigh, LimbType.LeftThigh,
        LimbType.Head, LimbType.Neck
    };

    /// <summary>
    /// Get the LimbType for a collider that belongs to this character (torso or any limb).
    /// Returns null if the collider does not belong to this character.
    /// Checks most-specific limbs first (Hand, Forearm before Bicep) so contact on hand/forearm
    /// damages that part, not the parent.
    /// </summary>
    public LimbType? GetLimbTypeForCollider(Collider2D collider)
    {
        if (collider == null) return null;
        
        Transform colliderTransform = collider.transform;
        
        // Check limbs in specificity order (Hand/Forearm before Bicep) so we damage the contacted part
        foreach (LimbType limbType in LimbSpecificityOrder)
        {
            if (limbMap.TryGetValue(limbType, out ProceduralLimb limb) && limb != null
                && (colliderTransform == limb.transform || colliderTransform.IsChildOf(limb.transform)))
            {
                return limbType;
            }
        }
        
        // Check torso last (least specific)
        if (torso != null && (colliderTransform == torso.transform || colliderTransform.IsChildOf(torso.transform)))
        {
            return LimbType.Torso;
        }
        
        return null;
    }
    
    /// <summary>
    /// Select a random limb for knife torso-contact damage. High bias: Head, Neck, Torso. Low bias: Forearms, Hands, Thighs, Feet, Calves.
    /// </summary>
    public LimbType SelectKnifeTorsoContactLimb(float headChance, float neckChance, float torsoChance,
        float forearmChance, float handChance, float thighChance, float calfChance, float footChance)
    {
        float total = headChance + neckChance + torsoChance
            + (forearmChance * 2) + (handChance * 2)
            + (thighChance * 2) + (calfChance * 2) + (footChance * 2);
        float r = UnityEngine.Random.Range(0f, total);
        
        if ((r -= headChance) <= 0) return LimbType.Head;
        if ((r -= neckChance) <= 0) return LimbType.Neck;
        if ((r -= torsoChance) <= 0) return LimbType.Torso;
        if ((r -= forearmChance) <= 0) return LimbType.RightForearm;
        if ((r -= forearmChance) <= 0) return LimbType.LeftForearm;
        if ((r -= handChance) <= 0) return LimbType.RightHand;
        if ((r -= handChance) <= 0) return LimbType.LeftHand;
        if ((r -= thighChance) <= 0) return LimbType.RightThigh;
        if ((r -= thighChance) <= 0) return LimbType.LeftThigh;
        if ((r -= calfChance) <= 0) return LimbType.RightCalf;
        if ((r -= calfChance) <= 0) return LimbType.LeftCalf;
        if ((r -= footChance) <= 0) return LimbType.RightFoot;
        return LimbType.LeftFoot;
    }
    
    /// <summary>
    /// Get the currently selected weapon sprite override
    /// </summary>
    public WeaponSpriteOverride GetSelectedSpriteOverride()
    {
        if (weaponSpriteOverrides == null || weaponSpriteOverrides.Count == 0)
            return null;
        
        int index = Mathf.Clamp(selectedSpriteOverrideIndex, 0, weaponSpriteOverrides.Count - 1);
        return weaponSpriteOverrides[index];
    }
    
    /// <summary>
    /// Get weapon sprite override by index
    /// </summary>
    public WeaponSpriteOverride GetSpriteOverride(int index)
    {
        if (weaponSpriteOverrides == null || index < 0 || index >= weaponSpriteOverrides.Count)
            return null;
        
        return weaponSpriteOverrides[index];
    }
    
    // Get current health (public getter for private field)
    public float GetCurrentHealth()
    {
        return currentHealth;
    }
    
    // Get max health (public getter)
    public float GetMaxHealth()
    {
        return maxHealth;
    }
    
    // Get torso current health
    public float GetTorsoHealth()
    {
        return torsoHealth;
    }
    
    // Get torso max health
    public float GetTorsoMaxHealth()
    {
        return torsoMaxHealth;
    }
    
    // Get health percentage for a specific limb
    public float GetLimbHealthPercentage(LimbType limbType)
    {
        if (limbMap.TryGetValue(limbType, out ProceduralLimb limb))
        {
            return limb.GetHealthPercentage();
        }
        return 1f; // Default to full health if limb not found
    }
    
    // Select a random limb based on weighted hit probabilities
    private LimbType SelectRandomLimb(List<Weapon.LimbHitProbability> hitProbabilities)
    {
        if (hitProbabilities == null || hitProbabilities.Count == 0)
        {
            // Default to torso if no probabilities provided
            return LimbType.Torso;
        }
        
        // Calculate total probability (may not sum to 100%)
        float totalProbability = 0f;
        foreach (var prob in hitProbabilities)
        {
            totalProbability += prob.hitChance;
        }
        
        // Normalize if total is not 100%
        float normalizationFactor = totalProbability > 0f ? 100f / totalProbability : 1f;
        
        // Generate random value between 0 and 100
        float randomValue = UnityEngine.Random.Range(0f, 100f);
        
        // Select limb using weighted random
        float cumulativeProbability = 0f;
        foreach (var prob in hitProbabilities)
        {
            float normalizedChance = prob.hitChance * normalizationFactor;
            cumulativeProbability += normalizedChance;
            
            if (randomValue <= cumulativeProbability)
            {
                return prob.limbType;
            }
        }
        
        // Fallback: return last limb in list (shouldn't reach here if probabilities are correct)
        return hitProbabilities[hitProbabilities.Count - 1].limbType;
    }
    
    // Apply damage using probability-based limb selection
    public float TakeDamageWithProbability(float baseDamage, List<Weapon.LimbHitProbability> hitProbabilities, Weapon.DamageType damageType = Weapon.DamageType.Generic)
    {
        if (hitProbabilities == null || hitProbabilities.Count == 0)
        {
            // Default to torso damage if no probabilities provided
            return ApplyDamageToLimb(LimbType.Torso, baseDamage, damageType);
        }
        
        // Select random limb based on probabilities
        LimbType selectedLimb = SelectRandomLimb(hitProbabilities);
        
        // Apply damage to selected limb
        float damageDealt = ApplyDamageToLimb(selectedLimb, baseDamage, damageType);
        
        return damageDealt;
    }
    
    // Convenience method to take damage from a weapon (weapon applies damage via its own TakeDamageWithWeapon)
    public float TakeDamageWithWeapon(Weapon weapon)
    {
        if (weapon == null)
            return 0f;
        return weapon.TakeDamageWithWeapon(this);
    }

    /// <summary>
    /// Called by BleedingController when blood level reaches 0. Triggers death, puts character in spectator mode.
    /// SpectatorController should release control in response to OnDeath.
    /// </summary>
    public void TriggerDeathFromBloodLoss()
    {
        if (IsDead)
            return;
        IsDead = true;
        SetSpectatorMode(true); // Stop movement/control
        OnDeath?.Invoke();
    }
}
