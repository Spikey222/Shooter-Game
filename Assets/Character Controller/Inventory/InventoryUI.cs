using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component that displays the character's inventory.
/// Shows items, quantities, weights, and allows interaction.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("Enable verbose logs for inventory clicks")]
    public bool enableDebugLogs = false;

    [Header("References")]
    [Tooltip("Reference to the character controller whose inventory to display")]
    public ProceduralCharacterController character;

    [Tooltip("Optional: spectator controller that handles possession. If set, this UI will follow the possessed character.")]
    public SpectatorController spectatorController;
    
    [Header("UI Elements")]
    [Tooltip("Panel that contains the entire inventory UI")]
    public GameObject inventoryPanel;
    
    [Tooltip("Parent transform for item slots (usually a Grid Layout Group or Vertical Layout Group)")]
    public Transform itemSlotsParent;
    
    [Tooltip("Prefab for individual item slots")]
    public GameObject itemSlotPrefab;
    
    [Tooltip("Text displaying current weight")]
    public TextMeshProUGUI weightText;
    
    [Tooltip("Text displaying item count")]
    public TextMeshProUGUI itemCountText;
    
    [Tooltip("Slider/Progress bar for weight (optional)")]
    public Slider weightSlider;

    [Header("Layout (Optional)")]
    [Tooltip("If the itemSlotsParent uses a GridLayoutGroup, force its cell size.")]
    public bool forceGridCellSize = true;

    [Tooltip("Grid cell size for inventory slots (width, height).")]
    public Vector2 gridCellSize = new Vector2(208f, 208f);

    [Tooltip("If true, destroy any existing children under itemSlotsParent when rebuilding UI (prevents leftover template slots).")]
    public bool clearAllChildrenUnderSlotsParent = true;
    
    [Header("Settings")]
    [Tooltip("Key to toggle inventory UI")]
    public KeyCode toggleKey = KeyCode.Tab;
    
    [Tooltip("Whether inventory starts visible or hidden")]
    public bool startVisible = false;

    [Tooltip("If true, inventory UI automatically links to the currently possessed character (and unlinks on unpossess).")]
    public bool autoLinkToPossessedCharacter = true;

    [Tooltip("If false, inventory cannot be opened when not possessing a character.")]
    public bool allowOpenWhenUnpossessed = false;

    [Tooltip("If true, inventory UI auto-hides when you unpossess.")]
    public bool hideWhenUnpossessed = true;
    
    private const float PoundsPerKilogram = 2.20462262f;

    private Inventory inventory;
    private List<InventorySlotUI> itemSlots = new List<InventorySlotUI>();
    
    private void Start()
    {
        ApplyGridLayoutSettings();

        if (autoLinkToPossessedCharacter)
        {
            if (spectatorController == null)
            {
                spectatorController = FindFirstObjectByType<SpectatorController>();
            }

            if (spectatorController != null)
            {
                spectatorController.OnControlledCharacterChanged += HandleControlledCharacterChanged;
                SetCharacter(spectatorController.ControlledCharacter);
            }
            else
            {
                // Fallback to manually assigned character (if any)
                SetCharacter(character);
            }
        }
        else
        {
            SetCharacter(character);
        }
        
        // Initialize UI visibility
        if (inventoryPanel != null)
        {
            bool shouldShow = startVisible;
            if (!allowOpenWhenUnpossessed && character == null)
                shouldShow = false;
            inventoryPanel.SetActive(shouldShow);
        }
        
        // Initial UI update
        UpdateUI();
    }
    
    private void Update()
    {
        // Toggle inventory with key press
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleInventory();
        }
    }
    
    /// <summary>
    /// Toggle inventory panel visibility
    /// </summary>
    public void ToggleInventory()
    {
        if (inventoryPanel != null)
        {
            if (!allowOpenWhenUnpossessed && character == null)
            {
                if (enableDebugLogs)
                    Debug.Log("[InventoryUI] ToggleInventory ignored: no character possessed.", this);
                return;
            }

            bool newState = !inventoryPanel.activeSelf;
            inventoryPanel.SetActive(newState);
            
            if (newState)
            {
                UpdateUI();
            }
        }
    }
    
    /// <summary>
    /// Show inventory panel
    /// </summary>
    public void ShowInventory()
    {
        if (inventoryPanel != null)
        {
            if (!allowOpenWhenUnpossessed && character == null)
                return;

            inventoryPanel.SetActive(true);
            UpdateUI();
        }
    }
    
    /// <summary>
    /// Hide inventory panel
    /// </summary>
    public void HideInventory()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }
    }

    private void HandleControlledCharacterChanged(ProceduralCharacterController newCharacter)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[InventoryUI] Controlled character changed -> {(newCharacter != null ? newCharacter.name : "NULL")}", this);
        }

        SetCharacter(newCharacter);

        if (hideWhenUnpossessed && newCharacter == null)
        {
            HideInventory();
        }
    }

    private void SetCharacter(ProceduralCharacterController newCharacter)
    {
        if (character == newCharacter)
            return;

        // Unsubscribe from previous inventory
        if (inventory != null)
        {
            inventory.OnInventoryChanged -= UpdateUI;
            inventory.OnWeightChanged -= UpdateWeightDisplay;
        }

        character = newCharacter;
        inventory = character != null ? character.inventory : null;

        // Subscribe to new inventory
        if (inventory != null)
        {
            inventory.OnInventoryChanged += UpdateUI;
            inventory.OnWeightChanged += UpdateWeightDisplay;
        }

        // Reset UI if neutral
        if (character == null)
        {
            ClearSlots();
            if (weightText != null)
                weightText.text = "No character possessed";
            if (itemCountText != null)
                itemCountText.text = "Items: 0";
            if (weightSlider != null)
            {
                weightSlider.maxValue = 1f;
                weightSlider.value = 0f;
            }
        }

        UpdateUI();
    }
    
    /// <summary>
    /// Update the entire inventory UI
    /// </summary>
    public void UpdateUI()
    {
        if (inventory == null)
        {
            // Neutral state; no inventory to render.
            if (character == null)
                return;

            // Character exists but inventory missing (unexpected) - still clear.
            ClearSlots();
            return;
        }
        
        // Clear existing slots
        ClearSlots();
        
        // Create slots for each item in inventory
        List<InventoryItem> items = inventory.Items;
        for (int i = 0; i < items.Count; i++)
        {
            CreateItemSlot(items[i], i);
        }
        
        // Update weight display
        UpdateWeightDisplay(inventory.CurrentWeight, inventory.MaxWeight);
        
        // Update item count
        if (itemCountText != null)
        {
            itemCountText.text = $"Items: {items.Count}";
        }
    }
    
    /// <summary>
    /// Create a UI slot for an inventory item
    /// </summary>
    private void CreateItemSlot(InventoryItem invItem, int slotIndex)
    {
        if (itemSlotPrefab == null || itemSlotsParent == null || invItem == null || invItem.IsEmpty())
            return;
        
        // Instantiate slot prefab
        GameObject slotObj = Instantiate(itemSlotPrefab, itemSlotsParent);
        
        // Get InventorySlotUI component
        InventorySlotUI slotUI = slotObj.GetComponent<InventorySlotUI>();
        if (slotUI == null)
        {
            slotUI = slotObj.AddComponent<InventorySlotUI>();
        }
        
        // Initialize slot
        slotUI.Initialize(invItem, slotIndex, this);
        itemSlots.Add(slotUI);
    }
    
    /// <summary>
    /// Clear all item slots
    /// </summary>
    private void ClearSlots()
    {
        if (clearAllChildrenUnderSlotsParent && itemSlotsParent != null)
        {
            for (int i = itemSlotsParent.childCount - 1; i >= 0; i--)
            {
                Destroy(itemSlotsParent.GetChild(i).gameObject);
            }
            itemSlots.Clear();
            return;
        }

        foreach (var slot in itemSlots)
        {
            if (slot != null && slot.gameObject != null)
            {
                Destroy(slot.gameObject);
            }
        }
        itemSlots.Clear();
    }
    
    /// <summary>
    /// Update weight display
    /// </summary>
    private void UpdateWeightDisplay(float currentWeight, float maxWeight)
    {
        float currentLb = currentWeight * PoundsPerKilogram;
        float maxLb = maxWeight * PoundsPerKilogram;

        if (weightText != null)
        {
            weightText.text = $"Weight: {currentLb:F1}/{maxLb:F1} lb";
        }
        
        if (weightSlider != null)
        {
            weightSlider.maxValue = maxLb;
            weightSlider.value = currentLb;
        }
    }

    private void ApplyGridLayoutSettings()
    {
        if (!forceGridCellSize || itemSlotsParent == null)
            return;

        GridLayoutGroup grid = itemSlotsParent.GetComponent<GridLayoutGroup>();
        if (grid == null)
            return;

        grid.cellSize = gridCellSize;
    }
    
    /// <summary>
    /// Called when an item slot is clicked
    /// </summary>
    public void OnItemSlotClicked(int slotIndex)
    {
        if (inventory == null || character == null)
        {
            if (enableDebugLogs)
                Debug.LogWarning($"[InventoryUI] OnItemSlotClicked({slotIndex}) ignored: inventory or character is NULL.", this);
            return;
        }
        
        InventoryItem invItem = inventory.GetItemAt(slotIndex);
        if (invItem == null || invItem.IsEmpty())
        {
            if (enableDebugLogs)
                Debug.LogWarning($"[InventoryUI] OnItemSlotClicked({slotIndex}) ignored: slot empty/out of range.", this);
            return;
        }

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[InventoryUI] OnItemSlotClicked({slotIndex}) item='{invItem.item.itemName}', type={invItem.item.itemType}, qty={invItem.quantity}, clothingController={(character.clothingController != null ? character.clothingController.name : "NULL")}",
                this
            );
        }
        
        // If it's a weapon, equip it
        if (invItem.item.itemType == Item.ItemType.Weapon)
        {
            character.EquipItemFromInventory(slotIndex);
            UpdateUI();
        }
        // If it's a consumable, use it
        else if (invItem.item.itemType == Item.ItemType.Consumable)
        {
            character.UseConsumable(invItem.item, 1);
            UpdateUI();
        }
        // If it's clothing, equip it as an overlay
        else if (invItem.item.itemType == Item.ItemType.Clothing)
        {
            if (character.clothingController != null)
            {
                bool result = character.clothingController.ToggleClothingFromInventory(slotIndex);
                if (enableDebugLogs)
                    Debug.Log($"[InventoryUI] Clothing toggle result={result}", this);
                UpdateUI();
            }
            else if (enableDebugLogs)
            {
                Debug.LogWarning("[InventoryUI] Clothing click ignored: character.clothingController is NULL.", this);
            }
        }
    }
    
    /// <summary>
    /// Called when an item slot's use button is clicked
    /// </summary>
    public void OnItemSlotUseClicked(int slotIndex)
    {
        if (inventory == null || character == null)
            return;
        
        InventoryItem invItem = inventory.GetItemAt(slotIndex);
        if (invItem == null || invItem.IsEmpty())
            return;
        
        if (invItem.item.itemType == Item.ItemType.Consumable)
        {
            character.UseConsumable(invItem.item, 1);
            UpdateUI();
        }
    }
    
    /// <summary>
    /// Called when an item slot's drop button is clicked
    /// </summary>
    public void OnItemSlotDropClicked(int slotIndex)
    {
        // Dropping is intentionally disabled for now.
        // (We don't want to remove items from inventory without spawning them in the world.)
        Debug.Log("Drop is disabled (not implemented yet).");
    }
    
    private void OnDestroy()
    {
        if (spectatorController != null)
        {
            spectatorController.OnControlledCharacterChanged -= HandleControlledCharacterChanged;
        }

        // Unsubscribe from events
        if (inventory != null)
        {
            inventory.OnInventoryChanged -= UpdateUI;
            inventory.OnWeightChanged -= UpdateWeightDisplay;
        }
    }
}
