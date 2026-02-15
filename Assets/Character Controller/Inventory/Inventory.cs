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
        NormalizeStacks();
        currentWeight = CalculateWeight();
    }
    
    /// <summary>
    /// Split any stack that exceeds the item's maxStackSize into multiple stacks (e.g. serialized 17 with max 10 becomes 10 + 7).
    /// Applies to all item types (Consumable, Weapon, Clothing, Other) when maxStackSize > 1.
    /// Call on load so inspector-set or saved quantities respect stack limits.
    /// </summary>
    public void NormalizeStacks()
    {
        var rebuilt = new List<InventoryItem>();
        foreach (var inv in items)
        {
            if (inv == null || inv.IsEmpty() || inv.item == null) continue;
            if (!inv.item.IsStackable() || inv.quantity <= inv.item.maxStackSize)
            {
                rebuilt.Add(inv);
                continue;
            }
            int remaining = inv.quantity;
            int maxStack = inv.item.maxStackSize;
            while (remaining > 0)
            {
                int chunk = Mathf.Min(remaining, maxStack);
                rebuilt.Add(new InventoryItem(inv.item, chunk));
                remaining -= chunk;
            }
        }
        items.Clear();
        foreach (var inv in rebuilt)
            items.Add(inv);
        UpdateWeight();
        OnInventoryChanged?.Invoke();
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
    /// For stackable items (any type: Consumable, Weapon, Clothing, Other with maxStackSize > 1), fills existing stacks then creates new stacks up to maxStackSize each until quantity or weight limit is reached.
    /// </summary>
    public bool AddItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;
        
        if (item.IsStackable())
        {
            int remaining = quantity;
            while (remaining > 0)
            {
                if (currentWeight + item.GetWeight(1) > maxWeight)
                    break;
                
                InventoryItem stackWithSpace = FindStackWithSpace(item);
                int toAdd;
                if (stackWithSpace != null)
                {
                    int space = stackWithSpace.item.maxStackSize - stackWithSpace.quantity;
                    toAdd = Mathf.Min(remaining, space);
                }
                else
                {
                    toAdd = Mathf.Min(remaining, item.maxStackSize);
                }
                
                float addWeight = item.GetWeight(toAdd);
                if (currentWeight + addWeight > maxWeight)
                {
                    int fitByWeight = Mathf.Max(0, Mathf.FloorToInt((maxWeight - currentWeight) / item.weight));
                    if (fitByWeight <= 0) break;
                    toAdd = Mathf.Min(toAdd, fitByWeight);
                    addWeight = item.GetWeight(toAdd);
                }
                if (toAdd <= 0) break;
                
                if (stackWithSpace != null)
                    stackWithSpace.quantity += toAdd;
                else
                    items.Add(new InventoryItem(item, toAdd));
                remaining -= toAdd;
                UpdateWeight();
            }
            OnInventoryChanged?.Invoke();
            return remaining < quantity;
        }
        
        // Non-stackable: add one at a time
        float additionalWeight = item.GetWeight(quantity);
        if (currentWeight + additionalWeight > maxWeight)
        {
            int canFit = CalculateMaxQuantityThatFits(item, quantity);
            if (canFit <= 0) return false;
            quantity = canFit;
        }
        for (int i = 0; i < quantity; i++)
        {
            float singleWeight = item.GetWeight(1);
            if (currentWeight + singleWeight > maxWeight) break;
            items.Add(new InventoryItem(item, 1));
            currentWeight += singleWeight;
        }
        UpdateWeight();
        OnInventoryChanged?.Invoke();
        return true;
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
    /// Remove an item from inventory (from first stack, then next, etc.). Returns true if any was removed.
    /// </summary>
    public bool RemoveItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;
        int toRemove = quantity;
        bool anyRemoved = false;
        while (toRemove > 0)
        {
            InventoryItem invItem = FindItem(item);
            if (invItem == null) break;
            int removed = invItem.RemoveQuantity(toRemove);
            if (removed <= 0) break;
            toRemove -= removed;
            anyRemoved = true;
            if (invItem.IsEmpty())
                items.Remove(invItem);
        }
        if (anyRemoved)
        {
            UpdateWeight();
            OnInventoryChanged?.Invoke();
        }
        return anyRemoved;
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
    /// Find the first stack of this item in the inventory.
    /// </summary>
    public InventoryItem FindItem(Item item)
    {
        if (item == null)
            return null;
        return items.FirstOrDefault(invItem => invItem != null && invItem.item == item);
    }
    
    /// <summary>
    /// Find a stack of this item that has space for more (quantity &lt; maxStackSize).
    /// </summary>
    private InventoryItem FindStackWithSpace(Item item)
    {
        if (item == null || !item.IsStackable())
            return null;
        return items.FirstOrDefault(invItem => invItem != null && invItem.item == item && invItem.quantity < invItem.item.maxStackSize);
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
    /// Get total quantity of a specific item across all stacks.
    /// </summary>
    public int GetItemQuantity(Item item)
    {
        if (item == null) return 0;
        int total = 0;
        foreach (var invItem in items)
            if (invItem != null && invItem.item == item)
                total += invItem.quantity;
        return total;
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
