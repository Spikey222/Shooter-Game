using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.IO;

/// <summary>
/// Keyframe-based animation system for procedural limbs.
/// Allows defining animation keyframes in the Unity Inspector where multiple limbs can animate simultaneously.
/// </summary>
public class WaypointAnimation : MonoBehaviour
{
    [System.Serializable]
    public class LimbTarget
    {
        [Tooltip("Which limb to animate (assign in Inspector)")]
        public ProceduralLimb targetLimb;
        
        [Tooltip("Target angle for this limb to reach (in degrees)")]
        public float targetAngle;
        
        [Tooltip("Optional easing curve for this specific limb (overrides keyframe curve if set)")]
        public AnimationCurve easingCurve;
    }
    
    [System.Serializable]
    public class AnimationKeyframe
    {
        [Tooltip("Time at which this keyframe should be reached (in seconds from animation start)")]
        public float time = 0.5f;
        
        [Tooltip("Limb targets for this keyframe (multiple limbs can animate simultaneously)")]
        public List<LimbTarget> limbTargets = new List<LimbTarget>();
        
        [Tooltip("Default easing curve for all limbs in this keyframe (can be overridden per limb)")]
        public AnimationCurve defaultEasingCurve;
        
        [Tooltip("Optional event name/callback when this keyframe is reached")]
        public string eventName;
    }
    
    [Header("Animation Keyframes")]
    [Tooltip("Add keyframes here. Each keyframe can have multiple limbs animating simultaneously.")]
    public List<AnimationKeyframe> keyframes = new List<AnimationKeyframe>();
    
    [Header("Animation Settings")]
    [Tooltip("Should the animation loop?")]
    public bool loop = false;
    
    [Tooltip("Should the animation stop if interrupted?")]
    public bool stopOnInterrupt = true;
    
    [Tooltip("Speed multiplier for the entire animation (1.0 = normal speed, 2.0 = 2x faster, 0.5 = 2x slower)")]
    [Range(0.1f, 5f)]
    public float animationSpeedMultiplier = 1.0f;
    
    [Tooltip("Motor torque for limb movement (higher = more forceful/punchy movement)")]
    [Range(10f, 200f)]
    public float motorTorque = 100f;
    
    [Tooltip("Should limbs return to their initial/neutral positions after animation completes?")]
    public bool returnToNeutralOnComplete = true;
    
    [Tooltip("Delay before starting return to neutral (allows animation to finish naturally)")]
    public float returnToNeutralDelay = 0.1f;
    
    [Tooltip("Duration to return to neutral position (in seconds)")]
    public float returnToNeutralDuration = 0.3f;
    
    [Tooltip("Smoothing factor for return to neutral (higher = smoother, less aggressive)")]
    [Range(0.1f, 3f)]
    public float returnToNeutralSmoothing = 1.5f;
    
    // Events
    public event Action OnAnimationComplete;
    public event Action<int> OnKeyframeReached; // For callbacks with keyframe index
    public event Action<string> OnKeyframeReachedByName; // For event callbacks (eventName)
    
    // Internal state
    private Coroutine animationCoroutine;
    private bool isPlaying = false;
    private Dictionary<ProceduralLimb, float> initialAngles = new Dictionary<ProceduralLimb, float>();
    private Dictionary<ProceduralLimb, Coroutine> activeAnimations = new Dictionary<ProceduralLimb, Coroutine>();
    private ProceduralCharacterController characterController;
    
    /// <summary>
    /// Set the character controller reference explicitly (used by weapons to ensure correct character is targeted)
    /// </summary>
    public void SetCharacterController(ProceduralCharacterController controller)
    {
        characterController = controller;
        
        // CRITICAL FIX: Remap limb references in keyframes to the new character's limbs
        // This fixes the bug where cloned weapons still reference the original character's limbs
        if (controller != null && keyframes != null)
        {
            RemapLimbReferences(controller);
        }
    }
    
