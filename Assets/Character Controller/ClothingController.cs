using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages equipping/unequipping clothing visuals on a procedural character.
///
/// Supports two rendering approaches per body-part:
/// - Overlay: spawn a child SpriteRenderer at an overlay point (good for hair/headwear).
/// - ReplaceSprite: directly swap the SpriteRenderer sprite on the underlying limb/torso (good for shirts).
/// </summary>
public class ClothingController : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("Enable verbose logs to help debug equip/unequip behavior")]
    public bool enableDebugLogs = false;

    private struct EquippedPart
    {
        public Item item;
        public Item.ClothingVisualMode visualMode;

        // Overlay
        public GameObject overlayObject;

        // ReplaceSprite
        public SpriteRenderer targetRenderer;
        public Sprite originalSprite;
    }

    private struct ResolvedPiece
    {
        public Item.ClothingEquipSlot part;
        public Item.ClothingVisualMode visualMode;
        public Sprite sprite;
        public Vector2 localOffset;
    }

    [Header("References")]
    [Tooltip("Character controller used to find limb transforms (auto-assigned if not set)")]
    public ProceduralCharacterController character;

    [Tooltip("Inventory to remove/add clothing items from (auto-assigned if not set)")]
    public Inventory inventory;

    [Header("Overlay Points (optional overrides)")]
    public Transform headwearPoint;
    public Transform hairPoint;
    public Transform torsoPoint;
    public Transform leftBicepPoint;
    public Transform rightBicepPoint;
    public Transform leftForearmPoint;
    public Transform rightForearmPoint;
    public Transform leftHandPoint;
    public Transform rightHandPoint;

    [Header("Rendering")]
    [Tooltip("Base sorting order offset above the underlying limb sprite")]
    public int overlaySortingOrderOffset = 1;

    [Tooltip("Additional offset applied to headwear so it renders above hair")]
    public int headwearAdditionalSortingOffset = 1;

    // One equipped entry per body-part.
    private readonly Dictionary<Item.ClothingEquipSlot, EquippedPart> equippedByPart = new Dictionary<Item.ClothingEquipSlot, EquippedPart>();

    // Tracks items that were removed from inventory on equip (so they can be returned once on unequip).
    private readonly HashSet<Item> removedFromInventory = new HashSet<Item>();

    private void Awake()
    {
        if (character == null)
        {
            character = GetComponent<ProceduralCharacterController>();
        }

        if (inventory == null)
        {
            inventory = character != null ? character.inventory : GetComponent<Inventory>();
        }

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[ClothingController] Awake on '{name}'. character={(character != null ? character.name : "NULL")}, inventory={(inventory != null ? inventory.name : "NULL")}",
                this
            );
        }

        // Only needed for overlay items; safe to call anyway.
        EnsureOverlayPoints();
    }

    /// <summary>
    /// Equip a clothing item from inventory by inventory slot index.
    /// This removes one from inventory, equips the visuals, and will return it on unequip.
    /// (Not used by the click-to-toggle UI path.)
    /// </summary>
    public bool EquipClothingFromInventory(int inventorySlotIndex)
    {
        if (inventory == null)
            return false;

        InventoryItem invItem = inventory.GetItemAt(inventorySlotIndex);
        if (invItem == null || invItem.IsEmpty())
            return false;

        return EquipClothing(invItem.item, removeFromInventory: true);
    }

    /// <summary>
    /// Toggle a clothing item from inventory by inventory slot index.
    /// Click once to equip visuals, click again to unequip visuals.
    /// (Item stays in inventory in this workflow.)
    /// </summary>
    public bool ToggleClothingFromInventory(int inventorySlotIndex)
    {
        if (inventory == null)
        {
            if (enableDebugLogs)
                Debug.LogWarning($"[ClothingController] ToggleClothingFromInventory({inventorySlotIndex}) failed: inventory is NULL.", this);
            return false;
        }

        InventoryItem invItem = inventory.GetItemAt(inventorySlotIndex);
        if (invItem == null || invItem.IsEmpty())
        {
            if (enableDebugLogs)
                Debug.LogWarning($"[ClothingController] ToggleClothingFromInventory({inventorySlotIndex}) failed: inventory slot empty/out of range.", this);
            return false;
        }

        Item item = invItem.item;
        if (item == null || item.itemType != Item.ItemType.Clothing)
        {
            if (enableDebugLogs)
                Debug.LogWarning($"[ClothingController] ToggleClothingFromInventory({inventorySlotIndex}) failed: item is NULL or not Clothing.", this);
            return false;
        }

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[ClothingController] ToggleClothingFromInventory({inventorySlotIndex}) item='{item.itemName}', isEquipped={IsEquipped(item)}, pieces={(item.clothingPieces != null ? item.clothingPieces.Length : 0)}",
                this
            );
        }

        if (IsEquipped(item))
        {
            // Item stayed in inventory, so don't add another copy back.
            return UnequipClothing(item, addToInventory: false);
        }

        return EquipClothing(item, removeFromInventory: false);
    }

    /// <summary>
    /// Equip the given clothing Item.
    /// - If item.clothingPieces has entries: equips multi-part (shirts etc) using their visualMode per part.
    /// - Else: uses legacy fields clothingEquipSlot + overlaySprite as a single overlay.
    /// </summary>
    public bool EquipClothing(Item item, bool removeFromInventory)
    {
        if (item == null || item.itemType != Item.ItemType.Clothing)
        {
            if (enableDebugLogs)
                Debug.LogWarning("[ClothingController] EquipClothing failed: item is NULL or not Clothing.", this);
            return false;
        }

        List<ResolvedPiece> pieces = ResolvePieces(item);
        if (pieces.Count == 0)
        {
            if (enableDebugLogs)
                Debug.LogWarning($"[ClothingController] EquipClothing failed: item '{item.itemName}' has no clothing pieces and no legacy overlay sprite.", this);
            return false;
        }

        // Remove from inventory once (for legacy equip path).
        if (removeFromInventory)
        {
            if (inventory == null || !inventory.HasItem(item, 1))
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[ClothingController] EquipClothing failed: inventory missing '{item.itemName}'.", this);
                return false;
            }
            inventory.RemoveItem(item, 1);
            removedFromInventory.Add(item);
        }

        // Unequip any conflicting items occupying parts this item wants.
        HashSet<Item> conflicts = new HashSet<Item>();
        foreach (var piece in pieces)
        {
            if (equippedByPart.TryGetValue(piece.part, out EquippedPart existing) && existing.item != null && existing.item != item)
            {
                conflicts.Add(existing.item);
            }
        }
        foreach (var conflictItem in conflicts)
        {
            if (enableDebugLogs)
                Debug.Log($"[ClothingController] Equipping '{item.itemName}' conflicts with '{conflictItem.itemName}' -> unequipping conflict item.", this);
            UnequipClothing(conflictItem, addToInventory: true);
        }

        bool equippedAny = false;

        // Overlay points only needed for overlay pieces.
        EnsureOverlayPoints();

        foreach (var piece in pieces)
        {
            if (piece.sprite == null)
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[ClothingController] Skipping piece '{piece.part}' for '{item.itemName}': sprite is NULL.", this);
                continue;
            }

            // If something is already equipped in this part (same item partial state), clear it first.
            if (equippedByPart.ContainsKey(piece.part))
            {
                UnequipPart(piece.part);
            }

            if (piece.visualMode == Item.ClothingVisualMode.ReplaceSprite)
            {
                if (!TryGetSpriteRendererForPart(piece.part, out SpriteRenderer renderer) || renderer == null)
                {
                    if (enableDebugLogs)
                        Debug.LogWarning($"[ClothingController] Cannot ReplaceSprite for '{piece.part}' (item '{item.itemName}'): missing SpriteRenderer/limbSprite.", this);
                    continue;
                }

                Sprite original = renderer.sprite;
                renderer.sprite = piece.sprite;

                equippedByPart[piece.part] = new EquippedPart
                {
                    item = item,
                    visualMode = Item.ClothingVisualMode.ReplaceSprite,
                    overlayObject = null,
                    targetRenderer = renderer,
                    originalSprite = original
                };

                equippedAny = true;

                if (enableDebugLogs)
                {
                    Debug.Log(
                        $"[ClothingController] ReplaceSprite '{item.itemName}' part={piece.part} renderer='{renderer.name}' original='{(original != null ? original.name : "NULL")}' new='{piece.sprite.name}'",
                        this
                    );
                }
            }
            else // Overlay
            {
                Transform point = GetOverlayPoint(piece.part);
                if (point == null)
                {
                    if (enableDebugLogs)
                        Debug.LogWarning($"[ClothingController] Cannot Overlay for '{piece.part}' (item '{item.itemName}'): overlay point is NULL.", this);
                    continue;
                }

                GameObject overlayObject = CreateOverlayObject(item, piece.part, piece.sprite, piece.localOffset, point);
                equippedByPart[piece.part] = new EquippedPart
                {
                    item = item,
                    visualMode = Item.ClothingVisualMode.Overlay,
                    overlayObject = overlayObject,
                    targetRenderer = null,
                    originalSprite = null
                };

                equippedAny = true;
            }
        }

        if (!equippedAny)
        {
            // If we removed from inventory but didn't equip anything, give it back immediately.
            if (removeFromInventory && inventory != null && removedFromInventory.Contains(item))
            {
                inventory.AddItem(item, 1);
                removedFromInventory.Remove(item);
            }
        }

        return equippedAny;
    }

    /// <summary>
    /// Unequip everything affecting a specific part (and optionally return removed inventory items).
    /// </summary>
    public bool UnequipClothing(Item.ClothingEquipSlot part, bool addToInventory = true)
    {
        if (!equippedByPart.TryGetValue(part, out EquippedPart equipped) || equipped.item == null)
        {
            if (enableDebugLogs)
                Debug.Log($"[ClothingController] UnequipClothing({part}) ignored: nothing equipped.", this);
            return false;
        }

        Item item = equipped.item;
        UnequipPart(part);

        if (addToInventory)
        {
            ReturnToInventoryIfNeeded(item);
        }

        return true;
    }

    /// <summary>
    /// Unequip all parts belonging to the given item (and optionally return removed inventory items).
    /// </summary>
    public bool UnequipClothing(Item item, bool addToInventory = true)
    {
        if (item == null)
            return false;

        List<Item.ClothingEquipSlot> partsToRemove = new List<Item.ClothingEquipSlot>();
        foreach (var kvp in equippedByPart)
        {
            if (kvp.Value.item == item)
                partsToRemove.Add(kvp.Key);
        }

        if (partsToRemove.Count == 0)
            return false;

        foreach (var part in partsToRemove)
        {
            UnequipPart(part);
        }

        if (addToInventory)
        {
            ReturnToInventoryIfNeeded(item);
        }

        return true;
    }

    public Item GetEquippedItem(Item.ClothingEquipSlot part)
    {
        return equippedByPart.TryGetValue(part, out EquippedPart equipped) ? equipped.item : null;
    }

    public bool IsEquipped(Item item)
    {
        if (item == null)
            return false;
        foreach (var kvp in equippedByPart)
        {
            if (kvp.Value.item == item)
                return true;
        }
        return false;
    }

    private void ReturnToInventoryIfNeeded(Item item)
    {
        // Only return if this item was removed from inventory AND is no longer equipped anywhere.
        if (item == null)
            return;

        if (!removedFromInventory.Contains(item))
            return;

        if (IsEquipped(item))
            return;

        if (inventory != null)
        {
            inventory.AddItem(item, 1);
        }

        removedFromInventory.Remove(item);
    }

    private void UnequipPart(Item.ClothingEquipSlot part)
    {
        if (!equippedByPart.TryGetValue(part, out EquippedPart equipped))
            return;

        if (equipped.visualMode == Item.ClothingVisualMode.ReplaceSprite)
        {
            if (equipped.targetRenderer != null)
            {
                equipped.targetRenderer.sprite = equipped.originalSprite;
            }
        }
        else
        {
            if (equipped.overlayObject != null)
            {
                Destroy(equipped.overlayObject);
            }
        }

        equippedByPart.Remove(part);
    }

    private List<ResolvedPiece> ResolvePieces(Item item)
    {
        List<ResolvedPiece> pieces = new List<ResolvedPiece>();

        if (item != null && item.clothingPieces != null && item.clothingPieces.Length > 0)
        {
            foreach (var p in item.clothingPieces)
            {
                pieces.Add(new ResolvedPiece
                {
                    part = p.part,
                    visualMode = p.visualMode,
                    sprite = p.sprite,
                    localOffset = p.localOffset
                });
            }
            return pieces;
        }

        // Legacy fallback: single overlay.
        if (item != null && item.overlaySprite != null)
        {
            pieces.Add(new ResolvedPiece
            {
                part = item.clothingEquipSlot,
                visualMode = Item.ClothingVisualMode.Overlay,
                sprite = item.overlaySprite,
                localOffset = item.overlayOffset
            });
        }

        return pieces;
    }

    private void EnsureOverlayPoints()
    {
        if (character == null)
        {
            if (enableDebugLogs)
                Debug.LogWarning("[ClothingController] EnsureOverlayPoints skipped: character is NULL.", this);
            return;
        }

        Transform headTransform = character.head != null ? character.head.transform : null;
        Transform torsoTransform = character.torso != null ? character.torso.transform : null;

        Transform leftBicepTransform = character.leftArm != null ? character.leftArm.transform : null;
        Transform rightBicepTransform = character.rightArm != null ? character.rightArm.transform : null;
        Transform leftForearmTransform = character.leftForearm != null ? character.leftForearm.transform : null;
        Transform rightForearmTransform = character.rightForearm != null ? character.rightForearm.transform : null;
        Transform leftHandTransform = character.leftHand != null ? character.leftHand.transform : null;
        Transform rightHandTransform = character.rightHand != null ? character.rightHand.transform : null;

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[ClothingController] EnsureOverlayPoints head={(headTransform != null ? headTransform.name : "NULL")}, torso={(torsoTransform != null ? torsoTransform.name : "NULL")}, " +
                $"LBicep={(leftBicepTransform != null ? leftBicepTransform.name : "NULL")}, RBicep={(rightBicepTransform != null ? rightBicepTransform.name : "NULL")}, " +
                $"LForearm={(leftForearmTransform != null ? leftForearmTransform.name : "NULL")}, RForearm={(rightForearmTransform != null ? rightForearmTransform.name : "NULL")}, " +
                $"LHand={(leftHandTransform != null ? leftHandTransform.name : "NULL")}, RHand={(rightHandTransform != null ? rightHandTransform.name : "NULL")}",
                this
            );
        }

        // Head points (ensure hair is created before headwear)
        EnsurePoint(ref hairPoint, headTransform, "HairPoint");
        EnsurePoint(ref headwearPoint, headTransform, "HeadwearPoint");

        // Make sure hair is a lower sibling than headwear (helps render order with same sorting)
        if (hairPoint != null && headwearPoint != null && hairPoint.parent == headwearPoint.parent)
        {
            int hairIndex = hairPoint.GetSiblingIndex();
            int headwearIndex = headwearPoint.GetSiblingIndex();
            if (hairIndex > headwearIndex)
            {
                hairPoint.SetSiblingIndex(headwearIndex);
                headwearPoint.SetSiblingIndex(hairPoint.GetSiblingIndex() + 1);
            }
        }

        EnsurePoint(ref torsoPoint, torsoTransform, "TorsoClothingPoint");
        EnsurePoint(ref leftBicepPoint, leftBicepTransform, "LeftBicepClothingPoint");
        EnsurePoint(ref rightBicepPoint, rightBicepTransform, "RightBicepClothingPoint");
        EnsurePoint(ref leftForearmPoint, leftForearmTransform, "LeftForearmClothingPoint");
        EnsurePoint(ref rightForearmPoint, rightForearmTransform, "RightForearmClothingPoint");
        EnsurePoint(ref leftHandPoint, leftHandTransform, "LeftHandClothingPoint");
        EnsurePoint(ref rightHandPoint, rightHandTransform, "RightHandClothingPoint");
    }

    private static void EnsurePoint(ref Transform point, Transform parent, string pointName)
    {
        if (point != null)
            return;

        if (parent == null)
            return;

        Transform existing = parent.Find(pointName);
        if (existing != null)
        {
            point = existing;
            return;
        }

        GameObject go = new GameObject(pointName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        point = go.transform;
    }

    private Transform GetOverlayPoint(Item.ClothingEquipSlot slot)
    {
        switch (slot)
        {
            case Item.ClothingEquipSlot.Headwear: return headwearPoint;
            case Item.ClothingEquipSlot.Hair: return hairPoint;
            case Item.ClothingEquipSlot.Torso: return torsoPoint;
            case Item.ClothingEquipSlot.LeftBicep: return leftBicepPoint;
            case Item.ClothingEquipSlot.RightBicep: return rightBicepPoint;
            case Item.ClothingEquipSlot.LeftForearm: return leftForearmPoint;
            case Item.ClothingEquipSlot.RightForearm: return rightForearmPoint;
            case Item.ClothingEquipSlot.LeftHand: return leftHandPoint;
            case Item.ClothingEquipSlot.RightHand: return rightHandPoint;
            default: return null;
        }
    }

    private bool TryGetSpriteRendererForPart(Item.ClothingEquipSlot part, out SpriteRenderer renderer)
    {
        renderer = null;
        if (character == null)
            return false;

        switch (part)
        {
            case Item.ClothingEquipSlot.Headwear:
            case Item.ClothingEquipSlot.Hair:
                renderer = character.head != null ? character.head.limbSprite : null;
                if (renderer == null && character.head != null)
                    renderer = character.head.GetComponent<SpriteRenderer>();
                if (renderer == null && character.head != null)
                    renderer = character.head.GetComponentInChildren<SpriteRenderer>();
                break;

            case Item.ClothingEquipSlot.Torso:
                renderer = character.torso != null ? character.torso.GetComponent<SpriteRenderer>() : null;
                if (renderer == null && character.torso != null)
                    renderer = character.torso.GetComponentInChildren<SpriteRenderer>();
                break;

            case Item.ClothingEquipSlot.LeftBicep:
                renderer = character.leftArm != null ? character.leftArm.limbSprite : null;
                if (renderer == null && character.leftArm != null)
                    renderer = character.leftArm.GetComponent<SpriteRenderer>();
                if (renderer == null && character.leftArm != null)
                    renderer = character.leftArm.GetComponentInChildren<SpriteRenderer>();
                break;

            case Item.ClothingEquipSlot.RightBicep:
                renderer = character.rightArm != null ? character.rightArm.limbSprite : null;
                if (renderer == null && character.rightArm != null)
                    renderer = character.rightArm.GetComponent<SpriteRenderer>();
                if (renderer == null && character.rightArm != null)
                    renderer = character.rightArm.GetComponentInChildren<SpriteRenderer>();
                break;

            case Item.ClothingEquipSlot.LeftForearm:
                renderer = character.leftForearm != null ? character.leftForearm.limbSprite : null;
                if (renderer == null && character.leftForearm != null)
                    renderer = character.leftForearm.GetComponent<SpriteRenderer>();
                if (renderer == null && character.leftForearm != null)
                    renderer = character.leftForearm.GetComponentInChildren<SpriteRenderer>();
                break;

            case Item.ClothingEquipSlot.RightForearm:
                renderer = character.rightForearm != null ? character.rightForearm.limbSprite : null;
                if (renderer == null && character.rightForearm != null)
                    renderer = character.rightForearm.GetComponent<SpriteRenderer>();
                if (renderer == null && character.rightForearm != null)
                    renderer = character.rightForearm.GetComponentInChildren<SpriteRenderer>();
                break;

            case Item.ClothingEquipSlot.LeftHand:
                renderer = character.leftHand != null ? character.leftHand.limbSprite : null;
                if (renderer == null && character.leftHand != null)
                    renderer = character.leftHand.GetComponent<SpriteRenderer>();
                if (renderer == null && character.leftHand != null)
                    renderer = character.leftHand.GetComponentInChildren<SpriteRenderer>();
                break;

            case Item.ClothingEquipSlot.RightHand:
                renderer = character.rightHand != null ? character.rightHand.limbSprite : null;
                if (renderer == null && character.rightHand != null)
                    renderer = character.rightHand.GetComponent<SpriteRenderer>();
                if (renderer == null && character.rightHand != null)
                    renderer = character.rightHand.GetComponentInChildren<SpriteRenderer>();
                break;
        }

        return renderer != null;
    }

    private SpriteRenderer GetBaseSpriteRendererForOverlay(Item.ClothingEquipSlot slot)
    {
        if (TryGetSpriteRendererForPart(slot, out SpriteRenderer renderer))
            return renderer;
        return null;
    }

    private GameObject CreateOverlayObject(Item item, Item.ClothingEquipSlot slot, Sprite sprite, Vector2 localOffset, Transform parent)
    {
        GameObject overlayGO = new GameObject($"{item.itemName}_{slot}_Overlay");
        overlayGO.transform.SetParent(parent, false);
        overlayGO.transform.localPosition = new Vector3(localOffset.x, localOffset.y, 0f);
        overlayGO.transform.localRotation = Quaternion.identity;
        overlayGO.transform.localScale = Vector3.one;

        SpriteRenderer sr = overlayGO.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;

        SpriteRenderer baseRenderer = GetBaseSpriteRendererForOverlay(slot);
        if (baseRenderer != null)
        {
            sr.sortingLayerID = baseRenderer.sortingLayerID;
            int extra = (slot == Item.ClothingEquipSlot.Headwear) ? headwearAdditionalSortingOffset : 0;
            sr.sortingOrder = baseRenderer.sortingOrder + overlaySortingOrderOffset + extra;
        }
        else
        {
            // Fallback: still render, but rely on defaults.
            int extra = (slot == Item.ClothingEquipSlot.Headwear) ? headwearAdditionalSortingOffset : 0;
            sr.sortingOrder = overlaySortingOrderOffset + extra;
        }

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[ClothingController] CreateOverlayObject item='{item.itemName}', slot={slot}, parent='{(parent != null ? parent.name : "NULL")}', " +
                $"baseRenderer={(baseRenderer != null ? baseRenderer.name : "NULL")}, sortingLayerID={sr.sortingLayerID}, sortingOrder={sr.sortingOrder}",
                this
            );
        }

        return overlayGO;
    }
}

