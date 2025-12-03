using System.Collections.Generic;
using MachineRepair;
using UnityEngine;

public class DebugInventory : MonoBehaviour
{
    [SerializeField] private Inventory inventory;
    [SerializeField] private bool fillOnStart = true;
    [SerializeField, Min(1)] private int minQuantity = 1;
    [SerializeField, Min(1)] private int maxQuantity = 3;

    private void Awake()
    {
        inventory ??= GetComponent<Inventory>();
    }

    private void Start()
    {
        if (fillOnStart)
            PopulateRandomSlots();
    }

    [ContextMenu("Debug: Populate Random Inventory")]
    public void PopulateRandomSlots()
    {
        if (inventory == null)
        {
            Debug.LogWarning("DebugInventory: Missing Inventory reference.");
            return;
        }

        if (inventory.inventoryCatalog == null || inventory.inventoryCatalog.Count == 0)
        {
            Debug.LogWarning("DebugInventory: Inventory catalog is empty.");
            return;
        }

        ClearInventory();

        int min = Mathf.Max(1, minQuantity);
        int max = Mathf.Max(min, maxQuantity);

        for (int i = 0; i < inventory.slotCount; i++)
        {
            ThingDef def = inventory.inventoryCatalog[Random.Range(0, inventory.inventoryCatalog.Count)];
            int quantity = Random.Range(min, max + 1);
            inventory.AddItem(def.defName, quantity);
        }

        ShuffleSlots();
    }

    private void ClearInventory()
    {
        IReadOnlyList<Inventory.ItemStack> slots = inventory.GetSlots();
        HashSet<string> clearedIds = new HashSet<string>();

        for (int i = 0; i < slots.Count; i++)
        {
            var stack = slots[i];
            if (stack.IsEmpty || clearedIds.Contains(stack.id))
                continue;

            inventory.RemoveItem(stack.id, inventory.GetTotalCount(stack.id));
            clearedIds.Add(stack.id);
        }
    }

    private void ShuffleSlots()
    {
        for (int i = 0; i < inventory.slotCount; i++)
        {
            int swapIndex = Random.Range(i, inventory.slotCount);
            inventory.SwapSlots(i, swapIndex);
        }
    }
}