    /// <summary>
    /// Remap all limb references in keyframes to point to the specified character's limbs.
    /// This is necessary because Unity's Instantiate copies component field references,
    /// so cloned weapons would still reference the original character's limb GameObjects.
    /// </summary>
    private void RemapLimbReferences(ProceduralCharacterController newController)
    {
        foreach (var keyframe in keyframes)
        {
            if (keyframe.limbTargets == null)
                continue;
                
            for (int i = 0; i < keyframe.limbTargets.Count; i++)
            {
                var limbTarget = keyframe.limbTargets[i];
                if (limbTarget.targetLimb == null)
                    continue;
                
                // Determine which limb this is by checking the old character's limb references
                // (We need to find which limb this was on the old character to map to the new one)
                ProceduralCharacterController oldController = limbTarget.targetLimb.GetComponentInParent<ProceduralCharacterController>();
                if (oldController == null || oldController == newController)
                    continue; // Already correct or can't determine
                
                // Map old limb to new limb
                ProceduralLimb newLimb = null;
                if (limbTarget.targetLimb == oldController.rightArm)
                    newLimb = newController.rightArm;
                else if (limbTarget.targetLimb == oldController.leftArm)
                    newLimb = newController.leftArm;
                else if (limbTarget.targetLimb == oldController.rightForearm)
                    newLimb = newController.rightForearm;
                else if (limbTarget.targetLimb == oldController.leftForearm)
                    newLimb = newController.leftForearm;
                else if (limbTarget.targetLimb == oldController.rightHand)
                    newLimb = newController.rightHand;
                else if (limbTarget.targetLimb == oldController.leftHand)
                    newLimb = newController.leftHand;
                else if (limbTarget.targetLimb == oldController.head)
                    newLimb = newController.head;
                else if (limbTarget.targetLimb == oldController.neck)
                    newLimb = newController.neck;
                else if (limbTarget.targetLimb == oldController.rightThigh)
                    newLimb = newController.rightThigh;
                else if (limbTarget.targetLimb == oldController.rightCalf)
                    newLimb = newController.rightCalf;
                else if (limbTarget.targetLimb == oldController.rightFoot)
                    newLimb = newController.rightFoot;
                else if (limbTarget.targetLimb == oldController.leftThigh)
                    newLimb = newController.leftThigh;
                else if (limbTarget.targetLimb == oldController.leftCalf)
                    newLimb = newController.leftCalf;
                else if (limbTarget.targetLimb == oldController.leftFoot)
                    newLimb = newController.leftFoot;
                
                // Update the reference
                if (newLimb != null)
                {
                    limbTarget.targetLimb = newLimb;
                }
            }
        }
    }
    
