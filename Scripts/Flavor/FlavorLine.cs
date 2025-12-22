
using System.Collections.Generic;
using UnityEngine;

namespace MachineRepair.Flavor
{
    public enum FlavorModeReq { Any, Inspect, Place }

    [CreateAssetMenu(menuName = "MachineRepair/Flavor/Line", fileName = "FlavorLine")]
    public class FlavorLine : ScriptableObject
    {
        [System.Serializable]
        public class Segment
        {
            [TextArea(2, 5)]
            [Tooltip("Segment template text; placeholders match the legacy template field.")]
            public string text = string.Empty;

            [Min(0f)]
            [Tooltip("Optional delay for this segment before advancing to the next.")]
            public float delaySeconds = 0f;
        }

        [System.Serializable]
        public class TaggedAudience
        {
            [Tooltip("Audience tag this line caters to.")]
            public FlavorContextTag tag;

            [Min(1)]
            [Tooltip("Optional weight to emphasize this tag when filtering lines by audience.")]
            public int weight = 1;
        }

        [TextArea(2, 5)]
        public string template = "Machine has {totalParts} parts now.";

        [Tooltip("Assign one or more audience tags to this line.")]
        public List<TaggedAudience> tags = new()
        {
            new TaggedAudience { tag = FlavorContextTag.General, weight = 1 }
        };

        [Tooltip("Optional ordered segments to play sequentially; falls back to the template when empty.")]
        public List<Segment> segments = new();

        [Min(0f)]
        [Tooltip("Default delay to use for segments when none is specified; 0 uses the chat service fallback.")]
        public float defaultSegmentDelaySeconds = 0f;

        [Min(0)] public int weight = 1;
        [Min(0)] public float minCooldownSeconds = 300f;

        public FlavorModeReq requireMode = FlavorModeReq.Any;
        [Min(0)] public int minTotalParts = 0;
        [Min(0)] public int minBoilers = 0;
        public bool requireWaterRouting = false;
        public bool requirePowerRouting = false;

        public bool oncePerSession = false;

        [Tooltip("Optional response set to present after this line plays.")]
        public FlavorResponseSet responses;
    }
}
