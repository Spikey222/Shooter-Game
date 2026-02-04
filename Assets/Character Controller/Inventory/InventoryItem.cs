using UnityEngine;
using System;

/// <summary>
/// Serializable class representing an item stored in inventory.
/// Contains a reference to the Item ScriptableObject and its quantity.
/// </summary>
[Serializable]
public class InventoryItem
{
    [Tooltip("Reference to the item ScriptableObject")]
    public Item item;
    
    [Tooltip("Quantity of this item in the inventory")]
    public int quantity;
    
    /// <summary>
    /// Create a new InventoryItem
    /// </summary>
    public InventoryItem(Item item, int quantity = 1)
    {
        this.item = item;
        this.quantity = quantity;
    }
    
    /// <summary>
    /// Get the total weight of this inventory item
    /// </summary>
    public float GetTotalWeight()
    {
        if (item == null)
            return 0f;
        return item.GetWeight(quantity);
    }
    
    /// <summary>
    /// Check if this inventory item can stack more of the same item
    /// </summary>
    public bool CanStackMore(int additionalQuantity)
    {
        if (item == null || !item.IsStackable())
            return false;
        return (quantity + additionalQuantity) <= item.maxStackSize;
    }
    
    /// <summary>
    /// Try to add more quantity to this stack. Returns the amount actually added.
    /// </summary>
    public int TryAddQuantity(int amount)
    {
        if (!CanStackMore(amount))
        {
            int canAdd = Mathf.Max(0, item.maxStackSize - quantity);
            quantity += canAdd;
            return canAdd;
        }
        
        quantity += amount;
        return amount;
    }
    
    /// <summary>
    /// Remove quantity from this stack. Returns the amount actually removed.
    /// </summary>
    public int RemoveQuantity(int amount)
    {
        int removed = Mathf.Min(quantity, amount);
        quantity -= removed;
        return removed;
    }
    
    /// <summary>
    /// Check if this inventory item is empty
    /// </summary>
    public bool IsEmpty()
    {
        return quantity <= 0 || item == null;
    }
}
