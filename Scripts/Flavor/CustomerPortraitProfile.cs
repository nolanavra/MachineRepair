using System.Collections.Generic;
using UnityEngine;

namespace MachineRepair.Flavor
{
    [System.Serializable]
    public class CustomerPortraitProfile
    {
        [System.Serializable]
        public class TagWeight
        {
            public FlavorContextTag tag = FlavorContextTag.General;
            [Min(1)] public int weight = 1;
        }

        [Tooltip("Portrait sprite to use for this audience profile.")]
        public Sprite sprite;

        [Tooltip("Weighted audience tags represented by this portrait.")]
        public List<TagWeight> tags = new()
        {
            new TagWeight { tag = FlavorContextTag.General, weight = 1 }
        };
    }
}
