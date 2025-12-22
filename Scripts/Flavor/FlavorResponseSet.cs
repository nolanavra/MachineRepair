using System.Collections.Generic;
using UnityEngine;

namespace MachineRepair.Flavor
{
    [CreateAssetMenu(menuName = "MachineRepair/Flavor/Response Set", fileName = "FlavorResponseSet")]
    public class FlavorResponseSet : ScriptableObject
    {
        [System.Serializable]
        public class ResponseTarget
        {
            [Tooltip("Line to enqueue when this option is chosen. Selection is weighted by tag/mood fit.")]
            public FlavorLine line;

            [Tooltip("Audience tags this target caters to.")]
            public List<FlavorContextTag> preferredTags = new() { FlavorContextTag.General };

            [Tooltip("Minimum mood (inclusive) for this target to be valid.")]
            [Range(0, 100)] public int minMood = 0;

            [Tooltip("Maximum mood (inclusive) for this target to be valid.")]
            [Range(0, 100)] public int maxMood = 100;

            [Tooltip("Base weight when picking among valid targets.")]
            [Min(1)] public int weight = 1;
        }

        [System.Serializable]
        public class ResponseOption
        {
            [Tooltip("Button label shown to the player.")]
            public string label = "Respond";

            [Tooltip("Mood delta applied to the active customer when selected.")]
            public int moodDelta = 0;

            [Tooltip("Optional follow-up targets selected using the customer's tags and mood.")]
            public List<ResponseTarget> followUps = new();
        }

        [Tooltip("Available responses to present for a line.")]
        public List<ResponseOption> options = new();
    }
}
