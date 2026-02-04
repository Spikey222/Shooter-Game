using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

/// <summary>
/// Inventory component that manages items for a character.
/// Uses a weight-based system where items have weight and characters have a maximum carrying capacity.
/// </summary>
public class Inventory : MonoBehaviour
{
    [Header("Weight Settings")]
    [Tooltip("Maximum weight capacity (displayed in pounds in UI)")]
    public float maxWeight = 50f;
    
    [Tooltip("Current weight of all items in inventory")]
    [SerializeField] private float currentWeight = 0f;
    
    [Header("Items")]
    [Tooltip("List of items in the inventory")]
    [SerializeField] private List<InventoryItem> items = new List<InventoryItem>();
    
    // Events
    public event Action OnInventoryChanged;
    public event Action<float, float> OnWeightChanged; // currentWeight, maxWeight
    
    // Properties
    public float CurrentWeight => currentWeight;
    public float MaxWeight => maxWeight;
    public int ItemCount => items.Count;
    public List<InventoryItem> Items => new List<InventoryItem>(items); // Return copy to prevent external modification
    
    private void Awake()
    {
        currentWeight = CalculateWeight();
    }
    
    /// <summary>
    /// Calculate the total weight of all items in inventory
    /// </summary>
    private float CalculateWeight()
    {
        float total = 0f;
        foreach (var invItem in items)
        {
            if (invItem != null && !invItem.IsEmpty())
            {
                total += invItem.GetTotalWeight();
            }
        }
        return total;
    }
    
    /// <summary>
    /// Update current weight and notify listeners
    /// </summary>
    private void UpdateWeight()
    {
        float oldWeight = currentWeight;
        currentWeight = CalculateWeight();
        
        if (Mathf.Abs(oldWeight - currentWeight) > 0.01f)
        {
            OnWeightChanged?.Invoke(currentWeight, maxWeight);
        }
    }
    
    /// <summary>
    /// Check if an item can be added to inventory (weight check)
    /// </summary>
    public bool CanAddItem(Item item, int quantity = 1)
    {
        if (item == null)
            return false;
        
        float additionalWeight = item.GetWeight(quantity);
        
        // Check if adding this item would exceed weight capacity
        if (currentWeight + additionalWeight > maxWeight)
        {
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Add an item to the inventory. Returns true if successfully added.
    /// </summary>
    public bool AddItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;
        
        // Check if we can add the full quantity
        float additionalWeight = item.GetWeight(quantity);
        if (currentWeight + additionalWeight > maxWeight)
        {
            // Try to add what we can fit
            int canFit = CalculateMaxQuantityThatFits(item, quantity);
            if (canFit <= 0)
            {
                return false;
            }
            quantity = canFit;
        }
        
        // Try to stack with existing item if stackable
        if (item.IsStackable())
        {
            InventoryItem existingItem = FindItem(item);
            if (existingItem != null)
            {
                int added = existingItem.TryAddQuantity(quantity);
                if (added > 0)
                {
                    UpdateWeight();
                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }
        }
        
        // Add as new item if not stackable or no existing stack found
        // For non-stackable items, add one at a time
        if (!item.IsStackable())
        {
            for (int i = 0; i < quantity; i++)
            {
                float singleWeight = item.GetWeight(1);
                if (currentWeight + singleWeight > maxWeight)
                {
                    break; // Can't fit more
                }
                
                items.Add(new InventoryItem(item, 1));
                currentWeight += singleWeight;
            }
            
            UpdateWeight();
            OnInventoryChanged?.Invoke();
            return quantity > 0;
        }
        else
        {
            // Stackable item - add all at once
            items.Add(new InventoryItem(item, quantity));
            UpdateWeight();
            OnInventoryChanged?.Invoke();
            return true;
        }
    }
    
    /// <summary>
    /// Calculate how many of an item can fit in inventory
    /// </summary>
    private int CalculateMaxQuantityThatFits(Item item, int desiredQuantity)
    {
        float availableWeight = maxWeight - currentWeight;
        if (availableWeight <= 0f)
            return 0;
        
        float weightPerItem = item.weight;
        int maxByWeight = Mathf.FloorToInt(availableWeight / weightPerItem);
        
        // If stackable, check if there's an existing stack
        if (item.IsStackable())
        {
            InventoryItem existingItem = FindItem(item);
            if (existingItem != null)
            {
                int spaceInStack = item.maxStackSize - existingItem.quantity;
                return Mathf.Min(desiredQuantity, maxByWeight, spaceInStack);
            }
        }
        
        return Mathf.Min(desiredQuantity, maxByWeight);
    }
    
    /// <summary>
    /// Remove an item from inventory. Returns true if successfully removed.
    /// </summary>
    public bool RemoveItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;
        
        InventoryItem invItem = FindItem(item);
        if (invItem == null)
            return false;
        
        int removed = invItem.RemoveQuantity(quantity);
        
        // Remove from list if empty
        if (invItem.IsEmpty())
        {
            items.Remove(invItem);
        }
        
        if (removed > 0)
        {
            UpdateWeight();
            OnInventoryChanged?.Invoke();
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Remove item at a specific index
    /// </summary>
    public bool RemoveItemAt(int index, int quantity = 1)
    {
        if (index < 0 || index >= items.Count)
            return false;
        
        InventoryItem invItem = items[index];
        if (invItem == null || invItem.IsEmpty())
            return false;
        
        int removed = invItem.RemoveQuantity(quantity);
        
        if (invItem.IsEmpty())
        {
            items.RemoveAt(index);
        }
        
        if (removed > 0)
        {
            UpdateWeight();
            OnInventoryChanged?.Invoke();
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Find an item in the inventory by Item reference
    /// </summary>
    public InventoryItem FindItem(Item item)
    {
        if (item == null)
            return null;
        
        return items.FirstOrDefault(invItem => invItem != null && invItem.item == item);
    }
    
    /// <summary>
    /// Get item at specific index
    /// </summary>
    public InventoryItem GetItemAt(int index)
    {
        if (index < 0 || index >= items.Count)
            return null;
        return items[index];
    }
    
    /// <summary>
    /// Get quantity of a specific item in inventory
    /// </summary>
    public int GetItemQuantity(Item item)
    {
        InventoryItem invItem = FindItem(item);
        return invItem != null ? invItem.quantity : 0;
    }
    
    /// <summary>
    /// Check if inventory has a specific item
    /// </summary>
    public bool HasItem(Item item, int minimumQuantity = 1)
    {
        return GetItemQuantity(item) >= minimumQuantity;
    }
    
    /// <summary>
    /// Clear all items from inventory
    /// </summary>
    public void Clear()
    {
        items.Clear();
        UpdateWeight();
        OnInventoryChanged?.Invoke();
    }
    
    /// <summary>
    /// Get the weight percentage (0-1) of inventory capacity used
    /// </summary>
    public float GetWeightPercentage()
    {
        if (maxWeight <= 0f)
            return 0f;
        return Mathf.Clamp01(currentWeight / maxWeight);
    }
}
