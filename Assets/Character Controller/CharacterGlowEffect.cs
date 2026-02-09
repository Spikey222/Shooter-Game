using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(SpriteRenderer))]
public class CharacterGlowEffect : MonoBehaviour
{
    [Header("Highlight Settings")]
    [Tooltip("Color to change sprites to when highlighted")]
    public Color highlightColor = new Color(0f, 1f, 0f, 1f); // Green
    
    [Tooltip("Pulsation speed")]
    public float pulsateSpeed = 2f;
    
    [Tooltip("Minimum color intensity")]
    [Range(0.1f, 1f)]
    public float minIntensity = 0.7f;
    
    [Tooltip("Maximum color intensity")]
    [Range(0.1f, 1f)]
    public float maxIntensity = 1f;
    
    [Header("Hover Detection")]
    [Tooltip("Whether to use the object's collider for hover detection")]
    public bool useColliderForHover = true;
    
    [Tooltip("Width of the hover rectangle if not using collider")]
    public float hoverWidth = 1f;
    
    [Tooltip("Height of the hover rectangle if not using collider")]
    public float hoverHeight = 1.5f;
    
    [Header("Debug")]
    [Tooltip("Log highlight/restore messages to Console")]
    public bool logHighlightToConsole = false;
    
    // References
    private SpriteRenderer torsoRenderer;
    private ProceduralCharacterController characterController;
    private Camera mainCamera;
    private bool isHovering = false;
    
    // Dictionary to store original colors of all sprites
    private Dictionary<SpriteRenderer, Color> originalColors = new Dictionary<SpriteRenderer, Color>();
    
    // Flag to track if we're currently highlighted
    private bool isHighlighted = false;
    
    private void Awake()
    {
        // Get references
        torsoRenderer = GetComponent<SpriteRenderer>();
        characterController = GetComponent<ProceduralCharacterController>();
        mainCamera = Camera.main;
        
        // Store original colors of all sprites
        StoreOriginalColors();
        
        // Ensure the collider is properly set up to rotate with the character
        UpdateColliderRotation();
    }
    
    private void StoreOriginalColors()
    {
        // Clear existing stored colors
        originalColors.Clear();
        
        // Store torso color
        if (torsoRenderer != null)
        {
            originalColors[torsoRenderer] = torsoRenderer.color;
        }
        
        // Store colors of all child sprites
        if (characterController != null)
        {
            SpriteRenderer[] allSprites = characterController.GetComponentsInChildren<SpriteRenderer>();
            foreach (SpriteRenderer sprite in allSprites)
            {
                if (sprite != null && sprite != torsoRenderer)
                {
                    originalColors[sprite] = sprite.color;
                }
            }
        }
    }
    
    private void Update()
    {
        // Check if we have a valid character controller
        if (characterController == null) return;
        
        // Update collider rotation to match character's current rotation
        UpdateColliderRotation();
        
        // Check if mouse is hovering over this character
        CheckHover();
        
        // Update highlight state based on hover and controllability
        UpdateHighlightState();
    }
    
    private void CheckHover()
    {
        // Only check hover if we have a camera
        if (mainCamera == null) return;
        
        // Get mouse position in world space
        if (Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector2 worldPos = mainCamera.ScreenToWorldPoint(mousePos);
            
            if (useColliderForHover)
            {
                // Use the object's collider for hover detection
                Collider2D collider = GetComponent<Collider2D>();
                if (collider != null)
                {
                    // Check if mouse position is within the collider
                    isHovering = collider.OverlapPoint(worldPos);
                }
                else
                {
                    // Fallback to rectangle if no collider found
                    CheckRectangleHover(worldPos);
                }
            }
            else
            {
                // Use rectangle hover detection
                CheckRectangleHover(worldPos);
            }
        }
    }
    
    // Call this method in Awake or Start to ensure the collider rotates with the character
    private void UpdateColliderRotation()
    {
        // Get the collider if it exists
        Collider2D collider = GetComponent<Collider2D>();
        if (collider != null && collider is BoxCollider2D boxCollider)
        {
            // Make sure the collider's rotation matches the transform's rotation
            // This is handled automatically by Unity's physics system
            // but we need to make sure the collider's offset is properly set
            
            // If the character has a custom pivot point, you might need to adjust the offset
            // based on the character's current rotation
            float zRotation = transform.rotation.eulerAngles.z;
            
            // For a BoxCollider2D, we don't need to manually rotate it as it will follow the transform
        }
    }
    