    /// <summary>
    /// Play the keyframe animation from the beginning
    /// </summary>
    public void Play()
    {
        // Prevent spam - if already playing, don't restart
        if (isPlaying)
        {
            return; // Animation is already playing, ignore new play request
        }
        
        Stop(); // Stop any existing animation (shouldn't be needed, but safety check)
        
        if (keyframes == null || keyframes.Count == 0)
        {
            Debug.LogWarning($"[WaypointAnimation] No keyframes defined on {gameObject.name}", this);
            return;
        }
        
        // Get character controller reference if not already set
        if (characterController == null)
        {
            characterController = GetComponentInParent<ProceduralCharacterController>();
            // #region agent log
            try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"hypothesisId\":\"D\",\"location\":\"WaypointAnimation.Play\",\"message\":\"fallback GetComponentInParent\",\"data\":{\"controllerName\":\"" + (characterController != null ? characterController.gameObject.name : "null").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"weaponName\":\"" + (gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\"},\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}\n"); } catch { }
            // #endregion
        }
        // #region agent log
        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"hypothesisId\":\"D\",\"location\":\"WaypointAnimation.Play\",\"message\":\"Play starting\",\"data\":{\"controllerName\":\"" + (characterController != null ? characterController.gameObject.name : "null").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"weaponName\":\"" + (gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\"},\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}\n"); } catch { }
        // #endregion
        // Sort keyframes by time
        var sortedKeyframes = keyframes.OrderBy(k => k.time).ToList();
        
        isPlaying = true;
        animationCoroutine = StartCoroutine(PlayAnimationCoroutine(sortedKeyframes));
    }
    
    /// <summary>
    /// Stop the current animation
    /// </summary>
    public void Stop()
    {
        // Stop all active limb animations
        foreach (var coroutine in activeAnimations.Values)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        activeAnimations.Clear();
        
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
        isPlaying = false;
        initialAngles.Clear();
        
        // Release control so idle animation can take over
        ReleaseLimbControl();
    }
    
    /// <summary>
    /// Check if animation is currently playing
    /// </summary>
    public bool IsPlaying()
    {
        return isPlaying;
    }
    
    /// <summary>
    /// Main animation coroutine that plays through keyframes
    /// </summary>
    private IEnumerator PlayAnimationCoroutine(List<AnimationKeyframe> sortedKeyframes)
    {
        do
        {
            // Store initial angles for all limbs that will be animated
            initialAngles.Clear();
            foreach (var keyframe in sortedKeyframes)
            {
                foreach (var limbTarget in keyframe.limbTargets)
                {
                    if (limbTarget.targetLimb != null && !initialAngles.ContainsKey(limbTarget.targetLimb))
                    {
                        HingeJoint2D joint = limbTarget.targetLimb.GetComponent<HingeJoint2D>();
                        if (joint != null)
                        {
                            initialAngles[limbTarget.targetLimb] = joint.jointAngle;
                        }
                    }
                }
            }
            
            float animationStartTime = Time.time;
            float firstKeyframeTime = sortedKeyframes.Count > 0 ? sortedKeyframes[0].time : 0f;
            float lastKeyframeTime = firstKeyframeTime; // Track last keyframe time for duration calculation
            
            // Play through each keyframe
            for (int i = 0; i < sortedKeyframes.Count; i++)
            {
                var keyframe = sortedKeyframes[i];
                
                // First keyframe ALWAYS plays immediately (no delay, regardless of its time value)
                // Subsequent keyframes wait based on the time difference from previous keyframe
                // Apply speed multiplier to make animations faster
                if (i > 0)
                {
                    float waitTime = (keyframe.time - lastKeyframeTime) / animationSpeedMultiplier;
                    if (waitTime > 0f)
                    {
                        yield return new WaitForSeconds(waitTime);
                    }
                }
                // First keyframe (i == 0) plays immediately - skip wait entirely
                
                // Animate all limbs in this keyframe simultaneously
                foreach (var limbTarget in keyframe.limbTargets)
                {
                    if (limbTarget.targetLimb == null)
                    {
                        Debug.LogWarning($"[WaypointAnimation] Keyframe {i} has null targetLimb, skipping", this);
                        continue;
                    }
                    // #region agent log
                    if (i == 0)
                    {
                        var limbChar = limbTarget.targetLimb.GetComponentInParent<ProceduralCharacterController>();
                        try { File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"hypothesisId\":\"A\",\"location\":\"WaypointAnimation.PlayAnimationCoroutine\",\"message\":\"first keyframe limb\",\"data\":{\"limbName\":\"" + (limbTarget.targetLimb.gameObject.name ?? "").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"limbBelongsTo\":\"" + (limbChar != null ? limbChar.gameObject.name : "null").Replace("\\","\\\\").Replace("\"","\\\"") + "\",\"animController\":\"" + (characterController != null ? characterController.gameObject.name : "null").Replace("\\","\\\\").Replace("\"","\\\"") + "\"},\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}\n"); } catch { }
                    }
                    // #endregion
                    // Get starting angle
                    float startAngle = initialAngles.ContainsKey(limbTarget.targetLimb) 
                        ? initialAngles[limbTarget.targetLimb] 
                        : limbTarget.targetLimb.GetComponent<HingeJoint2D>()?.jointAngle ?? 0f;
                    
                    float targetAngle = limbTarget.targetAngle;
                    
                    // Calculate duration based on time until next keyframe (or end of animation)
                    // Apply speed multiplier to make animations faster
                    float duration = 0.2f; // Default duration
                    if (i < sortedKeyframes.Count - 1)
                    {
                        duration = (sortedKeyframes[i + 1].time - keyframe.time) / animationSpeedMultiplier;
                    }
                    else
                    {
                        // Last keyframe - use a default duration or calculate from animation length
                        float totalAnimationTime = sortedKeyframes.Last().time;
                        duration = Mathf.Max(0.05f, (totalAnimationTime * 0.1f) / animationSpeedMultiplier); // 10% of total time, scaled by speed
                    }
                    
                    // Use limb-specific easing curve if provided, otherwise use keyframe default
                    AnimationCurve easingCurve = limbTarget.easingCurve != null && limbTarget.easingCurve.length > 0
                        ? limbTarget.easingCurve
                        : keyframe.defaultEasingCurve;
                    
                    // Start animation for this limb (non-blocking, so multiple limbs animate simultaneously)
                    Coroutine limbCoroutine = StartCoroutine(AnimateLimbToTarget(
                        limbTarget.targetLimb, 
                        startAngle, 
                        targetAngle, 
                        duration, 
                        easingCurve
                    ));
                    
                    activeAnimations[limbTarget.targetLimb] = limbCoroutine;
                    
                    // Update initial angle for next keyframe
                    initialAngles[limbTarget.targetLimb] = targetAngle;
                }
                
                // Trigger keyframe reached events
                OnKeyframeReached?.Invoke(i);
                
                if (!string.IsNullOrEmpty(keyframe.eventName))
                {
                    OnKeyframeReachedByName?.Invoke(keyframe.eventName);
                }
                
                lastKeyframeTime = keyframe.time;
            }
            
            // Wait for all animations to complete
            while (activeAnimations.Count > 0)
            {
                yield return null;
            }
            
            // Return all animated limbs to their initial/neutral positions if enabled
            if (returnToNeutralOnComplete)
            {
                // Wait for delay before starting return to neutral
                // This allows the animation to finish naturally before returning
                if (returnToNeutralDelay > 0f)
                {
                    yield return new WaitForSeconds(returnToNeutralDelay);
                }
                
                yield return StartCoroutine(ReturnToInitialPositions());
            }
            
            // Set isPlaying to false FIRST so idle animation system knows it can take over
            isPlaying = false;
            
            // Stop controlling limbs - release control so idle animation can take over
            ReleaseLimbControl();
            
            // Animation complete
            OnAnimationComplete?.Invoke();
            
        } while (loop && isPlaying);
        
        // Ensure control is released when loop ends
        ReleaseLimbControl();
    }
    
    /// <summary>
    /// Release control of all limbs so idle animation can take over
    /// </summary>
    private void ReleaseLimbControl()
    {
        // Clear all active animations
        foreach (var coroutine in activeAnimations.Values)
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
        }
        activeAnimations.Clear();
        
        // Note: We don't set target angles here - we let the idle animation system take over
        // The limbs will naturally return to idle positions via the ProceduralCharacterController's UpdateIdleAnimation()
    }
    
    /// <summary>
    /// Get the neutral/base idle angle for a limb from the character controller
    /// </summary>
    private float GetNeutralAngleForLimb(ProceduralLimb limb)
    {
        if (characterController == null || limb == null)
        {
            // Fallback to initial angle if no character controller
            return initialAngles.ContainsKey(limb) ? initialAngles[limb] : 0f;
        }
        
        // Map limb to its base idle angle
        if (limb == characterController.rightArm)
            return characterController.rightArmBaseAngle;
        else if (limb == characterController.leftArm)
            return characterController.leftArmBaseAngle;
        else if (limb == characterController.rightForearm)
            return characterController.rightForearmBaseAngle;
        else if (limb == characterController.leftForearm)
            return characterController.leftForearmBaseAngle;
        else if (limb == characterController.rightHand)
            return characterController.rightHandBaseAngle;
        else if (limb == characterController.leftHand)
            return characterController.leftHandBaseAngle;
        
        // Fallback to initial angle if limb not recognized
        return initialAngles.ContainsKey(limb) ? initialAngles[limb] : 0f;
    }
    
    /// <summary>
    /// Return all animated limbs to their neutral/idle base positions
    /// </summary>
    private IEnumerator ReturnToInitialPositions()
    {
        if (initialAngles.Count == 0)
            yield break;
        
        // Start returning all limbs to neutral base angles simultaneously
        foreach (var kvp in initialAngles)
        {
            ProceduralLimb limb = kvp.Key;
            
            if (limb == null)
                continue;
            
            // Get neutral angle for this limb (from character controller base angles)
            float neutralAngle = GetNeutralAngleForLimb(limb);
            
            // Get current angle
            HingeJoint2D joint = limb.GetComponent<HingeJoint2D>();
            if (joint == null)
                continue;
            
            float startAngle = joint.jointAngle;
            
            // Start return animation to neutral position
            // Uses returnToNeutralDuration setting for the return animation speed
            Coroutine returnCoroutine = StartCoroutine(AnimateLimbToNeutral(
                limb,
                startAngle,
                neutralAngle,
                returnToNeutralDuration // Duration is controlled by returnToNeutralDuration setting
            ));
            
            activeAnimations[limb] = returnCoroutine;
        }
        
        // Wait for all return animations to complete
        while (activeAnimations.Count > 0)
        {
            yield return null;
        }
    }
    
    /// <summary>
    /// Animate a limb to neutral position with controlled speed respecting duration
    /// Uses motor speed calculation to ensure the duration is exactly respected
    /// </summary>
    private IEnumerator AnimateLimbToNeutral(ProceduralLimb limb, float startAngle, float targetAngle, float duration)
    {
        if (limb == null || duration <= 0f || limb.GetComponent<HingeJoint2D>() == null)
        {
            activeAnimations.Remove(limb);
            yield break;
        }
        
        HingeJoint2D joint = limb.GetComponent<HingeJoint2D>();
        Rigidbody2D rb = limb.GetComponent<Rigidbody2D>();
        float elapsedTime = 0f;
        float initialAngularVelocity = rb != null ? rb.angularVelocity : 0f;
        
        // Use smoothed easing for gentler return to neutral
        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / duration);
            
            // Apply smoothstep easing for smooth deceleration (ease-out)
            // Higher smoothing factor = smoother/slower deceleration
            float smoothedT = t;
            if (returnToNeutralSmoothing > 1f)
            {
                // Apply smoothstep with adjustable smoothing
                smoothedT = Mathf.Pow(t, returnToNeutralSmoothing);
            }
            else if (returnToNeutralSmoothing < 1f)
            {
                // For values < 1, use inverse smoothstep for faster start
                smoothedT = 1f - Mathf.Pow(1f - t, 1f / returnToNeutralSmoothing);
            }
            
            // Calculate target angle with smooth interpolation
            float currentTargetAngle = Mathf.LerpAngle(startAngle, targetAngle, smoothedT);
            
            // Get current angle
            float currentAngle = joint.jointAngle;
            float remainingAngle = Mathf.DeltaAngle(currentAngle, currentTargetAngle);
            float remainingTime = duration - elapsedTime;
            
            // Gradually reduce angular velocity for smoother movement
            if (rb != null)
            {
                float velocityReduction = Mathf.Lerp(initialAngularVelocity, 0f, smoothedT * smoothedT);
                rb.angularVelocity = velocityReduction;
            }
            
            // Update motor speed with smoothing
            if (remainingTime > 0.01f && Mathf.Abs(remainingAngle) > 0.5f)
            {
                // Calculate speed needed, but apply smoothing to reduce aggressiveness
                float baseSpeed = remainingAngle / remainingTime;
                
                // Apply smoothing factor to reduce speed as we approach target
                float smoothingFactor = 1f + (returnToNeutralSmoothing * (1f - t));
                float smoothedSpeed = baseSpeed / smoothingFactor;
                
                // Update joint limits with gradually tightening range
                joint.useLimits = true;
                JointAngleLimits2D limits = joint.limits;
                float range = Mathf.Lerp(15f, 5f, smoothedT); // Gradually tighten limits
                limits.min = currentTargetAngle - range;
                limits.max = currentTargetAngle + range;
                joint.limits = limits;
                
                // Use reduced torque for smoother return (less aggressive)
                JointMotor2D motor = joint.motor;
                motor.maxMotorTorque = Mathf.Lerp(motorTorque * 0.5f, motorTorque * 0.3f, smoothedT); // Lower torque for smoother return
                motor.motorSpeed = smoothedSpeed;
                joint.motor = motor;
            }
            else
            {
                // Close enough, set final angle
                break;
            }
            
            yield return null;
        }
        
        // Ensure final angle is set
        limb.SetTargetAngle(targetAngle);
        
        // Remove from active animations
        activeAnimations.Remove(limb);
    }
    
    /// <summary>
    /// Animate a limb from start angle to target angle over duration
    /// </summary>
    private IEnumerator AnimateLimbToTarget(ProceduralLimb limb, float startAngle, float targetAngle, float duration, AnimationCurve easingCurve)
    {
        if (limb == null || duration <= 0f)
        {
            activeAnimations.Remove(limb);
            yield break;
        }
        
        float elapsedTime = 0f;
        
        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / duration);
            
            // Apply easing curve if provided
            if (easingCurve != null && easingCurve.length > 0)
            {
                t = easingCurve.Evaluate(t);
            }
            
            // Interpolate angle
            float currentAngle = Mathf.LerpAngle(startAngle, targetAngle, t);
            
            // Set target angle on limb
            limb.SetTargetAngle(currentAngle);
            
            yield return null;
        }
        
        // Ensure final angle is set
        limb.SetTargetAngle(targetAngle);
        
        // Remove from active animations
        activeAnimations.Remove(limb);
    }
    
    private void OnDisable()
    {
        Stop();
    }
}
