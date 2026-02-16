using UnityEngine;
using System;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(HingeJoint2D))]
public class ProceduralLimb : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional sprite to swap when equipping items")]
    public SpriteRenderer limbSprite;
    
    [Header("Health Settings")]
    [Tooltip("Maximum health for this limb")]
    public float maxHealth = 100f;
    
    [Tooltip("Current health of the limb")]
    [SerializeField] private float currentHealth;
    
    [Tooltip("Whether damage to this limb affects overall character health")]
    public bool affectsCharacterHealth = true;
    
    [Tooltip("Multiplier for damage taken by this limb")]
    [Range(0.1f, 2f)]
    public float damageMultiplier = 1f;
    
    [Header("Motor Weakness (Damage / Blood Loss)")]
    [Tooltip("Minimum motor strength at 0% limb health (avoids fully paralyzed limb). 1 = no scaling.")]
    [Range(0f, 1f)]
    public float minMotorStrengthAtZeroHealth = 0.2f;
    [Tooltip("Minimum motor strength at 0% blood level (avoids full paralysis before death). 1 = no scaling.")]
    [Range(0f, 1f)]
    public float minMotorStrengthAtZeroBlood = 0.25f;
    
    // Event for when limb health changes
    public event Action<float, float> OnHealthChanged; // current, max
    
    // Components
    private Rigidbody2D rb;
    private HingeJoint2D joint;
    // Optional: set by ProceduralCharacterController in InitializeLimbMap for blood-level scaling
    private ProceduralCharacterController characterController;
    
    // Track previous health value to detect inspector changes at runtime
    private float previousHealth = -1f;
    
    // Store original sprite for restoration when unequipping items
    private Sprite originalSprite;
    private bool originalSpriteStored = false;
    
    private void Awake()
    {
        // Get components
        rb = GetComponent<Rigidbody2D>();
        joint = GetComponent<HingeJoint2D>();

        // Auto-wire limb sprite if not assigned (helps clothing + weapon sprite swaps)
        if (limbSprite == null)
        {
            limbSprite = GetComponent<SpriteRenderer>();
            if (limbSprite == null)
            {
                limbSprite = GetComponentInChildren<SpriteRenderer>();
            }
        }
        
        // Configure rigidbody for procedural movement
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        
        // Initialize health
        currentHealth = maxHealth;
        previousHealth = currentHealth;
        
        // Store the original sprite if it exists
        if (limbSprite != null && !originalSpriteStored)
        {
            originalSprite = limbSprite.sprite;
            originalSpriteStored = true;
        }
    }
    
    // Detect inspector changes (runs in edit mode and play mode when inspector values change)
    private void OnValidate()
    {
        // Fire event if health changed (only in play mode, to avoid spamming in edit mode)
        if (Application.isPlaying && previousHealth >= 0f && currentHealth != previousHealth)
        {
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            previousHealth = currentHealth;
        }
    }
    
    // Check for runtime inspector changes (since OnValidate might not always fire in play mode)
    private void Update()
    {
        // Only check if we're in play mode and have a valid previous health
        if (Application.isPlaying && previousHealth >= 0f && currentHealth != previousHealth)
        {
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            previousHealth = currentHealth;
        }
    }
    
    /// <summary>Set by ProceduralCharacterController when limb is registered. Used for blood-level motor scaling.</summary>
    public void SetCharacterController(ProceduralCharacterController controller)
    {
        characterController = controller;
    }
    
    /// <summary>Default motor torque when not overridden (idle/walk).</summary>
    public const float DefaultMotorTorque = 100f;
    /// <summary>Default motor speed multiplier when not overridden.</summary>
    public const float DefaultMotorSpeedMultiplier = 5f;

    /// <summary>Combined scale for motor strength from limb health and character blood (0-1). Used by SetExactAngle and SmoothRotateToZero.</summary>
    private float GetMotorStrengthScale()
    {
        float damageFactor = Mathf.Lerp(minMotorStrengthAtZeroHealth, 1f, GetHealthPercentage());
        float bloodFactor = 1f;
        if (characterController != null)
        {
            BleedingController bc = characterController.GetComponent<BleedingController>();
            if (bc != null)
                bloodFactor = Mathf.Lerp(minMotorStrengthAtZeroBlood, 1f, bc.GetBloodLevelPercent());
        }
        return Mathf.Max(0.01f, damageFactor * bloodFactor);
    }

    // Method to set a new target angle (used for animation or control)
    // Respects the joint's configured angle limits - does not modify them.
    public void SetTargetAngle(float newAngle)
    {
        SetTargetAngle(newAngle, DefaultMotorTorque, DefaultMotorSpeedMultiplier);
    }

    /// <summary>
    /// Set target angle with explicit motor strength. Use higher torque/speed when animations
    /// must win over movement (e.g. attacks) so joints don't get overpowered by torso motion.
    /// Motor strength and speed are scaled by limb health and character blood level (weaker when damaged or low blood).
    /// </summary>
    public void SetTargetAngle(float newAngle, float maxMotorTorque, float motorSpeedMultiplier)
    {
        if (joint != null)
        {
            // Clamp target to joint's configured limits so we never ask motor to drive outside allowed range
            float clampedTarget = newAngle;
            if (joint.useLimits)
            {
                clampedTarget = Mathf.Clamp(newAngle, joint.limits.min, joint.limits.max);
            }
            
            float scale = GetMotorStrengthScale();
            float effectiveTorque = Mathf.Max(1f, maxMotorTorque * scale);
            float effectiveSpeedMultiplier = Mathf.Max(0.5f, motorSpeedMultiplier * scale);
            
            // Enable motor to move toward the target angle (limits constrain movement)
            joint.useMotor = true;
            JointMotor2D motor = joint.motor;
            motor.maxMotorTorque = effectiveTorque;
            
            // Calculate current angle and set motor direction
            float currentAngle = joint.jointAngle;
            float angleDifference = Mathf.DeltaAngle(currentAngle, clampedTarget);
            motor.motorSpeed = angleDifference * effectiveSpeedMultiplier;
            
            joint.motor = motor;
        }
    }
    
    // Method to set an exact angle (for spectator mode head positioning)
    // Respects the joint's configured angle limits - does not modify them. Motor strength scaled by damage/blood.
    public void SetExactAngle(float exactAngle)
    {
        if (joint != null)
        {
            // Clamp target to joint's configured limits
            float clampedTarget = exactAngle;
            if (joint.useLimits)
            {
                clampedTarget = Mathf.Clamp(exactAngle, joint.limits.min, joint.limits.max);
            }
            
            float scale = GetMotorStrengthScale();
            joint.useMotor = true;
            JointMotor2D motor = joint.motor;
            motor.maxMotorTorque = Mathf.Max(1f, 200f * scale);
            float currentAngle = joint.jointAngle;
            float angleDifference = Mathf.DeltaAngle(currentAngle, clampedTarget);
            motor.motorSpeed = angleDifference * Mathf.Max(0.5f, 10f * scale);
            joint.motor = motor;
        }
    }
    
    // Coroutine for smooth rotation to zero with guaranteed end position
    private Coroutine smoothRotationCoroutine;
    
    // Method to smoothly rotate to zero with guaranteed end position
    public void SmoothRotateToZero()
    {
        // Stop any existing rotation coroutine
        if (smoothRotationCoroutine != null)
        {
            MonoBehaviour owner = this as MonoBehaviour;
            owner.StopCoroutine(smoothRotationCoroutine);
        }
        
        // Start a new smooth rotation coroutine
        smoothRotationCoroutine = StartCoroutine(SmoothRotateToZeroCoroutine());
    }
    
    // Coroutine that handles the smooth rotation to zero.
    // Respects the joint's configured angle limits - does not modify them.
    private System.Collections.IEnumerator SmoothRotateToZeroCoroutine()
    {
        if (joint != null)
        {
            // Get starting angle
            float startAngle = joint.jointAngle;
            float duration = 0.8f; // Longer duration for smoother rotation
            float elapsedTime = 0f;
            
            // Clamp target to joint's configured limits (zero may be outside limits)
            float targetAngle = 0f;
            if (joint.useLimits)
            {
                targetAngle = Mathf.Clamp(0f, joint.limits.min, joint.limits.max);
            }
            
            // Get rigidbody for velocity control
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            float initialAngularVelocity = rb != null ? rb.angularVelocity : 0f;
            
            // Use smooth easing function for natural movement - motor drives toward target
            while (elapsedTime < duration)
            {
                // Calculate progress with smooth easing
                float t = elapsedTime / duration;
                // Smoothstep for ease-in-out effect
                t = t * t * (3f - 2f * t); 
                
                // Interpolate target along path (start -> targetAngle)
                float currentTarget = Mathf.Lerp(startAngle, targetAngle, t);
                
                // Gradually reduce angular velocity to prevent jolt
                if (rb != null)
                {
                    float velocityReduction = Mathf.Lerp(initialAngularVelocity, 0f, t * t);
                    rb.angularVelocity = velocityReduction;
                }
                
                // Motor drives toward interpolated target (limits constrain movement); scale by damage/blood
                float scale = GetMotorStrengthScale();
                joint.useMotor = true;
                JointMotor2D motor = joint.motor;
                motor.maxMotorTorque = Mathf.Max(1f, Mathf.Lerp(30f, 80f, t) * scale);
                float currentAngle = joint.jointAngle;
                float angleDifference = Mathf.DeltaAngle(currentAngle, currentTarget);
                float speedMultiplier = Mathf.Lerp(8f, 3f, t) * scale;
                motor.motorSpeed = angleDifference * Mathf.Max(0.5f, speedMultiplier);
                joint.motor = motor;
                
                elapsedTime += Time.deltaTime;
                yield return null;
            }
            
            // Final phase - gentle approach to target (within limits)
            float finalDuration = 0.3f;
            float finalElapsedTime = 0f;
            
            while (finalElapsedTime < finalDuration)
            {
                float scale = GetMotorStrengthScale();
                joint.useMotor = true;
                JointMotor2D motor = joint.motor;
                motor.maxMotorTorque = Mathf.Max(1f, 30f * scale);
                float currentAngle = joint.jointAngle;
                motor.motorSpeed = Mathf.DeltaAngle(currentAngle, targetAngle) * Mathf.Max(0.5f, 2f * scale);
                joint.motor = motor;
                
                if (rb != null)
                {
                    rb.angularVelocity = Mathf.Lerp(rb.angularVelocity, 0f, Time.deltaTime * 5f);
                }
                
                finalElapsedTime += Time.deltaTime;
                yield return null;
            }
            
            // Final stabilization - maintain position with minimal force (scaled by damage/blood)
            float finalScale = GetMotorStrengthScale();
            JointMotor2D finalMotor = joint.motor;
            finalMotor.maxMotorTorque = Mathf.Max(1f, 10f * finalScale);
            finalMotor.motorSpeed = 0f;
            joint.motor = finalMotor;
        }
    }
    
    // Method to change the sprite (used when equipping items)
    // If newSprite is null, restores the original/default sprite
    public void SwapSprite(Sprite newSprite)
    {
        if (limbSprite == null)
            return;
        
        // Store original sprite if we haven't stored it yet
        if (!originalSpriteStored)
        {
            originalSprite = limbSprite.sprite;
            originalSpriteStored = true;
        }
        
        // If null is passed, restore the original sprite
        if (newSprite == null)
        {
            limbSprite.sprite = originalSprite;
        }
        else
        {
            // Otherwise, swap to the new sprite
            limbSprite.sprite = newSprite;
        }
    }
    
    // Method to apply a force impulse (for recoil, etc.)
    public void ApplyImpulse(Vector2 force)
    {
        rb.AddForce(force, ForceMode2D.Impulse);
    }
    
    // Health-related methods
    
    /// <summary>
    /// Apply damage to the limb
    /// </summary>
    /// <param name="amount">Amount of damage to apply</param>
    /// <returns>The actual amount of damage applied</returns>
    public float TakeDamage(float amount)
    {
        // Apply damage multiplier
        float actualDamage = amount * damageMultiplier;
        
        // Reduce health
        float oldHealth = currentHealth;
        currentHealth = Mathf.Max(0f, currentHealth - actualDamage);
        previousHealth = currentHealth; // Update tracking variable for inspector change detection
        
        // Invoke health changed event
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        
        // Return actual damage dealt
        return oldHealth - currentHealth;
    }
    
    /// <summary>
    /// Heal the limb
    /// </summary>
    /// <param name="amount">Amount of healing to apply</param>
    /// <returns>The actual amount healed</returns>
    public float Heal(float amount)
    {
        // Calculate healing
        float oldHealth = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        previousHealth = currentHealth; // Update tracking variable for inspector change detection
        
        // Invoke health changed event
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        
        // Return actual amount healed
        return currentHealth - oldHealth;
    }
    
    /// <summary>
    /// Get the current health percentage (0-1)
    /// </summary>
    public float GetHealthPercentage()
    {
        return currentHealth / maxHealth;
    }
    
    /// <summary>
    /// Get the current health value
    /// </summary>
    public float GetCurrentHealth()
    {
        return currentHealth;
    }
    
    /// <summary>
    /// Set health to a specific value
    /// </summary>
    public void SetHealth(float value)
    {
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        previousHealth = currentHealth; // Update tracking variable
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }
    
    /// <summary>
    /// Reset health to maximum
    /// </summary>
    public void ResetHealth()
    {
        currentHealth = maxHealth;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }
    
    /// <summary>
    /// Check if the limb is "dead" (health <= 0)
    /// </summary>
    public bool IsDead()
    {
        return currentHealth <= 0f;
    }
}