    private void CheckRectangleHover(Vector2 worldPos)
    {
        // Get the character's rotation
        float angle = transform.rotation.eulerAngles.z;
        
        // Convert mouse position to local space to account for rotation
        Vector2 localPoint = RotatePointAroundPivot(worldPos, (Vector2)transform.position, -angle);
        
        // Calculate rectangle bounds in local space (aligned with character)
        Vector2 min = (Vector2)transform.position - new Vector2(hoverWidth * 0.5f, hoverHeight * 0.5f);
        Vector2 max = (Vector2)transform.position + new Vector2(hoverWidth * 0.5f, hoverHeight * 0.5f);
        
        // Check if the rotated point is within the rectangle
        isHovering = (localPoint.x >= min.x && localPoint.x <= max.x && 
                      localPoint.y >= min.y && localPoint.y <= max.y);
    }
    
    // Helper method to rotate a point around a pivot by a given angle (in degrees)
    private Vector2 RotatePointAroundPivot(Vector2 point, Vector2 pivot, float angle)
    {
        // Convert angle to radians
        float radians = angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        
        // Translate point to origin
        Vector2 translated = point - pivot;
        
        // Rotate point
        Vector2 rotated = new Vector2(
            translated.x * cos - translated.y * sin,
            translated.x * sin + translated.y * cos);
        
        // Translate point back
        return rotated + pivot;
    }
    
    private void UpdateHighlightState()
    {
        // Highlight character if hovering and character is controllable
        bool isControllable = IsCharacterControllable();
        bool shouldHighlight = isHovering && isControllable;
        
        // Track if highlight state changed
        if (shouldHighlight != isHighlighted)
        {
            isHighlighted = shouldHighlight;
            
            if (shouldHighlight)
            {
                // Apply highlight color
                ApplyHighlightToAllLimbs();
            }
            else
            {
                // Restore original colors
                RestoreOriginalColors();
            }
        }
        
        // If highlighted, update the pulsating effect
        if (isHighlighted)
        {
            UpdatePulsatingEffect();
        }
    }
    
    private void ApplyHighlightToAllLimbs()
    {
        if (characterController == null) return;
        
        // Make sure we have the original colors stored
        StoreOriginalColors();
        
        // Apply initial highlight color
        UpdatePulsatingEffect();
        
        if (logHighlightToConsole)
            Debug.Log("Applying highlight to all limbs");
    }
    
    private void UpdatePulsatingEffect()
    {
        // Calculate pulsating intensity
        float intensity = Mathf.Lerp(minIntensity, maxIntensity, 
            (Mathf.Sin(Time.time * pulsateSpeed) + 1f) * 0.5f);
        
        // Apply to torso directly
        if (torsoRenderer != null && originalColors.ContainsKey(torsoRenderer))
        {
            // Create highlight color that preserves original opacity
            Color originalColor = originalColors[torsoRenderer];
            Color newColor = new Color(
                highlightColor.r * intensity,
                highlightColor.g * intensity,
                highlightColor.b * intensity,
                originalColor.a // Preserve original alpha
            );
            
            torsoRenderer.color = newColor;
        }
        
        // Apply to all limbs directly through the character controller
        if (characterController != null)
        {
            // Find all SpriteRenderer components in children
            SpriteRenderer[] allSprites = characterController.GetComponentsInChildren<SpriteRenderer>();
            
            // Apply color to all sprites
            foreach (SpriteRenderer sprite in allSprites)
            {
                // Skip the torso sprite as we've already handled it
                if (sprite != torsoRenderer && sprite != null)
                {
                    // Get original color to preserve alpha
                    Color originalColor = Color.white; // Default
                    if (originalColors.ContainsKey(sprite))
                    {
                        originalColor = originalColors[sprite];
                    }
                    else
                    {
                        // Store the color if we haven't seen this sprite before
                        originalColors[sprite] = sprite.color;
                        originalColor = sprite.color;
                    }
                    
                    // Create highlight color that preserves original opacity
                    Color newColor = new Color(
                        highlightColor.r * intensity,
                        highlightColor.g * intensity,
                        highlightColor.b * intensity,
                        originalColor.a // Preserve original alpha
                    );
                    
                    sprite.color = newColor;
                }
            }
        }
    }
    
