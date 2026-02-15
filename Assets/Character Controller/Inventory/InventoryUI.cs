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

    [Header("Audio")]
    [Tooltip("Sound played when inventory opens (optional)")]
    public AudioClip openSound;
    [Tooltip("Sound played when inventory closes (optional)")]
    public AudioClip closeSound;
    [Tooltip("Volume for open/close sounds")]
    [Range(0f, 1f)]
    public float audioVolume = 1f;
    [Tooltip("Sound played when a consumable is used (delay timer + this sound). Assign here on the same component that has Open/Close sounds.")]
    public AudioClip consumableUseSound;
    [Tooltip("Volume for consumable use sound")]
    [Range(0f, 1f)]
    public float consumableUseVolume = 1f;

    [Header("Body Outline Position")]
    [Tooltip("Assign BodyOutline > Outline RectTransform here. It will move when inventory opens/closes.")]
    public RectTransform bodyOutlineOutline;
    [Tooltip("Position when inventory is closed (neutral).")]
    public Vector2 bodyOutlinePositionClosed = new Vector2(286f, 299f);
    [Tooltip("Position when inventory is open.")]
    public Vector2 bodyOutlinePositionOpen = new Vector2(673f, 579f);
    
    private const float PoundsPerKilogram = 2.20462262f;

    private Inventory inventory;
    private List<InventorySlotUI> itemSlots = new List<InventorySlotUI>();
    private AudioSource audioSource;
    private int pendingHealableSlotIndex = -1;
    private Dictionary<Item, float> itemCooldownEndTime = new Dictionary<Item, float>();
    private Dictionary<Item, float> itemCooldownDuration = new Dictionary<Item, float>();

    /// <summary>
    /// True when the inventory panel is open. Used to block mouse aim and attack on the controlled character.
    /// </summary>
    public static bool IsInventoryOpen { get; private set; }
    
    private void Start()
    {
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
        
        // Get or add AudioSource for inventory sounds
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && (openSound != null || closeSound != null))
            audioSource = gameObject.AddComponent<AudioSource>();

        // Initialize UI visibility
        if (inventoryPanel != null)
        {
            bool shouldShow = startVisible;
            if (!allowOpenWhenUnpossessed && character == null)
                shouldShow = false;
            SetInventoryPanelState(shouldShow, playSound: false);
        }
        
        // Initial UI update
        UpdateUI();
    }

    private void SetInventoryPanelState(bool open, bool playSound = true)
    {
        if (inventoryPanel == null) return;
        inventoryPanel.SetActive(open);
        IsInventoryOpen = open;
        if (!open)
            pendingHealableSlotIndex = -1;
        ApplyBodyOutlinePosition(open);
        if (playSound && audioSource != null)
        {
            var clip = open ? openSound : closeSound;
            if (clip != null)
                audioSource.PlayOneShot(clip, audioVolume);
        }
    }

    private void ApplyBodyOutlinePosition(bool inventoryOpen)
    {
        if (bodyOutlineOutline == null)
            return;
        if (inventoryOpen)
        {
            bodyOutlineOutline.anchorMin = new Vector2(0.5f, 0.5f);
            bodyOutlineOutline.anchorMax = new Vector2(0.5f, 0.5f);
            bodyOutlineOutline.anchoredPosition = bodyOutlinePositionOpen;
        }
        else
        {
            bodyOutlineOutline.anchorMin = Vector2.zero;
            bodyOutlineOutline.anchorMax = Vector2.zero;
            bodyOutlineOutline.anchoredPosition = bodyOutlinePositionClosed;
        }
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
            SetInventoryPanelState(newState);
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

            SetInventoryPanelState(true);
            UpdateUI();
        }
    }
    
    /// <summary>
    /// Hide inventory panel
    /// </summary>
    /// <param name="playSound">If true, plays close sound (use false for programmatic hide e.g. on unpossess)</param>
    public void HideInventory(bool playSound = true)
    {
        if (inventoryPanel != null)
        {
            SetInventoryPanelState(false, playSound);
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
            HideInventory(playSound: false); // programmatic hide on unpossess
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

        // Re-apply cooldown after rebuild so delay timer and radial survive UpdateUI
        Item item = invItem.item;
        float endTime = 0f;
        float dur = 0f;
        bool hasCD = itemCooldownEndTime.TryGetValue(item, out endTime) && itemCooldownDuration.TryGetValue(item, out dur);
        float rem = hasCD ? endTime - Time.time : 0f;
        bool applyRem = hasCD && rem > 0f;
        if (applyRem)
            slotUI.StartCooldownWithRemaining(rem, dur);
        else if (hasCD && rem <= 0f)
        {
            itemCooldownEndTime.Remove(item);
            itemCooldownDuration.Remove(item);
        }
        // #region agent log
        try { System.IO.File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"id\":\"log_createSlot\",\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ",\"location\":\"InventoryUI.CreateItemSlot\",\"message\":\"Slot created\",\"data\":{\"itemName\":\"" + (item?.name ?? "null") + "\",\"hasCooldownInDict\":" + (hasCD ? "true" : "false") + ",\"rem\":" + rem + ",\"applyRemaining\":" + (applyRem ? "true" : "false") + "},\"hypothesisId\":\"H3\"}\n"); } catch { }
        // #endregion
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
        // Weights are stored in pounds (Item.weight and Inventory use same unit); display as-is
        if (weightText != null)
        {
            weightText.text = $"Weight: {currentWeight:F1}/{maxWeight:F1} lb";
        }
        
        if (weightSlider != null)
        {
            weightSlider.maxValue = maxWeight;
            weightSlider.value = currentWeight;
        }
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
        // If it's a consumable, use it or enter limb-target mode
        else if (invItem.item.itemType == Item.ItemType.Consumable)
        {
            if (invItem.item is ConsumableItem consumable && consumable.requiresLimbTarget)
            {
                if (IsItemOnCooldown(invItem.item))
                {
                    UpdateUI();
                    return;
                }
                if (pendingHealableSlotIndex == slotIndex)
                {
                    pendingHealableSlotIndex = -1;
                }
                else
                {
                    pendingHealableSlotIndex = slotIndex;
                }
                UpdateUI();
            }
            else
            {
                // #region agent log
                float useTimeVal = invItem.item is ConsumableItem c1 ? c1.useTime : 0f;
                try { System.IO.File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"id\":\"log_use\",\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ",\"location\":\"InventoryUI.OnItemSlotClicked\",\"message\":\"Consumable direct use attempt\",\"data\":{\"itemName\":\"" + (invItem.item?.itemName ?? "null") + "\",\"useTime\":" + useTimeVal + ",\"slotIndex\":" + slotIndex + "},\"hypothesisId\":\"H1\"}\n"); } catch { }
                // #endregion
                bool used = character.UseConsumable(invItem.item, 1, slotIndex);
                bool willStart = used && invItem.item is ConsumableItem c2 && c2.useTime > 0f;
                // #region agent log
                try { System.IO.File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"id\":\"log_used\",\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ",\"location\":\"InventoryUI.OnItemSlotClicked\",\"message\":\"After UseConsumable\",\"data\":{\"used\":" + (used ? "true" : "false") + ",\"willStartCooldown\":" + (willStart ? "true" : "false") + "},\"hypothesisId\":\"H1\"}\n"); } catch { }
                // #endregion
                if (used)
                {
                    PlayConsumableUseSound(invItem.item);
                    if (willStart)
                        StartCooldownForSlot(slotIndex, (invItem.item as ConsumableItem).useTime);
                }
                UpdateUI();
            }
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
            if (invItem.item is ConsumableItem consumable && consumable.requiresLimbTarget)
            {
                if (IsItemOnCooldown(invItem.item))
                {
                    UpdateUI();
                    return;
                }
                if (pendingHealableSlotIndex == slotIndex)
                    pendingHealableSlotIndex = -1;
                else
                    pendingHealableSlotIndex = slotIndex;
                UpdateUI();
            }
            else
            {
                bool used = character.UseConsumable(invItem.item, 1, slotIndex);
                if (used)
                {
                    PlayConsumableUseSound(invItem.item);
                    if (invItem.item is ConsumableItem cu && cu.useTime > 0f)
                        StartCooldownForSlot(slotIndex, cu.useTime);
                }
                UpdateUI();
            }
        }
    }
    
    /// <summary>
    /// True when a consumable that requires a limb target is selected and waiting for the user to click a body part.
    /// </summary>
    public bool HasPendingHealable()
    {
        if (pendingHealableSlotIndex < 0 || inventory == null || character == null)
            return false;
        InventoryItem invItem = inventory.GetItemAt(pendingHealableSlotIndex);
        if (invItem == null || invItem.IsEmpty() || invItem.item.itemType != Item.ItemType.Consumable)
            return false;
        if (invItem.item is not ConsumableItem consumable || !consumable.requiresLimbTarget)
            return false;
        return true;
    }
    
    /// <summary>
    /// True if this item is currently on use delay (medical cooldown). Use to block healing another part until timer is up.
    /// </summary>
    public bool IsItemOnCooldown(Item item)
    {
        if (item == null) return false;
        return itemCooldownEndTime.TryGetValue(item, out float endTime) && Time.time < endTime;
    }

    /// <summary>
    /// Apply the pending healable consumable to the given limb. Returns true if applied and one item was consumed.
    /// Bandage mode stays active so the user can apply to more limbs without re-clicking the item; cleared when the slot is empty or user cancels.
    /// Cannot apply until delay timer is up.
    /// </summary>
    public bool TryApplyPendingHealableToLimb(ProceduralCharacterController.LimbType limbType)
    {
        if (!HasPendingHealable())
            return false;
        InventoryItem invItem = inventory.GetItemAt(pendingHealableSlotIndex);
        if (invItem == null || invItem.IsEmpty() || invItem.item.itemType != Item.ItemType.Consumable)
        {
            pendingHealableSlotIndex = -1;
            return false;
        }
        Item item = invItem.item;
        if (IsItemOnCooldown(item))
            return false;
        bool success = character.UseConsumableOnLimb(item, limbType, 1, pendingHealableSlotIndex);
        if (success)
        {
            PlayConsumableUseSound(item);

            if (item is ConsumableItem consumable && consumable.useTime > 0f)
                StartCooldownForSlot(pendingHealableSlotIndex, consumable.useTime);

            if (!inventory.HasItem(item, 1))
                pendingHealableSlotIndex = -1;
        }
        else
            pendingHealableSlotIndex = -1;
        UpdateUI();
        return success;
    }

    /// <summary>
    /// True when the given slot index is the pending healable (bandage mode active). Used by slot UI for pulsating indicator.
    /// </summary>
    public bool IsSlotPendingHealable(int slotIndex)
    {
        return pendingHealableSlotIndex >= 0 && pendingHealableSlotIndex == slotIndex;
    }

    private void PlayConsumableUseSound(Item item = null)
    {
        if (audioSource == null) return;
        AudioClip clip = null;
        float vol = consumableUseVolume;
        if (item is ConsumableItem cons && cons.useSound != null)
        {
            clip = cons.useSound;
            vol = cons.useSoundVolume;
        }
        else if (consumableUseSound != null)
            clip = consumableUseSound;
        // #region agent log
        try { System.IO.File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"id\":\"log_playSound\",\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ",\"location\":\"InventoryUI.PlayConsumableUseSound\",\"message\":\"Play consumable sound\",\"data\":{\"audioSourceNotNull\":true,\"clipNotNull\":" + (clip != null ? "true" : "false") + ",\"fromItem\":" + (item is ConsumableItem c && c.useSound != null ? "true" : "false") + "},\"hypothesisId\":\"H4\"}\n"); } catch { }
        // #endregion
        if (clip != null)
            audioSource.PlayOneShot(clip, vol);
    }

    private void StartCooldownForSlot(int slotIndex, float duration)
    {
        if (duration <= 0f)
            return;
        if (inventory == null)
            return;
        InventoryItem invItem = inventory.GetItemAt(slotIndex);
        if (invItem == null || invItem.IsEmpty())
            return;
        Item item = invItem.item;
        itemCooldownEndTime[item] = Time.time + duration;
        itemCooldownDuration[item] = duration;
        // #region agent log
        try { System.IO.File.AppendAllText(@"f:\Unity\Shooter\.cursor\debug.log", "{\"id\":\"log_startCD\",\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ",\"location\":\"InventoryUI.StartCooldownForSlot\",\"message\":\"Cooldown stored\",\"data\":{\"slotIndex\":" + slotIndex + ",\"itemName\":\"" + (item?.name ?? "null") + "\",\"duration\":" + duration + ",\"itemSlotsCount\":" + itemSlots.Count + "},\"hypothesisId\":\"H2\"}\n"); } catch { }
        // #endregion
        if (slotIndex >= 0 && slotIndex < itemSlots.Count)
        {
            var slot = itemSlots[slotIndex];
            if (slot != null)
                slot.StartCooldown(duration);
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
