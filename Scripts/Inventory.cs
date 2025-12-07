using MachineRepair;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drop this on a GameObject (e.g. "GameController").
/// Fill the itemCatalog in the Inspector, set slotCount, and call AddItem/RemoveItem from other scripts.
/// </summary>
public class Inventory : MonoBehaviour
{
    // Optional global access
    public static Inventory Instance { get; private set; }
    private ThingDef thing;

    [Header("Item Catalog (definitions)")]
    [Tooltip("All item types the inventory can use. id must be unique.")]
    public List<ThingDef> inventoryCatalog = new List<ThingDef>();

    [Header("Inventory Settings")]
    [Min(1)]
    public int slotCount = 16;

    [Header("Testing")]
    [Tooltip("When enabled, seeds the inventory with dummy sample stacks on Awake for debugging.")]
    [SerializeField]
    private bool seedDummyInventoryOnAwake = false;

    [Header("Runtime Slots (read-only)")]
    [SerializeField]
    private List<ItemStack> slots = new List<ItemStack>();

    #region Nested Types


    [Serializable]
    public class ItemStack
    {
        public string id;
        public int quantity;

        public bool IsEmpty => string.IsNullOrEmpty(id) || quantity <= 0;

        public void Clear()
        {
            id = null;
            quantity = 0;
        }
    }

    #endregion

    private void Awake()
    {
        // Simple singleton (optional)
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        InitSlots();

        if (seedDummyInventoryOnAwake)
            SeedDummyInventory();
    }


    private void InitSlots()
    {
        slots ??= new List<ItemStack>();

        if (slots.Count != slotCount)
        {
            slots.Clear();
            for (int i = 0; i < slotCount; i++)
                slots.Add(new ItemStack());
        }
    }

    // --- Public API ---

    /// <summary>
    /// Try to add 'amount' of item with given id. Returns true if fully added, false if not enough space.
    /// </summary>
    public bool AddItem(string id, int amount = 1)
    {
        if (amount <= 0) return true;

        ThingDef def = GetDef(id);
        if (def == null)
        {
            Debug.LogWarning($"SimpleInventory: No ItemDef found for id '{id}'");
            return false;
        }

        int remaining = amount;

        // 1) Fill existing stacks of same item
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.IsEmpty || slot.id != id) continue;

            int space = def.maxStack - slot.quantity;
            if (space <= 0) continue;

            int toAdd = Mathf.Min(space, remaining);
            slot.quantity += toAdd;
            remaining -= toAdd;
            slots[i] = slot;

            if (remaining <= 0)
                return true;
        }

        // 2) Use empty slots
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (!slot.IsEmpty) continue;

            int toAdd = Mathf.Min(def.maxStack, remaining);
            slot.id = id;
            slot.quantity = toAdd;
            slots[i] = slot;
            remaining -= toAdd;

            if (remaining <= 0)
                return true;
        }

        // Not all items fit
        return false;
    }

    /// <summary>
    /// Try to remove 'amount' of item with given id. Returns true if fully removed, false if not enough present.
    /// </summary>
    public bool RemoveItem(string id, int amount = 1)
    {
        if (amount <= 0) return true;

        int total = GetTotalCount(id);
        if (total < amount)
            return false;

        int remaining = amount;

        // Remove from stacks
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.IsEmpty || slot.id != id) continue;

            int toRemove = Mathf.Min(slot.quantity, remaining);
            slot.quantity -= toRemove;
            remaining -= toRemove;

            if (slot.quantity <= 0)
                slot.Clear();

            slots[i] = slot;

            if (remaining <= 0)
                break;
        }

        return true;
    }

    /// Total quantity of a given item across all slots.
    public int GetTotalCount(string id)
    {
        int total = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (!slot.IsEmpty && slot.id == id)
                total += slot.quantity;
        }
        return total;
    }

    /// Read-only access to slots for UI, etc.
    public IReadOnlyList<ItemStack> GetSlots() => slots;

    /// <summary>
    /// Consume a single item from the given slot. Returns false if the slot is empty or invalid.
    /// </summary>
    public bool ConsumeFromSlot(int slotIndex, out string itemId)
    {
        itemId = null;
        if (slotIndex < 0 || slotIndex >= slots.Count) return false;

        var slot = slots[slotIndex];
        if (slot.IsEmpty) return false;

        itemId = slot.id;
        slot.quantity -= 1;
        if (slot.quantity <= 0)
            slot.Clear();

        slots[slotIndex] = slot;
        return true;
    }

    /// <summary>
    /// Swap two slot positions. Returns false if indices are invalid.
    /// </summary>
    public bool SwapSlots(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= slots.Count) return false;
        if (toIndex < 0 || toIndex >= slots.Count) return false;
        if (fromIndex == toIndex) return true;

        (slots[fromIndex], slots[toIndex]) = (slots[toIndex], slots[fromIndex]);
        return true;
    }

    /// Helper to look up an ItemDef by id.
    public ThingDef GetDef(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < inventoryCatalog.Count; i++)
        {
            if (inventoryCatalog[i].defName == id)
                return inventoryCatalog[i];
        }
        return null;
    }

    private void SeedDummyInventory()
    {
        List<ItemStack> sampleStacks = TestingDummy.BuildSampleStacks(this);
        if (sampleStacks == null || sampleStacks.Count == 0)
            return;

        for (int i = 0; i < slots.Count; i++)
            slots[i].Clear();

        for (int i = 0; i < sampleStacks.Count; i++)
        {
            ItemStack stack = sampleStacks[i];
            if (stack == null || stack.IsEmpty)
                continue;

            AddItem(stack.id, stack.quantity);
        }
    }

    #region Debug Helpers (optional)

    [ContextMenu("Debug: Print Inventory")]
    private void DebugPrint()
    {
        Debug.Log("Inventory contents:");
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s.IsEmpty)
                Debug.Log($"Slot {i}: (empty)");
            else
                Debug.Log($"Slot {i}: {s.id} x{s.quantity}");
        }
    }

    #endregion
}
