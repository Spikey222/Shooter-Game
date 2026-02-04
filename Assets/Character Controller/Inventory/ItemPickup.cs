using UnityEngine;

/// <summary>
/// Component for items that can be picked up in the world.
/// Attach this to GameObject with a trigger collider for pickup detection.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemPickup : MonoBehaviour
{
    [Header("Item Data")]
    [Tooltip("Reference to the Item ScriptableObject to pick up")]
    public Item item;
    
    [Tooltip("Quantity of items to give when picked up")]
    public int quantity = 1;
    
    [Header("Pickup Settings")]
    [Tooltip("Auto-pickup when player enters trigger")]
    public bool autoPickup = false;
    
    [Tooltip("Interaction key (set to None for auto-pickup)")]
    public KeyCode interactionKey = KeyCode.E;
    
    [Tooltip("Distance for interaction if not using trigger")]
    public float interactionDistance = 2f;
    
    [Header("Visual Feedback")]
    [Tooltip("Optional highlight effect when player is near")]
    public SpriteRenderer highlightRenderer;
    
    [Tooltip("Scale factor for highlight pulse")]
    public float pulseScale = 1.2f;
    
    [Tooltip("Speed of highlight pulse")]
    public float pulseSpeed = 2f;
    
    private bool isPlayerNearby = false;
    private ProceduralCharacterController nearbyPlayer = null;
    private Vector3 originalScale;
    private Collider2D pickupCollider;
    
    private void Awake()
    {
        pickupCollider = GetComponent<Collider2D>();
        if (pickupCollider != null && !pickupCollider.isTrigger)
        {
            pickupCollider.isTrigger = true;
        }
        
        originalScale = transform.localScale;
        
        if (highlightRenderer != null)
        {
            highlightRenderer.enabled = false;
        }
    }
    
    private void Update()
    {
        // Handle manual interaction
        if (!autoPickup && isPlayerNearby && nearbyPlayer != null)
        {
            if (Input.GetKeyDown(interactionKey))
            {
                TryPickup(nearbyPlayer);
            }
        }
        
        // Visual feedback pulse
        if (isPlayerNearby && highlightRenderer != null)
        {
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * (pulseScale - 1f) * 0.5f;
            transform.localScale = originalScale * pulse;
        }
        else
        {
            transform.localScale = originalScale;
        }
    }
    
    private void OnTriggerEnter2D(Collider2D other)
    {
        ProceduralCharacterController player = other.GetComponent<ProceduralCharacterController>();
        if (player != null)
        {
            isPlayerNearby = true;
            nearbyPlayer = player;
            
            if (highlightRenderer != null)
            {
                highlightRenderer.enabled = true;
            }
            
            // Auto-pickup if enabled
            if (autoPickup)
            {
                TryPickup(player);
            }
        }
    }
    
    private void OnTriggerExit2D(Collider2D other)
    {
        ProceduralCharacterController player = other.GetComponent<ProceduralCharacterController>();
        if (player != null && player == nearbyPlayer)
        {
            isPlayerNearby = false;
            nearbyPlayer = null;
            
            if (highlightRenderer != null)
            {
                highlightRenderer.enabled = false;
            }
        }
    }
    
    /// <summary>
    /// Try to pick up this item with a character
    /// </summary>
    public bool TryPickup(ProceduralCharacterController character)
    {
        if (character == null || item == null)
            return false;
        
        bool pickedUp = character.PickupItem(this);
        
        if (pickedUp)
        {
            // Item was successfully picked up, pickup will be handled by OnPickedUp
        }
        
        return pickedUp;
    }
    
    /// <summary>
    /// Called when item is successfully picked up
    /// </summary>
    public void OnPickedUp()
    {
        // Disable or destroy the pickup
        gameObject.SetActive(false);
        // Or use object pooling: ReturnToPool();
    }
    
    /// <summary>
    /// Spawn an item pickup in the world
    /// </summary>
    public static ItemPickup Spawn(Item item, int quantity, Vector3 position)
    {
        if (item == null)
            return null;
        
        // Create GameObject for the pickup
        GameObject pickupObj = new GameObject($"Pickup_{item.itemName}");
        pickupObj.transform.position = position;
        
        // Add SpriteRenderer if item has an icon
        if (item.icon != null)
        {
            SpriteRenderer renderer = pickupObj.AddComponent<SpriteRenderer>();
            renderer.sprite = item.icon;
            renderer.sortingOrder = 10;
        }
        
        // Add collider
        CircleCollider2D collider = pickupObj.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.5f;
        
        // Add ItemPickup component
        ItemPickup pickup = pickupObj.AddComponent<ItemPickup>();
        pickup.item = item;
        pickup.quantity = quantity;
        
        return pickup;
    }
}
