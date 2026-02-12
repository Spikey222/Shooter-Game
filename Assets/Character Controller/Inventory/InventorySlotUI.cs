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
    private bool pendingHealableActive;

    [Header("Pending healable (bandage) pulse")]
    [Tooltip("Pulse speed for the indicator when bandage/limb-target mode is active (lower = slower)")]
    public float pendingPulseSpeed = 1.2f;

    [Header("Cooldown (delay timer)")]
    [Tooltip("Radial 360 image for the delay timer: fills in sync with Use Time after each use. Assign here or add a child of Equipped Indicator named 'CooldownRadial' with an Image (Filled, Radial 360).")]
    public Image cooldownImage;
    private float cooldownRemaining;
    private float cooldownDuration;

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

    private void EnsureCooldownRadialImage()
    {
        if (cooldownImage != null)
        {
            cooldownImage.type = Image.Type.Filled;
            cooldownImage.fillMethod = Image.FillMethod.Radial360;
            cooldownImage.fillOrigin = (int)Image.Origin360.Right;
            cooldownImage.fillClockwise = true;
            return;
        }
        if (equippedIndicator == null)
            return;
        Transform cooldownT = equippedIndicator.transform.Find("CooldownRadial");
        if (cooldownT != null)
        {
            cooldownImage = cooldownT.GetComponent<Image>();
            if (cooldownImage != null)
            {
                cooldownImage.type = Image.Type.Filled;
                cooldownImage.fillMethod = Image.FillMethod.Radial360;
                cooldownImage.fillOrigin = (int)Image.Origin360.Right;
                cooldownImage.fillClockwise = true;
            }
        }
    }

    private void AutoWireDisplayReferencesIfNeeded()
    {
        if (nameText != null && weightText != null && quantityText != null && iconImage != null)
            return;

        Transform[] all = GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t == null)
                continue;
            if (nameText == null && t.name == "NameText")
            {
                var tmp = t.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                    nameText = tmp;
            }
            else if (weightText == null && t.name == "WeightText")
            {
                var tmp = t.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                    weightText = tmp;
            }
            else if (quantityText == null && t.name == "QuantityText")
            {
                var tmp = t.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                    quantityText = tmp;
            }
            else if (iconImage == null && (t.name == "Image" || t.name == "Icon"))
            {
                var img = t.GetComponent<Image>();
                if (img != null)
                    iconImage = img;
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

        AutoWireDisplayReferencesIfNeeded();
        AutoWireButtonsIfNeeded();
        AutoWireEquippedIndicatorIfNeeded();
        EnsureCooldownRadialImage();
        
        UpdateDisplay();
        
        // Setup button callbacks
        if (slotButton != null)
        {
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(OnSlotButtonClicked);
        }
        else if (DebugEnabled)
        {
            Debug.LogWarning($"[InventorySlotUI] Slot '{name}' (index {slotIndex}) has no slotButton wired/found. Clicking may do nothing.", this);
        }
        
        if (useButton != null)
        {
            useButton.onClick.RemoveAllListeners();
            useButton.onClick.AddListener(OnUseButtonClicked);
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
            nameText.gameObject.SetActive(true);
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
            weightText.gameObject.SetActive(true);
        }

        // Equipped indicator (for clothing and weapons), or pulsating indicator when bandage/limb-target mode is active
        if (equippedIndicator != null)
        {
            bool isEquipped = false;
            pendingHealableActive = false;

            if (item.itemType == Item.ItemType.Clothing && parentUI != null && parentUI.character != null && parentUI.character.clothingController != null)
            {
                isEquipped = parentUI.character.clothingController.IsEquipped(item);
            }
            else if (item.itemType == Item.ItemType.Weapon && parentUI != null && parentUI.character != null)
            {
                isEquipped = parentUI.character.IsEquipped(item);
            }
            else if (item.itemType == Item.ItemType.Consumable && parentUI != null && parentUI.IsSlotPendingHealable(slotIndex))
            {
                pendingHealableActive = true;
            }

            equippedIndicator.SetActive(isEquipped || pendingHealableActive);
        }
        
        // Cooldown radial: show for consumables with Use Time (delay), hide otherwise
        if (cooldownImage != null)
        {
            bool showCooldown = item is ConsumableItem cons && cons.useTime > 0f;
            cooldownImage.gameObject.SetActive(showCooldown);
            if (showCooldown && cooldownRemaining <= 0f)
                cooldownImage.fillAmount = 1f;
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
    
    private void Update()
    {
        // Cooldown timer / radial fill
        if (cooldownRemaining > 0f)
        {
            cooldownRemaining -= Time.deltaTime;
            if (cooldownRemaining < 0f)
                cooldownRemaining = 0f;

            if (cooldownImage != null && cooldownDuration > 0f)
            {
                // 0 -> 1 as the item becomes ready again
                float t = 1f - (cooldownRemaining / cooldownDuration);
                cooldownImage.fillAmount = Mathf.Clamp01(t);
            }
        }
        else if (cooldownImage != null && cooldownDuration > 0f)
        {
            cooldownImage.fillAmount = 1f;
        }

        // Pending-healable indicator alpha pulse (transparency)
        if (equippedIndicator == null)
            return;

        var indicatorImage = equippedIndicator.GetComponent<Image>();
        if (indicatorImage == null)
            return;

        if (pendingHealableActive && equippedIndicator.activeSelf)
        {
            // Pulse alpha between 40% and 100%
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * pendingPulseSpeed * Mathf.PI * 2f);
            float alpha = Mathf.Lerp(0.4f, 1f, t);
            Color c = indicatorImage.color;
            c.a = alpha;
            indicatorImage.color = c;
        }
        else
        {
            Color c = indicatorImage.color;
            c.a = 1f;
            indicatorImage.color = c;
        }
    }

    /// <summary>
    /// Start a cooldown for this slot (delay in seconds). Radial 360 image fills from 0 to 1 in sync; when full, item can be used again.
    /// </summary>
    public void StartCooldown(float duration)
    {
        if (duration <= 0f)
            return;
        cooldownDuration = duration;
        cooldownRemaining = duration;
        if (cooldownImage != null)
            cooldownImage.fillAmount = 0f;
    }

    /// <summary>
    /// Apply remaining cooldown after UI rebuild so the delay timer and radial persist.
    /// </summary>
    public void StartCooldownWithRemaining(float remaining, float fullDuration)
    {
        if (fullDuration <= 0f)
            return;
        cooldownDuration = fullDuration;
        cooldownRemaining = Mathf.Max(0f, remaining);
        if (cooldownImage != null)
            cooldownImage.fillAmount = 1f - Mathf.Clamp01(cooldownRemaining / fullDuration);
    }

    public bool IsOnCooldown()
    {
        return cooldownRemaining > 0f;
    }

    private void OnSlotButtonClicked()
    {
        if (inventoryItem == null || inventoryItem.IsEmpty())
            return;

        if (inventoryItem.item is ConsumableItem consumable && consumable.useTime > 0f && IsOnCooldown())
            return;

        parentUI?.OnItemSlotClicked(slotIndex);
    }

    private void OnUseButtonClicked()
    {
        if (inventoryItem == null || inventoryItem.IsEmpty())
            return;

        if (inventoryItem.item is ConsumableItem consumable && consumable.useTime > 0f && IsOnCooldown())
            return;

        parentUI?.OnItemSlotUseClicked(slotIndex);
    }

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