    private void ApplyColorToLimb(ProceduralLimb limb, Color color)
    {
        if (limb != null && limb.limbSprite != null)
        {
            limb.limbSprite.color = color;
            Debug.Log($"Applied color to {limb.name} sprite");
        }
        else if (limb != null)
        {
            Debug.LogWarning($"Limb {limb.name} has no sprite renderer!");
        }
    }
    
    private void RestoreOriginalColors()
    {
        // Restore all original colors from our dictionary
        foreach (KeyValuePair<SpriteRenderer, Color> pair in originalColors)
        {
            if (pair.Key != null)
            {
                pair.Key.color = pair.Value;
            }
        }
        
        // Double-check all sprites in case we missed any
        if (characterController != null)
        {
            SpriteRenderer[] allSprites = characterController.GetComponentsInChildren<SpriteRenderer>();
            foreach (SpriteRenderer sprite in allSprites)
            {
                if (sprite != null && !originalColors.ContainsKey(sprite))
                {
                    // If we somehow missed storing this sprite's color, reset to white
                    sprite.color = Color.white;
                }
            }
        }
        
        if (logHighlightToConsole)
            Debug.Log("Restored original colors for all sprites");
    }
    
    private bool IsCharacterControllable()
    {
        // A character is controllable if:
        // 1. It IS in spectator mode (meaning it's not currently controlled by the player)
        // 2. It has a valid character controller
        // 3. Its team ID matches the spectator's team ID
        
        if (characterController == null || !characterController.spectatorMode)
            return false;
        
        // Find the spectator controller to check team ID
        SpectatorController spectator = FindFirstObjectByType<SpectatorController>();
        if (spectator == null)
            return true; // If no spectator found, allow control as fallback
        
        // Only allow control if team IDs match
        return characterController.teamId == spectator.teamId;
    }
    
    // Draw hover detection area in editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        
        if (useColliderForHover)
        {
            // Try to visualize the collider if present
            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null)
            {
                // For BoxCollider2D, draw a wire cube
                if (collider is BoxCollider2D boxCollider)
                {
                    // Get the box size and center in world space
                    Vector3 size = new Vector3(
                        boxCollider.size.x * transform.lossyScale.x,
                        boxCollider.size.y * transform.lossyScale.y,
                        0.1f);
                    Vector3 center = transform.TransformPoint(boxCollider.offset);
                    
                    // Draw wire cube representing the box collider with proper rotation
                    Matrix4x4 rotationMatrix = Matrix4x4.TRS(
                        center,
                        transform.rotation,
                        size);
                    
                    // Store the current Gizmos matrix
                    Matrix4x4 oldMatrix = Gizmos.matrix;
                    
                    // Set the Gizmos matrix to our rotation matrix
                    Gizmos.matrix = rotationMatrix;
                    
                    // Draw a unit cube (which will be transformed by the matrix)
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                    
                    // Restore the old Gizmos matrix
                    Gizmos.matrix = oldMatrix;
                }
                else
                {
                    // For other collider types, draw the rectangle bounds as fallback
                    Bounds bounds = collider.bounds;
                    Gizmos.DrawWireCube(bounds.center, bounds.size);
                }
            }
            else
            {
                // Draw rotated rectangle if no collider
                DrawRotatedRectangle(transform.position, hoverWidth, hoverHeight, transform.rotation.eulerAngles.z);
            }
        }
        else
        {
            // Draw rotated rectangle for manual hover detection
            DrawRotatedRectangle(transform.position, hoverWidth, hoverHeight, transform.rotation.eulerAngles.z);
        }
    }
    
    // Helper method to draw a rotated rectangle in the editor
    private void DrawRotatedRectangle(Vector3 center, float width, float height, float angle)
    {
        // Calculate the four corners of the rectangle
        Vector3 topLeft = new Vector3(-width/2, height/2, 0);
        Vector3 topRight = new Vector3(width/2, height/2, 0);
        Vector3 bottomRight = new Vector3(width/2, -height/2, 0);
        Vector3 bottomLeft = new Vector3(-width/2, -height/2, 0);
        
        // Rotate the corners
        Quaternion rotation = Quaternion.Euler(0, 0, angle);
        topLeft = rotation * topLeft;
        topRight = rotation * topRight;
        bottomRight = rotation * bottomRight;
        bottomLeft = rotation * bottomLeft;
        
        // Translate to center position
        topLeft += center;
        topRight += center;
        bottomRight += center;
        bottomLeft += center;
        
        // Draw the four lines of the rectangle
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
        Gizmos.DrawLine(bottomLeft, topLeft);
    }
}
