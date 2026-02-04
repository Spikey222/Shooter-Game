using UnityEngine;

/// <summary>
/// Base class for all items in the game. Items are ScriptableObjects that define
/// properties like name, description, weight, and stack size.
/// </summary>
[CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
public class Item : ScriptableObject
{
    [Header("Basic Information")]
    [Tooltip("Display name of the item")]
    public string itemName = "New Item";
    
    [TextArea(3, 5)]
    [Tooltip("Description of the item")]
    public string description = "Item description";
    
    [Tooltip("Icon/sprite for the item")]
    public Sprite icon;
    
    [Header("Inventory Properties")]
    [Tooltip("Weight of the item (displayed in pounds in UI)")]
    public float weight = 1f;
    
    [Tooltip("Type of item")]
    public ItemType itemType = ItemType.Other;
    
    [Tooltip("Maximum number of this item that can stack together (1 = no stacking)")]
    public int maxStackSize = 1;
    
    [Header("Clothing (when ItemType = Clothing)")]
    [Tooltip("Which body part this clothing item equips to (legacy single-slot clothing)")]
    public ClothingEquipSlot clothingEquipSlot = ClothingEquipSlot.Torso;

    [Tooltip("Sprite used as the clothing overlay when equipped (legacy single-slot clothing)")]
    public Sprite overlaySprite;

    [Tooltip("Local X/Y offset applied to the legacy overlay sprite when equipped")]
    public Vector2 overlayOffset;

    [Header("Clothing Pieces (multi-part clothing)")]
    [Tooltip("Optional: define multiple sprites for a single clothing item (e.g., a Shirt). If set, these are used instead of the legacy single-slot fields above.")]
    public ClothingPiece[] clothingPieces;

    /// <summary>
    /// Enum defining different types of items
    /// </summary>
    public enum ItemType
    {
        Weapon,
        Consumable,
        Other,
        Clothing
    }

    /// <summary>
    /// Equip slots for clothing overlays (no legs/pants).
    /// </summary>
    public enum ClothingEquipSlot
    {
        Headwear,
        Hair,
        Torso,
        LeftBicep,
        RightBicep,
        LeftForearm,
        RightForearm,
        LeftHand,
        RightHand
    }

    public enum ClothingVisualMode
    {
        Overlay,
        ReplaceSprite
    }

    [System.Serializable]
    public struct ClothingPiece
    {
        [Tooltip("Which body part this piece affects")]
        public ClothingEquipSlot part;

        [Tooltip("How this piece is applied (overlay sprite vs replace the base sprite)")]
        public ClothingVisualMode visualMode;

        [Tooltip("Sprite to apply for this part (overlay sprite or replacement sprite)")]
        public Sprite sprite;

        [Tooltip("Local X/Y offset applied when visualMode = Overlay (ignored for ReplaceSprite)")]
        public Vector2 localOffset;
    }
    
    /// <summary>
    /// Check if this item can stack
    /// </summary>
    public bool IsStackable()
    {
        return maxStackSize > 1;
    }
    
    /// <summary>
    /// Get the weight of a specific quantity of this item
    /// </summary>
    public float GetWeight(int quantity)
    {
        return weight * quantity;
    }
}
