
using UnityEngine;

namespace MachineRepair.Flavor
{
    public enum FlavorModeReq { Any, Inspect, Place }

    [CreateAssetMenu(menuName = "MachineRepair/Flavor/Line", fileName = "FlavorLine")]
    public class FlavorLine : ScriptableObject
    {
        [TextArea(2, 5)]
        public string template = "Machine has {totalParts} parts now.";
        public string tag;
        [Min(0)] public int weight = 1;
        [Min(0)] public float minCooldownSeconds = 300f;

        public FlavorModeReq requireMode = FlavorModeReq.Any;
        [Min(0)] public int minTotalParts = 0;
        [Min(0)] public int minBoilers = 0;
        public bool requireWaterRouting = false;
        public bool requirePowerRouting = false;

        public bool oncePerSession = false;
    }
}
