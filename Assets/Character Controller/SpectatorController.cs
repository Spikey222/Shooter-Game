using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;

[RequireComponent(typeof(Rigidbody2D))]
public class SpectatorController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Movement speed of the spectator camera")]
    public float moveSpeed = 10f;
    
    [Tooltip("Movement acceleration")]
    public float acceleration = 50f;
    
    [Tooltip("Movement deceleration")]
    public float deceleration = 20f;
    
    [Tooltip("Velocity threshold to completely stop movement")]
    public float stopThreshold = 0.1f;
    
    [Tooltip("Team ID for controlling characters")]
    public int teamId = 0;
    
    [Header("Camera Settings")]
    [Tooltip("Camera follow speed")]
    public float cameraFollowSpeed = 10f; // Faster camera follow
    
    [Tooltip("Camera transition speed when switching control")]
    public float cameraTransitionSpeed = 5f;
    
    [Tooltip("Enable camera zoom with mouse wheel")]
    public bool enableCameraZoom = true;
    
    [Tooltip("Camera zoom speed")]
    public float cameraZoomSpeed = 3f; // Faster zoom speed
    
    [Tooltip("Minimum camera orthographic size")]
    public float minZoom = 2f;
    
    [Tooltip("Maximum camera orthographic size")]
    public float maxZoom = 15f; // Higher max zoom for overview
    
    // Runtime variables
    private Vector2 moveInput;
    private Vector2 currentVelocity;
    private Vector2 targetVelocity;
    private Camera mainCamera;
    private Rigidbody2D rb;
    
    // Currently controlled character
    private ProceduralCharacterController controlledCharacter;

    /// <summary>
    /// Current possessed/controlled character (null when unpossessed).
    /// </summary>
    public ProceduralCharacterController ControlledCharacter => controlledCharacter;

    /// <summary>
    /// Fired whenever the controlled character changes (including to null on unpossess).
    /// </summary>
    public event Action<ProceduralCharacterController> OnControlledCharacterChanged;
    
    // Distance threshold to determine if controlling a character
    public float controlThreshold = 3f;
    
    private void Awake()
    {
        // Initialize components
        rb = GetComponent<Rigidbody2D>();
        
        // Configure rigidbody physics
        if (rb != null)
        {
            rb.gravityScale = 0f; // No gravity in top-down view
            rb.linearDamping = 0.5f;
            rb.angularDamping = 0.5f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            
            // Make spectator pass through objects
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
        
        // Get main camera
        mainCamera = Camera.main;
        
        // Attach camera to spectator if not already attached
        if (mainCamera != null && mainCamera.transform.parent == null)
        {
            mainCamera.transform.SetParent(transform);
            mainCamera.transform.localPosition = new Vector3(0, 0, -10); // Standard camera distance
        }
    }
    
    private void Update()
    {
        // Handle keyboard input for WASD movement
        HandleKeyboardInput();
        
        // Update camera zoom
        UpdateCameraZoom();
        
        // Handle mouse input for character control
        HandleMouseInput();
        
        // Check for CTRL key to release control
        CheckControlRelease();
        
        // Update controlled character if we have one
        if (controlledCharacter != null)
        {
            // Make sure the controlled character is NOT in spectator mode
            // This gives the player direct control over the character
            if (controlledCharacter.spectatorMode)
            {
                controlledCharacter.SetSpectatorMode(false);
            }
            
            // Update camera position to follow the controlled character
            UpdateCameraPosition();
        }
    }
    
    private void FixedUpdate()
    {
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
        
        // Apply movement to rigidbody
        if (rb != null)
        {
            rb.MovePosition(rb.position + currentVelocity * Time.fixedDeltaTime);
        }
    }
    
    // Handle keyboard input for WASD movement
    private void HandleKeyboardInput()
    {
        // Reset move input each frame to ensure it stops when keys are released
        moveInput = Vector2.zero;
        
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
        }
    }
    
    // Update camera zoom with mouse wheel
    private void UpdateCameraZoom()
    {
        if (mainCamera != null && enableCameraZoom)
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
    
    // Switch to a specific character in overview mode
    public void SwitchToCharacter(ProceduralCharacterController character)
    {
        if (character != null)
        {
            if (character.IsDead)
                return;
            // Only allow switching to characters with matching team ID
            if (character.teamId != teamId)
                return;

            // If we already have a controlled character, release it first
            if (controlledCharacter != null)
            {
                // Disable camera attachment on the current character
                controlledCharacter.attachCamera = false;
                
                // Reset current character to spectator mode
                controlledCharacter.SetSpectatorMode(true);
            }
                
            // Set as controlled character
            SetControlledCharacter(character);
            
            // Move spectator to character position
            transform.position = character.transform.position;
            
            // Reset velocity
            currentVelocity = Vector2.zero;
            targetVelocity = Vector2.zero;
            
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
            
            // Make sure camera is attached to spectator first (to reset position)
            if (mainCamera != null)
            {
                mainCamera.transform.SetParent(transform);
                mainCamera.transform.localPosition = new Vector3(0, 0, -10);
            }
            
            // Then enable camera attachment on the new character
            if (controlledCharacter != null)
            {
                controlledCharacter.attachCamera = true;
            }
        }
    }
    
    // Check for CTRL key to release control
    private void CheckControlRelease()
    {
        // Check if CTRL key is pressed and we have a controlled character
        if (Keyboard.current != null && Keyboard.current.ctrlKey.wasPressedThisFrame && controlledCharacter != null)
        {
            // Release control of the character
            ReleaseControl();
        }
    }
    
    // Release control of the current character
    public void ReleaseControl()
    {
        if (controlledCharacter != null)
        {
            // Store the character's position before clearing the reference
            Vector3 characterPosition = controlledCharacter.transform.position;
            
            // Disable camera attachment on the character
            controlledCharacter.attachCamera = false;
            
            // Reset character to spectator mode
            controlledCharacter.SetSpectatorMode(true);
            
            // Position spectator centered on the character
            transform.position = characterPosition;
            
            // Clear reference
            SetControlledCharacter(null);
            
            // Make sure camera is attached to spectator
            if (mainCamera != null)
            {
                mainCamera.transform.SetParent(transform);
                mainCamera.transform.localPosition = new Vector3(0, 0, -10);
            }
        }
    }
    
    // Update camera position to smoothly follow the controlled character
    private void UpdateCameraPosition()
    {
        if (mainCamera != null && controlledCharacter != null)
        {
            // Get current camera position
            Vector3 cameraPosition = mainCamera.transform.position;
            
            // Calculate target position (keep z the same)
            Vector3 targetPosition = new Vector3(
                controlledCharacter.transform.position.x, 
                controlledCharacter.transform.position.y, 
                cameraPosition.z);
            
            // Smoothly move camera
            mainCamera.transform.position = Vector3.Lerp(
                cameraPosition, 
                targetPosition, 
                Time.deltaTime * cameraTransitionSpeed);
        }
    }
    
    // Check if this spectator is controlling a specific character
    public bool IsControllingCharacter(ProceduralCharacterController character)
    {
        // Only check explicitly controlled character
        return controlledCharacter == character;
    }
    
    // Find the nearest character to control
    public void FindNearestCharacterToControl()
    {
        ProceduralCharacterController[] characters = GameObject.FindObjectsByType<ProceduralCharacterController>(FindObjectsSortMode.None);
        ProceduralCharacterController nearest = null;
        float nearestDistance = float.MaxValue;
        
        foreach (ProceduralCharacterController character in characters)
        {
            if (character.IsDead)
                continue;
            // Skip characters already in spectator mode
            if (character.spectatorMode)
                continue;

            // Skip characters with different team ID
            if (character.teamId != teamId)
                continue;
                
            float distance = Vector2.Distance(transform.position, character.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = character;
            }
        }
        
        if (nearest != null && nearestDistance <= controlThreshold)
        {
            SwitchToCharacter(nearest);
        }
        else
        {
            SetControlledCharacter(null);
        }
    }

    private void SetControlledCharacter(ProceduralCharacterController newCharacter)
    {
        if (controlledCharacter == newCharacter)
            return;

        if (controlledCharacter != null)
            controlledCharacter.OnDeath -= OnControlledCharacterDied;

        controlledCharacter = newCharacter;

        if (controlledCharacter != null)
            controlledCharacter.OnDeath += OnControlledCharacterDied;

        OnControlledCharacterChanged?.Invoke(controlledCharacter);
    }

    private void OnControlledCharacterDied()
    {
        if (controlledCharacter != null)
        {
            controlledCharacter.OnDeath -= OnControlledCharacterDied;
            ReleaseControl();
        }
    }
    
    // Handle mouse input for character control
    private void HandleMouseInput()
    {
        // Check for left mouse button click
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Get mouse position in world space
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector2 worldPos = mainCamera.ScreenToWorldPoint(mousePos);
            
            // Find all characters in the scene
            ProceduralCharacterController[] characters = GameObject.FindObjectsByType<ProceduralCharacterController>(FindObjectsSortMode.None);
            
            // Find the character under the cursor
            foreach (ProceduralCharacterController character in characters)
            {
                if (character.IsDead)
                    continue;
                // Only allow taking control of characters that ARE in spectator mode
                // (meaning they're not already controlled by the player)
                if (!character.spectatorMode)
                    continue;
                    
                // Skip characters with different team ID
                if (character.teamId != teamId)
                    continue;
                
                // Check if mouse is over this character
                Collider2D collider = character.GetComponent<Collider2D>();
                if (collider != null && collider.OverlapPoint(worldPos))
                {
                    // Switch to this character
                    SwitchToCharacter(character);
                    break;
                }
            }
        }
    }
}
