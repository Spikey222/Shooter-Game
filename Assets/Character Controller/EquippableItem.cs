using UnityEngine;

public class EquippableItem : MonoBehaviour
{
    [Header("Item Data")]
    [Tooltip("Reference to the Item ScriptableObject for inventory purposes (optional)")]
    public Item itemData;
    
    [Header("Hand Attachment Points")]
    [Tooltip("Transform where the right hand should attach")]
    public Transform rightHandAttachPoint;
    
    [Tooltip("Transform where the left hand should attach")]
    public Transform leftHandAttachPoint;
    
    [Header("Hand Sprites")]
    [Tooltip("Sprite to use for right hand when equipped")]
    public Sprite rightHandSprite;
    
    [Tooltip("Sprite to use for left hand when equipped")]
    public Sprite leftHandSprite;
    
    [Header("Physics Settings")]
    [Tooltip("How strongly hands are pulled to attachment points")]
    public float handAttachStrength = 100f;
    
    [Tooltip("Maximum force applied to hands")]
    public float maxHandForce = 200f;
    
    [Tooltip("Damping to prevent oscillation")]
    public float handDamping = 10f;
    
    [Header("Recoil Settings")]
    [Tooltip("Base recoil force when firing")]
    public float recoilForce = 10f;
    
    [Tooltip("Random variation in recoil direction")]
    public float recoilVariation = 15f;
    
    [Header("Waypoint Animation")]
    [Tooltip("Optional waypoint animation component for this item")]
    public WaypointAnimation waypointAnimation;
    
    // Reference to the character controller
    protected ProceduralCharacterController characterController;
    
    private void Awake()
    {
        // Ensure attachment points exist
        if (rightHandAttachPoint == null)
        {
            GameObject rightPoint = new GameObject("RightHandAttach");
            rightPoint.transform.SetParent(transform);
            rightHandAttachPoint = rightPoint.transform;
        }
        
        if (leftHandAttachPoint == null)
        {
            GameObject leftPoint = new GameObject("LeftHandAttach");
            leftPoint.transform.SetParent(transform);
            leftHandAttachPoint = leftPoint.transform;
        }
    }
    
    // Called by the character controller to update hand positions
    public void UpdateHandPositions(ProceduralLimb rightHand, ProceduralLimb leftHand)
    {
        // Update right hand position
        if (rightHand != null && rightHandAttachPoint != null)
        {
            ApplyHandAttraction(rightHand, rightHandAttachPoint.position);
        }
        
        // Update left hand position
        if (leftHand != null && leftHandAttachPoint != null)
        {
            ApplyHandAttraction(leftHand, leftHandAttachPoint.position);
        }
    }
    
    // Apply attraction force to pull hand toward attachment point
    private void ApplyHandAttraction(ProceduralLimb hand, Vector3 targetPosition)
    {
        Rigidbody2D handRb = hand.GetComponent<Rigidbody2D>();
        if (handRb == null)
            return;
            
        // Calculate direction and distance to target
        Vector2 direction = (Vector2)targetPosition - handRb.position;
        float distance = direction.magnitude;
        
        // Normalize direction
        if (distance > 0.01f)
        {
            direction /= distance;
        }
        
        // Calculate force based on distance (stronger when further away)
        Vector2 attractionForce = direction * distance * handAttachStrength;
        
        // Apply damping to prevent oscillation
        attractionForce -= handRb.linearVelocity * handDamping;
        
        // Clamp force to maximum
        if (attractionForce.magnitude > maxHandForce)
        {
            attractionForce = attractionForce.normalized * maxHandForce;
        }
        
        // Apply force to hand
        handRb.AddForce(attractionForce);
    }
    
    // Method to trigger recoil (called when firing a weapon)
    public void TriggerRecoil()
    {
        if (characterController == null)
        {
            characterController = GetComponentInParent<ProceduralCharacterController>();
        }
        
        if (characterController != null)
        {
            // Calculate recoil direction (backward with some random variation)
            float randomAngle = Random.Range(-recoilVariation, recoilVariation);
            Vector2 recoilDirection = -transform.right; // Assuming item points right
            recoilDirection = Quaternion.Euler(0, 0, randomAngle) * recoilDirection;
            
            // Apply recoil force
            Vector2 recoilVector = recoilDirection * recoilForce;
            characterController.ApplyRecoil(recoilVector);
        }
    }
    
    // Method to use the item (can be overridden by specific item types)
    public virtual void Use()
    {
        // If waypoint animation exists, play it instead of just recoil
        if (waypointAnimation != null && waypointAnimation.IsPlaying() == false)
        {
            waypointAnimation.Play();
        }
        else
        {
            // Base implementation just triggers recoil
            TriggerRecoil();
        }
    }
    
    /// <summary>
    /// Play waypoint animation if available
    /// </summary>
    public void PlayWaypointAnimation()
    {
        if (waypointAnimation != null)
        {
            waypointAnimation.Play();
        }
    }
    
    /// <summary>
    /// Stop waypoint animation if playing
    /// </summary>
    public void StopWaypointAnimation()
    {
        if (waypointAnimation != null)
        {
            waypointAnimation.Stop();
        }
    }
}
