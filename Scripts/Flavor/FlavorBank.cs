
using System.Collections.Generic;
using UnityEngine;

namespace MachineRepair.Flavor
{
    [CreateAssetMenu(menuName = "MachineRepair/Flavor/Bank", fileName = "FlavorBank")]
    public class FlavorBank : ScriptableObject
    {
        public List<FlavorLine> lines = new();
    }
}
