// AGENT NOTE: Dummy/testing data only; do not use this script for production behavior.
using System.Collections.Generic;
using MachineRepair;
using UnityEngine;

/// <summary>
/// Provides sample inventory stacks assembled from the current Inventory catalog.
/// </summary>
public static class TestingDummy
{
    /// <summary>
    /// Builds one stack per catalog entry with a random quantity between 1 and that entry's max stack.
    /// Returns empty when the catalog is missing or empty.
    /// </summary>
    public static List<Inventory.ItemStack> BuildSampleStacks(Inventory inventory)
    {
        if (inventory == null || inventory.inventoryCatalog == null || inventory.inventoryCatalog.Count == 0)
            return new List<Inventory.ItemStack>();

        List<Inventory.ItemStack> sampleStacks = new List<Inventory.ItemStack>(inventory.inventoryCatalog.Count);

        for (int i = 0; i < inventory.inventoryCatalog.Count; i++)
        {
            ThingDef def = inventory.inventoryCatalog[i];
            if (def == null || string.IsNullOrWhiteSpace(def.defName))
                continue;

            int maxStack = Mathf.Max(1, def.maxStack);
            int quantity = Random.Range(1, maxStack + 1);

            sampleStacks.Add(new Inventory.ItemStack
            {
                id = def.defName,
                quantity = quantity
            });
        }

        return sampleStacks;
    }
}
