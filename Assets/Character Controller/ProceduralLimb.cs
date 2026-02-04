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
    
    // Event for when limb health changes
    public event Action<float, float> OnHealthChanged; // current, max
    
    // Components
    private Rigidbody2D rb;
    private HingeJoint2D joint;
    
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
    
    // Method to set a new target angle (used for animation or control)
    public void SetTargetAngle(float newAngle)
    {
        if (joint != null)
        {
            // Update joint limits around the new target angle
            joint.useLimits = true;
            JointAngleLimits2D limits = joint.limits;
            limits.min = newAngle - 15f; // Allow some movement around target
            limits.max = newAngle + 15f;
            joint.limits = limits;
            
            // Enable motor to move toward the target angle
            joint.useMotor = true;
            JointMotor2D motor = joint.motor;
            motor.maxMotorTorque = 100f; // Increased for more punchy movement
            
            // Calculate current angle and set motor direction
            float currentAngle = joint.jointAngle;
            float angleDifference = Mathf.DeltaAngle(currentAngle, newAngle);
            motor.motorSpeed = angleDifference * 5f; // Adjust multiplier as needed
            
            joint.motor = motor;
        }
    }
    
    // Method to set an exact angle with very tight limits (for spectator mode head positioning)
    public void SetExactAngle(float exactAngle)
    {
        if (joint != null)
        {
            // Update joint limits with very tight constraints
            joint.useLimits = true;
            JointAngleLimits2D limits = joint.limits;
            limits.min = exactAngle - 0.5f; // Very tight limits
            limits.max = exactAngle + 0.5f;
            joint.limits = limits;
            
            // Enable motor with higher torque for precise control
            joint.useMotor = true;
            JointMotor2D motor = joint.motor;
            motor.maxMotorTorque = 200f; // Higher torque for precise positioning
            
            // Calculate current angle and set motor direction
            float currentAngle = joint.jointAngle;
            float angleDifference = Mathf.DeltaAngle(currentAngle, exactAngle);
            motor.motorSpeed = angleDifference * 10f; // Higher multiplier for faster correction
            
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
    
    // Coroutine that handles the smooth rotation to zero
    private System.Collections.IEnumerator SmoothRotateToZeroCoroutine()
    {
        if (joint != null)
        {
            // Get starting angle
            float startAngle = joint.jointAngle;
            float duration = 0.8f; // Longer duration for smoother rotation
            float elapsedTime = 0f;
            
            // Get rigidbody for velocity control
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            float initialAngularVelocity = rb != null ? rb.angularVelocity : 0f;
            
            // Use smooth easing function for natural movement
            while (elapsedTime < duration)
            {
                // Calculate progress with smooth easing
                float t = elapsedTime / duration;
                // Smoothstep for ease-in-out effect
                t = t * t * (3f - 2f * t); 
                
                // Calculate target angle with smooth interpolation
                float targetAngle = Mathf.Lerp(startAngle, 0f, t);
                
                // Gradually reduce angular velocity to prevent jolt
                if (rb != null)
                {
                    float velocityReduction = Mathf.Lerp(initialAngularVelocity, 0f, t * t);
                    rb.angularVelocity = velocityReduction;
                }
                
                // Set joint limits with gradually decreasing range
                JointAngleLimits2D limits = joint.limits;
                float range = Mathf.Lerp(10f, 1f, t); // Gradually tighten limits
                limits.min = targetAngle - range;
                limits.max = targetAngle + range;
                joint.limits = limits;
                
                // Gradually increase motor torque for smoother end control
                joint.useMotor = true;
                JointMotor2D motor = joint.motor;
                motor.maxMotorTorque = Mathf.Lerp(30f, 80f, t); // Gradually increase torque
                
                // Calculate motor speed with decreasing multiplier near end
                float currentAngle = joint.jointAngle;
                float angleDifference = Mathf.DeltaAngle(currentAngle, targetAngle);
                float speedMultiplier = Mathf.Lerp(8f, 3f, t); // Reduce multiplier near end
                motor.motorSpeed = angleDifference * speedMultiplier;
                joint.motor = motor;
                
                // Wait for next frame
                elapsedTime += Time.deltaTime;
                yield return null;
            }
            
            // Final phase - very gentle approach to zero
            float finalDuration = 0.3f;
            float finalElapsedTime = 0f;
            
            while (finalElapsedTime < finalDuration)
            {
                // Very gentle approach to zero
                float t = finalElapsedTime / finalDuration;
                
                // Set very gentle limits
                JointAngleLimits2D limits = joint.limits;
                limits.min = -0.5f;
                limits.max = 0.5f;
                joint.limits = limits;
                
                // Very gentle motor settings
                JointMotor2D motor = joint.motor;
                motor.maxMotorTorque = 30f; // Lower torque to prevent jolt
                
                // Gentle speed toward zero
                float currentAngle = joint.jointAngle;
                motor.motorSpeed = currentAngle * -2f; // Very gentle correction
                joint.motor = motor;
                
                // Ensure zero angular velocity
                if (rb != null)
                {
                    rb.angularVelocity = 0f;
                }
                
                finalElapsedTime += Time.deltaTime;
                yield return null;
            }
            
            // Final stabilization - just maintain position with minimal force
            JointAngleLimits2D finalLimits = joint.limits;
            finalLimits.min = -0.2f;
            finalLimits.max = 0.2f;
            joint.limits = finalLimits;
            
            JointMotor2D finalMotor = joint.motor;
            finalMotor.maxMotorTorque = 10f; // Just enough to maintain position
            finalMotor.motorSpeed = 0f; // No active rotation
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
