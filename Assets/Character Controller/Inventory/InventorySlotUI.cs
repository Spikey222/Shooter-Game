using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component for a single inventory item slot.
/// Displays item icon, name, quantity, and weight.
/// </summary>
public class InventorySlotUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Image component for item icon")]
    public Image iconImage;
    
    [Tooltip("Text for item name")]
    public TextMeshProUGUI nameText;
    
    [Tooltip("Text for item quantity (for stackable items)")]
    public TextMeshProUGUI quantityText;
    
    [Tooltip("Text for item weight")]
    public TextMeshProUGUI weightText;
    
    [Tooltip("Button to interact with item")]
    public Button slotButton;
    
    [Tooltip("Button to use item (optional, separate from main button)")]
    public Button useButton;
    
    [Tooltip("Button to drop item (optional)")]
    public Button dropButton;
    
    [Tooltip("Background image (for highlighting selected items)")]
    public Image backgroundImage;

    [Tooltip("Optional: enabled when an accessory is currently equipped/worn")]
    public GameObject equippedIndicator;
    
    private InventoryItem inventoryItem;
    private int slotIndex;
    private InventoryUI parentUI;

    private bool DebugEnabled => parentUI != null && parentUI.enableDebugLogs;

    private const float PoundsPerKilogram = 2.20462262f;

    private void AutoWireEquippedIndicatorIfNeeded()
    {
        if (equippedIndicator != null)
            return;

        Transform[] all = GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t.name == "EquippedIndicator")
            {
                equippedIndicator = t.gameObject;
                break;
            }
        }
    }

    private void AutoWireButtonsIfNeeded()
    {
        // If the prefab didn't assign slotButton, try to find a reasonable default.
        if (slotButton == null)
        {
            // Prefer a Button on the same GameObject.
            slotButton = GetComponent<Button>();
        }

        if (slotButton == null)
        {
            // Otherwise pick the first child Button that isn't the use/drop button.
            Button[] buttons = GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                if (b == null)
                    continue;
                if (b == useButton || b == dropButton)
                    continue;
                slotButton = b;
                break;
            }
        }
    }
    
    /// <summary>
    /// Initialize the slot with an inventory item
    /// </summary>
    public void Initialize(InventoryItem invItem, int index, InventoryUI parent)
    {
        inventoryItem = invItem;
        slotIndex = index;
        parentUI = parent;

        AutoWireButtonsIfNeeded();
        AutoWireEquippedIndicatorIfNeeded();
        
        UpdateDisplay();
        
        // Setup button callbacks
        if (slotButton != null)
        {
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(() => parentUI.OnItemSlotClicked(slotIndex));
        }
        else if (DebugEnabled)
        {
            Debug.LogWarning($"[InventorySlotUI] Slot '{name}' (index {slotIndex}) has no slotButton wired/found. Clicking may do nothing.", this);
        }
        
        if (useButton != null)
        {
            useButton.onClick.RemoveAllListeners();
            useButton.onClick.AddListener(() => parentUI.OnItemSlotUseClicked(slotIndex));
        }
        
        if (dropButton != null)
        {
            dropButton.onClick.RemoveAllListeners();
            dropButton.onClick.AddListener(() => parentUI.OnItemSlotDropClicked(slotIndex));
        }
    }
    
    /// <summary>
    /// Update the visual display of this slot
    /// </summary>
    private void UpdateDisplay()
    {
        if (inventoryItem == null || inventoryItem.IsEmpty())
        {
            // Hide slot if empty
            gameObject.SetActive(false);
            return;
        }
        
        Item item = inventoryItem.item;
        
        // Set icon
        if (iconImage != null)
        {
            if (item.icon != null)
            {
                iconImage.sprite = item.icon;
                iconImage.color = Color.white;
            }
            else
            {
                iconImage.color = new Color(1, 1, 1, 0); // Transparent
            }
        }
        
        // Set name
        if (nameText != null)
        {
            nameText.text = item.itemName;
        }
        
        // Set quantity (only show if stackable and quantity > 1)
        if (quantityText != null)
        {
            if (item.IsStackable() && inventoryItem.quantity > 1)
            {
                quantityText.text = $"x{inventoryItem.quantity}";
                quantityText.gameObject.SetActive(true);
            }
            else
            {
                quantityText.gameObject.SetActive(false);
            }
        }
        
        // Set weight
        if (weightText != null)
        {
            float totalWeightKg = inventoryItem.GetTotalWeight();
            float totalWeightLb = totalWeightKg * PoundsPerKilogram;
            weightText.text = $"{totalWeightLb:F1} lb";
        }

        // Equipped indicator (for clothing and weapons)
        if (equippedIndicator != null)
        {
            bool isEquipped = false;

            if (item.itemType == Item.ItemType.Clothing && parentUI != null && parentUI.character != null && parentUI.character.clothingController != null)
            {
                // Show indicator for any clothing item currently equipped (overlay or ReplaceSprite).
                isEquipped = parentUI.character.clothingController.IsEquipped(item);
            }
            else if (item.itemType == Item.ItemType.Weapon && parentUI != null && parentUI.character != null)
            {
                // Show indicator for any weapon currently equipped
                isEquipped = parentUI.character.IsEquipped(item);
            }

            equippedIndicator.SetActive(isEquipped);
        }
        
        // Show/hide use button based on item type
        if (useButton != null)
        {
            // Clothing is equipped/unequipped by clicking the inventory slot itself.
            useButton.gameObject.SetActive(item.itemType == Item.ItemType.Consumable);
        }
        
        // Show/hide drop button
        if (dropButton != null)
        {
            // Dropping is intentionally disabled for now.
            dropButton.gameObject.SetActive(false);
        }
    }

    // NOTE: We previously only showed the indicator for overlay accessories.
    // Now we show it for any equipped clothing item, including ReplaceSprite shirts.
    
    /// <summary>
    /// Set the slot as selected/highlighted
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (backgroundImage != null)
        {
            backgroundImage.color = selected ? new Color(1f, 1f, 0.5f, 0.3f) : new Color(1f, 1f, 1f, 0.1f);
        }
    }
}
